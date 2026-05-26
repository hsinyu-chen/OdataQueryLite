using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace OdataQueryLite.AspNetCore
{
    // Pure mapping IQueryCollection -> OdataQueryParts. Extracted from the model binder so
    // it can be unit-tested without spinning up MVC and so non-MVC hosts (minimal APIs,
    // background jobs reading captured query strings) can reuse it.
    public static class OdataQueryPartsFactory
    {
        public static OdataQueryParts FromQuery(IQueryCollection query)
        {
            ArgumentNullException.ThrowIfNull(query);
            return new OdataQueryParts
            {
                Filter = ReadString(query, "$filter"),
                OrderBy = ReadString(query, "$orderby"),
                Expand = ReadString(query, "$expand"),
                Select = ReadString(query, "$select"),
                Apply = ReadString(query, "$apply"),
                Top = ReadInt(query, "$top"),
                Skip = ReadInt(query, "$skip"),
                Count = ReadBool(query, "$count"),
            };
        }

        private static string? ReadString(IQueryCollection query, string key)
        {
            if (!query.TryGetValue(key, out var values)) return null;
            // OData v4.01 Part 1 §11.2: "The same system query option MUST NOT be specified
            // more than once." Without this check StringValues silently joins repeats with
            // a comma, which would turn `?$top=5&$top=10` into `"5,10"` and surface as a
            // confusing int-parse error rather than the actual protocol violation.
            if (values.Count > 1)
                throw new OdataQueryException($"{key} must not appear more than once.");
            var s = values.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static int? ReadInt(IQueryCollection query, string key)
        {
            var raw = ReadString(query, key);
            if (raw is null) return null;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                throw new OdataQueryException($"{key} must be an integer; got '{raw}'.");
            return n;
        }

        // OData v4.01 Part 2 §5.1.6: $count value is `true` or `false`. Absence treated as
        // false (no total count requested). We accept case-insensitive matches.
        private static bool ReadBool(IQueryCollection query, string key)
        {
            var raw = ReadString(query, key);
            if (raw is null) return false;
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new OdataQueryException($"{key} must be 'true' or 'false'; got '{raw}'.");
        }
    }
}
