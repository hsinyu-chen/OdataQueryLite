using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using OdataQueryLite.Ast;
using OdataQueryLite.Diagnostics;
using OdataQueryLite.ExpressionBuilding;

namespace OdataQueryLite.Caching
{
    /// <summary>Builds <see cref="ICompiledQuery{T}"/> instances from parsed <c>$filter</c> results.</summary>
    public static class CompiledQueryFactory
    {
        /// <summary>
        /// Compiles <paramref name="parsed"/> into a reusable <see cref="ICompiledQuery{T}"/>. The result captures
        /// the body Expression and the literal-slot types; per-call <see cref="ICompiledQuery{T}.Apply"/> substitutes
        /// the literal array without recompiling.
        /// </summary>
        /// <typeparam name="T">Entity type the filter references.</typeparam>
        /// <param name="parsed">Parsed filter AST and literal slots.</param>
        /// <returns>The compiled query.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parsed"/> is <see langword="null"/>.</exception>
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
            private int _aotWarningEmitted;

            public CompiledQuery(ParameterExpression entityParam, ParameterExpression argsParam, Expression body, Type[] slotTypes)
            {
                _entityParam = entityParam;
                _argsParam = argsParam;
                _body = body;
                _slotTypes = slotTypes;
            }

            public IQueryable<T> Apply(IQueryable<T> source, IReadOnlyList<LiteralValue> literals)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(literals);
                if (literals.Count != _slotTypes.Length)
                    throw new ArgumentException(
                        $"Literal count {literals.Count} does not match cached shape's slot count {_slotTypes.Length}.");

                // BCL EnumerableQuery under AOT walks the Expression tree per row via the interpreter
                // (no JIT codegen). Documented anti-pattern: warn once per CompiledQuery instance so
                // hot paths emit a single trace, not one per Apply call. Volatile read first so the
                // post-emission steady state pays only a load (no Interlocked CAS / memory barrier).
                if (!RuntimeProbe.IsDynamicCodeSupported
                    && source.Provider is EnumerableQuery
                    && Volatile.Read(ref _aotWarningEmitted) == 0
                    && Interlocked.CompareExchange(ref _aotWarningEmitted, 1, 0) == 0)
                {
                    OdataQueryLiteEventSource.Log.AotInMemoryProviderDetected(typeof(T).FullName ?? typeof(T).Name);
                }

                // No literals — _body holds no references to _argsParam, so we can skip the
                // ArgsSubstitutor traversal entirely and reuse _body as the lambda body.
                if (_slotTypes.Length == 0)
                    return source.Where(Expression.Lambda<Func<T, bool>>(_body, _entityParam));

                var args = new object?[_slotTypes.Length];
                for (int i = 0; i < _slotTypes.Length; i++)
                    args[i] = TypeCoercion.Coerce(literals[i].Value, literals[i].Kind, _slotTypes[i]);

                var bound = new ArgsSubstitutor(_argsParam, args).Visit(_body);
                return source.Where(Expression.Lambda<Func<T, bool>>(bound, _entityParam));
            }
        }

        // EF Core inlines Expression.Constant values into the generated SQL, polluting the
        // plan cache. Wrapping in a closure-like instance and substituting argsParam with
        // Expression.Field(Constant(closure), ValuesField) makes EF Core treat the array
        // as a captured value and parameterize it (`@p0`) instead.
        private sealed class ArgsClosure
        {
            public object?[] Values = [];

            // EF Core hashes Expression.Constant by the wrapped instance's Equals/GetHashCode.
            // Default reference equality makes every Apply call produce a structurally-new
            // tree, busting EF's query plan cache. All ArgsClosure instances are
            // interchangeable as far as the tree shape is concerned (the parameterizer reads
            // Values via the FieldInfo at execution time), so equate them by type.
            public override bool Equals(object? obj) => obj is ArgsClosure;
            public override int GetHashCode() => typeof(ArgsClosure).GetHashCode();
        }

        private static readonly FieldInfo _valuesField =
            typeof(ArgsClosure).GetField(nameof(ArgsClosure.Values))!;

        private sealed class ArgsSubstitutor : ExpressionVisitor
        {
            private readonly ParameterExpression _argsParam;
            private readonly MemberExpression _argsAccess;

            public ArgsSubstitutor(ParameterExpression argsParam, object?[] args)
            {
                _argsParam = argsParam;
                var closure = new ArgsClosure { Values = args };
                _argsAccess = Expression.Field(Expression.Constant(closure), _valuesField);
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _argsParam ? _argsAccess : base.VisitParameter(node);
        }
    }
}
