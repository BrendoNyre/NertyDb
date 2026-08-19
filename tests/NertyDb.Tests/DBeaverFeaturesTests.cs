using System;
using System.Collections.Generic;
using NertyDb.Editor;
using NertyDb.Services;
using NertyDb.ViewModels;
using Xunit;

namespace NertyDb.Tests
{
    public class DBeaverFeaturesTests
    {
        [Fact]
        public void SqlStatementExtractor_WithSelectedText_ReturnsSelectedText()
        {
            var fullSql = "SELECT * FROM R034FUN; SELECT * FROM R030EMP;";
            var selection = "SELECT * FROM R030EMP";
            var result = SqlStatementExtractor.ExtractStatementToExecute(fullSql, 5, selection);

            Assert.Equal("SELECT * FROM R030EMP", result);
        }

        [Fact]
        public void SqlStatementExtractor_WithoutSelection_ExtractsStatementUnderCaret()
        {
            var fullSql = "SELECT * FROM R034FUN;\r\nSELECT * FROM R030EMP;\r\nSELECT * FROM R038HFI;";

            // Caret on the second query
            int caretOffset = fullSql.IndexOf("R030EMP", StringComparison.Ordinal);
            var result = SqlStatementExtractor.ExtractStatementToExecute(fullSql, caretOffset, null);

            Assert.Equal("SELECT * FROM R030EMP", result);
        }

        [Fact]
        public void SqlStatementExtractor_WithGoBatches_ExtractsWithinActiveBatch()
        {
            var fullSql = "SELECT 1 AS FirstBatch;\r\nGO\r\nSELECT 2 AS SecondBatch;\r\nGO\r\nSELECT 3 AS ThirdBatch;";

            int caretOffset = fullSql.IndexOf("SecondBatch", StringComparison.Ordinal);
            var result = SqlStatementExtractor.ExtractStatementToExecute(fullSql, caretOffset, null);

            Assert.Equal("SELECT 2 AS SecondBatch", result);
        }

        [Fact]
        public void SelectionStatsViewModel_NumericValues_CalculatesSumAvgMinMaxAndDistinct()
        {
            var vm = new SelectionStatsViewModel();
            var values = new object?[] { 10, 20.5m, "30,5", 10, null, DBNull.Value };

            vm.Calculate(values);

            Assert.True(vm.HasSelection);
            Assert.True(vm.IsNumeric);
            Assert.Equal(6, vm.TotalCount);
            Assert.Equal(4, vm.NonNullCount);
            Assert.Equal(3, vm.DistinctCount); // 10, 20.5, 30.5
            Assert.Equal(71.0m, vm.Sum);
            Assert.Equal(17.75m, vm.Average);
            Assert.Contains("Soma", vm.FormattedSummary);
            Assert.Contains("Média", vm.FormattedSummary);
        }

        [Fact]
        public void SelectionStatsViewModel_TextValues_CalculatesCountAndDistinct()
        {
            var vm = new SelectionStatsViewModel();
            var values = new object?[] { "Alpha", "Beta", "Alpha", "Gamma", null };

            vm.Calculate(values);

            Assert.True(vm.HasSelection);
            Assert.False(vm.IsNumeric);
            Assert.Equal(5, vm.TotalCount);
            Assert.Equal(4, vm.NonNullCount);
            Assert.Equal(3, vm.DistinctCount);
            Assert.Contains("Contagem: 4", vm.FormattedSummary);
            Assert.Contains("Distintos: 3", vm.FormattedSummary);
        }

        [Fact]
        public void AppLogService_RecordsEntriesAndClearsSuccessfully()
        {
            var log = AppLogService.Instance;
            log.Clear();
            Assert.Empty(log.Entries);

            log.LogSuccess("Test Source", "Success message", "SELECT 1");
            log.LogError("Test Source", "Error message", "SELECT 2");
            log.LogWarning("Test Source", "Warning message");
            log.LogInfo("Test Source", "Info message");

            Assert.Equal(4, log.Entries.Count);
            Assert.Equal(ToastType.Info, log.Entries[0].Level); // newest first
            Assert.Equal(ToastType.Warning, log.Entries[1].Level);
            Assert.Equal(ToastType.Error, log.Entries[2].Level);
            Assert.Equal(ToastType.Success, log.Entries[3].Level);

            log.Clear();
            Assert.Empty(log.Entries);
        }

        [Fact]
        public void ToastService_AddsAndRemovesToasts()
        {
            var toastService = ToastService.Instance;
            toastService.ShowSuccess("Registro inserido com sucesso!", "Sucesso Teste");

            Assert.NotEmpty(toastService.ActiveToasts);
            var toast = toastService.ActiveToasts.FirstOrDefault(t => t.Title == "Sucesso Teste");
            Assert.NotNull(toast);
            Assert.Equal("Sucesso Teste", toast.Title);
            Assert.Equal("Registro inserido com sucesso!", toast.Message);
            Assert.Equal(ToastType.Success, toast.Type);

            toastService.Dismiss(toast.Id);
            Assert.DoesNotContain(toast, toastService.ActiveToasts);
        }

        [Fact]
        public void SchemaNode_TooltipAndDescription_FormattedCorrectly()
        {
            var node = new SchemaNode
            {
                NodeType = SchemaNodeType.Table,
                Title = "R034FUN",
                Description = "Ficha Básica Colaborador",
                SubTitle = "dbo (1.500 lins) — Ficha Básica Colaborador"
            };

            Assert.True(node.HasDescription);
            Assert.Equal("R034FUN: Ficha Básica Colaborador", node.TooltipText);
        }

        [Fact]
        public void SqlResultTab_DuplicateRow_CreatesNewRowAndAllowsEditingBeforeSave()
        {
            var dt = new System.Data.DataTable("r900pdt");
            dt.Columns.Add("perid", typeof(int));
            dt.Columns.Add("datseq", typeof(int));
            dt.Columns.Add("dat1", typeof(string));

            dt.Rows.Add(1073741836, 1, "ORIGINAL_1");
            dt.Rows.Add(1073741836, 2, "ORIGINAL_2");

            var conn = new NertyDb.Models.ConnectionProfile { Name = "Test" };
            var vm = new SqlResultTabViewModel(
                dt,
                "r900pdt",
                conn,
                "db",
                new NertyDb.Data.SqlServerDriver(),
                new NertyDb.Services.ExportService(),
                _ => { },
                _ => { },
                sourceTable: "r900pdt",
                sourceSchema: "dbo");

            vm.PrimaryKeyColumns = new List<string> { "perid", "datseq" };

            // 1. Duplicate row 0
            vm.ExecuteDuplicateRow(dt.DefaultView[0]);

            Assert.Equal(3, vm.RowCount);
            Assert.True(vm.HasPendingChanges);
            Assert.Single(vm.PendingChanges);
            Assert.Equal(NertyDb.Models.ChangeType.Insert, vm.PendingChanges[0].Type);
            Assert.Equal(1073741836, dt.Rows[2]["perid"]);
            Assert.Equal("ORIGINAL_1", dt.Rows[2]["dat1"]);

            // 2. Edit duplicated row cell
            vm.OnCellEdited(dt.DefaultView[2], "dat1", "VALOR_ALTERADO");
            Assert.Equal("VALOR_ALTERADO", vm.PendingChanges[0].NewValues["dat1"]);

            // 3. Discard changes
            vm.DiscardChangesCommand.Execute(null);
            Assert.Equal(2, vm.RowCount);
            Assert.False(vm.HasPendingChanges);
            Assert.Empty(vm.PendingChanges);
        }

        [Fact]
        public void SqlResultTab_BulkCellEdit_UpdatesAllSelectedCells()
        {
            var dt = new System.Data.DataTable("r034fun");
            dt.Columns.Add("numemp", typeof(int));
            dt.Columns.Add("numcad", typeof(int));
            dt.Columns.Add("sitcad", typeof(int));

            for (int i = 1; i <= 5; i++)
            {
                dt.Rows.Add(1, i, 1);
            }

            var conn = new NertyDb.Models.ConnectionProfile { Name = "Test" };
            var vm = new SqlResultTabViewModel(
                dt,
                "r034fun",
                conn,
                "db",
                new NertyDb.Data.SqlServerDriver(),
                new NertyDb.Services.ExportService(),
                _ => { },
                _ => { },
                sourceTable: "r034fun",
                sourceSchema: "dbo");

            vm.PrimaryKeyColumns = new List<string> { "numemp", "numcad" };

            // Mass edit rows 0, 1, 2, 3 to sitcad = 7
            var targetCells = new List<(System.Data.DataRowView, string)>
            {
                (dt.DefaultView[0], "sitcad"),
                (dt.DefaultView[1], "sitcad"),
                (dt.DefaultView[2], "sitcad"),
                (dt.DefaultView[3], "sitcad")
            };

            vm.ApplyBulkCellValues(targetCells, 7);

            Assert.Equal(7, dt.Rows[0]["sitcad"]);
            Assert.Equal(7, dt.Rows[1]["sitcad"]);
            Assert.Equal(7, dt.Rows[2]["sitcad"]);
            Assert.Equal(7, dt.Rows[3]["sitcad"]);
            Assert.Equal(1, dt.Rows[4]["sitcad"]); // untouched

            Assert.Equal(4, vm.TotalPendingCount);
        }

        [Fact]
        public void SqlResultTab_MultiRowDelete_MarksAllSelectedForDelete()
        {
            var dt = new System.Data.DataTable("r030emp");
            dt.Columns.Add("numemp", typeof(int));
            dt.Columns.Add("nomemp", typeof(string));

            dt.Rows.Add(1, "Empresa 1");
            dt.Rows.Add(2, "Empresa 2");
            dt.Rows.Add(3, "Empresa 3");

            var conn = new NertyDb.Models.ConnectionProfile { Name = "Test" };
            var vm = new SqlResultTabViewModel(
                dt,
                "r030emp",
                conn,
                "db",
                new NertyDb.Data.SqlServerDriver(),
                new NertyDb.Services.ExportService(),
                _ => { },
                _ => { },
                sourceTable: "r030emp",
                sourceSchema: "dbo");

            vm.PrimaryKeyColumns = new List<string> { "numemp" };

            // Select rows 0 and 1
            var selected = new List<System.Data.DataRowView> { dt.DefaultView[0], dt.DefaultView[1] };
            vm.ExecuteDeleteRow(selected);

            Assert.Equal(2, vm.DeletedRowIndices.Count);
            Assert.Equal(2, vm.TotalPendingCount);
            Assert.All(vm.PendingChanges, c => Assert.Equal(NertyDb.Models.ChangeType.Delete, c.Type));

            // Discard
            vm.DiscardChangesCommand.Execute(null);
            Assert.Empty(vm.DeletedRowIndices);
            Assert.False(vm.HasPendingChanges);
        }

        [Fact]
        public void DmlGenerator_StrictPrimaryKey_GeneratesPreciseWhereClause()
        {
            var change = new NertyDb.Models.PendingChange
            {
                Type = NertyDb.Models.ChangeType.Update,
                Schema = "dbo",
                TableName = "r900pdt",
                PrimaryKeyValues = new Dictionary<string, object?>
                {
                    { "perid", 1073741836 },
                    { "datseq", 1 }
                },
                NewValues = new Dictionary<string, object?>
                {
                    { "dat1", "NOVO_VALOR_CRIPT" }
                },
                ModifiedColumns = new HashSet<string> { "dat1" }
            };

            var sql = NertyDb.Data.DmlGenerator.GenerateSqlStatement(change);

            Assert.Equal("UPDATE [dbo].[r900pdt] SET [dat1] = 'NOVO_VALOR_CRIPT' WHERE [perid] = 1073741836 AND [datseq] = 1;", sql);
        }
    }
}
