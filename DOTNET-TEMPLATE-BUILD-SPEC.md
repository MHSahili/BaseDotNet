# .NET 10 Vertical-Slice API Template: Build Spec

A build guide for a .NET 10 Web API designed to be easy for humans **and AI agents** to work in: one project, vertical slices, controllers, EF Core used directly, and one standard way to do each cross-cutting concern.

> **How to use this file**
> Follow the phases in order. Each phase ends with a check. Items marked **(verify)** use APIs or package behavior that change between versions, so confirm them against current docs before relying on them. Section 15 lists what we have *not* decided or built yet.

---

## 1. Decisions

### Decided

| Area | Decision |
|---|---|
| Runtime | .NET 10, one web project, vertical slices (feature folders) |
| API style | **Controllers** (thin), one base class, handlers injected per action |
| Mediator / mapping / validation libs | **None.** No MediatR, AutoMapper, or FluentValidation |
| Request handling | Plain handler classes, one per feature, registered in DI |
| Mapping | Project in the query (`Select`) or map by hand |
| Validation | DataAnnotations on request types for shape; business rules in domain methods |
| Data access | Inject `AppDbContext` directly into handlers. **No repositories, no unit-of-work wrapper** |
| Errors | Typed exceptions + one global `IExceptionHandler` returning `ProblemDetails` |
| Logging | Built-in `ILogger<T>` + `[LoggerMessage]`, exported by OpenTelemetry. **No Serilog** |
| Telemetry | OpenTelemetry traces, metrics, logs over OTLP → Grafana |
| Service timing | `ActivitySource` spans at service boundaries (+ optional histogram) |
| AuthN / AuthZ | Fallback policy requires authentication; named policies; `ICurrentUser` |
| Caching | `HybridCache` (L1 memory + L2 Redis), tag invalidation, short L1 TTL |
| Lists | `PagedRequest`/`PagedResponse`, whitelisted sorting with `Id` tiebreaker, typed filters, offset paging (keyset for large lists) |
| Testing | Integration tests through `WebApplicationFactory` against a real database (Testcontainers + Respawn) |
| Performance | BenchmarkDotNet (handler level), k6 (load), OpenTelemetry (production) |

### Assumed defaults (change if you disagree)

| Area | Default | Alternative |
|---|---|---|
| Database | PostgreSQL | SQL Server, SQLite |
| Test framework | xUnit | NUnit |
| Identity | External provider, JWT bearer | ASP.NET Core Identity (you own users) |
| Assertions | Shouldly | AwesomeAssertions or built-in asserts |

---

## 2. Target layout

```
MyApp/
├── AGENTS.md
├── MyApp.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── BannedSymbols.txt
├── docker-compose.yml
├── docs/adr/                         # one short file per decision
├── src/MyApp.Api/
│   ├── Program.cs
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/           # IEntityTypeConfiguration<T>
│   │   └── Migrations/
│   ├── Domain/                       # entities, value objects (behavior lives here)
│   ├── Features/
│   │   └── Todos/
│   │       ├── TodosController.cs
│   │       ├── CreateTodo.cs         # request, response, handler
│   │       ├── GetTodo.cs
│   │       ├── ListTodos.cs
│   │       └── CompleteTodo.cs
│   └── Common/
│       ├── Api/ApiControllerBase.cs
│       ├── Auth/{AuthExtensions,Policies,ICurrentUser}.cs
│       ├── Caching/{CachingExtensions,CacheKeys,CacheTags}.cs
│       ├── Errors/{AppExceptions,GlobalExceptionHandler,ErrorExtensions}.cs
│       ├── Observability/{ObservabilityExtensions,Telemetry}.cs
│       └── Paging/{PagedRequest,PagedResponse,PagingExtensions}.cs
├── tests/
│   ├── MyApp.IntegrationTests/       # correctness
│   ├── MyApp.Benchmarks/             # BenchmarkDotNet
│   └── MyApp.LoadTests/              # k6 scripts
└── .github/workflows/ci.yml          # see Future Updates
```

Rule: features depend on `Common/`, `Data/`, `Domain/`. `Common/` never depends on `Features/`. Features never depend on each other.

---

## 3. Phase 0: Scaffold

```bash
mkdir MyApp && cd MyApp
git init

dotnet new globaljson --sdk-version <your installed 10.x SDK> --roll-forward latestFeature
dotnet new sln --format slnx --name MyApp

dotnet new webapi --use-controllers -o src/MyApp.Api
dotnet new xunit -o tests/MyApp.IntegrationTests
dotnet new console -o tests/MyApp.Benchmarks

dotnet sln add src/MyApp.Api/MyApp.Api.csproj \
               tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj \
               tests/MyApp.Benchmarks/MyApp.Benchmarks.csproj

dotnet add tests/MyApp.IntegrationTests reference src/MyApp.Api
dotnet add tests/MyApp.Benchmarks reference src/MyApp.Api
mkdir -p tests/MyApp.LoadTests docs/adr
```

Delete the template's sample controller and weather forecast types.

**Check:** `dotnet build` succeeds from the repo root.

---

## 4. Phase 1: Build settings

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisMode>Recommended</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

If a Recommended-level analyzer rule is too noisy, downgrade that one rule in `.editorconfig` with a comment explaining why. Do not turn off warnings-as-errors.

### `Directory.Packages.props` (central package management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Versions are added by `dotnet add package`. Use latest stable. -->
  </ItemGroup>
</Project>
```

Install packages in Phase 2 with `dotnet add package`, which records versions here (verify that your SDK does this; otherwise add `PackageVersion` entries by hand).

### `BannedSymbols.txt`

```
P:System.DateTime.UtcNow;Use TimeProvider
P:System.DateTime.Now;Use TimeProvider
```

Add EF raw SQL methods here if you want raw SQL banned (use the documentation-ID format for each method). Requires `Microsoft.CodeAnalysis.BannedApiAnalyzers`.

### `.editorconfig`

Start from `dotnet new editorconfig`, then set file-scoped namespaces, `var` preferences, and naming rules as errors.

**Check:** `dotnet build` passes with warnings as errors on an empty project.

---

## 5. Phase 2: Packages

### `MyApp.Api`

```bash
cd src/MyApp.Api
# Data
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
# Auth
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
# Caching
dotnet add package Microsoft.Extensions.Caching.Hybrid
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
# Telemetry
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.Runtime
# API docs
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
# Analyzers
dotnet add package Microsoft.CodeAnalysis.BannedApiAnalyzers
# Health + outbound resilience
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
dotnet add package Microsoft.Extensions.Http.Resilience
```

### `MyApp.IntegrationTests`

```bash
cd ../../tests/MyApp.IntegrationTests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Testcontainers.PostgreSql
dotnet add package Testcontainers.Redis
dotnet add package Respawn
dotnet add package Shouldly
dotnet add package NetArchTest.Rules
```

### `MyApp.Benchmarks`

```bash
cd ../MyApp.Benchmarks
dotnet add package BenchmarkDotNet
dotnet add package Testcontainers.PostgreSql
```

### Deliberately not included

MediatR, AutoMapper, FluentValidation, Serilog, a repository library, Moq (we do not mock `DbContext`), Aspire (add later if you want one-command orchestration).

**Check:** `dotnet restore && dotnet build` from the repo root.

---

## 6. Phase 3: Common building blocks

Build these before any feature. Each concern has **one** registration extension, called from `Program.cs`.

### 6.1 Errors

`Common/Errors/AppExceptions.cs`

```csharp
namespace MyApp.Api.Common.Errors;

public abstract class AppException(string message, int status, string title) : Exception(message)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
}

public sealed class BadRequestException(string message)
    : AppException(message, StatusCodes.Status400BadRequest, "Bad request");

public sealed class NotFoundException(string entity, object key)
    : AppException($"{entity} '{key}' was not found.", StatusCodes.Status404NotFound, "Not found");

public sealed class ConflictException(string message)
    : AppException(message, StatusCodes.Status409Conflict, "Conflict");

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException(message, StatusCodes.Status403Forbidden, "Forbidden");

public sealed class DomainRuleException(string message)
    : AppException(message, StatusCodes.Status422UnprocessableEntity, "Business rule violated");
```

`Common/Errors/GlobalExceptionHandler.cs`

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Api.Common.Errors;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problems,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
            return true; // client went away; not an error

        var (status, title, detail) = ex switch
        {
            AppException app => (app.Status, app.Title, app.Message),
            DbUpdateConcurrencyException => (409, "Conflict", "The resource was modified by someone else. Reload and retry."),
            BadHttpRequestException => (400, "Bad request", "The request could not be read."),
            _ => (500, "Server error", "An unexpected error occurred.")
        };

        if (status >= 500) logger.LogError(ex, "Unhandled exception");
        else logger.LogInformation("Request failed with {Status}: {Message}", status, ex.Message);

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = { Status = status, Title = title, Detail = detail }
        });
    }
}
```

`Common/Errors/ErrorExtensions.cs`

```csharp
namespace MyApp.Api.Common.Errors;

public static class ErrorExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
            c.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString());
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
```

Rules: handlers throw these types for expected failures; nothing else catches exceptions; the 500 response never includes exception details; every error response carries a `traceId`. **(verify)** `IProblemDetailsService` usage against current docs.

### 6.2 API base

`Common/Api/ApiControllerBase.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Api.Common.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class ApiControllerBase : ControllerBase;
```

### 6.3 Observability

`Common/Observability/Telemetry.cs`

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Api.Common.Observability;

public static class Telemetry
{
    public const string Name = "MyApp";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("myapp.cache.hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("myapp.cache.misses");
}
```

`Common/Observability/ObservabilityExtensions.cs`

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MyApp.Api.Common.Observability;

public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t
                .AddSource(Telemetry.Name)          // without this, custom spans are silently dropped
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(Telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .UseOtlpExporter();                     // endpoint/headers/service name come from OTEL_* env vars

        return builder;
    }
}
```

Rules:
- Use only `ILogger<T>` with `[LoggerMessage]` source-generated methods.
- Every public method on a service class that does I/O or calls other services starts a span: `using var activity = Telemetry.Source.StartActivity("ClassName.Method");`
- Endpoints, tokens, and credentials come only from environment variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`). Never log secrets or tokens.
- Optionally add EF Core tracing via its OpenTelemetry instrumentation package. **(verify)** current release status first.

### 6.4 Authentication and authorization

`Common/Auth/Policies.cs`

```csharp
namespace MyApp.Api.Common.Auth;

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);
}
```

`Common/Auth/ICurrentUser.cs`

```csharp
using System.Security.Claims;

namespace MyApp.Api.Common.Auth;

public interface ICurrentUser
{
    string Id { get; }
    bool IsInRole(string role);
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No current user.");

    public string Id => Principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Authenticated user has no id claim.");

    public bool IsInRole(string role) => Principal.IsInRole(role);
}
```

`Common/Auth/AuthExtensions.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace MyApp.Api.Common.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        // Authority / audience are read from configuration (Authentication:Schemes:Bearer). (verify)
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole("admin"));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }
}
```

Rules:
- Secure by default: the fallback policy protects every endpoint. Public endpoints must say `[AllowAnonymous]`.
- Policy names live only in `Policies.cs`. No role strings scattered in controllers.
- Handlers use `ICurrentUser`. **Nothing outside `Common/Auth` touches `HttpContext`.**
- Resource ownership checks ("can this user edit this item?") happen in the handler and throw `ForbiddenException` (or use `IAuthorizationService` for resource-based policies).
- If you own your users instead, swap the JWT setup for ASP.NET Core Identity. The rest of this document is unchanged.

### 6.5 Caching

`Common/Caching/CacheKeys.cs` and `CacheTags.cs`. The **only** place keys and tags are built.

```csharp
namespace MyApp.Api.Common.Caching;

public static class CacheKeys
{
    private const string V = "v1";   // bump when cached shapes change
    public static string Todo(int id) => $"{V}:todos:item:{id}";
    public static string TodoList(string ownerId, int page, int pageSize, string sort, string completed) =>
        $"{V}:todos:list:{ownerId}:{page}:{pageSize}:{sort}:{completed}";
}

public static class CacheTags
{
    public static string TodosOf(string ownerId) => $"todos:user:{ownerId}";
    public static string Todo(int id) => $"todo:{id}";
}
```

`Common/Caching/CachingExtensions.cs`

```csharp
using Microsoft.Extensions.Caching.Hybrid;

namespace MyApp.Api.Common.Caching;

public static class CachingExtensions
{
    public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration config)
    {
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);   // L2; HybridCache picks it up

        services.AddHybridCache(o =>
        {
            o.MaximumPayloadBytes = 1024 * 1024;
            o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(1),   // L1: worst-case cross-node staleness
                Expiration = TimeSpan.FromMinutes(10)             // L2
            };
        });
        return services;
    }
}
```

Rules:
- Cache DTOs/response objects, never EF entities.
- Keys come only from `CacheKeys`. Include every dimension the result depends on (user, tenant, parameters).
- Every entry sets tags. Writes call `RemoveByTagAsync` **after** `SaveChangesAsync` succeeds.
- **L1 is not invalidated across nodes** (HybridCache has no backplane; tracked in the dotnet/runtime repo, **verify** current status). Keep L1 TTL short, or use FusionCache if stale reads after writes are unacceptable.
- No Redis configured means L1 only, which is fine for a single instance.
- Never cache secrets, authorization decisions with long lifetimes, or "not found" results by accident.

### 6.6 Paging, sorting, filtering

`Common/Paging/PagedRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Api.Common.Paging;

public class PagedRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;   // hard cap
    public string? Sort { get; init; }                         // e.g. "title" or "-created"
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResponse<T>> ToPagedAsync<T>(
        this IQueryable<T> query, PagedRequest r, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToListAsync(ct);
        return new(items, r.Page, r.PageSize, total);
    }
}
```

Rules:
- Sorting goes through an **explicit whitelist** in each list feature, with a deterministic `Id` tiebreaker. Unknown sort → `BadRequestException`.
- Filters are typed properties on the feature's request. No dynamic filter languages (OData, dynamic LINQ, `field:op:value` parsers).
- Cap `PageSize` at the API boundary. Index every sortable and filterable column.
- Use offset paging by default. Use keyset (cursor) paging for large or infinite-scroll lists, and don't mix both shapes in one list.
- Cache only hot, low-cardinality lists (first pages, default sort, no free-text search). Normalize keys. Tag per owner.

### 6.7 Data

`Data/AppDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Api.Domain;

namespace MyApp.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

Register with a **lazy** configuration read, so tests can override connection strings:

```csharp
services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
```

Rules (put in `AGENTS.md`):
1. Handlers inject `AppDbContext` and nothing else for data access.
2. Reads: `AsNoTracking()` + `Select` into the response type.
3. Writes: load the entity, call a method on it, `SaveChangesAsync` once per handler.
4. Shared query logic: extension methods on `IQueryable<T>`.
5. No raw SQL or Dapper unless documented as an exception in `docs/adr/`.
6. `AppDbContext` is never referenced outside `Features/` and `Data/`.

### 6.8 Handler registration

Handlers are plain scoped classes. Register them in one place. Either list them explicitly in `Features/FeatureRegistration.cs`, or scan once for nested classes named `Handler`. **Pick one and document it.** The explicit list is easier to follow, and an architecture test can assert none are missing.

**Check:** `dotnet build` passes with all of the above in place.

---

## 7. Phase 4: `Program.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Api.Common.Auth;
using MyApp.Api.Common.Caching;
using MyApp.Api.Common.Errors;
using MyApp.Api.Common.Observability;
using MyApp.Api.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddErrorHandling();
builder.Services.AddAuth();
builder.Services.AddCaching(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFeatures();                  // handlers
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live").AllowAnonymous();

app.Run();

public partial class Program;   // required by WebApplicationFactory<Program>
```

`Program.cs` only calls extension methods. No business logic, no inline configuration.

**Check:** the app starts and `GET /health/live` returns 200 (it needs a database; start Phase 7 first).

---

## 8. Phase 5: Reference feature (`Features/Todos`)

This is the pattern every feature copies. Keep it in the repo as the canonical example and reference it from `AGENTS.md`.

### Domain

`Domain/TodoItem.cs`

```csharp
using MyApp.Api.Common.Errors;

namespace MyApp.Api.Domain;

public sealed class TodoItem
{
    private TodoItem() { }

    public int Id { get; private set; }
    public string OwnerId { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public bool Completed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static TodoItem Create(string ownerId, string title, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainRuleException("A todo needs a title.");
        return new TodoItem { OwnerId = ownerId, Title = title.Trim(), CreatedAt = now };
    }

    public void Complete()
    {
        if (Completed) throw new DomainRuleException("This todo is already completed.");
        Completed = true;
    }
}
```

### Create

`Features/Todos/CreateTodo.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Caching.Hybrid;
using MyApp.Api.Common.Auth;
using MyApp.Api.Common.Caching;
using MyApp.Api.Data;
using MyApp.Api.Domain;

namespace MyApp.Api.Features.Todos;

public static class CreateTodo
{
    public sealed record Request([property: Required, MaxLength(200)] string Title);
    public sealed record Response(int Id, string Title);

    public sealed class Handler(AppDbContext db, ICurrentUser user, TimeProvider time, HybridCache cache)
    {
        public async Task<Response> HandleAsync(Request request, CancellationToken ct)
        {
            var todo = TodoItem.Create(user.Id, request.Title, time.GetUtcNow());
            db.Todos.Add(todo);
            await db.SaveChangesAsync(ct);

            await cache.RemoveByTagAsync(CacheTags.TodosOf(user.Id), ct);   // after the save succeeds
            return new Response(todo.Id, todo.Title);
        }
    }
}
```

Test that a missing or empty `Title` actually returns 400. Attribute targets on positional records are easy to get wrong, hence the `property:` target.

### Get (with an ownership check)

`Features/Todos/GetTodo.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Api.Common.Auth;
using MyApp.Api.Common.Errors;
using MyApp.Api.Data;

namespace MyApp.Api.Features.Todos;

public static class GetTodo
{
    public sealed record Response(int Id, string Title, bool Completed, DateTimeOffset CreatedAt);

    public sealed class Handler(AppDbContext db, ICurrentUser user)
    {
        public async Task<Response> HandleAsync(int id, CancellationToken ct)
        {
            var todo = await db.Todos.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new { t.OwnerId, Response = new Response(t.Id, t.Title, t.Completed, t.CreatedAt) })
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Todo", id);

            if (todo.OwnerId != user.Id) throw new NotFoundException("Todo", id);   // don't reveal existence
            return todo.Response;
        }
    }
}
```

### List (paging, sorting, filtering, caching)

`Features/Todos/ListTodos.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MyApp.Api.Common.Auth;
using MyApp.Api.Common.Caching;
using MyApp.Api.Common.Errors;
using MyApp.Api.Common.Paging;
using MyApp.Api.Data;
using MyApp.Api.Domain;

namespace MyApp.Api.Features.Todos;

public static class ListTodos
{
    public sealed class Request : PagedRequest
    {
        public bool? Completed { get; init; }
        [MaxLength(100)] public string? Search { get; init; }
    }

    public sealed record Item(int Id, string Title, bool Completed);

    public sealed class Handler(AppDbContext db, HybridCache cache, ICurrentUser user)
    {
        public ValueTask<PagedResponse<Item>> HandleAsync(Request r, CancellationToken ct)
        {
            if (!IsCacheable(r))
                return new ValueTask<PagedResponse<Item>>(QueryAsync(r, ct));

            var key = CacheKeys.TodoList(user.Id, r.Page, r.PageSize,
                (r.Sort ?? "-created").ToLowerInvariant(), r.Completed?.ToString() ?? "any");

            return cache.GetOrCreateAsync(
                key,
                token => new ValueTask<PagedResponse<Item>>(QueryAsync(r, token)),
                tags: [CacheTags.TodosOf(user.Id)],
                cancellationToken: ct);
        }

        // Only hot, low-cardinality queries are cached.
        private static bool IsCacheable(Request r) => r.Page <= 3 && string.IsNullOrWhiteSpace(r.Search);

        private async Task<PagedResponse<Item>> QueryAsync(Request r, CancellationToken ct)
        {
            var q = db.Todos.AsNoTracking().Where(t => t.OwnerId == user.Id);
            if (r.Completed is { } c) q = q.Where(t => t.Completed == c);
            if (!string.IsNullOrWhiteSpace(r.Search)) q = q.Where(t => t.Title.Contains(r.Search));

            return await ApplySort(q, r.Sort)
                .Select(t => new Item(t.Id, t.Title, t.Completed))
                .ToPagedAsync(r, ct);
        }

        private static IQueryable<TodoItem> ApplySort(IQueryable<TodoItem> q, string? sort) =>
            sort?.ToLowerInvariant() switch
            {
                "title"   => q.OrderBy(t => t.Title).ThenBy(t => t.Id),
                "-title"  => q.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id),
                "created" => q.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
                null or "-created" => q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
                _ => throw new BadRequestException($"Unknown sort '{sort}'.")
            };
    }
}
```

### Complete (domain rule + invalidation)

`Features/Todos/CompleteTodo.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MyApp.Api.Common.Auth;
using MyApp.Api.Common.Caching;
using MyApp.Api.Common.Errors;
using MyApp.Api.Data;

namespace MyApp.Api.Features.Todos;

public static class CompleteTodo
{
    public sealed class Handler(AppDbContext db, ICurrentUser user, HybridCache cache)
    {
        public async Task HandleAsync(int id, CancellationToken ct)
        {
            var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == user.Id, ct)
                ?? throw new NotFoundException("Todo", id);

            todo.Complete();                                   // throws DomainRuleException if already done
            await db.SaveChangesAsync(ct);

            await cache.RemoveByTagAsync([CacheTags.TodosOf(user.Id), CacheTags.Todo(id)], ct);
        }
    }
}
```

### Controller

`Features/Todos/TodosController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Common.Api;
using MyApp.Api.Common.Paging;

namespace MyApp.Api.Features.Todos;

public sealed class TodosController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateTodo.Response>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateTodo.Response>> Create(
        CreateTodo.Request request, [FromServices] CreateTodo.Handler handler, CancellationToken ct)
    {
        var response = await handler.HandleAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<GetTodo.Response>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetTodo.Response>> Get(
        int id, [FromServices] GetTodo.Handler handler, CancellationToken ct) =>
        await handler.HandleAsync(id, ct);

    [HttpGet]
    [ProducesResponseType<PagedResponse<ListTodos.Item>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ListTodos.Item>>> List(
        [FromQuery] ListTodos.Request request, [FromServices] ListTodos.Handler handler, CancellationToken ct) =>
        await handler.HandleAsync(request, ct);

    [HttpPost("{id:int}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Complete(
        int id, [FromServices] CompleteTodo.Handler handler, CancellationToken ct)
    {
        await handler.HandleAsync(id, ct);
        return NoContent();
    }
}
```

Controller rules: thin (one handler call per action), no `DbContext`, no business logic, no try/catch, `ActionResult<T>` plus `[ProducesResponseType]` everywhere, explicit binding sources where not obvious.

Add the EF configuration (`Data/Configurations/TodoItemConfiguration.cs`: key, max lengths, an index on `(OwnerId, CreatedAt, Id)`), then create the first migration:

```bash
dotnet ef migrations add Initial --project src/MyApp.Api --output-dir Data/Migrations
```

**Check:** `dotnet build`, then call the endpoints from Scalar with a valid token.

---

## 9. Phase 6: Integration tests

### Test infrastructure

`tests/MyApp.IntegrationTests/ApiFactory.cs`

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Api.Data;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace MyApp.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().Build();
    private Respawner _respawner = default!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // Env vars are read before Program's top-level code runs, so they are the reliable override.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _db.GetConnectionString());

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { }));

    public HttpClient CreateClientAs(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    // WebApplicationFactory and xUnit v2 both define DisposeAsync; implement xUnit's explicitly.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

`tests/MyApp.IntegrationTests/TestAuthHandler.cs`

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MyApp.IntegrationTests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());   // unauthenticated -> 401

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(r => new Claim(ClaimTypes.Role, r)));

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

**(verify)** that the test scheme becomes the default scheme in your setup. If the JWT scheme still wins, set `DefaultAuthenticateScheme` and `DefaultChallengeScheme` explicitly in the test registration. Share one factory per test run with a collection fixture, and call `ResetAsync()` at the start of each test.

### Required cases for every feature

1. Happy path
2. Validation failure → 400 `ProblemDetails`
3. Unauthenticated → 401
4. Forbidden / not the owner → 403 or 404 (as designed)
5. Not found → 404
6. Domain rule violated → 422
7. For cached features: read → write → read again reflects the change (run against real Redis via `Testcontainers.Redis`)
8. For lists: first, middle, last, out-of-range page; page size over cap (400); unknown sort (400); stable order with duplicate sort values; user A never sees user B's rows

Also assert a 500 response contains no exception details.

---

## 10. Phase 7: Local environment

`docker-compose.yml`

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: app
      POSTGRES_PASSWORD: app
      POSTGRES_DB: myapp
    ports: ["5432:5432"]
  redis:
    image: redis:7
    ports: ["6379:6379"]
  grafana:
    image: grafana/otel-lgtm      # Grafana + Loki + Tempo + Prometheus + OTLP collector
    ports: ["3000:3000", "4317:4317", "4318:4318"]
```

App environment (via `launchSettings.json` or user-secrets, never committed):

```
ConnectionStrings__Default=Host=localhost;Port=5432;Database=myapp;Username=app;Password=app
ConnectionStrings__Redis=localhost:6379
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_SERVICE_NAME=myapp-api
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=development
```

Grafana Cloud later: set `OTEL_EXPORTER_OTLP_ENDPOINT` to your stack's OTLP gateway, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and `OTEL_EXPORTER_OTLP_HEADERS` with the basic-auth token. Take the exact values from your stack's OpenTelemetry connection page. In production, put an OpenTelemetry Collector between the app and your backends.

**Check:** `docker compose up -d`, run the app, call an endpoint, and see the trace in Grafana (`localhost:3000` → Explore → Tempo). The span tree should show your service spans nested under the request.

---

## 11. Phase 8: Performance tests

### Handler benchmarks (BenchmarkDotNet)

One class per feature that matters. Use the production database engine with realistic row counts.

```csharp
[MemoryDiagnoser]
public class GetTodoBenchmarks
{
    private AppDbContext _db = null!;
    private GetTodo.Handler _handler = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _db = await BenchmarkDatabase.CreateSeededAsync(rows: 10_000);   // you write this helper: Testcontainers + fixed seed
        // construct the handler with a fake ICurrentUser that matches the seeded owner
    }

    [Benchmark] public Task<GetTodo.Response> ById() => _handler.HandleAsync(42, CancellationToken.None);

    [GlobalCleanup] public Task Cleanup() => BenchmarkDatabase.DisposeAsync();
}
```

Run: `dotnet run -c Release --project tests/MyApp.Benchmarks`.

Rules: benchmark **cache hit and cache miss as separate cases**; benchmark page 1 and a deep page for lists; keep data and environment fixed; do not assert absolute timings in normal CI tests (shared runners are noisy). Compare against stored baselines on a scheduled job.

### Load tests (k6)

`tests/MyApp.LoadTests/todos.js`

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 50 },
    { duration: '3m', target: 50 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<200'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get(`${__ENV.BASE_URL}/api/todos?page=1&pageSize=20`, {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` },
  });
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(1);
}
```

Run against a production-like environment, not a laptop.

### Production

Chart request duration p50/p95/p99 per route from the ASP.NET Core metrics in Grafana. Use traces to see time per service and per database call. Use the cache hit/miss counters to judge whether caching is helping.

---

## 12. Phase 9: Enforcement

Rules that are not checked get ignored. Add these as tests in `MyApp.IntegrationTests` (or a separate `MyApp.ArchitectureTests`):

```csharp
[Fact]
public void Controllers_inherit_ApiControllerBase() =>
    Types.InAssembly(typeof(Program).Assembly)
        .That().HaveNameEndingWith("Controller").And().AreNotAbstract()
        .Should().Inherit(typeof(ApiControllerBase))
        .GetResult().IsSuccessful.ShouldBeTrue();

[Fact]
public void Controllers_do_not_use_DbContext() =>
    Types.InAssembly(typeof(Program).Assembly)
        .That().HaveNameEndingWith("Controller")
        .ShouldNot().HaveDependencyOn("MyApp.Api.Data")
        .GetResult().IsSuccessful.ShouldBeTrue();

[Fact]
public void Features_do_not_depend_on_each_other() { /* one rule per feature namespace */ }

[Fact]
public void Only_Common_Caching_touches_distributed_cache_types() { /* IMemoryCache / IDistributedCache */ }

[Fact]
public void Features_do_not_use_HttpContext() { /* only Common/Auth may */ }
```

Also add:
- **Endpoint authorization coverage test.** Enumerate `EndpointDataSource` and fail if any endpoint lacks authorization metadata without an explicit `AllowAnonymous`.
- **Every controller action has `[ProducesResponseType]`.**
- **Every nested `Handler` is registered in DI** (if you chose explicit registration).
- Analyzer rules: nullable, warnings as errors, banned APIs (`DateTime.UtcNow`).

---

## 13. `AGENTS.md` (copy to repo root)

```markdown
# AGENTS.md

.NET 10 Web API. One project (`src/MyApp.Api`), vertical slices under `Features/`, controllers.
Reference feature to copy: `src/MyApp.Api/Features/Todos`.

## Commands
- Build: `dotnet build` (warnings are errors)
- Test: `dotnet test`
- Format check: `dotnet format --verify-no-changes`
- Local services: `docker compose up -d`
- Benchmarks: `dotnet run -c Release --project tests/MyApp.Benchmarks`

## Where code goes
- New behavior → `Features/<Area>/<Action>.cs` (request, response, handler) + one controller action.
- Business rules → methods on entities in `Domain/`.
- Cross-cutting setup → `Common/<Concern>/`, registered by one extension method.

## Hard rules
- Controllers are thin: one handler call per action. No DbContext, logic, or try/catch in controllers.
- Handlers inject `AppDbContext` directly. No repositories, no MediatR, AutoMapper, FluentValidation, Serilog.
- Reads: `AsNoTracking()` + `Select` into the response. Writes: load entity, call method, one `SaveChangesAsync`.
- Expected failures throw types from `Common/Errors/AppExceptions.cs`. Do not add new exception types without asking.
- Use `ICurrentUser`. Never touch `HttpContext` outside `Common/Auth`.
- Public endpoints need `[AllowAnonymous]`. Policy names live in `Policies.cs`.
- Logging: `ILogger<T>` with `[LoggerMessage]`. Never log secrets or tokens.
- Services that do I/O start a span: `Telemetry.Source.StartActivity("Class.Method")`.
- Cache DTOs only. Keys and tags come only from `CacheKeys`/`CacheTags`. Invalidate by tag after `SaveChangesAsync`.
- Lists: inherit `PagedRequest`, whitelist sorts, always add an `Id` tiebreaker, cap page size.
- Use `TimeProvider`, not `DateTime.UtcNow`.
- Do not add packages without asking. Do not edit generated files or applied migrations.
- Config, endpoints, and credentials come from environment variables. Never hardcode.

## Adding a feature (checklist)
1. Domain method (if there is a business rule)
2. Handler + request/response in `Features/<Area>/`
3. Controller action with `ActionResult<T>` and `[ProducesResponseType]`
4. Cache read and tag invalidation (if applicable)
5. Integration tests: happy path, 400, 401, 403/404, 404, 422 (as applicable)
6. Benchmark if performance-sensitive; report before/after numbers when optimizing
```

Add nested `AGENTS.md` files (for example in `tests/`) only when a folder has rules the root file does not cover.

---

## 14. Definition of done for a feature

- [ ] Follows the reference feature's shape
- [ ] No rule in `AGENTS.md` is violated (architecture tests pass)
- [ ] Integration tests cover the required cases (Section 9)
- [ ] Authorization is explicit and tested
- [ ] Cached reads have tags and an invalidation test
- [ ] Lists have a sort whitelist, a tiebreaker, and indexes
- [ ] Spans exist for service boundaries; traces look right in Grafana
- [ ] A benchmark exists if the feature is performance-sensitive
- [ ] `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass

---

## 15. Future updates (not yet discussed or built)

### Decide before writing many features (expensive to change later)

| Item | Why it matters | Suggested direction |
|---|---|---|
| **Domain model style** | Affects every entity | Rich entities with private setters and factory methods (started above). Decide on value objects and **strongly typed IDs** (`TodoId` instead of `int`; needs EF value converters and route model binding for controllers) |
| **Transactions and concurrency** | Silent data corruption if skipped | One `SaveChangesAsync` per handler (done). Add optimistic concurrency (row version column), a rule for multi-save operations (explicit transaction), and **idempotency keys** for retried POSTs |
| **Migrations and seeding strategy** | Risky if applied at startup in production | Apply automatically in development only. In production, run migrations as a separate deployment step (migration bundle). Decide how reference data is seeded |
| **Multi-tenancy** | Very hard to retrofit | If it might ever apply: tenant id on every entity, global query filter, tenant in every cache key |

### Before production

| Item | Notes |
|---|---|
| **CI pipeline** | Build, format check, analyzers, tests, architecture tests; scheduled benchmark job with stored baselines; k6 against a staging environment |
| **Containerization and deployment** | Dockerfile (multi-stage), environment configuration, how secrets reach the app, migration step |
| **Rate limiting** | ASP.NET Core built-in rate limiting middleware; per-user or per-IP policies |
| **CORS and security headers** | Explicit allowed origins; HSTS, content-type options, etc. |
| **Options pattern with startup validation** | `ValidateOnStart()` so missing settings fail at boot, not on first request |
| **Readiness health checks** | Liveness exists. Add readiness (database, Redis) and wire to the orchestrator |
| **Dependency hygiene** | Renovate or Dependabot; vulnerability scanning of packages |
| **Data protection and secrets** | Key storage for Data Protection if cookies or tokens are used; secret store in production |
| **Alerting and dashboards** | Error rate, latency percentiles, cache hit rate, saturation; imported ASP.NET Core dashboards |
| **Tail-based sampling** | At the collector, keep slow and failed traces |
| **API versioning and contract policy** | Whether and how to version; OpenAPI diffing in CI to catch breaking changes |

### When the need appears

| Item | Notes |
|---|---|
| **Keyset (cursor) pagination** | For large or infinite-scroll lists; opaque validated cursors; composite indexes |
| **Full-text search** | Replace `Contains` (`LIKE '%x%'`) with database full-text once tables are large |
| **Auditing** | Created/modified timestamps and user, set centrally in `SaveChanges` |
| **Soft delete** | One global query filter, not per feature |
| **Background jobs and messaging** | Hosted services or a job library; **outbox pattern** if events are published so database writes and messages cannot diverge |
| **Outbound HTTP resilience** | Retries, timeouts, circuit breakers via the resilience package, per client |
| **Cross-node cache invalidation** | Switch to FusionCache (backplane) if stale L1 reads after writes become a problem |
| **Aspire** | One-command local orchestration and shared service defaults, if you add more services |
| **Domain events** | In-process events dispatched after save, if features need to react to each other without coupling |
| **Output caching** | For public, non-user-specific endpoints |
| **File uploads and storage** | Blob storage abstraction, size limits, virus scanning |
| **Feature flags** | If you need gradual rollout |
| **Internationalization** | Localized error messages and formats |

### Open decisions to confirm

1. Database engine (PostgreSQL assumed)
2. Identity: external provider with JWT bearer, or ASP.NET Core Identity
3. Test framework (xUnit assumed) and assertion library
4. Handler registration: explicit list vs. one assembly scan
5. Single instance (L1 only) vs. multiple instances (L1 + Redis L2, and the staleness trade-off)
6. Grafana target: local LGTM image, Grafana Cloud, or self-hosted with a collector
7. Whether to add Aspire for local orchestration

---

## 16. Build order summary

1. Scaffold solution and projects (Phase 0)
2. Build settings, analyzers, central packages (Phase 1)
3. Install packages (Phase 2)
4. `Common/` blocks: errors, API base, observability, auth, caching, paging, data (Phase 3)
5. `Program.cs` (Phase 4)
6. Reference feature `Todos` + first migration (Phase 5)
7. Integration test infrastructure and the required cases (Phase 6)
8. Local environment with Docker Compose and Grafana (Phase 7)
9. Benchmarks and k6 load tests (Phase 8)
10. Architecture tests and `AGENTS.md` (Phase 9)
11. Work through Section 15 in priority order

When this order is followed, each phase is independently verifiable, and a new feature is the Section 14 checklist applied to a copy of `Features/Todos`.
