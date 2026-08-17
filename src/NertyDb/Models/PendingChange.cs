using System;
using System.Collections.Generic;

namespace NertyDb.Models
{
    public enum ChangeType
    {
        Update,
        Insert,
        Delete
    }

    public class PendingChange
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public ChangeType Type { get; set; }
        public string Schema { get; set; } = "dbo";
        public string TableName { get; set; } = string.Empty;
        
        // Primary key column names and values (for UPDATE / DELETE)
        public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // Original values of the entire row (for fallback or audit)
        public Dictionary<string, object?> OriginalValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // New values (for UPDATE / INSERT)
        public Dictionary<string, object?> NewValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // Columns modified (for UPDATE)
        public HashSet<string> ModifiedColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Row reference in Grid
        public int RowIndex { get; set; }

        public string Description
        {
            get
            {
                var pkSummary = PrimaryKeyValues.Count > 0 
                    ? string.Join(", ", PrimaryKeyValues.Select(kv => $"{kv.Key}={FormatValuePreview(kv.Value)}"))
                    : $"Linha #{RowIndex + 1}";

                return Type switch
                {
                    ChangeType.Update => $"UPDATE {Schema}.{TableName} ({string.Join(", ", ModifiedColumns)}) PK: [{pkSummary}]",
                    ChangeType.Insert => $"INSERT {Schema}.{TableName} ({NewValues.Count} colunas)",
                    ChangeType.Delete => $"DELETE {Schema}.{TableName} PK: [{pkSummary}]",
                    _ => $"{Type} {Schema}.{TableName}"
                };
            }
        }

        private static string FormatValuePreview(object? val)
        {
            if (val == null || val == DBNull.Value) return "NULL";
            if (val is string s) return $"'{s}'";
            if (val is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
            return val.ToString() ?? "NULL";
        }
    }
}
