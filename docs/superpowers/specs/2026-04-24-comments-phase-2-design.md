# Comments-driven V1 finalization — Phase 2 design

**Date:** 2026-04-24
**Status:** Approved for planning. Awaiting Phase 1 completion before execution.
**Source:** `docs/comments.md` — code-review feedback on the V1 generated output.
**Phase 1 sister-spec:** Phase 1 (comments 1, 2, 3, 7, 9 + the production-middleware subset of 8) is being implemented as direct emitter edits without a separate design doc; the comments doc is its specification.

---

## 1. Scope

Phase 2 covers the three structural comments that cannot be reduced to mechanical emitter edits:

- **Comment 4** — Repositories must not call `SaveChangesAsync`. A `IUnitOfWork` commits once per use case.
- **Comment 5** — Use cases should represent business actions, not tables. Generator emits a clean extension-point pattern instead of inferring business actions from the schema.
- **Comment 6** — Repository interfaces are too mechanical. Reshape `I<Entity>Queries + I<Entity>Commands + <Entity>DataAccess` into `I<Entity>Repository` (write side) + `I<Entity>ReadService` (read side).

A fourth structural change — aggregate-root classification (which entities should even get endpoints) — is **deferred but binding**. It is the next major piece of work after Phase 2 ships and is tracked in `docs/superpowers/specs/2026-04-24-aggregate-roots-brainstorm.md`. Phase 2 does *not* gate any tables out; every non-join-table entity with a primary key continues to get a repository, read service, handlers, and endpoints. Aggregate-root classification will later flip emission off for child entities; the shape introduced by Phase 2 is the shape that survives that flip.

## 2. Locked decisions (Q&A from brainstorming)

| Question | Decision |
|---|---|
| Q1 — Comment 5 use cases | **C.** Don't generate business-action stubs from schema. Emit clean extension points: a `partial void OnBeforeCommit(...)` hook on each generated handler + an `IRequestHandler<TCommand, TResult>` interface in `Application/UseCases/` for hand-written use cases. |
| Q2 — Comment 6 repos | **B.** Reshape now (repo + read-service per entity); defer aggregate-root classification as a binding obligation for the next release. |
| Q3 — Comment 8 production concerns | **B.** Emit global exception handler + ProblemDetails + `/health` + correlation-ID middleware in Phase 1. CORS / auth / OTel / structured logging / rate limiting belong to JD Framework. |
| Q4 — Comment 4 commit point | **B.** Per-entity use-case `Handler` classes orchestrate `IRepository` + `IUnitOfWork`. No pipeline behavior, no endpoint filter. |

## 3. Target shape per entity (after Phase 2)

For every non-join-table entity `<Entity>` with a primary key:

```
src/<Project>.Application/Features/<Plural>/
├── Abstractions/
│   ├── I<Entity>Repository.cs        # write-side aggregate persistence
│   └── I<Entity>ReadService.cs       # read-side projections / search
├── Create<Entity>Command.cs          # positional record (unchanged from today)
├── Update<Entity>Command.cs          # positional record (unchanged from today)
├── Patch<Entity>Command.cs           # positional record (unchanged from today)
├── Create<Entity>Handler.cs          # NEW — sealed partial; injects repo + uow
├── Update<Entity>Handler.cs          # NEW — sealed partial; injects repo + uow
├── Patch<Entity>Handler.cs           # NEW — sealed partial; injects repo + uow
└── Delete<Entity>Handler.cs          # NEW — sealed partial; injects repo + uow

src/<Project>.Application/Abstractions/
└── IUnitOfWork.cs                    # NEW — single shared interface

src/<Project>.Application/UseCases/
├── IRequestHandler.cs                # NEW — generic IRequestHandler<TCommand, TResult>
└── ExtensionPoint.cs                 # NEW — README pointer for hand-written use cases

src/<Project>.Infrastructure/Data/<Plural>/
├── <Entity>Repository.cs             # NEW — sealed; implements I<Entity>Repository
└── <Entity>ReadService.cs            # NEW — sealed; implements I<Entity>ReadService

src/<Project>.Infrastructure/Data/
└── EfUnitOfWork.cs                   # NEW — sealed; wraps DbContext.SaveChangesAsync
```

The current `<Entity>DataAccess.cs` and the two interface files (`I<Entity>Queries.cs`, `I<Entity>Commands.cs`) are **deleted** in Phase 2. The `CommandRecordsEmitter` stays as-is.

## 4. Component shapes

### 4.1 `IUnitOfWork`

```csharp
namespace <Project>.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken ct);
}
```

### 4.2 `EfUnitOfWork`

```csharp
namespace <Project>.Infrastructure.Data;

public sealed class EfUnitOfWork(<Project>DbContext db) : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
```

### 4.3 `I<Entity>Repository`

Per-entity. Aggregate-shaped — small surface, oriented around the entity, not the table.

```csharp
public interface I<Entity>Repository
{
    Task<<Entity>?> GetByIdAsync(<PkType> id, CancellationToken ct);
    Task AddAsync(<Entity> entity, CancellationToken ct);
    void Update(<Entity> entity);
    void Remove(<Entity> entity);
    Task<bool> ExistsAsync(<PkType> id, CancellationToken ct);
    // Optional: ExistsBy<UniqueProperty>Async per non-PK unique constraint
}
```

`Add` / `Update` / `Remove` only **stage** changes against the DbContext. They never call `SaveChangesAsync`.

`ExistsBy<UniqueProperty>Async` helpers are emitted **one per non-PK unique constraint** detected during introspection (e.g., `ExistsByEmailAsync` if `Email` has a UNIQUE constraint). This is the small surface that lets handlers enforce uniqueness without leaking EF Core into Application.

### 4.4 `I<Entity>ReadService`

Per-entity. Read-side projections, never returns the domain entity directly.

```csharp
public interface I<Entity>ReadService
{
    Task<(IReadOnlyList<<Entity>Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct);
    Task<<Entity>Dto?> GetByIdAsync(<PkType> id, CancellationToken ct);
}
```

The implementing class projects directly to `<Entity>Dto` using `Select` over `AsNoTracking()` — same projection logic as today's `<Entity>DataAccess.GetPagedAsync` / `GetByIdAsync`, just split out.

### 4.5 Generated handlers

Sealed partials with extension-point hooks. Skeleton:

```csharp
namespace <Project>.Application.Features.<Plural>;

public sealed partial class Create<Entity>Handler(
    I<Entity>Repository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<<Entity>Dto> HandleAsync(Create<Entity>Command command, CancellationToken ct)
    {
        var result = <Entity>.Create(/* command fields */);
        if (result is Result<<Entity>>.Failure failure)
            throw new DomainValidationException(failure.Errors);

        var entity = ((Result<<Entity>>.Success)result).Value;

        OnBeforeAdd(command, entity);             // partial hook
        await repository.AddAsync(entity, ct);
        OnBeforeCommit(command, entity);          // partial hook
        await unitOfWork.CommitAsync(ct);
        OnAfterCommit(command, entity);           // partial hook

        return DtoMapper.Map<<Entity>, <Entity>Dto>(entity);
    }

    partial void OnBeforeAdd(Create<Entity>Command command, <Entity> entity);
    partial void OnBeforeCommit(Create<Entity>Command command, <Entity> entity);
    partial void OnAfterCommit(Create<Entity>Command command, <Entity> entity);
}
```

`Update` / `Patch` / `Delete` follow the same pattern. The user adds business logic in `Create<Entity>Handler.Hooks.cs` (a hand-written sibling file) by implementing the partial methods.

`DomainValidationException` is a single new type in `<Project>.Domain.Common`; it carries the `IReadOnlyList<DomainError>` from `Result<T>.Failure` and is translated to a 400 response by the global exception handler installed in Phase 1.

### 4.6 `IRequestHandler<TCommand, TResult>`

A neutral interface for hand-written use cases that don't map to a single entity:

```csharp
namespace <Project>.Application.UseCases;

public interface IRequestHandler<TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}
```

Generated handlers don't *implement* this interface — they're free-standing partials so the user can extend them via partials without dealing with interface seams. `IRequestHandler` is only for hand-written cross-aggregate operations (`RegisterCustomer`, `AssignCustomerToClub`, …). The user wires their own `*Handler` into DI; Artect doesn't try to discover them.

## 5. Endpoint shape after Phase 2

Endpoints inject *handlers* (write side) and *read services* (read side):

```csharp
group.MapPost("/", async (
    Create<Entity>Request request,
    Create<Entity>Handler handler,
    IValidator<Create<Entity>Request> validator,
    CancellationToken ct) =>
{
    var validation = validator.Validate(request);
    if (!validation.IsValid) return validation.ToBadRequest();

    var command = new Create<Entity>Command(/* request fields */);
    var dto = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/<route>/{dto.<PkProp>}", dto.ToResponse());
});

group.MapGet("/", async (
    I<Entity>ReadService reads,
    CancellationToken ct,
    int page = 1, int pageSize = 50) => /* ... */);
```

Endpoints never touch `IUnitOfWork` directly. The handler always commits.

## 6. Emitter changes

**Removed:**
- `DataAccessEmitter`
- `FeatureCommandsInterfaceEmitter`
- `FeatureQueriesInterfaceEmitter`

**New:**
- `RepositoryInterfaceEmitter` → `Application/Features/<Plural>/Abstractions/I<Entity>Repository.cs`
- `ReadServiceInterfaceEmitter` → `Application/Features/<Plural>/Abstractions/I<Entity>ReadService.cs`
- `RepositoryEmitter` → `Infrastructure/Data/<Plural>/<Entity>Repository.cs`
- `ReadServiceEmitter` → `Infrastructure/Data/<Plural>/<Entity>ReadService.cs`
- `HandlerEmitter` → `Application/Features/<Plural>/{Create,Update,Patch,Delete}<Entity>Handler.cs`
- `UnitOfWorkEmitter` → `Application/Abstractions/IUnitOfWork.cs` + `Infrastructure/Data/EfUnitOfWork.cs`
- `RequestHandlerEmitter` → `Application/UseCases/IRequestHandler.cs`
- `DomainValidationExceptionEmitter` (or extend `DomainCommonEmitter`) → `Domain/Common/DomainValidationException.cs`

**Modified:**
- `MinimalApiEndpointEmitter` — endpoints inject `<Verb><Entity>Handler` + `I<Entity>ReadService` instead of `I<Entity>Commands` + `I<Entity>Queries`.
- `InfrastructureDiEmitter` — register `I<Entity>Repository → <Entity>Repository`, `I<Entity>ReadService → <Entity>ReadService`, `IUnitOfWork → EfUnitOfWork`. Drop the two `<Entity>DataAccess` registrations.
- `ApplicationDiEmitter` — register all four handlers per entity (`AddScoped<Create<Entity>Handler>()` etc.). Handlers are concrete classes, not interface-bound.

## 7. CleanLayout helpers needed

Add to `CleanLayout.cs`:
- `ApplicationAbstractionsPath(root, className)` → `src/<Root>.Application/Abstractions/<className>.cs`
- `ApplicationAbstractionsNamespace(root)` → `<Root>.Application.Abstractions`
- `ApplicationUseCasesPath(root, className)` → `src/<Root>.Application/UseCases/<className>.cs`
- `ApplicationUseCasesNamespace(root)` → `<Root>.Application.UseCases`
- (Repository / ReadService paths reuse the existing `ApplicationFeatureAbstractionsPath` and `InfrastructureDataEntityPath`.)

## 8. Test-project impact

The `IntegrationTests` project today exercises `<Entity>DataAccess` against an in-memory or real SQL Server. After Phase 2, tests target `<Entity>Repository` and `<Entity>ReadService` separately, with `EfUnitOfWork` driving commit. The tests-emitter generates the new shape; the existing tests-emitter changes are mechanical once the new emitters are in place.

## 9. Validation posture

Phase 2 is structural and crosses three projects (Application, Infrastructure, Api). The validation gate is the same as Phase 1: the regenerated `out/SmokeIT9` solution must `dotnet build` with zero errors and produce a working endpoint surface. There are no automated tests against the tool itself in V1; that remains a V1.1 candidate per the original Artect spec.

## 10. Risks

- **Mapper breakage** — `DtoMapper.Map<Entity, EntityDto>` is used in handlers but lives in `Infrastructure/Mapping` today; if a handler in Application layer tries to use it, the dependency direction breaks Clean. **Mitigation:** move `DtoMapper` to `Application/Mappings` *before* Phase 2 starts, or have the read-service do the projection inline (it already does) and the handler return the entity → `<Entity>Dto` mapping via a stateless static in Application.
- **PropertyAccessMode for collections** — Phase 1 changes the entity's collection navigations to private-backed `IReadOnlyCollection`. The DbContext config in Phase 1 already calls `UsePropertyAccessMode(PropertyAccessMode.Field)` for navigations; Phase 2 must preserve that when the per-entity `IEntityTypeConfiguration` classes are generated.
- **Unique-constraint detection** — `ExistsBy<X>Async` helpers depend on the introspection layer surfacing UNIQUE constraints (it does — `UniqueConstraintsReader`). The risk is naming: a multi-column unique constraint must produce one helper named after the column tuple, not one helper per column. **Mitigation:** for V1, emit `ExistsByXAsync` only for **single-column** unique constraints; multi-column uniques produce no helper (the user can hand-write them via the handler partial hook).

## 11. Out of scope for Phase 2 (V1.1 candidates)

- Aggregate-root classification (the binding next-release obligation).
- `IRequestHandler<TCommand, TResult>` automatic discovery / DI registration.
- Cross-aggregate transactions spanning multiple `IUnitOfWork` instances (single DbContext means a single UoW today; multi-context scenarios are out of scope).
- Soft-delete, optimistic-concurrency tokens, audit trails — all hand-written today via the partial hooks.
