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
        private readonly ICompiledQuery<T> _filterCompiled;
        private readonly FilterParseResult _filterParsed;
        private readonly OrderByClause _orderByClause;

        public string RawFilter { get; }
        public string RawOrderBy { get; }
        public string RawExpand { get; }
        public string RawSelect { get; }
        public int? Top { get; }
        public int? Skip { get; }
        public bool Count { get; }

        // Merged $expand + $select tree. null when neither option was supplied.
        public ExpandRequestNode Expand { get; }

        [RequiresUnreferencedCode("Compiles a filter Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression<Func<T, bool>> / Func<T, TKey> at runtime; AOT may require dynamic-code support depending on the IQueryable provider.")]
        public OdataQueryOptions(OdataQueryParts parts, QueryCompileCache cache = null)
        {
            ArgumentNullException.ThrowIfNull(parts);

            if (!string.IsNullOrEmpty(parts.Apply))
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

            if (!string.IsNullOrEmpty(parts.Filter))
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

            if (!string.IsNullOrEmpty(parts.OrderBy))
                _orderByClause = OrderByParser.Parse(parts.OrderBy);

            ExpandRequestNode expand = null;
            if (!string.IsNullOrEmpty(parts.Expand))
                expand = ExpandParser.Parse(parts.Expand);
            // OData allows $select at the same level as $expand without nesting it inside.
            // Top-level $select merges its field set onto the root node; lower-level $select
            // (inside $expand(...)) is handled by ExpandParser.Parse directly.
            if (!string.IsNullOrEmpty(parts.Select))
            {
                var fromSelect = ExpandParser.ParseSelect(parts.Select);
                if (expand is null) expand = fromSelect;
                else expand.SelectedFields = fromSelect.SelectedFields;
            }
            Expand = expand;
        }

        [RequiresUnreferencedCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which build Expression trees over T.")]
        [RequiresDynamicCode("Delegates to ICompiledQuery<T>.Apply / OrderByApplier.Apply which compile generic delegates at runtime.")]
        public QueryResult Apply(IQueryable<T> source, IApplyOptions options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            var opt = options ?? new ApplyOptions();
            var q = source;

            if (_filterCompiled is not null)
                q = _filterCompiled.Apply(q, _filterParsed.Literals);

            // Count is measured on the filtered, pre-paged set so the total reflects what the
            // client could iterate if they paged through everything — matches Microsoft's
            // ApplyTo TotalCount semantics.
            long? total = (opt.Count && Count) ? q.LongCount() : null;

            if (opt.OrderBy && _orderByClause is not null)
                q = OrderByApplier.Apply(q, _orderByClause);

            if (opt.Paging)
            {
                // Negatives already rejected at construction; 0-skip is a no-op so save the
                // Queryable.Skip call. 0-top is meaningful (returns empty set) per spec.
                if (Skip is int skip and > 0) q = q.Skip(skip);
                if (Top is int top) q = q.Take(top);
            }

            return new QueryResult(q, total);
        }
    }
}
