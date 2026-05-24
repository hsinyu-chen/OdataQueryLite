using System.Collections.Generic;
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
        public void Single_navigation_leaf_becomes_expandable_node_with_no_select_restriction()
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
            Assert.Equal(new HashSet<string> { "Name", "Price" }, product.AllowedSelectFields);
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
    }
}
