> ### Document additions — 2026-04-27 (post-original)
>
> Each numbered comment below is followed by an **Implementation status** block, separated from the original by a horizontal rule and prefixed with `>`. The block records what Artect emits today, where the user must extend by hand, and what is deliberately deferred. **The bold-numbered comment text is the original feedback verbatim.** Anything inside a `>` block-quote with the **Implementation status** header is an addition.
>
> Locked decisions referenced below come from `docs/superpowers/specs/2026-04-24-comments-phase-2-design.md` and the brainstorming Q&A captured there.

**1\. The domain model is too thin** 

The entities currently look more like database records than business objects. They have properties and creation methods, but the creation methods do not enforce many rules. 

For example, a customer should not be created with an empty PartyId or an empty CustomerTypeId. Dates should also be checked so that UpdatedAtUtc is not earlier than CreatedAtUtc. 

The domain layer should be responsible for these kinds of rules because they are part of the business model. 

Recommended improvement: 

**public static Result\<Customer\> Create(**   
    **Guid partyId,**   
    **Guid customerTypeId,**   
	**bool isActive,**   
    **DateTime createdAtUtc,**   
    **DateTime updatedAtUtc)**   
**{**   
	**var errors \= new List\<DomainError\>();**   
   
	**if (partyId \== Guid.Empty)**   
        **errors.Add(new DomainError(nameof(PartyId), "required", "Party is required."));**   
   
	**if (customerTypeId \== Guid.Empty)**   
        **errors.Add(new DomainError(nameof(CustomerTypeId), "required", "Customer type is required."));**   
   
	**if (updatedAtUtc \< createdAtUtc)**   
        **errors.Add(new DomainError(nameof(UpdatedAtUtc), "invalid\_date", "Updated date cannot be before created date."));**   
   
	**if (errors.Count \> 0\)**   
    	**return new Result\<Customer\>.Failure(errors);**   
   
	**return new Result\<Customer\>.Success(new Customer**   
	**{**   
        **CustomerId \= Guid.NewGuid(),**   
        **PartyId \= partyId,**   
        **CustomerTypeId \= customerTypeId,**   
        **IsActive \= isActive,**   
        **CreatedAtUtc \= createdAtUtc,**   
        **UpdatedAtUtc \= updatedAtUtc**   
	**});**   
**}** 

This is the kind of logic that should live close to the entity, not only in the API or database. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done for everything derivable from a relational schema.
>
> **What Artect emits**
> - `<Project>.Domain.Common.Result<T>` — sealed `record`, with nested `Success(T Value)` and `Failure(IReadOnlyList<DomainError> Errors)` (`DomainCommonEmitter` + `Result.cs.artect`).
> - `<Project>.Domain.Common.DomainError(string Field, string Code, string Message)` (`DomainError.cs.artect`).
> - `<Project>.Domain.Common.DomainValidationException(IReadOnlyList<DomainError>)` — thrown by generated handlers and translated to RFC-7231 `ValidationProblemDetails` (HTTP 400) by the `GlobalExceptionHandler` (see comment 8).
> - Per-entity static factory `<Entity>.Create(...)` returning `Result<<Entity>>`, with positional args restricted to non-server-generated columns (`EntityEmitter` + `Entity.cs.artect`).
>
> **Schema-derived invariants emitted in `Create`**
> - non-nullable `Guid` arg → `value == Guid.Empty` ⇒ error `"<col> is required."`
> - non-nullable `string` arg → `IsNullOrWhiteSpace(value)` ⇒ error `"<col> is required."`
> - `string` with a max length → `value.Length > N` ⇒ error `"<col> must be at most N characters."`
> - paired `Created*` + `Updated*`/`Modified*` date columns → `updated < created` ⇒ error `"<col> cannot be before <col>."`
>
> **What the user adds by hand (not inferable from schema)**
> - Domain-specific cross-property invariants (e.g., *"if `IsActive` then `ActivatedOn` must be set"*).
> - Custom factory variants taking richer inputs.
>
> Both go in `<Entity>.Hooks.cs` next to the generated `<Entity>.cs`. The generated entity is `public sealed partial class <Entity>`, so the user's partial sits beside it and shares state.

**2\. Navigation collections are too exposed** 

Several domain entities expose navigation collections directly. This makes the domain model behave like an Entity Framework data model instead of a protected business model. 

For example, exposing collections like this allows outside code to modify related records without going through business rules: 

**public ICollection\<CustomerAssociation\> CustomerAssociations { get; init; } \= new List\<CustomerAssociation\>();** 

A safer approach is to use private backing fields and expose read-only collections: 

**private readonly List\<CustomerAssociation\> \_associations \= new();**   
   
**public IReadOnlyCollection\<CustomerAssociation\> Associations \=\> \_associations.AsReadOnly();** 

Then provide methods such as: 

**public Result\<CustomerAssociation\> AssignToClub(Guid clubId, Guid roleId)**   
**{**   
	**if (clubId \== Guid.Empty)**   
    	**return new Result\<CustomerAssociation\>.Failure(\[**   
        	**new DomainError("ClubId", "required", "Club is required.")**   
        **\]);**   
   
	**// additional duplicate checks or role validation would go here**   
**}** 

This keeps the rules inside the domain object instead of allowing any layer to manipulate the object graph directly. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done.
>
> **What Artect emits** — for every collection navigation, `Entity.cs.artect` emits:
>
> ```csharp
> private readonly System.Collections.Generic.List<T> _backingField = new();
> public System.Collections.Generic.IReadOnlyCollection<T> PropertyName => _backingField;
> ```
>
> Backing-field naming preserves underscores from disambiguated nav names (a self-referencing FK produces `_billingProfiles_ChargesBillToProfileId`, not `_billingProfilesChargesBillToProfileId`) so EF Core's backing-field convention matches without a manual `HasField(...)` call.
>
> `EntityConfigurationsEmitter` emits, per nav, `builder.Navigation(e => e.Prop).UsePropertyAccessMode(PropertyAccessMode.Field);` so EF reads and writes through the backing field rather than through a public setter — outside code can read but cannot mutate.
>
> **What the user adds by hand**
> - Business methods that mutate the backing list under business rules — `AssignToClub`, `Deactivate`, etc. — go on `<Entity>.Hooks.cs`. Because the entity is `sealed partial`, the user's partial can access `_backingField` directly.
>
> **Note on `AsReadOnly()`** — the original feedback's example called `_associations.AsReadOnly()`. Artect omits the wrapper because `IReadOnlyCollection<T>` already prevents mutation through the property surface, and skipping the wrapper means EF can materialize directly into the underlying list during query execution.

**3\. Validators exist, but they are mostly empty** 

The application layer has command validators, but many of them do not currently validate anything. 

For example, a validator should not just return success by default. It should check the command before the use case runs. 

A better CreateCustomerCommand validator would look like this: 

**public ValidationResult Validate(CreateCustomerCommand request)**   
**{**   
	**var errors \= new List\<ValidationError\>();**   
   
	**if (request.PartyId \== Guid.Empty)**   
        **errors.Add(new ValidationError(nameof(request.PartyId), "PartyId is required."));**   
   
	**if (request.CustomerTypeId \== Guid.Empty)**   
        **errors.Add(new ValidationError(nameof(request.CustomerTypeId), "CustomerTypeId is required."));**   
   
	**if (request.CreatedAtUtc \== default)**   
        **errors.Add(new ValidationError(nameof(request.CreatedAtUtc), "CreatedAtUtc is required."));**   
   
	**if (request.UpdatedAtUtc \< request.CreatedAtUtc)**   
        **errors.Add(new ValidationError(nameof(request.UpdatedAtUtc), "UpdatedAtUtc cannot be before CreatedAtUtc."));**   
   
	**return errors.Count \== 0**   
    	**? ValidationResult.Success()**   
        **: ValidationResult.Fail(errors.ToArray());**   
**}** 

The application layer should validate request shape and required inputs. The domain layer should still enforce the deeper business rules. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done.
>
> **What Artect emits** — `ApiValidatorsEmitter` emits one validator per request type per entity (`Create<E>RequestValidator`, `Update<E>RequestValidator`, `Patch<E>RequestValidator`) implementing `IValidator<<Request>>`. Each runs:
> - null-request guard
> - non-nullable string → `IsNullOrWhiteSpace` ⇒ `"<col> is required."`
> - string with a max length → `Length > N` ⇒ `"<col> must be at most N characters."`
> - non-nullable Guid → `== Guid.Empty` ⇒ `"<col> is required."`
> - paired Created/Updated check
>
> Every generated validator is `public sealed partial class` with hook:
>
> ```csharp
> partial void ExtendValidate(<Request> dto, ValidationResult result);
> ```
>
> Domain-specific request-level rules go in `<Request>Validator.Hooks.cs`. The split the original feedback recommends is preserved: validators handle shape and required-input checks; the entity factory's invariants enforce the deeper business rules in the domain layer.

**4\. Repositories should not call SaveChangesAsync** 

This is one of the biggest architectural issues in the solution. 

The write repositories currently save directly to the database. At the same time, the solution also has a Unit of Work and transaction behavior. These two approaches are competing. 

A repository should usually stage changes. The use case, through the Unit of Work, should decide when to commit. 

Current pattern: 

**await \_db.Customers.AddAsync(entity, ct);**   
**await \_db.SaveChangesAsync(ct);** 

Recommended pattern: 

**public async Task\<Customer\> AddAsync(Customer entity, CancellationToken ct)**   
**{**   
	**await \_db.Customers.AddAsync(entity, ct);**   
	**return entity;**   
**}** 

Then the transaction behavior should commit once after the use case completes successfully: 

**var result \= await \_inner.ExecuteAsync(request, ct);**   
   
**if (result is UseCaseResult\<CustomerModel\>.Success)**   
**{**   
	**await \_unitOfWork.CommitAsync(ct);**   
**}** 

This becomes very important when a use case needs to save multiple related objects. For example, creating a customer, assigning the customer to a club, and creating a membership should either all succeed or all fail together. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done.
>
> **Components**
> - `IUnitOfWork.CommitAsync(CancellationToken ct)` in `<Project>.Application.Abstractions` (`UnitOfWorkEmitter`).
> - `EfUnitOfWork(<Project>DbContext db) : IUnitOfWork` in `<Project>.Infrastructure.Data` — the **only** place `SaveChangesAsync` is called in the generated tree.
> - Per-entity `<E>Repository` emits `AddAsync` / `ApplyChanges` / `Remove` that *only stage* — never call `SaveChangesAsync` (`RepositoryEmitter`).
> - Each generated `<Verb><E>Handler` injects `I<E>Repository` + `IUnitOfWork`, stages, then calls `unitOfWork.CommitAsync(ct)` exactly once after staging (`HandlerEmitter`).
> - DI: `InfrastructureDiEmitter` registers `services.AddScoped<IUnitOfWork, EfUnitOfWork>();` plus the per-entity repository and read-service.
> - Endpoints inject the handler — they never see `IUnitOfWork`.
>
> **Multi-aggregate flows** (the *"create customer + assign to club + create membership"* example): the user writes a hand-rolled `IRequestHandler<TCommand, TResult>` (see comment 5) that takes multiple `I<E>Repository` instances + one `IUnitOfWork`, stages all changes across them, and commits once. Because Artect generates a single DbContext per project, a single `EfUnitOfWork` covers every staged change, so multi-aggregate atomicity comes for free without any distributed-transaction machinery.

**5\. Use cases should represent business actions, not tables** 

The current use cases are mostly CRUD-oriented. That is expected from generated code, but it is not enough for a real customer module. 

Instead of only having use cases like: 

CreateCustomer   
GetCustomerById   
ListCustomers   
CreateMembership   
CreateBillingProfile 

The solution should introduce business-level use cases such as: 

RegisterCustomer   
AssignCustomerToClub   
CreateHouseholdMembership   
AddDependentToMembership   
CreateBillingProfileForMembership   
DeactivateCustomer   
ChangePrimaryBillingProfile 

These use cases better describe what the system actually does from a business perspective. 

The CRUD use cases can still exist for simple reference tables, but the core module should not be designed around one use case per database table. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ⚠️ Partial — extension points only. Locked decision: **don't infer business actions from schema**.
>
> **What Artect emits**
> - Per-CRUD-op handlers: `Create<E>Handler` / `Update<E>Handler` / `Patch<E>Handler` / `Delete<E>Handler`. Each is `public sealed partial class` with `partial void` hooks the user implements in a sibling `<Verb><E>Handler.Hooks.cs`:
>   - `OnBeforeAdd(command, entity)` — Create only
>   - `OnBeforeApplyChanges(command, existing, replacement)` — Update / Patch only
>   - `OnBeforeRemove(entity)` — Delete only
>   - `OnBeforeCommit(command, entity)` — every verb
>   - `OnAfterCommit(command, entity)` — every verb
> - `IRequestHandler<TCommand, TResult>` interface in `<Project>.Application.UseCases` — the extension point for hand-written cross-aggregate operations:
>
>   ```csharp
>   public interface IRequestHandler<TCommand, TResult>
>   {
>       Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
>   }
>   ```
>
> **Why we don't generate `RegisterCustomer` / `AssignCustomerToClub` / etc.**
> The schema does not encode that *"registration"* means *"create customer + create primary billing profile + enroll into default club"*. A generator that invented those flows would have to either fabricate business logic or guess — both destroy more value than they create. The user writes (for example) `RegisterCustomerHandler : IRequestHandler<RegisterCustomerCommand, CustomerDto>` in `Application/UseCases/`, injects whatever repos + `IUnitOfWork` it needs, stages across them, commits once, and registers it in `Application/DependencyInjection.cs`.
>
> **CRUD use cases continue to exist** for reference tables, exactly per the original feedback's last paragraph: *"The CRUD use cases can still exist for simple reference tables."* They become the persistence-only seam underneath user-written business handlers.

**6\. Repository interfaces are too mechanical** 

The solution has many read and write repository interfaces, often one pair per entity. This can create a lot of noise without adding much design value. 

A better approach is to define repositories around aggregates and business concepts. 

For example: 

**public interface ICustomerRepository**   
**{**   
	**Task\<Customer?\> GetByIdAsync(Guid customerId, CancellationToken ct);**   
	**Task AddAsync(Customer customer, CancellationToken ct);**   
	**Task\<bool\> ExistsForPartyAsync(Guid partyId, CancellationToken ct);**   
**}** 

For read-heavy screens and lists, use query services or read models instead of forcing every query through a repository. 

**public interface ICustomerReadService**   
**{**   
	**Task\<PagedResult\<CustomerSummaryModel\>\> SearchAsync(CustomerSearchCriteria criteria, CancellationToken ct);**   
**}** 

That creates a cleaner split: 

Repositories: write-side aggregate persistence   
Query services: read-side projections and search screens 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done.
>
> **What Artect emits per non-join, primary-keyed entity**
> - `I<E>Repository` (write-side, in `Application/Features/<Plural>/Abstractions/`):
>   - `Task<<E>?> GetByIdAsync(<PkType> id, CancellationToken ct)` — for write-side existence checks before update
>   - `Task<bool> ExistsAsync(<PkType> id, CancellationToken ct)`
>   - `Task<bool> ExistsBy<UniqueProp>Async(<Type> value, CancellationToken ct)` — one per *single-column non-PK* UNIQUE constraint
>   - `Task AddAsync(<E> entity, CancellationToken ct)` (only if `Post` is in the CRUD set)
>   - `void ApplyChanges(<E> existing, <E> replacement)` (only if `Put` or `Patch`)
>   - `void Remove(<E> entity)` (only if `Delete`)
> - `<E>Repository : I<E>Repository` (Infrastructure) — `sealed`; stages-only against the DbContext.
> - `I<E>ReadService` (read-side, in `Application/Features/<Plural>/Abstractions/`):
>   - `Task<(IReadOnlyList<<E>Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct)`
>   - `Task<<E>Dto?> GetByIdAsync(<PkType> id, CancellationToken ct)`
> - `<E>ReadService : I<E>ReadService` (Infrastructure) — `AsNoTracking()` + LINQ projection direct to `<E>Dto`. Never returns the domain entity.
>
> **Why the original `ICustomerRepository` and `ICustomerReadService` examples landed where they did**
> The original suggested `ExistsForPartyAsync(Guid partyId, ...)`. Artect emits `ExistsBy<X>Async` *only* for single-column non-PK UNIQUE constraints — that's the surface the schema can guarantee. If `Customer.PartyId` carries a UNIQUE constraint, you'll get `ExistsByPartyIdAsync` automatically. If it doesn't (it's just an FK with cardinality enforced elsewhere), the user adds the helper as a partial method on `<E>Repository` (the implementation class is generated as `sealed partial class <E>Repository` precisely so this is friction-free).
>
> Multi-column UNIQUE constraints and PK-overlapping uniques are deliberately skipped — the user can hand-write their semantics on the partial.
>
> **Why we kept `GetByIdAsync` on the repository in addition to the read service**
> The repository's `GetByIdAsync` returns the domain entity (for write-side existence checks before update). The read service's `GetByIdAsync` returns the projected `<E>Dto` (for HTTP responses). They serve different layers and don't overlap.

**7\. EF Core configuration should be split into configuration classes** 

The DbContext contains a large amount of entity configuration inside OnModelCreating. This works technically, but it becomes hard to maintain as the system grows. 

A cleaner approach is to create one configuration class per entity: 

**public sealed class CustomerConfiguration : IEntityTypeConfiguration\<Customer\>**   
**{**   
	**public void Configure(EntityTypeBuilder\<Customer\> builder)**   
	**{**   
        **builder.ToTable("Customer", "dbo");**   
   
        **builder.HasKey(e \=\> e.CustomerId);**   
   
        **builder.Property(e \=\> e.CustomerId)**   
        	**.ValueGeneratedNever();**   
   
        **builder.Property(e \=\> e.PartyId)**   
        	**.IsRequired();**   
   
        **builder.Property(e \=\> e.CustomerTypeId)**   
        	**.IsRequired();**   
   
        **builder.Property(e \=\> e.CreatedAtUtc)**   
        	**.IsRequired();**   
   
        **builder.HasIndex(e \=\> e.PartyId);**   
	**}**   
**}** 

Then the DbContext can simply do this: 

**protected override void OnModelCreating(ModelBuilder modelBuilder)**   
**{**   
    **modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmokeV1DbContext).Assembly);**   
**}** 

This keeps the DbContext smaller and makes entity mappings easier to review. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done.
>
> **What Artect emits** — `EntityConfigurationsEmitter` produces one file per entity at `Infrastructure/Data/Configurations/<E>Configuration.cs`. Each implements `IEntityTypeConfiguration<<E>>` and emits:
> - `ToTable(name, schema)`
> - `HasKey(...)` + `ValueGeneratedOnAdd()` for server-generated single-column PKs (composite PKs and `HasNoKey()` for keyless tables and views are handled too)
> - column → property maps via `HasColumnName`
> - per reference nav: `HasOne(...).WithMany(<collectionPropOnTarget>).HasForeignKey(...).IsRequired(<bool>)`
> - per nav (reference and collection): `Navigation(...).UsePropertyAccessMode(PropertyAccessMode.Field)` — this is what makes the encapsulated-collection pattern from comment 2 work
> - per UNIQUE constraint: `HasAlternateKey(...).HasName(<constraintName>)`
> - per index: `HasIndex(...).HasDatabaseName(<indexName>)` with `.IsUnique()` if applicable
> - per CHECK constraint: `ToTable(tb => tb.HasCheckConstraint(<name>, <expr>))`
>
> **`DbContext.OnModelCreating` is now slim**:
>
> ```csharp
> protected override void OnModelCreating(ModelBuilder modelBuilder)
> {
>     // sequences (one block per HasSequence call)
>     modelBuilder.HasSequence<long>("...", "dbo").StartsAt(...).IncrementsBy(...);
>
>     modelBuilder.ApplyConfigurationsFromAssembly(typeof(<Project>DbContext).Assembly);
>
>     // views stay inline — they're keyless and don't fit the IEntityTypeConfiguration shape
>     modelBuilder.Entity<View>(b => { b.ToView(...); b.HasNoKey(); /* column maps */ });
>
>     base.OnModelCreating(modelBuilder);
> }
> ```
>
> No per-entity blocks remain in the DbContext. Views stay inline because they're keyless and don't enumerate cleanly through `EntityConfigurationsEmitter`.

**8\. The API needs more production-level concerns** 

The API maps endpoints and starts the application, but it is missing several concerns that should be present in a production-ready service. 

At minimum, I would expect to see, but this will be tackled by JD Framework: 

Global exception handling   
ProblemDetails responses   
Authentication   
Authorization   
CORS policy   
Health checks   
Structured logging   
Correlation IDs   
OpenTelemetry tracing   
API versioning   
Rate limiting, if needed 

The API project should act as the composition root. It should wire in security, diagnostics, middleware, endpoint registration, and infrastructure dependencies. 

A more complete setup would include something like: 

**builder.Services.AddProblemDetails();**   
**builder.Services.AddExceptionHandler\<GlobalExceptionHandler\>();**   
   
**builder.Services.AddAuthentication();**   
**builder.Services.AddAuthorization();**   
   
**builder.Services.AddHealthChecks();**   
   
**var app \= builder.Build();**   
   
**app.UseExceptionHandler();**   
**app.UseHttpsRedirection();**   
**app.UseAuthentication();**   
**app.UseAuthorization();**   
   
**app.MapHealthChecks("/health");**   
**app.MapApplicationEndpoints();** 

Endpoint groups should also require authorization where appropriate. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ⚠️ Partial — Artect emits the parts that aren't deferred. Deliberate split, mirroring the original feedback's note *"this will be tackled by JD Framework"*.
>
> **What Artect emits** (`ProductionMiddlewareEmitter` + `ProgramCsEmitter`)
> - **Global exception handling** — `GlobalExceptionHandler : IExceptionHandler`. Catches `DomainValidationException` ⇒ HTTP 400 `ValidationProblemDetails` with errors keyed by `DomainError.Field`; everything else ⇒ HTTP 500 `ProblemDetails` (with `Detail` populated only in `Development`).
> - **`ProblemDetails`** — `builder.Services.AddProblemDetails()` + `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()` + `app.UseExceptionHandler()` wired in `Program.cs`.
> - **Health checks** — `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/health")`.
> - **Correlation IDs** — `CorrelationIdMiddleware` (reads `X-Correlation-ID` header or generates one, stamps `httpContext.TraceIdentifier`) + `app.UseMiddleware<CorrelationIdMiddleware>()`.
>
> **Deferred to JD Framework** (locked decision Q3 = B in the Phase 1/2 spec)
> - 🚫 Authentication (`AddAuthentication`, scheme config, `app.UseAuthentication()`)
> - 🚫 Authorization (`AddAuthorization`, policies, `[Authorize]` per endpoint group)
> - 🚫 CORS policy
> - 🚫 Structured logging (Serilog/etc.) — Artect uses default `ILogger<T>` only
> - 🚫 OpenTelemetry tracing
> - 🚫 API versioning (the wizard exposes a versioning question, but the implementation is cosmetic until JD Framework lands the cross-cutting wiring)
> - 🚫 Rate limiting
>
> **Why this split** — these concerns vary across deployment targets in ways the schema can't predict (auth scheme depends on identity provider, CORS depends on which origins, OTel depends on which collector, etc.). JD Framework owns the cross-cutting wiring at the org level so individual generated services don't drift from the company-wide policy.

**9\. Some generated names need cleanup** 

There are names such as: 

ApprovalStatu   
CommissionStatu   
EmploymentStatu   
MaritalStatu 

These names should be corrected. This may seem minor, but naming matters in a domain model. The code should use business language that developers and business users can both understand. 

These should become: 

ApprovalStatus   
CommissionStatus   
EmploymentStatus   
MaritalStatus 

Generated naming issues should be fixed at the generation metadata level so they do not keep coming back after regeneration. 

---

> ### Implementation status — added 2026-04-27 (post-original)
>
> **Status:** ✅ Done — fixed at the generation-metadata level, exactly per the original feedback's last paragraph.
>
> **What Artect changed** — `Pluralizer.Singularize` (`src/Artect.Naming/Pluralizer.cs`) now guards a list of "already-singular" suffixes (`us`, `is`, `as`, `os`) against the trailing-`s` strip rule. Words where the trailing `s` is part of the *singular* form, not a plural marker, are no longer mauled:
>
> | Input | Before | After |
> |---|---|---|
> | `Status` | `Statu` | `Status` |
> | `Atlas` | `Atla` | `Atlas` |
> | `Crisis` | `Crisi` | `Crisis` |
> | `Bus` | `Bu` | `Bus` |
> | `Octopus` | `Octopu` | `Octopus` |
> | `Bonus` | `Bonu` | `Bonus` |
>
> Because the rule lives in the singularization pass itself (not in a per-project naming-corrections override), these names cannot recur after regeneration. Per-project naming overrides remain available in `artect.yaml` for cases the generic rule can't detect (industry-specific abbreviations, legacy column conventions, etc.).
