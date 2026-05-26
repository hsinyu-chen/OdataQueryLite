using System.Linq;
using OdataQueryLite.Caching;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class OdataQueryOptionsTests
    {
        public sealed class Item
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
        }

        private static IQueryable<Item> Rows() => new[]
        {
            new Item { Id = 1, Name = "Apple",  Price = 30 },
            new Item { Id = 2, Name = "Banana", Price = 10 },
            new Item { Id = 3, Name = "Cherry", Price = 50 },
            new Item { Id = 4, Name = "Date",   Price = 20 },
            new Item { Id = 5, Name = "Elder",  Price = 40 },
        }.AsQueryable();

        [Fact]
        public void Empty_parts_returns_source_unchanged()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts());
            var result = opts.Apply(Rows());
            // Unpaged is always populated; host decides whether to enumerate based on opts.Count.
            Assert.Equal(5, result.Unpaged.LongCount());
            Assert.Equal(5, result.Data.Cast<Item>().Count());
        }

        [Fact]
        public void Filter_only_applies_predicate()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { Filter = "Price gt 25" });
            var matched = opts.Apply(Rows()).Data.Cast<Item>().ToList();
            Assert.Equal([1, 3, 5], matched.Select(x => x.Id));
        }

        [Fact]
        public void OrderBy_then_top_skip_compose_in_correct_order()
        {
            // Sort by Price desc -> 50, 40, 30, 20, 10 -> skip 1 take 2 = 40, 30 (ids 5, 1)
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts
            {
                OrderBy = "Price desc",
                Skip = 1,
                Top = 2,
            });
            var page = opts.Apply(Rows()).Data.Cast<Item>().ToList();
            Assert.Equal([5, 1], page.Select(x => x.Id));
        }

        [Fact]
        public void Unpaged_reflects_filtered_pre_paged_set()
        {
            // Filter narrows to 3 rows; paging only emits 1; Unpaged.LongCount() must still be 3.
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts
            {
                Filter = "Price gt 25",
                Top = 1,
                Count = true,
            });
            var result = opts.Apply(Rows());
            Assert.Equal(3, result.Unpaged.LongCount());
            Assert.Single(result.Data);
        }

        [Fact]
        public void Count_wire_flag_exposed_for_host_decision()
        {
            // $count=true on the wire -> opts.Count is true. Host inspects this to decide
            // whether to materialize result.Unpaged into the response payload. Engine has
            // no opinion — Unpaged is always populated.
            var withCount = new OdataQueryOptions<Item>(new OdataQueryParts { Count = true });
            var withoutCount = new OdataQueryOptions<Item>(new OdataQueryParts { Count = false });
            Assert.True(withCount.Count);
            Assert.False(withoutCount.Count);
            // Both still populate Unpaged.
            Assert.Equal(5, withCount.Apply(Rows()).Unpaged.LongCount());
            Assert.Equal(5, withoutCount.Apply(Rows()).Unpaged.LongCount());
        }

        [Fact]
        public void Apply_paging_disabled_ignores_top_and_skip()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { Top = 1, Skip = 2 });
            var result = opts.Apply(Rows(), new ApplyOptions().ApplyPaging(false));
            Assert.Equal(5, result.Data.Cast<Item>().Count());
        }

        [Fact]
        public void Apply_orderby_disabled_skips_sort()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { OrderBy = "Price desc" });
            var rowsAsGiven = Rows().ToList();
            var result = opts.Apply(Rows(), new ApplyOptions().ApplyOrderBy(false));
            Assert.Equal(rowsAsGiven.Select(x => x.Id), result.Data.Cast<Item>().Select(x => x.Id));
        }

        [Fact]
        public void Negative_top_rejected_at_construction()
        {
            var ex = Assert.Throws<OdataQueryException>(() =>
                new OdataQueryOptions<Item>(new OdataQueryParts { Top = -1 }));
            Assert.Contains("$top", ex.Message);
        }

        [Fact]
        public void Negative_skip_rejected_at_construction()
        {
            var ex = Assert.Throws<OdataQueryException>(() =>
                new OdataQueryOptions<Item>(new OdataQueryParts { Skip = -3 }));
            Assert.Contains("$skip", ex.Message);
        }

        [Fact]
        public void Top_zero_returns_empty_page()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { Top = 0 });
            Assert.Empty(opts.Apply(Rows()).Data.Cast<Item>());
        }

        [Fact]
        public void Apply_dollar_apply_throws_unsupported()
        {
            var ex = Assert.Throws<UnsupportedQueryOptionException>(() =>
                new OdataQueryOptions<Item>(new OdataQueryParts { Apply = "groupby((Name))" }));
            Assert.Equal("$apply", ex.OptionName);
        }

        [Fact]
        public void Expand_string_parsed_into_tree_for_whitelist_check()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { Expand = "Related($select=X,Y)" });
            Assert.NotNull(opts.Expand);
            Assert.True(opts.Expand.ExpandedProperties.ContainsKey("Related"));
            Assert.Equal(new[] { "X", "Y" }, opts.Expand.ExpandedProperties["Related"].SelectedFields.OrderBy(s => s));
        }

        [Fact]
        public void Top_level_select_merges_into_root_expand_tree()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts
            {
                Expand = "Related",
                Select = "Id,Name",
            });
            Assert.NotNull(opts.Expand);
            Assert.True(opts.Expand.ExpandedProperties.ContainsKey("Related"));
            Assert.Equal(new[] { "Id", "Name" }, opts.Expand.SelectedFields.OrderBy(s => s));
        }

        [Fact]
        public void Select_without_expand_creates_expand_tree_with_only_root_select()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts { Select = "Id" });
            Assert.NotNull(opts.Expand);
            Assert.Empty(opts.Expand.ExpandedProperties);
            Assert.Equal(["Id"], opts.Expand.SelectedFields);
        }

        [Fact]
        public void No_expand_no_select_leaves_expand_null()
        {
            var opts = new OdataQueryOptions<Item>(new OdataQueryParts());
            Assert.Null(opts.Expand);
        }

        [Fact]
        public void Cache_reuses_compiled_filter_across_instances()
        {
            var cache = new QueryCompileCache();
            // Two distinct OdataQueryOptions instances sharing the cache should produce one MISS + one HIT.
            _ = new OdataQueryOptions<Item>(new OdataQueryParts { Filter = "Price gt 25" }, cache);
            _ = new OdataQueryOptions<Item>(new OdataQueryParts { Filter = "Price gt 99" }, cache);
            Assert.Equal(1, cache.Hits);
            Assert.Equal(1, cache.Misses);
        }
    }
}
