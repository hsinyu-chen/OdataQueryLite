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
            public DateOnly BirthDate { get; set; }
            public DateOnly? AnniversaryDate { get; set; }
            public bool? IsActive { get; set; }
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
            // Slot widens to decimal? — integer member vs Number literal promotes to the
            // wider numeric so fractional literals don't narrow.
            Assert.Equal(typeof(decimal?), c.SlotTypes[0]);
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
            // year returns int; literal is Number (decimal). Slot widens to decimal?.
            Assert.Equal(typeof(decimal?), c.SlotTypes[0]);
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

        public sealed class SetEntity
        {
            public HashSet<Order> Tagged { get; set; } = new();
            public ISet<Order> Marked { get; set; } = new HashSet<Order>();
        }

        [Fact]
        public void ISet_navigation_resolved_by_collection_element_type_lookup()
        {
            var c = Compile<SetEntity>("Marked/any(o: o/Quantity gt 0)");
            var match = new SetEntity { Marked = new HashSet<Order> { new() { Quantity = 5 } } };
            var miss = new SetEntity { Marked = new HashSet<Order> { new() { Quantity = 0 } } };
            Assert.True(c.Match(match));
            Assert.False(c.Match(miss));
        }

        public sealed class Nested
        {
            public ICollection<Inner> Outers { get; set; } = new List<Inner>();
        }
        public sealed class Inner
        {
            public int Threshold { get; set; }
            public ICollection<Leaf> Children { get; set; } = new List<Leaf>();
        }
        public sealed class Leaf
        {
            public int Value { get; set; }
        }

        [Fact]
        public void Nested_lambda_inner_scope_can_reference_outer_scope()
        {
            // Outers where ANY outer has at least one child whose Value > outer's Threshold —
            // inner `i` references outer scope `o`'s member.
            var c = Compile<Nested>("Outers/any(o: o/Children/any(i: i/Value gt o/Threshold))");

            var matches = new Nested
            {
                Outers =
                {
                    new Inner { Threshold = 10, Children = { new Leaf { Value = 50 } } }
                }
            };
            var noMatch = new Nested
            {
                Outers =
                {
                    new Inner { Threshold = 100, Children = { new Leaf { Value = 50 } } }
                }
            };
            Assert.True(c.Match(matches));
            Assert.False(c.Match(noMatch));
        }

        // OData spec: function returning null when arg is null. We collapse to false for bool
        // returns and null for non-bool — matches user intent (Phase 1.B.8 design call).
        [Fact]
        public void Contains_on_null_member_does_not_throw_and_returns_false()
        {
            var c = Compile<Customer>("contains(Name, 'a')");
            Assert.False(c.Match(new Customer { Name = null }));
            Assert.True(c.Match(new Customer { Name = "alice" }));
        }

        [Fact]
        public void Startswith_on_null_member_does_not_throw_and_returns_false()
        {
            var c = Compile<Customer>("startswith(Name, 'a')");
            Assert.False(c.Match(new Customer { Name = null }));
            Assert.True(c.Match(new Customer { Name = "alice" }));
        }

        [Fact]
        public void Tolower_on_null_member_returns_null_and_compare_excludes_row()
        {
            var c = Compile<Customer>("tolower(Name) eq 'alice'");
            Assert.False(c.Match(new Customer { Name = null }));
            Assert.True(c.Match(new Customer { Name = "ALICE" }));
        }

        [Fact]
        public void Length_on_null_member_returns_null_and_compare_excludes_row()
        {
            var c = Compile<Customer>("length(Name) eq 5");
            Assert.False(c.Match(new Customer { Name = null }));
            Assert.True(c.Match(new Customer { Name = "Alice" }));
        }

        [Fact]
        public void Concat_with_null_arg_returns_null()
        {
            var c = Compile<Customer>("concat(Name, Email) eq 'AliceX'");
            Assert.False(c.Match(new Customer { Name = null, Email = "X" }));
            Assert.False(c.Match(new Customer { Name = "Alice", Email = null }));
            Assert.True(c.Match(new Customer { Name = "Alice", Email = "X" }));
        }

        [Fact]
        public void Comparison_of_bool_returning_subexpression_uses_value_equality()
        {
            // Bool-returning subexpressions (any / not / nested compare) compared to a bool
            // literal must use value equality, not boxed reference equality.
            var any = Compile<Customer>("Orders/any() eq true");
            Assert.True(any.Match(new Customer { Orders = { new Order() } }));
            Assert.False(any.Match(new Customer { Orders = new List<Order>() }));

            var neg = Compile<Customer>("(Name eq 'A') eq false");
            Assert.True(neg.Match(new Customer { Name = "B" }));
            Assert.False(neg.Match(new Customer { Name = "A" }));
        }

        [Fact]
        public void Literal_eq_literal_uses_value_equality()
        {
            // Dynamic-builder `WHERE 1 = 1 AND …` idiom: both operands are ParamRef literals.
            // Without an explicit slot, both lift to typeof(object) and equality runs on the
            // boxed primitives, returning false for identical values in-memory.
            Assert.True(Compile<Customer>("1 eq 1").Match(new Customer()));
            Assert.False(Compile<Customer>("1 eq 2").Match(new Customer()));
            Assert.True(Compile<Customer>("'a' eq 'a'").Match(new Customer()));
            Assert.True(Compile<Customer>("true eq true").Match(new Customer()));
            Assert.True(Compile<Customer>("1 eq 1 and Name eq 'Alice'").Match(new Customer { Name = "Alice" }));
        }

        [Fact]
        public void Substring_numeric_args_keep_nullable_slot_invariant()
        {
            // Numeric arg slots stay nullable per the engine-wide invariant — a packed null
            // would otherwise unbox-NRE inside the Convert(args[i], int) at the literal site.
            var c = Compile<Customer>("substring(Name, 1) eq 'lice'");
            Assert.Equal(typeof(int?), c.SlotTypes[0]);
            Assert.True(c.Match(new Customer { Name = "Alice" }));
            Assert.False(c.Match(new Customer { Name = "Bob" }));
        }

        [Fact]
        public void Non_boolean_filter_body_throws_clear_message()
        {
            // Per OData v4 a $filter expression must evaluate to bool. A bare non-bool member
            // (e.g. `filter=Name`) used to fail with an inner Expression.Equal "operator not
            // defined" — confusing for callers. Surface the spec contract directly.
            var ex = Assert.Throws<ArgumentException>(() => Compile<Customer>("Name"));
            Assert.Contains("boolean", ex.Message);
            Assert.Contains("String", ex.Message);
        }

        [Fact]
        public void Int_member_eq_25_fractional_literal_should_not_match_id_2()
        {
            // Spec: 2 eq 2.5 must be false. Current slot picks Id (int) and Coerce(2.5m,
            // Number, int?) banker-rounds to 2 — making `Id eq 2.5` against Id=2 a spurious
            // hit. Failing test documents the bug for the follow-up common-type widening fix.
            Assert.False(Compile<Customer>("Id eq 2.5").Match(new Customer { Id = 2 }));
        }

        [Fact]
        public void Not_on_nullable_bool_member_lifts_through_top_level_collapse()
        {
            // Expression.Not is lifted on bool? — not(null) = null, then the top-level
            // collapse turns null into false (row excluded). Per OData v4 §5.1.1.5.1
            // "not null = null", which in a filter context silently excludes the row.
            Assert.False(Compile<Customer>("not IsActive").Match(new Customer { IsActive = true }));
            Assert.True(Compile<Customer>("not IsActive").Match(new Customer { IsActive = false }));
            Assert.False(Compile<Customer>("not IsActive").Match(new Customer { IsActive = null }));
        }

        [Fact]
        public void And_or_of_nullable_bool_members_compiles_and_collapses_to_false_on_null()
        {
            // AndAlso/OrElse over bool? operands: lifted result is bool?, top-level fix
            // collapses to bool. Mixed bool / bool? operands must also compose without
            // ArgumentException at Expression construction.
            Assert.True(Compile<Customer>("IsActive and IsActive").Match(new Customer { IsActive = true }));
            Assert.False(Compile<Customer>("IsActive and IsActive").Match(new Customer { IsActive = null }));
            Assert.True(Compile<Customer>("IsActive or (Age gt 30)").Match(new Customer { IsActive = null, Age = 40 }));
            Assert.False(Compile<Customer>("IsActive or (Age gt 30)").Match(new Customer { IsActive = null, Age = 20 }));
        }

        [Fact]
        public void Contains_with_null_string_arg_collapses_to_false_per_spec()
        {
            // ParamRef null packed as string at the arg slot must not reach BCL Contains —
            // string.Contains(null) throws ArgumentNullException. Per OData v4, the function
            // returns null (silently false in the outer compare).
            Assert.False(Compile<Customer>("contains(Name, null)").Match(new Customer { Name = "Alice" }));
            Assert.False(Compile<Customer>("startswith(Name, null)").Match(new Customer { Name = "Alice" }));
            Assert.False(Compile<Customer>("endswith(Name, null)").Match(new Customer { Name = "Alice" }));
            Assert.False(Compile<Customer>("indexof(Name, null) eq 0").Match(new Customer { Name = "Alice" }));
        }

        [Fact]
        public void Bare_nullable_bool_member_compiles_and_treats_null_as_false()
        {
            // Body type would be bool? if Equal lifted to nullable; Lambda<Func<T,bool>>
            // creation would then throw on type mismatch. The top-level Equal must collapse
            // to bool (treating null as false per OData spec).
            Assert.True(Compile<Customer>("IsActive").Match(new Customer { IsActive = true }));
            Assert.False(Compile<Customer>("IsActive").Match(new Customer { IsActive = false }));
            Assert.False(Compile<Customer>("IsActive").Match(new Customer { IsActive = null }));
        }

        [Fact]
        public void Date_functions_support_DateOnly_per_Edm_Date_mapping()
        {
            // OData v4 Edm.Date maps to .NET DateOnly. year/month/day must read directly from
            // the DateOnly property; the value-typed and nullable forms both route through
            // DateProperty's effective-type check.
            var d = new DateOnly(2026, 5, 25);
            var c1 = Compile<Customer>("year(BirthDate) eq 2026").Match(new Customer { BirthDate = d });
            Assert.True(c1);
            Assert.True(Compile<Customer>("month(BirthDate) eq 5").Match(new Customer { BirthDate = d }));
            Assert.True(Compile<Customer>("day(BirthDate) eq 25").Match(new Customer { BirthDate = d }));
            Assert.True(Compile<Customer>("year(AnniversaryDate) eq 2026").Match(new Customer { AnniversaryDate = d }));
            // Nullable null propagation per spec.
            Assert.False(Compile<Customer>("year(AnniversaryDate) eq 2026").Match(new Customer { AnniversaryDate = null }));
        }

        [Fact]
        public void Substring_with_null_numeric_arg_returns_null_per_spec()
        {
            // OData v4 functions return null when any argument is null. The numeric arg slot is
            // nullable so a packed-null int? must short-circuit before UnwrapNullableInt's .Value
            // dereference. Result: silently false on `... eq <literal>` per the null-comparison rule.
            Assert.False(Compile<Customer>("substring(Name, null) eq 'lice'").Match(new Customer { Name = "Alice" }));
            Assert.False(Compile<Customer>("substring(Name, 1, null) eq 'lice'").Match(new Customer { Name = "Alice" }));
            // Null instance still short-circuits to null too (existing guard).
            Assert.False(Compile<Customer>("substring(Name, 1) eq 'lice'").Match(new Customer { Name = null }));
        }

        [Fact]
        public void IndexOf_returns_int_and_null_member_propagates_null()
        {
            // IndexOf returns int; the null-guard's null path must lift to int? so both
            // branches of the Condition share a type.
            var c = Compile<Customer>("indexof(Name, 'lic') eq 1");
            Assert.True(c.Match(new Customer { Name = "Alice" }));     // "lic" at index 1
            Assert.False(c.Match(new Customer { Name = "Bob" }));
            Assert.False(c.Match(new Customer { Name = null }));
        }
    }
}
