using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    public static class TypeCoercion
    {
        // Slot types are always nullable so that a single cache shape covers both
        // null and non-null arg patterns for the same query template.
        // OData v4 spec (Part 2, "null is equal only to itself") makes lifted comparisons
        // produce the spec-correct silently-false result for non-nullable members vs null.
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Nullable<T> is a fundamental BCL type whose instantiations the AOT runtime preserves for any value type reachable at compile time.")]
        public static Type SlotTypeFor(Type memberType)
        {
            ArgumentNullException.ThrowIfNull(memberType);
            return memberType.IsValueType && Nullable.GetUnderlyingType(memberType) == null
                ? typeof(Nullable<>).MakeGenericType(memberType)
                : memberType;
        }

        public static object Coerce(object rawValue, LiteralKind kind, Type slotType)
        {
            ArgumentNullException.ThrowIfNull(slotType);
            if (rawValue == null) return null;

            var target = Nullable.GetUnderlyingType(slotType) ?? slotType;
            if (target == rawValue.GetType()) return rawValue;

            if (target.IsEnum)
            {
                if (rawValue is not string s)
                    throw new ArgumentException($"Enum slot {target.Name} expects string literal; got {kind}.");
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
                    _ => throw new ArgumentException($"DateTimeOffset slot expects DateTime/DateTimeOffset literal; got {rawValue.GetType().Name}.")
                };
            }

            if (target == typeof(DateTime))
            {
                return rawValue switch
                {
                    DateTimeOffset dto => dto.UtcDateTime,
                    DateTime { Kind: DateTimeKind.Utc } dt => dt,
                    DateTime { Kind: DateTimeKind.Local } dt => dt.ToUniversalTime(),
                    DateTime { Kind: DateTimeKind.Unspecified } => throw new ArgumentException(
                        "DateTime literal has Unspecified kind; OData v4 requires UTC (Z) or explicit offset."),
                    _ => throw new ArgumentException($"DateTime slot expects DateTime/DateTimeOffset literal; got {rawValue.GetType().Name}.")
                };
            }

            if (target == typeof(Guid))
            {
                if (rawValue is string gs) return Guid.Parse(gs);
                if (rawValue is Guid g) return g;
                throw new ArgumentException($"Guid slot expects string / Guid literal; got {rawValue.GetType().Name}.");
            }

            if (rawValue is IConvertible)
                return Convert.ChangeType(rawValue, target, CultureInfo.InvariantCulture);

            throw new ArgumentException(
                $"Cannot coerce literal of type {rawValue.GetType().Name} (kind {kind}) to slot type {target.Name}.");
        }

        // args[idx] is object; Convert unboxes / casts to slotType. EF Core's
        // EvaluatableExpressionFilter treats the subtree as a captured value and emits
        // a parameterized SQL @p placeholder instead of inlining the literal.
        public static Expression LiteralAccess(Expression argsParam, int idx, Type slotType)
        {
            return Expression.Convert(
                Expression.ArrayIndex(argsParam, Expression.Constant(idx)),
                slotType);
        }

        public static Expression LiftToSlotType(Expression expr, Type slotType) =>
            expr.Type == slotType ? expr : Expression.Convert(expr, slotType);
    }
}
