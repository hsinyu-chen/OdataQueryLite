using System;

namespace OdataQueryLite
{
    // Base for any client-error condition raised by the OdataQueryLite engine — caller
    // input was syntactically or semantically invalid for the entity model (e.g. `$count`
    // applied to a non-collection, or an unsupported $-option). Host code (the future
    // AspNetCore middleware in Phase 1.B.12, or a custom global exception handler) can
    // catch this single type and map to HTTP 400, instead of guessing among
    // ArgumentException / InvalidOperationException / etc. which would otherwise
    // collide with framework-internal "developer error" exceptions and lead to 500s.
    public class OdataQueryException(string message) : Exception(message);
}
