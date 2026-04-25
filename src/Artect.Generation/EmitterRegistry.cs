using System.Collections.Generic;
using Artect.Generation.Emitters;

namespace Artect.Generation;

public static class EmitterRegistry
{
    public static IReadOnlyList<IEmitter> All() => new IEmitter[]
    {
        new ApiDiEmitter(),
        new ApiProblemEmitter(),
        new ApiTestsEmitter(),
        new AppSettingsEmitter(),
        new ApplicationDiEmitter(),
        new ApplicationTestsEmitter(),
        new ArtectConfigEmitter(),
        new CommandRecordsEmitter(),
        new CsProjEmitter(),
        new DataAccessEmitter(),
        new DapperConnectionFactoryEmitter(),
        new DbContextEmitter(),
        new DtoMapperEmitter(),
        new DbFunctionsEmitter(),
        new DockerEmitter(),
        new DomainCommonEmitter(),
        new DomainTestsEmitter(),
        new DtoEmitter(),
        new EntityBehaviorEmitter(),
        new EntityConfigurationsEmitter(),
        new EntityEmitter(),
        new EntityDtoEmitter(),
        new EntityMappingsEmitter(),
        new EndpointRegistrationEmitter(),
        new EnumEmitter(),
        new FeatureCommandsInterfaceEmitter(),
        new FeatureQueriesInterfaceEmitter(),
        new InfrastructureDiEmitter(),
        new InfrastructureTestsEmitter(),
        new LaunchSettingsEmitter(),
        new MigrationsEmitter(),
        new MinimalApiEndpointEmitter(),
        new PagedResponseEmitter(),
        new ProductionMiddlewareEmitter(),
        new ProgramCsEmitter(),
        new RepoHygieneEmitter(),
        new RequestEmitter(),
        new ResponseEmitter(),
        new SlnEmitter(),
        new StoredProceduresEmitter(),
        new ValidationErrorEmitter(),
        new ValidationResultEmitter(),
        new ApiValidatorsEmitter(),
        new ValidationResultExtensionsEmitter(),
    };
}
