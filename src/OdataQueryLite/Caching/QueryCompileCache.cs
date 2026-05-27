using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using OdataQueryLite.Ast;
using OdataQueryLite.Parsing;

namespace OdataQueryLite.Caching
{
    /// <summary>
    /// Cross-request LRU cache of compiled <c>$filter</c> queries, keyed by (entity type, literals-erased
    /// shape). Approximate LRU: when the entry count exceeds the cap, the coldest ~10% are evicted in one
    /// pass; concurrent eviction is single-threaded but concurrent inserts can briefly exceed the cap.
    /// </summary>
    /// <remarks>
    /// Thread-safe; typically registered as a singleton (the AspNet package does so by default through
    /// <c>AddOdataQueryLite</c>).
    /// </remarks>
    public sealed class QueryCompileCache
    {
        private readonly ConcurrentDictionary<QueryShapeKey, Entry> _cache = new();
        private readonly int _maxEntries;
        private long _hits;
        private long _misses;
        private int _isEvicting;

        /// <summary>Creates a cache with a soft cap of <paramref name="maxEntries"/>.</summary>
        /// <param name="maxEntries">Soft cap on cached compiled queries; must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxEntries"/> is non-positive.</exception>
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

        /// <summary>
        /// Returns the cached compiled query for the (<typeparamref name="T"/>, <paramref name="filter"/>) shape,
        /// building and inserting it on first miss. Always yields the parsed filter result via <paramref name="parsed"/>
        /// so callers can read its literals without a second parse.
        /// </summary>
        /// <typeparam name="T">Entity type the filter references.</typeparam>
        /// <param name="filter">Raw <c>$filter</c> string.</param>
        /// <param name="parsed">Receives the parsed filter (literals + AST) regardless of cache hit/miss.</param>
        /// <returns>The compiled query for this shape.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
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
            try
            {
                return (ICompiledQuery<T>)entry.Compiled.Value;
            }
            catch
            {
                // The Lazy caches the exception, so without this TryRemove the entry would sit
                // forever consuming a cache slot — an attacker spamming random invalid field
                // names ($filter=Bogus1 eq 1, $filter=Bogus2 eq 1, …) could otherwise evict
                // every hot legitimate query. Race-safe: concurrent failed builds either land
                // on the same dead entry (one TryRemove wins, others no-op) or have already
                // raced past us into a fresh entry that doesn't share state.
                _cache.TryRemove(key, out _);
                throw;
            }
        }

        private void EvictColdest(int count)
        {
            if (count <= 0) return;
            var snap = _cache.ToArray();
            Array.Sort(snap, static (a, b) => a.Value.LastUsedTicks.CompareTo(b.Value.LastUsedTicks));
            for (int i = 0; i < Math.Min(count, snap.Length); i++)
                _cache.TryRemove(snap[i].Key, out _);
        }

        /// <summary>Current number of cached compiled queries.</summary>
        public int Count => _cache.Count;

        /// <summary>The soft cap supplied at construction; entries beyond it trigger LRU eviction.</summary>
        public int MaxEntries => _maxEntries;

        /// <summary>Total cache hits since construction (thread-safe snapshot).</summary>
        public long Hits => Interlocked.Read(ref _hits);

        /// <summary>Total cache misses since construction (thread-safe snapshot).</summary>
        public long Misses => Interlocked.Read(ref _misses);

        /// <summary>Tests whether <paramref name="key"/> is currently present (chiefly for diagnostics / tests).</summary>
        /// <param name="key">Shape key to probe.</param>
        /// <returns><see langword="true"/> when present.</returns>
        public bool Contains(QueryShapeKey key) => _cache.ContainsKey(key);
    }
}
