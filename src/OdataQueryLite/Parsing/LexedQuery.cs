using System.Collections.Generic;
using System.Text;

namespace OdataQueryLite.Parsing
{
    // Lexer output wrapper. Three renderings:
    //   ToString()           — verbatim re-render (debug).
    //   ToShapeString()      — typed placeholders (?str / ?num / ?bool / ?date / ?null);
    //                          for debugging/inspection.
    //   ToShapeString(typed=false) — single `?` placeholder for all literals; used as the
    //                                compiled-query cache key so null doesn't fragment cache
    //                                shape vs the same template called with non-null values.
    public sealed class LexedQuery(IReadOnlyList<Token> tokens)
    {
        public IReadOnlyList<Token> Tokens { get; } = tokens;

        public override string ToString() => Render(LiteralRenderMode.Verbatim);

        public string ToShapeString(bool typed = true) =>
            Render(typed ? LiteralRenderMode.Typed : LiteralRenderMode.Untyped);

        private enum LiteralRenderMode { Verbatim, Typed, Untyped }

        private string Render(LiteralRenderMode mode)
        {
            var sb = new StringBuilder();
            Token? prev = null;
            foreach (var t in Tokens)
            {
                if (t.Kind == TokenKind.EOF) break;
                if (NeedsSpaceBetween(prev, t)) sb.Append(' ');
                sb.Append(RenderToken(t, mode));
                prev = t;
            }
            return sb.ToString();
        }

        private static bool NeedsSpaceBetween(Token? prev, Token next)
        {
            if (prev is null) return false;
            var p = prev.Value;
            if (p.Kind is TokenKind.Comma or TokenKind.Semicolon or TokenKind.Colon) return true;
            if (p.Kind == TokenKind.RParen && IsWordy(next.Kind)) return true;
            return IsWordy(p.Kind) && IsWordy(next.Kind);
        }

        private static bool IsWordy(TokenKind k) => k is TokenKind.Identifier
            or TokenKind.StringLiteral or TokenKind.NumberLiteral
            or TokenKind.BoolLiteral or TokenKind.NullLiteral or TokenKind.DateTimeLiteral;

        private static string RenderToken(Token t, LiteralRenderMode mode) => t.Kind switch
        {
            TokenKind.StringLiteral => mode switch
            {
                LiteralRenderMode.Verbatim => $"'{t.Text.Replace("'", "''")}'",
                LiteralRenderMode.Typed => "?str",
                _ => "?"
            },
            TokenKind.NumberLiteral => mode == LiteralRenderMode.Verbatim ? t.Text : (mode == LiteralRenderMode.Typed ? "?num" : "?"),
            TokenKind.BoolLiteral => mode == LiteralRenderMode.Verbatim ? t.Text : (mode == LiteralRenderMode.Typed ? "?bool" : "?"),
            TokenKind.DateTimeLiteral => mode == LiteralRenderMode.Verbatim ? t.Text : (mode == LiteralRenderMode.Typed ? "?date" : "?"),
            TokenKind.NullLiteral => mode == LiteralRenderMode.Verbatim ? "null" : (mode == LiteralRenderMode.Typed ? "?null" : "?"),
            _ => t.Text
        };
    }
}
