using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NertyDb.Data;
using NertyDb.Models;

namespace NertyDb.Services
{
    public class MetadataCacheService
    {
        private readonly ConcurrentDictionary<string, List<TableMetadata>> _tablesCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TableMetadata> _tableDetailsCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _keywordsCache = new(StringComparer.OrdinalIgnoreCase);

        public static MetadataCacheService Instance { get; } = new();

        public MetadataCacheService()
        {
            InitializeSqlKeywords();
        }

        private void InitializeSqlKeywords()
        {
            var keywords = new List<string>
            {
                "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "IS NULL", "IS NOT NULL",
                "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
                "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL OUTER JOIN", "CROSS JOIN", "ON",
                "GROUP BY", "HAVING", "ORDER BY", "ASC", "DESC",
                "COUNT", "SUM", "AVG", "MIN", "MAX", "DISTINCT",
                "TOP", "OFFSET", "FETCH NEXT", "ROWS ONLY", "ROWNUM",
                "UNION", "UNION ALL", "EXISTS", "BETWEEN", "CASE", "WHEN", "THEN", "ELSE", "END",
                "AS", "CAST", "CONVERT", "COALESCE", "NULLIF", "GETDATE", "SYSDATE"
            };
            _keywordsCache["SQL"] = keywords;
        }

        public List<string> GetKeywords() => _keywordsCache.TryGetValue("SQL", out var kw) ? kw : new List<string>();

        private string GetKey(string profileId, string database) => $"{profileId}::{database}";
        private string GetTableKey(string profileId, string database, string schema, string table) => $"{profileId}::{database}::{schema}::{table}".ToUpperInvariant();

        public async Task<List<TableMetadata>> GetTablesAsync(ConnectionProfile profile, string database, IDbDriver driver, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var key = GetKey(profile.Id, database);
            if (!forceRefresh && _tablesCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var tables = await driver.GetTablesAsync(profile, database, cancellationToken);
            _tablesCache[key] = tables;

            // Preload all column metadata in 1 single fast batch query (no connection pool spam)
            _ = Task.Run(async () =>
            {
                try
                {
                    var allDetails = await driver.GetAllTableDetailsBatchAsync(profile, database, CancellationToken.None);
                    foreach (var kvp in allDetails)
                    {
                        var tKey = GetTableKey(profile.Id, database, kvp.Value.Schema, kvp.Value.Name);
                        _tableDetailsCache[tKey] = kvp.Value;
                    }
                }
                catch { }
            });

            return tables;
        }

        public async Task<TableMetadata> GetTableDetailsAsync(ConnectionProfile profile, string database, string schema, string table, IDbDriver driver, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var key = GetTableKey(profile.Id, database, schema, table);
            if (!forceRefresh && _tableDetailsCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var details = await driver.GetTableDetailsAsync(profile, database, schema, table, cancellationToken);
            _tableDetailsCache[key] = details;
            return details;
        }

        public void SetCachedTables(string profileId, string database, List<TableMetadata> tables)
        {
            var key = GetKey(profileId, database);
            _tablesCache[key] = tables;
        }

        public void SetCachedTableDetails(string profileId, string database, string schema, string table, TableMetadata details)
        {
            var key = GetTableKey(profileId, database, schema, table);
            _tableDetailsCache[key] = details;
        }

        public List<TableMetadata> GetCachedTables(string profileId, string database)
        {
            var key = GetKey(profileId, database);
            return _tablesCache.TryGetValue(key, out var list) ? list : new List<TableMetadata>();
        }

        public TableMetadata? GetCachedTableDetails(string profileId, string database, string schema, string table)
        {
            var key = GetTableKey(profileId, database, schema, table);
            return _tableDetailsCache.TryGetValue(key, out var details) ? details : null;
        }

        public List<string> GetAllTableNames(string profileId, string database)
        {
            var tables = GetCachedTables(profileId, database);
            return tables.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<ColumnMetadata> GetAllColumnsForTable(string profileId, string database, string schema, string table)
        {
            var details = GetCachedTableDetails(profileId, database, schema, table);
            return details?.Columns ?? new List<ColumnMetadata>();
        }

        public List<string> GetAllKnownColumnNames(string profileId, string database)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefix = $"{profileId}::{database}::".ToUpperInvariant();

            foreach (var kvp in _tableDetailsCache)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var col in kvp.Value.Columns)
                    {
                        columns.Add(col.Name);
                    }
                }
            }

            return columns.ToList();
        }

        public void ClearCache(string? profileId = null)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                _tablesCache.Clear();
                _tableDetailsCache.Clear();
            }
            else
            {
                var keysToRemove = _tablesCache.Keys.Where(k => k.StartsWith(profileId + "::")).ToList();
                foreach (var k in keysToRemove) _tablesCache.TryRemove(k, out _);

                var detailKeysToRemove = _tableDetailsCache.Keys.Where(k => k.StartsWith(profileId + "::", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var k in detailKeysToRemove) _tableDetailsCache.TryRemove(k, out _);
            }
        }
    }
}
