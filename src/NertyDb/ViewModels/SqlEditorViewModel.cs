using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class SqlEditorViewModel : ObservableObject, IDisposable
    {
        private readonly IDbDriver _driver;
        private readonly ExportService _exportService;
        private readonly StorageService _storageService;
        private readonly Action<PendingChangesViewModel> _openPendingChangesDialog;
        private readonly Action<ExportViewModel> _openExportDialog;

        private string _sqlText = string.Empty;
        private bool _isExecuting;
        private string _statusText = "Pronto";
        private long _lastDurationMs;
        private int _totalRowsAffected;
        private string _messagesText = string.Empty;
        private int _selectedResultTabIndex;
        private CancellationTokenSource? _cts;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Nova Consulta";
        public ConnectionProfile Connection { get; }
        public string Database { get; set; }

        public ObservableCollection<SqlResultTabViewModel> ResultTabs { get; } = new();
        public ObservableCollection<SeniorTemplate> AvailableSnippets { get; } = new();

        public string SqlText
        {
            get => _sqlText;
            set => SetProperty(ref _sqlText, value);
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    (ExecuteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public long LastDurationMs
        {
            get => _lastDurationMs;
            set => SetProperty(ref _lastDurationMs, value);
        }

        public int TotalRowsAffected
        {
            get => _totalRowsAffected;
            set => SetProperty(ref _totalRowsAffected, value);
        }

        public string MessagesText
        {
            get => _messagesText;
            set => SetProperty(ref _messagesText, value);
        }

        public int SelectedResultTabIndex
        {
            get => _selectedResultTabIndex;
            set => SetProperty(ref _selectedResultTabIndex, value);
        }

        public bool HasResults => ResultTabs.Count > 0;

        public ICommand ExecuteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand FormatSqlCommand { get; }
        public ICommand ClearMessagesCommand { get; }
        public ICommand ExportCurrentResultCommand { get; }
        public ICommand InsertSnippetCommand { get; }

        public event EventHandler<string>? InsertTextRequested;

        public SqlEditorViewModel(
            ConnectionProfile connection,
            string database,
            IDbDriver driver,
            ExportService exportService,
            StorageService storageService,
            Action<PendingChangesViewModel> openPendingChangesDialog,
            Action<ExportViewModel> openExportDialog,
            string? initialSql = null)
        {
            Connection = connection;
            Database = database;
            _driver = driver;
            _exportService = exportService;
            _storageService = storageService;
            _openPendingChangesDialog = openPendingChangesDialog;
            _openExportDialog = openExportDialog;

            _sqlText = initialSql ?? $"-- Editor SQL NertyDb: {Connection.Name} ({Database})\r\n-- Pressione F5 para executar ou use Ctrl+Espaço para autocomplete\r\n\r\nSELECT TOP (100) * \r\nFROM sys.tables\r\nORDER BY name;\r\n";

            ExecuteCommand = new AsyncRelayCommand(async (param) =>
            {
                var sqlToRun = param as string;
                if (string.IsNullOrWhiteSpace(sqlToRun))
                {
                    sqlToRun = SqlText;
                }
                await ExecuteSqlAsync(sqlToRun);
            }, _ => !IsExecuting);

            CancelCommand = new RelayCommand(() =>
            {
                _cts?.Cancel();
                StatusText = "Cancelamento de consulta solicitado...";
            }, () => IsExecuting);

            FormatSqlCommand = new RelayCommand(() =>
            {
                SqlText = FormatSqlString(SqlText);
            });

            ClearMessagesCommand = new RelayCommand(() => MessagesText = string.Empty);

            ExportCurrentResultCommand = new RelayCommand(() =>
            {
                if (ResultTabs.Count > 0 && SelectedResultTabIndex >= 0 && SelectedResultTabIndex < ResultTabs.Count)
                {
                    var tab = ResultTabs[SelectedResultTabIndex];
                    tab.ExportCommand.Execute(null);
                }
            }, () => HasResults);

            InsertSnippetCommand = new RelayCommand((snippetObj) =>
            {
                if (snippetObj is SeniorTemplate snippet)
                {
                    if (string.IsNullOrWhiteSpace(SqlText) || SqlText.Trim().StartsWith("-- Editor SQL"))
                    {
                        SqlText = snippet.Sql;
                    }
                    else
                    {
                        InsertTextRequested?.Invoke(this, "\r\n\r\n" + snippet.Sql);
                    }
                }
            });

            LoadSnippets();
        }

        private void LoadSnippets()
        {
            AvailableSnippets.Clear();
            var templates = SeniorTemplates.GetBuiltInTemplates();
            foreach (var t in templates)
            {
                AvailableSnippets.Add(t);
            }
        }

        private int _pageSize = 200;

        public int PageSize
        {
            get => _pageSize;
            set => SetProperty(ref _pageSize, value);
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            IsExecuting = true;
            StatusText = "Executando consulta...";
            ResultTabs.Clear();
            OnPropertyChanged(nameof(HasResults));

            var sw = Stopwatch.StartNew();

            try
            {
                var result = await _driver.ExecuteQueryAsync(Connection, Database, sql, timeoutSeconds: 30, maxRows: PageSize, cancellationToken: _cts.Token);
                sw.Stop();

                LastDurationMs = result.DurationMs;
                TotalRowsAffected = result.TotalRowsAffected;

                var tableInfo = ExtractTableInfo(sql);

                // Add result tables as editable SqlResultTabViewModel
                for (int i = 0; i < result.Tables.Count; i++)
                {
                    var dt = result.Tables[i];
                    var title = result.Tables.Count > 1 ? $"Resultado {i + 1}" : (string.IsNullOrEmpty(tableInfo.Table) ? "Resultado 1" : tableInfo.Table);
                    
                    var tabVm = new SqlResultTabViewModel(
                        dt,
                        title,
                        Connection,
                        Database,
                        _driver,
                        _exportService,
                        _openPendingChangesDialog,
                        _openExportDialog,
                        sourceTable: tableInfo.Table,
                        sourceSchema: tableInfo.Schema,
                        executedSql: sql,
                        isReadOnly: !tableInfo.IsSingleTable,
                        isReadOnlyReason: tableInfo.ReadOnlyReason,
                        pageSize: PageSize,
                        durationMs: result.DurationMs);

                    ResultTabs.Add(tabVm);
                }

                OnPropertyChanged(nameof(HasResults));

                var msgSb = new StringBuilder();
                msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] Consulta executada com sucesso em {result.DurationMs} ms.");

                if (result.TotalRowsAffected >= 0)
                {
                    msgSb.AppendLine($"Linhas afetadas: {result.TotalRowsAffected:N0}");
                }

                if (result.HasError)
                {
                    StatusText = $"Erro: {result.ErrorMessage}";
                    msgSb.AppendLine($"❌ Erro: {result.ErrorMessage}");
                    ToastService.Instance.ShowError($"Erro SQL: {result.ErrorMessage}", "Erro na Consulta");
                    AppLogService.Instance.LogError("Editor SQL", $"Erro: {result.ErrorMessage}", sql);
                }
                else
                {
                    int totalRows = result.Tables.Sum(t => t.Rows.Count);
                    StatusText = $"Executado em {result.DurationMs} ms. {result.Tables.Count} conjunto(s) retornado(s).";
                    ToastService.Instance.ShowSuccess($"Query executada em {result.DurationMs} ms ({totalRows:N0} linhas)", "Sucesso");
                    AppLogService.Instance.LogSuccess("Editor SQL", $"Query concluída em {result.DurationMs} ms. Retornou {totalRows:N0} linhas.", sql);
                }

                foreach (var msg in result.Messages)
                {
                    msgSb.AppendLine(msg);
                }

                MessagesText = msgSb.ToString();
                SelectedResultTabIndex = ResultTabs.Count > 0 ? 0 : -1;

                // Save to history
                var historyItem = new QueryHistoryItem
                {
                    Timestamp = DateTime.Now,
                    Sql = sql,
                    ConnectionName = Connection.Name,
                    Database = Database,
                    DurationMs = result.DurationMs,
                    RowsAffected = result.TotalRowsAffected,
                    Success = !result.HasError,
                    ErrorMessage = result.ErrorMessage
                };
                var history = _storageService.LoadHistory();
                history.Insert(0, historyItem);
                _storageService.SaveHistory(history);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                StatusText = "Consulta cancelada pelo usuário.";
                MessagesText = $"[{DateTime.Now:HH:mm:ss}] ⚠️ Execução cancelada pelo usuário após {sw.ElapsedMilliseconds} ms.";
                ToastService.Instance.ShowWarning("Execução cancelada pelo usuário.", "Cancelado");
                AppLogService.Instance.LogWarning("Editor SQL", "Execução cancelada pelo usuário.", sql);
            }
            catch (Exception ex)
            {
                sw.Stop();
                StatusText = $"Erro: {ex.Message}";
                MessagesText = $"[{DateTime.Now:HH:mm:ss}] ❌ Exceção: {ex.Message}";
                ToastService.Instance.ShowError($"Exceção: {ex.Message}", "Erro de Execução");
                AppLogService.Instance.LogError("Editor SQL", $"Exceção: {ex.Message}", sql);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        private static (string Schema, string Table, bool IsSingleTable, string ReadOnlyReason) ExtractTableInfo(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return ("dbo", "", false, "SQL vazio");

            // Check for JOIN, GROUP BY, UNION, or aggregate functions
            bool hasJoin = Regex.IsMatch(sql, @"\b(?:INNER|LEFT|RIGHT|FULL|CROSS)?\s*JOIN\b", RegexOptions.IgnoreCase);
            bool hasGroupBy = Regex.IsMatch(sql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase);
            bool hasUnion = Regex.IsMatch(sql, @"\bUNION\b", RegexOptions.IgnoreCase);
            bool hasAggregates = Regex.IsMatch(sql, @"\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\(", RegexOptions.IgnoreCase);

            if (hasJoin) return ("dbo", "", false, "Consulta possui cláusula JOIN");
            if (hasGroupBy) return ("dbo", "", false, "Consulta possui agrupamento (GROUP BY)");
            if (hasUnion) return ("dbo", "", false, "Consulta possui união (UNION)");
            if (hasAggregates) return ("dbo", "", false, "Consulta possui funções agregadas");

            var match = Regex.Match(sql, @"\bFROM\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var schema = match.Groups[1].Success ? match.Groups[1].Value : "dbo";
                var table = match.Groups[2].Value;
                return (schema, table, true, string.Empty);
            }

            return ("dbo", "", false, "Não foi possível determinar a tabela de origem");
        }

        private static string FormatSqlString(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            var keywords = new[]
            {
                "SELECT", "FROM", "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN",
                "ON", "GROUP BY", "ORDER BY", "HAVING", "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
                "CREATE TABLE", "ALTER TABLE", "DROP TABLE", "AND", "OR", "NOT", "IN", "IS NULL", "IS NOT NULL",
                "LIKE", "BETWEEN", "EXISTS", "UNION ALL", "UNION", "TOP", "DISTINCT", "AS", "CASE", "WHEN", "THEN", "ELSE", "END"
            };

            var formatted = sql;
            foreach (var kw in keywords.OrderByDescending(k => k.Length))
            {
                var pattern = $@"\b{kw}\b";
                formatted = Regex.Replace(
                    formatted,
                    pattern,
                    kw,
                    RegexOptions.IgnoreCase);
            }

            return formatted;
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                foreach (var tab in ResultTabs.ToList())
                {
                    tab.Dispose();
                }
                ResultTabs.Clear();
            }
            catch { }
        }
    }
}
