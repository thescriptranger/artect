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
    public static string RepositoryImplPath(string root, string entityName) => $"{InfrastructureDir(root)}/Repositories/{entityName}Repository.cs";
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

    public static string ApiNamespace(string root) => $"{root}.Api";
    public static string ApplicationNamespace(string root) => $"{root}.Application";
    public static string DomainNamespace(string root) => $"{root}.Domain";
    public static string InfrastructureNamespace(string root) => $"{root}.Infrastructure";
    public static string SharedNamespace(string root) => $"{root}.Shared";
}
