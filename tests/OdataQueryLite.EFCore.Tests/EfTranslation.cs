namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>
    /// EF Core 10 removed <c>QueryClientEvaluationWarning</c> (gone since EF Core 3.0): a query whose
    /// filter / order-by cannot be translated to SQL now THROWS <see cref="InvalidOperationException"/>
    /// ("could not be translated") at translation time instead of silently client-evaluating. That
    /// throw is the modern client-eval signal — the harness uses real SQLite (never the EF InMemory
    /// provider, which would client-evaluate everything and hide translation gaps) and surfaces a
    /// <c>ToQueryString()</c> / materialization throw of this kind as a translation gap.
    /// </summary>
    public static class EfTranslation
    {
        public static bool IsTranslationFailure(Exception ex) =>
            ex is InvalidOperationException && ex.Message.Contains("could not be translated", StringComparison.OrdinalIgnoreCase);
    }
}
