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

        [Fact]
        public void SeniorCryptoService_EncryptUserPassword_MatchesKnownVectors()
        {
            byte[] expectedSenior = new byte[] { 0x90, 0x9A, 0x4E, 0xA2, 0x97, 0x49, 0x8C, 0x91, 0x96, 0x8D };
            byte[] actualSenior = SeniorCryptoService.EncryptUserPassword("SENIOR", "senior");
            Assert.Equal(expectedSenior, actualSenior);

            byte[] expectedWrong = new byte[] { 0x8B, 0x4E, 0xA2, 0x97, 0x49, 0x8B };
            byte[] actualWrong = SeniorCryptoService.EncryptUserPassword("SENIOR", "tt");
            Assert.Equal(expectedWrong, actualWrong);

            Assert.NotEqual(expectedSenior, actualWrong);
        }

        [Fact]
        public void SeniorCryptoService_DecodeUserDataAndValidatePassword()
        {
            // Real sample from senior database for SENIOR user
            string dat1 = "%!!!!!:425Z*4V)!*$O%:IP)Y5!'=W6O;7^S!!K1GE[CFUG-E:;.!!!!!!!!!!!!!!!!!!!!!!$CI<0F.*4G1#!O%P#UCO2!!!!!!!%!!!!\"!!!!!1!!!!%!!!!\"!!!!!1!!!!%!!!!\"!!$!QD_G3H;GZ%!!!#!O%P#UCO2!!!!!!!!!!!!!!!!!!!!!!!!!!!$;\"A`^+E)";
            byte[] userStream = SeniorCryptoService.DecodeUserData(new[] { dat1 });
            Assert.NotEmpty(userStream);

            // Correct password
            bool valid = SeniorCryptoService.ValidateSguPassword("SENIOR", "senior", userStream);
            Assert.True(valid);

            // Incorrect password
            bool invalid = SeniorCryptoService.ValidateSguPassword("SENIOR", "tt", userStream);
            Assert.False(invalid);
        }

        [Fact]
        public async Task SguAuthenticationService_ValidatesRealCredentials()
        {
            var profile = new ConnectionProfile
            {
                DatabaseType = DatabaseType.SqlServer,
                Server = "127.0.0.1",
                Port = 1433,
                Database = "senior",
                AuthType = AuthenticationType.SqlServer,
                Username = "sa",
                Password = "12345678",
                SeniorAuthMode = SeniorAuthMode.SeniorSgu
            };

            var driver = NertyDb.Data.DbDriverFactory.GetDriver(profile);
            var (canConnect, _, _) = await driver.TestConnectionAsync(profile);
            if (!canConnect) return; // Skip if local SQL instance is not running

            // 1. Correct credentials: SENIOR / senior
            var resOk = await SguAuthenticationService.ValidateSguUserAsync(driver, profile, "senior", "senior", "senior");
            Assert.True(resOk.IsSuccess);
            Assert.Equal("SENIOR", resOk.UserName.ToUpperInvariant());

            // 2. Incorrect credentials: SENIOR / tt
            var resWrong = await SguAuthenticationService.ValidateSguUserAsync(driver, profile, "senior", "senior", "tt");
            Assert.False(resWrong.IsSuccess);
            Assert.Contains("Senha inválida", resWrong.ErrorMessage);

            // 3. Inexistent user: usuario_inexistente_xyz
            var resNotFound = await SguAuthenticationService.ValidateSguUserAsync(driver, profile, "senior", "usuario_inexistente_xyz", "123");
            Assert.False(resNotFound.IsSuccess);
            Assert.Contains("não encontrado", resNotFound.ErrorMessage);
        }
    }
}
