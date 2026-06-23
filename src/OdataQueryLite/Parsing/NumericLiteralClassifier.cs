using System.Globalization;

namespace OdataQueryLite.Parsing
{
    /// <summary>
    /// Single source of truth for how a numeric literal's text maps to a CLR value and, from that, its
    /// cache-key shape tag. The cache-key shape (<see cref="LexedQuery"/>) and the parsed slot value
    /// (<see cref="FilterParser"/>) both derive from <see cref="Parse"/>, so a shape tag and the resolved
    /// slot type can never disagree — that agreement keeps the compiled-query slot a deterministic
    /// function of the shape.
    /// </summary>
    internal static class NumericLiteralClassifier
    {
        /// <summary>Parses to the boxed CLR value — <see cref="long"/> for integers, <see cref="decimal"/> for fractionals, <see cref="double"/> on decimal overflow.</summary>
        public static object Parse(string text)
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Untyped cache-key shape suffix for a numeric literal, derived from the same <see cref="Parse"/>
        /// so it tracks the slot's CLR kind — distinct per kind (<c>int</c> / <c>dec</c> / <c>dbl</c>) so
        /// integer and fractional literals don't share a key.
        /// </summary>
        public static string ShapeTag(string text) => Parse(text) switch
        {
            long => "int",
            decimal => "dec",
            _ => "dbl",
        };
    }
}
