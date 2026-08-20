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
using NertyDb.Editor;
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

            var statements = SqlScriptParser.ParseStatements(sql);
            if (statements.Count == 0) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            IsExecuting = true;
            StatusText = $"Executando {statements.Count} comando(s)...";
            ResultTabs.Clear();
            OnPropertyChanged(nameof(HasResults));

            var swTotal = Stopwatch.StartNew();
            var msgSb = new StringBuilder();
            msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] Iniciando execução de {statements.Count} comando(s)...");

            long totalDuration = 0;
            int totalRows = 0;
            int totalAffected = 0;
            bool hasAnyError = false;
            string? firstErrorMessage = null;
            int tabIndexCounter = 1;

            try
            {
                foreach (var stmt in statements)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Execução interrompida antes do comando #{stmt.Index}.");
                        break;
                    }

                    StatusText = $"Executando comando {stmt.Index} de {statements.Count} ({stmt.CommandType})...";

                    var result = await _driver.ExecuteQueryAsync(
                        Connection,
                        Database,
                        stmt.Sql,
                        timeoutSeconds: 30,
                        maxRows: PageSize,
                        cancellationToken: _cts.Token);

                    totalDuration += result.DurationMs;

                    if (result.HasError)
                    {
                        hasAnyError = true;
                        firstErrorMessage ??= result.ErrorMessage;

                        msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ❌ Erro no comando #{stmt.Index} [{stmt.CommandType}]:");
                        msgSb.AppendLine($"   SQL: {stmt.Sql}");
                        msgSb.AppendLine($"   Mensagem do banco: {result.ErrorMessage}");

                        AppLogService.Instance.LogError("Editor SQL", $"Erro no comando #{stmt.Index} [{stmt.CommandType}]: {result.ErrorMessage}", stmt.Sql);

                        // Interrompe a execução dos statements subsequentes em caso de erro
                        break;
                    }

                    // Se retornou tabelas / registros (ex: SELECT)
                    if (result.Tables.Count > 0)
                    {
                        var tableInfo = ExtractTableInfo(stmt.Sql);

                        for (int i = 0; i < result.Tables.Count; i++)
                        {
                            var dt = result.Tables[i];
                            totalRows += dt.Rows.Count;

                            string title;
                            if (statements.Count > 1)
                            {
                                var name = !string.IsNullOrEmpty(tableInfo.Table) ? tableInfo.Table : $"Resultado {tabIndexCounter}";
                                title = $"#{stmt.Index}: {name}";
                            }
                            else
                            {
                                title = result.Tables.Count > 1 
                                    ? $"Resultado {i + 1}" 
                                    : (string.IsNullOrEmpty(tableInfo.Table) ? "Resultado 1" : tableInfo.Table);
                            }

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
                                executedSql: stmt.Sql,
                                isReadOnly: !tableInfo.IsSingleTable,
                                isReadOnlyReason: tableInfo.ReadOnlyReason,
                                pageSize: PageSize,
                                durationMs: result.DurationMs);

                            ResultTabs.Add(tabVm);
                            tabIndexCounter++;
                        }

                        msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] #{stmt.Index} ({stmt.CommandType}): {result.Tables.Count} conjunto(s) de dados retornado(s) ({result.Tables.Sum(t => t.Rows.Count):N0} linha(s)) em {result.DurationMs} ms.");
                        AppLogService.Instance.LogSuccess("Editor SQL", $"Comando #{stmt.Index} [{stmt.CommandType}] retornou {result.Tables.Sum(t => t.Rows.Count):N0} linhas em {result.DurationMs} ms.", stmt.Sql);
                    }
                    else
                    {
                        // Comandos DML / DDL (UPDATE, DELETE, INSERT, CREATE, etc.)
                        int affected = result.TotalRowsAffected >= 0 ? result.TotalRowsAffected : 0;
                        totalAffected += affected;

                        msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] #{stmt.Index} ({stmt.CommandType}): {affected:N0} linha(s) afetada(s) em {result.DurationMs} ms.");
                        AppLogService.Instance.LogSuccess("Editor SQL", $"Comando #{stmt.Index} [{stmt.CommandType}] concluiu: {affected:N0} linhas afetadas em {result.DurationMs} ms.", stmt.Sql);
                    }

                    // Mensagens extras do driver
                    foreach (var msg in result.Messages)
                    {
                        if (!msg.StartsWith("Comando(s) concluído(s)"))
                        {
                            msgSb.AppendLine($"   ℹ️ {msg}");
                        }
                    }
                }

                swTotal.Stop();
                LastDurationMs = swTotal.ElapsedMilliseconds;
                TotalRowsAffected = totalAffected > 0 ? totalAffected : totalRows;

                OnPropertyChanged(nameof(HasResults));
                SelectedResultTabIndex = ResultTabs.Count > 0 ? 0 : -1;

                if (hasAnyError)
                {
                    StatusText = $"Erro: {firstErrorMessage}";
                    ToastService.Instance.ShowError($"Falha na execução: {firstErrorMessage}", "Erro SQL");
                }
                else
                {
                    if (ResultTabs.Count > 0)
                    {
                        StatusText = $"Concluído em {swTotal.ElapsedMilliseconds} ms ({statements.Count} comando(s), {ResultTabs.Count} aba(s), {totalRows:N0} linhas).";
                        ToastService.Instance.ShowSuccess($"Executado em {swTotal.ElapsedMilliseconds} ms ({ResultTabs.Count} resultado(s))", "Sucesso");
                    }
                    else
                    {
                        StatusText = $"Concluído em {swTotal.ElapsedMilliseconds} ms ({statements.Count} comando(s), {totalAffected:N0} linhas afetadas).";
                        ToastService.Instance.ShowSuccess($"Executado em {swTotal.ElapsedMilliseconds} ms ({totalAffected:N0} afetadas)", "Sucesso");
                    }
                }

                msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] Execução finalizada em {swTotal.ElapsedMilliseconds} ms.");
                MessagesText = msgSb.ToString();

                // Salvar no histórico
                var historyItem = new QueryHistoryItem
                {
                    Timestamp = DateTime.Now,
                    Sql = sql,
                    ConnectionName = Connection.Name,
                    Database = Database,
                    DurationMs = swTotal.ElapsedMilliseconds,
                    RowsAffected = TotalRowsAffected,
                    Success = !hasAnyError,
                    ErrorMessage = firstErrorMessage
                };
                var history = _storageService.LoadHistory();
                history.Insert(0, historyItem);
                _storageService.SaveHistory(history);
            }
            catch (OperationCanceledException)
            {
                swTotal.Stop();
                StatusText = "Consulta cancelada pelo usuário.";
                msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Execução cancelada pelo usuário após {swTotal.ElapsedMilliseconds} ms.");
                MessagesText = msgSb.ToString();
                ToastService.Instance.ShowWarning("Execução cancelada pelo usuário.", "Cancelado");
                AppLogService.Instance.LogWarning("Editor SQL", "Execução cancelada pelo usuário.", sql);
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                StatusText = $"Erro: {ex.Message}";
                msgSb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ❌ Exceção inesperada: {ex.Message}");
                MessagesText = msgSb.ToString();
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

            // Strip comments first
            var cleanSql = SqlScriptParser.StripLeadingComments(sql);

            // Check for JOIN, GROUP BY, UNION, or aggregate functions
            bool hasJoin = Regex.IsMatch(cleanSql, @"\b(?:INNER|LEFT|RIGHT|FULL|CROSS)?\s*JOIN\b", RegexOptions.IgnoreCase);
            bool hasGroupBy = Regex.IsMatch(cleanSql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase);
            bool hasUnion = Regex.IsMatch(cleanSql, @"\bUNION\b", RegexOptions.IgnoreCase);
            bool hasAggregates = Regex.IsMatch(cleanSql, @"\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\(", RegexOptions.IgnoreCase);

            if (hasJoin) return ("dbo", "", false, "Consulta possui cláusula JOIN");
            if (hasGroupBy) return ("dbo", "", false, "Consulta possui agrupamento (GROUP BY)");
            if (hasUnion) return ("dbo", "", false, "Consulta possui união (UNION)");
            if (hasAggregates) return ("dbo", "", false, "Consulta possui funções agregadas");

            // Support: FROM [schema].[table], FROM "schema"."table", FROM schema.table, FROM [table], FROM "table", FROM table
            var match = Regex.Match(cleanSql, @"\bFROM\s+(?:[\[""]?(\w+)[\]""]?\.)?[\[""]?(\w+)[\]""]?", RegexOptions.IgnoreCase);
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
