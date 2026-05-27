using OdataQueryLite.Ast;

namespace OdataQueryLite.Parsing
{
    /// <summary>Parses raw <c>$expand</c> / <c>$select</c> strings into an <see cref="ExpandRequestNode"/> tree.</summary>
    public static class ExpandParser
    {
        /// <summary>
        /// Parses a raw <c>$expand</c> string. Supports slash-chained nested expands (<c>Customer/Orders</c>),
        /// parenthesized inner options (<c>$select=...;$expand=...</c>), and comma-separated siblings.
        /// </summary>
        /// <param name="input">Raw <c>$expand</c> value.</param>
        /// <returns>The parsed tree (a root node whose <see cref="ExpandRequestNode.ExpandedProperties"/> holds the top-level expansions).</returns>
        /// <exception cref="FilterSyntaxException">The input does not match the OData <c>$expand</c> grammar.</exception>
        public static ExpandRequestNode Parse(string input)
        {
            var s = new ParserState(OdataLexer.Tokenize(input).Tokens);
            var root = new ExpandRequestNode();
            ParseList(s, root);
            if (s.Peek().Kind != TokenKind.EOF)
                throw new FilterSyntaxException($"unexpected token '{s.Peek().Text}' in $expand", s.Peek().Position);
            return root;
        }

        /// <summary>
        /// Parses a top-level <c>$select</c> string. Accepts comma-separated member paths
        /// (e.g. <c>Name,Customer/Phone</c>) and returns a single-node tree whose
        /// <see cref="ExpandRequestNode.SelectedFields"/> holds the parsed set.
        /// </summary>
        /// <param name="input">Raw <c>$select</c> value.</param>
        /// <returns>An <see cref="ExpandRequestNode"/> carrying the selected field set.</returns>
        /// <exception cref="FilterSyntaxException">The input does not match the OData <c>$select</c> grammar.</exception>
        public static ExpandRequestNode ParseSelect(string input)
        {
            var s = new ParserState(OdataLexer.Tokenize(input).Tokens);
            var root = new ExpandRequestNode { SelectedFields = [] };
            ReadSelectPath(s, root);
            while (s.Peek().Kind == TokenKind.Comma)
            {
                s.Consume();
                ReadSelectPath(s, root);
            }
            if (s.Peek().Kind != TokenKind.EOF)
                throw new FilterSyntaxException($"unexpected token '{s.Peek().Text}' in $select", s.Peek().Position);
            return root;
        }

        // OData v4 §5.1.4: $select supports nested paths (`Customer/Name`) that imply an
        // expansion of every intermediate segment. Fold the slashed path into the
        // ExpandRequestNode tree so the projector treats it like `$expand=Customer($select=Name)`.
        private static void ReadSelectPath(ParserState s, ExpandRequestNode root)
        {
            var segments = s.ReadMemberPath();
            if (segments.Count == 1)
            {
                root.SelectedFields!.Add(segments[0]);
                return;
            }
            // `$count` is a terminal segment with special collection-cardinality semantics
            // the projector doesn't apply yet. Keep the joined-path form so the existing
            // wire shape ($select=Items/$count emerges in SelectedFields verbatim) is
            // preserved until that projection feature lands.
            if (segments[^1] == "$count")
            {
                root.SelectedFields!.Add(string.Join('/', segments));
                return;
            }
            var node = root;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                if (!node.ExpandedProperties.TryGetValue(segments[i], out var child))
                {
                    child = new ExpandRequestNode();
                    node.ExpandedProperties[segments[i]] = child;
                }
                node = child;
            }
            node.SelectedFields ??= [];
            node.SelectedFields.Add(segments[^1]);
        }

        private static void ParseList(ParserState s, ExpandRequestNode parent)
        {
            s.EnterRecursion();
            try
            {
                ParseListBody(s, parent);
            }
            finally { s.ExitRecursion(); }
        }

        private static void ParseListBody(ParserState s, ExpandRequestNode parent)
        {
            while (true)
            {
                // Slash-chain is an implicit nested $expand: $expand=Customer/Orders means
                // expand Customer, then inside Customer also expand Orders. The frontend's
                // OdataDataSource.include('Customer.Orders') replaces '.' with '/' and
                // emits the slashed form, so this path is on the hot wire.
                var current = GetOrAddChild(parent, s.Expect(TokenKind.Identifier).Text);
                while (s.Peek().Kind == TokenKind.Slash)
                {
                    s.Consume();
                    current = GetOrAddChild(current, s.Expect(TokenKind.Identifier).Text);
                }
                // (...) options attach to the deepest node only — OData spec semantics.
                if (s.Peek().Kind == TokenKind.LParen)
                {
                    s.Consume();
                    ParseInner(s, current);
                    while (s.Peek().Kind == TokenKind.Semicolon)
                    {
                        s.Consume();
                        ParseInner(s, current);
                    }
                    s.Expect(TokenKind.RParen);
                }
                if (s.Peek().Kind == TokenKind.Comma) { s.Consume(); continue; }
                break;
            }
        }

        private static ExpandRequestNode GetOrAddChild(ExpandRequestNode parent, string name)
        {
            if (!parent.ExpandedProperties.TryGetValue(name, out var child))
            {
                child = new ExpandRequestNode();
                parent.ExpandedProperties[name] = child;
            }
            return child;
        }

        private static void ParseInner(ParserState s, ExpandRequestNode current)
        {
            var kw = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.Equals);
            switch (kw.Text)
            {
                case "$select":
                    current.SelectedFields ??= [];
                    s.ReadMemberPathList(current.SelectedFields);
                    break;
                case "$expand":
                    ParseList(s, current);
                    break;
                default:
                    throw new FilterSyntaxException($"unknown $expand option '{kw.Text}'", kw.Position);
            }
        }
    }
}
