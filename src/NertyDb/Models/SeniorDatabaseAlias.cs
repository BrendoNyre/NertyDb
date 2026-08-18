using System;

namespace NertyDb.Models
{
    public class SeniorDatabaseAlias
    {
        public string Alias { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DbKind { get; set; } = string.Empty;
        public DatabaseType DatabaseType { get; set; } = DatabaseType.SqlServer;
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; } = 1433;
        public string DatabaseName { get; set; } = string.Empty;
        public string Username { get; set; } = "sa";
        public string? EncryptedPassword { get; set; }
        public string? PlainPassword { get; set; }
        public string? TbsFile { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString() => $"{Alias} ({DbKind} • {Server}/{DatabaseName})";
    }
}
