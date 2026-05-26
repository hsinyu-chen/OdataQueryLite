using System;

namespace OdataQueryLite.Caching
{
    /// <summary>
    /// Identity for one cached compiled query — the entity type plus the literals-erased token shape of the
    /// <c>$filter</c> string.
    /// </summary>
    /// <remarks>
    /// Slot types are a deterministic function of (<see cref="EntityType"/>, <see cref="Shape"/>) — same query
    /// template against the same <c>T</c> resolves to the same member types every time — so they don't need
    /// to participate in the cache key.
    /// </remarks>
    /// <param name="EntityType">The <c>T</c> the filter was compiled against.</param>
    /// <param name="Shape">Token shape with literal placeholders, as produced by <c>LexedQuery.ToShapeString(typed: false)</c>.</param>
    public readonly record struct QueryShapeKey(Type EntityType, string Shape);
}
