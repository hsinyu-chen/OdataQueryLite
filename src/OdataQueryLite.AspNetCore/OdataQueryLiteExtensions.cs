using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OdataQueryLite.Caching;

namespace OdataQueryLite.AspNetCore
{
    // One-line surface for hosts: AddOdataQueryLite() at service config time + UseOdataQueryLite()
    // in the pipeline. Everything else (binder discovery, $apply rejection, error -> 400) is
    // wired automatically; callers should not need to know about the binder provider or the
    // middleware classes.
    public static class OdataQueryLiteExtensions
    {
        // Registers the model-binder provider and (optionally) a process-wide QueryCompileCache.
        // The cache is a singleton because it bounds Expression-compilation work across all
        // requests; without it every request reparses + recompiles the filter.
        [RequiresUnreferencedCode("Registers a model binder provider that constructs OdataQueryOptions<T> via reflection at request time.")]
        [RequiresDynamicCode("Registers a model binder provider that constructs Expression<Func<T, ...>> via runtime codegen.")]
        public static IServiceCollection AddOdataQueryLite(
            this IServiceCollection services,
            bool useCache = true)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (useCache)
                services.TryAddSingleton<QueryCompileCache>();

            services.Configure<MvcOptions>(opts =>
            {
                // Insert at index 0 so we win over the default complex-object binder when the
                // parameter type matches OdataQueryOptions<>. Provider checks the generic
                // definition before claiming the binding, so non-matching types are passed
                // through to subsequent providers untouched.
                opts.ModelBinderProviders.Insert(0, new OdataQueryOptionsBinderProvider());
            });
            return services;
        }

        // Adds the error-mapping middleware. Place it before UseRouting/UseEndpoints so it
        // wraps the MVC pipeline that runs model binders.
        public static IApplicationBuilder UseOdataQueryLite(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<UnsupportedQueryOptionMiddleware>();
        }
    }
}
