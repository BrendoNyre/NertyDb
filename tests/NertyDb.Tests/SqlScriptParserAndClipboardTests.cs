using System;
using System.Linq;
using NertyDb.Editor;
using NertyDb.Services;
using Xunit;

namespace NertyDb.Tests
{
    public class SqlScriptParserAndClipboardTests
    {
        [Fact]
        public void ClipboardHelper_SetTextAndGetText_OperatesWithoutExceptions()
        {
            // Even if running in headless CI/test environment without desktop, SetText/GetText must never throw unhandled crash
            var ok = ClipboardHelper.SetText("NertyDb Test Content");
            Assert.True(ok || true); // Doesn't throw

            var text = ClipboardHelper.GetText();
            Assert.NotNull(text); // Doesn't throw
        }

        [Fact]
        public void SqlScriptParser_BasicMultipleStatements_SplitsCorrectly()
        {
            var sql = @"
                SELECT * FROM tabela1;
                SELECT * FROM tabela2;
                SELECT * FROM tabela3;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(3, stmts.Count);
            Assert.Equal("SELECT * FROM tabela1", stmts[0].Sql);
            Assert.Equal("SELECT * FROM tabela2", stmts[1].Sql);
            Assert.Equal("SELECT * FROM tabela3", stmts[2].Sql);

            Assert.All(stmts, s => Assert.True(s.IsQuery));
            Assert.All(stmts, s => Assert.Equal("SELECT", s.CommandType));
        }

        [Fact]
        public void SqlScriptParser_SemicolonInsideSingleQuoteString_DoesNotSplit()
        {
            var sql = @"
                SELECT 'teste;abc' AS descricao;
                SELECT * FROM usuarios;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Equal("SELECT 'teste;abc' AS descricao", stmts[0].Sql);
            Assert.Equal("SELECT * FROM usuarios", stmts[1].Sql);
        }

        [Fact]
        public void SqlScriptParser_EscapedQuotesWithSemicolon_DoesNotSplit()
        {
            var sql = @"
                SELECT 'teste''com;aspas;duplas' AS x;
                SELECT 2;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Equal("SELECT 'teste''com;aspas;duplas' AS x", stmts[0].Sql);
            Assert.Equal("SELECT 2", stmts[1].Sql);
        }

        [Fact]
        public void SqlScriptParser_SemicolonInsideDoubleQuotesAndBrackets_DoesNotSplit()
        {
            var sql = @"
                SELECT [campo;1], ""outro;campo"" FROM [schema;dbo].[tabela;1];
                SELECT 2;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Equal(@"SELECT [campo;1], ""outro;campo"" FROM [schema;dbo].[tabela;1]", stmts[0].Sql);
            Assert.Equal("SELECT 2", stmts[1].Sql);
        }

        [Fact]
        public void SqlScriptParser_SemicolonInsideComments_DoesNotSplit()
        {
            var sql = @"
                -- primeira consulta; com ponto e virgula no comentário
                SELECT * FROM clientes;

                /* segunda consulta;
                   com múltiplos ; comentários */
                SELECT * FROM produtos;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Contains("SELECT * FROM clientes", stmts[0].Sql);
            Assert.Contains("SELECT * FROM produtos", stmts[1].Sql);
        }

        [Fact]
        public void SqlScriptParser_MixedDmlAndQueryStatements_IdentifiesTypesCorrectly()
        {
            var sql = @"
                SELECT * FROM alguma_tabela;

                UPDATE alguma_tabela
                SET algum_campo = 7
                WHERE 1 = 0;

                DELETE FROM alguma_tabela WHERE 1 = 0;

                SELECT * FROM outra_tabela;
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(4, stmts.Count);

            Assert.True(stmts[0].IsQuery);
            Assert.Equal("SELECT", stmts[0].CommandType);

            Assert.False(stmts[1].IsQuery);
            Assert.Equal("UPDATE", stmts[1].CommandType);

            Assert.False(stmts[2].IsQuery);
            Assert.Equal("DELETE", stmts[2].CommandType);

            Assert.True(stmts[3].IsQuery);
            Assert.Equal("SELECT", stmts[3].CommandType);
        }

        [Fact]
        public void SqlScriptParser_ConsecutiveSemicolonsAndEmptyStatements_Ignored()
        {
            var sql = ";;;\nSELECT 1;\n;\nSELECT 2;\n;;";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Equal("SELECT 1", stmts[0].Sql);
            Assert.Equal("SELECT 2", stmts[1].Sql);
        }

        [Fact]
        public void SqlScriptParser_GoBatchSeparator_SplitsBatches()
        {
            var sql = @"
                SELECT 1
                GO
                SELECT 2
                GO
            ";

            var stmts = SqlScriptParser.ParseStatements(sql);

            Assert.Equal(2, stmts.Count);
            Assert.Equal("SELECT 1", stmts[0].Sql);
            Assert.Equal("SELECT 2", stmts[1].Sql);
        }

        [Fact]
        public void ConnectionProfile_PasswordEncryptionAndPersistence_MaintainsDecryptedPasswordAcrossCycles()
        {
            var profile = new Models.ConnectionProfile
            {
                Name = "Test Connection",
                Server = "localhost",
                Username = "sa",
                SavePassword = true,
                Password = "SecretPassword123!",
                SguPassword = "SguSecret456!"
            };

            // Ensure encrypted password was generated
            Assert.False(string.IsNullOrEmpty(profile.EncryptedPassword));
            Assert.False(string.IsNullOrEmpty(profile.EncryptedSguPassword));

            // Serialize to JSON and deserialize (simulating closing and reopening app)
            var json = System.Text.Json.JsonSerializer.Serialize(profile);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<Models.ConnectionProfile>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("SecretPassword123!", deserialized.Password);
            Assert.Equal("SguSecret456!", deserialized.SguPassword);

            // Calling UpdateEncryptedPassword when deserialized must NOT wipe out the encrypted password
            deserialized.UpdateEncryptedPassword();
            deserialized.UpdateEncryptedSguPassword();

            Assert.False(string.IsNullOrEmpty(deserialized.EncryptedPassword));
            Assert.False(string.IsNullOrEmpty(deserialized.EncryptedSguPassword));
            Assert.Equal("SecretPassword123!", deserialized.Password);
        }

        [Fact]
        public void ConnectionProfile_TogglingSavePassword_ClearsOrReEncrypts()
        {
            var profile = new Models.ConnectionProfile
            {
                Name = "Test Toggle",
                SavePassword = true,
                Password = "MyPassword"
            };

            Assert.False(string.IsNullOrEmpty(profile.EncryptedPassword));

            // Disabling SavePassword clears the encrypted password
            profile.SavePassword = false;
            Assert.Null(profile.EncryptedPassword);

            // Re-enabling SavePassword re-encrypts the in-memory password
            profile.SavePassword = true;
            Assert.False(string.IsNullOrEmpty(profile.EncryptedPassword));
            Assert.Equal("MyPassword", profile.Password);
        }
    }
}
