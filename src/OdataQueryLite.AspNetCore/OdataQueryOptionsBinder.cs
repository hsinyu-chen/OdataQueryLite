using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OdataQueryLite.Caching;

namespace OdataQueryLite.AspNetCore
{
    // Per-entity-type IModelBinder. Reads $-options from HttpRequest.Query, constructs
    // OdataQueryOptions<T>, and hands it to MVC as the action argument. Parse-time errors
    // propagate as OdataQueryException — UnsupportedQueryOptionMiddleware turns those into
    // HTTP 400 at the pipeline boundary.
    //
    // [DAM(PublicProperties)] T: the constructed OdataQueryOptions<T> requires T's
    // properties to be preserved by the trimmer.
    public sealed class OdataQueryOptionsBinder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        : IModelBinder where T : class
    {
        // IModelBinder.BindModelAsync is not annotated upstream so we cannot propagate
        // RequiresUnreferencedCode / RequiresDynamicCode on the override (IL2046 / IL3051).
        // The dynamic-code requirement is declared on AddOdataQueryLite() — the public
        // surface every consumer goes through — so suppressing here keeps the warning chain
        // single-rooted instead of leaking through every MVC invocation site.
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "OdataQueryOptions<T> ctor's trim requirements are declared on AddOdataQueryLite() entry point.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
            Justification = "OdataQueryOptions<T> ctor's dynamic-code requirements are declared on AddOdataQueryLite() entry point.")]
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            var http = bindingContext.HttpContext;
            // Logger fetched per-request because the binder is created once via Activator
            // (no DI ctor injection). Debug-level so a host can flip log filter to confirm
            // OdataQueryLite is actually claiming the parameter without rebuilding.
            var logger = http.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("OdataQueryLite.AspNetCore.OdataQueryOptionsBinder");
            logger?.LogDebug("Binding OdataQueryOptions<{EntityType}> from {Query}", typeof(T).Name, http.Request.QueryString.Value);
            var parts = OdataQueryPartsFactory.FromQuery(http.Request.Query);
            // QueryCompileCache is optional — host opts in via AddOdataQueryLite() default
            // (or by registering it themselves). Absent registration = no cross-request
            // compile reuse, which is correct behavior for small surfaces and tests.
            var cache = http.RequestServices.GetService<QueryCompileCache>();
            var options = new OdataQueryOptions<T>(parts, cache);
            bindingContext.Result = ModelBindingResult.Success(options);
            return Task.CompletedTask;
        }
    }
}
