using System;

namespace NertyDb.Models
{
    public class QueryHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Sql { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public int RowsAffected { get; set; }
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }

        public string DisplaySummary => $"{Timestamp:HH:mm:ss} [{Database}] {Sql.Replace("\r", " ").Replace("\n", " ").Trim()}".Substring(0, Math.Min(Sql.Length + 25, 100));
    }

    public enum ExportFormat
    {
        Csv,
        ExcelXml, // Native Excel Spreadsheet XML format (opens directly in Excel, styled)
        Json,
        SqlInsert
    }

    public class ExportOptions
    {
        public ExportFormat Format { get; set; } = ExportFormat.Csv;
        public string FilePath { get; set; } = string.Empty;
        public string Delimiter { get; set; } = ";";
        public string TextQualifier { get; set; } = "\"";
        public string LineEnding { get; set; } = "\r\n";
        public string EncodingName { get; set; } = "UTF-8-BOM"; // UTF-8, UTF-8-BOM, Windows-1252, ISO-8859-1
        public bool IncludeHeaders { get; set; } = true;
        public bool SelectedRowsOnly { get; set; } = false;
        public string? TableNameForInsert { get; set; }
    }

    public class SeniorTemplate
    {
        public string Category { get; set; } = "Geral";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }
}
