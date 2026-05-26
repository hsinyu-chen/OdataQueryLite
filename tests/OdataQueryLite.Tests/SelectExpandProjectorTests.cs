#nullable enable
using System.Collections.Generic;
using System.Linq;
using OdataQueryLite.Ast;
using OdataQueryLite.ExpressionBuilding;
using OdataQueryLite.Parsing;
using STJ = System.Text.Json.Serialization;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class SelectExpandProjectorTests
    {
        public sealed class Order
        {
            public int Id { get; set; }
            public int Qty { get; set; }
        }

        public sealed class Customer
        {
            public string Name { get; set; } = "";
            public string Phone { get; set; } = "";
        }

        public sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Price { get; set; }
            public Customer Customer { get; set; } = new();
            public List<Order> Orders { get; set; } = new();

            [STJ.JsonIgnore]
            public string PasswordStj { get; set; } = "secret-stj";

            [Newtonsoft.Json.JsonIgnore]
            public string PasswordNewtonsoft { get; set; } = "secret-newtonsoft";

            [OdataIgnore]
            public string PasswordOwn { get; set; } = "secret-own";
        }

        private static IQueryable<Row> Rows() => new[]
        {
            new Row
            {
                Id = 1, Name = "Apple", Price = 30m,
                Customer = new Customer { Name = "Alice", Phone = "111" },
                Orders = new List<Order> { new() { Id = 10, Qty = 2 }, new() { Id = 11, Qty = 3 } },
            },
            new Row
            {
                Id = 2, Name = "Banana", Price = 10m,
                Customer = new Customer { Name = "Bob", Phone = "222" },
                Orders = new List<Order> { new() { Id = 20, Qty = 1 } },
            },
        }.AsQueryable();

        [Fact]
        public void Flat_select_emits_only_named_scalar_keys()
        {
            var node = new ExpandRequestNode { SelectedFields = new HashSet<string> { "Id", "Name" } };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, projected.Count);
            Assert.Equal(new[] { "Id", "Name" }, projected[0].Keys.OrderBy(k => k));
            Assert.Equal(1, projected[0]["Id"]);
            Assert.Equal("Apple", projected[0]["Name"]);
        }

        [Fact]
        public void Select_skips_unmentioned_scalars()
        {
            var node = new ExpandRequestNode { SelectedFields = new HashSet<string> { "Id" } };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.All(projected, dict =>
            {
                Assert.Single(dict);
                Assert.Contains("Id", dict.Keys);
            });
        }

        [Fact]
        public void Reference_expand_emits_nested_dictionary()
        {
            var node = new ExpandRequestNode
            {
                SelectedFields = new HashSet<string> { "Id" },
            };
            node.ExpandedProperties["Customer"] = new ExpandRequestNode
            {
                SelectedFields = new HashSet<string> { "Name", "Phone" },
            };

            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, projected.Count);
            Assert.Equal(new[] { "Customer", "Id" }, projected[0].Keys.OrderBy(k => k));
            var nested = Assert.IsType<Dictionary<string, object?>>(projected[0]["Customer"]);
            Assert.Equal("Alice", nested["Name"]);
            Assert.Equal("111", nested["Phone"]);
        }

        [Fact]
        public void Collection_expand_emits_list_of_dictionaries()
        {
            var node = new ExpandRequestNode
            {
                SelectedFields = new HashSet<string> { "Id" },
            };
            node.ExpandedProperties["Orders"] = new ExpandRequestNode
            {
                SelectedFields = new HashSet<string> { "Qty" },
            };

            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            var orders = Assert.IsType<List<Dictionary<string, object?>>>(projected[0]["Orders"]);
            Assert.Equal(2, orders.Count);
            Assert.Equal(2, orders[0]["Qty"]);
            Assert.Equal(3, orders[1]["Qty"]);
            Assert.DoesNotContain("Id", orders[0].Keys);
        }

        [Fact]
        public void Stj_JsonIgnore_filters_property_even_when_explicitly_selected()
        {
            var node = new ExpandRequestNode { SelectedFields = new HashSet<string> { "Id", "PasswordStj" } };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.DoesNotContain("PasswordStj", projected[0].Keys);
        }

        [Fact]
        public void Newtonsoft_JsonIgnore_filters_property_even_when_explicitly_selected()
        {
            var node = new ExpandRequestNode { SelectedFields = new HashSet<string> { "Id", "PasswordNewtonsoft" } };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.DoesNotContain("PasswordNewtonsoft", projected[0].Keys);
        }

        [Fact]
        public void OdataIgnore_filters_property_even_when_explicitly_selected()
        {
            var node = new ExpandRequestNode { SelectedFields = new HashSet<string> { "Id", "PasswordOwn" } };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.DoesNotContain("PasswordOwn", projected[0].Keys);
        }

        [Fact]
        public void No_select_no_expand_skips_projection_and_returns_source()
        {
            var node = new ExpandRequestNode();
            var source = Rows();
            var projected = SelectExpandProjector.Project(source, node);

            Assert.Same(source, projected);
        }

        [Fact]
        public void Apply_with_select_routes_through_projector()
        {
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Select = "Id,Name" });
            var result = opts.Apply(Rows());
            var dicts = result.Data.Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, dicts.Count);
            Assert.Equal(new[] { "Id", "Name" }, dicts[0].Keys.OrderBy(k => k));
            Assert.Equal(2, result.Unpaged.LongCount());
        }

        [Fact]
        public void Apply_with_expand_routes_through_projector()
        {
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts
            {
                Select = "Id",
                Expand = "Customer($select=Name)",
            });
            var result = opts.Apply(Rows());
            var dicts = result.Data.Cast<Dictionary<string, object?>>().ToList();

            var nested = Assert.IsType<Dictionary<string, object?>>(dicts[0]["Customer"]);
            Assert.Equal("Alice", nested["Name"]);
            Assert.DoesNotContain("Phone", nested.Keys);
        }

        [Fact]
        public void Apply_select_expand_disabled_keeps_source_type()
        {
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Select = "Id,Name" });
            var result = opts.Apply(Rows(), new ApplyOptions().ApplySelectExpand(false));

            // Data is still IQueryable<Row> — cast succeeds without InvalidCastException.
            var rows = result.Data.Cast<Row>().ToList();
            Assert.Equal(2, rows.Count);
        }
    }
}
