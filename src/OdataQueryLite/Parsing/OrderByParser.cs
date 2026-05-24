using System.Collections.Generic;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Parsing
{
    public static class OrderByParser
    {
        public static OrderByClause Parse(string input)
        {
            var s = new ParserState(OdataLexer.Tokenize(input).Tokens);
            List<OrderByItem> items = [];
            while (s.Peek().Kind != TokenKind.EOF)
            {
                var member = new MemberNode(s.ReadMemberPath());
                var direction = OrderByDirection.Ascending;
                var t = s.Peek();
                if (t.Kind == TokenKind.Identifier)
                {
                    if (t.Text == "asc") { s.Consume(); }
                    else if (t.Text == "desc") { s.Consume(); direction = OrderByDirection.Descending; }
                    else throw new FilterSyntaxException($"expected 'asc' or 'desc' but got '{t.Text}'", t.Position);
                }
                items.Add(new OrderByItem(member, direction));
                if (s.Peek().Kind == TokenKind.Comma)
                {
                    s.Consume();
                    if (s.Peek().Kind == TokenKind.EOF)
                        throw new FilterSyntaxException("trailing ',' in orderby", s.Peek().Position);
                    continue;
                }
                if (s.Peek().Kind != TokenKind.EOF)
                    throw new FilterSyntaxException($"expected ',' or end of orderby but got '{s.Peek().Text}'", s.Peek().Position);
            }
            return new OrderByClause(items);
        }
    }
}
