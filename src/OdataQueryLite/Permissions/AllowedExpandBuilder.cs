using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataQueryLite.Permissions
{
    public sealed class AllowedExpandBuilder<TEntity>
    {
        private readonly AllowedExpandNode _root = new();

        public AllowedExpandBuilder<TEntity> AllowExpand<TChild>(Expression<Func<TEntity, TChild>> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            var path = PropertyAccessVisitor.ExtractPath(selector);
            EnsureNonEmpty(path);

            var cursor = Navigate(_root, path, 0, path.Count - 1);
            var leaf = path[^1];
            if (IsNavigation(leaf.PropertyType))
            {
                var leafNode = GetOrAddChild(cursor, leaf.Name);
                leafNode.MarkSelectUnrestricted();
            }
            else
            {
                cursor.AddAllowedSelect(leaf.Name);
            }
            return this;
        }

        public AllowedExpandBuilder<TEntity> AllowExpand<TChild>(
            Expression<Func<TEntity, IEnumerable<TChild>>> collectionSelector,
            Action<AllowedExpandBuilder<TChild>> configureChild)
        {
            ArgumentNullException.ThrowIfNull(collectionSelector);
            ArgumentNullException.ThrowIfNull(configureChild);

            var path = PropertyAccessVisitor.ExtractPath(collectionSelector);
            EnsureNonEmpty(path);
            var collectionNode = Navigate(_root, path, 0, path.Count);

            var childBuilder = new AllowedExpandBuilder<TChild>();
            configureChild(childBuilder);
            collectionNode.MergeFrom(childBuilder._root);
            return this;
        }

        public AllowedExpandNode Build() => _root;

        private static AllowedExpandNode Navigate(AllowedExpandNode start, IReadOnlyList<PropertyInfo> path, int from, int toExclusive)
        {
            var cursor = start;
            for (int i = from; i < toExclusive; i++)
            {
                cursor = GetOrAddChild(cursor, path[i].Name);
            }
            return cursor;
        }

        private static AllowedExpandNode GetOrAddChild(AllowedExpandNode node, string name)
        {
            if (!node.ExpandableProperties.TryGetValue(name, out var child))
            {
                child = new AllowedExpandNode();
                node.ExpandableProperties[name] = child;
            }
            return child;
        }

        // OData structural property (scalar) vs navigation:
        // - string / value type / Nullable<T> are scalar.
        // - Collections / arrays inherit from their element type (List<int> is scalar, ICollection<Customer> is navigation).
        // - Everything else (reference types) is navigation.
        private static bool IsNavigation(Type t)
        {
            var inner = Nullable.GetUnderlyingType(t) ?? t;
            if (inner == typeof(string) || inner.IsValueType) return false;

            var element = GetCollectionElementType(inner);
            return element == null || IsNavigation(element);
        }

        private static Type GetCollectionElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            foreach (var i in t.GetInterfaces())
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return i.GetGenericArguments()[0];
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];
            return null;
        }

        private static void EnsureNonEmpty(IReadOnlyList<PropertyInfo> path)
        {
            if (path.Count == 0)
                throw new ArgumentException("Selector did not yield any property access path.");
        }
    }
}
