namespace OdataQueryLite.EFCore.Tests.Model
{
    // Stored as STRING via HasConversion<string>() on the entity config — exercises the engine's
    // enum-from-string-literal coercion path (TypeCoercion.Coerce) against a real provider column.
    public enum Status
    {
        Active,
        Pending,
        Closed,
    }

    // Doubles as the nullable-enum corpus AND the Category.Kind ("Kind") enum, so a single enum
    // covers enum + nullable-enum + enum-as-string on a ref nav.
    public enum Priority
    {
        Low,
        Medium,
        High,
    }
}
