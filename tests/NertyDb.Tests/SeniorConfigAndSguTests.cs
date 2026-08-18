using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NertyDb.Models;
using NertyDb.Services;
using Xunit;

namespace NertyDb.Tests
{
    public class SeniorConfigAndSguTests
    {
        [Fact]
        public void SeniorConfigService_CanParseSeniorCfgXml()
        {
            var sampleXml = @"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<environment>
  <configuration>
    <com>
      <senior>
        <default_database>
          <id>vetorh</id>
        </default_database>
        <database>
          <vetorh>
            <db_kind>mssql_2019</db_kind>
            <database>senior</database>
            <databasename>senior</databasename>
            <serverJDBC>NC-PC13</serverJDBC>
            <portJDBC>1433</portJDBC>
            <username>sa</username>
            <password>XSn1fmF1w1Tqix5C</password>
          </vetorh>
          <sapiens_oracle>
            <db_kind>oracle_19c</db_kind>
            <database>sapiens</database>
            <serverJDBC>SRV-ORA01</serverJDBC>
            <portJDBC>1521</portJDBC>
            <username>sapiens</username>
          </sapiens_oracle>
        </database>
      </senior>
    </com>
  </configuration>
</environment>";

            var tempFile = Path.Combine(Path.GetTempPath(), $"test_senior_cfg_{Guid.NewGuid():N}.cfg");
            File.WriteAllText(tempFile, sampleXml);

            try
            {
                var service = new SeniorConfigService(tempFile);
                var aliases = service.LoadAliases();

                Assert.Equal(2, aliases.Count);

                var vetorh = aliases.FirstOrDefault(a => a.Alias == "vetorh");
                Assert.NotNull(vetorh);
                Assert.Equal(DatabaseType.SqlServer, vetorh.DatabaseType);
                Assert.Equal("NC-PC13", vetorh.Server);
                Assert.Equal(1433, vetorh.Port);
                Assert.Equal("senior", vetorh.DatabaseName);
                Assert.Equal("sa", vetorh.Username);
                Assert.True(vetorh.IsDefault);

                var oracle = aliases.FirstOrDefault(a => a.Alias == "sapiens_oracle");
                Assert.NotNull(oracle);
                Assert.Equal(DatabaseType.Oracle, oracle.DatabaseType);
                Assert.Equal("SRV-ORA01", oracle.Server);
                Assert.Equal(1521, oracle.Port);
                Assert.Equal("sapiens", oracle.DatabaseName);
                Assert.False(oracle.IsDefault);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SeniorConfigService_HandlesMissingFileGracefully()
        {
            var service = new SeniorConfigService(@"C:\NonExistentDirectory_12345\senior.cfg");
            var aliases = service.LoadAliases();

            Assert.NotNull(aliases);
            Assert.Empty(aliases);
            Assert.False(service.IsSeniorInstalled);
        }

        [Fact]
        public void ConnectionProfile_SupportsSeniorSguAuthMode()
        {
            var profile = new ConnectionProfile
            {
                Name = "Senior HCM",
                SeniorAuthMode = SeniorAuthMode.SeniorSgu,
                SeniorAlias = "vetorh",
                SguUsername = "analista.senior",
                SguPassword = "secretPassword123"
            };

            profile.UpdateEncryptedSguPassword();

            Assert.Equal(SeniorAuthMode.SeniorSgu, profile.SeniorAuthMode);
            Assert.Equal("analista.senior", profile.SguUsername);
            Assert.Equal("secretPassword123", profile.SguPassword);
            Assert.NotNull(profile.EncryptedSguPassword);

            // Test clone
            var clone = profile.Clone();
            Assert.Equal(profile.SeniorAuthMode, clone.SeniorAuthMode);
            Assert.Equal(profile.SeniorAlias, clone.SeniorAlias);
            Assert.Equal(profile.SguUsername, clone.SguUsername);
        }

        [Fact]
        public async Task SguAuthenticationService_ValidatesEmptyUsername()
        {
            var profile = new ConnectionProfile();
            var dummyDriver = NertyDb.Data.DbDriverFactory.GetDriver(profile);

            var result = await SguAuthenticationService.ValidateSguUserAsync(dummyDriver, profile, "senior", "", "");
            Assert.False(result.IsSuccess);
            Assert.Contains("Informe o usuário", result.ErrorMessage);
        }

        [Fact]
        public void SeniorCryptoService_CanDecryptDatabasePassword()
        {
            // Senior's actual encrypted password in senior.cfg
            var encrypted = "XSn1fmF1w1Tqix5C";
            var decrypted = SeniorCryptoService.Decrypt(encrypted, SeniorCryptoService.DbKey);

            Assert.Equal("12345678", decrypted);
        }

        [Fact]
        public void SeniorCryptoService_EncryptDecryptRoundtrip()
        {
            var original = "SenhaSenior@2026";
            var enc = SeniorCryptoService.Encrypt(original, SeniorCryptoService.DbKey);
            Assert.NotNull(enc);

            var dec = SeniorCryptoService.Decrypt(enc, SeniorCryptoService.DbKey);
            Assert.Equal(original, dec);
        }
    }
}
