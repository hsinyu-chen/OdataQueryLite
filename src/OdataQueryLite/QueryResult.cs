using System.Collections.Generic;
using System.Linq;

namespace OdataQueryLite
{
    /// <summary>
    /// Result of <see cref="OdataQueryOptions{T}.Apply(IQueryable{T}, IApplyOptions?)"/>.
    /// </summary>
    /// <typeparam name="T">Entity type. <see cref="Unpaged"/> always carries <c>IQueryable&lt;T&gt;</c>;
    /// <see cref="Data"/> is the non-generic <see cref="IQueryable"/> because its element type
    /// switches to <see cref="Dictionary{TKey, TValue}"/> of <c>string</c>→<c>object?</c> when
    /// <c>$select</c> or <c>$expand</c> projects the row, and stays <typeparamref name="T"/> otherwise.</typeparam>
    /// <param name="Data">
    /// The filtered, ordered, paged, and possibly projected query — what the client will iterate.
    /// Runtime element type is <typeparamref name="T"/> when no projection runs, otherwise
    /// <c>Dictionary&lt;string, object?&gt;</c>. JSON serializers handle both shapes transparently
    /// (cast to <see cref="object"/> at the materialization boundary).
    /// </param>
    /// <param name="Unpaged">
    /// The filtered-but-unpaged query (no <c>$orderby</c> / <c>$top</c> / <c>$skip</c> / projection
    /// applied). Callers materialize this themselves: <c>LongCount()</c> for in-memory / pure LINQ
    /// providers, <c>await x.LongCountAsync()</c> for EF Core, or skip enumerating entirely if a
    /// total isn't needed. Whether to materialize is the host's decision (typically gated on
    /// <see cref="OdataQueryOptions{T}.Count"/>, the parsed wire-level <c>$count</c> flag).
    /// Engine deliberately doesn't enumerate so it stays provider-agnostic and async-friendly
    /// without an EF-Core sub-package.
    /// </param>
    public readonly record struct QueryResult<T>(IQueryable Data, IQueryable<T> Unpaged);
}
