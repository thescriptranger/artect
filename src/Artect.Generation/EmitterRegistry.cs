using System.Collections.Generic;
using Artect.Generation.Emitters;

namespace Artect.Generation;

public static class EmitterRegistry
{
    public static IReadOnlyList<IEmitter> All() => new IEmitter[]
    {
        new ApiProblemEmitter(),
        new ApiTestsEmitter(),
        new AppSettingsEmitter(),
        new ApplicationCommonEmitter(),
        new ApplicationErrorMappersEmitter(),
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
        new EntityCommandEmitter(),
        new EntityEmitter(),
        new EntityDtoEmitter(),
        new EntityMappingsEmitter(),
        new EntityQueryEmitter(),
        new EnumEmitter(),
        new FeatureCommandsInterfaceEmitter(),
        new FeatureQueriesInterfaceEmitter(),
        new InfrastructureTestsEmitter(),
        new LaunchSettingsEmitter(),
        new MigrationsEmitter(),
        new MinimalApiEndpointEmitter(),
        new PagedResponseEmitter(),
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
