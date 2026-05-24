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

        private sealed class Invoice { public IQueryable<Order> Items { get; set; } }

        [Fact]
        public void ExtractPath_handles_Quote_wrapped_lambda_from_Queryable_Select()
        {
            // Items typed as IQueryable<T> binds Select to Queryable.Select which emits Quote(lambda)
            // around the inner selector — Unwrap must strip Quote (regression).
            Expression<System.Func<Invoice, IQueryable<string>>> sel =
                i => i.Items.Select(o => o.Product.Name);
            var path = PropertyAccessVisitor.ExtractPath(sel);
            Assert.Equal(new[] { "Items", "Product", "Name" }, path.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void ExtractPath_handles_multiple_Convert_layers()
        {
            // (int)(object)c.LatestOrder.Id — Unwrap must strip both Convert layers.
            var param = Expression.Parameter(typeof(Customer), "c");
            var idAccess = Expression.Property(
                Expression.Property(param, nameof(Customer.LatestOrder)),
                nameof(Order.Id));
            var lambda = Expression.Lambda(
                Expression.Convert(Expression.Convert(idAccess, typeof(object)), typeof(int)),
                param);

            var path = PropertyAccessVisitor.ExtractPath(lambda);
            Assert.Equal(new[] { "LatestOrder", "Id" }, path.Select(p => p.Name).ToArray());
        }
    }
}
