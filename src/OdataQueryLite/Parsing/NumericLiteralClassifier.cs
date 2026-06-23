using System.Globalization;

namespace OdataQueryLite.Parsing
{
    /// <summary>Numeric kind a literal parses to, in the lexer's <c>long → decimal → double</c> precedence.</summary>
    internal enum NumericLiteralKind { Integer, Decimal, Double }

    /// <summary>
    /// Single source of truth for how a numeric literal's text maps to a CLR kind. The cache-key shape
    /// (<see cref="LexedQuery"/>) and the parsed slot value (<see cref="FilterParser"/>) both go through
    /// this, so a shape tag and the resolved slot type can never disagree — that agreement is what keeps
    /// the compiled-query slot a deterministic function of the shape.
    /// </summary>
    internal static class NumericLiteralClassifier
    {
        /// <summary>Classifies by the same precedence <see cref="Parse"/> uses: integer (fits long), else decimal, else double.</summary>
        public static NumericLiteralKind Classify(string text) =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? NumericLiteralKind.Integer
            : decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? NumericLiteralKind.Decimal
            : NumericLiteralKind.Double;

        /// <summary>The untyped cache-key shape suffix for a numeric literal — distinct per kind so integer and fractional literals don't share a key.</summary>
        public static string ShapeTag(string text) => Classify(text) switch
        {
            NumericLiteralKind.Integer => "int",
            NumericLiteralKind.Decimal => "dec",
            _ => "dbl",
        };

        /// <summary>Parses to the boxed CLR value — <see cref="long"/> for integers, <see cref="decimal"/> for fractionals, <see cref="double"/> on decimal overflow.</summary>
        public static object Parse(string text) => Classify(text) switch
        {
            NumericLiteralKind.Integer => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            NumericLiteralKind.Decimal => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => double.Parse(text, CultureInfo.InvariantCulture),
        };
    }
}
