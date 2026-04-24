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
        new CsProjEmitter(),
        new DapperConnectionFactoryEmitter(),
        new DbContextEmitter(),
        new DbFunctionsEmitter(),
        new DockerEmitter(),
        new DomainCommonEmitter(),
        new DomainTestsEmitter(),
        new DtoEmitter(),
        new EntityBehaviorEmitter(),
        new EntityCommandEmitter(),
        new EntityEmitter(),
        new EntityDtoEmitter(),
        new EntityQueryEmitter(),
        new EnumEmitter(),
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
        new ValidatorEmitter(),
    };
}
