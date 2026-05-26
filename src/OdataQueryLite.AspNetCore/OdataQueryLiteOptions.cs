namespace OdataQueryLite.AspNetCore
{
    /// <summary>
    /// Tunable knobs for <see cref="OdataQueryLiteExtensions.AddOdataQueryLite(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{OdataQueryLiteOptions}?)"/>.
    /// New knobs go here so the host registration surface stays a single method without growing parameters
    /// per feature.
    /// </summary>
    public sealed class OdataQueryLiteOptions
    {
        /// <summary>
        /// When <see langword="false"/>, <c>AddOdataQueryLite()</c> does not register a
        /// <see cref="QueryCompileCache"/> singleton and every request reparses + recompiles its filter.
        /// Useful for tests, very small surfaces, or hosts that already inject their own cache instance.
        /// </summary>
        public bool UseCache { get; set; } = true;

        /// <summary>
        /// Soft cap on cached compiled queries. The cache evicts the coldest ~10% when this is exceeded;
        /// tune up for high-cardinality filter surfaces, down for memory-tight hosts. Ignored when
        /// <see cref="UseCache"/> is <see langword="false"/>.
        /// </summary>
        public int MaxCacheEntries { get; set; } = 10_000;
    }
}
