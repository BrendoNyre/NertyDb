using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly StorageService _storageService;
        private readonly ExportService _exportService;
        private readonly Action<PendingChangesViewModel> _openPendingChangesDialog;
        private readonly Action<ExportViewModel> _openExportDialog;
        private readonly Action<ConnectionViewModel> _openConnectionDialog;
        private readonly Action _openAboutDialog;
        private readonly Action<string> _applyTheme;

        private ConnectionProfile? _activeConnection;
        private string _activeDatabase = "master";
        private object? _selectedTab;
        private string _currentTheme = "Light";
        private string _statusMessage = "Pronto para conectar";
        private string _serverVersion = string.Empty;
        private bool _autoCommit = true;

        public SchemaTreeViewModel SchemaTree { get; }
        public ObservableCollection<object> DocumentTabs { get; } = new();

        public bool HasDocumentTabs => DocumentTabs.Count > 0;

        public bool AutoCommit
        {
            get => _autoCommit;
            set
            {
                if (SetProperty(ref _autoCommit, value))
                {
                    OnPropertyChanged(nameof(AutoCommitLabel));
                    foreach (var tab in DocumentTabs.OfType<TableDataViewModel>())
                    {
                        tab.AutoCommit = value;
                    }
                }
            }
        }

        public string AutoCommitLabel => AutoCommit ? "Auto-Commit: LIGADO" : "Auto-Commit: MANUAL";

        public ConnectionProfile? ActiveConnection
        {
            get => _activeConnection;
            set
            {
                if (SetProperty(ref _activeConnection, value))
                {
                    OnPropertyChanged(nameof(HasActiveConnection));
                    OnPropertyChanged(nameof(ActiveConnectionSummary));
                }
            }
        }

        public string ActiveDatabase
        {
            get => _activeDatabase;
            set
            {
                if (SetProperty(ref _activeDatabase, value))
                {
                    OnPropertyChanged(nameof(ActiveConnectionSummary));
                }
            }
        }

        public object? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    (CommitActiveTabCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RollbackActiveTabCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (SetProperty(ref _currentTheme, value))
                {
                    _applyTheme(value);
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ServerVersion
        {
            get => _serverVersion;
            set => SetProperty(ref _serverVersion, value);
        }

        public bool HasActiveConnection => ActiveConnection != null;

        public string ActiveConnectionSummary => ActiveConnection != null 
            ? (ActiveConnection.SeniorAuthMode == SeniorAuthMode.SeniorSgu 
                ? $"Senior SGU: {ActiveConnection.SguUsername} • {ActiveConnection.Name} ({ActiveConnection.DatabaseType}) • {ActiveConnection.Server}/{ActiveDatabase}"
                : $"{ActiveConnection.Name} ({ActiveConnection.DatabaseType}) • {ActiveConnection.Server}/{ActiveDatabase}")
            : "Nenhuma conexão ativa";

        public ICommand OpenConnectionsDialogCommand { get; }
        public ICommand NewQueryTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand CloseAllTabsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand RefreshAllCommand { get; }
        public ICommand ToggleAutoCommitCommand { get; }
        public ICommand CommitActiveTabCommand { get; }
        public ICommand RollbackActiveTabCommand { get; }

        public MainViewModel(
            StorageService storageService,
            ExportService exportService,
            Action<PendingChangesViewModel> openPendingChangesDialog,
            Action<ExportViewModel> openExportDialog,
            Action<ConnectionViewModel> openConnectionDialog,
            Action openAboutDialog,
            Action<string> applyTheme)
        {
            _storageService = storageService;
            _exportService = exportService;
            _openPendingChangesDialog = openPendingChangesDialog;
            _openExportDialog = openExportDialog;
            _openConnectionDialog = openConnectionDialog;
            _openAboutDialog = openAboutDialog;
            _applyTheme = applyTheme;

            DocumentTabs.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasDocumentTabs));
                OnPropertyChanged(nameof(DocumentTabs));
            };

            SchemaTree = new SchemaTreeViewModel(
                onOpenTable: OpenTableDataTab,
                onNewQueryWithSql: OpenQueryTabWithSql);

            OpenConnectionsDialogCommand = new RelayCommand(() =>
            {
                var connVm = new ConnectionViewModel(
                    DbDriverFactory.GetDriver(ActiveConnection),
                    _storageService, 
                    onConnect: (profile, db) =>
                    {
                        _ = ConnectToDatabaseAsync(profile, db);
                    });
                _openConnectionDialog(connVm);
            });

            NewQueryTabCommand = new RelayCommand(() =>
            {
                if (ActiveConnection != null)
                {
                    OpenNewQueryTab(ActiveConnection, ActiveDatabase);
                }
                else
                {
                    OpenConnectionsDialogCommand.Execute(null);
                }
            });

            CloseTabCommand = new RelayCommand((tab) =>
            {
                if (tab != null)
                {
                    if (tab is TableDataViewModel td && td.HasPendingChanges)
                    {
                        var result = MessageBox.Show(
                            $"A tabela {td.Title} possui {td.TotalPendingCount} alteração(ões) não gravada(s).\r\nDeseja fechar e descartar essas alterações?",
                            "Alterações Pendentes",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (result != MessageBoxResult.Yes) return;
                    }
                    DocumentTabs.Remove(tab);
                    (tab as IDisposable)?.Dispose();

                    if (SelectedTab == tab)
                    {
                        SelectedTab = DocumentTabs.LastOrDefault();
                    }
                }
            });

            CloseAllTabsCommand = new RelayCommand(() =>
            {
                var tabs = DocumentTabs.ToList();
                foreach (var t in tabs)
                {
                    DocumentTabs.Remove(t);
                    (t as IDisposable)?.Dispose();
                }
                SelectedTab = null;
            }, () => DocumentTabs.Count > 0);

            ToggleThemeCommand = new RelayCommand(() =>
            {
                CurrentTheme = CurrentTheme == "Dark" ? "Light" : "Dark";
                var settings = _storageService.LoadSettings();
                settings.Theme = CurrentTheme;
                _storageService.SaveSettings(settings);
            });

            ToggleAutoCommitCommand = new RelayCommand(() =>
            {
                AutoCommit = !AutoCommit;
            });

            CommitActiveTabCommand = new RelayCommand(() =>
            {
                if (SelectedTab is TableDataViewModel td && td.HasPendingChanges)
                {
                    td.CommitChangesCommand.Execute(null);
                }
            }, () => SelectedTab is TableDataViewModel td && td.HasPendingChanges);

            RollbackActiveTabCommand = new RelayCommand(() =>
            {
                if (SelectedTab is TableDataViewModel td && td.HasPendingChanges)
                {
                    td.DiscardChangesCommand.Execute(null);
                }
            }, () => SelectedTab is TableDataViewModel td && td.HasPendingChanges);

            OpenAboutCommand = new RelayCommand(() => _openAboutDialog());

            RefreshAllCommand = new AsyncRelayCommand(async () =>
            {
                if (ActiveConnection != null)
                {
                    await SchemaTree.LoadDatabaseStructureAsync(ActiveConnection, ActiveDatabase);
                }
            }, () => HasActiveConnection);

            // Load initial theme from settings
            var savedSettings = _storageService.LoadSettings();
            CurrentTheme = savedSettings.Theme ?? "Light";
        }

        public async Task ConnectToDatabaseAsync(ConnectionProfile profile, string database)
        {
            ActiveConnection = profile;
            ActiveDatabase = !string.IsNullOrWhiteSpace(database) ? database : (profile.DatabaseType == DatabaseType.Oracle ? (profile.ServiceName ?? "ORCL") : profile.Database);
            StatusMessage = $"Conectando a {profile.Name} ({profile.DatabaseType})...";

            try
            {
                var driver = DbDriverFactory.GetDriver(profile);
                var (success, msg, latency) = await driver.TestConnectionAsync(profile);
                if (!success)
                {
                    StatusMessage = $"Erro na conexão com o banco: {msg}";
                    MessageBox.Show(msg, "Erro de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (profile.SeniorAuthMode == SeniorAuthMode.SeniorSgu)
                {
                    var sguResult = await SguAuthenticationService.ValidateSguUserAsync(
                        driver,
                        profile,
                        ActiveDatabase,
                        profile.SguUsername,
                        profile.SguPassword);

                    if (!sguResult.IsSuccess)
                    {
                        StatusMessage = $"Falha na autenticação SGU: {sguResult.ErrorMessage}";
                        MessageBox.Show(sguResult.ErrorMessage, "Autenticação Senior (SGU)", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    StatusMessage = $"Conectado com sucesso ({latency} ms) • SGU: {sguResult.UserName} ({sguResult.GroupName})";
                }
                else
                {
                    StatusMessage = $"Conectado com sucesso ({latency} ms)";
                }

                // Auto open a SQL query tab immediately so user has zero wait time
                if (DocumentTabs.Count == 0)
                {
                    OpenNewQueryTab(profile, ActiveDatabase);
                }

                // Load schema tree asynchronously
                _ = SchemaTree.LoadDatabaseStructureAsync(profile, ActiveDatabase);

                // Fetch server version in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ver = await driver.GetServerVersionAsync(profile);
                        Application.Current.Dispatcher.Invoke(() => ServerVersion = ver);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao conectar: {ex.Message}";
            }
        }

        public void OpenTableDataTab(ConnectionProfile connection, string database, string schema, string tableName)
        {
            var existing = DocumentTabs.OfType<TableDataViewModel>()
                .FirstOrDefault(t => t.Connection.Id == connection.Id && t.Database == database && t.Schema == schema && t.TableName == tableName);

            if (existing != null)
            {
                SelectedTab = existing;
                return;
            }

            var driver = DbDriverFactory.GetDriver(connection);
            var tableVm = new TableDataViewModel(
                connection,
                database,
                schema,
                tableName,
                isView: false,
                driver,
                _exportService,
                _openPendingChangesDialog,
                _openExportDialog)
            {
                AutoCommit = AutoCommit
            };

            DocumentTabs.Add(tableVm);
            SelectedTab = tableVm;
            _ = tableVm.LoadDataAsync();
        }

        public void OpenQueryTabWithSql(ConnectionProfile connection, string database, string sql)
        {
            var driver = DbDriverFactory.GetDriver(connection);
            var queryVm = new SqlEditorViewModel(
                connection,
                database,
                driver,
                _exportService,
                _storageService,
                _openPendingChangesDialog,
                _openExportDialog,
                initialSql: sql);

            queryVm.Title = $"Consulta {DocumentTabs.OfType<SqlEditorViewModel>().Count() + 1}";
            DocumentTabs.Add(queryVm);
            SelectedTab = queryVm;
        }

        public void OpenNewQueryTab(ConnectionProfile connection, string database)
        {
            var count = DocumentTabs.OfType<SqlEditorViewModel>().Count() + 1;
            var driver = DbDriverFactory.GetDriver(connection);
            var queryVm = new SqlEditorViewModel(
                connection,
                database,
                driver,
                _exportService,
                _storageService,
                _openPendingChangesDialog,
                _openExportDialog);

            queryVm.Title = $"Consulta {count}";
            DocumentTabs.Add(queryVm);
            SelectedTab = queryVm;
        }
    }
}
