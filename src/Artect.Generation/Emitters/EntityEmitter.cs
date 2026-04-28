using System.Collections.Generic;
using System.Text;
using Artect.Config;
using Artect.Generation.Models;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#16 canonical thin emitter: schema interpretation lives in
/// <see cref="EntityCodeModelBuilder"/>, paths in <see cref="CleanLayout"/>,
/// rendering in <see cref="Renderer"/>. This file owns only the loop that ties
/// those collaborators together. Adding new entity-rendering logic should grow
/// the model + builder, not this emitter.
/// </summary>
public sealed class EntityEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Entity.cs.artect"));
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.OwnedEntity, EntityClassification.ReadModel, EntityClassification.LookupData)) continue;

            var model = EntityCodeModelBuilder.Build(ctx, entity);
            list.Add(new EmittedFile(
                CleanLayout.EntityPath(ctx.Config.ProjectName, entity.EntityTypeName),
                Renderer.Render(template, model)));

            if (ctx.Config.EnableDomainEvents && entity.Classification == EntityClassification.AggregateRoot)
            {
                list.Add(new EmittedFile(
                    CleanLayout.EntityDomainEventsPath(ctx.Config.ProjectName, entity.EntityTypeName),
                    BuildDomainEventsPartial(ctx, entity)));
            }
        }
        return list;
    }

    static string BuildDomainEventsPartial(EmitterContext ctx, NamedEntity entity)
    {
        var ns = $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities";
        var commonNs = CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName);
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {entity.EntityTypeName} : {commonNs}.IHasDomainEvents");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly System.Collections.Generic.List<{commonNs}.IDomainEvent> _domainEvents = new();");
        sb.AppendLine();
        sb.AppendLine($"    public System.Collections.Generic.IReadOnlyCollection<{commonNs}.IDomainEvent> DomainEvents => _domainEvents;");
        sb.AppendLine();
        sb.AppendLine("    public void ClearDomainEvents() => _domainEvents.Clear();");
        sb.AppendLine();
        sb.AppendLine($"    private void RaiseDomainEvent({commonNs}.IDomainEvent @event) => _domainEvents.Add(@event);");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
