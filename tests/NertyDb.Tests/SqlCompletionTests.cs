using System;
using System.Collections.Generic;
using System.Linq;
using NertyDb.Editor;
using NertyDb.Models;
using NertyDb.Services;
using Xunit;

namespace NertyDb.Tests
{
    public class SqlCompletionTests
    {
        [Fact]
        public void SqlCompletionProvider_ShouldSuggestTables_WithAutoAlias_AndNoSqlKeywords()
        {
            var profile = new ConnectionProfile { Id = $"test_{Guid.NewGuid():N}", Database = "senior" };
            var cache = MetadataCacheService.Instance;

            var tables = new List<TableMetadata>
            {
                new TableMetadata { Schema = "dbo", Name = "R034FUN" },
                new TableMetadata { Schema = "dbo", Name = "R070ACC" },
                new TableMetadata { Schema = "dbo", Name = "R034CRA" },
                new TableMetadata { Schema = "dbo", Name = "V_COLABORADORES", IsView = true }
            };

            cache.SetCachedTables(profile.Id, "senior", tables);

            var sql = "SELECT * FROM R03";
            var context = SqlCompletionProvider.GetCompletionContext(sql, sql.Length, profile, "senior");

            Assert.NotEmpty(context.Items);
            Assert.Equal("R03", context.CurrentWord);
            Assert.Equal(14, context.TokenStartOffset); // "SELECT * FROM " has length 14, where 'R' starts

            var itemTexts = context.Items.Select(s => s.Text).ToList();
            Assert.Contains("R034FUN rf", itemTexts);
            Assert.Contains("R034CRA rc", itemTexts);

            // Verify SQL keywords are strictly excluded
            Assert.DoesNotContain("SELECT", itemTexts);
            Assert.DoesNotContain("FROM", itemTexts);
            Assert.DoesNotContain("WHERE", itemTexts);
        }

        [Fact]
        public void SqlCompletionProvider_ShouldSuggestColumns_WithExactTokenOffset_AfterTableDot()
        {
            var profile = new ConnectionProfile { Id = $"test_{Guid.NewGuid():N}", Database = "senior" };
            var cache = MetadataCacheService.Instance;

            var tables = new List<TableMetadata>
            {
                new TableMetadata { Schema = "dbo", Name = "R034FUN" }
            };

            cache.SetCachedTables(profile.Id, "senior", tables);

            var tableMeta = new TableMetadata
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

            cache.SetCachedTableDetails(profile.Id, "senior", "dbo", "R034FUN", tableMeta);

            var sql = "SELECT R034FUN.NO";
            var context = SqlCompletionProvider.GetCompletionContext(sql, sql.Length, profile, "senior");

            Assert.NotEmpty(context.Items);
            Assert.Equal("NO", context.CurrentWord);
            Assert.Equal(15, context.TokenStartOffset); // "SELECT R034FUN." has length 15, where 'N' starts
            Assert.Contains(context.Items, s => s.Text == "NOMFUN");
        }
    }
}
