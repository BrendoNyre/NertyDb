using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NertyDb.Editor
{
    public static class SqlAliasGenerator
    {
        private static readonly HashSet<string> ReservedSqlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "AS", "ON", "WHERE", "GROUP", "ORDER", "HAVING", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "JOIN",
            "SELECT", "FROM", "UNION", "ALL", "AND", "OR", "SET", "INTO", "VALUES", "WITH", "LIMIT", "TOP"
        };

        /// <summary>
        /// Generates a clean, concise alias for a table name (DBeaver style), avoiding existing aliases.
        /// </summary>
        public static string GenerateTableAlias(string tableName, IEnumerable<string>? existingAliases = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return "t";

            var cleanName = tableName.Trim('[', ']', '\"').Trim();
            var used = new HashSet<string>(existingAliases ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            string baseAlias = ComputeBaseAlias(cleanName);

            if (string.IsNullOrWhiteSpace(baseAlias) || ReservedSqlKeywords.Contains(baseAlias))
            {
                baseAlias = "t";
            }

            // If base alias is already used in this query, add numeric suffix (f, f2, f3...)
            string candidate = baseAlias;
            int counter = 2;
            while (used.Contains(candidate))
            {
                candidate = $"{baseAlias}{counter++}";
            }

            return candidate;
        }

        private static string ComputeBaseAlias(string tableName)
        {
            // Case 1: Senior standard tables starting with R0xx (e.g. R034FUN -> rf, R070ACC -> ra, R034CRA -> rc)
            var seniorMatch = Regex.Match(tableName, @"^R\d{2,3}([a-zA-Z]+)$", RegexOptions.IgnoreCase);
            if (seniorMatch.Success)
            {
                var suffix = seniorMatch.Groups[1].Value.ToLowerInvariant();
                if (suffix.Length > 0)
                {
                    return $"r{suffix[0]}";
                }
            }

            // Case 2: Underscore-separated words (e.g. CONTROLE_PONTO -> cp, SENIOR_USUARIOS -> su)
            if (tableName.Contains('_'))
            {
                var parts = tableName.Split('_', StringSplitOptions.RemoveEmptyEntries);
                var initials = string.Concat(parts.Select(p => p.Length > 0 ? char.ToLowerInvariant(p[0]).ToString() : ""));
                if (!string.IsNullOrEmpty(initials) && initials.Length <= 4)
                {
                    return initials;
                }
            }

            // Case 3: CamelCase words (e.g. TabelaClientes -> tc)
            var camelMatches = Regex.Matches(tableName, @"[A-Z][a-z0-9]*");
            if (camelMatches.Count > 1 && camelMatches.Count <= 4)
            {
                return string.Concat(camelMatches.Select(m => char.ToLowerInvariant(m.Value[0])));
            }

            // Case 4: First letter of single word (e.g. USUARIOS -> u, EMPRESA -> e)
            return char.ToLowerInvariant(tableName[0]).ToString();
        }

        /// <summary>
        /// Extracts all table aliases defined in a SQL query: maps Alias -> TableName.
        /// Supports:
        ///   FROM TableName alias
        ///   FROM TableName AS alias
        ///   FROM schema.TableName alias
        ///   JOIN TableName alias ON ...
        ///   JOIN schema.TableName AS alias ON ...
        /// </summary>
        public static Dictionary<string, string> ExtractTableAliases(string sql)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(sql)) return map;

            // Regex matches: FROM/JOIN [schema.][table] [AS] [alias]
            var pattern = @"\b(?:FROM|JOIN)\s+(?:\[?([a-zA-Z0-9_]+)\]?\.)?\[?([a-zA-Z0-9_]+)\]?(?:\s+(?:AS\s+)?([a-zA-Z0-9_]+))?";
            var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                var tableName = m.Groups[2].Value;
                var potentialAlias = m.Groups[3].Value;

                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    // Map Table Name to itself
                    map[tableName] = tableName;

                    if (!string.IsNullOrWhiteSpace(potentialAlias) && !ReservedSqlKeywords.Contains(potentialAlias))
                    {
                        map[potentialAlias] = tableName;
                    }
                }
            }

            return map;
        }
    }
}
