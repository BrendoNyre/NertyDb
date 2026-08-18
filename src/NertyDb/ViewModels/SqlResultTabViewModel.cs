using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class SqlResultTabViewModel : ObservableObject, IDisposable
    {
        private readonly ConnectionProfile _connection;
        private readonly string _database;
        private readonly IDbDriver _driver;
        private readonly ExportService _exportService;
        private readonly Action<PendingChangesViewModel> _openPendingChangesDialog;
        private readonly Action<ExportViewModel> _openExportDialog;

        private DataTable _data = new();
        private DataView _filteredView;
        private string _quickFilterText = string.Empty;
        private string _title = "Resultado";
        private string _schema = "dbo";
        private string _tableName = string.Empty;

        // Row change tracking
        private readonly Dictionary<int, Dictionary<string, object?>> _originalRowValues = new();
        public Dictionary<int, HashSet<string>> ModifiedCellsByRow { get; } = new();
        public HashSet<int> InsertedRowIndices { get; } = new();
        public HashSet<int> DeletedRowIndices { get; } = new();
        public ObservableCollection<PendingChange> PendingChanges { get; } = new();
        public List<string> PrimaryKeyColumns { get; set; } = new();

        public event EventHandler? VisualChangesUpdated;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Schema
        {
            get => _schema;
            set => SetProperty(ref _schema, value);
        }

        public string TableName
        {
            get => _tableName;
            set
            {
                if (SetProperty(ref _tableName, value))
                {
                    OnPropertyChanged(nameof(SourceTableSummary));
                    OnPropertyChanged(nameof(HasIdentifiedTable));
                }
            }
        }

        public bool HasIdentifiedTable => !string.IsNullOrEmpty(TableName);
        public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;
        public string SourceTableSummary => HasIdentifiedTable ? $"{Schema}.{TableName}" : "(Resultado de Consulta)";

        public DataTable Data
        {
            get => _data;
            set
            {
                if (SetProperty(ref _data, value))
                {
                    FilteredView = _data.DefaultView;
                    InitializeRowTracking();
                    OnPropertyChanged(nameof(RowCount));
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public DataView FilteredView
        {
            get => _filteredView;
            private set => SetProperty(ref _filteredView, value);
        }

        public string QuickFilterText
        {
            get => _quickFilterText;
            set
            {
                if (SetProperty(ref _quickFilterText, value))
                {
                    ApplyQuickFilter();
                }
            }
        }

        public int RowCount => Data.Rows.Count;
        public string Summary => $"{Data.Rows.Count:N0} linha(s), {Data.Columns.Count} coluna(s)";

        public int TotalPendingCount => PendingChanges.Count;
        public bool HasPendingChanges => TotalPendingCount > 0;
        public string PendingBadgeText => $"{TotalPendingCount} alteração(ões)";

        public ICommand AddRowCommand { get; }
        public ICommand DuplicateRowCommand { get; }
        public ICommand DeleteRowCommand { get; }
        public ICommand DiscardChangesCommand { get; }
        public ICommand CommitChangesCommand { get; }
        public ICommand ExportCommand { get; }

        public SqlResultTabViewModel(
            DataTable data,
            string title,
            ConnectionProfile connection,
            string database,
            IDbDriver driver,
            ExportService exportService,
            Action<PendingChangesViewModel> openPendingChangesDialog,
            Action<ExportViewModel> openExportDialog,
            string? sourceTable = null,
            string? sourceSchema = null)
        {
            _connection = connection;
            _database = database;
            _driver = driver;
            _exportService = exportService;
            _openPendingChangesDialog = openPendingChangesDialog;
            _openExportDialog = openExportDialog;
            _title = title;
            _tableName = sourceTable ?? string.Empty;
            _schema = sourceSchema ?? "dbo";

            // Unlock all columns so editing is enabled
            foreach (DataColumn col in data.Columns)
            {
                col.ReadOnly = false;
            }

            _data = data;
            _filteredView = _data.DefaultView;

            ResolveTableAndPkInfo();
            InitializeRowTracking();

            AddRowCommand = new RelayCommand(ExecuteAddNewRow);
            DuplicateRowCommand = new RelayCommand(ExecuteDuplicateRow);
            DeleteRowCommand = new RelayCommand(ExecuteDeleteRow);
            DiscardChangesCommand = new RelayCommand(ExecuteDiscardChanges, () => HasPendingChanges);
            CommitChangesCommand = new RelayCommand(ExecuteCommitChanges, () => HasPendingChanges);
            ExportCommand = new RelayCommand(ExecuteExport);
        }

        private void ResolveTableAndPkInfo()
        {
            if (string.IsNullOrEmpty(TableName)) return;

            var cache = MetadataCacheService.Instance;
            var details = cache.GetCachedTableDetails(_connection.Id, _database, Schema, TableName);
            if (details != null && details.PrimaryKeyColumns.Count > 0)
            {
                PrimaryKeyColumns = new List<string>(details.PrimaryKeyColumns);
            }
            else
            {
                // Inspect columns in Data to guess PK if column names match Senior standard
                var pkCandidates = new[] { "NUMEMP", "CODFIL", "NUMCAD", "DATACC", "HORACC", "CODUSU", "ID", "CODIGO" };
                foreach (var c in pkCandidates)
                {
                    if (Data.Columns.Contains(c) && !PrimaryKeyColumns.Contains(c))
                    {
                        PrimaryKeyColumns.Add(c);
                    }
                }
            }
        }

        private void InitializeRowTracking()
        {
            _originalRowValues.Clear();
            ModifiedCellsByRow.Clear();
            InsertedRowIndices.Clear();
            DeletedRowIndices.Clear();
            PendingChanges.Clear();

            for (int r = 0; r < Data.Rows.Count; r++)
            {
                var row = Data.Rows[r];
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn col in Data.Columns)
                {
                    dict[col.ColumnName] = row[col];
                }
                _originalRowValues[r] = dict;
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyQuickFilter()
        {
            if (Data == null || Data.Columns.Count == 0) return;

            if (string.IsNullOrWhiteSpace(QuickFilterText))
            {
                FilteredView.RowFilter = string.Empty;
                return;
            }

            var terms = QuickFilterText.Trim().Replace("'", "''");
            var filters = new List<string>();

            foreach (DataColumn col in Data.Columns)
            {
                if (col.DataType == typeof(string))
                {
                    filters.Add($"[{col.ColumnName}] LIKE '%{terms}%'");
                }
                else
                {
                    filters.Add($"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{terms}%'");
                }
            }

            if (filters.Count > 0)
            {
                FilteredView.RowFilter = string.Join(" OR ", filters);
            }
        }

        public void OnCellEdited(DataRowView rowView, string columnName, object? newValue)
        {
            if (rowView == null) return;
            var row = rowView.Row;
            var rowIndex = Data.Rows.IndexOf(row);
            if (rowIndex < 0) return;

            // Check if this is an inserted row
            if (InsertedRowIndices.Contains(rowIndex))
            {
                var insertChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Insert && c.RowIndex == rowIndex);
                if (insertChange != null)
                {
                    insertChange.NewValues[columnName] = newValue == DBNull.Value ? null : newValue;
                }
                UpdatePendingCounts();
                VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_originalRowValues.TryGetValue(rowIndex, out var origDict))
            {
                origDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn c in Data.Columns)
                {
                    origDict[c.ColumnName] = row[c];
                }
                _originalRowValues[rowIndex] = origDict;
            }

            origDict.TryGetValue(columnName, out var originalValue);
            bool isDifferent = !AreValuesEqual(originalValue, newValue);

            if (!ModifiedCellsByRow.TryGetValue(rowIndex, out var modCols))
            {
                modCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ModifiedCellsByRow[rowIndex] = modCols;
            }

            if (isDifferent)
            {
                modCols.Add(columnName);
            }
            else
            {
                modCols.Remove(columnName);
            }

            var existingChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Update && c.RowIndex == rowIndex);

            if (modCols.Count > 0)
            {
                if (existingChange == null)
                {
                    existingChange = new PendingChange
                    {
                        Type = ChangeType.Update,
                        Schema = Schema,
                        TableName = string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                        RowIndex = rowIndex,
                        OriginalValues = new Dictionary<string, object?>(origDict),
                        PrimaryKeyValues = ExtractPrimaryKeyValues(row, origDict)
                    };
                    PendingChanges.Add(existingChange);
                }

                existingChange.ModifiedColumns = new HashSet<string>(modCols, StringComparer.OrdinalIgnoreCase);
                existingChange.NewValues[columnName] = newValue == DBNull.Value ? null : newValue;
            }
            else if (existingChange != null)
            {
                PendingChanges.Remove(existingChange);
                ModifiedCellsByRow.Remove(rowIndex);
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        private bool AreValuesEqual(object? v1, object? v2)
        {
            if ((v1 == null || v1 == DBNull.Value) && (v2 == null || v2 == DBNull.Value)) return true;
            if ((v1 == null || v1 == DBNull.Value) || (v2 == null || v2 == DBNull.Value)) return false;
            return Equals(v1, v2) || string.Equals(v1.ToString()?.Trim(), v2.ToString()?.Trim(), StringComparison.Ordinal);
        }

        private Dictionary<string, object?> ExtractPrimaryKeyValues(DataRow row, Dictionary<string, object?> origDict)
        {
            var pks = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (PrimaryKeyColumns.Count > 0)
            {
                foreach (var pk in PrimaryKeyColumns)
                {
                    if (origDict.TryGetValue(pk, out var val))
                    {
                        pks[pk] = val == DBNull.Value ? null : val;
                    }
                    else if (Data.Columns.Contains(pk))
                    {
                        var v = row[pk];
                        pks[pk] = v == DBNull.Value ? null : v;
                    }
                }
            }
            else
            {
                // Use all columns in WHERE if no PK
                foreach (var kvp in origDict)
                {
                    pks[kvp.Key] = kvp.Value == DBNull.Value ? null : kvp.Value;
                }
            }
            return pks;
        }

        public void ExecuteAddNewRow(object? _ = null)
        {
            var newRow = Data.NewRow();
            foreach (DataColumn col in Data.Columns)
            {
                if (col.AutoIncrement) continue;
                newRow[col] = DBNull.Value;
            }

            Data.Rows.Add(newRow);
            var rowIndex = Data.Rows.Count - 1;
            InsertedRowIndices.Add(rowIndex);

            var change = new PendingChange
            {
                Type = ChangeType.Insert,
                Schema = Schema,
                TableName = string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                RowIndex = rowIndex,
                NewValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (DataColumn col in Data.Columns)
            {
                change.NewValues[col.ColumnName] = null;
            }

            PendingChanges.Add(change);
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
        }

        public void ExecuteDuplicateRow(object? parameter)
        {
            DataRow? sourceRow = null;
            if (parameter is DataRowView rowView)
            {
                sourceRow = rowView.Row;
            }
            else if (parameter is System.Collections.IList list && list.Count > 0 && list[0] is DataRowView firstView)
            {
                sourceRow = firstView.Row;
            }

            if (sourceRow == null && Data.Rows.Count > 0)
            {
                sourceRow = Data.Rows[Data.Rows.Count - 1];
            }

            if (sourceRow == null) return;

            var newRow = Data.NewRow();
            var change = new PendingChange
            {
                Type = ChangeType.Insert,
                Schema = Schema,
                TableName = string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                NewValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (DataColumn col in Data.Columns)
            {
                if (col.AutoIncrement)
                {
                    newRow[col] = DBNull.Value;
                    change.NewValues[col.ColumnName] = null;
                }
                else
                {
                    var val = sourceRow[col];
                    newRow[col] = val;
                    change.NewValues[col.ColumnName] = val == DBNull.Value ? null : val;
                }
            }

            Data.Rows.Add(newRow);
            var rowIndex = Data.Rows.Count - 1;
            change.RowIndex = rowIndex;
            InsertedRowIndices.Add(rowIndex);

            PendingChanges.Add(change);
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
        }

        public void ExecuteDeleteRow(object? parameter)
        {
            if (parameter is DataRowView rowView)
            {
                DeleteRow(rowView.Row);
            }
            else if (parameter is System.Collections.IList list)
            {
                var rowViews = list.OfType<DataRowView>().ToList();
                foreach (var rv in rowViews)
                {
                    DeleteRow(rv.Row);
                }
            }
        }

        private void DeleteRow(DataRow row)
        {
            var rowIndex = Data.Rows.IndexOf(row);
            if (rowIndex < 0) return;

            if (InsertedRowIndices.Contains(rowIndex))
            {
                InsertedRowIndices.Remove(rowIndex);
                var insChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Insert && c.RowIndex == rowIndex);
                if (insChange != null) PendingChanges.Remove(insChange);
                Data.Rows.Remove(row);
                UpdatePendingCounts();
                VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(RowCount));
                OnPropertyChanged(nameof(Summary));
                return;
            }

            if (DeletedRowIndices.Contains(rowIndex))
            {
                DeletedRowIndices.Remove(rowIndex);
                var delChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Delete && c.RowIndex == rowIndex);
                if (delChange != null) PendingChanges.Remove(delChange);
            }
            else
            {
                DeletedRowIndices.Add(rowIndex);
                var updChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Update && c.RowIndex == rowIndex);
                if (updChange != null) PendingChanges.Remove(updChange);
                ModifiedCellsByRow.Remove(rowIndex);

                if (!_originalRowValues.TryGetValue(rowIndex, out var origDict))
                {
                    origDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataColumn c in Data.Columns)
                    {
                        origDict[c.ColumnName] = row[c];
                    }
                }

                var delChange = new PendingChange
                {
                    Type = ChangeType.Delete,
                    Schema = Schema,
                    TableName = string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                    RowIndex = rowIndex,
                    OriginalValues = origDict,
                    PrimaryKeyValues = ExtractPrimaryKeyValues(row, origDict)
                };
                PendingChanges.Add(delChange);
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void DiscardRow(DataRow row)
        {
            var rowIndex = Data.Rows.IndexOf(row);
            if (rowIndex < 0) return;

            if (InsertedRowIndices.Contains(rowIndex))
            {
                InsertedRowIndices.Remove(rowIndex);
                var insChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Insert && c.RowIndex == rowIndex);
                if (insChange != null) PendingChanges.Remove(insChange);
                Data.Rows.Remove(row);
            }
            else
            {
                DeletedRowIndices.Remove(rowIndex);
                ModifiedCellsByRow.Remove(rowIndex);
                var changes = PendingChanges.Where(c => c.RowIndex == rowIndex).ToList();
                foreach (var c in changes) PendingChanges.Remove(c);

                if (_originalRowValues.TryGetValue(rowIndex, out var origDict))
                {
                    foreach (var kvp in origDict)
                    {
                        if (Data.Columns.Contains(kvp.Key))
                        {
                            row[kvp.Key] = kvp.Value ?? DBNull.Value;
                        }
                    }
                }
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
        }

        private void ExecuteDiscardChanges()
        {
            // Restore all rows from original values
            var insertedRows = InsertedRowIndices.OrderByDescending(i => i).ToList();
            foreach (var idx in insertedRows)
            {
                if (idx < Data.Rows.Count) Data.Rows.RemoveAt(idx);
            }

            InsertedRowIndices.Clear();
            DeletedRowIndices.Clear();
            ModifiedCellsByRow.Clear();
            PendingChanges.Clear();

            for (int r = 0; r < Data.Rows.Count; r++)
            {
                if (_originalRowValues.TryGetValue(r, out var origDict))
                {
                    var row = Data.Rows[r];
                    foreach (var kvp in origDict)
                    {
                        if (Data.Columns.Contains(kvp.Key))
                        {
                            row[kvp.Key] = kvp.Value ?? DBNull.Value;
                        }
                    }
                }
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
        }

        private void ExecuteCommitChanges()
        {
            if (PendingChanges.Count == 0) return;

            var pendingVm = new PendingChangesViewModel(
                _connection,
                _database,
                Schema,
                string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                PendingChanges.ToList(),
                _driver,
                onSuccess: () =>
                {
                    // Re-initialize tracking with current data as clean baseline
                    InitializeRowTracking();
                    return Task.CompletedTask;
                });

            _openPendingChangesDialog(pendingVm);
        }

        private void ExecuteExport()
        {
            var exportVm = new ExportViewModel(
                Data,
                $"{Title}_{DateTime.Now:yyyyMMdd_HHmmss}",
                _exportService);

            _openExportDialog(exportVm);
        }

        private void UpdatePendingCounts()
        {
            OnPropertyChanged(nameof(TotalPendingCount));
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(PendingBadgeText));
            (CommitChangesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DiscardChangesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            try
            {
                _originalRowValues.Clear();
                ModifiedCellsByRow.Clear();
                InsertedRowIndices.Clear();
                DeletedRowIndices.Clear();
                PendingChanges.Clear();

                _data?.Clear();
                _data?.Dispose();
            }
            catch { }
        }
    }
}
