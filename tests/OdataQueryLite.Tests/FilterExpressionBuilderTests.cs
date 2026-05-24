using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using OdataQueryLite.ExpressionBuilding;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class FilterExpressionBuilderTests
    {
        public enum Status { Pending, Active, Cancelled }

        public sealed class Product
        {
            public string Name { get; set; }
            public decimal Price { get; set; }
        }

        public sealed class Order
        {
            public int Id { get; set; }
            public int Quantity { get; set; }
            public Status Status { get; set; }
            public Product Product { get; set; }
        }

        public sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public int? Age { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTimeOffset? LastSeenAt { get; set; }
            public ICollection<Order> Orders { get; set; } = new List<Order>();
            public Status PrimaryStatus { get; set; }
        }

        private sealed record CompiledFilter<T>(Func<T, bool> Match, Type[] SlotTypes, IReadOnlyList<Ast.LiteralValue> Literals);

        private static CompiledFilter<T> Compile<T>(string filter)
        {
            var parsed = FilterParser.Parse(filter);
            var entity = Expression.Parameter(typeof(T), "x");
            var args = Expression.Parameter(typeof(object[]), "args");
            var built = FilterExpressionBuilder.Build<T>(parsed, entity, args);
            var fn = Expression.Lambda<Func<T, object[], bool>>(built.Body, entity, args).Compile();
            var packed = PackArgs(parsed.Literals, built.SlotTypes);
            return new CompiledFilter<T>(row => fn(row, packed), built.SlotTypes, parsed.Literals);
        }

        private static object[] PackArgs(IReadOnlyList<Ast.LiteralValue> literals, Type[] slotTypes)
        {
            var arr = new object[literals.Count];
            for (int i = 0; i < literals.Count; i++)
                arr[i] = TypeCoercion.Coerce(literals[i].Value, literals[i].Kind, slotTypes[i]);
            return arr;
        }

        [Fact]
        public void Eq_on_string_member_matches_exact_value()
        {
            var c = Compile<Customer>("Name eq 'Alice'");
            Assert.Equal(typeof(string), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { Name = "Alice" }));
            Assert.False(c.Match(new Customer { Name = "Bob" }));
        }

        [Fact]
        public void Gt_on_nullable_int_lifted_to_nullable_slot()
        {
            var c = Compile<Customer>("Age gt 30");
            Assert.Equal(typeof(int?), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { Age = 35 }));
            Assert.False(c.Match(new Customer { Age = 30 }));
            Assert.False(c.Match(new Customer { Age = null }));
        }

        [Fact]
        public void Non_nullable_int_compared_to_null_is_silently_false_per_spec()
        {
            // OData v4 Part 2: null is equal only to itself. A non-nullable column compared
            // to null lifts both sides to nullable, evaluating to false at row level.
            var c = Compile<Customer>("Id eq null");
            Assert.Equal(typeof(int?), c.SlotTypes[0]);
            Assert.False(c.Match(new Customer { Id = 0 }));
            Assert.False(c.Match(new Customer { Id = 42 }));
        }

        [Fact]
        public void Enum_member_eq_string_literal_lifts_to_nullable_enum()
        {
            var c = Compile<Customer>("PrimaryStatus eq 'Active'");
            Assert.Equal(typeof(Status?), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { PrimaryStatus = Status.Active }));
            Assert.False(c.Match(new Customer { PrimaryStatus = Status.Pending }));
        }

        [Fact]
        public void Logical_and_or_compose_predicates()
        {
            var c = Compile<Customer>("Name eq 'A' and Age gt 20 or Name eq 'B'");
            Assert.True(c.Match(new Customer { Name = "A", Age = 25 }));
            Assert.False(c.Match(new Customer { Name = "A", Age = 15 }));
            Assert.True(c.Match(new Customer { Name = "B", Age = 5 }));
        }

        [Fact]
        public void Not_negates_predicate()
        {
            var c = Compile<Customer>("not (Name eq 'A')");
            Assert.False(c.Match(new Customer { Name = "A" }));
            Assert.True(c.Match(new Customer { Name = "B" }));
        }

        [Fact]
        public void Contains_on_string_member()
        {
            var c = Compile<Customer>("contains(Name, 'lic')");
            Assert.True(c.Match(new Customer { Name = "Alice" }));
            Assert.False(c.Match(new Customer { Name = "Bob" }));
        }

        [Fact]
        public void Startswith_endswith_tolower_chained()
        {
            var c = Compile<Customer>("startswith(tolower(Name), 'ali')");
            Assert.True(c.Match(new Customer { Name = "ALICE" }));
            Assert.True(c.Match(new Customer { Name = "Alice" }));
            Assert.False(c.Match(new Customer { Name = "Bob" }));
        }

        [Fact]
        public void Length_function_returns_int()
        {
            var c = Compile<Customer>("length(Name) gt 3");
            Assert.True(c.Match(new Customer { Name = "Alice" }));
            Assert.False(c.Match(new Customer { Name = "Al" }));
        }

        [Fact]
        public void Year_function_on_datetime()
        {
            var c = Compile<Customer>("year(CreatedAt) eq 2024");
            Assert.True(c.Match(new Customer { CreatedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc) }));
            Assert.False(c.Match(new Customer { CreatedAt = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc) }));
        }

        [Fact]
        public void Lambda_any_with_body()
        {
            var c = Compile<Customer>("Orders/any(o: o/Quantity gt 5)");
            var matches = new Customer { Orders = { new Order { Quantity = 10 } } };
            var noMatch = new Customer { Orders = { new Order { Quantity = 3 } } };
            Assert.True(c.Match(matches));
            Assert.False(c.Match(noMatch));
        }

        [Fact]
        public void Lambda_all_with_body()
        {
            var c = Compile<Customer>("Orders/all(o: o/Quantity gt 0)");
            var allPositive = new Customer { Orders = { new Order { Quantity = 1 }, new Order { Quantity = 2 } } };
            var hasZero = new Customer { Orders = { new Order { Quantity = 1 }, new Order { Quantity = 0 } } };
            Assert.True(c.Match(allPositive));
            Assert.False(c.Match(hasZero));
        }

        [Fact]
        public void Lambda_any_no_body_is_collection_non_empty_check()
        {
            var c = Compile<Customer>("Orders/any()");
            Assert.True(c.Match(new Customer { Orders = { new Order() } }));
            Assert.False(c.Match(new Customer { Orders = new List<Order>() }));
        }

        [Fact]
        public void Count_path_translates_to_enumerable_count()
        {
            var c = Compile<Customer>("Orders/$count gt 2");
            Assert.True(c.Match(new Customer { Orders = { new Order(), new Order(), new Order() } }));
            Assert.False(c.Match(new Customer { Orders = { new Order(), new Order() } }));
        }

        [Fact]
        public void Unknown_property_throws_with_property_list()
        {
            var ex = Assert.Throws<ArgumentException>(() => Compile<Customer>("Bogus eq 1"));
            Assert.Contains("Bogus", ex.Message);
            Assert.Contains("Customer", ex.Message);
        }

        [Fact]
        public void DateTime_literal_with_Z_is_treated_as_UTC()
        {
            var c = Compile<Customer>("CreatedAt gt 2024-01-01T00:00:00Z");
            Assert.Equal(typeof(DateTime?), c.SlotTypes[0]);
            var packed = TypeCoercion.Coerce(c.Literals[0].Value, c.Literals[0].Kind, c.SlotTypes[0]);
            Assert.Equal(DateTimeKind.Utc, ((DateTime)packed).Kind);
        }

        [Fact]
        public void Enum_member_pack_parses_string_to_enum_value()
        {
            var parsed = FilterParser.Parse("Status eq 'Active'");
            var entity = Expression.Parameter(typeof(Order), "x");
            var args = Expression.Parameter(typeof(object[]), "args");
            var built = FilterExpressionBuilder.Build<Order>(parsed, entity, args);
            Assert.Equal(typeof(Status?), built.SlotTypes[0]);

            var packed = TypeCoercion.Coerce(parsed.Literals[0].Value, parsed.Literals[0].Kind, built.SlotTypes[0]);
            Assert.Equal(Status.Active, packed);
        }

        [Fact]
        public void Null_literal_packs_to_null_in_args_array()
        {
            var c = Compile<Customer>("Email eq null");
            Assert.Equal(typeof(string), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { Email = null }));
            Assert.False(c.Match(new Customer { Email = "x@y" }));
        }

        [Fact]
        public void Year_on_nullable_date_propagates_null_per_spec()
        {
            // LastSeenAt is DateTimeOffset?. year(...) on null row must NOT throw and must
            // not match — OData null-propagation: result is null → comparison silently false.
            var c = Compile<Customer>("year(LastSeenAt) eq 2024");
            Assert.Equal(typeof(int?), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { LastSeenAt = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero) }));
            Assert.False(c.Match(new Customer { LastSeenAt = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero) }));
            Assert.False(c.Match(new Customer { LastSeenAt = null }));
        }

        public sealed class PricingRow
        {
            public decimal Amount { get; set; }
            public decimal? Discount { get; set; }
        }

        [Fact]
        public void Round_on_decimal_dispatches_to_decimal_overload()
        {
            // Math.Round(double) on a decimal column would have thrown at Expression.Call;
            // dispatch on operand type picks Math.Round(decimal) — no double conversion, no
            // precision loss for money.
            var c = Compile<PricingRow>("round(Amount) eq 100");
            Assert.True(c.Match(new PricingRow { Amount = 100.4m }));
            Assert.False(c.Match(new PricingRow { Amount = 100.6m }));
        }

        [Fact]
        public void Floor_on_nullable_decimal_propagates_null()
        {
            var c = Compile<PricingRow>("floor(Discount) eq 5");
            Assert.True(c.Match(new PricingRow { Discount = 5.7m }));
            Assert.False(c.Match(new PricingRow { Discount = 6.2m }));
            Assert.False(c.Match(new PricingRow { Discount = null }));
        }

        public sealed class GuidEntity
        {
            public Guid Id { get; set; }
        }

        [Fact]
        public void Guid_member_eq_string_literal_parses_at_pack()
        {
            // OData PK comparisons commonly send Guid as a string literal.
            var c = Compile<GuidEntity>("Id eq '550e8400-e29b-41d4-a716-446655440000'");
            Assert.Equal(typeof(Guid?), c.SlotTypes[0]);
            Assert.True(c.Match(new GuidEntity { Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000") }));
            Assert.False(c.Match(new GuidEntity { Id = Guid.Empty }));
        }

        public sealed class CustomList : List<Order> { }

        public sealed class Sale
        {
            public CustomList Items { get; set; } = new CustomList();
        }

        [Fact]
        public void Custom_collection_subclass_resolved_via_BaseType_walk()
        {
            // `CustomList : List<Order>` — t.IsGenericType is false but List<Order> is in
            // the BaseType chain. AOT-clean (no GetInterfaces).
            var c = Compile<Sale>("Items/$count gt 1");
            Assert.True(c.Match(new Sale { Items = { new Order(), new Order() } }));
            Assert.False(c.Match(new Sale { Items = { new Order() } }));
        }
    }
}
