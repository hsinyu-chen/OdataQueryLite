using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using OdataQueryLite.Permissions;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class PropertyAccessVisitorTests
    {
        private sealed class Order
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public Product Product { get; set; }
        }
        private sealed class Product
        {
            public string Name { get; set; }
        }
        private sealed class Customer
        {
            public string Name { get; set; }
            public Order LatestOrder { get; set; }
            public ICollection<Order> Orders { get; set; }
        }

        [Fact]
        public void ExtractPath_single_property_yields_one_segment()
        {
            Expression<System.Func<Customer, string>> sel = c => c.Name;
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void ExtractPath_member_chain_yields_outer_to_inner()
        {
            Expression<System.Func<Customer, string>> sel = c => c.LatestOrder.Product.Name;
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "LatestOrder", "Product", "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void ExtractPath_object_cast_is_unwrapped()
        {
            Expression<System.Func<Customer, object>> sel = c => c.LatestOrder.Id;
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "LatestOrder", "Id" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void ExtractPath_Select_on_collection_traverses_into_element()
        {
            Expression<System.Func<Customer, IEnumerable<string>>> sel =
                c => c.Orders.Select(o => o.Product.Name);
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "Orders", "Product", "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void ExtractPath_unsupported_method_call_throws()
        {
            // First() / Where() / Any() etc. don't match the Select/SelectMany pattern.
            Expression<System.Func<Customer, string>> sel = c => c.Orders.First().Product.Name;
            Assert.Throws<System.ArgumentException>(() => PropertyAccessVisitor.ExtractPath(sel));
        }
    }
}
