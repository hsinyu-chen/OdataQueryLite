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
        /// <exception cref="OdataQueryException">Negative <c>$top</c>/<c>$skip</c>, or any parse failure inside the supplied <c>$</c>-options.</exception>
        /// <exception cref="UnsupportedQueryOptionException"><c>$apply</c> was supplied.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="parts"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Compiles a filter Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression<Func<T, bool>> / Func<T, TKey> at runtime; AOT may require dynamic-code support depending on the IQueryable provider.")]
        public OdataQueryOptions(OdataQueryParts parts, QueryCompileCache? cache = null)
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
                // Spec invariant: per OData v4.01 ABNF, `select` only appears inside
                // `expandOption` (the parens after an expand item), so ExpandParser.Parse
                // never assigns root-level SelectedFields. This overwrite is therefore
                // always against null. Locked by ExpandParserTests.Parse_never_sets_root_SelectedFields
                // — if that test fires, this merge must switch to UnionWith.
                if (expand is null) expand = fromSelect;
                else expand.SelectedFields = fromSelect.SelectedFields;
            }
            Expand = expand;
        }

        /// <summary>
        /// Composes the parsed pipeline (<c>$filter</c>, <c>$orderby</c>, <c>$top</c>, <c>$skip</c>) onto
        /// <paramref name="source"/> and returns the resulting query plus an unpaged snapshot for counting.
        /// </summary>
        /// <param name="source">The provider-bound input query (typically an EF Core DbSet or in-memory <see cref="IQueryable"/>).</param>
        /// <param name="options">Per-call switches; <see langword="null"/> applies every stage.</param>
        /// <returns>The composed query plus the filtered-but-unpaged snapshot.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which build Expression trees over T.")]
        [RequiresDynamicCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which compile generic delegates at runtime.")]
        public QueryResult Apply(IQueryable<T> source, IApplyOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            var opt = options ?? new ApplyOptions();
            var q = source;

            if (_filterCompiled is not null)
                q = _filterCompiled.Apply(q, _filterParsed!.Literals);

            // Snapshot the filtered, pre-orderby, pre-paged queryable so the caller can count
            // it independently. We don't enumerate — caller chooses sync LongCount() or async
            // LongCountAsync() per their provider, or skips entirely. Whether to surface a
            // total to the client is the host's call, typically gated on Count (the wire $count
            // flag). OrderBy is excluded because Count is order-independent.
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

            return new QueryResult(q, unpaged);
        }
    }
}
