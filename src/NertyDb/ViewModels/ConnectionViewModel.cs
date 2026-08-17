using System;
using System.Collections.ObjectModel;
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
        private readonly Action<ConnectionProfile, string> _onConnect;

        private ConnectionProfile _selectedProfile;
        private bool _isTesting;
        private string _testResultMessage = string.Empty;
        private bool _testSuccess;
        private bool _hasTested;
        private string _selectedDatabase = string.Empty;
        private bool _showPassword;
        private ObservableCollection<string> _availableDatabases = new();
        private ObservableCollection<string> _detectedServers = new();

        public ObservableCollection<ConnectionProfile> SavedProfiles { get; } = new();

        public ObservableCollection<DatabaseType> AvailableDatabaseTypes { get; } = new()
        {
            DatabaseType.SqlServer,
            DatabaseType.Oracle
        };

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
                    OnPropertyChanged(nameof(IsWindowsAuth));
                    OnPropertyChanged(nameof(IsSqlAuth));
                    OnPropertyChanged(nameof(IsSqlServer));
                    OnPropertyChanged(nameof(IsOracle));
                    OnPropertyChanged(nameof(SelectedDatabaseType));
                    OnPropertyChanged(nameof(CurrentPassword));
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
                        if (string.IsNullOrWhiteSpace(SelectedProfile.Database)) SelectedProfile.Database = "master";
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

        public event EventHandler? RequestClose;

        public ConnectionViewModel(
            IDbDriver initialDriver,
            StorageService storageService,
            Action<ConnectionProfile, string> onConnect)
        {
            _storageService = storageService;
            _onConnect = onConnect;

            var loaded = _storageService.LoadConnections();
            foreach (var p in loaded)
            {
                SavedProfiles.Add(p);
            }

            _selectedProfile = SavedProfiles.FirstOrDefault() ?? new ConnectionProfile();
            _selectedDatabase = _selectedProfile.Database;

            DetectLocalServers();

            TestConnectionCommand = new AsyncRelayCommand(ExecuteTestConnectionAsync, () => !IsTesting && SelectedProfile != null);
            ConnectCommand = new RelayCommand(ExecuteConnect, () => SelectedProfile != null && !IsTesting);
            
            NewProfileCommand = new RelayCommand(() =>
            {
                var newP = new ConnectionProfile
                {
                    Name = $"Conexão {SavedProfiles.Count + 1}",
                    DatabaseType = DatabaseType.SqlServer,
                    Server = DetectedServers.FirstOrDefault() ?? ".\\SQLEXPRESS",
                    Port = 1433,
                    Database = "senior",
                    Username = "sa"
                };
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
                }
                _storageService.SaveConnections(SavedProfiles.ToList());
                TestResultMessage = "Perfis salvos com sucesso!";
                HasTested = true;
                TestSuccess = true;
            });

            FetchDatabasesCommand = new AsyncRelayCommand(ExecuteFetchDatabasesAsync, () => !IsTesting && SelectedProfile != null);
            DetectServersCommand = new RelayCommand(DetectLocalServers);
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
                TestSuccess = success;
                TestResultMessage = msg;
                HasTested = true;

                if (success)
                {
                    _ = ExecuteFetchDatabasesAsync();
                }
            }
            catch (Exception ex)
            {
                TestSuccess = false;
                TestResultMessage = $"Erro: {ex.Message}";
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

        private void ExecuteConnect()
        {
            if (SelectedProfile == null) return;
            SelectedProfile.UpdateEncryptedPassword();
            _storageService.SaveConnections(SavedProfiles.ToList());
            var db = SelectedProfile.DatabaseType == DatabaseType.Oracle ? (SelectedProfile.ServiceName ?? SelectedProfile.Database) : SelectedDatabase;
            _onConnect(SelectedProfile, db);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
