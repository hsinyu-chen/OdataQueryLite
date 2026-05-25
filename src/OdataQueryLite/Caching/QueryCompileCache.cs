using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using OdataQueryLite.Ast;
using OdataQueryLite.Parsing;

namespace OdataQueryLite.Caching
{
    public sealed class QueryCompileCache
    {
        private readonly ConcurrentDictionary<QueryShapeKey, Entry> _cache = new();
        private readonly int _maxEntries;
        private long _hits;
        private long _misses;
        private int _isEvicting;

        public QueryCompileCache(int maxEntries = 10_000)
        {
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be positive.");
            _maxEntries = maxEntries;
        }

        private sealed class Entry
        {
            public required Lazy<object> Compiled;
            public long LastUsedTicks;
        }

        [RequiresUnreferencedCode("Builds an Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Compiles an Expression tree to a delegate via Expression.Compile() / preferInterpretation under AOT.")]
        public ICompiledQuery<T> GetOrBuild<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            string filter,
            out FilterParseResult parsed)
        {
            ArgumentNullException.ThrowIfNull(filter);

            // Tokenize once; reuse for both shape extraction and AST parse.
            var tokens = OdataLexer.Tokenize(filter);
            // Untyped shape so null and non-null calls share a cache entry.
            var shape = tokens.ToShapeString(typed: false);
            parsed = FilterParser.Parse(tokens);

            var key = new QueryShapeKey(typeof(T), shape);
            var now = Environment.TickCount64;

            if (_cache.TryGetValue(key, out var hit))
            {
                // Race-tolerant: concurrent writes of slightly different `now` only smear the
                // LRU ordering by milliseconds, no correctness impact.
                hit.LastUsedTicks = now;
                Interlocked.Increment(ref _hits);
                return (ICompiledQuery<T>)hit.Compiled.Value;
            }

            Interlocked.Increment(ref _misses);
            // Approximate LRU: when over cap, sample the coldest 10% and drop them so the
            // new shape has room. Hot entries stay; cold entries can rebuild on next hit.
            // Only one thread evicts at a time; concurrent misses skip and proceed to add,
            // briefly exceeding the cap — acceptable, the next eviction round catches up.
            if (_cache.Count >= _maxEntries
                && Interlocked.CompareExchange(ref _isEvicting, 1, 0) == 0)
            {
                try { EvictColdest(Math.Max(1, _maxEntries / 10)); }
                finally { Volatile.Write(ref _isEvicting, 0); }
            }

            var local = parsed;
            var entry = _cache.GetOrAdd(key, _ => new Entry
            {
                // Lazy ensures Build runs exactly once per key even under concurrent first hits.
                Compiled = new Lazy<object>(() => CompiledQueryFactory.Build<T>(local)),
                LastUsedTicks = now
            });
            return (ICompiledQuery<T>)entry.Compiled.Value;
        }

        private void EvictColdest(int count)
        {
            if (count <= 0) return;
            var snap = _cache.ToArray();
            Array.Sort(snap, static (a, b) => a.Value.LastUsedTicks.CompareTo(b.Value.LastUsedTicks));
            for (int i = 0; i < Math.Min(count, snap.Length); i++)
                _cache.TryRemove(snap[i].Key, out _);
        }

        public int Count => _cache.Count;
        public int MaxEntries => _maxEntries;
        public long Hits => Interlocked.Read(ref _hits);
        public long Misses => Interlocked.Read(ref _misses);
        public bool Contains(QueryShapeKey key) => _cache.ContainsKey(key);
    }
}
