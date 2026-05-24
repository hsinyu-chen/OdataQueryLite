using System;
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
        public void Single_property_yields_one_segment()
        {
            Expression<Func<Customer, string>> sel = c => c.Name;
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void Member_chain_yields_outer_to_inner()
        {
            Expression<Func<Customer, string>> sel = c => c.LatestOrder.Product.Name;
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "LatestOrder", "Product", "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void Convert_wrapper_is_rejected()
        {
            // AllowExpand must use Expression<Func<T, TChild>> with strongly-typed leaf,
            // not Expression<Func<T, object>>. The (object)-cast form should fail loudly.
            Expression<Func<Customer, object>> sel = c => c.LatestOrder.Id;
            Assert.Throws<ArgumentException>(() => PropertyAccessVisitor.ExtractPath(sel));
        }

        [Fact]
        public void Collection_Select_traversal_is_rejected()
        {
            // The Select-based form should be rejected; users must use the collection overload
            // AllowExpand(x => x.Orders, n => n.AllowExpand(...)) — see the exception message.
            Expression<Func<Customer, IEnumerable<string>>> sel =
                c => c.Orders.Select(o => o.Product.Name);
            var ex = Assert.Throws<ArgumentException>(() => PropertyAccessVisitor.ExtractPath(sel));
            Assert.Contains("collection navigation", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Method_call_on_collection_is_rejected()
        {
            Expression<Func<Customer, string>> sel = c => c.Orders.First().Product.Name;
            Assert.Throws<ArgumentException>(() => PropertyAccessVisitor.ExtractPath(sel));
        }
    }
}
