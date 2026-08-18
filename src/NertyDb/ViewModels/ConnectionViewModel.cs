using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using NertyDb.Data;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.ViewModels
{
    public class ConnectionViewModel : ObservableObject
    {
        private readonly StorageService _storageService;
        private readonly SeniorConfigService _seniorConfigService;
        private readonly Action<ConnectionProfile, string> _onConnect;

        private ConnectionProfile _selectedProfile;
        private bool _isTesting;
        private string _testResultMessage = string.Empty;
        private bool _testSuccess;
        private bool _hasTested;
        private string _selectedDatabase = string.Empty;
        private bool _showPassword;
        private bool _showSguPassword;
        private ObservableCollection<string> _availableDatabases = new();
        private ObservableCollection<string> _detectedServers = new();
        private ObservableCollection<SeniorDatabaseAlias> _seniorAliases = new();
        private SeniorDatabaseAlias? _selectedSeniorAlias;

        public ObservableCollection<ConnectionProfile> SavedProfiles { get; } = new();

        public ObservableCollection<DatabaseType> AvailableDatabaseTypes { get; } = new()
        {
            DatabaseType.SqlServer,
            DatabaseType.Oracle
        };

        public ObservableCollection<SeniorDatabaseAlias> SeniorAliases
        {
            get => _seniorAliases;
            set => SetProperty(ref _seniorAliases, value);
        }

        public SeniorDatabaseAlias? SelectedSeniorAlias
        {
            get => _selectedSeniorAlias;
            set
            {
                if (SetProperty(ref _selectedSeniorAlias, value) && value != null && SelectedProfile != null)
                {
                    SelectedProfile.SeniorAlias = value.Alias;
                    SelectedProfile.Server = value.Server;
                    SelectedProfile.Port = value.Port;
                    SelectedProfile.Database = value.DatabaseName;
                    SelectedProfile.DatabaseType = value.DatabaseType;
                    SelectedProfile.Username = value.Username;
                    if (!string.IsNullOrEmpty(value.PlainPassword))
                    {
                        SelectedProfile.Password = value.PlainPassword;
                        OnPropertyChanged(nameof(CurrentPassword));
                    }
                    SelectedDatabase = value.DatabaseName;
                    
                    OnPropertyChanged(nameof(SelectedDatabaseType));
                    OnPropertyChanged(nameof(IsSqlServer));
                    OnPropertyChanged(nameof(IsOracle));
                }
            }
        }

        public bool IsSeniorInstalled => _seniorConfigService.IsSeniorInstalled;
        public string SeniorConfigPath => _seniorConfigService.DetectedConfigPath ?? "Não localizado (C:\\Senior\\senior.cfg)";
        public string SeniorStatusSummary => IsSeniorInstalled
            ? $"✅ Configuração Senior carregada ({_seniorAliases.Count} bases encontradas em {_seniorConfigService.DetectedConfigPath})"
            : "⚠️ Instalação do Senior não encontrada em C:\\Senior (Modo Standalone / Remoto)";

        public ObservableCollection<string> DetectedServers
        {
            get => _detectedServers;
            set => SetProperty(ref _detectedServers, value);
        }

        public ConnectionProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    HasTested = false;
                    TestResultMessage = string.Empty;
                    SelectedDatabase = _selectedProfile?.Database ?? "master";
                    
                    // Match senior alias if applicable
                    if (!string.IsNullOrEmpty(_selectedProfile?.SeniorAlias))
                    {
                        _selectedSeniorAlias = SeniorAliases.FirstOrDefault(a => a.Alias.Equals(_selectedProfile.SeniorAlias, StringComparison.OrdinalIgnoreCase));
                        OnPropertyChanged(nameof(SelectedSeniorAlias));
                    }

                    OnPropertyChanged(nameof(IsSeniorAuth));
                    OnPropertyChanged(nameof(IsDirectDbAuth));
                    OnPropertyChanged(nameof(IsWindowsAuth));
                    OnPropertyChanged(nameof(IsSqlAuth));
                    OnPropertyChanged(nameof(IsSqlServer));
                    OnPropertyChanged(nameof(IsOracle));
                    OnPropertyChanged(nameof(SelectedDatabaseType));
                    OnPropertyChanged(nameof(CurrentPassword));
                    OnPropertyChanged(nameof(CurrentSguPassword));
                }
            }
        }

        public bool IsSeniorAuth
        {
            get => SelectedProfile?.SeniorAuthMode == SeniorAuthMode.SeniorSgu;
            set
            {
                if (SelectedProfile != null && value)
                {
                    SelectedProfile.SeniorAuthMode = SeniorAuthMode.SeniorSgu;
                    OnPropertyChanged(nameof(IsSeniorAuth));
                    OnPropertyChanged(nameof(IsDirectDbAuth));
                }
            }
        }

        public bool IsDirectDbAuth
        {
            get => SelectedProfile?.SeniorAuthMode == SeniorAuthMode.DirectDatabase;
            set
            {
                if (SelectedProfile != null && value)
                {
                    SelectedProfile.SeniorAuthMode = SeniorAuthMode.DirectDatabase;
                    OnPropertyChanged(nameof(IsSeniorAuth));
                    OnPropertyChanged(nameof(IsDirectDbAuth));
                }
            }
        }

        public DatabaseType SelectedDatabaseType
        {
            get => SelectedProfile?.DatabaseType ?? DatabaseType.SqlServer;
            set
            {
                if (SelectedProfile != null && SelectedProfile.DatabaseType != value)
                {
                    SelectedProfile.DatabaseType = value;
                    if (value == DatabaseType.Oracle)
                    {
                        if (SelectedProfile.Port == 1433 || SelectedProfile.Port == 0) SelectedProfile.Port = 1521;
                        if (SelectedProfile.Username == "sa") SelectedProfile.Username = "SYSTEM";
                        if (string.IsNullOrWhiteSpace(SelectedProfile.ServiceName)) SelectedProfile.ServiceName = "ORCL";
                    }
                    else
                    {
                        if (SelectedProfile.Port == 1521 || SelectedProfile.Port == 0) SelectedProfile.Port = 1433;
                        if (SelectedProfile.Username == "SYSTEM") SelectedProfile.Username = "sa";
                        if (string.IsNullOrWhiteSpace(SelectedProfile.Database)) SelectedProfile.Database = "senior";
                    }
                    OnPropertyChanged(nameof(SelectedDatabaseType));
                    OnPropertyChanged(nameof(IsSqlServer));
                    OnPropertyChanged(nameof(IsOracle));
                }
            }
        }

        public bool IsSqlServer => SelectedProfile?.DatabaseType == DatabaseType.SqlServer;
        public bool IsOracle => SelectedProfile?.DatabaseType == DatabaseType.Oracle;

        public bool ShowPassword
        {
            get => _showPassword;
            set
            {
                if (SetProperty(ref _showPassword, value))
                {
                    OnPropertyChanged(nameof(CurrentPassword));
                }
            }
        }

        public bool ShowSguPassword
        {
            get => _showSguPassword;
            set
            {
                if (SetProperty(ref _showSguPassword, value))
                {
                    OnPropertyChanged(nameof(CurrentSguPassword));
                }
            }
        }

        public string CurrentPassword
        {
            get => SelectedProfile?.Password ?? string.Empty;
            set
            {
                if (SelectedProfile != null)
                {
                    SelectedProfile.Password = value;
                    OnPropertyChanged(nameof(CurrentPassword));
                }
            }
        }

        public string CurrentSguPassword
        {
            get => SelectedProfile?.SguPassword ?? string.Empty;
            set
            {
                if (SelectedProfile != null)
                {
                    SelectedProfile.SguPassword = value;
                    OnPropertyChanged(nameof(CurrentSguPassword));
                }
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            set => SetProperty(ref _isTesting, value);
        }

        public string TestResultMessage
        {
            get => _testResultMessage;
            set => SetProperty(ref _testResultMessage, value);
        }

        public bool TestSuccess
        {
            get => _testSuccess;
            set => SetProperty(ref _testSuccess, value);
        }

        public bool HasTested
        {
            get => _hasTested;
            set => SetProperty(ref _hasTested, value);
        }

        public string SelectedDatabase
        {
            get => _selectedDatabase;
            set => SetProperty(ref _selectedDatabase, value);
        }

        public ObservableCollection<string> AvailableDatabases
        {
            get => _availableDatabases;
            set => SetProperty(ref _availableDatabases, value);
        }

        public bool IsWindowsAuth
        {
            get => SelectedProfile?.AuthType == AuthenticationType.WindowsIntegrated;
            set
            {
                if (SelectedProfile != null && value)
                {
                    SelectedProfile.AuthType = AuthenticationType.WindowsIntegrated;
                    OnPropertyChanged(nameof(IsWindowsAuth));
                    OnPropertyChanged(nameof(IsSqlAuth));
                }
            }
        }

        public bool IsSqlAuth
        {
            get => SelectedProfile?.AuthType == AuthenticationType.SqlServer;
            set
            {
                if (SelectedProfile != null && value)
                {
                    SelectedProfile.AuthType = AuthenticationType.SqlServer;
                    OnPropertyChanged(nameof(IsWindowsAuth));
                    OnPropertyChanged(nameof(IsSqlAuth));
                }
            }
        }

        public ICommand TestConnectionCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand CloneProfileCommand { get; }
        public ICommand SaveProfilesCommand { get; }
        public ICommand FetchDatabasesCommand { get; }
        public ICommand DetectServersCommand { get; }
        public ICommand RefreshSeniorConfigCommand { get; }
        public ICommand BrowseSeniorConfigCommand { get; }

        public event EventHandler? RequestClose;

        public ConnectionViewModel(
            IDbDriver initialDriver,
            StorageService storageService,
            Action<ConnectionProfile, string> onConnect)
        {
            _storageService = storageService;
            _seniorConfigService = new SeniorConfigService();
            _onConnect = onConnect;

            LoadSeniorAliases();

            var loaded = _storageService.LoadConnections();
            foreach (var p in loaded)
            {
                SavedProfiles.Add(p);
            }

            if (SavedProfiles.Count == 0)
            {
                var defaultProfile = CreateDefaultProfile();
                SavedProfiles.Add(defaultProfile);
            }

            _selectedProfile = SavedProfiles.FirstOrDefault() ?? CreateDefaultProfile();
            _selectedDatabase = _selectedProfile.Database;

            if (!string.IsNullOrEmpty(_selectedProfile.SeniorAlias))
            {
                _selectedSeniorAlias = SeniorAliases.FirstOrDefault(a => a.Alias.Equals(_selectedProfile.SeniorAlias, StringComparison.OrdinalIgnoreCase));
            }
            else if (SeniorAliases.Count > 0)
            {
                _selectedSeniorAlias = SeniorAliases.FirstOrDefault(a => a.IsDefault) ?? SeniorAliases.First();
                _selectedProfile.SeniorAlias = _selectedSeniorAlias.Alias;
                _selectedProfile.Server = _selectedSeniorAlias.Server;
                _selectedProfile.Port = _selectedSeniorAlias.Port;
                _selectedProfile.Database = _selectedSeniorAlias.DatabaseName;
                _selectedProfile.DatabaseType = _selectedSeniorAlias.DatabaseType;
                _selectedDatabase = _selectedSeniorAlias.DatabaseName;
            }

            DetectLocalServers();

            TestConnectionCommand = new AsyncRelayCommand(ExecuteTestConnectionAsync, () => !IsTesting && SelectedProfile != null);
            ConnectCommand = new AsyncRelayCommand(ExecuteConnectAsync, () => SelectedProfile != null && !IsTesting);
            
            NewProfileCommand = new RelayCommand(() =>
            {
                var newP = CreateDefaultProfile();
                newP.Name = $"Conexão {SavedProfiles.Count + 1}";
                SavedProfiles.Add(newP);
                SelectedProfile = newP;
                _storageService.SaveConnections(SavedProfiles.ToList());
            });

            DeleteProfileCommand = new RelayCommand((param) =>
            {
                var toDelete = param as ConnectionProfile ?? SelectedProfile;
                if (toDelete != null && SavedProfiles.Count > 1)
                {
                    SavedProfiles.Remove(toDelete);
                    SelectedProfile = SavedProfiles.First();
                    _storageService.SaveConnections(SavedProfiles.ToList());
                }
            }, _ => SavedProfiles.Count > 1);

            CloneProfileCommand = new RelayCommand(() =>
            {
                if (SelectedProfile != null)
                {
                    var clone = SelectedProfile.Clone();
                    clone.Name += " (Cópia)";
                    SavedProfiles.Add(clone);
                    SelectedProfile = clone;
                    _storageService.SaveConnections(SavedProfiles.ToList());
                }
            });

            SaveProfilesCommand = new RelayCommand(() =>
            {
                if (SelectedProfile != null)
                {
                    SelectedProfile.UpdateEncryptedPassword();
                    SelectedProfile.UpdateEncryptedSguPassword();
                }
                _storageService.SaveConnections(SavedProfiles.ToList());
                TestResultMessage = "Perfis salvos com sucesso!";
                HasTested = true;
                TestSuccess = true;
            });

            FetchDatabasesCommand = new AsyncRelayCommand(ExecuteFetchDatabasesAsync, () => !IsTesting && SelectedProfile != null);
            DetectServersCommand = new RelayCommand(DetectLocalServers);
            RefreshSeniorConfigCommand = new RelayCommand(LoadSeniorAliases);
            
            BrowseSeniorConfigCommand = new RelayCommand(() =>
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Selecionar arquivo de configuração Senior (senior.cfg)",
                    Filter = "Configuração Senior (senior.cfg)|senior.cfg|Todos os arquivos (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dlg.ShowDialog() == true)
                {
                    var customService = new SeniorConfigService(dlg.FileName);
                    var aliases = customService.LoadAliases();
                    if (aliases.Count > 0)
                    {
                        SeniorAliases.Clear();
                        foreach (var a in aliases) SeniorAliases.Add(a);
                        SelectedSeniorAlias = SeniorAliases.FirstOrDefault(a => a.IsDefault) ?? SeniorAliases.First();
                        OnPropertyChanged(nameof(SeniorStatusSummary));
                        OnPropertyChanged(nameof(IsSeniorInstalled));
                    }
                }
            });
        }

        private ConnectionProfile CreateDefaultProfile()
        {
            var defaultAlias = SeniorAliases.FirstOrDefault(a => a.IsDefault) ?? SeniorAliases.FirstOrDefault();
            return new ConnectionProfile
            {
                Name = defaultAlias != null ? $"Senior ({defaultAlias.Alias})" : "Senior Vetorh",
                SeniorAuthMode = SeniorAuthMode.SeniorSgu,
                SeniorAlias = defaultAlias?.Alias ?? "vetorh",
                DatabaseType = defaultAlias?.DatabaseType ?? DatabaseType.SqlServer,
                Server = defaultAlias?.Server ?? (DetectedServers.FirstOrDefault() ?? "localhost"),
                Port = defaultAlias?.Port ?? 1433,
                Database = defaultAlias?.DatabaseName ?? "senior",
                Username = defaultAlias?.Username ?? "sa",
                Password = defaultAlias?.PlainPassword,
                SguUsername = "senior"
            };
        }

        public void LoadSeniorAliases()
        {
            SeniorAliases.Clear();
            var aliases = _seniorConfigService.LoadAliases();
            foreach (var a in aliases)
            {
                SeniorAliases.Add(a);
            }
            OnPropertyChanged(nameof(SeniorStatusSummary));
            OnPropertyChanged(nameof(IsSeniorInstalled));
        }

        public void DetectLocalServers()
        {
            DetectedServers.Clear();
            DetectedServers.Add(".\\SQLEXPRESS");
            DetectedServers.Add("localhost");
            DetectedServers.Add("(local)");

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var inst = name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                            ? "localhost"
                            : $".\\{name}";

                        if (!DetectedServers.Contains(inst, StringComparer.OrdinalIgnoreCase))
                        {
                            DetectedServers.Add(inst);
                        }
                    }
                }
            }
            catch { }
        }

        private async Task ExecuteTestConnectionAsync()
        {
            if (SelectedProfile == null) return;
            IsTesting = true;
            HasTested = false;
            TestResultMessage = $"Testando conexão com {SelectedProfile.DatabaseType}...";

            try
            {
                var driver = DbDriverFactory.GetDriver(SelectedProfile);
                var (success, msg, latency) = await driver.TestConnectionAsync(SelectedProfile);
                
                if (!success)
                {
                    TestSuccess = false;
                    TestResultMessage = $"❌ Falha na conexão com o banco: {msg}";
                    HasTested = true;
                    return;
                }

                // If SGU Authentication mode is enabled, test SGU user validation
                if (SelectedProfile.SeniorAuthMode == SeniorAuthMode.SeniorSgu)
                {
                    TestResultMessage = $"Conexão física OK ({latency} ms). Validando usuário SGU '{SelectedProfile.SguUsername}'...";
                    var sguResult = await SguAuthenticationService.ValidateSguUserAsync(
                        driver,
                        SelectedProfile,
                        SelectedDatabase,
                        SelectedProfile.SguUsername,
                        SelectedProfile.SguPassword);

                    if (!sguResult.IsSuccess)
                    {
                        TestSuccess = false;
                        TestResultMessage = $"⚠️ Conexão com banco OK ({latency} ms), mas o usuário SGU falhou:\r\n{sguResult.ErrorMessage}";
                        HasTested = true;
                        return;
                    }

                    TestSuccess = true;
                    TestResultMessage = $"✅ Conexão e Autenticação SGU OK ({latency} ms)!\r\nUsuário: {sguResult.UserName} (Cód: {sguResult.UserCode}) • Grupo: {sguResult.GroupName}";
                    HasTested = true;
                }
                else
                {
                    TestSuccess = true;
                    TestResultMessage = $"✅ Conexão direta estabelecida com sucesso ({latency} ms)!";
                    HasTested = true;
                }

                _ = ExecuteFetchDatabasesAsync();
            }
            catch (Exception ex)
            {
                TestSuccess = false;
                TestResultMessage = $"❌ Erro: {ex.Message}";
                HasTested = true;
            }
            finally
            {
                IsTesting = false;
            }
        }

        private async Task ExecuteFetchDatabasesAsync()
        {
            if (SelectedProfile == null) return;
            try
            {
                var driver = DbDriverFactory.GetDriver(SelectedProfile);
                var dbs = await driver.GetDatabasesAsync(SelectedProfile);
                AvailableDatabases.Clear();
                foreach (var db in dbs)
                {
                    AvailableDatabases.Add(db);
                }
                if (AvailableDatabases.Count > 0)
                {
                    if (AvailableDatabases.Contains("senior", StringComparer.OrdinalIgnoreCase))
                    {
                        SelectedDatabase = AvailableDatabases.First(d => d.Equals("senior", StringComparison.OrdinalIgnoreCase));
                        SelectedProfile.Database = SelectedDatabase;
                    }
                    else if (AvailableDatabases.Contains("vetorh", StringComparer.OrdinalIgnoreCase))
                    {
                        SelectedDatabase = AvailableDatabases.First(d => d.Equals("vetorh", StringComparison.OrdinalIgnoreCase));
                        SelectedProfile.Database = SelectedDatabase;
                    }
                }
            }
            catch { }
        }

        private async Task ExecuteConnectAsync()
        {
            if (SelectedProfile == null) return;

            // Validate SGU user before connecting if in SGU mode
            if (SelectedProfile.SeniorAuthMode == SeniorAuthMode.SeniorSgu)
            {
                IsTesting = true;
                var driver = DbDriverFactory.GetDriver(SelectedProfile);
                var sguResult = await SguAuthenticationService.ValidateSguUserAsync(
                    driver,
                    SelectedProfile,
                    SelectedDatabase,
                    SelectedProfile.SguUsername,
                    SelectedProfile.SguPassword);
                IsTesting = false;

                if (!sguResult.IsSuccess)
                {
                    TestSuccess = false;
                    TestResultMessage = $"Não foi possível conectar: {sguResult.ErrorMessage}";
                    HasTested = true;
                    return;
                }
            }

            SelectedProfile.UpdateEncryptedPassword();
            SelectedProfile.UpdateEncryptedSguPassword();
            _storageService.SaveConnections(SavedProfiles.ToList());
            
            var db = SelectedProfile.DatabaseType == DatabaseType.Oracle 
                ? (SelectedProfile.ServiceName ?? SelectedProfile.Database) 
                : SelectedDatabase;

            _onConnect(SelectedProfile, db);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
