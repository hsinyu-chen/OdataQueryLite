namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>
    /// Selects the EF Core provider the harness runs against, via the <c>ODATA_HARNESS_PROVIDER</c>
    /// environment variable: <c>sqlite</c> (default), <c>localdb</c> / <c>mssql</c> / <c>sqlserver</c>
    /// (SQL Server LocalDB), or <c>postgres</c> / <c>postgresql</c> / <c>pg</c> (PostgreSQL). The golden
    /// fixture is provider-specific so an engine run is never compared against a golden captured on a
    /// different backend — SQLite cannot translate date/math functions, DateTimeOffset ORDER BY, or
    /// native decimal that SQL Server / PostgreSQL can, so the two diverge legitimately.
    /// </summary>
    public static class HarnessConfig
    {
        public static string Provider =>
            (Environment.GetEnvironmentVariable("ODATA_HARNESS_PROVIDER") ?? "sqlite").Trim().ToLowerInvariant() switch
            {
                "localdb" or "mssql" or "sqlserver" => "localdb",
                "postgres" or "postgresql" or "pg" => "postgres",
                _ => "sqlite",
            };

        /// <summary>
        /// Connection string for the postgres provider, supplied out-of-band via ODATA_HARNESS_PG_CONNSTRING
        /// so no credential is ever committed to source. Null when the env var is unset.
        /// </summary>
        public static string? PgConnectionString => Environment.GetEnvironmentVariable("ODATA_HARNESS_PG_CONNSTRING");

        public static string GoldenFileName => $"golden.{Provider}.json";
    }
}
