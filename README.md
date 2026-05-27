# OdataQueryLite

A lightweight OData v4 **`$filter` / `$orderby` / `$expand` / `$select` / `$top` / `$skip` / `$count`** engine for .NET. No `Microsoft.AspNetCore.OData`, no EDM model, no MVC formatter — just parses the query string and composes `IQueryable<T>` transformations you can hand to EF Core or any LINQ provider.

> **Status:** `0.1.0`. Parser + expression builder + cache + ASP.NET Core integration shipped. 248 tests, 0 build warnings. Public API may still shift before `1.0.0` — pin the patch version if you depend on it now.

## Why

`Microsoft.AspNetCore.OData` is ~9 MB of EDM model, MVC formatters, routers, and serializers — overkill when all you want is "accept `$filter=...` against my `IQueryable<T>`." OdataQueryLite gives you that without the rest:

- Pure `System.Linq.Expressions`, AOT-compatible (`<IsAotCompatible>true</IsAotCompatible>`).
- Provider-agnostic — the engine never enumerates. Hand the result to EF Core, an in-memory list, or any custom `IQueryable<T>` provider.
- Async-friendly without an EF-Core sub-package: the engine returns `IQueryable`, you `await x.LongCountAsync()` (or sync `LongCount()`) on your side.

## Two packages

| Package | Targets | Use when |
|---|---|---|
| **`OdataQueryLite`** | pure `net10.0`, no ASP.NET Core dependency | parsing query strings outside a web host (CLI tools, batch jobs, tests) — or you bring your own host glue |
| **`OdataQueryLite.AspNetCore`** | `net10.0`, `FrameworkReference Microsoft.AspNetCore.App` | MVC model binder + Minimal-API parameter wrapper + error-mapping middleware in one line of startup |

## Quick start (ASP.NET Core)

```csharp
// Program.cs — Minimal API + MVC, same setup.
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddControllers()
    .AddOdataQueryLite();    // MVC binder (skip this for pure Minimal-API hosts)
builder.Services.AddOdataQueryLite();  // cache + infrastructure

var app = builder.Build();
app.UseOdataQueryLite();     // maps OdataQueryException -> HTTP 400
app.MapControllers();

// Minimal API: take OdataQueryRequest<T> as a parameter.
app.MapGet("/items", async (OdataQueryRequest<Item> q, AppDbContext db) =>
{
    var result = q.Options.Apply(db.Items);
    return Results.Ok(new
    {
        total = q.Options.Count ? await result.Unpaged.LongCountAsync() : (long?)null,
        // result.Data is IQueryable — element type is Item when no $select/$expand is
        // requested, otherwise Dictionary<string, object?>. Cast<object>() lets the JSON
        // serializer pick up the runtime element type either way.
        data  = await result.Data.Cast<object>().ToListAsync(),
    });
});

app.Run();
```

MVC controller version:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ItemsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(OdataQueryOptions<Item> q)
    {
        var result = q.Apply(db.Items);
        return Ok(new
        {
            total = q.Count ? await result.Unpaged.LongCountAsync() : (long?)null,
            data  = await result.Data.Cast<object>().ToListAsync(),
        });
    }
}
```

Malformed `$filter`, `$apply`, negative `$top`, duplicate `$-options`, etc. throw `OdataQueryException` from the binder/`BindAsync`; the middleware turns them into `400` with `{ Error, Message, Option }` JSON. Controllers see `q.Apply(...)` succeed or never run.

## Standalone parsing (no web host)

```csharp
using OdataQueryLite;

var opts = new OdataQueryOptions<Item>(new OdataQueryParts
{
    Filter  = "Price gt 25 and Name eq 'Apple'",
    OrderBy = "Price desc",
    Top     = 10,
    Count   = true,
});

var result = opts.Apply(items.AsQueryable());
long total = result.Unpaged.LongCount();
// No $select/$expand here, so Data's runtime element type is Item.
List<Item> page = result.Data.Cast<Item>().ToList();
```

## Surface

| OData query option | Status |
|---|---|
| `$filter` — `eq / ne / gt / ge / lt / le / and / or / not` | ✅ parsed + applied |
| `$filter` — `contains / startswith / endswith` | ✅ |
| `$filter` — string / date / math functions (`tolower`, `year`, `round`, …) | ✅ |
| `$filter` — lambdas `Items/any(o: o/Status eq 'X')`, `Items/all(...)` | ✅ |
| `$filter` — collection count `Items/$count gt 0` | ✅ |
| `$orderby` — multi-key, `desc`, nested paths, collection `/$count` | ✅ |
| `$expand` — nested, slash chains, inner `$select`/`$expand` | ✅ parsed + applied (`Dictionary<string, object?>` projection) |
| `$select` — flat names, nested paths | ✅ parsed + applied (honors `[JsonIgnore]` from Newtonsoft / STJ + `[OdataIgnore]`) |
| `$top` / `$skip` / `$count` | ✅ applied (`$count` exposes `Unpaged` for caller to materialize) |
| `$apply` | ❌ — `UnsupportedQueryOptionException` at construction |

## Configuration

```csharp
services.AddOdataQueryLite(opts =>
{
    opts.UseCache         = true;     // default: process-wide compiled-query cache
    opts.MaxCacheEntries  = 10_000;   // default; bump for high-cardinality filter surfaces
});
```

Cache is keyed on `(entityType, normalized-shape, parameter-types)` — `?$filter=Id eq 1` and `?$filter=Id eq 2` share a compiled `Func<T, bool>`; the literal `1` / `2` flows through a `ValueTuple<>` rather than re-baked into the expression tree.

## Roadmap

- [x] Lexer + filter / orderby / expand / select parsers + AST
- [x] Parameterized literals + shape-based caching
- [x] `PropertyAccessVisitor` + `AllowedExpandNode` (whitelist + subsumption)
- [x] `FilterExpressionBuilder` (AST → `Expression<Func<T, bool>>`)
- [x] `TypeCoercion` (enum / nullable / DateTimeOffset / `Edm.Binary`)
- [x] Compiled-delegate cache (`QueryCompileCache`, LRU-style soft cap)
- [x] `OdataQueryOptions<T>` orchestrator + `OrderByApplier`
- [x] ASP.NET Core integration package (MVC `IModelBinder`, Minimal-API `OdataQueryRequest<T>`, error-mapping middleware, idempotent `AddOdataQueryLite()` split per ASP.NET Core convention)
- [x] `IQueryable` return-shape redesign (`QueryResult.Unpaged` deferred enumeration → no EF-Core sub-package needed)
- [x] `$select` / `$expand` projection applied to the returned `IQueryable` (`SelectExpandProjector` — emits `Dictionary<string, object?>` projection, EF Core translates to flat `SELECT col1, col2`; honors `[JsonIgnore]` from both `Newtonsoft.Json` and `System.Text.Json.Serialization` plus its own `[OdataIgnore]`)

## Trim / AOT

The library is annotated for the trimmer (`<IsAotCompatible>true</IsAotCompatible>`). Public entry points (`OdataQueryOptions<T>` ctor, `AddOdataQueryLite()`) carry `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` because filter expressions are compiled at runtime against `T`'s reflected properties. Hosts targeting Native AOT should mark their `[DynamicallyAccessedMembers(PublicProperties)]` on the entity types they expose.

## Origin

Extracted from an internal HCS Platform module that was replacing `Microsoft.AspNetCore.OData` for performance + dependency-surface reasons. Open-sourced to broaden the review surface and let the wider .NET ecosystem use the parser independently of the original host.

## License

MIT — see [LICENSE](LICENSE).
