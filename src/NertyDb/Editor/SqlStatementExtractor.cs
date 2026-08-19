using System;
using System.Text.RegularExpressions;

namespace NertyDb.Editor
{
    public static class SqlStatementExtractor
    {
        public static string ExtractStatementToExecute(string? fullText, int caretOffset, string? selectedText)
        {
            // 1. If text is selected by user, execute selection
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText.Trim();
            }

            if (string.IsNullOrWhiteSpace(fullText))
            {
                return string.Empty;
            }

            caretOffset = Math.Clamp(caretOffset, 0, fullText.Length);

            // 2. Split by batches (GO keyword on its own line) first
            var lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int currentPos = 0;
            int batchStart = 0;
            int batchEnd = fullText.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int lineStart = currentPos;
                int lineEnd = currentPos + line.Length;

                if (Regex.IsMatch(line.Trim(), @"^\s*GO\s*$", RegexOptions.IgnoreCase))
                {
                    if (caretOffset <= lineStart)
                    {
                        batchEnd = lineStart;
                        break;
                    }
                    else
                    {
                        batchStart = lineEnd + (i < lines.Length - 1 ? 1 : 0);
                    }
                }

                currentPos += line.Length + 1; // approx newline
            }

            if (batchStart > fullText.Length) batchStart = 0;
            if (batchEnd > fullText.Length) batchEnd = fullText.Length;
            if (batchEnd < batchStart) batchEnd = fullText.Length;

            var batchSql = fullText.Substring(batchStart, batchEnd - batchStart);
            int relCaret = Math.Clamp(caretOffset - batchStart, 0, batchSql.Length);

            // 3. Find statement within batch delimited by semicolons (;)
            int stmtStart = 0;
            int stmtEnd = batchSql.Length;

            bool inString = false;
            bool inComment = false;
            bool inBlockComment = false;

            int lastDelimiter = 0;

            for (int i = 0; i < batchSql.Length; i++)
            {
                char c = batchSql[i];
                char next = (i + 1 < batchSql.Length) ? batchSql[i + 1] : '\0';

                if (!inString && !inComment && !inBlockComment)
                {
                    if (c == '\'')
                    {
                        inString = true;
                    }
                    else if (c == '-' && next == '-')
                    {
                        inComment = true;
                    }
                    else if (c == '/' && next == '*')
                    {
                        inBlockComment = true;
                    }
                    else if (c == ';')
                    {
                        if (i >= relCaret)
                        {
                            stmtStart = lastDelimiter;
                            stmtEnd = i;
                            break;
                        }
                        else
                        {
                            lastDelimiter = i + 1;
                        }
                    }
                }
                else if (inString)
                {
                    if (c == '\'')
                    {
                        if (next == '\'') i++; // escaped quote ''
                        else inString = false;
                    }
                }
                else if (inComment)
                {
                    if (c == '\n' || c == '\r')
                    {
                        inComment = false;
                    }
                }
                else if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                }
            }

            if (stmtEnd > batchSql.Length) stmtEnd = batchSql.Length;
            if (stmtStart >= stmtEnd)
            {
                stmtStart = lastDelimiter;
                stmtEnd = batchSql.Length;
            }

            var result = batchSql.Substring(stmtStart, stmtEnd - stmtStart).Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return fullText.Trim();
            }

            return result;
        }
    }
}
