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
            // bool? body (e.g. bare nullable-bool member or lifted comparison) — match the
            // constant's type and force liftToNull=false so null collapses to false per
            // OData v4's null-comparison rule, yielding a Func<T,bool> result type.
            if (body.Type == typeof(bool?))
                body = Expression.Equal(body, Expression.Constant(true, typeof(bool?)), liftToNull: false, method: null);
            else if (body.Type != typeof(bool))
                throw new ArgumentException(
                    $"Filter expression must evaluate to a boolean; got {body.Type.Name}.", nameof(parsed));
            return new BuiltFilter(body, ctx.SlotTypes);
        }

        // Internal helpers reuse the same reflection / Expression-building primitives the
        // public Build<T> already declared. Apply the same Requires* attributes to the class
        // so the analyzer treats every method body as part of the same contract chain.
        [RequiresUnreferencedCode("Resolves entity properties by name via reflection.")]
        [RequiresDynamicCode("Constructs LINQ Expression trees and instantiates generic methods.")]
        private sealed class BuildContext(Type entityType, ParameterExpression entity, ParameterExpression args, IReadOnlyList<LiteralValue> literals)
        {
            private readonly Type _entityType = entityType;
            private readonly ParameterExpression _entity = entity;
            private readonly ParameterExpression _args = args;
            private readonly IReadOnlyList<LiteralValue> _literals = literals;
            private readonly Stack<(string Name, ParameterExpression Param)> _lambdaScopes = new();

            public Type[] SlotTypes { get; } = new Type[literals.Count];

            public Expression Build(FilterNode node, Type expectedType) => node switch
            {
                BinaryNode b => BuildBinary(b),
                UnaryNode u => Expression.Not(Build(u.Operand, typeof(bool))),
                FunctionNode f => BuildFunction(f),
                MemberNode m => BuildMember(m.Path),
                ParamRefNode p => RecordAndAccess(p.Index, expectedType),
                LambdaCollectionNode lc => BuildLambdaCollection(lc),
                _ => throw new NotSupportedException($"Unsupported filter node: {node.GetType().Name}")
            };

            private BinaryExpression BuildBinary(BinaryNode node)
            {
                if (node.Op == BinaryOp.And) return Expression.AndAlso(Build(node.Left, typeof(bool)), Build(node.Right, typeof(bool)));
                if (node.Op == BinaryOp.Or) return Expression.OrElse(Build(node.Left, typeof(bool)), Build(node.Right, typeof(bool)));

                // Pre-build MemberNode operands so the BuildMember reflection walk runs once
                // per side. Other node kinds resolve their slot type without expression work.
                var preLeft = node.Left is MemberNode lm ? BuildMember(lm.Path) : null;
                var preRight = node.Right is MemberNode rm ? BuildMember(rm.Path) : null;

                var leftSlot = preLeft != null
                    ? TypeCoercion.SlotTypeFor(preLeft.Type)
                    : TryResolveOperandSlotType(node.Left);
                var rightSlot = preRight != null
                    ? TypeCoercion.SlotTypeFor(preRight.Type)
                    : TryResolveOperandSlotType(node.Right);
                var slot = leftSlot ?? rightSlot ?? typeof(object);

                var left = TypeCoercion.LiftToSlotType(preLeft ?? Build(node.Left, slot), slot);
                var right = TypeCoercion.LiftToSlotType(preRight ?? Build(node.Right, slot), slot);
                return EmitCompare(node.Op, left, right);
            }

            private static Type TryResolveOperandSlotType(FilterNode operand) => operand switch
            {
                FunctionNode f => TypeCoercion.SlotTypeFor(FunctionReturnType(f.Name)),
                // Bool-returning nodes — without these, `(A eq B) eq true` and
                // `Items/any() eq false` fall through to typeof(object), box the bool, and
                // do reference equality instead of value equality.
                BinaryNode or UnaryNode or LambdaCollectionNode => typeof(bool?),
                // Literal-vs-literal (e.g. dynamic-builder `1 eq 1 and …`) — without these,
                // both sides slot to typeof(object) and EmitCompare does reference equality
                // on boxed primitives, returning false for identical values in-memory.
                ParamRefNode { Kind: LiteralKind.Number } => typeof(decimal?),
                ParamRefNode { Kind: LiteralKind.Boolean } => typeof(bool?),
                ParamRefNode { Kind: LiteralKind.DateTime } => typeof(DateTimeOffset?),
                ParamRefNode { Kind: LiteralKind.String } => typeof(string),
                _ => null
            };

            private static BinaryExpression EmitCompare(BinaryOp op, Expression l, Expression r) => op switch
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

                // Walk the lambda-scope stack innermost-first so nested any/all can shadow and
                // reference outer scopes — e.g. Orders/any(o: o/Items/any(i: i/Price gt o/Min)).
                Expression head = _entity;
                int start = 0;
                foreach (var (name, param) in _lambdaScopes)
                {
                    if (path[0] == name)
                    {
                        head = param;
                        start = 1;
                        break;
                    }
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
                        cursor = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [elem], cursor);
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

            private Expression BuildFunction(FunctionNode node)
            {
                return node.Name switch
                {
                    FunctionName.Contains => StringBoolMethodCall(node, nameof(string.Contains)),
                    FunctionName.StartsWith => StringBoolMethodCall(node, nameof(string.StartsWith)),
                    FunctionName.EndsWith => StringBoolMethodCall(node, nameof(string.EndsWith)),
                    FunctionName.ToLower => StringInstance(node, nameof(string.ToLower)),
                    FunctionName.ToUpper => StringInstance(node, nameof(string.ToUpper)),
                    FunctionName.Trim => StringInstance(node, nameof(string.Trim)),
                    FunctionName.Length => StringLengthProperty(node),
                    FunctionName.IndexOf => StringIndexOfCall(node),
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

            // String instance method on a null member would NRE during JIT in-memory execution;
            // EF Core SQL handles null gracefully, but the OData spec also says functions
            // return null when any arg is null. We collapse to the type-appropriate "no match"
            // sentinel: false for bool methods, null for string/int methods (lifted to nullable).
            private static ConditionalExpression GuardStringNull(Expression instance, Expression nonNullResult, Expression nullResult) =>
                Expression.Condition(
                    Expression.Equal(instance, Expression.Constant(null, typeof(string))),
                    nullResult,
                    nonNullResult);

            private ConditionalExpression StringBoolMethodCall(FunctionNode node, string method)
            {
                ExpectArgs(node, 2);
                var instance = Build(node.Args[0], typeof(string));
                var arg = Build(node.Args[1], typeof(string));
                var mi = typeof(string).GetMethod(method, [typeof(string)])
                    ?? throw new InvalidOperationException($"string.{method}(string) not found.");
                return GuardStringNull(instance, Expression.Call(instance, mi, arg), Expression.Constant(false));
            }

            private ConditionalExpression StringIndexOfCall(FunctionNode node)
            {
                ExpectArgs(node, 2);
                var instance = Build(node.Args[0], typeof(string));
                var arg = Build(node.Args[1], typeof(string));
                var mi = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])
                    ?? throw new InvalidOperationException("string.IndexOf(string) not found.");
                // IndexOf returns int — lift to int? so the null-guard's true/false branches match
                // and so callers comparing the result use the nullable slot type.
                var call = Expression.Convert(Expression.Call(instance, mi, arg), typeof(int?));
                return GuardStringNull(instance, call, Expression.Constant(null, typeof(int?)));
            }

            private ConditionalExpression StringInstance(FunctionNode node, string method)
            {
                ExpectArgs(node, 1);
                var instance = Build(node.Args[0], typeof(string));
                var mi = typeof(string).GetMethod(method, Type.EmptyTypes)
                    ?? throw new InvalidOperationException($"string.{method}() not found.");
                return GuardStringNull(instance, Expression.Call(instance, mi), Expression.Constant(null, typeof(string)));
            }

            private ConditionalExpression StringLengthProperty(FunctionNode node)
            {
                ExpectArgs(node, 1);
                var instance = Build(node.Args[0], typeof(string));
                var len = Expression.Convert(Expression.Property(instance, nameof(string.Length)), typeof(int?));
                return GuardStringNull(instance, len, Expression.Constant(null, typeof(int?)));
            }

            private ConditionalExpression SubstringCall(FunctionNode node)
            {
                if (node.Args.Count is not (2 or 3))
                    throw new ArgumentException($"substring expects 2 or 3 args; got {node.Args.Count}.");
                var instance = Build(node.Args[0], typeof(string));
                // Numeric args go through SlotTypeFor (int?) so a ParamRef slot stays nullable
                // in line with every other arg site; unwrap back to int for the BCL signature.
                var startRaw = Build(node.Args[1], TypeCoercion.SlotTypeFor(typeof(int)));
                var startVal = UnwrapNullableInt(startRaw);
                Expression nullGuard = Expression.Equal(instance, Expression.Constant(null, typeof(string)));
                if (startRaw.Type == typeof(int?))
                    nullGuard = Expression.OrElse(nullGuard, Expression.Equal(startRaw, Expression.Constant(null, typeof(int?))));
                Expression call;
                if (node.Args.Count == 2)
                    call = Expression.Call(instance, typeof(string).GetMethod(nameof(string.Substring), [typeof(int)]), startVal);
                else
                {
                    var lenRaw = Build(node.Args[2], TypeCoercion.SlotTypeFor(typeof(int)));
                    var lenVal = UnwrapNullableInt(lenRaw);
                    if (lenRaw.Type == typeof(int?))
                        nullGuard = Expression.OrElse(nullGuard, Expression.Equal(lenRaw, Expression.Constant(null, typeof(int?))));
                    call = Expression.Call(instance, typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)]), startVal, lenVal);
                }
                return Expression.Condition(nullGuard, Expression.Constant(null, typeof(string)), call);
            }

            private static Expression UnwrapNullableInt(Expression expr) =>
                expr.Type == typeof(int?) ? Expression.Property(expr, nameof(Nullable<>.Value)) : expr;

            private ConditionalExpression ConcatCall(FunctionNode node)
            {
                ExpectArgs(node, 2);
                var a = Build(node.Args[0], typeof(string));
                var b = Build(node.Args[1], typeof(string));
                var mi = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)]);
                var concat = Expression.Call(mi, a, b);
                var anyNull = Expression.OrElse(
                    Expression.Equal(a, Expression.Constant(null, typeof(string))),
                    Expression.Equal(b, Expression.Constant(null, typeof(string))));
                return Expression.Condition(anyNull, Expression.Constant(null, typeof(string)), concat);
            }

            private Expression DateProperty(FunctionNode node, string property)
            {
                ExpectArgs(node, 1);
                var operand = Build(node.Args[0], typeof(DateTime));
                var underlying = Nullable.GetUnderlyingType(operand.Type);
                var effective = underlying ?? operand.Type;
                // DateOnly maps to Edm.Date per OData v4; only Year/Month/Day are valid on it
                // (Hour/Minute/Second have no source property and throw via reflection lookup).
                if (effective != typeof(DateTime) && effective != typeof(DateTimeOffset) && effective != typeof(DateOnly))
                    throw new ArgumentException($"Date function expects DateTime/DateTimeOffset/DateOnly; got {operand.Type.Name}.");

                if (underlying != null)
                    return IfNullableHasValue(operand, value => Expression.Property(value, property), typeof(int?));
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

                var mi = typeof(Math).GetMethod(method, [mathArg])
                    ?? throw new InvalidOperationException($"Math.{method}({mathArg.Name}) not found.");

                if (underlying != null)
                {
                    var nullableMath = typeof(Nullable<>).MakeGenericType(mathArg);
                    return IfNullableHasValue(operand, value =>
                    {
                        var arg = value.Type == mathArg ? value : Expression.Convert(value, mathArg);
                        return Expression.Call(mi, arg);
                    }, nullableMath);
                }

                Expression nonNullArg = operand.Type == mathArg ? operand : Expression.Convert(operand, mathArg);
                return Expression.Call(mi, nonNullArg);
            }

            // Builds `nullable.HasValue ? (TResult)body(nullable.Value) : null`. Shared by
            // date-property and math-call null propagation.
            private static ConditionalExpression IfNullableHasValue(Expression nullableOperand, Func<Expression, Expression> bodyFromValue, Type nullableResultType)
            {
                var hasValue = Expression.Property(nullableOperand, nameof(Nullable<>.HasValue));
                var value = Expression.Property(nullableOperand, nameof(Nullable<>.Value));
                var inner = bodyFromValue(value);
                var lifted = inner.Type == nullableResultType ? inner : Expression.Convert(inner, nullableResultType);
                return Expression.Condition(hasValue, lifted, Expression.Constant(null, nullableResultType));
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

            private MethodCallExpression BuildLambdaCollection(LambdaCollectionNode node)
            {
                var collection = BuildMember(node.CollectionPath);
                var elem = GetEnumerableElementType(collection.Type)
                    ?? throw new ArgumentException($"any/all target is not enumerable: {collection.Type.Name}");

                if (node.Body == null)
                {
                    // Items/any() — terminal collection-non-empty check.
                    return Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [elem], collection);
                }

                var lambdaParam = Expression.Parameter(elem, node.Param);
                _lambdaScopes.Push((node.Param, lambdaParam));
                try
                {
                    var body = Build(node.Body, typeof(bool));
                    var lambda = Expression.Lambda(body, lambdaParam);
                    var method = node.Op == LambdaOp.Any ? nameof(Enumerable.Any) : nameof(Enumerable.All);
                    return Expression.Call(typeof(Enumerable), method, [elem], collection, lambda);
                }
                finally
                {
                    _lambdaScopes.Pop();
                }
            }
        }
    }
}
