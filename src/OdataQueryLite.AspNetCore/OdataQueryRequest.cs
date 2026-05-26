using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OdataQueryLite.Caching;

namespace OdataQueryLite.AspNetCore
{
    // Minimal-API parameter type. Wraps OdataQueryOptions<T> behind the `BindAsync(HttpContext)`
    // contract that the Minimal-API endpoint factory recognises, so endpoint handlers can take
    // it directly:
    //     app.MapGet("/items", (OdataQueryRequest<Item> q) => q.Options.Apply(...));
    //
    // The MVC IModelBinder path (OdataQueryOptions<T> as the action parameter) is unchanged —
    // this exists only so Minimal-API users avoid the manual
    // `OdataQueryPartsFactory.FromQuery + new OdataQueryOptions<T>(...)` two-step. The wrapper
    // intentionally lives in OdataQueryLite.AspNetCore rather than core, so the core package
    // stays pure .NET and remains usable from console / batch / non-web contexts.
    public sealed class OdataQueryRequest<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        where T : class
    {
        public OdataQueryOptions<T> Options { get; }

        private OdataQueryRequest(OdataQueryOptions<T> options)
        {
            Options = options;
        }

        // BindAsync must be static and exactly match this signature (or one of a few defined
        // by RequestDelegateFactory) for Minimal APIs to discover and call it.
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "OdataQueryOptions<T> ctor's trim requirements are declared on AddOdataQueryLite() entry point.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
            Justification = "OdataQueryOptions<T> ctor's dynamic-code requirements are declared on AddOdataQueryLite() entry point.")]
        public static ValueTask<OdataQueryRequest<T>?> BindAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var logger = context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("OdataQueryLite.AspNetCore.OdataQueryRequest");
            logger?.LogDebug("Binding OdataQueryRequest<{EntityType}> from {Query}", typeof(T).Name, context.Request.QueryString.Value);
            var parts = OdataQueryPartsFactory.FromQuery(context.Request.Query);
            var cache = context.RequestServices.GetService<QueryCompileCache>();
            var options = new OdataQueryOptions<T>(parts, cache);
            return ValueTask.FromResult<OdataQueryRequest<T>?>(new OdataQueryRequest<T>(options));
        }
    }
}
