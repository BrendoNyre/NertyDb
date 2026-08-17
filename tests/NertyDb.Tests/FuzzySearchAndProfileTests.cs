using NertyDb.Data;
using NertyDb.Models;
using NertyDb.ViewModels;
using Xunit;

namespace NertyDb.Tests
{
    public class FuzzySearchTests
    {
        [Theory]
        [InlineData("R034FUN", "r034", true)]
        [InlineData("R034FUN", "fun", true)]
        [InlineData("R070ACC", "70acc", true)]
        [InlineData("R070ACC", "r070", true)]
        [InlineData("R070CON", "r70con", true)]
        [InlineData("R034CRA", "cracha", false)]
        [InlineData("R034CRA", "cra", true)]
        [InlineData("dbo", "dbo", true)]
        [InlineData("BatidasPonto", "ponto", true)]
        public void IsFuzzyMatch_ShouldMatchPatternsCorrectly(string text, string pattern, bool expected)
        {
            var match = SchemaTreeViewModel.IsFuzzyMatch(text, pattern);
            Assert.Equal(expected, match);
        }
    }

    public class ConnectionProfileTests
    {
        [Fact]
        public void BuildConnectionString_ShouldCreateCorrectSqlServerAuthString()
        {
            var profile = new ConnectionProfile
            {
                DatabaseType = DatabaseType.SqlServer,
                Server = "192.168.1.100",
                Port = 1433,
                Database = "Vetorh",
                AuthType = AuthenticationType.SqlServer,
                Username = "senior",
                Password = "SecretPassword123",
                TrustServerCertificate = true,
                Encrypt = false
            };

            var connStr = profile.BuildConnectionString();

            Assert.Contains("Data Source=192.168.1.100", connStr);
            Assert.Contains("Initial Catalog=Vetorh", connStr);
            Assert.Contains("User ID=senior", connStr);
            Assert.Contains("Password=SecretPassword123", connStr);
            Assert.Contains("Trust Server Certificate=True", connStr);
        }

        [Fact]
        public void BuildConnectionString_ShouldCreateCorrectOracleAuthString()
        {
            var profile = new ConnectionProfile
            {
                DatabaseType = DatabaseType.Oracle,
                Server = "192.168.1.200",
                Port = 1521,
                ServiceName = "SENIORPDB",
                Username = "VETORH",
                Password = "OraclePassword456"
            };

            var connStr = profile.BuildConnectionString();

            Assert.Contains("USER ID=VETORH", connStr, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PASSWORD=OraclePassword456", connStr, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HOST=192.168.1.200", connStr, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PORT=1521", connStr, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SERVICE_NAME=SENIORPDB", connStr, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DbDriverFactory_ShouldResolveCorrectDrivers()
        {
            var sqlProfile = new ConnectionProfile { DatabaseType = DatabaseType.SqlServer };
            var oracleProfile = new ConnectionProfile { DatabaseType = DatabaseType.Oracle };

            var sqlDriver = DbDriverFactory.GetDriver(sqlProfile);
            var oracleDriver = DbDriverFactory.GetDriver(oracleProfile);

            Assert.IsType<SqlServerDriver>(sqlDriver);
            Assert.IsType<OracleDriver>(oracleDriver);
        }

        [Fact]
        public void Password_DPAPI_Roundtrip_ShouldWork()
        {
            var profile = new ConnectionProfile
            {
                SavePassword = true
            };

            profile.Password = "MinhaSenhaSenior!@#2026";

            // Encrypted string should not be plain text
            Assert.NotNull(profile.EncryptedPassword);
            Assert.DoesNotContain("MinhaSenhaSenior", profile.EncryptedPassword);

            // Reading Password property should decrypt it
            Assert.Equal("MinhaSenhaSenior!@#2026", profile.Password);
        }
    }
}
