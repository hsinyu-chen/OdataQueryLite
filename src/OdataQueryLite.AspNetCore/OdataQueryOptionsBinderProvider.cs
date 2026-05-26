using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OdataQueryLite.AspNetCore
{
    /// <summary>
    /// Detects action parameters of shape <see cref="OdataQueryOptions{T}"/> and routes them to
    /// <see cref="OdataQueryOptionsBinder{T}"/>. Registered into <c>MvcOptions.ModelBinderProviders</c> by
    /// <see cref="OdataQueryLiteExtensions.AddOdataQueryLite(IMvcBuilder)"/> so callers don't have to wire
    /// this manually.
    /// </summary>
    public sealed class OdataQueryOptionsBinderProvider : IModelBinderProvider
    {
        /// <summary>
        /// Returns an <see cref="OdataQueryOptionsBinder{T}"/> when the action parameter is closed-generic
        /// over <see cref="OdataQueryOptions{T}"/>; otherwise <see langword="null"/> so the next provider in
        /// the chain is tried.
        /// </summary>
        /// <param name="context">Provider context.</param>
        /// <returns>The matched binder, or <see langword="null"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        [UnconditionalSuppressMessage("Trimming", "IL2055:MakeGenericType",
            Justification = "Constructs OdataQueryOptionsBinder<T> from the entity type embedded in the action signature; AddOdataQueryLite() declares the trim requirement for the whole pipeline.")]
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "Trim requirements declared on AddOdataQueryLite() entry point.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
            Justification = "Dynamic-code requirements declared on AddOdataQueryLite() entry point.")]
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var t = context.Metadata.ModelType;
            if (!t.IsGenericType) return null;
            if (t.GetGenericTypeDefinition() != typeof(OdataQueryOptions<>)) return null;
            var entity = t.GetGenericArguments()[0];
            var binderType = typeof(OdataQueryOptionsBinder<>).MakeGenericType(entity);
            return (IModelBinder)Activator.CreateInstance(binderType)!;
        }
    }
}
