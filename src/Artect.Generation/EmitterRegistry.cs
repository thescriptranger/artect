using System.Collections.Generic;
using Artect.Generation.Emitters;

namespace Artect.Generation;

public static class EmitterRegistry
{
    public static IReadOnlyList<IEmitter> All() => new IEmitter[]
    {
        new ApiProblemEmitter(),
        new AppLoggerEmitter(),
        new AppSettingsEmitter(),
        new ArtectConfigEmitter(),
        new CsProjEmitter(),
        new DapperConnectionFactoryEmitter(),
        new DapperRepositoryImplEmitter(),
        new DbContextEmitter(),
        new DbFunctionsEmitter(),
        new DockerEmitter(),
        new DomainCommonEmitter(),
        new DtoEmitter(),
        new EfRepositoryEmitter(),
        new EntityBehaviorEmitter(),
        new EntityEmitter(),
        new EnumEmitter(),
        new LaunchSettingsEmitter(),
        new MapperEmitter(),
        new MigrationsEmitter(),
        new MinimalApiEndpointEmitter(),
        new PagedResponseEmitter(),
        new ProgramCsEmitter(),
        new RepoHygieneEmitter(),
        new RepositoryInterfaceEmitter(),
        new RequestEmitter(),
        new ResponseEmitter(),
        new ServiceInstallerEmitter(),
        new SlnEmitter(),
        new StoredProceduresEmitter(),
        new TestsProjectEmitter(),
        new UnitOfWorkEmitter(),
        new UseCaseInteractorEmitter(),
        new UseCaseResultEmitter(),
        new UseCaseResultExtensionsEmitter(),
        new ValidationErrorEmitter(),
        new ValidationResultEmitter(),
        new ValidatorEmitter(),
    };
}
