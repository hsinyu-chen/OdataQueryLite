using System.Collections.Generic;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Parsing
{
    /// <summary>Parses raw <c>$orderby</c> strings into an <see cref="OrderByClause"/>.</summary>
    public static class OrderByParser
    {
        /// <summary>Tokenizes and parses <paramref name="input"/>.</summary>
        /// <param name="input">Raw <c>$orderby</c> value.</param>
        /// <returns>The parsed clause; an empty <see cref="OrderByClause.Items"/> list when the input is empty.</returns>
        /// <exception cref="FilterSyntaxException">The input does not match the OData <c>$orderby</c> grammar.</exception>
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
