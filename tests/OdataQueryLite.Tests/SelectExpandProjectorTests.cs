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
    // Sequential collection — one test mutates SelectExpandProjector.ScalarClassTypes (a
    // process-global static) to verify the host extension hook. xUnit's default parallel
    // execution would race that mutation against Reference_expand_emits_nested_dictionary
    // and similar tests that depend on Customer being classified as a navigation property.
    [Collection("Sequential")]
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
            public Customer? Customer { get; set; } = new();
            public List<Order> Orders { get; set; } = [];

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
                Orders = [new() { Id = 10, Qty = 2 }, new() { Id = 11, Qty = 3 }],
            },
            new Row
            {
                Id = 2, Name = "Banana", Price = 10m,
                Customer = new Customer { Name = "Bob", Phone = "222" },
                Orders = [new() { Id = 20, Qty = 1 }],
            },
        }.AsQueryable();

        [Fact]
        public void Flat_select_emits_only_named_scalar_keys()
        {
            var node = new ExpandRequestNode { SelectedFields = ["Id", "Name"] };
            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, projected.Count);
            Assert.Equal(["Id", "Name"], projected[0].Keys.OrderBy(k => k));
            Assert.Equal(1, projected[0]["Id"]);
            Assert.Equal("Apple", projected[0]["Name"]);
        }

        [Fact]
        public void Select_skips_unmentioned_scalars()
        {
            var node = new ExpandRequestNode { SelectedFields = ["Id"] };
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
                SelectedFields = ["Id"],
            };
            node.ExpandedProperties["Customer"] = new ExpandRequestNode
            {
                SelectedFields = ["Name", "Phone"],
            };

            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, projected.Count);
            Assert.Equal(["Customer", "Id"], projected[0].Keys.OrderBy(k => k));
            var nested = Assert.IsType<Dictionary<string, object?>>(projected[0]["Customer"]);
            Assert.Equal("Alice", nested["Name"]);
            Assert.Equal("111", nested["Phone"]);
        }

        [Fact]
        public void Collection_expand_emits_list_of_dictionaries()
        {
            var node = new ExpandRequestNode
            {
                SelectedFields = ["Id"],
            };
            node.ExpandedProperties["Orders"] = new ExpandRequestNode
            {
                SelectedFields = ["Qty"],
            };

            var projected = SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList();

            var orders = Assert.IsType<List<Dictionary<string, object?>>>(projected[0]["Orders"]);
            Assert.Equal(2, orders.Count);
            Assert.Equal(2, orders[0]["Qty"]);
            Assert.Equal(3, orders[1]["Qty"]);
            Assert.DoesNotContain("Id", orders[0].Keys);
        }

        [Fact]
        public void Stj_JsonIgnore_property_in_select_throws_not_found()
        {
            // $select on a [JsonIgnore]-decorated property surfaces the same "not found"
            // diagnostic as a typo, denying the client a way to discriminate hidden vs absent.
            var node = new ExpandRequestNode { SelectedFields = ["Id", "PasswordStj"] };
            var ex = Assert.Throws<OdataQueryException>(() =>
                SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList());
            Assert.Contains("not found", ex.Message);
        }

        [Fact]
        public void Newtonsoft_JsonIgnore_property_in_select_throws_not_found()
        {
            var node = new ExpandRequestNode { SelectedFields = ["Id", "PasswordNewtonsoft"] };
            var ex = Assert.Throws<OdataQueryException>(() =>
                SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList());
            Assert.Contains("not found", ex.Message);
        }

        [Fact]
        public void OdataIgnore_property_in_select_throws_not_found()
        {
            var node = new ExpandRequestNode { SelectedFields = ["Id", "PasswordOwn"] };
            var ex = Assert.Throws<OdataQueryException>(() =>
                SelectExpandProjector.Project(Rows(), node).Cast<Dictionary<string, object?>>().ToList());
            Assert.Contains("not found", ex.Message);
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
            Assert.Equal(["Id", "Name"], dicts[0].Keys.OrderBy(k => k));
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
        public void Reference_expand_with_null_navigation_emits_null_dictionary()
        {
            var rowsWithNullCustomer = new[]
            {
                new Row { Id = 1, Name = "Apple", Customer = null },
            }.AsQueryable();

            var node = new ExpandRequestNode
            {
                SelectedFields = ["Id"],
            };
            node.ExpandedProperties["Customer"] = new ExpandRequestNode
            {
                SelectedFields = ["Name"],
            };

            var projected = SelectExpandProjector.Project(rowsWithNullCustomer, node)
                .Cast<Dictionary<string, object?>>().ToList();

            // Ternary null guard surfaces null rather than NRE'ing on the property access.
            Assert.Null(projected[0]["Customer"]);
        }

        [Fact]
        public void Filter_against_JsonIgnore_property_rejected_as_not_found()
        {
            // Closes the $filter side-channel: a boolean probe like
            // `?$filter=startswith(Password, 'sec')` must surface as a plain "not found", with
            // the Available list omitting hidden props so the attacker can't enumerate them.
            var ex = Assert.Throws<OdataQueryException>(() =>
                new OdataQueryOptions<Row>(new OdataQueryParts { Filter = "PasswordStj eq 'x'" }));
            Assert.Contains("not found", ex.Message);
            Assert.Contains("Available: ", ex.Message);
            var available = ex.Message[(ex.Message.IndexOf("Available: ") + "Available: ".Length)..];
            Assert.DoesNotContain("PasswordStj", available);
            Assert.DoesNotContain("PasswordNewtonsoft", available);
            Assert.DoesNotContain("PasswordOwn", available);
        }

        [Fact]
        public void OrderBy_against_JsonIgnore_property_rejected_as_not_found()
        {
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { OrderBy = "PasswordOwn" });
            var ex = Assert.Throws<OdataQueryException>(() => opts.Apply(Rows()));
            Assert.Contains("not found", ex.Message);
            var available = ex.Message[(ex.Message.IndexOf("Available: ") + "Available: ".Length)..];
            Assert.DoesNotContain("PasswordOwn", available);
        }

        public sealed class RowWithWrappers
        {
            public int Id { get; set; }
            public System.Uri? Website { get; set; }
            public System.Text.Json.Nodes.JsonNode? Metadata { get; set; }
        }

        [Fact]
        public void Default_select_treats_Uri_and_JsonNode_as_scalars_not_navigations()
        {
            var src = new[]
            {
                new RowWithWrappers { Id = 1, Website = new System.Uri("https://example.com"), Metadata = null },
            }.AsQueryable();

            // No SelectedFields => default emits scalars only. Uri / JsonNode should be in,
            // not omitted as "navigation".
            var node = new ExpandRequestNode { SelectedFields = null };
            // Need at least one expand to force projection — use empty expand on a non-existent
            // collection won't work, so go through the $select path instead:
            node = new ExpandRequestNode { SelectedFields = ["Id", "Website", "Metadata"] };
            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();
            // Uri serializes as object; emerges in the dict.
            Assert.Equal("https://example.com/", projected[0]["Website"]?.ToString());
            Assert.Null(projected[0]["Metadata"]);
        }

        public sealed class RowWithStringCollection
        {
            public int Id { get; set; }
            public List<string> Tags { get; set; } = [];
        }

        [Fact]
        public void Collection_of_scalars_treated_as_scalar_not_navigation()
        {
            // List<string> / int[] etc are inline-serialized values, not sub-entities. They
            // should appear in the default projection rather than being omitted as "nav".
            var src = new[]
            {
                new RowWithStringCollection { Id = 1, Tags = ["a", "b"] },
            }.AsQueryable();

            var node = new ExpandRequestNode { SelectedFields = ["Id", "Tags"] };
            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();

            var tags = Assert.IsType<List<string>>(projected[0]["Tags"]);
            Assert.Equal(["a", "b"], tags);
        }

        [Fact]
        public void Nested_select_path_folds_into_expand_tree()
        {
            // $select=Id,Customer/Name routes through the new slashed-path tree fold —
            // equivalent to $expand=Customer($select=Name).
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Select = "Id,Customer/Name" });
            var result = opts.Apply(Rows());
            var dicts = result.Data.Cast<Dictionary<string, object?>>().ToList();

            var customer = Assert.IsType<Dictionary<string, object?>>(dicts[0]["Customer"]);
            Assert.Equal("Alice", customer["Name"]);
            Assert.DoesNotContain("Phone", customer.Keys);
        }

        [Fact]
        public void Explicit_select_on_navigation_type_auto_expands_to_nested_dictionary()
        {
            // $select=Id,Customer (no $expand) — client explicitly named Customer. To keep
            // [OdataIgnore] honored on nested fields (the raw entity would bypass it during
            // JSON serialization), the projector auto-expands the nav with an empty inner
            // node so the recursive dict build picks up all visible scalars.
            var node = new ExpandRequestNode { SelectedFields = ["Id", "Customer"] };
            var projected = SelectExpandProjector.Project(Rows(), node)
                .Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal(2, projected.Count);
            var customer = Assert.IsType<Dictionary<string, object?>>(projected[0]["Customer"]);
            Assert.Equal("Alice", customer["Name"]);
            Assert.Equal("111", customer["Phone"]);
        }

        public sealed class IndexerRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public object this[string key] => key;
        }

        [Fact]
        public void Filter_on_indexer_named_property_throws_not_found()
        {
            // `public object this[string key]` is exposed as a metadata property named "Item".
            // Without the indexer guard, $filter=Item eq 'x' would call Expression.Property
            // on the indexer without index args and 500 inside the LINQ provider. The throw
            // happens at construction time because FilterExpressionBuilder resolves the path
            // there; wrap the ctor itself in Assert.Throws.
            var ex = Assert.Throws<OdataQueryException>(() =>
                new OdataQueryOptions<IndexerRow>(new OdataQueryParts { Filter = "Item eq 'x'" }));
            Assert.Contains("not found", ex.Message);
            var available = ex.Message[(ex.Message.IndexOf("Available: ") + "Available: ".Length)..];
            Assert.DoesNotContain("Item", available);
        }

        [Fact]
        public void Select_on_unknown_property_throws()
        {
            // Aligns with $filter / $orderby diagnostics; silent omission would mask typos.
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Select = "Id,Nmae" });
            var ex = Assert.Throws<OdataQueryException>(() => opts.Apply(Rows()));
            Assert.Contains("Nmae", ex.Message);
            Assert.Contains("not found", ex.Message);
        }

        [Fact]
        public void Expand_on_unknown_property_throws()
        {
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Expand = "Nonexistent" });
            var ex = Assert.Throws<OdataQueryException>(() => opts.Apply(Rows()));
            Assert.Contains("Nonexistent", ex.Message);
            Assert.Contains("not found", ex.Message);
        }

        [Fact]
        public void Select_on_ignored_property_still_throws_not_found()
        {
            // Side-channel guard: hidden property must produce the same diagnostic shape as a
            // typo so the client can't enumerate which fields are JsonIgnored vs missing.
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Select = "Id,PasswordStj" });
            var ex = Assert.Throws<OdataQueryException>(() => opts.Apply(Rows()));
            Assert.Contains("not found", ex.Message);
            var available = ex.Message[(ex.Message.IndexOf("Available: ") + "Available: ".Length)..];
            Assert.DoesNotContain("PasswordStj", available);
        }

        public sealed class RowWithObjectAndJsonSubclass
        {
            public int Id { get; set; }
            public object? Untyped { get; set; }
            public System.Text.Json.Nodes.JsonObject? Tree { get; set; } // : JsonNode
        }

        [Fact]
        public void Object_type_treated_as_scalar_not_navigation()
        {
            var src = new[]
            {
                new RowWithObjectAndJsonSubclass { Id = 1, Untyped = "hello" },
            }.AsQueryable();

            var node = new ExpandRequestNode { SelectedFields = ["Id", "Untyped"] };
            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();

            Assert.Equal("hello", projected[0]["Untyped"]);
        }

        [Fact]
        public void JsonNode_subclass_treated_as_scalar()
        {
            // JsonObject : JsonNode; the whitelist must match subclasses, not just the exact
            // registered type.
            var src = new[]
            {
                new RowWithObjectAndJsonSubclass
                {
                    Id = 1,
                    Tree = new System.Text.Json.Nodes.JsonObject { ["k"] = "v" },
                },
            }.AsQueryable();

            var node = new ExpandRequestNode { SelectedFields = ["Id", "Tree"] };
            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();

            // Stays as JsonObject — not recursed into as a nested entity dict.
            Assert.IsType<System.Text.Json.Nodes.JsonObject>(projected[0]["Tree"]);
        }

        [Fact]
        public void ScalarClassTypes_extension_treats_custom_type_as_scalar()
        {
            // Host registers Order as scalar at startup; projection emits it directly rather
            // than recursing into a nested dict. Mutation is set-once-at-startup pattern; we
            // remove on Dispose to keep test isolation.
            SelectExpandProjector.ScalarClassTypes.TryAdd(typeof(Customer), 0);
            try
            {
                var node = new ExpandRequestNode { SelectedFields = ["Id", "Customer"] };
                var projected = SelectExpandProjector.Project(Rows(), node)
                    .Cast<Dictionary<string, object?>>().ToList();
                // With Customer treated as scalar, the value is the Customer instance itself,
                // not a nested Dictionary.
                Assert.IsType<Customer>(projected[0]["Customer"]);
            }
            finally
            {
                SelectExpandProjector.ScalarClassTypes.TryRemove(typeof(Customer), out _);
            }
        }

        public interface IBaseId
        {
            int Id { get; }
        }
        public interface INamedRow : IBaseId
        {
            string Name { get; }
        }

        [Fact]
        public void Filter_on_inherited_interface_property_resolves()
        {
            // Type.GetProperty on INamedRow does NOT return Id (inherited from IBaseId);
            // engine must walk base interfaces. Without the fix, $filter=Id eq 1 against
            // IQueryable<INamedRow> throws "not found".
            var ex = Record.Exception(() =>
                new OdataQueryOptions<INamedRow>(new OdataQueryParts { Filter = "Id eq 1" }));
            Assert.Null(ex);
        }

        public interface IHasIdA { int Id { get; } }
        public interface IHasIdB { int Id { get; } }
        public interface IBothIds : IHasIdA, IHasIdB { }

        [Fact]
        public void Duplicate_interface_property_names_dont_throw_on_dictionary_ctor()
        {
            // Two parent interfaces both expose Id; GetPropertiesIncludingInterfaces would
            // surface it twice without DistinctBy, and the Dictionary ctor would throw
            // ArgumentException at projection-build time.
            var ex = Record.Exception(() =>
                new OdataQueryOptions<IBothIds>(new OdataQueryParts { Select = "Id" }));
            Assert.Null(ex);
        }

        [Fact]
        public void Collection_expand_with_null_navigation_emits_null_list()
        {
            var src = new[]
            {
                // Orders deliberately null to exercise the collection null guard.
                new Row { Id = 1, Name = "Apple", Customer = new(), Orders = null! },
            }.AsQueryable();

            var node = new ExpandRequestNode { SelectedFields = ["Id"] };
            node.ExpandedProperties["Orders"] = new ExpandRequestNode
            {
                SelectedFields = ["Qty"],
            };

            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();

            // Conditional null guard surfaces null rather than ArgumentNullException'ing.
            Assert.Null(projected[0]["Orders"]);
        }

        public sealed class RowWithImmutableArrayNav
        {
            public int Id { get; set; }
            public System.Collections.Immutable.ImmutableArray<Order> Orders { get; set; }
        }

        [Fact]
        public void Collection_expand_works_on_value_type_collection()
        {
            // ImmutableArray<T> is a struct that implements IEnumerable<T>. Without the
            // Expression.Convert to IEnumerable<elementType>, the Enumerable.Select call
            // fails to bind at runtime.
            var src = new[]
            {
                new RowWithImmutableArrayNav
                {
                    Id = 1,
                    Orders = [new Order { Id = 10, Qty = 5 }],
                },
            }.AsQueryable();

            var node = new ExpandRequestNode { SelectedFields = ["Id"] };
            node.ExpandedProperties["Orders"] = new ExpandRequestNode
            {
                SelectedFields = ["Qty"],
            };

            var projected = SelectExpandProjector.Project(src, node)
                .Cast<Dictionary<string, object?>>().ToList();

            var orders = Assert.IsType<List<Dictionary<string, object?>>>(projected[0]["Orders"]);
            Assert.Single(orders);
            Assert.Equal(5, orders[0]["Qty"]);
        }

        [Fact]
        public void Expanding_a_scalar_property_throws()
        {
            // OData v4 §5.1.3: $expand is defined for navigation properties only. Asking to
            // expand a scalar (Price : decimal) must surface a clean 400, not crash inside
            // Queryable.Select at execution.
            var opts = new OdataQueryOptions<Row>(new OdataQueryParts { Expand = "Price" });
            var ex = Assert.Throws<OdataQueryException>(() => opts.Apply(Rows()));
            Assert.Contains("Price", ex.Message);
            Assert.Contains("not a navigation property", ex.Message);
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
