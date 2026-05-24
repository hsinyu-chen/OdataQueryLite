using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    public sealed record BuiltFilter(Expression Body, Type[] SlotTypes);

    public static class FilterExpressionBuilder
    {
        // Uses reflection on T's properties to build Expression nodes by name, and Expression.Call
        // with generic Enumerable / string methods — both flag IL2026 / IL3050 under trim+AOT.
        // Caller-side annotation surfaces this to the user of OdataQueryLite at their entry point.
        [RequiresUnreferencedCode("FilterExpressionBuilder resolves entity properties by name via reflection. T's public properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("FilterExpressionBuilder constructs LINQ Expression trees and instantiates generic methods (Enumerable.Any/All/Count, string instance methods).")]
        public static BuiltFilter Build<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            FilterParseResult parsed,
            ParameterExpression entityParam,
            ParameterExpression argsParam)
        {
            ArgumentNullException.ThrowIfNull(parsed);
            ArgumentNullException.ThrowIfNull(entityParam);
            ArgumentNullException.ThrowIfNull(argsParam);
            if (entityParam.Type != typeof(T))
                throw new ArgumentException($"entityParam type {entityParam.Type} does not match T {typeof(T)}.", nameof(entityParam));
            if (argsParam.Type != typeof(object[]))
                throw new ArgumentException($"argsParam type {argsParam.Type} must be object[].", nameof(argsParam));

            var ctx = new BuildContext(typeof(T), entityParam, argsParam, parsed.Literals);
            var body = ctx.Build(parsed.Ast, expectedType: typeof(bool));
            if (body.Type != typeof(bool))
                body = Expression.Equal(body, Expression.Constant(true));
            return new BuiltFilter(body, ctx.SlotTypes);
        }

        // Internal helpers reuse the same reflection / Expression-building primitives the
        // public Build<T> already declared. Apply the same Requires* attributes to the class
        // so the analyzer treats every method body as part of the same contract chain.
        [RequiresUnreferencedCode("Resolves entity properties by name via reflection.")]
        [RequiresDynamicCode("Constructs LINQ Expression trees and instantiates generic methods.")]
        private sealed class BuildContext
        {
            private readonly Type _entityType;
            private readonly ParameterExpression _entity;
            private readonly ParameterExpression _args;
            private readonly IReadOnlyList<LiteralValue> _literals;
            private readonly Stack<(string Name, ParameterExpression Param)> _lambdaScopes = new();

            public Type[] SlotTypes { get; }

            public BuildContext(Type entityType, ParameterExpression entity, ParameterExpression args, IReadOnlyList<LiteralValue> literals)
            {
                _entityType = entityType;
                _entity = entity;
                _args = args;
                _literals = literals;
                SlotTypes = new Type[literals.Count];
            }

            public Expression Build(FilterNode node, Type expectedType) => node switch
            {
                BinaryNode b => BuildBinary(b),
                UnaryNode u => Expression.Not(Build(u.Operand, typeof(bool))),
                FunctionNode f => BuildFunction(f),
                MemberNode m => BuildMember(m.Path),
                ParamRefNode p => RecordAndAccess(p.Index, expectedType ?? typeof(object)),
                LambdaCollectionNode lc => BuildLambdaCollection(lc),
                LiteralNode l => Expression.Constant(l.Value, l.Value?.GetType() ?? typeof(object)),
                _ => throw new NotSupportedException($"Unsupported filter node: {node.GetType().Name}")
            };

            private Expression BuildBinary(BinaryNode node)
            {
                if (node.Op == BinaryOp.And) return Expression.AndAlso(Build(node.Left, typeof(bool)), Build(node.Right, typeof(bool)));
                if (node.Op == BinaryOp.Or) return Expression.OrElse(Build(node.Left, typeof(bool)), Build(node.Right, typeof(bool)));

                // Comparison: pick slot type from the non-ParamRef side first.
                var leftSlot = TryResolveOperandSlotType(node.Left);
                var rightSlot = TryResolveOperandSlotType(node.Right);
                var slot = leftSlot ?? rightSlot ?? typeof(object);

                var left = TypeCoercion.LiftToSlotType(Build(node.Left, slot), slot);
                var right = TypeCoercion.LiftToSlotType(Build(node.Right, slot), slot);
                return EmitCompare(node.Op, left, right);
            }

            private Type TryResolveOperandSlotType(FilterNode operand) => operand switch
            {
                MemberNode m => TypeCoercion.SlotTypeFor(BuildMember(m.Path).Type),
                FunctionNode f => TypeCoercion.SlotTypeFor(FunctionReturnType(f.Name)),
                _ => null
            };

            private static Expression EmitCompare(BinaryOp op, Expression l, Expression r) => op switch
            {
                BinaryOp.Eq => Expression.Equal(l, r),
                BinaryOp.Ne => Expression.NotEqual(l, r),
                BinaryOp.Gt => Expression.GreaterThan(l, r),
                BinaryOp.Ge => Expression.GreaterThanOrEqual(l, r),
                BinaryOp.Lt => Expression.LessThan(l, r),
                BinaryOp.Le => Expression.LessThanOrEqual(l, r),
                _ => throw new InvalidOperationException($"EmitCompare called with non-comparison op {op}")
            };

            private Expression RecordAndAccess(int idx, Type slotType)
            {
                if (idx < 0 || idx >= SlotTypes.Length)
                    throw new InvalidOperationException($"ParamRef index {idx} out of range (literals count = {SlotTypes.Length}).");
                if (SlotTypes[idx] == null) SlotTypes[idx] = slotType;
                else if (SlotTypes[idx] != slotType)
                    throw new InvalidOperationException(
                        $"ParamRef #{idx} used with conflicting slot types: {SlotTypes[idx]} vs {slotType}.");
                return TypeCoercion.LiteralAccess(_args, idx, slotType);
            }

            private Expression BuildMember(IReadOnlyList<string> path)
            {
                if (path.Count == 0) throw new ArgumentException("Member path is empty.");

                // Detect lambda-scope variable (any/all bound parameter).
                Expression head;
                int start;
                if (_lambdaScopes.Count > 0 && path[0] == _lambdaScopes.Peek().Name)
                {
                    head = _lambdaScopes.Peek().Param;
                    start = 1;
                }
                else
                {
                    head = _entity;
                    start = 0;
                }

                var cursor = head;
                for (int i = start; i < path.Count; i++)
                {
                    var seg = path[i];
                    if (seg == "$count")
                    {
                        if (i != path.Count - 1)
                            throw new ArgumentException($"$count must be the terminal segment; saw '{string.Join('/', path)}'.");
                        var elem = GetEnumerableElementType(cursor.Type)
                            ?? throw new ArgumentException($"$count target is not enumerable: {cursor.Type.Name}");
                        cursor = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), new[] { elem }, cursor);
                        return cursor;
                    }
                    var prop = ResolveProperty(cursor.Type, seg);
                    cursor = Expression.Property(cursor, prop);
                }
                return cursor;
            }

            private static PropertyInfo ResolveProperty(
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
                string name)
            {
                var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null) return pi;
                var available = string.Join(", ", t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
                throw new ArgumentException(
                    $"Property '{name}' not found on type '{t.Name}'. Available: {available}.");
            }

            // Walks the BaseType chain to catch custom collections (`class MyOrders : List<Order>`)
            // — `t.IsGenericType` is false for those, but List<Order> is in the inheritance chain.
            // Stays AOT-clean by avoiding GetInterfaces().
            private static Type GetEnumerableElementType(Type t)
            {
                if (t.IsArray) return t.GetElementType();
                for (var c = t; c != null; c = c.BaseType)
                {
                    if (!c.IsGenericType) continue;
                    var def = c.GetGenericTypeDefinition();
                    if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>)
                        || def == typeof(IList<>) || def == typeof(IReadOnlyCollection<>)
                        || def == typeof(IReadOnlyList<>) || def == typeof(List<>)
                        || def == typeof(HashSet<>))
                    {
                        return c.GetGenericArguments()[0];
                    }
                }
                return null;
            }

            private Expression BuildFunction(FunctionNode node)
            {
                return node.Name switch
                {
                    FunctionName.Contains => StringMethodCall(node, nameof(string.Contains), typeof(string)),
                    FunctionName.StartsWith => StringMethodCall(node, nameof(string.StartsWith), typeof(string)),
                    FunctionName.EndsWith => StringMethodCall(node, nameof(string.EndsWith), typeof(string)),
                    FunctionName.ToLower => StringInstance(node, nameof(string.ToLower)),
                    FunctionName.ToUpper => StringInstance(node, nameof(string.ToUpper)),
                    FunctionName.Trim => StringInstance(node, nameof(string.Trim)),
                    FunctionName.Length => StringLengthProperty(node),
                    FunctionName.IndexOf => StringMethodCall(node, nameof(string.IndexOf), typeof(string)),
                    FunctionName.Substring => SubstringCall(node),
                    FunctionName.Concat => ConcatCall(node),
                    FunctionName.Year => DateProperty(node, nameof(DateTime.Year)),
                    FunctionName.Month => DateProperty(node, nameof(DateTime.Month)),
                    FunctionName.Day => DateProperty(node, nameof(DateTime.Day)),
                    FunctionName.Hour => DateProperty(node, nameof(DateTime.Hour)),
                    FunctionName.Minute => DateProperty(node, nameof(DateTime.Minute)),
                    FunctionName.Second => DateProperty(node, nameof(DateTime.Second)),
                    FunctionName.Round => MathCall(node, nameof(Math.Round)),
                    FunctionName.Floor => MathCall(node, nameof(Math.Floor)),
                    FunctionName.Ceiling => MathCall(node, nameof(Math.Ceiling)),
                    _ => throw new NotSupportedException($"Function {node.Name} not implemented.")
                };
            }

            private Expression StringMethodCall(FunctionNode node, string method, Type argType)
            {
                ExpectArgs(node, 2);
                var instance = Build(node.Args[0], typeof(string));
                var arg = Build(node.Args[1], argType);
                var mi = typeof(string).GetMethod(method, new[] { argType })
                    ?? throw new InvalidOperationException($"string.{method}({argType.Name}) not found.");
                return Expression.Call(instance, mi, arg);
            }

            private Expression StringInstance(FunctionNode node, string method)
            {
                ExpectArgs(node, 1);
                var instance = Build(node.Args[0], typeof(string));
                var mi = typeof(string).GetMethod(method, Type.EmptyTypes)
                    ?? throw new InvalidOperationException($"string.{method}() not found.");
                return Expression.Call(instance, mi);
            }

            private Expression StringLengthProperty(FunctionNode node)
            {
                ExpectArgs(node, 1);
                var instance = Build(node.Args[0], typeof(string));
                return Expression.Property(instance, nameof(string.Length));
            }

            private Expression SubstringCall(FunctionNode node)
            {
                if (node.Args.Count is not (2 or 3))
                    throw new ArgumentException($"substring expects 2 or 3 args; got {node.Args.Count}.");
                var instance = Build(node.Args[0], typeof(string));
                var start = Build(node.Args[1], typeof(int));
                if (node.Args.Count == 2)
                    return Expression.Call(instance, typeof(string).GetMethod(nameof(string.Substring), new[] { typeof(int) }), start);
                var len = Build(node.Args[2], typeof(int));
                return Expression.Call(instance, typeof(string).GetMethod(nameof(string.Substring), new[] { typeof(int), typeof(int) }), start, len);
            }

            private Expression ConcatCall(FunctionNode node)
            {
                ExpectArgs(node, 2);
                var a = Build(node.Args[0], typeof(string));
                var b = Build(node.Args[1], typeof(string));
                var mi = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) });
                return Expression.Call(mi, a, b);
            }

            private Expression DateProperty(FunctionNode node, string property)
            {
                ExpectArgs(node, 1);
                var operand = Build(node.Args[0], typeof(DateTime));
                var underlying = Nullable.GetUnderlyingType(operand.Type);
                var effective = underlying ?? operand.Type;
                if (effective != typeof(DateTime) && effective != typeof(DateTimeOffset))
                    throw new ArgumentException($"Date function expects DateTime/DateTimeOffset; got {operand.Type.Name}.");

                // For nullable operands: `dt.HasValue ? (int?)dt.Value.Year : null` so null
                // propagates per OData spec rather than throwing at row evaluation.
                if (underlying != null)
                {
                    var hasValue = Expression.Property(operand, nameof(Nullable<int>.HasValue));
                    var value = Expression.Property(operand, nameof(Nullable<int>.Value));
                    var prop = Expression.Property(value, property);
                    var lifted = Expression.Convert(prop, typeof(int?));
                    return Expression.Condition(hasValue, lifted, Expression.Constant(null, typeof(int?)));
                }
                return Expression.Property(operand, property);
            }

            private Expression MathCall(FunctionNode node, string method)
            {
                ExpectArgs(node, 1);
                var operand = Build(node.Args[0], typeof(double));
                var underlying = Nullable.GetUnderlyingType(operand.Type);
                var effective = underlying ?? operand.Type;

                // Math has separate decimal / double overloads — dispatch on member type to
                // avoid lossy double conversion for decimal columns (money fields, etc).
                Type mathArg = effective == typeof(decimal) ? typeof(decimal)
                    : effective == typeof(double) || effective == typeof(float) ? typeof(double)
                    : throw new ArgumentException($"Math.{method} expects decimal / double / float; got {effective.Name}.");

                var mi = typeof(Math).GetMethod(method, new[] { mathArg })
                    ?? throw new InvalidOperationException($"Math.{method}({mathArg.Name}) not found.");

                if (underlying != null)
                {
                    var hasValue = Expression.Property(operand, nameof(Nullable<int>.HasValue));
                    var value = Expression.Property(operand, nameof(Nullable<int>.Value));
                    Expression arg = value.Type == mathArg ? value : Expression.Convert(value, mathArg);
                    var call = Expression.Call(mi, arg);
                    var nullableMath = typeof(Nullable<>).MakeGenericType(mathArg);
                    return Expression.Condition(
                        hasValue,
                        Expression.Convert(call, nullableMath),
                        Expression.Constant(null, nullableMath));
                }

                Expression nonNullArg = operand.Type == mathArg ? operand : Expression.Convert(operand, mathArg);
                return Expression.Call(mi, nonNullArg);
            }

            private static void ExpectArgs(FunctionNode node, int count)
            {
                if (node.Args.Count != count)
                    throw new ArgumentException($"{node.Name} expects {count} arg(s); got {node.Args.Count}.");
            }

            private static Type FunctionReturnType(FunctionName fn) => fn switch
            {
                FunctionName.Contains or FunctionName.StartsWith or FunctionName.EndsWith => typeof(bool),
                FunctionName.ToLower or FunctionName.ToUpper or FunctionName.Trim or FunctionName.Substring or FunctionName.Concat => typeof(string),
                FunctionName.Length or FunctionName.IndexOf
                    or FunctionName.Year or FunctionName.Month or FunctionName.Day
                    or FunctionName.Hour or FunctionName.Minute or FunctionName.Second => typeof(int),
                FunctionName.Round or FunctionName.Floor or FunctionName.Ceiling => typeof(double),
                _ => throw new NotSupportedException($"FunctionReturnType: {fn} not mapped.")
            };

            private Expression BuildLambdaCollection(LambdaCollectionNode node)
            {
                var collection = BuildMember(node.CollectionPath);
                var elem = GetEnumerableElementType(collection.Type)
                    ?? throw new ArgumentException($"any/all target is not enumerable: {collection.Type.Name}");

                if (node.Body == null)
                {
                    // Items/any() — terminal collection-non-empty check.
                    return Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), new[] { elem }, collection);
                }

                var lambdaParam = Expression.Parameter(elem, node.Param);
                _lambdaScopes.Push((node.Param, lambdaParam));
                try
                {
                    var body = Build(node.Body, typeof(bool));
                    var lambda = Expression.Lambda(body, lambdaParam);
                    var method = node.Op == LambdaOp.Any ? nameof(Enumerable.Any) : nameof(Enumerable.All);
                    return Expression.Call(typeof(Enumerable), method, new[] { elem }, collection, lambda);
                }
                finally
                {
                    _lambdaScopes.Pop();
                }
            }
        }
    }
}
