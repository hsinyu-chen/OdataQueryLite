using System.Linq;
using OdataQueryLite.Ast;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class FilterParserTests
    {
        [Theory]
        [InlineData("Name eq 'X'")]
        [InlineData("Name ne null")]
        [InlineData("Amount gt 100")]
        [InlineData("contains(Code, 'abc')")]
        [InlineData("startswith(Code, 'abc')")]
        [InlineData("endswith(Code, 'abc')")]
        [InlineData("(A eq 1 or A eq 2) and B ne null")]
        [InlineData("Customer/Name eq 'X'")]
        [InlineData("Status eq 'Active'")]
        [InlineData("CreatedTime gt 2024-01-01T00:00:00Z")]
        [InlineData("Name eq 'O''Brien'")]
        [InlineData("not contains(Code, 'X')")]
        [InlineData("IsActive eq true")]
        public void Parses_without_throwing(string input)
        {
            var result = FilterParser.Parse(input);
            Assert.NotNull(result.Ast);
        }

        [Fact]
        public void Eq_with_string_emits_parameterized_string()
        {
            var r = FilterParser.Parse("Name eq 'X'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            Assert.Equal(BinaryOp.Eq, bin.Op);
            var member = Assert.IsType<MemberNode>(bin.Left);
            Assert.Equal(new[] { "Name" }, member.Path);
            var p = Assert.IsType<ParamRefNode>(bin.Right);
            Assert.Equal(0, p.Index);
            Assert.Equal(LiteralKind.String, p.Kind);
            Assert.Single(r.Literals);
            Assert.Equal("X", r.Literals[0].Value);
        }

        [Fact]
        public void Null_compare_is_parameterized_to_keep_cache_shape_stable()
        {
            // Null goes through the literals list like every other value so that
            // a query template doesn't fragment the cache by null vs non-null arg pattern.
            var r = FilterParser.Parse("Name ne null");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            Assert.Equal(BinaryOp.Ne, bin.Op);
            var p = Assert.IsType<ParamRefNode>(bin.Right);
            Assert.Equal(LiteralKind.Null, p.Kind);
            Assert.Single(r.Literals);
            Assert.Null(r.Literals[0].Value);
            Assert.Equal(LiteralKind.Null, r.Literals[0].Kind);
        }

        [Fact]
        public void Nested_property_path_collected_into_member_node()
        {
            var r = FilterParser.Parse("Customer/Name eq 'X'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var member = Assert.IsType<MemberNode>(bin.Left);
            Assert.Equal(new[] { "Customer", "Name" }, member.Path);
        }

        [Fact]
        public void Or_and_grouping_respects_paren_precedence()
        {
            var r = FilterParser.Parse("(A eq 1 or A eq 2) and B ne null");
            var top = Assert.IsType<BinaryNode>(r.Ast);
            Assert.Equal(BinaryOp.And, top.Op);
            var orNode = Assert.IsType<BinaryNode>(top.Left);
            Assert.Equal(BinaryOp.Or, orNode.Op);
        }

        [Fact]
        public void Endswith_recognized_as_endswith_function()
        {
            var r = FilterParser.Parse("endswith(Code, 'abc')");
            var fn = Assert.IsType<FunctionNode>(r.Ast);
            Assert.Equal(FunctionName.EndsWith, fn.Name);
            Assert.Equal(2, fn.Args.Count);
        }

        [Fact]
        public void Endwith_typo_no_longer_recognized()
        {
            // The frontend's OdataFilter.ts had an 'endwith' method but grep showed 0 call sites
            // across this repo's frontend. Backend now follows the OData spec strictly.
            Assert.Throws<FilterSyntaxException>(() => FilterParser.Parse("endwith(Code, 'abc')"));
        }

        [Fact]
        public void Datetime_literal_parsed_as_datetimeoffset()
        {
            var r = FilterParser.Parse("CreatedTime gt 2024-01-01T00:00:00Z");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var p = Assert.IsType<ParamRefNode>(bin.Right);
            Assert.Equal(LiteralKind.DateTime, p.Kind);
            var lit = r.Literals[p.Index];
            Assert.IsType<System.DateTimeOffset>(lit.Value);
        }

        [Fact]
        public void Escaped_quote_in_string_literal_preserved()
        {
            var r = FilterParser.Parse("Name eq 'O''Brien'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var p = Assert.IsType<ParamRefNode>(bin.Right);
            Assert.Equal("O'Brien", r.Literals[p.Index].Value);
        }

        [Fact]
        public void Not_prefix_wraps_function_in_unary_node()
        {
            var r = FilterParser.Parse("not contains(Code, 'X')");
            var u = Assert.IsType<UnaryNode>(r.Ast);
            Assert.Equal(UnaryOp.Not, u.Op);
            var fn = Assert.IsType<FunctionNode>(u.Operand);
            Assert.Equal(FunctionName.Contains, fn.Name);
        }

        [Fact]
        public void Literals_collected_in_left_to_right_order()
        {
            var r = FilterParser.Parse("A eq 1 and B eq 'two'");
            Assert.Equal(2, r.Literals.Count);
            Assert.Equal(1L, r.Literals[0].Value);
            Assert.Equal("two", r.Literals[1].Value);
        }

        [Theory]
        [InlineData("Name eq ")]
        [InlineData("Name eq 'unterminated")]
        [InlineData("(A eq 1")]
        [InlineData("contains(Code")]
        public void Malformed_input_throws_filter_syntax_exception(string input)
        {
            Assert.Throws<FilterSyntaxException>(() => FilterParser.Parse(input));
        }

        // OdataDataSource OdataFilter.ts:7-8 stringOrDateFunction surface — frontend can
        // emit any of these as `${fn}(${path}[,${arg}...])` wrapping a property reference.
        [Theory]
        [InlineData("tolower(Name) eq 'foo'", FunctionName.ToLower)]
        [InlineData("toupper(Name) eq 'FOO'", FunctionName.ToUpper)]
        [InlineData("trim(Name) eq 'foo'", FunctionName.Trim)]
        [InlineData("length(Name) gt 3", FunctionName.Length)]
        [InlineData("indexof(Name, 'x') ge 0", FunctionName.IndexOf)]
        [InlineData("substring(Name, 1, 3) eq 'oob'", FunctionName.Substring)]
        [InlineData("concat(First, Last) eq 'JohnDoe'", FunctionName.Concat)]
        [InlineData("year(CreatedTime) eq 2026", FunctionName.Year)]
        [InlineData("month(CreatedTime) eq 5", FunctionName.Month)]
        [InlineData("day(CreatedTime) eq 25", FunctionName.Day)]
        [InlineData("hour(CreatedTime) eq 12", FunctionName.Hour)]
        [InlineData("minute(CreatedTime) eq 30", FunctionName.Minute)]
        [InlineData("second(CreatedTime) eq 0", FunctionName.Second)]
        [InlineData("round(Amount) eq 100", FunctionName.Round)]
        [InlineData("floor(Amount) eq 99", FunctionName.Floor)]
        [InlineData("ceiling(Amount) eq 100", FunctionName.Ceiling)]
        public void String_date_math_functions_recognised(string input, FunctionName expectedFn)
        {
            var r = FilterParser.Parse(input);
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var fn = Assert.IsType<FunctionNode>(bin.Left);
            Assert.Equal(expectedFn, fn.Name);
        }

        [Fact]
        public void Substring_three_arg_form_supported()
        {
            var r = FilterParser.Parse("substring(Name, 1, 3) eq 'oob'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var fn = Assert.IsType<FunctionNode>(bin.Left);
            Assert.Equal(3, fn.Args.Count);
        }

        [Fact]
        public void Nested_function_calls_supported()
        {
            // tolower(trim(Name)) eq 'foo' — function whose arg is another function
            var r = FilterParser.Parse("tolower(trim(Name)) eq 'foo'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var outer = Assert.IsType<FunctionNode>(bin.Left);
            Assert.Equal(FunctionName.ToLower, outer.Name);
            var inner = Assert.IsType<FunctionNode>(outer.Args[0]);
            Assert.Equal(FunctionName.Trim, inner.Name);
        }

        [Fact]
        public void Count_path_segment_parses_as_member()
        {
            // Items/$count gt 0 — OData collection-count terminal segment. The $-prefix is
            // already supported by IsIdentifierStart, so it lands in the member path as-is.
            var r = FilterParser.Parse("Items/$count gt 0");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var member = Assert.IsType<MemberNode>(bin.Left);
            Assert.Equal(new[] { "Items", "$count" }, member.Path);
        }

        [Fact]
        public void Select_with_count_path_collected()
        {
            // $select=Name,Items/$count — OdataUrlBuilder.ts:83 preserves these verbatim.
            var t = ExpandParser.ParseSelect("Name,Items/$count");
            Assert.Contains("Items/$count", t.SelectedFields);
        }

        [Fact]
        public void Any_lambda_with_body_parsed()
        {
            var r = FilterParser.Parse("Items/any(o: o/Status eq 'Active')");
            var lambda = Assert.IsType<LambdaCollectionNode>(r.Ast);
            Assert.Equal(LambdaOp.Any, lambda.Op);
            Assert.Equal(new[] { "Items" }, lambda.CollectionPath);
            Assert.Equal("o", lambda.Param);
            var bin = Assert.IsType<BinaryNode>(lambda.Body);
            Assert.Equal(BinaryOp.Eq, bin.Op);
            var member = Assert.IsType<MemberNode>(bin.Left);
            Assert.Equal(new[] { "o", "Status" }, member.Path);
        }

        [Fact]
        public void All_lambda_with_body_parsed()
        {
            var r = FilterParser.Parse("Items/all(x: x/Quantity gt 0)");
            var lambda = Assert.IsType<LambdaCollectionNode>(r.Ast);
            Assert.Equal(LambdaOp.All, lambda.Op);
            Assert.Equal("x", lambda.Param);
        }

        [Fact]
        public void Any_no_arg_form_parsed()
        {
            // OData allows Items/any() to test non-empty collection
            var r = FilterParser.Parse("Items/any()");
            var lambda = Assert.IsType<LambdaCollectionNode>(r.Ast);
            Assert.Equal(LambdaOp.Any, lambda.Op);
            Assert.Null(lambda.Param);
            Assert.Null(lambda.Body);
        }

        [Fact]
        public void Lambda_after_multi_segment_path()
        {
            var r = FilterParser.Parse("Customer/Orders/any(o: o/Total gt 100)");
            var lambda = Assert.IsType<LambdaCollectionNode>(r.Ast);
            Assert.Equal(new[] { "Customer", "Orders" }, lambda.CollectionPath);
            Assert.Equal(LambdaOp.Any, lambda.Op);
        }

        [Fact]
        public void Lambda_can_be_combined_with_outer_operators()
        {
            // Test lambda as operand inside a larger boolean expression
            var r = FilterParser.Parse("Active eq true and Items/any(o: o/Qty gt 0)");
            var top = Assert.IsType<BinaryNode>(r.Ast);
            Assert.Equal(BinaryOp.And, top.Op);
            Assert.IsType<LambdaCollectionNode>(top.Right);
        }

        [Fact]
        public void Lambda_property_named_any_without_paren_treated_as_path()
        {
            // If a real entity has property "any", `Things/any` (no `(`) stays a member path.
            var r = FilterParser.Parse("Things/any eq 'x'");
            var bin = Assert.IsType<BinaryNode>(r.Ast);
            var member = Assert.IsType<MemberNode>(bin.Left);
            Assert.Equal(new[] { "Things", "any" }, member.Path);
        }
    }
}
