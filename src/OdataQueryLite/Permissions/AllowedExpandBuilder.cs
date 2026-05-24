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
            AddSinglePath(_root, path);
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
            var collectionNode = NavigateOrCreate(_root, path);

            var childBuilder = new AllowedExpandBuilder<TChild>();
            configureChild(childBuilder);
            MergeInto(collectionNode, childBuilder._root);
            return this;
        }

        public AllowedExpandNode Build() => _root;

        private static void AddSinglePath(AllowedExpandNode start, IReadOnlyList<PropertyInfo> path)
        {
            var cursor = start;
            for (int i = 0; i < path.Count - 1; i++)
            {
                cursor = GetOrAddChild(cursor, path[i].Name);
            }

            var leaf = path[^1];
            if (IsNavigation(leaf.PropertyType))
            {
                GetOrAddChild(cursor, leaf.Name);
            }
            else
            {
                cursor.AllowedSelectFields ??= [];
                cursor.AllowedSelectFields.Add(leaf.Name);
            }
        }

        private static AllowedExpandNode NavigateOrCreate(AllowedExpandNode start, IReadOnlyList<PropertyInfo> path)
        {
            var cursor = start;
            foreach (var pi in path)
            {
                cursor = GetOrAddChild(cursor, pi.Name);
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

        private static void MergeInto(AllowedExpandNode dest, AllowedExpandNode src)
        {
            foreach (var (name, srcChild) in src.ExpandableProperties)
            {
                var destChild = GetOrAddChild(dest, name);
                MergeInto(destChild, srcChild);
            }
            if (src.AllowedSelectFields != null)
            {
                dest.AllowedSelectFields ??= [];
                foreach (var f in src.AllowedSelectFields) dest.AllowedSelectFields.Add(f);
            }
        }

        // string / value type / Nullable<T> = scalar; everything else = navigation
        private static bool IsNavigation(Type t) =>
            t != typeof(string)
            && !t.IsValueType
            && Nullable.GetUnderlyingType(t) == null;

        private static void EnsureNonEmpty(IReadOnlyList<PropertyInfo> path)
        {
            if (path.Count == 0)
                throw new ArgumentException("Selector did not yield any property access path.");
        }
    }
}
