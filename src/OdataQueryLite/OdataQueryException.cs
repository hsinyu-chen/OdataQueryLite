using System;

namespace OdataQueryLite
{
    /// <summary>
    /// Base for any client-error condition raised by the OdataQueryLite engine — caller input was
    /// syntactically or semantically invalid for the entity model (e.g. <c>$count</c> applied to a
    /// non-collection, or an unsupported <c>$</c>-option).
    /// </summary>
    /// <remarks>
    /// Host code (the AspNet middleware <see cref="OdataQueryLite.AspNetCore.UnsupportedQueryOptionMiddleware"/>,
    /// or a custom global exception handler) can catch this single type and map to HTTP 400, instead of
    /// guessing among <see cref="ArgumentException"/> / <see cref="InvalidOperationException"/> / etc. which
    /// would otherwise collide with framework-internal "developer error" exceptions and lead to 500s.
    /// </remarks>
    /// <param name="message">Human-readable description of the offending input.</param>
    public class OdataQueryException(string message) : Exception(message);
}
