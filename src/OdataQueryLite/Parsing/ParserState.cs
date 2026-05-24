using System.Collections.Generic;

namespace OdataQueryLite.Parsing
{
    public sealed class ParserState(IReadOnlyList<Token> tokens)
    {
        private int _pos;

        public Token Peek() => tokens[_pos];

        public Token Consume() => tokens[_pos++];

        public bool TryConsumeKeyword(string keyword)
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier && t.Text == keyword) { _pos++; return true; }
            return false;
        }

        public Token Expect(TokenKind kind)
        {
            var t = Peek();
            if (t.Kind != kind)
                throw new FilterSyntaxException($"expected {kind} but got '{t.Text}'", t.Position);
            _pos++;
            return t;
        }

        // Reads a comma-separated list of member paths into `sink`, joining each path
        // with '/' (e.g. $select=Name,Customer/Phone yields "Name", "Customer/Phone").
        // OData $select accepts nested property paths, not just flat identifiers.
        public void ReadMemberPathList(ICollection<string> sink)
        {
            sink.Add(string.Join('/', ReadMemberPath()));
            while (Peek().Kind == TokenKind.Comma)
            {
                Consume();
                sink.Add(string.Join('/', ReadMemberPath()));
            }
        }

        // Reads `A`, `A/B`, `A/B/C`, ... — an OData nested-property path.
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
