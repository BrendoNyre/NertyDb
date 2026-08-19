using System;
using System.Collections.Generic;
using System.Linq;

namespace NertyDb.Models
{
    public class ColumnMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = "varchar";
        public int MaxLength { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public string? DefaultValue { get; set; }
        public int OrdinalPosition { get; set; }
        public string? Description { get; set; }

        public string FullTypeDescription
        {
            get
            {
                var type = DataType.ToLowerInvariant();
                if (type is "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary")
                {
                    var len = MaxLength == -1 ? "max" : (type.StartsWith("n") ? (MaxLength / 2).ToString() : MaxLength.ToString());
                    return $"{DataType}({len})";
                }
                if (type is "decimal" or "numeric")
                {
                    return $"{DataType}({Precision},{Scale})";
                }
                return DataType;
            }
        }

        public override string ToString() => $"{Name} {FullTypeDescription} {(IsPrimaryKey ? "PK" : "")} {(IsNullable ? "NULL" : "NOT NULL")}".Trim();
    }

    public class ForeignKeyMetadata
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string ReferencedSchema { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
    }

    public class IndexMetadata
    {
        public string Name { get; set; } = string.Empty;
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string TypeDescription { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
    }

    public class TableMetadata
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public bool IsView { get; set; }
        public long RowCount { get; set; } = -1;
        public string? Description { get; set; }
        public List<ColumnMetadata> Columns { get; set; } = new();
        public List<ForeignKeyMetadata> ForeignKeys { get; set; } = new();
        public List<IndexMetadata> Indexes { get; set; } = new();

        public string FullName => $"[{Schema}].[{Name}]";
        public string DisplayName => $"{Schema}.{Name}";

        public List<string> PrimaryKeyColumns => Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);

        public override string ToString() => DisplayName;
    }
}
