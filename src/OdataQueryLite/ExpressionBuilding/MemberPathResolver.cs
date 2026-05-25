using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace OdataQueryLite.ExpressionBuilding
{
    // Shared between FilterExpressionBuilder and OrderByApplier so the OData semantic surface
    // (member resolution error messages + collection-element-type detection used by `$count`)
    // is identical across $filter and $orderby. Diverging messages would surface as
    // "different exception in $orderby vs $filter for the same broken request" — a user-error
    // class that must produce the same diagnostic regardless of which $-option triggered it.
    internal static class MemberPathResolver
    {
        // AOT-clean: explicit BaseType walk catches custom collections without GetInterfaces().
        public static Type GetEnumerableElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            for (var c = t; c != null; c = c.BaseType)
            {
                if (!c.IsGenericType) continue;
                var def = c.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(IQueryable<>)
                    || def == typeof(ICollection<>) || def == typeof(IList<>)
                    || def == typeof(IReadOnlyCollection<>) || def == typeof(IReadOnlyList<>)
                    || def == typeof(ISet<>) || def == typeof(IReadOnlySet<>)
                    || def == typeof(List<>) || def == typeof(HashSet<>))
                {
                    return c.GetGenericArguments()[0];
                }
            }
            return null;
        }

        public static PropertyInfo ResolveProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
            string name)
        {
            var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null) return pi;
            var available = string.Join(", ", t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
            throw new OdataQueryException(
                $"Property '{name}' not found on type '{t.Name}'. Available: {available}.");
        }
    }
}
