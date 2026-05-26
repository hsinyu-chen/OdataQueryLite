using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    /// <summary>
    /// Type-bridging helpers shared by the Expression-building stack: resolving the nullable "slot type" for
    /// each literal, coercing raw parser values into that slot type, and lifting <see cref="Expression"/>
    /// nodes into the slot type so binary operators agree on operand types.
    /// </summary>
    public static class TypeCoercion
    {
        /// <summary>
        /// Returns the nullable counterpart of a value type, or the type itself for reference / already-nullable
        /// types. Slot types are always nullable so a single cache shape covers both null and non-null calls.
        /// </summary>
        /// <param name="memberType">CLR member type to wrap.</param>
        /// <returns>Nullable slot type.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="memberType"/> is <see langword="null"/>.</exception>
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Nullable<T> is a fundamental BCL type whose instantiations the AOT runtime preserves for any value type reachable at compile time.")]
        public static Type SlotTypeFor(Type memberType)
        {
            ArgumentNullException.ThrowIfNull(memberType);
            return memberType.IsValueType && Nullable.GetUnderlyingType(memberType) == null
                ? typeof(Nullable<>).MakeGenericType(memberType)
                : memberType;
        }

        /// <summary>
        /// Coerces a raw literal value from the parser into the CLR <paramref name="slotType"/> expected by the
        /// compiled filter. Handles enum-from-string, date/time kind normalisation, <see cref="Guid"/> parsing,
        /// and the <see cref="IConvertible"/> fallback.
        /// </summary>
        /// <param name="rawValue">Boxed value as produced by the parser; <see langword="null"/> passes through.</param>
        /// <param name="kind">The literal kind reported by the parser (used in error messages).</param>
        /// <param name="slotType">Target slot type — typically a <see cref="Nullable{T}"/>.</param>
        /// <returns>The coerced value, or <see langword="null"/> when <paramref name="rawValue"/> is null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="slotType"/> is <see langword="null"/>.</exception>
        /// <exception cref="OdataQueryException">The value cannot be coerced — e.g. wrong literal kind, <see cref="DateTimeKind.Unspecified"/>, or no conversion path.</exception>
        public static object? Coerce(object? rawValue, LiteralKind kind, Type slotType)
        {
            ArgumentNullException.ThrowIfNull(slotType);
            if (rawValue == null) return null;

            var target = Nullable.GetUnderlyingType(slotType) ?? slotType;
            if (target == rawValue.GetType()) return rawValue;

            if (target.IsEnum)
            {
                if (rawValue is not string s)
                    throw new OdataQueryException($"Enum slot {target.Name} expects string literal; got {kind}.");
                return Enum.Parse(target, s, ignoreCase: false);
            }

            // Parser hands us a DateTimeOffset for date/time literals (OData v4 ABNF
            // requires `Z` or `+hh:mm`, so the offset is always known).
            if (target == typeof(DateTimeOffset))
            {
                return rawValue switch
                {
                    DateTimeOffset dto => dto,
                    DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                    _ => throw new OdataQueryException($"DateTimeOffset slot expects DateTime/DateTimeOffset literal; got {rawValue.GetType().Name}.")
                };
            }

            if (target == typeof(DateTime))
            {
                return rawValue switch
                {
                    DateTimeOffset dto => dto.UtcDateTime,
                    DateTime { Kind: DateTimeKind.Utc } dt => dt,
                    DateTime { Kind: DateTimeKind.Local } dt => dt.ToUniversalTime(),
                    DateTime { Kind: DateTimeKind.Unspecified } => throw new OdataQueryException(
                        "DateTime literal has Unspecified kind; OData v4 requires UTC (Z) or explicit offset."),
                    _ => throw new OdataQueryException($"DateTime slot expects DateTime/DateTimeOffset literal; got {rawValue.GetType().Name}.")
                };
            }

            if (target == typeof(Guid))
            {
                if (rawValue is string gs) return Guid.Parse(gs);
                if (rawValue is Guid g) return g;
                throw new OdataQueryException($"Guid slot expects string / Guid literal; got {rawValue.GetType().Name}.");
            }

            if (rawValue is IConvertible)
                return Convert.ChangeType(rawValue, target, CultureInfo.InvariantCulture);

            throw new OdataQueryException(
                $"Cannot coerce literal of type {rawValue.GetType().Name} (kind {kind}) to slot type {target.Name}.");
        }

        /// <summary>
        /// Builds <c>(slotType)argsParam[idx]</c>. EF Core's <c>EvaluatableExpressionFilter</c> treats the
        /// subtree as a captured value and emits a parameterized SQL <c>@p</c> placeholder instead of inlining
        /// the literal.
        /// </summary>
        /// <param name="argsParam">The <c>object[]</c> literal-array parameter.</param>
        /// <param name="idx">Zero-based slot index.</param>
        /// <param name="slotType">Target slot type.</param>
        /// <returns>An expression that fetches the slot value and casts to <paramref name="slotType"/>.</returns>
        public static Expression LiteralAccess(Expression argsParam, int idx, Type slotType)
        {
            return Expression.Convert(
                Expression.ArrayIndex(argsParam, Expression.Constant(idx)),
                slotType);
        }

        /// <summary>Lifts <paramref name="expr"/> to <paramref name="slotType"/> via <see cref="Expression.Convert(Expression, Type)"/> if it isn't already that type.</summary>
        /// <param name="expr">Source expression.</param>
        /// <param name="slotType">Desired type.</param>
        /// <returns>An expression of type <paramref name="slotType"/>.</returns>
        public static Expression LiftToSlotType(Expression expr, Type slotType) =>
            expr.Type == slotType ? expr : Expression.Convert(expr, slotType);
    }
}
