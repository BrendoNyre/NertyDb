using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NertyDb.Data;
using NertyDb.Models;

namespace NertyDb.Services
{
    public class ExportService
    {
        public static Encoding GetEncodingByName(string encodingName)
        {
            return encodingName.ToUpperInvariant() switch
            {
                "UTF-8" => new UTF8Encoding(false),
                "UTF-8-BOM" or "UTF8BOM" => new UTF8Encoding(true),
                "WINDOWS-1252" or "WIN-1252" or "ANSI" => Encoding.GetEncoding(1252),
                "ISO-8859-1" or "LATIN1" => Encoding.GetEncoding("ISO-8859-1"),
                "ASCII" => Encoding.ASCII,
                _ => new UTF8Encoding(true)
            };
        }

        public async Task ExportDataTableAsync(DataTable table, ExportOptions options, IEnumerable<DataRow>? specificRows = null, CancellationToken cancellationToken = default)
        {
            var rows = (specificRows ?? table.Rows.Cast<DataRow>()).ToList();
            var columns = table.Columns.Cast<DataColumn>().ToList();

            var dir = Path.GetDirectoryName(options.FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            switch (options.Format)
            {
                case ExportFormat.Csv:
                    await ExportToCsvAsync(rows, columns, options, cancellationToken);
                    break;
                case ExportFormat.ExcelXml:
                    await ExportToExcelXmlAsync(rows, columns, options, cancellationToken);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonAsync(rows, columns, options, cancellationToken);
                    break;
                case ExportFormat.SqlInsert:
                    await ExportToSqlInsertAsync(rows, columns, options, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"Formato {options.Format} não suportado.");
            }
        }

        public string FormatCsvString(DataTable table, ExportOptions options, IEnumerable<DataRow>? specificRows = null)
        {
            var rows = (specificRows ?? table.Rows.Cast<DataRow>()).ToList();
            var columns = table.Columns.Cast<DataColumn>().ToList();
            var sb = new StringBuilder();

            if (options.IncludeHeaders)
            {
                var headerCols = columns.Select(c => EscapeCsvCell(c.ColumnName, options.Delimiter, options.TextQualifier));
                sb.Append(string.Join(options.Delimiter, headerCols));
                sb.Append(options.LineEnding);
            }

            foreach (var row in rows)
            {
                var cellValues = columns.Select(col =>
                {
                    var val = row[col];
                    return EscapeCsvCell(FormatCellValue(val), options.Delimiter, options.TextQualifier);
                });
                sb.Append(string.Join(options.Delimiter, cellValues));
                sb.Append(options.LineEnding);
            }

            return sb.ToString();
        }

        private async Task ExportToCsvAsync(List<DataRow> rows, List<DataColumn> columns, ExportOptions options, CancellationToken cancellationToken)
        {
            var encoding = GetEncodingByName(options.EncodingName);
            await using var fs = new FileStream(options.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fs, encoding);

            if (options.IncludeHeaders)
            {
                var headerCols = columns.Select(c => EscapeCsvCell(c.ColumnName, options.Delimiter, options.TextQualifier));
                await writer.WriteAsync(string.Join(options.Delimiter, headerCols) + options.LineEnding);
            }

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cellValues = columns.Select(col =>
                {
                    var val = row[col];
                    return EscapeCsvCell(FormatCellValue(val), options.Delimiter, options.TextQualifier);
                });
                await writer.WriteAsync(string.Join(options.Delimiter, cellValues) + options.LineEnding);
            }

            await writer.FlushAsync();
        }

        public static string EscapeCsvCell(string value, string delimiter, string qualifier)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            bool mustQuote = value.Contains(delimiter) || 
                             value.Contains(qualifier) || 
                             value.Contains("\r") || 
                             value.Contains("\n") ||
                             value.StartsWith(" ") || 
                             value.EndsWith(" ");

            if (mustQuote)
            {
                var escaped = value.Replace(qualifier, qualifier + qualifier);
                return $"{qualifier}{escaped}{qualifier}";
            }
            return value;
        }

        public static string FormatCellValue(object? val)
        {
            if (val == null || val == DBNull.Value) return string.Empty;
            if (val is DateTime dt)
            {
                if (dt.TimeOfDay == TimeSpan.Zero) return dt.ToString("yyyy-MM-dd");
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            if (val is byte[] bytes)
            {
                return "0x" + BitConverter.ToString(bytes).Replace("-", "");
            }
            return val.ToString() ?? string.Empty;
        }

        private async Task ExportToExcelXmlAsync(List<DataRow> rows, List<DataColumn> columns, ExportOptions options, CancellationToken cancellationToken)
        {
            var encoding = new UTF8Encoding(true);
            await using var fs = new FileStream(options.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fs, encoding);

            await writer.WriteLineAsync("<?xml version=\"1.0\"?>");
            await writer.WriteLineAsync("<?mso-application progid=\"Excel.Sheet\"?>");
            await writer.WriteLineAsync("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            await writer.WriteLineAsync(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
            await writer.WriteLineAsync(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
            await writer.WriteLineAsync(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            await writer.WriteLineAsync(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");
            
            // Styles
            await writer.WriteLineAsync(" <Styles>");
            await writer.WriteLineAsync("  <Style ss:ID=\"Default\" ss:Name=\"Normal\"><Alignment ss:Vertical=\"Bottom\"/><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\"/></Style>");
            await writer.WriteLineAsync("  <Style ss:ID=\"HeaderStyle\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#1E3A8A\" ss:Pattern=\"Solid\"/><Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/></Style>");
            await writer.WriteLineAsync("  <Style ss:ID=\"DateStyle\"><NumberFormat ss:Format=\"yyyy\\-mm\\-dd\\ hh:mm:ss\"/></Style>");
            await writer.WriteLineAsync(" </Styles>");

            await writer.WriteLineAsync(" <Worksheet ss:Name=\"Dados\">");
            await writer.WriteLineAsync($"  <Table ss:ExpandedColumnCount=\"{columns.Count}\" ss:ExpandedRowCount=\"{rows.Count + (options.IncludeHeaders ? 1 : 0)}\">");

            if (options.IncludeHeaders)
            {
                await writer.WriteLineAsync("   <Row ss:StyleID=\"HeaderStyle\">");
                foreach (var col in columns)
                {
                    await writer.WriteLineAsync($"    <Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(col.ColumnName)}</Data></Cell>");
                }
                await writer.WriteLineAsync("   </Row>");
            }

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync("   <Row>");
                foreach (var col in columns)
                {
                    var val = row[col];
                    if (val == null || val == DBNull.Value)
                    {
                        await writer.WriteLineAsync("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                    }
                    else if (val is int or long or short or byte or uint or ulong or ushort or sbyte)
                    {
                        await writer.WriteLineAsync($"    <Cell><Data ss:Type=\"Number\">{val}</Data></Cell>");
                    }
                    else if (val is double or float or decimal)
                    {
                        var numStr = Convert.ToString(val, CultureInfo.InvariantCulture);
                        await writer.WriteLineAsync($"    <Cell><Data ss:Type=\"Number\">{numStr}</Data></Cell>");
                    }
                    else if (val is DateTime dt)
                    {
                        await writer.WriteLineAsync($"    <Cell ss:StyleID=\"DateStyle\"><Data ss:Type=\"DateTime\">{dt:yyyy-MM-ddTHH:mm:ss.fff}</Data></Cell>");
                    }
                    else if (val is bool b)
                    {
                        await writer.WriteLineAsync($"    <Cell><Data ss:Type=\"Number\">{(b ? 1 : 0)}</Data></Cell>");
                    }
                    else
                    {
                        var escapedStr = System.Security.SecurityElement.Escape(val.ToString() ?? "");
                        await writer.WriteLineAsync($"    <Cell><Data ss:Type=\"String\">{escapedStr}</Data></Cell>");
                    }
                }
                await writer.WriteLineAsync("   </Row>");
            }

            await writer.WriteLineAsync("  </Table>");
            await writer.WriteLineAsync(" </Worksheet>");
            await writer.WriteLineAsync("</Workbook>");
            await writer.FlushAsync();
        }

        private async Task ExportToJsonAsync(List<DataRow> rows, List<DataColumn> columns, ExportOptions options, CancellationToken cancellationToken)
        {
            var encoding = GetEncodingByName(options.EncodingName);
            var list = new List<Dictionary<string, object?>>();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dict = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var val = row[col];
                    dict[col.ColumnName] = val == DBNull.Value ? null : val;
                }
                list.Add(dict);
            }

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await File.WriteAllTextAsync(options.FilePath, json, encoding, cancellationToken);
        }

        private async Task ExportToSqlInsertAsync(List<DataRow> rows, List<DataColumn> columns, ExportOptions options, CancellationToken cancellationToken)
        {
            var encoding = GetEncodingByName(options.EncodingName);
            var tableName = !string.IsNullOrWhiteSpace(options.TableNameForInsert) 
                ? options.TableNameForInsert 
                : "TabelaExportada";

            var colNames = columns.Select(c => DmlGenerator.EscapeIdentifier(c.ColumnName)).ToList();
            var colNamesJoined = string.Join(", ", colNames);

            await using var fs = new FileStream(options.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fs, encoding);

            await writer.WriteLineAsync($"-- Exportação NertyDb: {rows.Count} registros para {tableName}");
            await writer.WriteLineAsync($"-- Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync("SET NOCOUNT ON;");
            await writer.WriteLineAsync();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = columns.Select(col => DmlGenerator.FormatLiteral(row[col] == DBNull.Value ? null : row[col]));
                var valuesJoined = string.Join(", ", values);
                await writer.WriteLineAsync($"INSERT INTO {DmlGenerator.EscapeIdentifier(tableName)} ({colNamesJoined}) VALUES ({valuesJoined});");
            }

            await writer.FlushAsync();
        }
    }
}
