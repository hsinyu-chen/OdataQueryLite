using System;

namespace OdataQueryLite
{
    /// <summary>
    /// Marker attribute that excludes a property from <c>$select</c> / <c>$expand</c> dictionary
    /// projection. The engine also honors <c>Newtonsoft.Json.JsonIgnoreAttribute</c> and
    /// <c>System.Text.Json.Serialization.JsonIgnoreAttribute</c> by full name (no NuGet
    /// dependency); this attribute exists for the case where a property must remain visible to
    /// a JSON serializer but be hidden from OData.
    /// </summary>
    /// <remarks>
    /// Filtering happens before the projection Expression is built, so an ignored property is
    /// removed from the requested field set even when the caller explicitly named it in
    /// <c>$select</c>. The request still succeeds; the property is simply absent from the
    /// projected dictionary.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class OdataIgnoreAttribute : Attribute
    {
    }
}
