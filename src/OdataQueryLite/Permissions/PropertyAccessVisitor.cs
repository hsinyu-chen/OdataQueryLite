using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataQueryLite.Permissions
{
    public static class PropertyAccessVisitor
    {
        public static IReadOnlyList<PropertyInfo> ExtractPath(LambdaExpression lambda)
        {
            ArgumentNullException.ThrowIfNull(lambda);

            List<PropertyInfo> path = [];
            Walk(lambda.Body, path);
            path.Reverse();
            return path;
        }

        private static void Walk(Expression e, List<PropertyInfo> path)
        {
            switch (e)
            {
                case MemberExpression { Member: PropertyInfo pi } m:
                    path.Add(pi);
                    Walk(m.Expression, path);
                    break;

                case ParameterExpression:
                    return;

                default:
                    throw new ArgumentException(
                        $"AllowExpand selector must be a simple property-access chain (x => x.A.B.C). " +
                        $"For collection navigations use the two-argument overload: " +
                        $"AllowExpand(x => x.Items, n => n.AllowExpand(i => i.Field)). " +
                        $"Got unsupported expression node '{e.NodeType}' ({e.GetType().Name}).");
            }
        }
    }
}
