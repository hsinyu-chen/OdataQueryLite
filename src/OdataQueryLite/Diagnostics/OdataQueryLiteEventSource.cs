using System.Diagnostics.Tracing;

namespace OdataQueryLite.Diagnostics
{
    [EventSource(Name = "OdataQueryLite")]
    public sealed class OdataQueryLiteEventSource : EventSource
    {
        public static readonly OdataQueryLiteEventSource Log = new();

        private OdataQueryLiteEventSource() { }

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
