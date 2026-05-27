using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
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
        public static Type? GetEnumerableElementType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type t)
        {
            // string + byte[] are OData primitive types (Edm.String / Edm.Binary), not collections.
            // string implements IEnumerable<char>; byte[] is an array of primitives. Treating
            // either as a collection would reject `$orderby=Name` (the round-3 collection-orderby
            // guard) and `$orderby=RowVersion` (byte[] concurrency tokens).
            if (t == typeof(string) || t == typeof(byte[])) return null;
            // Multi-dimensional arrays (int[,]) report IsArray=true but don't implement
            // IEnumerable<T> — accepting the rank-1 element type would compile Enumerable.Count<T>
            // and explode as 500 at execution. Only single-dimensional arrays are valid OData
            // collections. Multi-dim falls through to GetInterfaces() which finds nothing
            // generic and returns null, yielding a clean 400.
            if (t.IsArray && t.GetArrayRank() == 1) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            return null;
        }

        // Walks `path[startIndex..]` from `root`, dereferencing each segment as a public
        // property and translating a terminal `$count` segment into Enumerable.Count<T>().
        // Identical semantics + error messages from $filter and $orderby — the whole point
        // of this helper class.
        [RequiresUnreferencedCode("Resolves T's members by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression.Call to generic Enumerable.Count<T> at runtime for $count terminal segments.")]
        public static Expression WalkPath(Expression root, IReadOnlyList<string> path, int startIndex = 0)
        {
            var cursor = root;
            for (int i = startIndex; i < path.Count; i++)
            {
                var seg = path[i];
                if (seg == "$count")
                {
                    if (i != path.Count - 1)
                        throw new OdataQueryException($"$count must be the terminal segment; saw '{string.Join('/', path)}'.");
                    var elem = GetEnumerableElementType(cursor.Type)
                        ?? throw new OdataQueryException($"$count target is not enumerable: {cursor.Type.Name}");
                    return Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [elem], cursor);
                }
                cursor = Expression.Property(cursor, ResolveProperty(cursor.Type, seg));
            }
            return cursor;
        }

        public static PropertyInfo ResolveProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] Type t,
            string name)
        {
            var pi = GetPropertyIncludingInterfaces(t, name);
            // Treat ignored properties and indexers as if they didn't exist:
            //  - Ignored ([JsonIgnore]/[OdataIgnore]) — same error so callers can't discriminate
            //    "wrong name" from "hidden" and mount boolean probes like
            //    `$filter=startswith(Password, 's')`.
            //  - Indexers (`public object this[string key]`, default-named "Item" in metadata) —
            //    Expression.Property without index args throws ArgumentException at execution,
            //    surfacing as a 500. Filter them at the resolve boundary for a clean 400.
            // Available list filtered identically so attackers can't enumerate hidden props.
            // GetGetMethod() != null (vs PropertyInfo.CanRead, which counts non-public
            // getters too) rules out `public set; private get;` properties — Expression.Property
            // would otherwise hit them with no public accessor and 500 at execution.
            if (pi != null && pi.GetIndexParameters().Length == 0 && pi.GetGetMethod() != null && !IsIgnored(pi)) return pi;
            var available = string.Join(", ",
                GetPropertiesIncludingInterfaces(t)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod() != null && !IsIgnored(p))
                    .Select(p => p.Name)
                    .Distinct());
            throw new OdataQueryException(
                $"Property '{name}' not found on type '{t.Name}'. Available: {available}.");
        }

        /// <summary>
        /// Like <see cref="Type.GetProperty(string, BindingFlags)"/> but also walks base
        /// interfaces when <paramref name="type"/> is itself an interface. Plain
        /// <c>GetProperty</c> returns <see langword="null"/> for an inherited interface
        /// property because the BCL does not flatten interface hierarchies for reflection.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
            Justification = "Base interfaces of an OData root type are reachable through the same DAM annotation that preserves the root type's public properties — host registration of T propagates to T's interface metadata.")]
        public static PropertyInfo? GetPropertyIncludingInterfaces(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] Type type,
            string name)
        {
            var pi = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null) return pi;
            if (type.IsInterface)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    pi = iface.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null) return pi;
                }
            }
            return null;
        }

        /// <summary>
        /// Like <see cref="Type.GetProperties(BindingFlags)"/> but flattens inherited interface
        /// properties when <paramref name="type"/> is itself an interface. May yield duplicates
        /// (e.g. when two parent interfaces redeclare a property) — callers that need a unique
        /// name list should compose with <c>.Distinct()</c>.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2070:UnrecognizedReflectionPattern",
            Justification = "Same as GetPropertyIncludingInterfaces — base interfaces' public properties are kept by the root type's DAM annotation.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
            Justification = "iface enumerated from type.GetInterfaces() inherits the root type's PublicProperties guarantee transitively.")]
        public static IEnumerable<PropertyInfo> GetPropertiesIncludingInterfaces(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] Type type)
        {
            var direct = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (!type.IsInterface) return direct;
            var collected = new List<PropertyInfo>(direct);
            foreach (var iface in type.GetInterfaces())
                collected.AddRange(iface.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            return collected;
        }

        /// <summary>
        /// Whether <paramref name="prop"/> is hidden from OData via
        /// <see cref="OdataIgnoreAttribute"/>, <c>Newtonsoft.Json.JsonIgnoreAttribute</c>, or
        /// <c>System.Text.Json.Serialization.JsonIgnoreAttribute</c>. The JSON attributes are
        /// matched by full name so the engine carries no NuGet dependency on either package.
        /// Hidden properties are unreachable from any <c>$</c>-option — <c>$select</c>,
        /// <c>$expand</c>, <c>$filter</c>, and <c>$orderby</c> all reject them, surfacing the
        /// same "not found" diagnostic as a misspelled property to deny attackers a way to
        /// discriminate hidden-but-present from absent.
        /// </summary>
        public static bool IsIgnored(PropertyInfo prop) =>
            IgnoredCache.GetOrAdd(prop, static p =>
            {
                foreach (var attr in p.GetCustomAttributes(inherit: true))
                {
                    // Cheap concrete-type check first; FullName allocation only on miss.
                    if (attr is OdataIgnoreAttribute) return true;
                    var fullName = attr.GetType().FullName;
                    if (fullName == NewtonsoftJsonIgnoreFullName) return true;
                    if (fullName == SystemTextJsonIgnoreFullName) return true;
                }
                return false;
            });

        // PropertyInfo is reflection-stable per (DeclaringType, Name); equality is structural,
        // so the cache survives even if the BCL returns distinct PropertyInfo instances across
        // calls. Filter/orderby/projection all hit IsIgnored on every property of T, so the
        // cache turns N reflection probes per request into N once-per-process.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<PropertyInfo, bool> IgnoredCache = new();

        private const string NewtonsoftJsonIgnoreFullName = "Newtonsoft.Json.JsonIgnoreAttribute";
        private const string SystemTextJsonIgnoreFullName = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    }
}
