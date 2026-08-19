using System;
using System.Collections.Generic;
using NertyDb.Editor;
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
        public void SelectionStatsViewModel_EmptySelection_ResetsProperties()
        {
            var vm = new SelectionStatsViewModel();
            vm.Calculate(Array.Empty<object?>());

            Assert.False(vm.HasSelection);
            Assert.Equal(0, vm.TotalCount);
            Assert.Equal(0, vm.NonNullCount);
            Assert.Equal(string.Empty, vm.FormattedSummary);
        }
    }
}
