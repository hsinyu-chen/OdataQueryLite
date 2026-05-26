using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OdataQueryLite.AspNetCore
{
    // Detects action parameters of shape OdataQueryOptions<T> and routes them to
    // OdataQueryOptionsBinder<T>. Registered into MvcOptions.ModelBinderProviders by
    // AddOdataQueryLite() so callers don't have to wire this manually.
    public sealed class OdataQueryOptionsBinderProvider : IModelBinderProvider
    {
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
