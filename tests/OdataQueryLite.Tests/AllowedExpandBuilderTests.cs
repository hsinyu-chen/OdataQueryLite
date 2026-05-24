using System.Collections.Generic;
using System.Linq;
using OdataQueryLite.Permissions;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class AllowedExpandBuilderTests
    {
        private sealed class Product
        {
            public string Name { get; set; }
            public decimal Price { get; set; }
            public byte[] Photo { get; set; }
            public List<int> Ratings { get; set; }
            public string[] Tags { get; set; }
        }
        private sealed class Order
        {
            public int Id { get; set; }
            public int Quantity { get; set; }
            public Product Product { get; set; }
        }
        private sealed class Customer
        {
            public string Name { get; set; }
            public Order LatestOrder { get; set; }
            public ICollection<Order> Orders { get; set; }
        }

        [Fact]
        public void Single_scalar_leaf_goes_to_AllowedSelectFields_of_parent()
        {
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder.Quantity)
                .Build();

            var latestOrder = node.ExpandableProperties["LatestOrder"];
            Assert.Contains("Quantity", latestOrder.AllowedSelectFields);
            Assert.Empty(latestOrder.ExpandableProperties);
        }

        [Fact]
        public void Single_navigation_leaf_becomes_expandable_node_unrestricted()
        {
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder)
                .Build();

            var latestOrder = node.ExpandableProperties["LatestOrder"];
            Assert.Null(latestOrder.AllowedSelectFields);
            Assert.Empty(latestOrder.ExpandableProperties);
        }

        [Fact]
        public void Collection_overload_attaches_nested_whitelist_under_collection_node()
        {
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.Orders, n => n
                    .AllowExpand(o => o.Quantity)
                    .AllowExpand(o => o.Product.Name))
                .Build();

            var orders = node.ExpandableProperties["Orders"];
            Assert.Contains("Quantity", orders.AllowedSelectFields);

            var product = orders.ExpandableProperties["Product"];
            Assert.Contains("Name", product.AllowedSelectFields);
        }

        [Fact]
        public void Multiple_calls_on_overlapping_paths_deep_merge()
        {
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder.Quantity)
                .AllowExpand(x => x.LatestOrder.Product.Name)
                .AllowExpand(x => x.LatestOrder.Product.Price)
                .Build();

            var latestOrder = node.ExpandableProperties["LatestOrder"];
            Assert.Contains("Quantity", latestOrder.AllowedSelectFields);

            var product = latestOrder.ExpandableProperties["Product"];
            Assert.Equal(new[] { "Name", "Price" }, product.AllowedSelectFields.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Mixed_single_and_collection_calls_merge_into_single_tree()
        {
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.Name)
                .AllowExpand(x => x.Orders, n => n.AllowExpand(o => o.Id))
                .Build();

            Assert.Contains("Name", node.AllowedSelectFields);
            Assert.Contains("Id", node.ExpandableProperties["Orders"].AllowedSelectFields);
        }

        [Fact]
        public void Nav_leaf_before_scalar_leaf_keeps_node_unrestricted()
        {
            // Regression: broader wins. AllowExpand(x => x.LatestOrder) declares Customer's LatestOrder
            // fully expandable; a subsequent AllowExpand(x => x.LatestOrder.Quantity) must NOT silently
            // narrow it to {"Quantity"}.
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder)
                .AllowExpand(x => x.LatestOrder.Quantity)
                .Build();

            var latestOrder = node.ExpandableProperties["LatestOrder"];
            Assert.Null(latestOrder.AllowedSelectFields);
        }

        [Fact]
        public void Scalar_leaf_before_nav_leaf_lifts_restriction()
        {
            // Same regression, reverse order. The terminating AllowExpand(x => x.LatestOrder) must
            // override the earlier scalar restriction so the final state is "fully unrestricted".
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder.Quantity)
                .AllowExpand(x => x.LatestOrder)
                .Build();

            var latestOrder = node.ExpandableProperties["LatestOrder"];
            Assert.Null(latestOrder.AllowedSelectFields);
        }

        [Fact]
        public void Byte_array_property_is_treated_as_scalar_select_field()
        {
            // Regression: byte[] is OData Edm.Binary (scalar), not a navigation.
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder.Product.Photo)
                .Build();

            var product = node.ExpandableProperties["LatestOrder"].ExpandableProperties["Product"];
            Assert.Contains("Photo", product.AllowedSelectFields);
            Assert.False(product.ExpandableProperties.ContainsKey("Photo"));
        }

        [Fact]
        public void Collection_of_primitive_is_treated_as_scalar_select_field()
        {
            // Regression: List<int> / string[] are structural properties (OData $select),
            // not navigations — they must not turn into ExpandableProperties nodes.
            var node = new AllowedExpandBuilder<Customer>()
                .AllowExpand(x => x.LatestOrder.Product.Ratings)
                .AllowExpand(x => x.LatestOrder.Product.Tags)
                .Build();

            var product = node.ExpandableProperties["LatestOrder"].ExpandableProperties["Product"];
            Assert.Contains("Ratings", product.AllowedSelectFields);
            Assert.Contains("Tags", product.AllowedSelectFields);
            Assert.False(product.ExpandableProperties.ContainsKey("Ratings"));
            Assert.False(product.ExpandableProperties.ContainsKey("Tags"));
        }
    }
}
