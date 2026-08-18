using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace NertyDb.Models
{
    public enum DatabaseType
    {
        SqlServer = 0,
        Oracle = 1
    }

    public enum AuthenticationType
    {
        SqlServer = 0,
        WindowsIntegrated = 1
    }

    public enum SeniorAuthMode
    {
        SeniorSgu = 0,      // Autenticação via Usuário Senior (SGU)
        DirectDatabase = 1  // Autenticação Direta de Banco (DBA / SA / Windows)
    }

    public class ConnectionProfile
    {
        private string? _plainPassword;
        private string? _plainSguPassword;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Novo Servidor";
        public DatabaseType DatabaseType { get; set; } = DatabaseType.SqlServer;
        public string Server { get; set; } = ".\\SQLEXPRESS";
        public int Port { get; set; } = 1433;
        public string Database { get; set; } = "senior";
        public string ServiceName { get; set; } = "ORCL"; // For Oracle (SID or Service Name)
        public AuthenticationType AuthType { get; set; } = AuthenticationType.SqlServer;
        public string Username { get; set; } = "sa";
        
        // SGU Senior Authentication properties
        public SeniorAuthMode SeniorAuthMode { get; set; } = SeniorAuthMode.SeniorSgu;
        public string? SeniorAlias { get; set; } = "vetorh";
        public string SguUsername { get; set; } = "senior";
        
        [JsonPropertyName("EncryptedSguPassword")]
        public string? EncryptedSguPassword { get; set; }

        // Encrypted password in JSON, plain in memory
        [JsonPropertyName("EncryptedPassword")]
        public string? EncryptedPassword { get; set; }

        public bool SavePassword { get; set; } = true;
        public bool TrustServerCertificate { get; set; } = true;
        public bool Encrypt { get; set; } = false;
        public int ConnectionTimeout { get; set; } = 15;
        public int CommandTimeout { get; set; } = 30;
        public string? ColorTag { get; set; } = "#3B82F6";

        [JsonIgnore]
        public string? Password
        {
            get
            {
                if (_plainPassword != null) return _plainPassword;
                if (string.IsNullOrEmpty(EncryptedPassword)) return null;
                try
                {
                    byte[] encryptedBytes = Convert.FromBase64String(EncryptedPassword);
                    byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    _plainPassword = Encoding.UTF8.GetString(decryptedBytes);
                    return _plainPassword;
                }
                catch
                {
                    try
                    {
                        _plainPassword = Encoding.UTF8.GetString(Convert.FromBase64String(EncryptedPassword));
                        return _plainPassword;
                    }
                    catch
                    {
                        _plainPassword = EncryptedPassword;
                        return _plainPassword;
                    }
                }
            }
            set
            {
                _plainPassword = value;
                UpdateEncryptedPassword();
            }
        }

        [JsonIgnore]
        public string? SguPassword
        {
            get
            {
                if (_plainSguPassword != null) return _plainSguPassword;
                if (string.IsNullOrEmpty(EncryptedSguPassword)) return null;
                try
                {
                    byte[] encryptedBytes = Convert.FromBase64String(EncryptedSguPassword);
                    byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    _plainSguPassword = Encoding.UTF8.GetString(decryptedBytes);
                    return _plainSguPassword;
                }
                catch
                {
                    try
                    {
                        _plainSguPassword = Encoding.UTF8.GetString(Convert.FromBase64String(EncryptedSguPassword));
                        return _plainSguPassword;
                    }
                    catch
                    {
                        _plainSguPassword = EncryptedSguPassword;
                        return _plainSguPassword;
                    }
                }
            }
            set
            {
                _plainSguPassword = value;
                UpdateEncryptedSguPassword();
            }
        }

        public void UpdateEncryptedPassword()
        {
            if (string.IsNullOrEmpty(_plainPassword))
            {
                EncryptedPassword = null;
            }
            else if (SavePassword)
            {
                try
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(_plainPassword);
                    byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                    EncryptedPassword = Convert.ToBase64String(encryptedBytes);
                }
                catch
                {
                    EncryptedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(_plainPassword));
                }
            }
            else
            {
                EncryptedPassword = null;
            }
        }

        public void UpdateEncryptedSguPassword()
        {
            if (string.IsNullOrEmpty(_plainSguPassword))
            {
                EncryptedSguPassword = null;
            }
            else if (SavePassword)
            {
                try
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(_plainSguPassword);
                    byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                    EncryptedSguPassword = Convert.ToBase64String(encryptedBytes);
                }
                catch
                {
                    EncryptedSguPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(_plainSguPassword));
                }
            }
            else
            {
                EncryptedSguPassword = null;
            }
        }

        public string BuildConnectionString(string? specificDatabase = null)
        {
            if (DatabaseType == DatabaseType.Oracle)
            {
                return BuildOracleConnectionString();
            }

            return BuildSqlServerConnectionString(specificDatabase);
        }

        private string BuildSqlServerConnectionString(string? specificDatabase)
        {
            var db = !string.IsNullOrWhiteSpace(specificDatabase) ? specificDatabase : Database;
            
            // Named instances (e.g. .\SQLEXPRESS, localhost\SQLEXPRESS) shouldn't append port 1433
            string serverAddress;
            if (Server.Contains("\\") || Server.Contains(","))
            {
                serverAddress = Server;
            }
            else if (Port > 0 && Port != 1433)
            {
                serverAddress = $"{Server},{Port}";
            }
            else
            {
                serverAddress = Server;
            }

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = serverAddress,
                InitialCatalog = string.IsNullOrWhiteSpace(db) ? "master" : db,
                ConnectTimeout = ConnectionTimeout > 0 ? ConnectionTimeout : 15,
                TrustServerCertificate = TrustServerCertificate,
                Encrypt = Encrypt ? Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory : Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional,
                ApplicationName = "NertyDb Senior Client",
                Pooling = true
            };

            if (AuthType == AuthenticationType.WindowsIntegrated)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = Username;
                builder.Password = Password ?? string.Empty;
            }

            return builder.ConnectionString;
        }

        private string BuildOracleConnectionString()
        {
            var port = Port > 0 ? Port : 1521;
            var service = !string.IsNullOrWhiteSpace(ServiceName) ? ServiceName : (!string.IsNullOrWhiteSpace(Database) ? Database : "XE");
            
            var dataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={Server})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={service})))";
            var pwd = Password ?? string.Empty;

            var builder = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder
            {
                DataSource = dataSource,
                UserID = Username,
                Password = pwd,
                ConnectionTimeout = ConnectionTimeout > 0 ? ConnectionTimeout : 15,
                Pooling = true,
                MinPoolSize = 1,
                MaxPoolSize = 100
            };

            return builder.ConnectionString;
        }

        public ConnectionProfile Clone()
        {
            var clone = (ConnectionProfile)this.MemberwiseClone();
            clone.Id = Guid.NewGuid().ToString("N");
            return clone;
        }

        public override string ToString() => $"{Name} ({DatabaseType} - {Server}/{Database})";
    }
}
