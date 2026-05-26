using System.Diagnostics.Tracing;

namespace OdataQueryLite.Diagnostics
{
    /// <summary>
    /// <see cref="EventSource"/> for OdataQueryLite runtime diagnostics. Subscribe via <c>dotnet-trace</c>,
    /// <c>EventListener</c>, or the in-proc <see cref="System.Diagnostics.Tracing"/> APIs using the provider
    /// name <c>OdataQueryLite</c>.
    /// </summary>
    [EventSource(Name = "OdataQueryLite")]
    public sealed class OdataQueryLiteEventSource : EventSource
    {
        /// <summary>Singleton instance used by the engine to emit events.</summary>
        public static readonly OdataQueryLiteEventSource Log = new();

        private OdataQueryLiteEventSource() { }

        /// <summary>
        /// Emitted once per <see cref="ICompiledQuery{T}"/> when running under AOT against an in-memory
        /// <c>EnumerableQuery</c> provider — flagging the documented BCL interpreter slowdown so hosts can
        /// detect the misconfiguration in production logs.
        /// </summary>
        /// <param name="entityType">Full type name of the entity <c>T</c> for the affected compiled query.</param>
        [Event(
            1,
            Level = EventLevel.Warning,
            Message = "OdataQueryLite running under AOT but the IQueryable source is an in-memory EnumerableQuery. Each row is evaluated via the BCL Expression interpreter (no JIT codegen). Expect 5-50x slowdown vs JIT. For production data sets pass an EF Core IQueryable<T> instead, or build with JIT. EntityType={0}")]
        public void AotInMemoryProviderDetected(string entityType)
        {
            WriteEvent(1, entityType);
        }
    }
}
