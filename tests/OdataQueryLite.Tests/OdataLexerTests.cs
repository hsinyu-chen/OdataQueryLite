using System.Linq;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class OdataLexerTests
    {
        // '$' is IsIdentifierStart but not IsIdentifierPart. ReadIdentifierOrKeyword must
        // unconditionally consume at least one character or the outer Tokenize loop spins
        // on the same position forever for a lone '$' input.
        [Theory]
        [InlineData("$", 2)]            // Identifier("$") + EOF
        [InlineData("$$", 3)]           // Identifier("$") x2 + EOF
        [InlineData("$select", 2)]      // Identifier("$select") + EOF
        [InlineData("$select=foo", 4)]  // Identifier("$select") + Equals + Identifier("foo") + EOF
        public void Tokenize_terminates_on_dollar_prefix(string input, int expectedCount)
        {
            var tokens = OdataLexer.Tokenize(input).Tokens;
            Assert.Equal(expectedCount, tokens.Count);
            Assert.Equal(TokenKind.EOF, tokens[^1].Kind);
        }

        [Fact]
        public void Empty_input_yields_only_eof()
        {
            var tokens = OdataLexer.Tokenize("").Tokens;
            Assert.Single(tokens);
            Assert.Equal(TokenKind.EOF, tokens[0].Kind);
        }

        [Fact]
        public void Whitespace_only_input_yields_only_eof()
        {
            var tokens = OdataLexer.Tokenize("   \t  \n  ").Tokens;
            Assert.Single(tokens);
            Assert.Equal(TokenKind.EOF, tokens[0].Kind);
        }

        [Theory]
        [InlineData("@", '@')]
        [InlineData("#", '#')]
        [InlineData("%", '%')]
        public void Unrecognised_character_throws_with_position(string input, char expectedChar)
        {
            var ex = Assert.Throws<FilterSyntaxException>(() => OdataLexer.Tokenize(input));
            Assert.Contains($"'{expectedChar}'", ex.Message);
            Assert.Equal(0, ex.Position);
        }
    }
}
