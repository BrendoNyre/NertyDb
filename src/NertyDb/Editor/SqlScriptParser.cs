using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NertyDb.Editor
{
    /// <summary>
    /// Representa um comando SQL individual extraído de um script com múltiplos comandos.
    /// </summary>
    public class SqlStatement
    {
        public string Sql { get; set; } = string.Empty;
        public int Index { get; set; }
        public bool IsQuery { get; set; }
        public string CommandType { get; set; } = "SQL";
        public string? TargetTable { get; set; }
        public string? TargetSchema { get; set; }

        public override string ToString() => Sql;
    }

    /// <summary>
    /// Analisador léxico que divide scripts SQL em instruções individuais respeitando:
    /// - Aspas simples e escape ('...') e ('')
    /// - Aspas duplas ("...")
    /// - Delimitadores de colchetes ([...])
    /// - Comentários de linha (-- ...)
    /// - Comentários de bloco (/* ... */)
    /// - Separadores de instrução (;) e lotes (GO)
    /// </summary>
    public static class SqlScriptParser
    {
        /// <summary>
        /// Divide um script SQL em uma lista de instruções individuais prontas para execução.
        /// </summary>
        public static List<SqlStatement> ParseStatements(string? sqlScript)
        {
            var statements = new List<SqlStatement>();
            if (string.IsNullOrWhiteSpace(sqlScript))
            {
                return statements;
            }

            // 1. Dividir primeiro por lotes "GO" (caso existam)
            var batches = SplitByGoBatch(sqlScript);

            int stmtIndex = 1;
            foreach (var batch in batches)
            {
                var stmtsInBatch = SplitBatchBySemicolon(batch);
                foreach (var s in stmtsInBatch)
                {
                    var cleanSql = s.Trim();
                    if (string.IsNullOrWhiteSpace(cleanSql)) continue;

                    // Ignora se o statement for apenas comentários
                    if (IsOnlyCommentsOrEmpty(cleanSql)) continue;

                    var stmt = new SqlStatement
                    {
                        Sql = cleanSql,
                        Index = stmtIndex++,
                        IsQuery = DetermineIfQuery(cleanSql),
                        CommandType = DetermineCommandType(cleanSql)
                    };

                    statements.Add(stmt);
                }
            }

            return statements;
        }

        private static List<string> SplitByGoBatch(string script)
        {
            var batches = new List<string>();
            var lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var currentBatch = new StringBuilder();

            bool inBlockComment = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Verificar se a linha é exatamente a palavra-chave GO (fora de bloco de comentário)
                if (!inBlockComment && Regex.IsMatch(trimmed, @"^\s*GO\s*$", RegexOptions.IgnoreCase))
                {
                    var b = currentBatch.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(b))
                    {
                        batches.Add(b);
                    }
                    currentBatch.Clear();
                    continue;
                }

                currentBatch.AppendLine(line);

                // Rastreamento básico de comentário de bloco entre linhas
                for (int i = 0; i < line.Length - 1; i++)
                {
                    if (line[i] == '/' && line[i + 1] == '*') inBlockComment = true;
                    else if (line[i] == '*' && line[i + 1] == '/') inBlockComment = false;
                }
            }

            var lastBatch = currentBatch.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(lastBatch))
            {
                batches.Add(lastBatch);
            }

            return batches.Count > 0 ? batches : new List<string> { script };
        }

        private static List<string> SplitBatchBySemicolon(string sql)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(sql)) return list;

            var current = new StringBuilder();
            bool inString = false;
            bool inDoubleQuote = false;
            bool inBracket = false;
            bool inLineComment = false;
            bool inBlockComment = false;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                char next = (i + 1 < sql.Length) ? sql[i + 1] : '\0';

                // Estados fora de literais / comentários
                if (!inString && !inDoubleQuote && !inBracket && !inLineComment && !inBlockComment)
                {
                    if (c == '\'')
                    {
                        inString = true;
                        current.Append(c);
                    }
                    else if (c == '"')
                    {
                        inDoubleQuote = true;
                        current.Append(c);
                    }
                    else if (c == '[')
                    {
                        inBracket = true;
                        current.Append(c);
                    }
                    else if (c == '-' && next == '-')
                    {
                        inLineComment = true;
                        current.Append(c);
                    }
                    else if (c == '/' && next == '*')
                    {
                        inBlockComment = true;
                        current.Append(c);
                    }
                    else if (c == ';')
                    {
                        var stmt = current.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(stmt))
                        {
                            list.Add(stmt);
                        }
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (inString)
                {
                    current.Append(c);
                    if (c == '\'')
                    {
                        if (next == '\'')
                        {
                            // Escaped quote ''
                            current.Append(next);
                            i++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                }
                else if (inDoubleQuote)
                {
                    current.Append(c);
                    if (c == '"')
                    {
                        inDoubleQuote = false;
                    }
                }
                else if (inBracket)
                {
                    current.Append(c);
                    if (c == ']')
                    {
                        inBracket = false;
                    }
                }
                else if (inLineComment)
                {
                    current.Append(c);
                    if (c == '\n' || c == '\r')
                    {
                        inLineComment = false;
                    }
                }
                else if (inBlockComment)
                {
                    current.Append(c);
                    if (c == '*' && next == '/')
                    {
                        current.Append(next);
                        i++;
                        inBlockComment = false;
                    }
                }
            }

            var last = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(last))
            {
                list.Add(last);
            }

            return list;
        }

        private static bool IsOnlyCommentsOrEmpty(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return true;

            // Remove comentários de linha
            var withoutLineComments = Regex.Replace(sql, @"--[^\r\n]*", string.Empty);
            // Remove comentários de bloco
            var withoutBlockComments = Regex.Replace(withoutLineComments, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            return string.IsNullOrWhiteSpace(withoutBlockComments);
        }

        public static bool DetermineIfQuery(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;

            // Remove comentários iniciais para achar a primeira palavra-chave
            var clean = StripLeadingComments(sql).TrimStart();

            // Queries que retornam tabela / result set
            return clean.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("EXEC", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("DESC", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);
        }

        public static string DetermineCommandType(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "SQL";
            var clean = StripLeadingComments(sql).TrimStart();
            var match = Regex.Match(clean, @"^([A-Za-z]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "SQL";
        }

        public static string StripLeadingComments(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

            var str = sql;
            while (true)
            {
                str = str.TrimStart();
                if (str.StartsWith("--"))
                {
                    int newline = str.IndexOfAny(new[] { '\r', '\n' });
                    if (newline < 0) return string.Empty;
                    str = str.Substring(newline);
                }
                else if (str.StartsWith("/*"))
                {
                    int endComment = str.IndexOf("*/", StringComparison.Ordinal);
                    if (endComment < 0) return string.Empty;
                    str = str.Substring(endComment + 2);
                }
                else
                {
                    break;
                }
            }

            return str;
        }
    }
}
