using OdataQueryLite.Ast;

namespace OdataQueryLite.Parsing
{
    public static class ExpandParser
    {
        public static ExpandRequestNode Parse(string input)
        {
            var s = new ParserState(OdataLexer.Tokenize(input).Tokens);
            var root = new ExpandRequestNode();
            ParseList(s, root);
            if (s.Peek().Kind != TokenKind.EOF)
                throw new FilterSyntaxException($"unexpected token '{s.Peek().Text}' in $expand", s.Peek().Position);
            return root;
        }

        public static ExpandRequestNode ParseSelect(string input)
        {
            var s = new ParserState(OdataLexer.Tokenize(input).Tokens);
            var root = new ExpandRequestNode { SelectedFields = [] };
            s.ReadMemberPathList(root.SelectedFields);
            if (s.Peek().Kind != TokenKind.EOF)
                throw new FilterSyntaxException($"unexpected token '{s.Peek().Text}' in $select", s.Peek().Position);
            return root;
        }

        private static void ParseList(ParserState s, ExpandRequestNode parent)
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
