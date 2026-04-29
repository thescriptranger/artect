namespace Artect.Generation;

public static class CleanLayout
{
    public static string ApiProjectName(string root) => $"{root}.Api";
    public static string ApplicationProjectName(string root) => $"{root}.Application";
    public static string DomainProjectName(string root) => $"{root}.Domain";
    public static string InfrastructureProjectName(string root) => $"{root}.Infrastructure";
    public static string SharedProjectName(string root) => $"{root}.Shared";
    public static string TestsProjectName(string root) => $"{root}.IntegrationTests";

    public static string ApiDir(string root) => $"src/{ApiProjectName(root)}";
    public static string ApplicationDir(string root) => $"src/{ApplicationProjectName(root)}";
    public static string DomainDir(string root) => $"src/{DomainProjectName(root)}";
    public static string InfrastructureDir(string root) => $"src/{InfrastructureProjectName(root)}";
    public static string SharedDir(string root) => $"src/{SharedProjectName(root)}";
    public static string TestsDir(string root) => $"tests/{TestsProjectName(root)}";

    public static string EntityPath(string root, string entityName) => $"{DomainDir(root)}/Entities/{entityName}.cs";
    public static string DtoPath(string root, string entityName) => $"{ApplicationDir(root)}/Dtos/{entityName}Dto.cs";
    public static string ValidatorPath(string root, string className) => $"{ApplicationDir(root)}/Validators/{className}.cs";
    public static string MapperPath(string root, string entityName) => $"{ApplicationDir(root)}/Mappings/{entityName}Mappings.cs";
    public static string DbContextPath(string root, string className) => $"{InfrastructureDir(root)}/Data/{className}.cs";
    public static string ConnectionFactoryPath(string root) => $"{InfrastructureDir(root)}/Data/SqlDbConnectionFactory.cs";
    public static string EndpointPath(string root, string plural) => $"{ApiDir(root)}/Endpoints/{plural}Endpoints.cs";
    public static string ProgramCsPath(string root) => $"{ApiDir(root)}/Program.cs";
    public static string AppSettingsPath(string root) => $"{ApiDir(root)}/appsettings.json";
    public static string LaunchSettingsPath(string root) => $"{ApiDir(root)}/Properties/launchSettings.json";
    public static string SharedRequestPath(string root, string entityName, string kind) => $"{SharedDir(root)}/Requests/{kind}{entityName}Request.cs";
    public static string SharedResponsePath(string root, string entityName) => $"{SharedDir(root)}/Responses/{entityName}Response.cs";
    public static string SharedSummaryResponsePath(string root, string entityName) => $"{SharedDir(root)}/Responses/{entityName}SummaryResponse.cs";
    public static string SharedPagedResponsePath(string root) => $"{SharedDir(root)}/Responses/PagedResponse.cs";
    public static string SharedEnumPath(string root, string enumName) => $"{SharedDir(root)}/Enums/{enumName}.cs";
    public static string SharedErrorPath(string root, string className) => $"{SharedDir(root)}/Errors/{className}.cs";
    // V#7: stored-procedure typed wrappers live in Infrastructure (persistence is an
    // infrastructure concern). Application abstractions for the same data must be
    // hand-written as business-named ports (e.g., ICustomerRiskReader) and adapted in
    // Infrastructure to call the typed wrappers below.
    public static string InfrastructureStoredProceduresPath(string root, string name) => $"{InfrastructureDir(root)}/StoredProcedures/{name}.cs";
    public static string InfrastructureStoredProceduresNamespace(string root) => $"{root}.Infrastructure.StoredProcedures";

    // DI installer extensions
    public static string ApplicationInstallerPath(string root) => $"{ApplicationDir(root)}/DependencyInjection/ApplicationServiceCollectionExtensions.cs";
    public static string InfrastructureInstallerPath(string root) => $"{InfrastructureDir(root)}/DependencyInjection/InfrastructureServiceCollectionExtensions.cs";

    public static string DomainCommonPath(string root, string className) => $"{DomainDir(root)}/Common/{className}.cs";
    public static string DomainCommonNamespace(string root) => $"{root}.Domain.Common";

    // Phase 2 — Application layer abstractions and use-case extension points
    public static string ApplicationAbstractionsPath(string root, string className) =>
        $"{ApplicationDir(root)}/Abstractions/{className}.cs";

    public static string ApplicationAbstractionsNamespace(string root) =>
        $"{root}.Application.Abstractions";

    public static string ApplicationUseCasesPath(string root, string className) =>
        $"{ApplicationDir(root)}/UseCases/{className}.cs";

    public static string ApplicationUseCasesNamespace(string root) =>
        $"{root}.Application.UseCases";

    // Phase 2 — DtoMapper moves from Infrastructure/Mapping to Application/Mappings
    // so handlers in Application can call it without a downward layer reference.
    public static string ApplicationMappingsPath(string root, string className) =>
        $"{ApplicationDir(root)}/Mappings/{className}.cs";

    public static string ApplicationMappingsNamespace(string root) =>
        $"{root}.Application.Mappings";

    // Application-internal types (Phase C)
    public static string ApplicationDtosPath(string root, string className) => $"{ApplicationDir(root)}/Dtos/{className}.cs";
    public static string ApplicationDtosNamespace(string root) => $"{root}.Application.Dtos";

    public static string EntityBehaviorPath(string root, string entityName) => $"{DomainDir(root)}/Entities/{entityName}.Behavior.cs";
    public static string EntityDomainEventsPath(string root, string entityName) => $"{DomainDir(root)}/Entities/{entityName}.DomainEvents.cs";

    public static string ApiNamespace(string root) => $"{root}.Api";
    public static string ApplicationNamespace(string root) => $"{root}.Application";
    public static string DomainNamespace(string root) => $"{root}.Domain";
    public static string InfrastructureNamespace(string root) => $"{root}.Infrastructure";
    public static string SharedNamespace(string root) => $"{root}.Shared";

    // Phase D additions
    public static string SharedRequestsNamespace(string root) => $"{root}.Shared.Requests";
    public static string SharedResponsesNamespace(string root) => $"{root}.Shared.Responses";
    public static string SharedErrorsNamespace(string root) => $"{root}.Shared.Errors";

    // Phase D — Feature-layer helpers (IT Director GeneratedAPI layout)
    public static string ApplicationFeaturePath(string root, string entityTypeName, string className) =>
        $"{ApplicationDir(root)}/Features/{Pluralize(entityTypeName)}/{className}.cs";

    public static string ApplicationFeatureNamespace(string root, string entityTypeName) =>
        $"{root}.Application.Features.{Pluralize(entityTypeName)}";

    public static string ApplicationFeatureAbstractionsPath(string root, string entityTypeName, string className) =>
        $"{ApplicationDir(root)}/Features/{Pluralize(entityTypeName)}/Abstractions/{className}.cs";

    public static string ApplicationFeatureAbstractionsNamespace(string root, string entityTypeName) =>
        $"{root}.Application.Features.{Pluralize(entityTypeName)}.Abstractions";

    // Phase G — Api Mappings helpers
    public static string ApiMappingsPath(string root, string className) =>
        $"{ApiDir(root)}/Mappings/{className}.cs";

    public static string ApiMappingsNamespace(string root) =>
        $"{root}.Api.Mappings";

    // Phase F — Validator helpers
    public static string ApplicationValidatorsPath(string root, string className) =>
        $"{ApplicationDir(root)}/Validators/{className}.cs";

    public static string ApplicationValidatorsNamespace(string root) =>
        $"{root}.Application.Validators";

    public static string ApiValidatorsPath(string root, string className) =>
        $"{ApiDir(root)}/Validators/{className}.cs";

    public static string ApiValidatorsNamespace(string root) =>
        $"{root}.Api.Validators";

    // Phase E — Infrastructure helpers
    public static string InfrastructureDataEntityPath(string root, string entityTypeName, string className) =>
        $"{InfrastructureDir(root)}/Data/{Pluralize(entityTypeName)}/{className}.cs";

    public static string InfrastructureDataEntityNamespace(string root, string entityTypeName) =>
        $"{root}.Infrastructure.Data.{Pluralize(entityTypeName)}";

    // EF Core IEntityTypeConfiguration<T> classes
    public static string InfrastructureConfigurationsPath(string root, string className) =>
        $"{InfrastructureDir(root)}/Data/Configurations/{className}.cs";

    public static string InfrastructureConfigurationsNamespace(string root) =>
        $"{root}.Infrastructure.Data.Configurations";

    static string Pluralize(string entityTypeName) =>
        Artect.Naming.CasingHelper.ToPascalCase(
            Artect.Naming.Pluralizer.Pluralize(entityTypeName),
            corrections: null);
}
