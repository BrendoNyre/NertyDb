using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class TableDataViewModel : ObservableObject, IDisposable
    {
        private readonly IDbDriver _driver;
        private readonly ExportService _exportService;
        private readonly Action<PendingChangesViewModel> _openPendingChangesDialog;
        private readonly Action<ExportViewModel> _openExportDialog;

        private DataTable _tableData = new();
        private DataView _filteredView;
        private bool _isLoading;
        private string _statusText = "Pronto";
        private int _pageNumber = 1;
        private int _pageSize = 200;
        private long _totalRows;
        private string? _sortColumn;
        private bool _sortAscending = true;
        private string _quickFilterText = string.Empty;
        private CancellationTokenSource? _cts;
        private bool _autoCommit = true;

        public SelectionStatsViewModel SelectionStats { get; } = new();
        public Dictionary<string, string> ColumnDescriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Visual tracking for DataGrid styling
        private readonly Dictionary<int, Dictionary<string, object?>> _originalRowValues = new();
        public Dictionary<int, HashSet<string>> ModifiedCellsByRow { get; } = new();
        public HashSet<int> InsertedRowIndices { get; } = new();
        public HashSet<int> DeletedRowIndices { get; } = new();

        public ConnectionProfile Connection { get; }
        public string Database { get; }
        public string Schema { get; }
        public string TableName { get; }
        public bool IsView { get; }
        public string Title => $"{Schema}.{TableName}";

        public ObservableCollection<PendingChange> PendingChanges { get; } = new();
        public List<string> PrimaryKeyColumns { get; private set; } = new();

        public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;
        public bool IsReadOnly => IsView || !HasPrimaryKey;

        public bool AutoCommit
        {
            get => _autoCommit;
            set
            {
                if (SetProperty(ref _autoCommit, value))
                {
                    OnPropertyChanged(nameof(AutoCommitStatusText));
                }
            }
        }

        public string AutoCommitStatusText => AutoCommit ? "Auto-Commit: Ligado" : "Auto-Commit: Desligado (Manual)";

        public DataTable TableData
        {
            get => _tableData;
            private set
            {
                if (SetProperty(ref _tableData, value))
                {
                    FilteredView = _tableData.DefaultView;
                }
            }
        }

        public DataView FilteredView
        {
            get => _filteredView;
            private set => SetProperty(ref _filteredView, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public int PageNumber
        {
            get => _pageNumber;
            set
            {
                if (SetProperty(ref _pageNumber, Math.Max(1, value)))
                {
                    _ = LoadDataAsync();
                }
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    _pageNumber = 1;
                    OnPropertyChanged(nameof(PageNumber));
                    _ = LoadDataAsync();
                }
            }
        }

        public long TotalRows
        {
            get => _totalRows;
            private set
            {
                if (SetProperty(ref _totalRows, value))
                {
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(PaginationSummary));
                }
            }
        }

        public int TotalPages => PageSize > 0 && TotalRows > 0 ? (int)Math.Ceiling((double)TotalRows / PageSize) : 1;

        public string PaginationSummary
        {
            get
            {
                if (PageSize <= 0) return $"{TableData.Rows.Count:N0} linhas (Tudo)";
                var start = (PageNumber - 1) * PageSize + 1;
                var end = Math.Min(PageNumber * PageSize, TotalRows);
                return TotalRows > 0 
                    ? $"Exibindo {start:N0} - {end:N0} de {TotalRows:N0} linhas (Pág. {PageNumber}/{TotalPages})"
                    : $"{TableData.Rows.Count:N0} linhas";
            }
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

        public int TotalPendingCount => PendingChanges.Count;
        public bool HasPendingChanges => TotalPendingCount > 0;
        public string PendingBadgeText => $"{TotalPendingCount} alteração(ões) pendente(s)";

        public ICommand RefreshCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand AddRowCommand { get; }
        public ICommand DuplicateRowCommand { get; }
        public ICommand DeleteRowCommand { get; }
        public ICommand DiscardChangesCommand { get; }
        public ICommand CommitChangesCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CancelQueryCommand { get; }

        public event EventHandler? DataReloaded;
        public event EventHandler? VisualChangesUpdated;

        public TableDataViewModel(
            ConnectionProfile connection,
            string database,
            string schema,
            string tableName,
            bool isView,
            IDbDriver driver,
            ExportService exportService,
            Action<PendingChangesViewModel> openPendingChangesDialog,
            Action<ExportViewModel> openExportDialog)
        {
            Connection = connection;
            Database = database;
            Schema = schema;
            TableName = tableName;
            IsView = isView;
            _driver = driver;
            _exportService = exportService;
            _openPendingChangesDialog = openPendingChangesDialog;
            _openExportDialog = openExportDialog;

            _filteredView = _tableData.DefaultView;

            RefreshCommand = new AsyncRelayCommand(async () => await LoadDataAsync());
            
            FirstPageCommand = new RelayCommand(() => PageNumber = 1, () => PageNumber > 1 && !IsLoading);
            PrevPageCommand = new RelayCommand(() => PageNumber = Math.Max(1, PageNumber - 1), () => PageNumber > 1 && !IsLoading);
            NextPageCommand = new RelayCommand(() => PageNumber = Math.Min(TotalPages, PageNumber + 1), () => PageNumber < TotalPages && !IsLoading);
            LastPageCommand = new RelayCommand(() => PageNumber = TotalPages, () => PageNumber < TotalPages && !IsLoading);

            AddRowCommand = new RelayCommand(ExecuteAddNewRow, _ => !IsReadOnly && !IsLoading);
            DuplicateRowCommand = new RelayCommand(ExecuteDuplicateRow, _ => !IsReadOnly && !IsLoading);
            DeleteRowCommand = new RelayCommand(ExecuteDeleteRow, _ => !IsReadOnly && !IsLoading);
            DiscardChangesCommand = new RelayCommand(ExecuteDiscardChanges, () => HasPendingChanges && !IsLoading);
            CommitChangesCommand = new RelayCommand(ExecuteCommitChanges, () => HasPendingChanges && !IsLoading);
            ExportCommand = new RelayCommand(ExecuteExport, () => TableData.Rows.Count > 0 && !IsLoading);
            LoadMoreRowsCommand = new RelayCommand(() => PageNumber++, () => PageNumber < TotalPages && !IsLoading);

            CancelQueryCommand = new RelayCommand(() =>
            {
                _cts?.Cancel();
                StatusText = "Cancelamento solicitado...";
            }, () => IsLoading);
        }

        public ICommand LoadMoreRowsCommand { get; }

        public async Task LoadDataAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            IsLoading = true;
            StatusText = $"Consultando {Schema}.{TableName}...";

            try
            {
                var result = await _driver.FetchTableDataAsync(
                    Connection,
                    Database,
                    Schema,
                    TableName,
                    PageNumber,
                    PageSize,
                    _sortColumn,
                    _sortAscending,
                    filterExpression: null,
                    cancellationToken: _cts.Token);

                if (result.HasError)
                {
                    StatusText = $"Erro: {result.ErrorMessage}";
                    return;
                }

                PrimaryKeyColumns = result.PrimaryKeyColumns;
                OnPropertyChanged(nameof(HasPrimaryKey));
                OnPropertyChanged(nameof(IsReadOnly));

                TotalRows = result.TotalRowCount;

                // Load Column Descriptions for Tooltips
                try
                {
                    var details = await MetadataCacheService.Instance.GetTableDetailsAsync(Connection, Database, Schema, TableName, _driver);
                    ColumnDescriptions.Clear();
                    foreach (var c in details.Columns)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Description))
                        {
                            ColumnDescriptions[c.Name] = c.Description;
                        }
                    }
                }
                catch { }
                
                // Unlock DataColumns so WPF DataGrid allows editing
                foreach (DataColumn col in result.Data.Columns)
                {
                    col.ReadOnly = false;
                }

                TableData = result.Data;

                // Cache original values for rollback and diff checking
                _originalRowValues.Clear();
                ModifiedCellsByRow.Clear();
                InsertedRowIndices.Clear();
                DeletedRowIndices.Clear();
                PendingChanges.Clear();

                for (int r = 0; r < TableData.Rows.Count; r++)
                {
                    var row = TableData.Rows[r];
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataColumn col in TableData.Columns)
                    {
                        dict[col.ColumnName] = row[col];
                    }
                    _originalRowValues[r] = dict;
                }

                UpdatePendingCounts();
                ApplyQuickFilter();
                StatusText = $"Carregado em {result.DurationMs} ms. ({PaginationSummary})";
                DataReloaded?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Consulta cancelada pelo usuário.";
            }
            catch (Exception ex)
            {
                StatusText = $"Falha ao carregar dados: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void SortByColumn(string columnName)
        {
            if (_sortColumn == columnName)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = columnName;
                _sortAscending = true;
            }
            _ = LoadDataAsync();
        }

        private void ApplyQuickFilter()
        {
            if (TableData == null || TableData.Columns.Count == 0) return;

            if (string.IsNullOrWhiteSpace(QuickFilterText))
            {
                FilteredView.RowFilter = string.Empty;
                return;
            }

            var terms = QuickFilterText.Trim().Replace("'", "''");
            var filters = new List<string>();

            foreach (DataColumn col in TableData.Columns)
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
            if (IsReadOnly || rowView == null) return;
            var row = rowView.Row;
            var rowIndex = TableData.Rows.IndexOf(row);
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

            // Original value
            if (!_originalRowValues.TryGetValue(rowIndex, out var origDict))
            {
                origDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn c in TableData.Columns)
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

            // Update or remove PendingChange for this row
            var existingChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Update && c.RowIndex == rowIndex);

            if (modCols.Count > 0)
            {
                if (existingChange == null)
                {
                    existingChange = new PendingChange
                    {
                        Type = ChangeType.Update,
                        Schema = Schema,
                        TableName = TableName,
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
                    else if (TableData.Columns.Contains(pk))
                    {
                        var v = row[pk];
                        pks[pk] = v == DBNull.Value ? null : v;
                    }
                }
            }
            return pks;
        }

        public void ExecuteAddNewRow(object? _ = null)
        {
            if (IsReadOnly) return;

            foreach (DataColumn col in TableData.Columns)
            {
                col.ReadOnly = false;
                col.AllowDBNull = true;
            }

            var newRow = TableData.NewRow();
            foreach (DataColumn col in TableData.Columns)
            {
                if (col.AutoIncrement) continue;
                newRow[col] = DBNull.Value;
            }

            TableData.Rows.Add(newRow);
            var rowIndex = TableData.Rows.Count - 1;
            InsertedRowIndices.Add(rowIndex);

            var change = new PendingChange
            {
                Type = ChangeType.Insert,
                Schema = Schema,
                TableName = TableName,
                RowIndex = rowIndex,
                NewValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (DataColumn col in TableData.Columns)
            {
                change.NewValues[col.ColumnName] = null;
            }

            PendingChanges.Add(change);
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void ExecuteDuplicateRow(object? parameter)
        {
            if (IsReadOnly) return;

            DataRow? sourceRow = null;
            if (parameter is DataRowView rowView)
            {
                sourceRow = rowView.Row;
            }
            else if (parameter is System.Collections.IList list && list.Count > 0 && list[0] is DataRowView firstView)
            {
                sourceRow = firstView.Row;
            }

            if (sourceRow == null && TableData.Rows.Count > 0)
            {
                sourceRow = TableData.Rows[TableData.Rows.Count - 1];
            }

            if (sourceRow == null) return;

            foreach (DataColumn col in TableData.Columns)
            {
                col.ReadOnly = false;
                col.AllowDBNull = true;
            }

            var newRow = TableData.NewRow();
            var change = new PendingChange
            {
                Type = ChangeType.Insert,
                Schema = Schema,
                TableName = TableName,
                NewValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (DataColumn col in TableData.Columns)
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

            TableData.Rows.Add(newRow);
            var rowIndex = TableData.Rows.Count - 1;
            change.RowIndex = rowIndex;
            InsertedRowIndices.Add(rowIndex);

            PendingChanges.Add(change);
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void ExecuteDeleteRow(object? parameter)
        {
            if (IsReadOnly) return;

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
            var rowIndex = TableData.Rows.IndexOf(row);
            if (rowIndex < 0) return;

            // If it was newly inserted and not committed, simply remove from table
            if (InsertedRowIndices.Contains(rowIndex))
            {
                InsertedRowIndices.Remove(rowIndex);
                var insChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Insert && c.RowIndex == rowIndex);
                if (insChange != null) PendingChanges.Remove(insChange);
                TableData.Rows.Remove(row);
                UpdatePendingCounts();
                VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Otherwise mark as Deleted for commit
            if (DeletedRowIndices.Contains(rowIndex))
            {
                // Toggle / unmark deletion
                DeletedRowIndices.Remove(rowIndex);
                var delChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Delete && c.RowIndex == rowIndex);
                if (delChange != null) PendingChanges.Remove(delChange);
            }
            else
            {
                DeletedRowIndices.Add(rowIndex);
                
                // Remove any update change for this row
                var updChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Update && c.RowIndex == rowIndex);
                if (updChange != null) PendingChanges.Remove(updChange);
                ModifiedCellsByRow.Remove(rowIndex);

                if (!_originalRowValues.TryGetValue(rowIndex, out var origDict))
                {
                    origDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataColumn c in TableData.Columns)
                    {
                        origDict[c.ColumnName] = row[c];
                    }
                }

                var delChange = new PendingChange
                {
                    Type = ChangeType.Delete,
                    Schema = Schema,
                    TableName = TableName,
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
            var rowIndex = TableData.Rows.IndexOf(row);
            if (rowIndex < 0) return;

            if (InsertedRowIndices.Contains(rowIndex))
            {
                InsertedRowIndices.Remove(rowIndex);
                var insChange = PendingChanges.FirstOrDefault(c => c.Type == ChangeType.Insert && c.RowIndex == rowIndex);
                if (insChange != null) PendingChanges.Remove(insChange);
                TableData.Rows.Remove(row);
            }
            else
            {
                DeletedRowIndices.Remove(rowIndex);
                ModifiedCellsByRow.Remove(rowIndex);
                var changes = PendingChanges.Where(c => c.RowIndex == rowIndex).ToList();
                foreach (var c in changes) PendingChanges.Remove(c);

                // Restore original values
                if (_originalRowValues.TryGetValue(rowIndex, out var origDict))
                {
                    foreach (var kvp in origDict)
                    {
                        if (TableData.Columns.Contains(kvp.Key))
                        {
                            row[kvp.Key] = kvp.Value ?? DBNull.Value;
                        }
                    }
                }
            }

            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteDiscardChanges()
        {
            _ = LoadDataAsync();
        }

        private void ExecuteCommitChanges()
        {
            if (PendingChanges.Count == 0) return;

            var pendingVm = new PendingChangesViewModel(
                Connection,
                Database,
                Schema,
                TableName,
                PendingChanges.ToList(),
                _driver,
                onSuccess: async () =>
                {
                    await LoadDataAsync();
                });

            _openPendingChangesDialog(pendingVm);
        }

        private void ExecuteExport()
        {
            var exportVm = new ExportViewModel(
                TableData,
                $"{TableName}_{DateTime.Now:yyyyMMdd_HHmmss}",
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
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _originalRowValues.Clear();
                ModifiedCellsByRow.Clear();
                InsertedRowIndices.Clear();
                DeletedRowIndices.Clear();
                PendingChanges.Clear();

                _tableData?.Clear();
                _tableData?.Dispose();
            }
            catch { }
        }
    }
}
