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
    // One-line surface for hosts. The split mirrors the ASP.NET Core convention where
    // generic infrastructure lives on IServiceCollection and feature-specific glue lives on
    // the feature builder (AddControllers().AddJsonOptions(), AddAuthentication().AddJwtBearer()):
    //
    //   services.AddOdataQueryLite();                       // Minimal-API or shared infra
    //   services.AddControllers().AddOdataQueryLite();      // MVC users opt into the binder
    //
    // Both paths share UseOdataQueryLite() for error -> 400 mapping.
    public static class OdataQueryLiteExtensions
    {
        // Registers the optional process-wide QueryCompileCache. Does NOT touch MvcOptions,
        // so a pure Minimal-API host doesn't carry a dead IConfigureOptions<MvcOptions>
        // callback. MVC hosts get the binder via the IMvcBuilder overload below. Accepts
        // an Action<OdataQueryLiteOptions> for tunable knobs (cache cap, etc.) — IOptions
        // pattern lets us add more knobs later without breaking the call signature.
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

        // MVC opt-in: inserts OdataQueryOptionsBinderProvider so action parameters of type
        // OdataQueryOptions<T> get bound from the query string. Idempotent — repeated calls
        // (multiple module initializers, test fixtures rebuilding services) skip the second
        // insert so the provider chain doesn't get duplicate entries.
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

        // Adds the error-mapping middleware. Place it before UseRouting/UseEndpoints so it
        // wraps the request pipeline that runs both MVC binders and Minimal-API BindAsync.
        public static IApplicationBuilder UseOdataQueryLite(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<UnsupportedQueryOptionMiddleware>();
        }
    }
}
