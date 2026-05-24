using System;
using System.Collections.Generic;
using System.Text;

namespace OdataQueryLite.Parsing
{
    public sealed class FilterSyntaxException(string message, int position) : Exception($"{message} (position {position})")
    {
        public int Position { get; } = position;
    }

    public static class OdataLexer
    {
        public static LexedQuery Tokenize(string input)
        {
            ArgumentNullException.ThrowIfNull(input);
            List<Token> tokens = [];
            int i = 0;
            int n = input.Length;
            while (i < n)
            {
                if (char.IsWhiteSpace(input[i])) { i++; continue; }
                int posBefore = i;
                tokens.Add(ScanOneToken(input, ref i));
                // Safety net: every Scan path must advance i (or throw). A regression that breaks
                // this would spin forever and OOM the test host — be loud immediately.
                if (i == posBefore)
                    throw new InvalidOperationException($"OdataLexer made no progress at position {i} ('{input[i]}') — bug");
            }
            tokens.Add(new Token(TokenKind.EOF, string.Empty, n));
            return new LexedQuery(tokens);
        }

        private static Token ScanOneToken(string s, ref int i)
        {
            int start = i;
            char c = s[i];
            switch (c)
            {
                case '(': i++; return new Token(TokenKind.LParen, "(", start);
                case ')': i++; return new Token(TokenKind.RParen, ")", start);
                case ',': i++; return new Token(TokenKind.Comma, ",", start);
                case '/': i++; return new Token(TokenKind.Slash, "/", start);
                case ';': i++; return new Token(TokenKind.Semicolon, ";", start);
                case '=': i++; return new Token(TokenKind.Equals, "=", start);
                case ':': i++; return new Token(TokenKind.Colon, ":", start);
                case '\'': return ReadString(s, ref i);
            }
            if (c == '-' && i + 1 < s.Length && IsDigit(s[i + 1])) return ReadNumberOrDate(s, ref i);
            if (IsDigit(c)) return ReadNumberOrDate(s, ref i);
            if (IsIdentifierStart(c)) return ReadIdentifierOrKeyword(s, ref i);
            throw new FilterSyntaxException($"unexpected character '{c}'", i);
        }

        private static Token ReadString(string s, ref int i)
        {
            // '...''...' — single-quoted, '' escapes a single quote
            int start = i;
            i++; // consume opening '
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\'')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                        continue;
                    }
                    i++; // consume closing '
                    return new Token(TokenKind.StringLiteral, sb.ToString(), start);
                }
                sb.Append(c);
                i++;
            }
            throw new FilterSyntaxException("unterminated string literal", start);
        }

        private static Token ReadNumberOrDate(string s, ref int i)
        {
            int start = i;
            if (IsIsoDateStart(s, i))
            {
                return ReadDate(s, ref i, start);
            }
            // number: optional leading '-', digits, optional . digits, optional e[+-]digits
            if (s[i] == '-') i++;
            while (i < s.Length && IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && IsDigit(s[i])) i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && IsDigit(s[i])) i++;
            }
            return new Token(TokenKind.NumberLiteral, s[start..i], start);
        }

        private static bool IsIsoDateStart(string s, int i)
        {
            // \d{4}-\d{2}-\d{2}T   — 4 digits, dash, 2 digits, dash, 2 digits, 'T'
            if (i + 10 >= s.Length) return false;
            for (int k = 0; k < 4; k++) if (!IsDigit(s[i + k])) return false;
            if (s[i + 4] != '-') return false;
            if (!IsDigit(s[i + 5]) || !IsDigit(s[i + 6])) return false;
            if (s[i + 7] != '-') return false;
            if (!IsDigit(s[i + 8]) || !IsDigit(s[i + 9])) return false;
            if (s[i + 10] != 'T') return false;
            return true;
        }

        private static Token ReadDate(string s, ref int i, int start)
        {
            // consume until we hit a boundary char (whitespace, ',', ')', '(', or end)
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == ',' || c == ')' || c == '(' || c == '/') break;
                i++;
            }
            return new Token(TokenKind.DateTimeLiteral, s[start..i], start);
        }

        private static Token ReadIdentifierOrKeyword(string s, ref int i)
        {
            int start = i;
            i++; // IsIdentifierStart already validated the first char ('$' is start but not part — must advance unconditionally)
            while (i < s.Length && IsIdentifierPart(s[i])) i++;
            var text = s[start..i];
            return text switch
            {
                "true" or "false" => new Token(TokenKind.BoolLiteral, text, start),
                "null" => new Token(TokenKind.NullLiteral, text, start),
                _ => new Token(TokenKind.Identifier, text, start)
            };
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
