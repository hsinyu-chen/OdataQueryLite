using System;
using System.Collections.Generic;
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

        public sealed class Owner
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public ICollection<Address> Addresses { get; set; } = new List<Address>();
        }

        private static IQueryable<Owner> OwnersWithAddressCounts() => new[]
        {
            new Owner { Id = 1, Label = "A", Addresses = { new Address { City = "X" }, new Address { City = "Y" } } },
            new Owner { Id = 2, Label = "B", Addresses = { new Address { City = "X" } } },
            new Owner { Id = 3, Label = "C", Addresses = { new Address { City = "X" }, new Address { City = "Y" }, new Address { City = "Z" } } },
        }.AsQueryable();

        [Fact]
        public void Apply_orderby_collection_count_terminal_sorts_by_count()
        {
            var clause = OrderByParser.Parse("Addresses/$count");
            var ordered = OrderByApplier.Apply(OwnersWithAddressCounts(), clause).ToList();
            Assert.Equal([2, 1, 3], ordered.Select(o => o.Id));
        }

        [Fact]
        public void Apply_orderby_collection_count_desc_supported()
        {
            var clause = OrderByParser.Parse("Addresses/$count desc");
            var ordered = OrderByApplier.Apply(OwnersWithAddressCounts(), clause).ToList();
            Assert.Equal([3, 1, 2], ordered.Select(o => o.Id));
        }

        [Fact]
        public void Apply_count_on_non_collection_throws()
        {
            var clause = OrderByParser.Parse("Label/$count");
            Assert.Throws<OdataQueryException>(() => OrderByApplier.Apply(OwnersWithAddressCounts(), clause).ToList());
        }

        [Fact]
        public void Apply_count_not_terminal_throws()
        {
            // Addresses/$count/Foo — $count must be terminal.
            var clause = new Ast.OrderByClause([
                new Ast.OrderByItem(new Ast.MemberNode(["Addresses", "$count", "Foo"]), Ast.OrderByDirection.Ascending)
            ]);
            Assert.Throws<OdataQueryException>(() => OrderByApplier.Apply(OwnersWithAddressCounts(), clause).ToList());
        }
    }
}
