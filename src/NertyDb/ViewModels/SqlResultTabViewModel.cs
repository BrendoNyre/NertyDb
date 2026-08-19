using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
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
        private bool _isReadOnly;
        private string _isReadOnlyReason = string.Empty;
        private long _durationMs;
        private DateTime _executionTime = DateTime.Now;

        // Row change tracking
        private readonly Dictionary<int, Dictionary<string, object?>> _originalRowValues = new();
        public Dictionary<int, HashSet<string>> ModifiedCellsByRow { get; } = new();
        public HashSet<int> InsertedRowIndices { get; } = new();
        public HashSet<int> DeletedRowIndices { get; } = new();
        public ObservableCollection<PendingChange> PendingChanges { get; } = new();
        public List<string> PrimaryKeyColumns { get; set; } = new();
        public HashSet<string> IdentityColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public SelectionStatsViewModel SelectionStats { get; } = new();

        public event EventHandler? VisualChangesUpdated;
        public event EventHandler<int>? RowCreated;

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

        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (SetProperty(ref _isReadOnly, value))
                {
                    OnPropertyChanged(nameof(ReadOnlyBadgeText));
                    OnPropertyChanged(nameof(ReadOnlyBadgeColor));
                }
            }
        }

        public string IsReadOnlyReason
        {
            get => _isReadOnlyReason;
            set => SetProperty(ref _isReadOnlyReason, value);
        }

        public string ReadOnlyBadgeText
        {
            get
            {
                if (IsReadOnly) return "🔒 Somente Leitura (Query Complexa / Agregação)";
                if (HasIdentifiedTable && HasPrimaryKey) return "✏️ Gravável";
                if (HasIdentifiedTable && !HasPrimaryKey) return "⚠️ Sem PK (Somente Leitura)";
                return "🔒 Somente Leitura";
            }
        }

        public string ReadOnlyBadgeColor => (IsReadOnly || !HasPrimaryKey) ? "#EF4444" : "#10B981";

        public long DurationMs
        {
            get => _durationMs;
            set
            {
                if (SetProperty(ref _durationMs, value))
                {
                    OnPropertyChanged(nameof(ExecutionStatusText));
                }
            }
        }

        public DateTime ExecutionTime
        {
            get => _executionTime;
            set
            {
                if (SetProperty(ref _executionTime, value))
                {
                    OnPropertyChanged(nameof(ExecutionStatusText));
                }
            }
        }

        public string ExecutionStatusText
        {
            get
            {
                var rows = Data != null ? Data.Rows.Count : 0;
                var durationSec = DurationMs / 1000.0;
                return $"{rows:N0} linha(s) recuperada(s) - {durationSec:F3}s, em {ExecutionTime:dd/MM/yyyy às HH:mm:ss}";
            }
        }

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
                    OnPropertyChanged(nameof(ExecutionStatusText));
                    OnPropertyChanged(nameof(PaginationSummary));
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

        public int RowCount => Data?.Rows.Count ?? 0;
        public string Summary => $"{RowCount:N0} linha(s), {Data?.Columns.Count ?? 0} coluna(s)";

        public int TotalPendingCount => PendingChanges.Count;
        public bool HasPendingChanges => TotalPendingCount > 0;
        public string PendingBadgeText => $"{TotalPendingCount} alteração(ões)";

        private string _executedSql = string.Empty;
        private int _pageSize = 200;
        private int _currentPage = 1;
        private bool _isRefreshing;

        public string ExecutedSql
        {
            get => _executedSql;
            set => SetProperty(ref _executedSql, value);
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    OnPropertyChanged(nameof(PaginationSummary));
                    OnPropertyChanged(nameof(TotalPages));
                }
            }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(PaginationSummary));
                }
            }
        }

        public int TotalPages => PageSize > 0 && RowCount > 0 ? (int)Math.Max(1, Math.Ceiling((double)RowCount / PageSize)) : 1;

        public string PaginationSummary
        {
            get
            {
                if (Data == null || Data.Rows.Count == 0) return "0 linhas";
                return $"{Data.Rows.Count:N0} linha(s)";
            }
        }

        public ICommand AddRowCommand { get; }
        public ICommand DuplicateRowCommand { get; }
        public ICommand DeleteRowCommand { get; }
        public ICommand DiscardChangesCommand { get; }
        public ICommand CommitChangesCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        public ICommand FirstPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }

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
            string? sourceSchema = null,
            string? executedSql = null,
            bool isReadOnly = false,
            string isReadOnlyReason = "",
            int pageSize = 200,
            long durationMs = 0)
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
            _executedSql = executedSql ?? string.Empty;
            _isReadOnly = isReadOnly;
            _isReadOnlyReason = isReadOnlyReason;
            _pageSize = pageSize;
            _durationMs = durationMs;
            _executionTime = DateTime.Now;

            // Unlock all columns so editing is possible
            foreach (DataColumn col in data.Columns)
            {
                col.ReadOnly = false;
            }

            _data = data;
            _filteredView = _data.DefaultView;

            InitializeRowTracking();
            _ = ResolveTableAndPkInfoAsync();

            AddRowCommand = new RelayCommand(ExecuteAddNewRow, _ => !IsReadOnly);
            DuplicateRowCommand = new RelayCommand(ExecuteDuplicateRow, _ => !IsReadOnly);
            DeleteRowCommand = new RelayCommand(ExecuteDeleteRow, _ => !IsReadOnly);
            DiscardChangesCommand = new RelayCommand(_ => ExecuteDiscardChanges(), _ => HasPendingChanges);
            CommitChangesCommand = new RelayCommand(_ => ExecuteCommitChanges(), _ => HasPendingChanges && !IsReadOnly);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync());
            ExportCommand = new RelayCommand(_ => ExecuteExport());

            FirstPageCommand = new RelayCommand(_ => CurrentPage = 1, _ => CurrentPage > 1);
            PrevPageCommand = new RelayCommand(_ => CurrentPage = Math.Max(1, CurrentPage - 1), _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => CurrentPage = Math.Min(TotalPages, CurrentPage + 1), _ => CurrentPage < TotalPages);
            LastPageCommand = new RelayCommand(_ => CurrentPage = TotalPages, _ => CurrentPage < TotalPages);
        }

        public async Task RefreshDataAsync()
        {
            if (string.IsNullOrWhiteSpace(ExecutedSql) || IsRefreshing) return;
            IsRefreshing = true;

            try
            {
                var result = await _driver.ExecuteQueryAsync(_connection, _database, ExecutedSql, timeoutSeconds: 30, maxRows: PageSize);
                if (!result.HasError && result.Tables.Count > 0)
                {
                    var dt = result.Tables[0];
                    foreach (DataColumn col in dt.Columns)
                    {
                        col.ReadOnly = false;
                    }
                    DurationMs = result.DurationMs;
                    ExecutionTime = DateTime.Now;
                    Data = dt;
                    ToastService.Instance.ShowSuccess($"Resultado atualizado: {Data.Rows.Count:N0} linha(s).", "Atualização");
                    AppLogService.Instance.LogSuccess("Editor SQL", $"Query atualizada: {Data.Rows.Count} linhas retornadas.");
                }
                else
                {
                    var err = result.ErrorMessage ?? "Nenhum resultado retornado.";
                    ToastService.Instance.ShowError($"Erro ao atualizar query: {err}", "Falha de Atualização");
                    AppLogService.Instance.LogError("Editor SQL", $"Falha ao atualizar query: {err}");
                }
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError($"Exceção ao atualizar: {ex.Message}", "Erro");
                AppLogService.Instance.LogError("Editor SQL", $"Exceção ao atualizar: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        public async Task ResolveTableAndPkInfoAsync()
        {
            if (string.IsNullOrEmpty(TableName)) return;

            try
            {
                var cache = MetadataCacheService.Instance;
                var details = cache.GetCachedTableDetails(_connection.Id, _database, Schema, TableName);

                if (details == null)
                {
                    details = await _driver.GetTableDetailsAsync(_connection, _database, Schema, TableName);
                    if (details != null)
                    {
                        cache.SetCachedTableDetails(_connection.Id, _database, Schema, TableName, details);
                    }
                }

                if (details != null)
                {
                    if (details.PrimaryKeyColumns.Count > 0)
                    {
                        PrimaryKeyColumns = new List<string>(details.PrimaryKeyColumns);
                    }

                    IdentityColumns.Clear();
                    foreach (var col in details.Columns.Where(c => c.IsIdentity))
                    {
                        IdentityColumns.Add(col.Name);
                    }
                }
            }
            catch
            {
                // Fallback: check Senior convention PK column names
                var pkCandidates = new[] { "NUMEMP", "CODFIL", "NUMCAD", "DATACC", "HORACC", "CODUSU", "PERID", "DATSEQ", "ID", "CODIGO" };
                foreach (var c in pkCandidates)
                {
                    if (Data.Columns.Contains(c) && !PrimaryKeyColumns.Contains(c))
                    {
                        PrimaryKeyColumns.Add(c);
                    }
                }
            }
            finally
            {
                OnPropertyChanged(nameof(HasPrimaryKey));
                OnPropertyChanged(nameof(ReadOnlyBadgeText));
                OnPropertyChanged(nameof(ReadOnlyBadgeColor));
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

        public void TriggerVisualUpdate()
        {
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
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

        public void ApplyBulkCellValues(IEnumerable<(DataRowView rowView, string columnName)> cells, object? rawValue)
        {
            foreach (var item in cells)
            {
                if (item.rowView == null || string.IsNullOrEmpty(item.columnName)) continue;
                if (!Data.Columns.Contains(item.columnName)) continue;

                var col = Data.Columns[item.columnName]!;
                object? typedVal = ConvertValueToColumnType(rawValue, col);
                item.rowView[item.columnName] = typedVal ?? DBNull.Value;
                OnCellEdited(item.rowView, item.columnName, typedVal);
            }

            TriggerVisualUpdate();
        }

        public static object? ConvertValueToColumnType(object? value, DataColumn column)
        {
            if (value == null || value == DBNull.Value) return DBNull.Value;
            var str = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(str) || str.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return DBNull.Value;
            }

            var targetType = Nullable.GetUnderlyingType(column.DataType) ?? column.DataType;

            try
            {
                if (targetType == typeof(string)) return str;
                if (targetType == typeof(int)) return int.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(long)) return long.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(short)) return short.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(byte)) return byte.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(decimal)) return decimal.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(double)) return double.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(float)) return float.Parse(str, NumberStyles.Any, CultureInfo.CurrentCulture);
                if (targetType == typeof(bool))
                {
                    if (str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase) || str.Equals("sim", StringComparison.OrdinalIgnoreCase) || str.Equals("s", StringComparison.OrdinalIgnoreCase)) return true;
                    if (str == "0" || str.Equals("false", StringComparison.OrdinalIgnoreCase) || str.Equals("nao", StringComparison.OrdinalIgnoreCase) || str.Equals("não", StringComparison.OrdinalIgnoreCase) || str.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
                    return bool.Parse(str);
                }
                if (targetType == typeof(DateTime)) return DateTime.Parse(str, CultureInfo.CurrentCulture);
                if (targetType == typeof(Guid)) return Guid.Parse(str);

                return Convert.ChangeType(str, targetType, CultureInfo.CurrentCulture);
            }
            catch
            {
                // Fallback attempt with InvariantCulture
                try
                {
                    return Convert.ChangeType(str, targetType, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return str;
                }
            }
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
                // Fallback: match all original column values
                foreach (var kvp in origDict)
                {
                    pks[kvp.Key] = kvp.Value == DBNull.Value ? null : kvp.Value;
                }
            }
            return pks;
        }

        public void ExecuteAddNewRow(object? _ = null)
        {
            if (IsReadOnly) return;

            foreach (DataColumn col in Data.Columns)
            {
                col.ReadOnly = false;
                col.AllowDBNull = true;
            }

            var newRow = Data.NewRow();
            foreach (DataColumn col in Data.Columns)
            {
                if (col.AutoIncrement || IdentityColumns.Contains(col.ColumnName))
                {
                    newRow[col] = DBNull.Value;
                }
                else
                {
                    newRow[col] = DBNull.Value;
                }
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
                if (!IdentityColumns.Contains(col.ColumnName))
                {
                    change.NewValues[col.ColumnName] = null;
                }
            }

            PendingChanges.Add(change);
            UpdatePendingCounts();
            VisualChangesUpdated?.Invoke(this, EventArgs.Empty);
            RowCreated?.Invoke(this, rowIndex);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ExecutionStatusText));
        }

        public void ExecuteDuplicateRow(object? parameter)
        {
            if (IsReadOnly) return;

            DataRow? sourceRow = null;
            if (parameter is DataRowView rowView)
            {
                sourceRow = rowView.Row;
            }
            else if (parameter is IList list && list.Count > 0)
            {
                var first = list[0];
                if (first is DataRowView drv)
                {
                    sourceRow = drv.Row;
                }
                else if (first is DataGridCellInfo cellInfo && cellInfo.Item is DataRowView cellRow)
                {
                    sourceRow = cellRow.Row;
                }
            }

            if (sourceRow == null && Data.Rows.Count > 0)
            {
                sourceRow = Data.Rows[Data.Rows.Count - 1];
            }

            if (sourceRow == null) return;

            foreach (DataColumn col in Data.Columns)
            {
                col.ReadOnly = false;
                col.AllowDBNull = true;
            }

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
                if (col.AutoIncrement || IdentityColumns.Contains(col.ColumnName))
                {
                    newRow[col] = DBNull.Value;
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
            RowCreated?.Invoke(this, rowIndex);
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ExecutionStatusText));

            ToastService.Instance.ShowSuccess($"Linha #{rowIndex + 1} duplicada com sucesso! Altere os campos e clique em Salvar.", "Linha Duplicada");
        }

        public void ExecuteDeleteRow(object? parameter)
        {
            if (IsReadOnly) return;

            var rowsToDelete = new List<DataRow>();

            if (parameter is DataRowView rowView)
            {
                rowsToDelete.Add(rowView.Row);
            }
            else if (parameter is IList list)
            {
                foreach (var item in list)
                {
                    if (item is DataRowView drv && !rowsToDelete.Contains(drv.Row))
                    {
                        rowsToDelete.Add(drv.Row);
                    }
                    else if (item is DataGridCellInfo cell && cell.Item is DataRowView cellDrv && !rowsToDelete.Contains(cellDrv.Row))
                    {
                        rowsToDelete.Add(cellDrv.Row);
                    }
                }
            }

            if (rowsToDelete.Count == 0 && Data.Rows.Count > 0)
            {
                rowsToDelete.Add(Data.Rows[Data.Rows.Count - 1]);
            }

            foreach (var r in rowsToDelete)
            {
                DeleteSingleRow(r);
            }
        }

        private void DeleteSingleRow(DataRow row)
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
                OnPropertyChanged(nameof(ExecutionStatusText));
                return;
            }

            if (DeletedRowIndices.Contains(rowIndex))
            {
                // Unmark deletion
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
            OnPropertyChanged(nameof(ExecutionStatusText));
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
            OnPropertyChanged(nameof(ExecutionStatusText));
            ToastService.Instance.ShowInfo("Todas as alterações pendentes foram descartadas.", "Alterações Descartadas");
        }

        private void ExecuteCommitChanges()
        {
            if (PendingChanges.Count == 0 || IsReadOnly) return;

            var pendingVm = new PendingChangesViewModel(
                _connection,
                _database,
                Schema,
                string.IsNullOrEmpty(TableName) ? "TABELA" : TableName,
                PendingChanges.ToList(),
                _driver,
                onSuccess: async () =>
                {
                    // Auto-refresh from database after successful commit
                    await RefreshDataAsync();
                    ToastService.Instance.ShowSuccess("Alterações salvas e sincronizadas com sucesso!", "Salvo");
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
