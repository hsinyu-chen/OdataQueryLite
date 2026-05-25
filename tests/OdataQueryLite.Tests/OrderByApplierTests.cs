using System.Linq;
using OdataQueryLite.ExpressionBuilding;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class OrderByApplierTests
    {
        public sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Age { get; set; }
            public Address Home { get; set; }
        }

        public sealed class Address
        {
            public string City { get; set; }
        }

        private static IQueryable<Customer> Rows() => new[]
        {
            new Customer { Id = 3, Name = "Alice", Age = 30, Home = new Address { City = "Taipei" } },
            new Customer { Id = 1, Name = "Bob",   Age = 30, Home = new Address { City = "Taipei" } },
            new Customer { Id = 2, Name = "Alice", Age = 25, Home = new Address { City = "Hsinchu" } },
        }.AsQueryable();

        [Fact]
        public void Apply_single_ascending_sorts_by_property()
        {
            var clause = OrderByParser.Parse("Id");
            var ordered = OrderByApplier.Apply(Rows(), clause).ToList();
            Assert.Equal([1, 2, 3], ordered.Select(c => c.Id));
        }

        [Fact]
        public void Apply_single_descending_reverses_order()
        {
            var clause = OrderByParser.Parse("Id desc");
            var ordered = OrderByApplier.Apply(Rows(), clause).ToList();
            Assert.Equal([3, 2, 1], ordered.Select(c => c.Id));
        }

        [Fact]
        public void Apply_multi_uses_then_by_for_tiebreak()
        {
            // Name asc, then Id desc — Alice(3) before Alice(2) before Bob(1)
            var clause = OrderByParser.Parse("Name, Id desc");
            var ordered = OrderByApplier.Apply(Rows(), clause).ToList();
            Assert.Equal([3, 2, 1], ordered.Select(c => c.Id));
        }

        [Fact]
        public void Apply_nested_property_path_supported()
        {
            var clause = OrderByParser.Parse("Home/City");
            var ordered = OrderByApplier.Apply(Rows(), clause).ToList();
            Assert.Equal(["Hsinchu", "Taipei", "Taipei"], ordered.Select(c => c.Home.City));
        }

        [Fact]
        public void Apply_nullable_property_supported()
        {
            var clause = OrderByParser.Parse("Age desc");
            var ordered = OrderByApplier.Apply(Rows(), clause).ToList();
            Assert.Equal([30, 30, 25], ordered.Select(c => c.Age));
        }

        [Fact]
        public void Apply_with_empty_clause_returns_source_unchanged()
        {
            var source = Rows();
            var clause = new Ast.OrderByClause([]);
            Assert.Same(source, OrderByApplier.Apply(source, clause));
        }

        [Fact]
        public void Apply_with_null_clause_returns_source_unchanged()
        {
            var source = Rows();
            Assert.Same(source, OrderByApplier.Apply<Customer>(source, null));
        }
    }
}
