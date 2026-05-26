using System.Linq;

namespace OdataQueryLite
{
    /// <summary>
    /// Result of <see cref="OdataQueryOptions{T}.Apply(IQueryable{T}, IApplyOptions?)"/>.
    /// </summary>
    /// <param name="Data">The filtered, ordered, and paged query — what the client will iterate.</param>
    /// <param name="Unpaged">
    /// The filtered-but-unpaged query (no <c>$orderby</c> / <c>$top</c> / <c>$skip</c> applied). Callers
    /// materialize this themselves: <c>LongCount()</c> for in-memory / pure LINQ providers,
    /// <c>await x.LongCountAsync()</c> for EF Core, or skip enumerating entirely if a total isn't needed.
    /// Whether to materialize is the host's decision (typically gated on
    /// <see cref="OdataQueryOptions{T}.Count"/>, the parsed wire-level <c>$count</c> flag).
    /// Engine deliberately doesn't enumerate so it stays provider-agnostic and async-friendly without an
    /// EF-Core sub-package.
    /// </param>
    public readonly record struct QueryResult(IQueryable Data, IQueryable Unpaged);
}
