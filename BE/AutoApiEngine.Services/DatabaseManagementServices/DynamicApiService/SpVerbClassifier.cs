using System.Text;
using AutoApiEngine.Domain.Enums;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Classifies a stored procedure's HTTP verb from its definition body using the 3 literal rules:
    /// 1. last statement starts with SELECT -> GET
    /// 2. last statement starts with EXEC/EXECUTE and contains SELECT -> GET
    /// 3. anything else -> POST
    /// </summary>
    public class SpVerbClassifier
    {
        private const string ProcKeyword = "procedure";
        private const string ProcShortKeyword = "proc";
        private const string FuncKeyword = "function";

        /// <summary>
        /// Evaluates the routine definition and returns the HTTP verb it should be exposed as.
        /// Deterministic and never throws: unreadable/empty input falls back to <see cref="SpVerb.Post"/>.
        /// </summary>
        public SpVerb Evaluate(string definition)
        {
            var normalized = Normalize(definition);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return SpVerb.Post;
            }

            var body = StripCreateHeader(normalized);
            var lastStatement = UnwrapAndGetLastStatement(body);

            // Rule 1: the last real statement starts with SELECT -> GET.
            if (StartsWithWord(lastStatement, "select"))
            {
                return SpVerb.Get;
            }

            // Rule 2: the last real statement starts with EXEC/EXECUTE and its text contains SELECT -> GET.
            if ((StartsWithWord(lastStatement, "exec") || StartsWithWord(lastStatement, "execute"))
                && lastStatement.Contains("select", StringComparison.Ordinal))
            {
                return SpVerb.Get;
            }

            // Rule 3: anything else -> POST.
            return SpVerb.Post;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Normalization
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Strips /* */ block comments, -- and # line comments, lowercases, and trims.
        /// Never throws.
        /// </summary>
        private static string Normalize(string? definition)
        {
            if (string.IsNullOrEmpty(definition))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(definition.Length);
            int i = 0;
            int n = definition.Length;

            while (i < n)
            {
                char c = definition[i];

                // Block comment: /* ... */ (unterminated comment consumes the rest).
                if (c == '/' && i + 1 < n && definition[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(definition[i] == '*' && definition[i + 1] == '/'))
                    {
                        i++;
                    }
                    i += 2;
                    continue;
                }

                // Line comment: -- ... (to end of line).
                if (c == '-' && i + 1 < n && definition[i + 1] == '-')
                {
                    while (i < n && definition[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                // Line comment: # ... (to end of line, MySQL dialect).
                if (c == '#')
                {
                    while (i < n && definition[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                sb.Append(char.ToLowerInvariant(c));
                i++;
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Strips a leading "CREATE [OR ALTER] PROCEDURE/PROC/FUNCTION ... AS" header
        /// (or the MySQL-style "... (params)" header) leaving just the body.
        /// Never throws and returns the input unchanged when no header is present.
        /// </summary>
        private static string StripCreateHeader(string text)
        {
            // Only attempt to strip the header if the definition starts with create/alter.
            if (!StartsWithWord(text, "create") && !StartsWithWord(text, "alter"))
            {
                return text;
            }

            if (!TryFindRoutineKeyword(text, out int routineKeyword, out string keyword))
            {
                return text;
            }

            // 1. T-SQL header terminator: the first standalone AS keyword.
            //    Skip "WITH EXECUTE AS ..." clauses, whose AS is not the terminator.
            int searchFrom = routineKeyword;
            while (true)
            {
                int asIndex = IndexOfWord(text, "as", searchFrom);
                if (asIndex < 0)
                {
                    break;
                }

                if (text.AsSpan(0, asIndex).TrimEnd().EndsWith("execute", StringComparison.Ordinal))
                {
                    searchFrom = asIndex + 2;
                    continue;
                }

                return text.Substring(asIndex + 2).TrimStart();
            }

            // 2. No AS terminator (MySQL etc.): strip "CREATE PROCEDURE name(params)".
            int openParen = text.IndexOf('(', routineKeyword);
            if (openParen >= 0)
            {
                int depth = 0;
                for (int idx = openParen; idx < text.Length; idx++)
                {
                    if (text[idx] == '(')
                    {
                        depth++;
                    }
                    else if (text[idx] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return text.Substring(idx + 1).TrimStart();
                        }
                    }
                }
            }

            // 3. Fallback: header with no parameter list — cut after the routine name.
            int nameStart = routineKeyword + keyword.Length;
            while (nameStart < text.Length && text[nameStart] == ' ')
            {
                nameStart++;
            }
            int nameEnd = nameStart;
            while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]))
            {
                nameEnd++;
            }
            return text.Substring(nameEnd).TrimStart();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Last-statement extraction
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Removes a trailing BEGIN ... END wrapper (SQL Server) and the wrapper's opening
        /// BEGIN, then returns the final semicolon-separated statement. Empty body -> empty result.
        /// </summary>
        private static string UnwrapAndGetLastStatement(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            // Strip trailing semicolons/whitespace and any trailing END keyword(s).
            while (true)
            {
                body = body.Trim().TrimEnd(';').Trim();
                if (EndsWithWord(body, "end"))
                {
                    body = body.Substring(0, body.Length - 3).Trim();
                    continue;
                }
                break;
            }

            // Remove the wrapper's opening BEGIN (repeat for nested wrappers).
            while (StartsWithWord(body, "begin"))
            {
                body = body.Substring("begin".Length).TrimStart(';').Trim();
            }

            // The last real statement is everything after the final statement terminator.
            int lastSemicolon = body.LastIndexOf(';');
            if (lastSemicolon >= 0)
            {
                body = body.Substring(lastSemicolon + 1);
            }

            return body.Trim();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Word helpers
        // ─────────────────────────────────────────────────────────────────

        private static bool StartsWithWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            if (!text.StartsWith(word, StringComparison.Ordinal))
            {
                return false;
            }
            return text.Length == word.Length || !IsWordChar(text[word.Length]);
        }

        private static bool EndsWithWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            if (!text.EndsWith(word, StringComparison.Ordinal))
            {
                return false;
            }
            int before = text.Length - word.Length - 1;
            return before < 0 || !IsWordChar(text[before]);
        }

        private static int IndexOfWord(string text, string word, int start)
        {
            int i = Math.Max(start, 0);
            while (i <= text.Length - word.Length)
            {
                int found = text.IndexOf(word, i, StringComparison.Ordinal);
                if (found < 0)
                {
                    return -1;
                }

                int after = found + word.Length;
                bool leftBoundary = found == 0 || !IsWordChar(text[found - 1]);
                bool rightBoundary = after >= text.Length || !IsWordChar(text[after]);
                if (leftBoundary && rightBoundary)
                {
                    return found;
                }

                i = after;
            }
            return -1;
        }

        private static bool TryFindRoutineKeyword(string text, out int index, out string keyword)
        {
            int procIndex = IndexOfWord(text, ProcKeyword, 0);
            int procShortIndex = IndexOfWord(text, ProcShortKeyword, 0);
            int funcIndex = IndexOfWord(text, FuncKeyword, 0);

            return TryPickEarliest(new[]
                       {
                           (procIndex, ProcKeyword),
                           (procShortIndex, ProcShortKeyword),
                           (funcIndex, FuncKeyword)
                       },
                       out index, out keyword);
        }

        private static bool TryPickEarliest(
            (int Index, string Keyword)[] candidates,
            out int index,
            out string keyword)
        {
            index = -1;
            keyword = string.Empty;
            bool found = false;

            foreach (var candidate in candidates)
            {
                if (candidate.Index < 0)
                {
                    continue;
                }
                if (!found || candidate.Index < index)
                {
                    index = candidate.Index;
                    keyword = candidate.Keyword;
                    found = true;
                }
            }

            return found;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}