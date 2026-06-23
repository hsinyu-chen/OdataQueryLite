using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class LexedQueryTests
    {
        [Theory]
        [InlineData("Name eq 'X'", "Name eq 'X'")]
        [InlineData("Amount gt 100", "Amount gt 100")]
        [InlineData("(A eq 1 or A eq 2) and B ne null", "(A eq 1 or A eq 2) and B ne null")]
        [InlineData("Customer/Name eq 'X'", "Customer/Name eq 'X'")]
        [InlineData("contains(Code, 'abc')", "contains(Code, 'abc')")]
        [InlineData("not contains(Code, 'X')", "not contains(Code, 'X')")]
        [InlineData("Items/$count gt 0", "Items/$count gt 0")]
        [InlineData("Items/any(o: o/Status eq 'Active')", "Items/any(o: o/Status eq 'Active')")]
        public void Verbatim_round_trip_renders_canonical_form(string input, string expected)
        {
            Assert.Equal(expected, OdataLexer.Tokenize(input).ToString());
        }

        [Fact]
        public void Escaped_string_literal_preserves_doubled_quotes()
        {
            // Token text holds the unescaped value; ToString re-quotes and re-escapes.
            Assert.Equal("Name eq 'O''Brien'", OdataLexer.Tokenize("Name eq 'O''Brien'").ToString());
        }

        [Theory]
        [InlineData("Name eq 'X'", "Name eq ?str")]
        [InlineData("Amount gt 100", "Amount gt ?num")]
        [InlineData("IsActive eq true", "IsActive eq ?bool")]
        [InlineData("Name ne null", "Name ne ?null")] // null parameterized so cache shape is stable
        [InlineData("CreatedTime gt 2024-01-01T00:00:00Z", "CreatedTime gt ?date")]
        [InlineData("(A eq 1 or A eq 2) and B ne null", "(A eq ?num or A eq ?num) and B ne ?null")]
        public void Shape_collapses_literals_to_type_placeholders(string input, string expectedShape)
        {
            Assert.Equal(expectedShape, OdataLexer.Tokenize(input).ToShapeString());
        }

        [Fact]
        public void Shape_is_identical_for_queries_with_same_structure_different_values()
        {
            // The whole point of the cache key — these two queries share a compiled delegate.
            var a = OdataLexer.Tokenize("Status eq 'Active' and Amount gt 100").ToShapeString();
            var b = OdataLexer.Tokenize("Status eq 'Pending' and Amount gt 500").ToShapeString();
            Assert.Equal(a, b);
        }

        [Fact]
        public void Untyped_shape_tags_numbers_by_kind_but_collapses_other_literals()
        {
            // Cache key mode: a non-numeric slot renders `?` whether null or set, so null and non-null
            // calls for the same template share one entry (stops O(2^N) fragmentation across many slots).
            var strValue = OdataLexer.Tokenize("Name eq 'X' and Age gt 30").ToShapeString(typed: false);
            var strNull = OdataLexer.Tokenize("Name eq null and Age gt 30").ToShapeString(typed: false);
            Assert.Equal(strValue, strNull);
            Assert.Equal("Name eq ? and Age gt ?int", strValue);

            // Numeric literals additionally carry a CLR-kind tag — integer and fractional literals resolve
            // to different slot types, so they MUST get distinct keys to keep the slot deterministic.
            Assert.Equal("Amount gt ?int", OdataLexer.Tokenize("Amount gt 30").ToShapeString(typed: false));
            Assert.Equal("Amount gt ?dec", OdataLexer.Tokenize("Amount gt 9.99").ToShapeString(typed: false));
            Assert.NotEqual(
                OdataLexer.Tokenize("Amount gt 30").ToShapeString(typed: false),
                OdataLexer.Tokenize("Amount gt 9.99").ToShapeString(typed: false));
        }

        [Fact]
        public void Shape_differs_when_token_structure_differs()
        {
            // Same operands, different operator — must produce distinct shape.
            var a = OdataLexer.Tokenize("Amount gt 100").ToShapeString();
            var b = OdataLexer.Tokenize("Amount lt 100").ToShapeString();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Tokens_property_still_accessible_for_parser_consumption()
        {
            var lexed = OdataLexer.Tokenize("Name eq 'X'");
            Assert.NotEmpty(lexed.Tokens);
            Assert.Equal(TokenKind.EOF, lexed.Tokens[^1].Kind);
        }
    }
}
