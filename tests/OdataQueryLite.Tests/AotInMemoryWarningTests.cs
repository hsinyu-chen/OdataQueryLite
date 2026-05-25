using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Linq.Expressions;
using OdataQueryLite.Ast;
using OdataQueryLite.Caching;
using OdataQueryLite.Diagnostics;
using OdataQueryLite.Parsing;
using Xunit;

namespace OdataQueryLite.Tests
{
    [Collection(nameof(AotInMemoryWarningCollection))]
    public class AotInMemoryWarningTests
    {
        public sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private static IQueryable<Row> InMemorySource() => new[]
        {
            new Row { Id = 1, Name = "Alice" },
            new Row { Id = 2, Name = "Bob" },
        }.AsQueryable();

        private sealed class CapturingListener : EventListener
        {
            public readonly List<EventWrittenEventArgs> Events = new();
            private EventSource _attached;

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name == "OdataQueryLite")
                {
                    _attached = eventSource;
                    EnableEvents(eventSource, EventLevel.LogAlways);
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.EventSource.Name == "OdataQueryLite")
                    Events.Add(eventData);
            }

            public override void Dispose()
            {
                if (_attached != null) DisableEvents(_attached);
                base.Dispose();
            }
        }

        private static IDisposable SimulateAot()
        {
            var prev = RuntimeProbe.IsDynamicCodeSupported;
            RuntimeProbe.IsDynamicCodeSupported = false;
            return new RestoreOnDispose(() => RuntimeProbe.IsDynamicCodeSupported = prev);
        }

        private sealed class RestoreOnDispose(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }

        [Fact]
        public void Warns_when_simulated_AOT_with_in_memory_provider()
        {
            _ = OdataQueryLiteEventSource.Log; // force EventSource construction before listener subscribes
            using var listener = new CapturingListener();
            using (SimulateAot())
            {
                var parsed = FilterParser.Parse("Id eq 1");
                var compiled = CompiledQueryFactory.Build<Row>(parsed);
                compiled.Apply(InMemorySource(), parsed.Literals).ToList();
            }

            Assert.Contains(listener.Events, e => e.EventId == 1);
        }

        [Fact]
        public void Does_not_warn_in_JIT_mode_against_in_memory_provider()
        {
            _ = OdataQueryLiteEventSource.Log;
            using var listener = new CapturingListener();

            var parsed = FilterParser.Parse("Id eq 1");
            var compiled = CompiledQueryFactory.Build<Row>(parsed);
            compiled.Apply(InMemorySource(), parsed.Literals).ToList();

            Assert.DoesNotContain(listener.Events, e => e.EventId == 1);
        }

        [Fact]
        public void Does_not_warn_when_source_is_not_EnumerableQuery()
        {
            _ = OdataQueryLiteEventSource.Log;
            using var listener = new CapturingListener();
            using (SimulateAot())
            {
                var parsed = FilterParser.Parse("Id eq 1");
                var compiled = CompiledQueryFactory.Build<Row>(parsed);
                var nonEnumerable = new FakeQueryable<Row>([new Row { Id = 1, Name = "x" }]);
                compiled.Apply(nonEnumerable, parsed.Literals).ToList();
            }

            Assert.DoesNotContain(listener.Events, e => e.EventId == 1);
        }

        [Fact]
        public void Warns_only_once_per_compiled_query_instance()
        {
            _ = OdataQueryLiteEventSource.Log;
            using var listener = new CapturingListener();
            using (SimulateAot())
            {
                var parsed = FilterParser.Parse("Id eq 1");
                var compiled = CompiledQueryFactory.Build<Row>(parsed);
                compiled.Apply(InMemorySource(), parsed.Literals).ToList();
                compiled.Apply(InMemorySource(), parsed.Literals).ToList();
                compiled.Apply(InMemorySource(), parsed.Literals).ToList();
            }

            Assert.Single(listener.Events.Where(e => e.EventId == 1));
        }

        // Minimal IQueryable that is NOT an EnumerableQuery. Used to assert the warning is
        // gated on the provider type, not just the data being in-memory.
        private sealed class FakeQueryable<T>(IList<T> items) : IQueryable<T>, IQueryProvider
        {
            public Expression Expression { get; } = Expression.Constant(items.AsQueryable());
            public Type ElementType => typeof(T);
            public IQueryProvider Provider => this;

            public IQueryable CreateQuery(Expression expression) => items.AsQueryable();
            public IQueryable<TResult> CreateQuery<TResult>(Expression expression)
            {
                if (typeof(TResult) != typeof(T))
                    throw new NotSupportedException($"FakeQueryable does not support projections to {typeof(TResult).Name}.");
                return (IQueryable<TResult>)(object)new FakeQueryable<T>(items);
            }
            public object Execute(Expression expression) => items;
            public TResult Execute<TResult>(Expression expression) => (TResult)(object)items;

            public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
        }
    }

    [CollectionDefinition(nameof(AotInMemoryWarningCollection), DisableParallelization = true)]
    public class AotInMemoryWarningCollection { }
}
