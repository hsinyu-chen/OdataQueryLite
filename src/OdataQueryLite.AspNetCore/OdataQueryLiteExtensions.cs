using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OdataQueryLite.Caching;

namespace OdataQueryLite.AspNetCore
{
    /// <summary>
    /// Registration helpers that host applications use to wire OdataQueryLite into the ASP.NET Core DI /
    /// MVC / pipeline surface. The split mirrors the ASP.NET Core convention where generic infrastructure
    /// lives on <see cref="IServiceCollection"/> and feature-specific glue lives on the feature builder.
    /// </summary>
    /// <remarks>
    /// Typical wiring:
    /// <code>
    /// services.AddOdataQueryLite();                       // Minimal-API or shared infra
    /// services.AddControllers().AddOdataQueryLite();      // MVC users opt into the binder
    /// app.UseOdataQueryLite();                            // error -&gt; 400 mapping
    /// </code>
    /// </remarks>
    public static class OdataQueryLiteExtensions
    {
        /// <summary>
        /// Registers the optional process-wide <see cref="QueryCompileCache"/>. Does not touch
        /// <c>MvcOptions</c>, so pure Minimal-API hosts don't carry a dead
        /// <c>IConfigureOptions&lt;MvcOptions&gt;</c> callback.
        /// </summary>
        /// <param name="services">Host service collection.</param>
        /// <param name="configure">Optional callback to tune <see cref="OdataQueryLiteOptions"/>.</param>
        /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        public static IServiceCollection AddOdataQueryLite(
            this IServiceCollection services,
            Action<OdataQueryLiteOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            var opts = new OdataQueryLiteOptions();
            configure?.Invoke(opts);
            if (opts.UseCache)
                services.TryAddSingleton(_ => new QueryCompileCache(opts.MaxCacheEntries));
            return services;
        }

        /// <summary>
        /// MVC opt-in: inserts <see cref="OdataQueryOptionsBinderProvider"/> so action parameters of type
        /// <see cref="OdataQueryOptions{T}"/> are bound from the query string. Idempotent — repeated calls
        /// skip the second insert so the provider chain doesn't gain duplicate entries.
        /// </summary>
        /// <param name="builder">MVC builder returned by <c>AddControllers</c> / <c>AddMvc</c>.</param>
        /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Registers a model binder provider that constructs OdataQueryOptions<T> via reflection at request time.")]
        [RequiresDynamicCode("Registers a model binder provider that constructs Expression<Func<T, ...>> via runtime codegen.")]
        public static IMvcBuilder AddOdataQueryLite(this IMvcBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddOdataQueryLite();
            builder.Services.Configure<MvcOptions>(opts =>
            {
                if (opts.ModelBinderProviders.OfType<OdataQueryOptionsBinderProvider>().Any())
                    return;
                // Insert at index 0 so we win over the default complex-object binder when the
                // parameter type matches OdataQueryOptions<>. Provider checks the generic
                // definition before claiming the binding, so non-matching types are passed
                // through to subsequent providers untouched.
                opts.ModelBinderProviders.Insert(0, new OdataQueryOptionsBinderProvider());
            });
            return builder;
        }

        /// <summary>
        /// Adds <see cref="UnsupportedQueryOptionMiddleware"/> to the pipeline so
        /// <see cref="OdataQueryException"/>s thrown by the binder layer surface as HTTP 400. Place this
        /// before <c>UseRouting</c> / <c>UseEndpoints</c> so it wraps both MVC binders and Minimal-API
        /// <c>BindAsync</c>.
        /// </summary>
        /// <param name="app">The application pipeline builder.</param>
        /// <returns>The same <paramref name="app"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
        public static IApplicationBuilder UseOdataQueryLite(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<UnsupportedQueryOptionMiddleware>();
        }
    }
}
