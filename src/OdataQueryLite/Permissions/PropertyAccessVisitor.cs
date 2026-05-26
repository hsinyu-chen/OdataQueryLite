using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataQueryLite.Permissions
{
    /// <summary>Extracts an ordered <see cref="PropertyInfo"/> chain from a property-access lambda body.</summary>
    public static class PropertyAccessVisitor
    {
        /// <summary>
        /// Walks the body of <paramref name="lambda"/> as a property-access chain rooted in the lambda
        /// parameter (<c>x =&gt; x.A.B.C</c>) and returns the chain root-first (<c>[A, B, C]</c>).
        /// </summary>
        /// <param name="lambda">Lambda whose body is a property-access chain.</param>
        /// <returns>The chain ordered root → leaf.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lambda"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The body contains a non-property-access node.</exception>
        public static IReadOnlyList<PropertyInfo> ExtractPath(LambdaExpression lambda)
        {
            ArgumentNullException.ThrowIfNull(lambda);

            List<PropertyInfo> path = [];
            Walk(lambda.Body, path);
            path.Reverse();
            return path;
        }

        private static void Walk(Expression? e, List<PropertyInfo> path)
        {
            switch (e)
            {
                case MemberExpression { Member: PropertyInfo pi } m:
                    path.Add(pi);
                    Walk(m.Expression, path);
                    break;

                case ParameterExpression:
                    return;

                case null:
                    throw new ArgumentException(
                        "AllowExpand selector body terminated unexpectedly — expected a property-access chain rooted in the lambda parameter.");

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
