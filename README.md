# OdataQueryLite

A lightweight, dependency-free OData v4 **$filter / $orderby / $expand / $select** parser for .NET. No `Microsoft.AspNetCore.OData`, no EDM, no ASP.NET coupling.

> **Status:** alpha — parser front-half complete. Expression builder + EF Core IQueryable application + cache layer landing next.

## Why

`Microsoft.AspNetCore.OData` is ~9 MB of EDM model, MVC formatters, routers and serializers — useful when you want all of OData, heavyweight when you only want to accept query options against `IQueryable<T>`. OdataQueryLite is the latter half: it parses the URL query options into an AST, and (soon) translates them to `Expression<Func<T, ...>>` ready for EF Core.

## Surface (current)

| OData query option | Status |
|---|---|
| `$filter` operators `eq / ne / gt / ge / lt / le / and / or / not` | parsed |
| `$filter` functions `contains / startswith / endswith` | parsed |
| `$filter` string / date / math functions (`tolower`, `year`, `round`, …) | parsed |
| `$filter` lambdas `Items/any(o: o/Status eq 'X')`, `Items/all(...)` | parsed |
| `$filter` collection count `Items/$count gt 0` | parsed |
| `$orderby` | parsed |
| `$expand` (nested, with inner `$select`/`$expand`, slash chains) | parsed |
| `$select` (flat names, nested paths) | parsed |
| `$top` / `$skip` / `$count` | host-side (model binder) |
| `$apply` | **not supported** — reject at the host layer |

## Quick example

```csharp
using OdataQueryLite.Parsing;

var lexed = OdataLexer.Tokenize("Status eq 'Active' and Amount gt 100");
var result = FilterParser.Parse("Status eq 'Active' and Amount gt 100");

// AST root is parameterized — literals collected separately for cache reuse.
result.Ast        // BinaryNode(And, BinaryNode(Eq, MemberNode([Status]), ParamRefNode(0)), ...)
result.Literals   // [("Active", String), (100, Number)]

// Lexer wrapper also gives you a cache-friendly shape rendering.
lexed.ToShapeString()  // "Status eq ?str and Amount gt ?num"
lexed.ToString()       // "Status eq 'Active' and Amount gt 100" (re-rendered verbatim)
```

## Roadmap

- [x] Lexer + filter / orderby / expand parsers + AST
- [x] Parameterized literals for shape-based caching
- [ ] `PropertyAccessVisitor` + `AllowedExpandTree` (whitelist / subsumption)
- [ ] `FilterExpressionBuilder` (AST → `Expression<Func<T, bool>>`)
- [ ] `TypeCoercion` (enum / nullable / DateTimeOffset)
- [ ] Compiled-delegate cache keyed on `(entityType, shape, parameterTypes)`

## Origin

Extracted from an internal HCS Platform module that was replacing `Microsoft.AspNetCore.OData` for performance + dependency-surface reasons. Open-sourced to broaden the review surface and let the wider .NET ecosystem use the parser independently of the original host.

## License

MIT — see [LICENSE](LICENSE).
