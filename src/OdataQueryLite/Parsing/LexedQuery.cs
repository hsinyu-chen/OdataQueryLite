using System.Collections.Generic;
using System.Text;

namespace OdataQueryLite.Parsing
{
    /// <summary>
    /// Wrapper around an <see cref="OdataLexer"/> token list. Exposes three renderings: verbatim
    /// (<see cref="ToString"/>), typed shape (<see cref="ToShapeString"/> with <c>typed: true</c>), and
    /// untyped shape (the cache-key form: non-numeric literals collapse to a single <c>?</c>, numeric
    /// literals carry a CLR-kind tag — <c>?int</c> / <c>?dec</c> / <c>?dbl</c>).
    /// </summary>
    /// <param name="tokens">Tokens as emitted by <see cref="OdataLexer.Tokenize"/>.</param>
    public sealed class LexedQuery(IReadOnlyList<Token> tokens)
    {
        /// <summary>The underlying token list (including the terminating <see cref="TokenKind.EOF"/>).</summary>
        public IReadOnlyList<Token> Tokens { get; } = tokens;

        /// <summary>Verbatim re-rendering of the lexed input — handy for debugging.</summary>
        /// <returns>The reconstructed query string.</returns>
        public override string ToString() => Render(LiteralRenderMode.Verbatim);

        /// <summary>
        /// Renders the query with literal placeholders. Typed (<c>?str</c> / <c>?num</c> / ...) is for
        /// debugging; untyped is the compiled-query cache key — a null vs non-null literal of the same
        /// kind doesn't fragment the cache, while integer / decimal / double literals get distinct tags
        /// (they resolve to different slot types, so they must not share a key).
        /// </summary>
        /// <param name="typed">Use kind-tagged placeholders when <see langword="true"/>; single <c>?</c> otherwise.</param>
        /// <returns>The rendered shape.</returns>
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
            TokenKind.NumberLiteral => mode == LiteralRenderMode.Verbatim ? t.Text
                : mode == LiteralRenderMode.Typed ? "?num"
                // Untyped cache key: tag numeric literals by CLR kind (?int / ?dec / ?dbl). Integer and
                // fractional literals resolve to different slot types, so collapsing them to one `?` would
                // map a single cache key to two slots; distinct tags keep the slot a deterministic function
                // of the shape. Non-numeric literals still collapse to `?`.
                : "?" + NumericLiteralClassifier.ShapeTag(t.Text),
            TokenKind.BoolLiteral => mode == LiteralRenderMode.Verbatim ? t.Text : (mode == LiteralRenderMode.Typed ? "?bool" : "?"),
            TokenKind.DateTimeLiteral => mode == LiteralRenderMode.Verbatim ? t.Text : (mode == LiteralRenderMode.Typed ? "?date" : "?"),
            TokenKind.NullLiteral => mode == LiteralRenderMode.Verbatim ? "null" : (mode == LiteralRenderMode.Typed ? "?null" : "?"),
            _ => t.Text
        };
    }
}
