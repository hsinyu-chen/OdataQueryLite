using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using OdataQueryLite.Ast;
using OdataQueryLite.ExpressionBuilding;

namespace OdataQueryLite.Caching
{
    public static class CompiledQueryFactory
    {
        [RequiresUnreferencedCode("Builds an Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Calls IQueryable.Where with a generic Expression<Func<T,bool>>; provider implementations may need dynamic code under AOT.")]
        public static ICompiledQuery<T> Build<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            FilterParseResult parsed)
        {
            ArgumentNullException.ThrowIfNull(parsed);

            var entityParam = Expression.Parameter(typeof(T), "x");
            var argsParam = Expression.Parameter(typeof(object[]), "args");
            var built = FilterExpressionBuilder.Build<T>(parsed, entityParam, argsParam);

            // Store the body Expression + parameters as data. Per-request Apply swaps the
            // args ParameterExpression for a ConstantExpression holding that request's args
            // array, then hands the resulting Expression<Func<T,bool>> to Queryable.Where.
            // No outer meta delegate, no Expression.Compile, no Quote-based closure capture
            // — every layer of the runtime sees a self-contained Expression with no
            // cross-lambda parameter references.
            return new CompiledQuery<T>(entityParam, argsParam, built.Body, built.SlotTypes);
        }

        private sealed class CompiledQuery<T> : ICompiledQuery<T>
        {
            private readonly ParameterExpression _entityParam;
            private readonly ParameterExpression _argsParam;
            private readonly Expression _body;
            private readonly Type[] _slotTypes;

            public CompiledQuery(ParameterExpression entityParam, ParameterExpression argsParam, Expression body, Type[] slotTypes)
            {
                _entityParam = entityParam;
                _argsParam = argsParam;
                _body = body;
                _slotTypes = slotTypes;
            }

            public IQueryable<T> Apply(IQueryable<T> source, IReadOnlyList<LiteralValue> literals)
            {
                if (literals.Count != _slotTypes.Length)
                    throw new ArgumentException(
                        $"Literal count {literals.Count} does not match cached shape's slot count {_slotTypes.Length}.");

                var args = new object[_slotTypes.Length];
                for (int i = 0; i < _slotTypes.Length; i++)
                    args[i] = TypeCoercion.Coerce(literals[i].Value, literals[i].Kind, _slotTypes[i]);

                var bound = new ArgsSubstitutor(_argsParam, args).Visit(_body);
                var lambda = Expression.Lambda<Func<T, bool>>(bound, _entityParam);
                return source.Where(lambda);
            }
        }

        private sealed class ArgsSubstitutor : ExpressionVisitor
        {
            private readonly ParameterExpression _argsParam;
            private readonly ConstantExpression _argsConstant;

            public ArgsSubstitutor(ParameterExpression argsParam, object[] args)
            {
                _argsParam = argsParam;
                _argsConstant = Expression.Constant(args, typeof(object[]));
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _argsParam ? _argsConstant : base.VisitParameter(node);
        }
    }
}
