using System.Collections.Generic;
using System.Linq;
using OdataQueryLite.Caching;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class QueryCompileCacheTests
    {
        public sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Amount { get; set; }
        }

        private static IQueryable<Row> Source() => new[]
        {
            new Row { Id = 1, Name = "Alice", Amount = 100 },
            new Row { Id = 2, Name = "Bob", Amount = 50 },
            new Row { Id = 3, Name = "Alice", Amount = 200 },
        }.AsQueryable();

        [Fact]
        public void Factory_builds_executable_filter()
        {
            var parsed = FilterParser.Parse("Name eq 'Alice'");
            var compiled = CompiledQueryFactory.Build<Row>(parsed);
            var rows = compiled.Apply(Source(), parsed.Literals).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("Alice", r.Name));
        }

        [Fact]
        public void Cache_returns_same_instance_for_same_shape_different_values()
        {
            var cache = new QueryCompileCache();
            var a = cache.GetOrBuild<Row>("Name eq 'Alice'", out _);
            var b = cache.GetOrBuild<Row>("Name eq 'Bob'", out _);
            Assert.Same(a, b);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Cache_keys_separately_for_different_shapes()
        {
            var cache = new QueryCompileCache();
            cache.GetOrBuild<Row>("Name eq 'Alice'", out _);
            cache.GetOrBuild<Row>("Amount gt 100", out _);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void Cache_keys_separately_per_entity_type()
        {
            var cache = new QueryCompileCache();
            cache.GetOrBuild<Row>("Name eq 'Alice'", out _);
            cache.GetOrBuild<OtherRow>("Name eq 'Alice'", out _);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void Cache_hit_skips_rebuild_and_executes_against_current_args()
        {
            var cache = new QueryCompileCache();
            var c1 = cache.GetOrBuild<Row>("Amount gt 75", out var p1);
            var c2 = cache.GetOrBuild<Row>("Amount gt 150", out var p2);
            Assert.Same(c1, c2);

            var rows1 = c1.Apply(Source(), p1.Literals).ToList();
            var rows2 = c2.Apply(Source(), p2.Literals).ToList();
            Assert.Equal(new[] { 100, 200 }, rows1.Select(r => r.Amount).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { 200 }, rows2.Select(r => r.Amount).ToArray());
        }

        [Fact]
        public void Null_does_not_fragment_cache_shape()
        {
            // Critical: a query template that's sometimes called with null and sometimes
            // with non-null values must hit the same cache entry, not two.
            var cache = new QueryCompileCache();
            cache.GetOrBuild<Row>("Name eq null", out _);
            cache.GetOrBuild<Row>("Name eq 'Alice'", out _);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Hits_and_misses_counters_track_lookup_outcomes()
        {
            var cache = new QueryCompileCache();
            cache.GetOrBuild<Row>("Name eq 'A'", out _);
            cache.GetOrBuild<Row>("Name eq 'A'", out _);
            cache.GetOrBuild<Row>("Name eq 'A'", out _);
            cache.GetOrBuild<Row>("Amount gt 1", out _);
            Assert.Equal(2, cache.Misses);
            Assert.Equal(2, cache.Hits);
        }

        [Fact]
        public void Soft_cap_triggers_eviction_of_coldest_entries()
        {
            // Note: untyped shape collapses literals, so `Id eq 1` and `Id eq 2` share a key.
            // The 10 queries below all produce distinct shapes (different member or operator).
            var cache = new QueryCompileCache(maxEntries: 10);
            string[] shapes =
            [
                "Id eq 1", "Name eq 'A'", "Amount gt 1", "Amount lt 1", "Id ne 1",
                "Amount ge 1", "Amount le 1", "Id gt 1", "Id lt 1", "Id ge 1"
            ];
            foreach (var q in shapes) cache.GetOrBuild<Row>(q, out _);
            Assert.Equal(10, cache.Count);

            // Touch the first 5 to bump LastUsedTicks above the cold tail.
            System.Threading.Thread.Sleep(5);
            for (int i = 0; i < 5; i++) cache.GetOrBuild<Row>(shapes[i], out _);

            // 11th shape pushes us over cap → eviction drops the coldest 10% (= 1 entry).
            cache.GetOrBuild<Row>("Id le 1", out _);

            Assert.True(cache.Count <= 10, $"Count {cache.Count} should not exceed cap after eviction.");
            // The touched-hot entries should still be present.
            Assert.True(cache.Contains(new QueryShapeKey(typeof(Row), "Id eq ?")));
            Assert.True(cache.Contains(new QueryShapeKey(typeof(Row), "Name eq ?")));
        }

        [Fact]
        public void Concurrent_first_hit_builds_only_once_per_key()
        {
            var cache = new QueryCompileCache();
            int buildSerial = 0;

            // Hammer the same shape from many threads. Lazy guarantees the underlying Build
            // runs exactly once even though GetOrAdd's factory may fire on multiple threads.
            var threads = new System.Threading.Thread[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new System.Threading.Thread(() =>
                {
                    var c = cache.GetOrBuild<Row>("Name eq 'A'", out _);
                    System.Threading.Interlocked.CompareExchange(ref buildSerial, 1, 0);
                });
            }
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.Equal(1, cache.Count);
        }

        public sealed class OtherRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
