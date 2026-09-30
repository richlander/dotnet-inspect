using System.Collections.Immutable;
using System.Reflection;
using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.CSharp;

public readonly record struct CSharpAccessorDeclarationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method);

public readonly record struct CSharpAccessorDeclarationCoordinate(
    MetadataAccessorDeclarationRequest Declaration,
    MetadataAccessorSemanticsRole Role,
    MetadataMethodAddress Method);

public sealed record CSharpAccessorDeclarationPost
{
    internal CSharpAccessorDeclarationPost(
        CSharpAccessorDeclarationRequest request,
        MetadataAccessorAssociationResult association,
        CSharpAccessorDeclarationCoordinate? coordinate,
        MetadataAccessorDeclarationResult? declaration,
        MetadataTypeDeclarationResult containingType)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(containingType);
        Request = request;
        Association = association;
        Coordinate = coordinate;
        Declaration = declaration;
        ContainingType = containingType;
    }

    public CSharpAccessorDeclarationRequest Request { get; }

    public MetadataAccessorAssociationResult Association
        { get; internal init; }

    public CSharpAccessorDeclarationCoordinate? Coordinate
        { get; internal init; }

    public MetadataAccessorDeclarationResult? Declaration
        { get; internal init; }

    public MetadataTypeDeclarationResult ContainingType
        { get; internal init; }

    public static CSharpAccessorDeclarationPost Capture(
        MetadataDeclarationSession session,
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress method,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();
        var request = new CSharpAccessorDeclarationRequest(type, method);
        MetadataAccessorAssociationResult association =
            session.RelateAccessor(type, method, token);
        CSharpAccessorDeclarationCoordinate? coordinate = null;
        MetadataAccessorDeclarationResult? declaration = null;
        if (association is MetadataAccessorAssociationResult.Related related)
        {
            coordinate = new(
                new(type, related.Certificate.Declaration),
                related.Certificate.Role,
                method);
            declaration = session.PostAccessorDeclaration(
                coordinate.Value.Declaration,
                token);
        }

        return new(
            request,
            association,
            coordinate,
            declaration,
            session.PostTypeDeclaration(
                type,
                token));
    }
}

public enum CSharpAccessorDeclarationKind
{
    Property,
    Indexer,
    Event,
}

public enum CSharpAccessorBodyPolicy
{
    SelectedBody,
    SiblingStub,
}

public enum CSharpAccessorDeclarationRefusalReason
{
    UnsupportedSemanticOccurrence,
    IncompleteAccessorSet,
    MissingTargetBody,
    UnsupportedRootAttributes,
    UnsupportedAccessorSignature,
    UnsupportedAccessorCorrespondence,
    UnsupportedAccessorMultiplicity,
}

public enum CSharpAccessorDeclarationUnavailableReason
{
    AccessorAssociationAbsent,
    AccessorAssociationRejected,
    AccessorDeclarationRejected,
    ContainingTypeRejected,
    RequestMismatch,
    TargetOccurrenceMismatch,
    IdentifierUnavailable,
    TypeSpellingUnavailable,
    ParameterEvidenceIncomplete,
    AccessibilityUnavailable,
    ModifierShapeUnavailable,
    MemorySafetyUnavailable,
    OutsideInitialBoundary,
}

public sealed record CSharpAcceptedAccessorModifiers(
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsSealed);

public sealed record CSharpAcceptedAccessorBinding
{
    internal CSharpAcceptedAccessorBinding(
        MetadataAccessorSemanticsOccurrence occurrence,
        CSharpAccessorBodyPolicy bodyPolicy,
        string keyword,
        string? accessibility)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentException.ThrowIfNullOrEmpty(keyword);
        Occurrence = occurrence;
        BodyPolicy = bodyPolicy;
        Keyword = keyword;
        Accessibility = accessibility;
    }

    public MetadataAccessorSemanticsOccurrence Occurrence { get; }

    public CSharpAccessorBodyPolicy BodyPolicy { get; }

    public string Keyword { get; }

    public string? Accessibility { get; }
}

public sealed record CSharpAcceptedAccessorDeclarationRequest
{
    internal CSharpAcceptedAccessorDeclarationRequest(
        CSharpAccessorDeclarationCoordinate coordinate,
        CSharpLanguageProfile profile,
        CSharpAccessorDeclarationKind kind,
        MetadataAccessorDeclarationEvidence aggregate,
        MetadataTypeDeclarationEvidence containingType,
        string name,
        CSharpTypeSpelling type,
        ImmutableArray<CSharpParameterSpelling> parameters,
        string accessibility,
        CSharpAcceptedAccessorModifiers modifiers,
        ImmutableArray<CSharpAcceptedAccessorBinding> accessors)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(containingType);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(accessibility);
        ArgumentNullException.ThrowIfNull(modifiers);
        if (parameters.IsDefault)
        {
            throw new ArgumentException(
                "Parameters must be initialized.",
                nameof(parameters));
        }
        if (accessors.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Accessors must be initialized.",
                nameof(accessors));
        }

        Coordinate = coordinate;
        Profile = profile;
        Kind = kind;
        Aggregate = aggregate;
        ContainingType = containingType;
        Name = name;
        Type = type;
        Parameters = parameters;
        Accessibility = accessibility;
        Modifiers = modifiers;
        Accessors = accessors;
    }

    public CSharpAccessorDeclarationCoordinate Coordinate { get; }

    public CSharpLanguageProfile Profile { get; }

    public CSharpAccessorDeclarationKind Kind { get; }

    public MetadataAccessorDeclarationEvidence Aggregate { get; }

    public MetadataTypeDeclarationEvidence ContainingType { get; }

    public string Name { get; }

    public CSharpTypeSpelling Type { get; }

    public ImmutableArray<CSharpParameterSpelling> Parameters { get; }

    public string Accessibility { get; }

    public CSharpAcceptedAccessorModifiers Modifiers { get; }

    public ImmutableArray<CSharpAcceptedAccessorBinding> Accessors { get; }
}

public abstract record CSharpAccessorDeclarationRepresentabilityResult
{
    private protected CSharpAccessorDeclarationRepresentabilityResult()
    {
    }

    public sealed record Representable(
        CSharpAcceptedAccessorDeclarationRequest Request)
        : CSharpAccessorDeclarationRepresentabilityResult;

    public sealed record Unrepresentable(
        CSharpAccessorDeclarationRequest Request,
        CSharpLanguageProfile Profile,
        CSharpAccessorDeclarationRefusalReason Reason)
        : CSharpAccessorDeclarationRepresentabilityResult;

    public sealed record Unavailable(
        CSharpAccessorDeclarationRequest Request,
        CSharpLanguageProfile Profile,
        CSharpAccessorDeclarationUnavailableReason Reason)
        : CSharpAccessorDeclarationRepresentabilityResult;
}

public static class CSharpAccessorDeclarationRepresentability
{
    public static CSharpAccessorDeclarationRepresentabilityResult Decide(
        CSharpAccessorDeclarationPost post,
        CSharpLanguageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(profile);

        CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable Refuse(
            CSharpAccessorDeclarationRefusalReason reason) =>
            new(post.Request, profile, reason);
        CSharpAccessorDeclarationRepresentabilityResult.Unavailable Unavailable(
            CSharpAccessorDeclarationUnavailableReason reason) =>
            new(post.Request, profile, reason);

        if (post.Association is MetadataAccessorAssociationResult.Rejected)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorAssociationRejected);
        }
        if (post.Association is MetadataAccessorAssociationResult.Absent)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorAssociationAbsent);
        }
        if (post.Association is not
                MetadataAccessorAssociationResult.Related related
            || post.Coordinate is not
                CSharpAccessorDeclarationCoordinate coordinate
            || related.Certificate.Type != post.Request.Type
            || related.Certificate.Method != post.Request.Method
            || coordinate.Declaration.Type != post.Request.Type
            || coordinate.Declaration.Declaration
                != related.Certificate.Declaration
            || coordinate.Role != related.Certificate.Role
            || coordinate.Method != post.Request.Method)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .RequestMismatch);
        }
        if (post.Declaration is not
            MetadataAccessorDeclarationResult.Posted declaration)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorDeclarationRejected);
        }
        if (post.ContainingType is not
            MetadataTypeDeclarationResult.Posted containingType)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ContainingTypeRejected);
        }

        MetadataAccessorDeclarationEvidence aggregate =
            declaration.Evidence;
        if (aggregate.Type != coordinate.Declaration.Type
            || aggregate.Declaration
                != coordinate.Declaration.Declaration
            || containingType.Evidence.Type != aggregate.Type)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .RequestMismatch);
        }

        foreach (MetadataAccessorSemanticsOccurrence accessor
            in aggregate.Accessors)
        {
            if (!IsConventionalAccessorRole(
                    aggregate.Root,
                    accessor.Role))
            {
                continue;
            }
            if (accessor.Correspondence.OrdinaryCallable
                != MetadataAccessorOrdinaryCallableStatus.Ordinary)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedAccessorSignature);
            }
            if (accessor.Correspondence.Role.Status
                != MetadataAccessorRoleCorrespondenceStatus.Exact)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedAccessorCorrespondence);
            }
        }
        if (aggregate.Root
                is MetadataAccessorRootDeclarationEvidence.Property
            && aggregate.Correspondence.PropertyMultiplicity
                != MetadataPropertyAccessorMultiplicityStatus.Conventional)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedAccessorMultiplicity);
        }
        if (aggregate.Root
                is MetadataAccessorRootDeclarationEvidence.Event
            && aggregate.Correspondence.EventAddRemoveStaticnessMatches
                != true)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedAccessorCorrespondence);
        }

        if (aggregate.Accessors.Any(accessor =>
                accessor.Role is
                    MetadataAccessorSemanticsRole.Fire
                    or MetadataAccessorSemanticsRole.Other))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedSemanticOccurrence);
        }
        if (aggregate.Root
                is MetadataAccessorRootDeclarationEvidence.Property
            && !aggregate.Accessors.Any(accessor =>
                accessor.Role is
                    MetadataAccessorSemanticsRole.Getter
                    or MetadataAccessorSemanticsRole.Setter)
            || aggregate.Root
                is MetadataAccessorRootDeclarationEvidence.Event
                && (aggregate.Accessors.Count(accessor =>
                        accessor.Role
                            == MetadataAccessorSemanticsRole.AddOn) != 1
                    || aggregate.Accessors.Count(accessor =>
                        accessor.Role
                            == MetadataAccessorSemanticsRole.RemoveOn) != 1))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .IncompleteAccessorSet);
        }

        MetadataAccessorSemanticsOccurrence[] targets =
        [
            .. aggregate.Accessors.Where(accessor =>
                accessor.Role == coordinate.Role
                && accessor.Method.Method == coordinate.Method
                && accessor.PhysicalRowNumber
                    == related.Certificate.PhysicalRowNumber),
        ];
        if (targets.Length != 1)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .TargetOccurrenceMismatch);
        }
        MetadataAccessorSemanticsOccurrence target = targets[0];

        if (!TryGetContainingKind(
                containingType.Evidence,
                out CSharpContainingDeclarationKind containingKind)
            || containingKind == CSharpContainingDeclarationKind.Interface)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (!MemorySafetyIsSupported(aggregate))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .MemorySafetyUnavailable);
        }

        return aggregate.Root switch
        {
            MetadataAccessorRootDeclarationEvidence.Property property =>
                DecideProperty(
                    post,
                    profile,
                    aggregate,
                    containingType.Evidence,
                    containingKind,
                    property,
                    target),
            MetadataAccessorRootDeclarationEvidence.Event @event =>
                DecideEvent(
                    post,
                    profile,
                    aggregate,
                    containingType.Evidence,
                    containingKind,
                    @event,
                    target),
            _ => Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .OutsideInitialBoundary),
        };
    }

    static bool IsConventionalAccessorRole(
        MetadataAccessorRootDeclarationEvidence root,
        MetadataAccessorSemanticsRole role) =>
        root switch
        {
            MetadataAccessorRootDeclarationEvidence.Property =>
                role is MetadataAccessorSemanticsRole.Getter
                    or MetadataAccessorSemanticsRole.Setter,
            MetadataAccessorRootDeclarationEvidence.Event =>
                role is MetadataAccessorSemanticsRole.AddOn
                    or MetadataAccessorSemanticsRole.RemoveOn,
            _ => false,
        };

    static CSharpAccessorDeclarationRepresentabilityResult DecideProperty(
        CSharpAccessorDeclarationPost post,
        CSharpLanguageProfile profile,
        MetadataAccessorDeclarationEvidence aggregate,
        MetadataTypeDeclarationEvidence containingType,
        CSharpContainingDeclarationKind containingKind,
        MetadataAccessorRootDeclarationEvidence.Property property,
        MetadataAccessorSemanticsOccurrence target)
    {
        CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable Refuse(
            CSharpAccessorDeclarationRefusalReason reason) =>
            new(post.Request, profile, reason);
        CSharpAccessorDeclarationRepresentabilityResult.Unavailable Unavailable(
            CSharpAccessorDeclarationUnavailableReason reason) =>
            new(post.Request, profile, reason);

        if (aggregate.Accessors.Any(accessor =>
                accessor.Role is not (
                    MetadataAccessorSemanticsRole.Getter
                    or MetadataAccessorSemanticsRole.Setter)))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedSemanticOccurrence);
        }
        if (aggregate.Accessors.IsEmpty)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .IncompleteAccessorSet);
        }
        if (property.Attributes != PropertyAttributes.None)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedRootAttributes);
        }

        bool isIndexer =
            !property.Signature.IndexParameterTypes.IsEmpty;
        string metadataName =
            MetadataDeclarationText.RenderDeclarationName(property);
        string name;
        if (isIndexer)
        {
            if (metadataName != "Item")
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }
            name = "this";
        }
        else
        {
            if (!CSharpDeclarationRepresentability
                    .IsCompilerPreservedIdentifier(metadataName))
            {
                return Unavailable(
                    metadataName.Contains('.', StringComparison.Ordinal)
                        ? CSharpAccessorDeclarationUnavailableReason
                            .OutsideInitialBoundary
                        : CSharpAccessorDeclarationUnavailableReason
                            .IdentifierUnavailable);
            }
            name = CSharpIdentifier.Escape(metadataName);
        }

        if (!CSharpDeclarationRepresentability.TrySpellType(
                property.Signature.ValueType,
                out CSharpTypeSpelling? valueType))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .TypeSpellingUnavailable);
        }

        var parameters =
            ImmutableArray.CreateBuilder<CSharpParameterSpelling>(
                property.Signature.IndexParameterTypes.Length);
        if (isIndexer)
        {
            MetadataAccessorSemanticsOccurrence parameterSource =
                aggregate.Accessors.FirstOrDefault(accessor =>
                    accessor.Role
                        == MetadataAccessorSemanticsRole.Getter)
                ?? aggregate.Accessors[0];
            ImmutableArray<MetadataParameterDeclarationEvidence>
                parameterEvidence =
                    parameterSource.Role
                        == MetadataAccessorSemanticsRole.Getter
                    ? parameterSource.Method.Parameters
                    : [
                        .. parameterSource.Method.Parameters.Take(
                            property.Signature
                                .IndexParameterTypes.Length),
                    ];
            if (parameterEvidence.Length
                    != property.Signature.IndexParameterTypes.Length
                || parameterEvidence.Any(
                    parameter => !HasPlainParameterEvidence(parameter)))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ParameterEvidenceIncomplete);
            }

            ImmutableArray<string> names =
                CSharpDeclarationRepresentability.SpellParameterNames(
                    parameterEvidence);
            for (int index = 0;
                index < property.Signature.IndexParameterTypes.Length;
                index++)
            {
                if (!CSharpDeclarationRepresentability.TrySpellType(
                        property.Signature.IndexParameterTypes[index],
                        out CSharpTypeSpelling? parameterType))
                {
                    return Unavailable(
                        CSharpAccessorDeclarationUnavailableReason
                            .TypeSpellingUnavailable);
                }
                parameters.Add(new(parameterType, names[index]));
            }
        }

        if (aggregate.Accessors.Any(accessor =>
                !ParameterEvidenceIsComplete(accessor)))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ParameterEvidenceIncomplete);
        }
        if (aggregate.Accessors.Any(accessor =>
                !AccessorDeclarationShapeIsSupported(accessor)))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedAccessorSignature);
        }

        string[] accessibilities =
        [
            .. aggregate.Accessors.Select(accessor =>
                GetAccessibility(accessor.Method.Attributes)),
        ];
        if (accessibilities.Any(static value => value.Length == 0)
            || !CSharpAccessorDeclarationPolicy
                .TrySelectPropertyAccessibility(
                    accessibilities,
                    out string declarationAccessibility))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessibilityUnavailable);
        }

        if (!TryGetModifierShape(
                aggregate.Accessors,
                out CSharpDeclarationModifierShape modifiers)
            || !CSharpAccessorDeclarationPolicy
                .DeclarationModifiersAreRepresentable(
                    containingKind,
                    IsStaticType(containingType),
                    containingType.Attributes.HasFlag(
                        TypeAttributes.Abstract),
                    containingType.Attributes.HasFlag(
                        TypeAttributes.Sealed),
                    declarationAccessibility,
                    modifiers))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ModifierShapeUnavailable);
        }

        var bindings =
            ImmutableArray.CreateBuilder<CSharpAcceptedAccessorBinding>(
                aggregate.Accessors.Length);
        foreach (MetadataAccessorSemanticsOccurrence accessor
            in aggregate.Accessors)
        {
            string keyword;
            if (accessor.Role == MetadataAccessorSemanticsRole.Getter)
            {
                keyword = "get";
            }
            else if (!TryGetSetterKeyword(
                    accessor.Method.Signature.ReturnType,
                    out keyword))
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedAccessorSignature);
            }

            string accessorAccessibility =
                GetAccessibility(accessor.Method.Attributes);
            bindings.Add(
                new(
                    accessor,
                    accessor.PhysicalRowNumber
                            == target.PhysicalRowNumber
                        ? CSharpAccessorBodyPolicy.SelectedBody
                        : CSharpAccessorBodyPolicy.SiblingStub,
                    keyword,
                    accessorAccessibility == declarationAccessibility
                        ? null
                        : accessorAccessibility));
        }

        if (!target.Method.HasBodyRva)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .MissingTargetBody);
        }

        return new CSharpAccessorDeclarationRepresentabilityResult
            .Representable(
                new(
                    post.Coordinate!.Value,
                    profile,
                    isIndexer
                        ? CSharpAccessorDeclarationKind.Indexer
                        : CSharpAccessorDeclarationKind.Property,
                    aggregate,
                    containingType,
                    name,
                    valueType,
                    parameters.MoveToImmutable(),
                    declarationAccessibility,
                    new(
                        modifiers.IsStatic,
                        modifiers.IsVirtual,
                        modifiers.IsAbstract,
                        modifiers.IsOverride,
                        modifiers.IsSealed),
                    bindings.MoveToImmutable()));
    }

    static CSharpAccessorDeclarationRepresentabilityResult DecideEvent(
        CSharpAccessorDeclarationPost post,
        CSharpLanguageProfile profile,
        MetadataAccessorDeclarationEvidence aggregate,
        MetadataTypeDeclarationEvidence containingType,
        CSharpContainingDeclarationKind containingKind,
        MetadataAccessorRootDeclarationEvidence.Event @event,
        MetadataAccessorSemanticsOccurrence target)
    {
        CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable Refuse(
            CSharpAccessorDeclarationRefusalReason reason) =>
            new(post.Request, profile, reason);
        CSharpAccessorDeclarationRepresentabilityResult.Unavailable Unavailable(
            CSharpAccessorDeclarationUnavailableReason reason) =>
            new(post.Request, profile, reason);

        if (aggregate.Accessors.Length != 2
            || aggregate.Accessors.Count(accessor =>
                accessor.Role == MetadataAccessorSemanticsRole.AddOn) != 1
            || aggregate.Accessors.Count(accessor =>
                accessor.Role == MetadataAccessorSemanticsRole.RemoveOn) != 1)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .IncompleteAccessorSet);
        }
        if (@event.Attributes != EventAttributes.None)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedRootAttributes);
        }

        string metadataName =
            MetadataDeclarationText.RenderDeclarationName(@event);
        if (!CSharpDeclarationRepresentability
                .IsCompilerPreservedIdentifier(metadataName))
        {
            return Unavailable(
                metadataName.Contains('.', StringComparison.Ordinal)
                    ? CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary
                    : CSharpAccessorDeclarationUnavailableReason
                        .IdentifierUnavailable);
        }
        if (!CSharpDeclarationRepresentability.TrySpellType(
                @event.EventType,
                out CSharpTypeSpelling? eventType))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .TypeSpellingUnavailable);
        }
        if (aggregate.Accessors.Any(accessor =>
                !ParameterEvidenceIsComplete(accessor)))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ParameterEvidenceIncomplete);
        }
        if (aggregate.Accessors.Any(accessor =>
                !AccessorDeclarationShapeIsSupported(accessor)
                || !IsUnmodifiedVoid(
                    accessor.Method.Signature.ReturnType)))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedAccessorSignature);
        }

        string[] accessibilities =
        [
            .. aggregate.Accessors.Select(accessor =>
                GetAccessibility(accessor.Method.Attributes)),
        ];
        if (accessibilities.Any(static value => value.Length == 0)
            || accessibilities.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessibilityUnavailable);
        }
        string declarationAccessibility = accessibilities[0];

        if (!TryGetModifierShape(
                aggregate.Accessors,
                out CSharpDeclarationModifierShape modifiers)
            || !CSharpAccessorDeclarationPolicy
                .DeclarationModifiersAreRepresentable(
                    containingKind,
                    IsStaticType(containingType),
                    containingType.Attributes.HasFlag(
                        TypeAttributes.Abstract),
                    containingType.Attributes.HasFlag(
                        TypeAttributes.Sealed),
                    declarationAccessibility,
                    modifiers))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ModifierShapeUnavailable);
        }

        if (!target.Method.HasBodyRva)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .MissingTargetBody);
        }

        ImmutableArray<CSharpAcceptedAccessorBinding> bindings =
        [
            .. aggregate.Accessors.Select(accessor =>
                new CSharpAcceptedAccessorBinding(
                    accessor,
                    accessor.PhysicalRowNumber
                            == target.PhysicalRowNumber
                        ? CSharpAccessorBodyPolicy.SelectedBody
                        : CSharpAccessorBodyPolicy.SiblingStub,
                    accessor.Role == MetadataAccessorSemanticsRole.AddOn
                        ? "add"
                        : "remove",
                    accessibility: null)),
        ];
        return new CSharpAccessorDeclarationRepresentabilityResult
            .Representable(
                new(
                    post.Coordinate!.Value,
                    profile,
                    CSharpAccessorDeclarationKind.Event,
                    aggregate,
                    containingType,
                    CSharpIdentifier.Escape(metadataName),
                    eventType,
                    ImmutableArray<CSharpParameterSpelling>.Empty,
                    declarationAccessibility,
                    new(
                        modifiers.IsStatic,
                        modifiers.IsVirtual,
                        modifiers.IsAbstract,
                        modifiers.IsOverride,
                        modifiers.IsSealed),
                    bindings));
    }

    static bool TryGetContainingKind(
        MetadataTypeDeclarationEvidence type,
        out CSharpContainingDeclarationKind kind)
    {
        kind = type.Category switch
        {
            MetadataTypeDeclarationCategory.Class =>
                CSharpContainingDeclarationKind.Class,
            MetadataTypeDeclarationCategory.Struct =>
                CSharpContainingDeclarationKind.Struct,
            MetadataTypeDeclarationCategory.Interface =>
                CSharpContainingDeclarationKind.Interface,
            _ => default,
        };
        return type.Category is
            MetadataTypeDeclarationCategory.Class
            or MetadataTypeDeclarationCategory.Struct
            or MetadataTypeDeclarationCategory.Interface;
    }

    static bool MemorySafetyIsSupported(
        MetadataAccessorDeclarationEvidence aggregate) =>
        aggregate.Safety.DeclaringTypeLayout is not
            (ApiTypeLayout.Explicit or ApiTypeLayout.Extended)
        && MemorySafetyIsSupported(aggregate.Safety.Declaration)
        && aggregate.Accessors.All(accessor =>
            MemorySafetyIsSupported(accessor.MemorySafety));

    static bool MemorySafetyIsSupported(
        ApiMemberMemorySafetyFacts facts) =>
        facts.CallerContract is MemorySafetyMemberContractResult.None
        && facts.SignaturePointer == MemorySafetyPointerEvidence.Absent;

    static bool ParameterEvidenceIsComplete(
        MetadataAccessorSemanticsOccurrence accessor) =>
        HasPlainParameterEvidence(
            accessor.Method.ReturnParameter)
        && accessor.Method.Parameters.All(HasPlainParameterEvidence);

    static bool AccessorDeclarationShapeIsSupported(
        MetadataAccessorSemanticsOccurrence accessor) =>
        accessor.Method.ImplementationAttributes
            == MethodImplAttributes.IL
        && (accessor.Method.Attributes & ~SupportedAccessorAttributes) == 0
        && accessor.Method.Attributes.HasFlag(
            MethodAttributes.SpecialName)
        && accessor.Method.Attributes.HasFlag(
            MethodAttributes.HideBySig);

    static bool HasPlainParameterEvidence(
        MetadataParameterDeclarationEvidence parameter) =>
        parameter.Markers.IsComplete
        && parameter.Attributes == ParameterAttributes.None
        && parameter.Markers.IsReadOnlyCount == 0
        && parameter.Markers.RequiresLocationCount == 0
        && parameter.Markers.ParamArrayCount == 0
        && parameter.Markers.ParamCollectionCount == 0
        && parameter.Markers.ScopedRefCount == 0
        && parameter.Markers.UnscopedRefCount == 0;

    static bool TryGetModifierShape(
        ImmutableArray<MetadataAccessorSemanticsOccurrence> accessors,
        out CSharpDeclarationModifierShape modifiers)
    {
        modifiers = default;
        if (accessors.IsDefaultOrEmpty)
            return false;

        modifiers = NormalizeModifiers(
            accessors[0].Method.Attributes);
        CSharpDeclarationModifierShape expected = modifiers;
        return accessors.All(accessor =>
            NormalizeModifiers(accessor.Method.Attributes)
                == expected);
    }

    static CSharpDeclarationModifierShape NormalizeModifiers(
        MethodAttributes attributes)
    {
        bool isVirtual = attributes.HasFlag(MethodAttributes.Virtual);
        bool isAbstract = attributes.HasFlag(MethodAttributes.Abstract);
        bool isFinal = attributes.HasFlag(MethodAttributes.Final);
        bool isNewSlot = attributes.HasFlag(MethodAttributes.NewSlot);
        if (isVirtual && isFinal && isNewSlot && !isAbstract)
        {
            return new(
                attributes.HasFlag(MethodAttributes.Static),
                IsVirtual: false,
                IsAbstract: false,
                IsOverride: false,
                IsSealed: false);
        }

        return new(
            attributes.HasFlag(MethodAttributes.Static),
            isVirtual,
            isAbstract,
            isVirtual && !isNewSlot,
            isFinal);
    }

    static string GetAccessibility(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Private => "private",
            MethodAttributes.FamANDAssem => "private protected",
            MethodAttributes.Assembly => "internal",
            MethodAttributes.Family => "protected",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.Public => "public",
            _ => "",
        };

    static bool IsStaticType(MetadataTypeDeclarationEvidence type) =>
        type.Category == MetadataTypeDeclarationCategory.Class
        && type.Attributes.HasFlag(TypeAttributes.Abstract)
        && type.Attributes.HasFlag(TypeAttributes.Sealed);

    static bool TryGetSetterKeyword(
        MetadataTypeIdentity returnType,
        out string keyword)
    {
        if (IsUnmodifiedVoid(returnType))
        {
            keyword = "set";
            return true;
        }
        if (returnType is MetadataTypeIdentity.Modified
            {
                IsRequired: true,
                Modifier: MetadataTypeIdentity.Named modifier,
                Type: MetadataTypeIdentity.Primitive inner,
            }
            && MetadataDeclarationText.RenderNamespace(
                modifier.Definition)
                == "System.Runtime.CompilerServices"
            && MetadataDeclarationText.GetSegmentCount(
                modifier.Definition) == 1
            && MetadataDeclarationText.RenderSegment(
                modifier.Definition,
                0) == "IsExternalInit"
            && MetadataDeclarationText.RenderPrimitiveName(inner)
                == "void")
        {
            keyword = "init";
            return true;
        }

        keyword = "";
        return false;
    }

    static bool IsUnmodifiedVoid(MetadataTypeIdentity identity) =>
        identity is MetadataTypeIdentity.Primitive primitive
        && MetadataDeclarationText.RenderPrimitiveName(primitive)
            == "void";

    const MethodAttributes SupportedAccessorAttributes =
        MethodAttributes.MemberAccessMask
        | MethodAttributes.Static
        | MethodAttributes.Final
        | MethodAttributes.Virtual
        | MethodAttributes.HideBySig
        | MethodAttributes.VtableLayoutMask
        | MethodAttributes.Abstract
        | MethodAttributes.SpecialName;
}

public static class CSharpAcceptedAccessorDeclarationRenderer
{
    public static string RenderStub(
        CSharpAcceptedAccessorDeclarationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var head = new List<string>
        {
            request.Accessibility,
        };
        if (request.Modifiers.IsStatic)
            head.Add("static");
        else if (request.Modifiers.IsSealed)
        {
            head.Add("sealed");
            head.Add("override");
        }
        else if (request.Modifiers.IsOverride)
            head.Add("override");
        else if (request.Modifiers.IsAbstract)
            head.Add("abstract");
        else if (request.Modifiers.IsVirtual)
            head.Add("virtual");
        if (request.Kind == CSharpAccessorDeclarationKind.Event)
            head.Add("event");
        head.Add(request.Type.Source);
        head.Add(request.Kind == CSharpAccessorDeclarationKind.Indexer
            ? $"this[{string.Join(
                ", ",
                request.Parameters.Select(parameter =>
                    $"{parameter.Type.Source} {parameter.Name}"))}]"
            : request.Name);

        string accessors = string.Join(
            " ",
            request.Accessors.Select(accessor =>
                $"{(accessor.Accessibility is null
                    ? ""
                    : accessor.Accessibility + " ")}"
                + $"{accessor.Keyword} => throw null;"));
        return $"{string.Join(" ", head)} {{ {accessors} }}";
    }
}
