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
    public static string RepositoryInterfacePath(string root, string entityName) => $"{ApplicationDir(root)}/Abstractions/Repositories/I{entityName}Repository.cs";
    public static string RepositoryReadInterfacePath(string root, string entityName) => $"{ApplicationDir(root)}/Abstractions/Repositories/I{entityName}ReadRepository.cs";
    public static string RepositoryWriteInterfacePath(string root, string entityName) => $"{ApplicationDir(root)}/Abstractions/Repositories/I{entityName}WriteRepository.cs";
    public static string RepositoryImplPath(string root, string entityName) => $"{InfrastructureDir(root)}/Repositories/{entityName}Repository.cs";
    public static string RepositoryReadImplPath(string root, string entityName) => $"{InfrastructureDir(root)}/Repositories/{entityName}ReadRepository.cs";
    public static string RepositoryWriteImplPath(string root, string entityName) => $"{InfrastructureDir(root)}/Repositories/{entityName}WriteRepository.cs";
    public static string DbContextPath(string root, string className) => $"{InfrastructureDir(root)}/Data/{className}.cs";
    public static string ConnectionFactoryPath(string root) => $"{InfrastructureDir(root)}/Data/SqlDbConnectionFactory.cs";
    public static string EndpointPath(string root, string plural) => $"{ApiDir(root)}/Endpoints/{plural}Endpoints.cs";
    public static string ProgramCsPath(string root) => $"{ApiDir(root)}/Program.cs";
    public static string AppSettingsPath(string root) => $"{ApiDir(root)}/appsettings.json";
    public static string LaunchSettingsPath(string root) => $"{ApiDir(root)}/Properties/launchSettings.json";
    public static string SharedRequestPath(string root, string entityName, string kind) => $"{SharedDir(root)}/Requests/{kind}{entityName}Request.cs";
    public static string SharedResponsePath(string root, string entityName) => $"{SharedDir(root)}/Responses/{entityName}Response.cs";
    public static string SharedPagedResponsePath(string root) => $"{SharedDir(root)}/Responses/PagedResponse.cs";
    public static string SharedEnumPath(string root, string enumName) => $"{SharedDir(root)}/Enums/{enumName}.cs";
    public static string SharedErrorPath(string root, string className) => $"{SharedDir(root)}/Errors/{className}.cs";
    public static string SprocInterfacePath(string root, string name) => $"{ApplicationDir(root)}/StoredProcedures/{name}.cs";
    public static string DbFunctionsInterfacePath(string root) => $"{ApplicationDir(root)}/StoredProcedures/IDbFunctions.cs";

    // Use-case paths
    public static string UseCaseResultPath(string root) => $"{ApplicationDir(root)}/UseCases/UseCaseResult.cs";
    public static string UseCaseResultExtensionsPath(string root) => $"{ApiDir(root)}/UseCaseResultExtensions.cs";
    /// <param name="opName">Full operation name already including entity, e.g. "ListUsers", "GetUserById".</param>
    public static string UseCaseInterfacePath(string root, string opName) => $"{ApplicationDir(root)}/Abstractions/UseCases/I{opName}UseCase.cs";
    /// <param name="opName">Full operation name already including entity, e.g. "ListUsers", "GetUserById".</param>
    public static string UseCaseImplPath(string root, string opName) => $"{ApplicationDir(root)}/UseCases/{opName}UseCase.cs";

    public static string PortsPath(string root, string className) => $"{ApplicationDir(root)}/Abstractions/Ports/{className}.cs";
    public static string PortsNamespace(string root) => $"{root}.Application.Abstractions.Ports";
    public static string LoggingPath(string root, string className) => $"{InfrastructureDir(root)}/Logging/{className}.cs";
    public static string LoggingNamespace(string root) => $"{root}.Infrastructure.Logging";

    // DI installer extensions
    public static string ApplicationInstallerPath(string root) => $"{ApplicationDir(root)}/DependencyInjection/ApplicationServiceCollectionExtensions.cs";
    public static string InfrastructureInstallerPath(string root) => $"{InfrastructureDir(root)}/DependencyInjection/InfrastructureServiceCollectionExtensions.cs";

    public static string DomainCommonPath(string root, string className) => $"{DomainDir(root)}/Common/{className}.cs";
    public static string DomainCommonNamespace(string root) => $"{root}.Domain.Common";

    // Application-internal types (Phase C)
    public static string ApplicationCommonPath(string root, string className) => $"{ApplicationDir(root)}/Common/{className}.cs";
    public static string ApplicationCommonNamespace(string root) => $"{root}.Application.Common";
    public static string ApplicationErrorsPath(string root, string className) => $"{ApplicationDir(root)}/Errors/{className}.cs";
    public static string ApplicationErrorsNamespace(string root) => $"{root}.Application.Errors";
    public static string ApplicationCommandsPath(string root, string className) => $"{ApplicationDir(root)}/Commands/{className}.cs";
    public static string ApplicationCommandsNamespace(string root) => $"{root}.Application.Commands";
    public static string ApplicationQueriesPath(string root, string className) => $"{ApplicationDir(root)}/Queries/{className}.cs";
    public static string ApplicationQueriesNamespace(string root) => $"{root}.Application.Queries";
    public static string ApplicationModelsPath(string root, string className) => $"{ApplicationDir(root)}/Models/{className}.cs";
    public static string ApplicationModelsNamespace(string root) => $"{root}.Application.Models";

    public static string EntityBehaviorPath(string root, string entityName) => $"{DomainDir(root)}/Entities/{entityName}.Behavior.cs";

    public static string ApiNamespace(string root) => $"{root}.Api";
    public static string ApplicationNamespace(string root) => $"{root}.Application";
    public static string DomainNamespace(string root) => $"{root}.Domain";
    public static string InfrastructureNamespace(string root) => $"{root}.Infrastructure";
    public static string SharedNamespace(string root) => $"{root}.Shared";

    // Phase D additions
    public static string SharedRequestsNamespace(string root) => $"{root}.Shared.Requests";
    public static string SharedResponsesNamespace(string root) => $"{root}.Shared.Responses";
    public static string SharedErrorsNamespace(string root) => $"{root}.Shared.Errors";
    public static string ApiMapperPath(string root, string className) => $"{ApiDir(root)}/Mapping/{className}.cs";
    public static string ApiMappingNamespace(string root) => $"{root}.Api.Mapping";
}
