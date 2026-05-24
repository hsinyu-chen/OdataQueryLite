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
            Walk(Unwrap(lambda.Body), path);
            path.Reverse();
            return path;
        }

        // Strip Convert (object-cast) and Quote (IQueryable wraps lambdas in Quote) layers.
        private static Expression Unwrap(Expression e)
        {
            while (e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } u)
                e = u.Operand;
            return e;
        }

        private static void Walk(Expression e, List<PropertyInfo> path)
        {
            switch (Unwrap(e))
            {
                case MemberExpression { Member: PropertyInfo pi } m:
                    path.Add(pi);
                    Walk(m.Expression, path);
                    break;

                case ParameterExpression:
                    return;

                // x.Items.Select(i => i.Product) — descend into inner lambda then continue with source
                // IQueryable.Select wraps the lambda in Quote, which Unwrap strips for us.
                case MethodCallExpression { Method.Name: "Select" or "SelectMany", Arguments.Count: 2 } call:
                    var inner = ExtractPath((LambdaExpression)Unwrap(call.Arguments[1]));
                    for (int i = inner.Count - 1; i >= 0; i--) path.Add(inner[i]);
                    Walk(call.Arguments[0], path);
                    break;

                default:
                    throw new ArgumentException(
                        $"PropertyAccessVisitor: unsupported expression node '{e.NodeType}' ({e.GetType().Name}). " +
                        "Supported shapes: MemberExpression chain (x => x.A.B.C), Select/SelectMany on collections.");
            }
        }
    }
}
