using System.Collections.Generic;
using System.Text;

namespace OdataQueryLite.Parsing
{
    // Lexer output wrapper. Carries the token list plus the two debug/cache renderings:
    //   ToString()       — verbatim re-render (string literals re-quoted, escapes restored).
    //   ToShapeString()  — literals collapsed to type placeholders (?str / ?num / ?bool /
    //                      ?date), used as part of the compiled-query cache key.
    public sealed class LexedQuery(IReadOnlyList<Token> tokens)
    {
        public IReadOnlyList<Token> Tokens { get; } = tokens;

        public override string ToString() => Render(verbatim: true);

        public string ToShapeString() => Render(verbatim: false);

        private string Render(bool verbatim)
        {
            var sb = new StringBuilder();
            Token? prev = null;
            foreach (var t in Tokens)
            {
                if (t.Kind == TokenKind.EOF) break;
                if (NeedsSpaceBetween(prev, t)) sb.Append(' ');
                sb.Append(RenderToken(t, verbatim));
                prev = t;
            }
            return sb.ToString();
        }

        private static bool NeedsSpaceBetween(Token? prev, Token next)
        {
            if (prev is null) return false;
            var p = prev.Value;
            // Comma / semicolon / colon are list/clause separators — always followed by space.
            if (p.Kind is TokenKind.Comma or TokenKind.Semicolon or TokenKind.Colon) return true;
            // Closing paren followed by a wordy token needs a separator: `(A eq 1) and B`.
            // `))` and `),` stay compact.
            if (p.Kind == TokenKind.RParen && IsWordy(next.Kind)) return true;
            // Adjacent wordy tokens need a separator (e.g. `Name eq ?str` not `Nameeq?str`).
            return IsWordy(p.Kind) && IsWordy(next.Kind);
        }

        private static bool IsWordy(TokenKind k) => k is TokenKind.Identifier
            or TokenKind.StringLiteral or TokenKind.NumberLiteral
            or TokenKind.BoolLiteral or TokenKind.NullLiteral or TokenKind.DateTimeLiteral;

        private static string RenderToken(Token t, bool verbatim) => t.Kind switch
        {
            TokenKind.StringLiteral => verbatim ? $"'{t.Text.Replace("'", "''")}'" : "?str",
            TokenKind.NumberLiteral => verbatim ? t.Text : "?num",
            TokenKind.BoolLiteral => verbatim ? t.Text : "?bool",
            TokenKind.DateTimeLiteral => verbatim ? t.Text : "?date",
            TokenKind.NullLiteral => "null", // never parameterized — SQL semantics need IS NULL
            _ => t.Text
        };
    }
}
