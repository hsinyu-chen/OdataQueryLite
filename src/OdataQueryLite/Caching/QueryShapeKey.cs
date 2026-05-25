using System;

namespace OdataQueryLite.Caching
{
    // Slot types are a deterministic function of (EntityType, Shape) — same query template
    // against the same T resolves to the same member types every time — so they don't need
    // to participate in the cache key.
    public readonly record struct QueryShapeKey(Type EntityType, string Shape);
}
