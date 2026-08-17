using System;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NertyDb.Models;
using NertyDb.Services;
using Xunit;

namespace NertyDb.Tests
{
    public class ExportServiceTests
    {
        private DataTable CreateSampleDataTable()
        {
            var dt = new DataTable("R034FUN");
            dt.Columns.Add("NUMEMP", typeof(int));
            dt.Columns.Add("NUMCAD", typeof(int));
            dt.Columns.Add("NOMFUN", typeof(string));
            dt.Columns.Add("DATADM", typeof(DateTime));

            dt.Rows.Add(1, 1001, "Carlos da Silva; Júnior", new DateTime(2022, 5, 10));
            dt.Rows.Add(1, 1002, "Maria D'Ávila \"Coordenadora\"", new DateTime(2023, 1, 15));
            dt.Rows.Add(1, 1003, DBNull.Value, new DateTime(2024, 8, 20));

            return dt;
        }

        [Fact]
        public void FormatCsvString_ShouldEscapeSemicolonsAndQuotes()
        {
            var dt = CreateSampleDataTable();
            var service = new ExportService();
            var options = new ExportOptions
            {
                Delimiter = ";",
                TextQualifier = "\"",
                IncludeHeaders = true,
                LineEnding = "\r\n"
            };

            var csv = service.FormatCsvString(dt, options);

            Assert.Contains("NUMEMP;NUMCAD;NOMFUN;DATADM", csv);
            // Semicolon in Carlos name must be enclosed in quotes
            Assert.Contains("\"Carlos da Silva; Júnior\"", csv);
            // Quotes in Maria name must be doubled
            Assert.Contains("\"Maria D'Ávila \"\"Coordenadora\"\"\"", csv);
        }

        [Fact]
        public void EscapeCsvCell_ShouldWrapAndEscapeCorrectly()
        {
            Assert.Equal("normal", ExportService.EscapeCsvCell("normal", ";", "\""));
            Assert.Equal("\"com;delimitador\"", ExportService.EscapeCsvCell("com;delimitador", ";", "\""));
            Assert.Equal("\"com \"\"aspas\"\"\"", ExportService.EscapeCsvCell("com \"aspas\"", ";", "\""));
            Assert.Equal("\"com\nquebra\"", ExportService.EscapeCsvCell("com\nquebra", ";", "\""));
        }

        [Fact]
        public async Task ExportDataTableAsync_Csv_ShouldGenerateValidFile()
        {
            var dt = CreateSampleDataTable();
            var service = new ExportService();
            var tempFile = Path.Combine(Path.GetTempPath(), $"nertydb_test_{Guid.NewGuid():N}.csv");

            try
            {
                var options = new ExportOptions
                {
                    Format = ExportFormat.Csv,
                    FilePath = tempFile,
                    Delimiter = ";",
                    EncodingName = "UTF-8-BOM"
                };

                await service.ExportDataTableAsync(dt, options);

                Assert.True(File.Exists(tempFile));
                var content = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);
                Assert.Contains("Carlos da Silva", content);
                Assert.Contains("Maria D'Ávila", content);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ExportDataTableAsync_ExcelXml_ShouldGenerateValidSpreadsheetXml()
        {
            var dt = CreateSampleDataTable();
            var service = new ExportService();
            var tempFile = Path.Combine(Path.GetTempPath(), $"nertydb_test_{Guid.NewGuid():N}.xml");

            try
            {
                var options = new ExportOptions
                {
                    Format = ExportFormat.ExcelXml,
                    FilePath = tempFile
                };

                await service.ExportDataTableAsync(dt, options);

                Assert.True(File.Exists(tempFile));
                var xml = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);
                Assert.Contains("<?xml version=\"1.0\"?>", xml);
                Assert.Contains("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"", xml);
                Assert.Contains("Carlos da Silva", xml);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ExportDataTableAsync_Json_ShouldGenerateValidJsonArray()
        {
            var dt = CreateSampleDataTable();
            var service = new ExportService();
            var tempFile = Path.Combine(Path.GetTempPath(), $"nertydb_test_{Guid.NewGuid():N}.json");

            try
            {
                var options = new ExportOptions
                {
                    Format = ExportFormat.Json,
                    FilePath = tempFile,
                    EncodingName = "UTF-8"
                };

                await service.ExportDataTableAsync(dt, options);

                Assert.True(File.Exists(tempFile));
                var json = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);
                Assert.StartsWith("[", json.Trim());
                Assert.EndsWith("]", json.Trim());
                Assert.Contains("\"NOMFUN\": \"Carlos da Silva; Júnior\"", json);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
