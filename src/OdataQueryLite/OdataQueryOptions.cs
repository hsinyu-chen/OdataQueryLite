using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using OdataQueryLite.Ast;
using OdataQueryLite.Caching;
using OdataQueryLite.ExpressionBuilding;
using OdataQueryLite.Parsing;

namespace OdataQueryLite
{
    /// <summary>
    /// Public entry-point that parses an OData query at construction and applies the parsed pipeline to an
    /// <see cref="IQueryable{T}"/> on demand. Owns the parse of every <c>$</c>-option up front so malformed
    /// requests fail fast at the binder layer rather than mid-<see cref="Apply"/>; <see cref="Apply"/> itself
    /// is allocation-light (just composes <see cref="IQueryable"/> transformations).
    /// </summary>
    /// <remarks>
    /// <c>$select</c> / <c>$expand</c> projection is parsed (exposed via <see cref="Expand"/>) but not applied
    /// to the returned <see cref="IQueryable"/>. Whitelist enforcement against <see cref="Expand"/> is the
    /// caller's job (typically via <see cref="OdataQueryLite.Permissions.ExpandSubsumption"/>).
    /// </remarks>
    /// <typeparam name="T">Entity type whose public properties form the queryable surface.</typeparam>
    public sealed class OdataQueryOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        where T : class
    {
        private readonly ICompiledQuery<T>? _filterCompiled;
        private readonly FilterParseResult? _filterParsed;
        private readonly OrderByClause? _orderByClause;

        /// <summary>The original <c>$filter</c> text supplied by the caller, or <see langword="null"/>.</summary>
        public string? RawFilter { get; }

        /// <summary>The original <c>$orderby</c> text supplied by the caller, or <see langword="null"/>.</summary>
        public string? RawOrderBy { get; }

        /// <summary>The original <c>$expand</c> text supplied by the caller, or <see langword="null"/>.</summary>
        public string? RawExpand { get; }

        /// <summary>The original <c>$select</c> text supplied by the caller, or <see langword="null"/>.</summary>
        public string? RawSelect { get; }

        /// <summary>Parsed <c>$top</c>, or <see langword="null"/> when not supplied. Guaranteed non-negative.</summary>
        public int? Top { get; }

        /// <summary>Parsed <c>$skip</c>, or <see langword="null"/> when not supplied. Guaranteed non-negative.</summary>
        public int? Skip { get; }

        /// <summary>Whether the caller requested <c>$count=true</c>.</summary>
        public bool Count { get; }

        /// <summary>
        /// Merged <c>$expand</c> + <c>$select</c> tree. <see langword="null"/> when neither option was supplied.
        /// </summary>
        public ExpandRequestNode? Expand { get; }

        /// <summary>
        /// Parses every <c>$</c>-option from <paramref name="parts"/>. Failures surface as
        /// <see cref="OdataQueryException"/> / <see cref="UnsupportedQueryOptionException"/>.
        /// </summary>
        /// <param name="parts">The raw query options.</param>
        /// <param name="cache">Optional cross-request compile cache; when <see langword="null"/> every call recompiles.</param>
        /// <param name="maxTop">Optional upper bound on <c>$top</c>; values above it throw <see cref="OdataQueryException"/>. <see langword="null"/> disables the check.</param>
        /// <exception cref="OdataQueryException">Negative <c>$top</c>/<c>$skip</c>, <c>$top</c> above <paramref name="maxTop"/>, or any parse failure inside the supplied <c>$</c>-options.</exception>
        /// <exception cref="UnsupportedQueryOptionException"><c>$apply</c> was supplied.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="parts"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Compiles a filter Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression<Func<T, bool>> / Func<T, TKey> at runtime; AOT may require dynamic-code support depending on the IQueryable provider.")]
        public OdataQueryOptions(OdataQueryParts parts, QueryCompileCache? cache = null, int? maxTop = null)
        {
            ArgumentNullException.ThrowIfNull(parts);

            if (!string.IsNullOrWhiteSpace(parts.Apply))
                throw new UnsupportedQueryOptionException("$apply", "$apply is not supported. Use a dedicated aggregation API.");

            // OData v4 Part 2 §5.1.4 / §5.1.5: $top and $skip must be non-negative.
            // Silently ignoring negatives (the old `> 0` / `>= 0` guards on Apply) would let
            // a $top=-5 request return the entire dataset — spec violation and data-exposure
            // hazard. Reject at the binder boundary so the caller sees a clean 400.
            if (parts.Top is int t and < 0)
                throw new OdataQueryException($"$top must be a non-negative integer; got {t}.");
            if (parts.Skip is int s and < 0)
                throw new OdataQueryException($"$skip must be a non-negative integer; got {s}.");
            // Caller-configured ceiling so `$top=2147483647` against a 50M-row table doesn't
            // translate to TOP(int.MaxValue) and OOM the database. Off by default — host opts
            // in via OdataQueryLiteOptions.MaxTop (AspNetCore package) or by passing maxTop here.
            if (parts.Top is int requested && maxTop is int max && requested > max)
                throw new OdataQueryException($"$top exceeds the configured maximum of {max}; got {requested}.");

            RawFilter = parts.Filter;
            RawOrderBy = parts.OrderBy;
            RawExpand = parts.Expand;
            RawSelect = parts.Select;
            Top = parts.Top;
            Skip = parts.Skip;
            Count = parts.Count;

            if (!string.IsNullOrWhiteSpace(parts.Filter))
            {
                if (cache is not null)
                {
                    _filterCompiled = cache.GetOrBuild<T>(parts.Filter, out _filterParsed);
                }
                else
                {
                    _filterParsed = FilterParser.Parse(parts.Filter);
                    _filterCompiled = CompiledQueryFactory.Build<T>(_filterParsed);
                }
            }

            if (!string.IsNullOrWhiteSpace(parts.OrderBy))
                _orderByClause = OrderByParser.Parse(parts.OrderBy);

            ExpandRequestNode? expand = null;
            if (!string.IsNullOrWhiteSpace(parts.Expand))
                expand = ExpandParser.Parse(parts.Expand);
            // OData allows $select at the same level as $expand without nesting it inside.
            // Top-level $select merges its field set onto the root node; lower-level $select
            // (inside $expand(...)) is handled by ExpandParser.Parse directly.
            if (!string.IsNullOrWhiteSpace(parts.Select))
            {
                var fromSelect = ExpandParser.ParseSelect(parts.Select);
                if (expand is null) expand = fromSelect;
                else MergeSelectInto(expand, fromSelect);
            }
            Expand = expand;
        }

        // Merge a $select-derived tree into the existing $expand tree.
        //
        // Root-level $expand never carries SelectedFields (per OData v4.01 ABNF, $select only
        // appears inside `expandOption`); root-level $select on the other hand can carry both
        // SelectedFields (`$select=Id,Name`) and ExpandedProperties (from slashed nested paths
        // `$select=Customer/Name`). When both wire-level options are supplied we union them
        // so callers that mix $expand=Customer with $select=Customer/Phone get both — the
        // $expand-side gives the full Customer with no field filter, the $select-side adds a
        // narrowed projection on the same nav. Per-segment recursion mirrors that intent at
        // each depth.
        private static void MergeSelectInto(ExpandRequestNode target, ExpandRequestNode source)
        {
            if (source.SelectedFields is not null)
            {
                target.SelectedFields ??= [];
                foreach (var f in source.SelectedFields)
                    target.SelectedFields.Add(f);
            }
            foreach (var (key, sourceChild) in source.ExpandedProperties)
            {
                if (!target.ExpandedProperties.TryGetValue(key, out var targetChild))
                {
                    target.ExpandedProperties[key] = sourceChild;
                    continue;
                }
                MergeSelectInto(targetChild, sourceChild);
            }
        }

        /// <summary>
        /// Composes the parsed pipeline (<c>$filter</c>, <c>$orderby</c>, <c>$top</c>, <c>$skip</c>) onto
        /// <paramref name="source"/> and returns the resulting query plus an unpaged snapshot for counting.
        /// </summary>
        /// <param name="source">The provider-bound input query (typically an EF Core DbSet or in-memory <see cref="IQueryable"/>).</param>
        /// <param name="options">Per-call switches; <see langword="null"/> applies every stage.</param>
        /// <returns>The composed query plus the filtered-but-unpaged snapshot.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply / SelectExpandProjector.Project which build Expression trees over T.")]
        [RequiresDynamicCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply / SelectExpandProjector.Project which compile generic delegates at runtime.")]
        public QueryResult<T> Apply(IQueryable<T> source, IApplyOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            var opt = options ?? new ApplyOptions();
            var q = source;

            if (_filterCompiled is not null)
                q = _filterCompiled.Apply(q, _filterParsed!.Literals);

            // Snapshot the filtered, pre-orderby, pre-paged, pre-projection queryable so the
            // caller can count it independently. We don't enumerate — caller chooses sync
            // LongCount() or async LongCountAsync() per their provider, or skips entirely.
            // Whether to surface a total to the client is the host's call, typically gated on
            // Count (the wire $count flag). OrderBy + projection are excluded because count
            // is order- and shape-independent.
            var unpaged = q;

            if (opt.OrderBy && _orderByClause is not null)
                q = OrderByApplier.Apply(q, _orderByClause);

            if (opt.Paging)
            {
                // Negatives already rejected at construction; 0-skip is a no-op so save the
                // Queryable.Skip call. 0-top is meaningful (returns empty set) per spec.
                if (Skip is int skip and > 0) q = q.Skip(skip);
                if (Top is int top) q = q.Take(top);
            }

            IQueryable data = q;
            if (opt.SelectExpand && Expand is not null)
                data = SelectExpandProjector.Project(q, Expand);

            return new QueryResult<T>(data, unpaged);
        }
    }
}
