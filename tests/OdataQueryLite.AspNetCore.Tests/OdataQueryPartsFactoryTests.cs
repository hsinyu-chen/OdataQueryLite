using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OdataQueryLite.AspNetCore;
using Xunit;

namespace OdataQueryLite.AspNetCore.Tests
{
    public class OdataQueryPartsFactoryTests
    {
        private static IQueryCollection Q(params (string Key, string Value)[] pairs)
        {
            var d = new Dictionary<string, StringValues>();
            foreach (var (k, v) in pairs) d[k] = v;
            return new QueryCollection(d);
        }

        [Fact]
        public void Empty_query_yields_all_null_or_default()
        {
            var p = OdataQueryPartsFactory.FromQuery(Q());
            Assert.Null(p.Filter);
            Assert.Null(p.OrderBy);
            Assert.Null(p.Expand);
            Assert.Null(p.Select);
            Assert.Null(p.Apply);
            Assert.Null(p.Top);
            Assert.Null(p.Skip);
            Assert.False(p.Count);
        }

        [Fact]
        public void Dollar_options_passed_through_verbatim()
        {
            var p = OdataQueryPartsFactory.FromQuery(Q(
                ("$filter", "Name eq 'X'"),
                ("$orderby", "Id desc"),
                ("$expand", "Orders($select=Total)"),
                ("$select", "Id,Name"),
                ("$apply", "groupby((Name))"),
                ("$top", "20"),
                ("$skip", "5"),
                ("$count", "true")));

            Assert.Equal("Name eq 'X'", p.Filter);
            Assert.Equal("Id desc", p.OrderBy);
            Assert.Equal("Orders($select=Total)", p.Expand);
            Assert.Equal("Id,Name", p.Select);
            Assert.Equal("groupby((Name))", p.Apply);
            Assert.Equal(20, p.Top);
            Assert.Equal(5, p.Skip);
            Assert.True(p.Count);
        }

        [Theory]
        [InlineData("TRUE", true)]
        [InlineData("True", true)]
        [InlineData("false", false)]
        [InlineData("FALSE", false)]
        public void Count_is_case_insensitive(string raw, bool expected)
        {
            var p = OdataQueryPartsFactory.FromQuery(Q(("$count", raw)));
            Assert.Equal(expected, p.Count);
        }

        [Fact]
        public void Count_invalid_value_throws_OdataQueryException()
        {
            var ex = Assert.Throws<OdataQueryException>(() =>
                OdataQueryPartsFactory.FromQuery(Q(("$count", "yes"))));
            Assert.Contains("$count", ex.Message);
        }

        [Fact]
        public void Top_non_integer_throws_OdataQueryException()
        {
            var ex = Assert.Throws<OdataQueryException>(() =>
                OdataQueryPartsFactory.FromQuery(Q(("$top", "twenty"))));
            Assert.Contains("$top", ex.Message);
        }

        [Fact]
        public void Whitespace_only_value_treated_as_absent()
        {
            var p = OdataQueryPartsFactory.FromQuery(Q(("$filter", "   ")));
            Assert.Null(p.Filter);
        }

        [Fact]
        public void Negative_top_allowed_at_factory_layer_rejected_at_options_ctor()
        {
            // OdataQueryPartsFactory does not enforce non-negative — it's a pure mapping. The
            // OdataQueryOptions<T> ctor (Phase 1.B.11) is the spec-enforcement boundary.
            // Test fixes that division of responsibility in place.
            var p = OdataQueryPartsFactory.FromQuery(Q(("$top", "-5")));
            Assert.Equal(-5, p.Top);
        }
    }
}
