using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using OdataQueryLite.Ast;
using OdataQueryLite.ExpressionBuilding;

namespace OdataQueryLite.Caching
{
    public static class CompiledQueryFactory
    {
        [RequiresUnreferencedCode("Builds an Expression tree that accesses T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Compiles an Expression tree to a delegate via Expression.Compile() / preferInterpretation under AOT.")]
        public static ICompiledQuery<T> Build<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            FilterParseResult parsed)
        {
            ArgumentNullException.ThrowIfNull(parsed);

            var entityParam = Expression.Parameter(typeof(T), "x");
            var argsParam = Expression.Parameter(typeof(object[]), "args");
            var built = FilterExpressionBuilder.Build<T>(parsed, entityParam, argsParam);

            var innerLambda = Expression.Lambda<Func<T, bool>>(built.Body, entityParam);
            var queryableParam = Expression.Parameter(typeof(IQueryable<T>), "source");
            var whereCall = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                new[] { typeof(T) },
                queryableParam,
                Expression.Quote(innerLambda));

            var meta = Expression.Lambda<Func<IQueryable<T>, object[], IQueryable<T>>>(
                whereCall, queryableParam, argsParam);

            // AOT runtimes have no JIT — fall back to interpretation so Compile() doesn't throw.
            var compiled = RuntimeFeature.IsDynamicCodeSupported
                ? meta.Compile()
                : meta.Compile(preferInterpretation: true);

            return new CompiledQuery<T>(compiled, built.SlotTypes);
        }

        private sealed class CompiledQuery<T> : ICompiledQuery<T>
        {
            private readonly Func<IQueryable<T>, object[], IQueryable<T>> _apply;
            private readonly Type[] _slotTypes;

            public CompiledQuery(Func<IQueryable<T>, object[], IQueryable<T>> apply, Type[] slotTypes)
            {
                _apply = apply;
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
                return _apply(source, args);
            }
        }
    }
}
