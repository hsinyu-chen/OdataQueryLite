using System.Runtime.CompilerServices;

namespace OdataQueryLite.Diagnostics
{
    internal static class RuntimeProbe
    {
        // Plain static bool over Func<bool> so the JIT/AOT call site is a single field
        // load — no delegate dispatch on the hot Apply path. Tests assign this directly
        // (via InternalsVisibleTo) to simulate the AOT branch without a NativeAOT publish.
        internal static bool IsDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;
    }
}
