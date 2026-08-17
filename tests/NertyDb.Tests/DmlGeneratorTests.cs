using System;
using System.Collections.Generic;
using NertyDb.Data;
using NertyDb.Models;
using Xunit;

namespace NertyDb.Tests
{
    public class DmlGeneratorTests
    {
        [Fact]
        public void GenerateSqlStatement_ShouldGenerateProperUpdate_WithCompositePrimaryKey()
        {
            // Senior R034FUN table usually has composite PK: NUMEMP, TIPCOL, NUMCAD
            var change = new PendingChange
            {
                Type = ChangeType.Update,
                Schema = "dbo",
                TableName = "R034FUN",
                PrimaryKeyValues = new Dictionary<string, object?>
                {
                    ["NUMEMP"] = 1,
                    ["TIPCOL"] = 1,
                    ["NUMCAD"] = 1002
                },
                ModifiedColumns = new HashSet<string> { "NOMFUN", "SITAFA" },
                NewValues = new Dictionary<string, object?>
                {
                    ["NOMFUN"] = "João D'Ávila da Silva",
                    ["SITAFA"] = 1
                }
            };

            var sql = DmlGenerator.GenerateSqlStatement(change);

            Assert.StartsWith("UPDATE [dbo].[R034FUN] SET ", sql);
            Assert.Contains("[NOMFUN] = 'João D''Ávila da Silva'", sql);
            Assert.Contains("[SITAFA] = 1", sql);
            Assert.Contains("WHERE [NUMEMP] = 1 AND [TIPCOL] = 1 AND [NUMCAD] = 1002;", sql);
        }

        [Fact]
        public void GenerateSqlStatement_ShouldHandleNullValuesInUpdate()
        {
            var change = new PendingChange
            {
                Type = ChangeType.Update,
                Schema = "dbo",
                TableName = "R070ACC",
                PrimaryKeyValues = new Dictionary<string, object?>
                {
                    ["NUMEMP"] = 1,
                    ["NUMCAD"] = 550,
                    ["DATACC"] = new DateTime(2026, 8, 17)
                },
                ModifiedColumns = new HashSet<string> { "OBSMAR" },
                NewValues = new Dictionary<string, object?>
                {
                    ["OBSMAR"] = null
                }
            };

            var sql = DmlGenerator.GenerateSqlStatement(change);

            Assert.Contains("[OBSMAR] = NULL", sql);
            Assert.Contains("[DATACC] = '2026-08-17'", sql);
        }

        [Fact]
        public void GenerateSqlStatement_ShouldGenerateProperInsert()
        {
            var change = new PendingChange
            {
                Type = ChangeType.Insert,
                Schema = "dbo",
                TableName = "R070ACC",
                NewValues = new Dictionary<string, object?>
                {
                    ["NUMEMP"] = 1,
                    ["TIPCOL"] = 1,
                    ["NUMCAD"] = 5001,
                    ["DATACC"] = new DateTime(2026, 8, 17),
                    ["HORACC"] = 480, // 08:00
                    ["ORIBAT"] = "E"
                }
            };

            var sql = DmlGenerator.GenerateSqlStatement(change);

            Assert.StartsWith("INSERT INTO [dbo].[R070ACC] (", sql);
            Assert.Contains("[NUMEMP]", sql);
            Assert.Contains("[HORACC]", sql);
            Assert.Contains("VALUES (1, 1, 5001, '2026-08-17', 480, 'E');", sql);
        }

        [Fact]
        public void GenerateSqlStatement_ShouldGenerateProperDelete()
        {
            var change = new PendingChange
            {
                Type = ChangeType.Delete,
                Schema = "dbo",
                TableName = "R034CRA",
                PrimaryKeyValues = new Dictionary<string, object?>
                {
                    ["NUMEMP"] = 1,
                    ["NUMCRA"] = 998877
                }
            };

            var sql = DmlGenerator.GenerateSqlStatement(change);

            Assert.Equal("DELETE FROM [dbo].[R034CRA] WHERE [NUMEMP] = 1 AND [NUMCRA] = 998877;", sql);
        }

        [Fact]
        public void GenerateTransactionScript_ShouldWrapInBeginAndCommitTransaction()
        {
            var changes = new List<PendingChange>
            {
                new PendingChange
                {
                    Type = ChangeType.Update,
                    Schema = "dbo",
                    TableName = "R034FUN",
                    PrimaryKeyValues = new Dictionary<string, object?> { ["NUMCAD"] = 10 },
                    ModifiedColumns = new HashSet<string> { "CODFIL" },
                    NewValues = new Dictionary<string, object?> { ["CODFIL"] = 2 }
                }
            };

            var script = DmlGenerator.GenerateTransactionScript(changes);

            Assert.Contains("BEGIN TRANSACTION;", script);
            Assert.Contains("BEGIN TRY", script);
            Assert.Contains("COMMIT TRANSACTION;", script);
            Assert.Contains("ROLLBACK TRANSACTION;", script);
            Assert.Contains("UPDATE [dbo].[R034FUN]", script);
        }

        [Fact]
        public void FormatLiteral_ShouldProperlyFormatTypes()
        {
            Assert.Equal("NULL", DmlGenerator.FormatLiteral(null));
            Assert.Equal("NULL", DmlGenerator.FormatLiteral(DBNull.Value));
            Assert.Equal("'Texto Teste'", DmlGenerator.FormatLiteral("Texto Teste"));
            Assert.Equal("'O''Brien'", DmlGenerator.FormatLiteral("O'Brien"));
            Assert.Equal("123", DmlGenerator.FormatLiteral(123));
            Assert.Equal("1", DmlGenerator.FormatLiteral(true));
            Assert.Equal("0", DmlGenerator.FormatLiteral(false));
            Assert.Equal("'2026-08-17'", DmlGenerator.FormatLiteral(new DateTime(2026, 8, 17, 0, 0, 0)));
            Assert.Equal("'2026-08-17 14:30:00.000'", DmlGenerator.FormatLiteral(new DateTime(2026, 8, 17, 14, 30, 0)));
        }
    }
}
