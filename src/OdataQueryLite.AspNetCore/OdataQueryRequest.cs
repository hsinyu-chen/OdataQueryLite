using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OdataQueryLite.Caching;

namespace OdataQueryLite.AspNetCore
{
    /// <summary>
    /// Minimal-API parameter type. Wraps <see cref="OdataQueryOptions{T}"/> behind the
    /// <c>BindAsync(HttpContext)</c> contract that the Minimal-API endpoint factory recognises, so endpoint
    /// handlers can take it directly:
    /// <code>app.MapGet("/items", (OdataQueryRequest&lt;Item&gt; q) =&gt; q.Options.Apply(...));</code>
    /// </summary>
    /// <typeparam name="T">Entity type. Its public properties must be preserved by the trimmer.</typeparam>
    public sealed class OdataQueryRequest<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        where T : class
    {
        /// <summary>The parsed <see cref="OdataQueryOptions{T}"/> for the current request.</summary>
        public OdataQueryOptions<T> Options { get; }

        private OdataQueryRequest(OdataQueryOptions<T> options)
        {
            Options = options;
        }

        /// <summary>
        /// Minimal-API binding entry point. Must be static and match this exact signature for the framework
        /// to discover it. Parse failures throw <see cref="OdataQueryException"/>, mapped to HTTP 400 by
        /// <see cref="UnsupportedQueryOptionMiddleware"/>.
        /// </summary>
        /// <param name="context">Current HTTP context.</param>
        /// <returns>The bound request wrapper.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Constructs OdataQueryOptions<T> which reflects over T's public properties to compile filter Expression trees at runtime.")]
        [RequiresDynamicCode("Constructs OdataQueryOptions<T> which builds Expression<Func<T, ...>> at runtime; AOT may require dynamic-code support.")]
        public static ValueTask<OdataQueryRequest<T>?> BindAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var logger = context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("OdataQueryLite.AspNetCore.OdataQueryRequest");
            if (logger is not null)
                AspNetCoreLog.BindingRequest(logger, typeof(T).Name, context.Request.QueryString.Value);
            var parts = OdataQueryPartsFactory.FromQuery(context.Request.Query);
            var cache = context.RequestServices.GetService<QueryCompileCache>();
            var options = new OdataQueryOptions<T>(parts, cache);
            return ValueTask.FromResult<OdataQueryRequest<T>?>(new OdataQueryRequest<T>(options));
        }
    }
}
