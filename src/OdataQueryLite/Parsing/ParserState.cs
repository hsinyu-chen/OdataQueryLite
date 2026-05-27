using System.Collections.Generic;

namespace OdataQueryLite.Parsing
{
    /// <summary>
    /// Token cursor used by every parser in this namespace. Wraps a position into the token list and supplies
    /// the common <see cref="Peek"/> / <see cref="Consume"/> / <see cref="Expect"/> primitives plus member-path
    /// helpers shared between <c>$expand</c>, <c>$select</c>, and <c>$orderby</c>.
    /// </summary>
    /// <param name="tokens">Tokens to walk; the trailing <see cref="TokenKind.EOF"/> is required.</param>
    public sealed class ParserState(IReadOnlyList<Token> tokens)
    {
        // Caps recursive nesting (parens, lambda bodies, function args, nested $expand options).
        // 100 levels is well past any hand-written filter and well short of the ~50K stack frames
        // that would trigger StackOverflowException — which kills the process unrecoverably.
        private const int MaxRecursionDepth = 100;

        private int _pos;
        private int _depth;

        /// <summary>Returns the current token without advancing.</summary>
        public Token Peek() => tokens[_pos];

        /// <summary>
        /// Increments the recursion-depth counter; throws when the configured maximum is exceeded.
        /// Pair with <see cref="ExitRecursion"/> in a try/finally so the counter unwinds correctly on parse failure.
        /// </summary>
        /// <exception cref="FilterSyntaxException">Nesting depth exceeded the maximum.</exception>
        public void EnterRecursion()
        {
            if (++_depth > MaxRecursionDepth)
            {
                var t = Peek();
                throw new FilterSyntaxException(
                    $"expression nesting exceeds maximum depth of {MaxRecursionDepth}", t.Position);
            }
        }

        /// <summary>Decrements the recursion-depth counter. Always pair with a preceding <see cref="EnterRecursion"/>.</summary>
        public void ExitRecursion() => _depth--;

        /// <summary>Returns the current token and advances past it.</summary>
        public Token Consume() => tokens[_pos++];

        /// <summary>
        /// If the current token is the identifier <paramref name="keyword"/>, consumes it and returns <see langword="true"/>;
        /// otherwise leaves the cursor untouched.
        /// </summary>
        /// <param name="keyword">Expected keyword text (case-sensitive).</param>
        public bool TryConsumeKeyword(string keyword)
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier && t.Text == keyword) { _pos++; return true; }
            return false;
        }

        /// <summary>Asserts the current token has the given <paramref name="kind"/>, consumes it, and returns it.</summary>
        /// <param name="kind">Required token kind.</param>
        /// <returns>The consumed token.</returns>
        /// <exception cref="FilterSyntaxException">The current token's kind doesn't match.</exception>
        public Token Expect(TokenKind kind)
        {
            var t = Peek();
            if (t.Kind != kind)
                throw new FilterSyntaxException($"expected {kind} but got '{t.Text}'", t.Position);
            _pos++;
            return t;
        }

        /// <summary>
        /// Reads a comma-separated list of member paths into <paramref name="sink"/>, joining each path with
        /// <c>/</c> (e.g. <c>$select=Name,Customer/Phone</c> yields <c>"Name"</c>, <c>"Customer/Phone"</c>).
        /// </summary>
        /// <param name="sink">Destination collection; appended to, never cleared.</param>
        public void ReadMemberPathList(ICollection<string> sink)
        {
            sink.Add(string.Join('/', ReadMemberPath()));
            while (Peek().Kind == TokenKind.Comma)
            {
                Consume();
                sink.Add(string.Join('/', ReadMemberPath()));
            }
        }

        /// <summary>Reads <c>A</c>, <c>A/B</c>, <c>A/B/C</c>, ... — an OData nested-property path.</summary>
        /// <returns>The segment list, in order.</returns>
        public IReadOnlyList<string> ReadMemberPath()
        {
            List<string> path = [Expect(TokenKind.Identifier).Text];
            while (Peek().Kind == TokenKind.Slash)
            {
                Consume();
                path.Add(Expect(TokenKind.Identifier).Text);
            }
            return path;
        }
    }
}
