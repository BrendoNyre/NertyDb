using System;
using System.Collections.Generic;
using System.Linq;
using NertyDb.Editor;
using NertyDb.Models;
using NertyDb.Services;
using Xunit;

namespace NertyDb.Tests
{
    public class SqlAliasAndJoinTests
    {
        [Fact]
        public void SqlAliasGenerator_ShouldGenerateIntelligentAliases()
        {
            var aliasFun = SqlAliasGenerator.GenerateTableAlias("R034FUN");
            Assert.Equal("rf", aliasFun);

            var aliasAcc = SqlAliasGenerator.GenerateTableAlias("R070ACC");
            Assert.Equal("ra", aliasAcc);

            var aliasColab = SqlAliasGenerator.GenerateTableAlias("CONTROLE_PONTO");
            Assert.Equal("cp", aliasColab);

            var aliasFunc = SqlAliasGenerator.GenerateTableAlias("FUNCIONARIOS");
            Assert.Equal("f", aliasFunc);
        }

        [Fact]
        public void SqlAliasGenerator_ShouldHandleCollisionsWithNumericSuffix()
        {
            var existing = new List<string> { "rf", "cp" };
            var alias = SqlAliasGenerator.GenerateTableAlias("R034FUN", existing);
            Assert.Equal("rf2", alias);

            existing.Add("rf2");
            var alias3 = SqlAliasGenerator.GenerateTableAlias("R034FUN", existing);
            Assert.Equal("rf3", alias3);
        }

        [Fact]
        public void SqlAliasGenerator_ShouldExtractAliasesFromMultipleJoins()
        {
            var sql = @"
                SELECT f.NOMFUN, a.DATACC
                FROM R034FUN f
                INNER JOIN R070ACC a ON f.NUMEMP = a.NUMEMP AND f.NUMCAD = a.NUMCAD
                LEFT JOIN CONTROLE_PONTO cp ON a.NUMCAD = cp.NUMCAD;";

            var map = SqlAliasGenerator.ExtractTableAliases(sql);

            Assert.Equal("R034FUN", map["f"]);
            Assert.Equal("R070ACC", map["a"]);
            Assert.Equal("CONTROLE_PONTO", map["cp"]);
        }

        [Fact]
        public void SqlCompletionProvider_ShouldSuggestColumnsFromRespectiveAliasesInMultiJoinQuery()
        {
            var profile = new ConnectionProfile { Id = $"test_{Guid.NewGuid():N}", Database = "senior" };
            var cache = MetadataCacheService.Instance;

            // Populate mock cache for R034FUN and R070ACC
            var funMeta = new TableMetadata
            {
                Schema = "dbo",
                Name = "R034FUN",
                Columns = new List<ColumnMetadata>
                {
                    new ColumnMetadata { Name = "NUMEMP", DataType = "smallint", IsPrimaryKey = true },
                    new ColumnMetadata { Name = "NUMCAD", DataType = "int", IsPrimaryKey = true },
                    new ColumnMetadata { Name = "NOMFUN", DataType = "varchar", MaxLength = 60 }
                }
            };

            var accMeta = new TableMetadata
            {
                Schema = "dbo",
                Name = "R070ACC",
                Columns = new List<ColumnMetadata>
                {
                    new ColumnMetadata { Name = "NUMEMP", DataType = "smallint", IsPrimaryKey = true },
                    new ColumnMetadata { Name = "DATACC", DataType = "datetime", IsPrimaryKey = true },
                    new ColumnMetadata { Name = "HORACC", DataType = "smallint" },
                    new ColumnMetadata { Name = "ORIBAT", DataType = "char", MaxLength = 1 }
                }
            };

            cache.SetCachedTableDetails(profile.Id, "senior", "dbo", "R034FUN", funMeta);
            cache.SetCachedTableDetails(profile.Id, "senior", "dbo", "R070ACC", accMeta);

            var queryWithJoins = @"
                SELECT f.
                FROM R034FUN f
                INNER JOIN R070ACC a ON f.NUMEMP = a.NUMEMP;";

            // Test typing "f."
            int caretOffsetF = queryWithJoins.IndexOf("SELECT f.") + "SELECT f.".Length;
            var contextF = SqlCompletionProvider.GetCompletionContext(queryWithJoins, caretOffsetF, profile, "senior");

            Assert.NotEmpty(contextF.Items);
            var colNamesF = contextF.Items.Select(i => i.Text).ToList();
            Assert.Contains("NOMFUN", colNamesF);
            Assert.DoesNotContain("HORACC", colNamesF); // HORACC belongs to 'a', not 'f'

            // Test typing "a."
            var queryWithA = @"
                SELECT a.
                FROM R034FUN f
                INNER JOIN R070ACC a ON f.NUMEMP = a.NUMEMP;";

            int caretOffsetA = queryWithA.IndexOf("SELECT a.") + "SELECT a.".Length;
            var contextA = SqlCompletionProvider.GetCompletionContext(queryWithA, caretOffsetA, profile, "senior");

            Assert.NotEmpty(contextA.Items);
            var colNamesA = contextA.Items.Select(i => i.Text).ToList();
            Assert.Contains("HORACC", colNamesA);
            Assert.Contains("ORIBAT", colNamesA);
            Assert.DoesNotContain("NOMFUN", colNamesA); // NOMFUN belongs to 'f', not 'a'
        }
    }
}
