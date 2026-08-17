using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using NertyDb.Models;
using NertyDb.Services;

namespace NertyDb.Editor
{
    public class SqlCompletionItem : ICompletionData
    {
        public string Text { get; }
        public object Description { get; }
        public double Priority { get; }
        public string Category { get; }
        public string DisplayText { get; }

        public SqlCompletionItem(string text, string description, double priority, string category, string? displayText = null)
        {
            Text = text;
            Description = description;
            Priority = priority;
            Category = category;
            DisplayText = displayText ?? text;
        }

        public ImageSource? Image => null;

        public object Content => DisplayText;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    public class CompletionContextResult
    {
        public int TokenStartOffset { get; set; }
        public string CurrentWord { get; set; } = string.Empty;
        public List<ICompletionData> Items { get; set; } = new();
    }

    public static class SqlCompletionProvider
    {
        public static CompletionContextResult GetCompletionContext(
            string fullSql,
            int caretOffset,
            ConnectionProfile? profile,
            string database)
        {
            var result = new CompletionContextResult
            {
                TokenStartOffset = caretOffset
            };

            if (profile == null || string.IsNullOrWhiteSpace(database) || caretOffset < 0 || caretOffset > fullSql.Length)
            {
                return result;
            }

            var cache = MetadataCacheService.Instance;
            var textBeforeCaret = fullSql.Substring(0, caretOffset);

            // Find current word boundary before caret
            int wordStart = caretOffset;
            while (wordStart > 0)
            {
                char c = fullSql[wordStart - 1];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                {
                    wordStart--;
                }
                else
                {
                    break;
                }
            }

            var currentToken = fullSql.Substring(wordStart, caretOffset - wordStart);
            result.CurrentWord = currentToken;
            result.TokenStartOffset = wordStart;

            var items = new List<ICompletionData>();

            // Case 1: Typing after a dot: e.g. "R034FUN." or "f." or "ra." -> show only columns of that table
            if (currentToken.Contains('.'))
            {
                int lastDotIndex = currentToken.LastIndexOf('.');
                var qualifier = currentToken.Substring(0, lastDotIndex).Trim();
                var columnFilter = currentToken.Substring(lastDotIndex + 1).Trim();

                // Token start offset for replacing only the column part after the dot
                result.TokenStartOffset = wordStart + lastDotIndex + 1;
                result.CurrentWord = columnFilter;

                var resolvedTableName = ResolveTableOrAlias(qualifier, fullSql, profile.Id, database, cache);
                if (!string.IsNullOrEmpty(resolvedTableName))
                {
                    var columns = cache.GetAllColumnsForTable(profile.Id, database, "dbo", resolvedTableName);
                    if (columns.Count == 0)
                    {
                        var allTables = cache.GetCachedTables(profile.Id, database);
                        var matchTbl = allTables.FirstOrDefault(t => t.Name.Equals(resolvedTableName, StringComparison.OrdinalIgnoreCase));
                        if (matchTbl != null)
                        {
                            columns = cache.GetAllColumnsForTable(profile.Id, database, matchTbl.Schema, matchTbl.Name);
                        }
                    }

                    foreach (var col in columns)
                    {
                        if (string.IsNullOrEmpty(columnFilter) || IsMatch(col.Name, columnFilter))
                        {
                            var icon = col.IsPrimaryKey ? "🔑" : "🔹";
                            var desc = $"{col.Name} ({col.FullTypeDescription}) - {(col.IsPrimaryKey ? "Chave Primária" : (col.IsNullable ? "NULL" : "NOT NULL"))}";
                            items.Add(new SqlCompletionItem(
                                col.Name,
                                desc,
                                priority: col.IsPrimaryKey ? 100 : 90,
                                category: "Coluna",
                                displayText: $"{icon} {col.Name} ({col.FullTypeDescription})"));
                        }
                    }

                    result.Items = items.OrderByDescending(i => i.Priority).ThenBy(i => i.Text).ToList();
                    return result;
                }
            }

            var searchFilter = currentToken.Trim();

            // Extract all current table aliases in the query
            var queryAliases = SqlAliasGenerator.ExtractTableAliases(fullSql);
            var tokenContext = GetTokenContext(textBeforeCaret);
            bool isTableContext = tokenContext == "FROM" || tokenContext == "JOIN" || tokenContext == "INTO" || tokenContext == "UPDATE" || tokenContext == "TABLE";

            // 1. Tables & Views with DBeaver-style Auto-Alias
            var tables = cache.GetCachedTables(profile.Id, database);

            foreach (var t in tables)
            {
                if (string.IsNullOrEmpty(searchFilter) || IsMatch(t.Name, searchFilter))
                {
                    var icon = t.IsView ? "👁️" : "📊";
                    var priority = isTableContext ? 95 : 70;
                    if (t.Name.StartsWith(searchFilter, StringComparison.OrdinalIgnoreCase)) priority += 10;

                    if (isTableContext)
                    {
                        // Generate smart alias without collision (e.g. R034FUN rf, R070ACC ra)
                        var alias = SqlAliasGenerator.GenerateTableAlias(t.Name, queryAliases.Keys);
                        
                        items.Add(new SqlCompletionItem(
                            $"{t.Name} {alias}",
                            $"{t.Schema}.{t.Name} (Alias: {alias})",
                            priority: priority + 5,
                            category: t.IsView ? "View" : "Tabela",
                            displayText: $"{icon} {t.Name} {alias}"));
                    }
                    else
                    {
                        items.Add(new SqlCompletionItem(
                            t.Name,
                            $"{t.Schema}.{t.Name} ({(t.IsView ? "View" : "Tabela")})",
                            priority: priority,
                            category: t.IsView ? "View" : "Tabela",
                            displayText: $"{icon} {t.Name} ({t.Schema})"));
                    }
                }
            }

            // 2. Columns from tables referenced in the query
            foreach (var kvp in queryAliases)
            {
                var refTable = kvp.Value;
                var aliasName = kvp.Key;
                var columns = cache.GetAllColumnsForTable(profile.Id, database, "dbo", refTable);

                foreach (var col in columns)
                {
                    if (string.IsNullOrEmpty(searchFilter) || IsMatch(col.Name, searchFilter))
                    {
                        var icon = col.IsPrimaryKey ? "🔑" : "🔹";
                        var priority = isTableContext ? 50 : (col.IsPrimaryKey ? 88 : 80);
                        if (col.Name.StartsWith(searchFilter, StringComparison.OrdinalIgnoreCase)) priority += 5;

                        var displaySuffix = string.Equals(aliasName, refTable, StringComparison.OrdinalIgnoreCase) ? refTable : $"{aliasName} ({refTable})";

                        items.Add(new SqlCompletionItem(
                            col.Name,
                            $"{col.Name} ({col.FullTypeDescription}) em {refTable}",
                            priority: priority,
                            category: "Coluna",
                            displayText: $"{icon} {col.Name} ({col.FullTypeDescription}) • {displaySuffix}"));
                    }
                }
            }

            // 3. Known columns across database (fallback search: only if filter >= 3 chars and items < 30)
            if (searchFilter.Length >= 3 && items.Count < 30)
            {
                var existingNames = new HashSet<string>(items.Select(i => i.Text), StringComparer.OrdinalIgnoreCase);
                var allColumns = cache.GetAllKnownColumnNames(profile.Id, database);
                int added = 0;
                foreach (var colName in allColumns)
                {
                    if (!existingNames.Contains(colName) && IsMatch(colName, searchFilter))
                    {
                        items.Add(new SqlCompletionItem(
                            colName,
                            $"Coluna {colName}",
                            priority: 60,
                            category: "Coluna",
                            displayText: $"🔹 {colName}"));

                        if (++added >= 20) break; // Cap fallback to 20 items to avoid UI lag
                    }
                }
            }

            result.Items = items.OrderByDescending(r => r.Priority).ThenBy(r => r.Text).Take(40).ToList();
            return result;
        }

        private static string GetTokenContext(string text)
        {
            var matches = Regex.Matches(text, @"\b(FROM|JOIN|INTO|UPDATE|TABLE|SELECT|WHERE|AND|OR|ORDER BY|GROUP BY|SET)\b", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (matches.Count > 0)
            {
                return matches[0].Value.ToUpperInvariant();
            }
            return "";
        }

        private static string? ResolveTableOrAlias(string aliasOrTable, string fullSql, string profileId, string database, MetadataCacheService cache)
        {
            // 1. Dynamic extraction from current SQL query (maps alias -> table, including customized aliases and multiple JOINs)
            var aliases = SqlAliasGenerator.ExtractTableAliases(fullSql);
            if (aliases.TryGetValue(aliasOrTable, out var mappedTable))
            {
                return mappedTable;
            }

            // 2. Direct table match in cache
            var tables = cache.GetCachedTables(profileId, database);
            var direct = tables.FirstOrDefault(t => t.Name.Equals(aliasOrTable, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct.Name;

            // 3. Check details cache
            var detail = cache.GetCachedTableDetails(profileId, database, "dbo", aliasOrTable);
            if (detail != null) return detail.Name;

            return aliasOrTable;
        }

        private static bool IsMatch(string text, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (text.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) return true;
            if (text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return FuzzyMatch(text, filter);
        }

        private static bool FuzzyMatch(string text, string pattern)
        {
            text = text.ToLowerInvariant();
            pattern = pattern.ToLowerInvariant();
            int tIdx = 0, pIdx = 0;
            while (tIdx < text.Length && pIdx < pattern.Length)
            {
                if (text[tIdx] == pattern[pIdx]) pIdx++;
                tIdx++;
            }
            return pIdx == pattern.Length;
        }
    }
}
