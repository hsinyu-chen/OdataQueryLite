namespace OdataQueryLite.AspNetCore
{
    // Tunable knobs for AddOdataQueryLite(). Lives in the AspNetCore package because the
    // core OdataQueryLite library doesn't otherwise need a DI options story — it's just
    // an Expression builder. New knobs go here so the host registration surface stays
    // single-method without growing parameters per feature.
    public sealed class OdataQueryLiteOptions
    {
        // When false, AddOdataQueryLite() does not register a QueryCompileCache singleton
        // and every request reparses + recompiles its filter. Useful for tests / very small
        // surfaces / hosts that already inject their own QueryCompileCache.
        public bool UseCache { get; set; } = true;

        // Cap on cached compiled queries. The cache evicts the coldest 10% when this is
        // exceeded; tune up for high-cardinality filter surfaces, down for memory-tight
        // hosts. Ignored when UseCache is false.
        public int MaxCacheEntries { get; set; } = 10_000;
    }
}
