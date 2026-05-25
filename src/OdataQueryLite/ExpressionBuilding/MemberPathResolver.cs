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
        // Walks the implemented interfaces to find IEnumerable<T> and return T. Accepts any
        // type implementing IEnumerable<T> (including third-party collections), with string
        // explicitly excluded — string implements IEnumerable<char> but `$count("Name")` would
        // return character count, not a collection cardinality, surprising callers.
        // DynamicallyAccessedMembers(Interfaces) propagates the trim requirement: callers must
        // hand in a Type whose interface metadata the trimmer preserved. EF Core navigation
        // property types satisfy this because they're reachable from the entity class which
        // already carries [DynamicallyAccessedMembers(PublicProperties)] up at the engine entry.
        public static Type GetEnumerableElementType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type t)
        {
            if (t == typeof(string)) return null;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
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
