using OdataQueryLite.Ast;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class OrderByParserTests
    {
        [Fact]
        public void Single_field_default_ascending()
        {
            var c = OrderByParser.Parse("Name");
            Assert.Single(c.Items);
            Assert.Equal(OrderByDirection.Ascending, c.Items[0].Direction);
            Assert.Equal(["Name"], c.Items[0].Member.Path);
        }

        [Fact]
        public void Single_field_explicit_desc()
        {
            var c = OrderByParser.Parse("Name desc");
            Assert.Equal(OrderByDirection.Descending, c.Items[0].Direction);
        }

        [Fact]
        public void Multiple_fields_comma_separated()
        {
            var c = OrderByParser.Parse("Name,Date desc,Amount asc");
            Assert.Equal(3, c.Items.Count);
            Assert.Equal(OrderByDirection.Ascending, c.Items[0].Direction);
            Assert.Equal(OrderByDirection.Descending, c.Items[1].Direction);
            Assert.Equal(OrderByDirection.Ascending, c.Items[2].Direction);
        }

        [Fact]
        public void Nested_property_path_supported()
        {
            var c = OrderByParser.Parse("Customer/Name desc");
            Assert.Equal(["Customer", "Name"], c.Items[0].Member.Path);
            Assert.Equal(OrderByDirection.Descending, c.Items[0].Direction);
        }

        [Theory]
        [InlineData("Name xyz")]
        [InlineData(",")]
        [InlineData("Name,")]
        public void Malformed_orderby_throws(string input)
        {
            Assert.Throws<FilterSyntaxException>(() => OrderByParser.Parse(input));
        }
    }
}
