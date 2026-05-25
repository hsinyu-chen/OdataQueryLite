using System;
using System.Runtime.CompilerServices;

namespace OdataQueryLite.Diagnostics
{
    internal static class RuntimeProbe
    {
        // Indirection over RuntimeFeature.IsDynamicCodeSupported so tests can simulate the
        // AOT branch (where the static returns true) without an actual NativeAOT publish.
        internal static Func<bool> IsDynamicCodeSupported = static () => RuntimeFeature.IsDynamicCodeSupported;
    }
}
