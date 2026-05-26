using System.Collections.Generic;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class ExpandParserTests
    {
        [Fact]
        public void Single_field_no_options()
        {
            var t = ExpandParser.Parse("Customer");
            Assert.True(t.ExpandedProperties.ContainsKey("Customer"));
            Assert.Null(t.ExpandedProperties["Customer"].SelectedFields);
            Assert.Empty(t.ExpandedProperties["Customer"].ExpandedProperties);
        }

        // OData v4.01 ABNF: `expand = "$expand" EQ expandItem ...` and `select` only appears
        // inside expandOption (the parens after each expand item) — it is impossible for a
        // grammar-conformant $expand string to set root-level SelectedFields. Top-level
        // $select goes through ExpandParser.ParseSelect on a fresh node instead. The
        // OdataQueryOptions merge of top-level $select onto a $expand-built tree relies on
        // this invariant; if a future ExpandParser change breaks it, this test fires and
        // forces the merge to be rewritten (e.g. to UnionWith) rather than silently
        // overwriting the spec-set fields.
        [Theory]
        [InlineData("Customer")]
        [InlineData("Customer,Orders")]
        [InlineData("Customer($select=Name)")]
        [InlineData("Customer($expand=Orders)")]
        [InlineData("Customer($select=Id;$expand=Orders($select=Total))")]
        public void Parse_never_sets_root_SelectedFields(string input)
        {
            var t = ExpandParser.Parse(input);
            Assert.Null(t.SelectedFields);
        }

        [Fact]
        public void Select_inside_expand()
        {
            var t = ExpandParser.Parse("Customer($select=Name)");
            var c = t.ExpandedProperties["Customer"];
            Assert.NotNull(c.SelectedFields);
            Assert.Contains("Name", c.SelectedFields);
        }

        [Fact]
        public void Multiple_select_fields_inside_expand()
        {
            var t = ExpandParser.Parse("Customer($select=Name,Id,Phone)");
            var c = t.ExpandedProperties["Customer"];
            Assert.Equal(3, c.SelectedFields.Count);
            Assert.Contains("Name", c.SelectedFields);
            Assert.Contains("Id", c.SelectedFields);
            Assert.Contains("Phone", c.SelectedFields);
        }

        [Fact]
        public void Nested_expand_with_select_at_each_level()
        {
            var t = ExpandParser.Parse("Items($select=Qty;$expand=Product($select=Code))");
            var items = t.ExpandedProperties["Items"];
            Assert.Contains("Qty", items.SelectedFields);
            var product = items.ExpandedProperties["Product"];
            Assert.Contains("Code", product.SelectedFields);
        }

        [Fact]
        public void Comma_separated_top_level_expands()
        {
            var t = ExpandParser.Parse("Customer,Items");
            Assert.Equal(2, t.ExpandedProperties.Count);
            Assert.Contains("Customer", t.ExpandedProperties.Keys);
            Assert.Contains("Items", t.ExpandedProperties.Keys);
        }

        [Fact]
        public void Duplicate_expand_at_same_level_merges()
        {
            var t = ExpandParser.Parse("Customer($select=Name),Customer($select=Phone)");
            var c = t.ExpandedProperties["Customer"];
            Assert.Single(t.ExpandedProperties); // merged, not duplicated
            Assert.Contains("Name", c.SelectedFields);
            Assert.Contains("Phone", c.SelectedFields);
        }

        [Theory]
        [InlineData("Customer(")]
        [InlineData("Customer($select=)")]
        [InlineData("Customer($unknown=X)")]
        [InlineData("Customer($select=Name;)")]
        public void Malformed_expand_throws(string input)
        {
            Assert.Throws<FilterSyntaxException>(() => ExpandParser.Parse(input));
        }

        [Fact]
        public void ParseSelect_handles_csv()
        {
            var t = ExpandParser.ParseSelect("Name,Id,CreatedTime");
            Assert.Equal(3, t.SelectedFields.Count);
            Assert.Contains("CreatedTime", t.SelectedFields);
        }

        [Fact]
        public void ParseSelect_accepts_nested_member_path()
        {
            // OData spec: $select=Customer/Name is valid; the slash navigates the Customer
            // relationship and selects Name from it.
            var t = ExpandParser.ParseSelect("Name,Customer/Phone,Items/Product/Code");
            Assert.Contains("Name", t.SelectedFields);
            Assert.Contains("Customer/Phone", t.SelectedFields);
            Assert.Contains("Items/Product/Code", t.SelectedFields);
        }

        [Fact]
        public void Nested_select_inside_expand_accepts_paths()
        {
            var t = ExpandParser.Parse("Order($select=Id,Customer/Name)");
            var order = t.ExpandedProperties["Order"];
            Assert.Contains("Id", order.SelectedFields);
            Assert.Contains("Customer/Name", order.SelectedFields);
        }

        [Fact]
        public void Slash_chain_becomes_nested_expand()
        {
            // OdataDataSource.include('Customer.Orders') -> URL has $expand=Customer/Orders.
            // Backend must build the nested tree: Customer -> Orders.
            var t = ExpandParser.Parse("Customer/Orders");
            var customer = t.ExpandedProperties["Customer"];
            Assert.Contains("Orders", customer.ExpandedProperties.Keys);
            Assert.Empty(customer.SelectedFields ?? []);
        }

        [Fact]
        public void Three_level_slash_chain_becomes_three_deep_tree()
        {
            var t = ExpandParser.Parse("Customer/Orders/Items");
            var orders = t.ExpandedProperties["Customer"].ExpandedProperties["Orders"];
            Assert.Contains("Items", orders.ExpandedProperties.Keys);
        }

        [Fact]
        public void Multiple_slash_paths_share_common_parent()
        {
            // Customer should be one shared node carrying both sub-expansions.
            var t = ExpandParser.Parse("Customer/Orders,Customer/Items");
            var customer = t.ExpandedProperties["Customer"];
            Assert.Single(t.ExpandedProperties);
            Assert.Equal(2, customer.ExpandedProperties.Count);
            Assert.Contains("Orders", customer.ExpandedProperties.Keys);
            Assert.Contains("Items", customer.ExpandedProperties.Keys);
        }

        [Fact]
        public void Options_after_slash_chain_attach_to_deepest_node()
        {
            // $expand=Customer/Orders($select=Total) -- the $select applies to Orders, not Customer.
            var t = ExpandParser.Parse("Customer/Orders($select=Total)");
            var customer = t.ExpandedProperties["Customer"];
            var orders = customer.ExpandedProperties["Orders"];
            Assert.Null(customer.SelectedFields);
            Assert.Contains("Total", orders.SelectedFields);
        }
    }
}
