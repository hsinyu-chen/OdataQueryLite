using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using OdataQueryLite.Ast;
using OdataQueryLite.Caching;
using OdataQueryLite.ExpressionBuilding;
using OdataQueryLite.Parsing;

namespace OdataQueryLite
{
    // Phase 1.B.11 orchestrator. Replaces Microsoft.AspNetCore.OData.ODataQueryOptions<T>
    // as the public entry-point. Owns the parse of every $-option at construction so
    // malformed requests fail fast at the binder layer rather than mid-Apply; Apply itself
    // is allocation-light (just composes IQueryable transformations).
    //
    // $select/$expand projection is parsed (exposed via Expand) but not applied to the
    // returned IQueryable — that's Phase 1.B.13. Whitelist enforcement against Expand is
    // the caller's job (typically via OdataQueryLite.Permissions.ExpandSubsumption).
    public sealed class OdataQueryOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        where T : class
    {
        private readonly ICompiledQuery<T>? _filterCompiled;
        private readonly FilterParseResult? _filterParsed;
        private readonly OrderByClause? _orderByClause;

        public string? RawFilter { get; }
        public string? RawOrderBy { get; }
        public string? RawExpand { get; }
        public string? RawSelect { get; }
        public int? Top { get; }
        public int? Skip { get; }
        public bool Count { get; }

        // Merged $expand + $select tree. null when neither option was supplied.
        public ExpandRequestNode? Expand { get; }

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

        [RequiresUnreferencedCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which build Expression trees over T.")]
        [RequiresDynamicCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which compile generic delegates at runtime.")]
        public QueryResult Apply(IQueryable<T> source, IApplyOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            var opt = options ?? new ApplyOptions();
            var q = source;

            if (_filterCompiled is not null)
                q = _filterCompiled.Apply(q, _filterParsed!.Literals);

            // Capture the filtered, pre-orderby, pre-paged shape for the caller to count. We
            // don't enumerate here — the caller chooses sync LongCount() / async LongCountAsync()
            // per their provider, or doesn't enumerate at all. Engine stays provider-agnostic.
            // OrderBy is excluded because Count is order-independent; pre-paging is included
            // because clients expect $count to reflect the total matching set, not the page size.
            IQueryable? unpaged = (opt.Count && Count) ? q : null;

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
