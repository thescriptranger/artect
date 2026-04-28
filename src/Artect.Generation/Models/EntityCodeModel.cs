using System.Collections.Generic;

namespace Artect.Generation.Models;

/// <summary>
/// V#16 canonical example: typed code model that <see cref="Emitters.EntityEmitter"/>
/// hands to the templating engine. Replaces the inline anonymous-typed dictionary
/// the emitter used to build, so the model can be constructed and inspected
/// independently of rendering. New emitters should follow this pattern: declare
/// a sealed record per logical output, build it through a pure static
/// <c>*CodeModelBuilder</c>, then render in the emitter's <c>Emit</c> method.
///
/// Property names must match the variables referenced in <c>Entity.cs.artect</c>
/// (the templating engine resolves names via reflection over public properties).
/// </summary>
public sealed record EntityCodeModel(
    bool HasUsingNamespaces,
    IReadOnlyList<string> UsingNamespaces,
    string Namespace,
    string DomainCommonNamespace,
    string EntityName,
    bool EmitBehavior,
    string SetterModifier,
    bool EmitUpdateMethod,
    bool EmitSoftDelete,
    string SoftDeleteAssignment,
    IReadOnlyList<EntityColumnView> Columns,
    bool HasReferenceNavigations,
    IReadOnlyList<EntityReferenceNavigationView> ReferenceNavigations,
    bool HasCollectionNavigations,
    IReadOnlyList<EntityCollectionNavigationView> CollectionNavigations,
    IReadOnlyList<EntityArgView> CreateArgs,
    IReadOnlyList<InvariantLine> Invariants,
    IReadOnlyList<EntityArgView> UpdateArgs,
    IReadOnlyList<InvariantLine> UpdateInvariants);

public sealed record EntityColumnView(string ClrTypeWithNullability, string PropertyName, string Initializer);

public sealed record EntityReferenceNavigationView(string TypeName, string PropertyName);

public sealed record EntityCollectionNavigationView(string TypeName, string PropertyName, string BackingField);

public sealed record EntityArgView(string ClrTypeWithNullability, string ParamName, string PropertyName, string Comma);

public sealed record InvariantLine(string Line);
