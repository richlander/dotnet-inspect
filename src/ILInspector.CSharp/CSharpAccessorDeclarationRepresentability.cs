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

public sealed record CSharpAccessorDeclarationRelationshipPost
{
    internal CSharpAccessorDeclarationRelationshipPost(
        CSharpMethodImplementationPost implementation,
        MetadataAccessorAssociationResult? association,
        MetadataAccessorDeclarationResult? declaration)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        Implementation = implementation;
        Association = association;
        Declaration = declaration;
    }

    public CSharpMethodImplementationPost Implementation
        { get; internal init; }

    public MetadataAccessorAssociationResult? Association
        { get; internal init; }

    public MetadataAccessorDeclarationResult? Declaration
        { get; internal init; }
}

public sealed record CSharpAccessorImplementationPost
{
    internal CSharpAccessorImplementationPost(
        MetadataAccessorSemanticsOccurrence accessor,
        CSharpMethodDeclarationPost method,
        ImmutableArray<CSharpAccessorDeclarationRelationshipPost>
            declarations)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(method);
        if (declarations.IsDefault)
        {
            throw new ArgumentException(
                "Declaration relationships must be initialized.",
                nameof(declarations));
        }

        Accessor = accessor;
        Method = method;
        Declarations = declarations;
    }

    public MetadataAccessorSemanticsOccurrence Accessor
        { get; internal init; }

    public CSharpMethodDeclarationPost Method
        { get; internal init; }

    public ImmutableArray<CSharpAccessorDeclarationRelationshipPost>
        Declarations
        { get; internal init; }
}

public sealed record CSharpAccessorDeclarationPost
{
    internal CSharpAccessorDeclarationPost(
        CSharpAccessorDeclarationRequest request,
        MetadataAccessorAssociationResult association,
        CSharpAccessorDeclarationCoordinate? coordinate,
        MetadataAccessorDeclarationResult? declaration,
        MetadataTypeDeclarationResult containingType,
        ImmutableArray<CSharpAccessorImplementationPost> implementations)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(containingType);
        if (implementations.IsDefault)
        {
            throw new ArgumentException(
                "Accessor implementations must be initialized.",
                nameof(implementations));
        }
        Request = request;
        Association = association;
        Coordinate = coordinate;
        Declaration = declaration;
        ContainingType = containingType;
        Implementations = implementations;
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

    public ImmutableArray<CSharpAccessorImplementationPost> Implementations
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
        var implementations =
            ImmutableArray.CreateBuilder<CSharpAccessorImplementationPost>();
        if (association is MetadataAccessorAssociationResult.Related related)
        {
            coordinate = new(
                new(type, related.Certificate.Declaration),
                related.Certificate.Role,
                method);
            declaration = session.PostAccessorDeclaration(
                coordinate.Value.Declaration,
                token);
            if (declaration is
                MetadataAccessorDeclarationResult.Posted posted)
            {
                foreach (MetadataAccessorSemanticsOccurrence accessor
                    in posted.Evidence.Accessors)
                {
                    if (!IsConventionalRole(
                            posted.Evidence.Root,
                            accessor.Role))
                    {
                        continue;
                    }

                    CSharpMethodDeclarationPost methodPost =
                        CSharpMethodDeclarationPost.Capture(
                            session,
                            type,
                            accessor.Method.Method,
                            token);
                    var declarations = ImmutableArray.CreateBuilder<
                        CSharpAccessorDeclarationRelationshipPost>(
                            methodPost.ImplementationOccurrences.Length);
                    foreach (CSharpMethodImplementationPost implementation
                        in methodPost.ImplementationOccurrences)
                    {
                        MetadataAccessorAssociationResult?
                            declarationAssociation = null;
                        MetadataAccessorDeclarationResult?
                            declarationAggregate = null;
                        if (implementation.Relationship.Definition
                            is MetadataDeclarationDefinitionDisposition
                                .LocalResolved local)
                        {
                            declarationAssociation =
                                session.RelateAccessor(
                                    local.Owner,
                                    local.Definition,
                                    token);
                            if (declarationAssociation is
                                MetadataAccessorAssociationResult.Related
                                    declarationRelated)
                            {
                                declarationAggregate =
                                    session.PostAccessorDeclaration(
                                        new(
                                            local.Owner,
                                            declarationRelated.Certificate
                                                .Declaration),
                                        token);
                            }
                        }

                        declarations.Add(
                            new(
                                implementation,
                                declarationAssociation,
                                declarationAggregate));
                    }
                    implementations.Add(
                        new(
                            accessor,
                            methodPost,
                            declarations.MoveToImmutable()));
                }
            }
        }

        return new(
            request,
            association,
            coordinate,
            declaration,
            session.PostTypeDeclaration(
                type,
                token),
            implementations.ToImmutable());

        static bool IsConventionalRole(
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
    UnsupportedExplicitInterfaceMultiplicity,
    UnsupportedExplicitInterfaceComposition,
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
    MethodImplementationRejected,
    MethodImplementationMismatch,
    ExplicitInterfaceOwnerRejected,
    InterfaceImplementationUnavailable,
    DeclarationAccessorAssociationUnavailable,
    ExplicitInterfaceDeclarationRejected,
    ExplicitInterfaceEvidenceMismatch,
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
        string? accessibility,
        CSharpAcceptedExplicitAccessorRelationship? explicitInterface = null)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentException.ThrowIfNullOrEmpty(keyword);
        Occurrence = occurrence;
        BodyPolicy = bodyPolicy;
        Keyword = keyword;
        Accessibility = accessibility;
        ExplicitInterface = explicitInterface;
    }

    public MetadataAccessorSemanticsOccurrence Occurrence { get; }

    public CSharpAccessorBodyPolicy BodyPolicy { get; }

    public string Keyword { get; }

    public string? Accessibility { get; }

    public CSharpAcceptedExplicitAccessorRelationship? ExplicitInterface
        { get; }
}

public sealed record CSharpAcceptedExplicitAccessorRelationship
{
    internal CSharpAcceptedExplicitAccessorRelationship(
        MetadataMethodImplementationCertificate implementation,
        MetadataAccessorAssociationCertificate declaration)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentNullException.ThrowIfNull(declaration);
        Implementation = implementation;
        Declaration = declaration;
    }

    public MetadataMethodImplementationCertificate Implementation { get; }

    public MetadataAccessorAssociationCertificate Declaration { get; }
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
        ImmutableArray<CSharpAcceptedAccessorBinding> accessors,
        MetadataAccessorDeclarationEvidence? explicitInterfaceAggregate =
            null,
        MetadataTypeIdentity? explicitInterfaceIdentity = null,
        CSharpTypeSpelling? explicitInterface = null)
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
        bool hasExplicitInterfaceAggregate =
            explicitInterfaceAggregate is not null;
        bool hasExplicitInterfaceIdentity =
            explicitInterfaceIdentity is not null;
        bool hasExplicitInterfaceSpelling =
            explicitInterface is not null;
        if (hasExplicitInterfaceAggregate
                != hasExplicitInterfaceIdentity
            || hasExplicitInterfaceIdentity
                != hasExplicitInterfaceSpelling)
        {
            throw new ArgumentException(
                "Explicit-interface evidence must be complete.");
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
        ExplicitInterfaceAggregate = explicitInterfaceAggregate;
        ExplicitInterfaceIdentity = explicitInterfaceIdentity;
        ExplicitInterface = explicitInterface;
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

    public MetadataAccessorDeclarationEvidence? ExplicitInterfaceAggregate
        { get; }

    public MetadataTypeIdentity? ExplicitInterfaceIdentity { get; }

    public CSharpTypeSpelling? ExplicitInterface { get; }
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
    sealed record ExplicitInterfaceContext(
        MetadataAccessorDeclarationEvidence Aggregate,
        MetadataTypeIdentity Identity,
        CSharpTypeSpelling Spelling,
        ImmutableDictionary<
            int,
            CSharpAcceptedExplicitAccessorRelationship> Relationships);

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

        CSharpAccessorDeclarationRepresentabilityResult?
            explicitInterfaceFailure =
                TryComposeExplicitInterface(
                    post,
                    profile,
                    aggregate,
                    containingType.Evidence,
                    out ExplicitInterfaceContext? explicitInterface);
        if (explicitInterfaceFailure is not null)
            return explicitInterfaceFailure;

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
                    target,
                    explicitInterface),
            MetadataAccessorRootDeclarationEvidence.Event @event =>
                DecideEvent(
                    post,
                    profile,
                    aggregate,
                    containingType.Evidence,
                    containingKind,
                    @event,
                    target,
                    explicitInterface),
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

    static CSharpAccessorDeclarationRepresentabilityResult?
        TryComposeExplicitInterface(
            CSharpAccessorDeclarationPost post,
            CSharpLanguageProfile profile,
            MetadataAccessorDeclarationEvidence aggregate,
            MetadataTypeDeclarationEvidence containingType,
            out ExplicitInterfaceContext? context)
    {
        context = null;

        CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable Refuse(
            CSharpAccessorDeclarationRefusalReason reason) =>
            new(post.Request, profile, reason);
        CSharpAccessorDeclarationRepresentabilityResult.Unavailable Unavailable(
            CSharpAccessorDeclarationUnavailableReason reason) =>
            new(post.Request, profile, reason);

        MetadataAccessorSemanticsOccurrence[] accessors =
        [
            .. aggregate.Accessors.Where(accessor =>
                IsConventionalAccessorRole(
                    aggregate.Root,
                    accessor.Role)),
        ];
        if (post.Implementations.Length != accessors.Length)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .MethodImplementationMismatch);
        }

        bool hasAbsent = false;
        bool hasRelated = false;
        for (int index = 0; index < accessors.Length; index++)
        {
            MetadataAccessorSemanticsOccurrence accessor = accessors[index];
            CSharpAccessorImplementationPost implementation =
                post.Implementations[index];
            if (implementation.Accessor.PhysicalRowNumber
                    != accessor.PhysicalRowNumber
                || implementation.Accessor.Role != accessor.Role
                || implementation.Accessor.Method.Method
                    != accessor.Method.Method
                || implementation.Method.Request.Type
                    != post.Request.Type
                || implementation.Method.Request.Method
                    != accessor.Method.Method
                || implementation.Method.Method is not
                    MetadataMethodDeclarationResult.Posted method
                || method.Evidence.Type != post.Request.Type
                || method.Evidence.Method != accessor.Method.Method
                || method.Evidence.Signature
                    != accessor.Method.Signature)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .MethodImplementationMismatch);
            }

            switch (implementation.Method.Implementations)
            {
                case MetadataMethodImplementationResult.Rejected:
                    return Unavailable(
                        CSharpAccessorDeclarationUnavailableReason
                            .MethodImplementationRejected);
                case MetadataMethodImplementationResult.Absent:
                    hasAbsent = true;
                    if (!implementation.Method
                            .ImplementationOccurrences.IsEmpty
                        || !implementation.Declarations.IsEmpty)
                    {
                        return Unavailable(
                            CSharpAccessorDeclarationUnavailableReason
                                .MethodImplementationMismatch);
                    }
                    break;
                case MetadataMethodImplementationResult.Related:
                    hasRelated = true;
                    break;
                default:
                    return Unavailable(
                        CSharpAccessorDeclarationUnavailableReason
                            .MethodImplementationMismatch);
            }
        }

        if (!hasRelated)
            return null;
        if (hasAbsent)
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .ExplicitInterfaceEvidenceMismatch);
        }

        MetadataTypeDefinitionAddress? sharedOwner = null;
        MetadataTypeIdentity? sharedIdentity = null;
        MetadataAccessorDeclarationAddress? sharedDeclaration = null;
        MetadataAccessorDeclarationEvidence? sharedAggregate = null;
        var relationships = ImmutableDictionary.CreateBuilder<
            int,
            CSharpAcceptedExplicitAccessorRelationship>();

        for (int index = 0; index < accessors.Length; index++)
        {
            MetadataAccessorSemanticsOccurrence accessor = accessors[index];
            CSharpAccessorImplementationPost implementation =
                post.Implementations[index];
            var related = (MetadataMethodImplementationResult.Related)
                implementation.Method.Implementations;
            if (related.Relationships.Length != 1)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedExplicitInterfaceMultiplicity);
            }
            if (implementation.Method.ImplementationOccurrences.Length != 1
                || implementation.Declarations.Length != 1)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .MethodImplementationMismatch);
            }

            MetadataMethodImplementationCertificate relationship =
                related.Relationships[0];
            CSharpAccessorDeclarationRelationshipPost declarationPost =
                implementation.Declarations[0];
            CSharpMethodImplementationPost occurrence =
                implementation.Method.ImplementationOccurrences[0];
            if (occurrence.Relationship != relationship
                || declarationPost.Implementation != occurrence
                || relationship.Type != post.Request.Type
                || relationship.Body != accessor.Method.Method)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .MethodImplementationMismatch);
            }

            if (relationship.Definition is not
                MetadataDeclarationDefinitionDisposition.LocalResolved local)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }
            if (occurrence.DeclarationOwner is not
                MetadataTypeDeclarationResult.Posted owner
                || owner.Evidence.Category
                    != MetadataTypeDeclarationCategory.Interface)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceOwnerRejected);
            }

            MetadataNamedTypeIdentity? ownerDefinition =
                relationship.DeclarationOwner switch
                {
                    MetadataTypeIdentity.Named named => named.Definition,
                    MetadataTypeIdentity.GenericInstance generic =>
                        generic.Definition,
                    _ => null,
                };
            if (owner.Evidence.Type != local.Owner
                || ownerDefinition is null
                || owner.Evidence.DefinitionIdentity != ownerDefinition
                || relationship.SpecialName
                    is not MetadataSpecialNameEvidence.KnownTrue)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceEvidenceMismatch);
            }

            if (occurrence.InterfaceRequest is not
                    MetadataInterfaceImplementationRequest interfaceRequest
                || interfaceRequest.Type != relationship.Type
                || interfaceRequest.Interface
                    != relationship.DeclarationOwner
                || occurrence.InterfaceResult is not
                    MetadataInterfaceImplementationResult.Related
                        interfaceResult)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .InterfaceImplementationUnavailable);
            }
            if (interfaceResult.Relationships.Length != 1)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedExplicitInterfaceMultiplicity);
            }
            MetadataInterfaceImplementationCertificate interfaceCertificate =
                interfaceResult.Relationships[0];
            if (interfaceCertificate.Type != relationship.Type
                || interfaceCertificate.Interface
                    != relationship.DeclarationOwner)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceEvidenceMismatch);
            }

            if (declarationPost.Association is not
                    MetadataAccessorAssociationResult.Related
                        declarationAssociation)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .DeclarationAccessorAssociationUnavailable);
            }
            MetadataAccessorAssociationCertificate association =
                declarationAssociation.Certificate;
            if (association.Type != local.Owner
                || association.Method != local.Definition
                || association.Role != accessor.Role)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceEvidenceMismatch);
            }
            if (declarationPost.Declaration is not
                    MetadataAccessorDeclarationResult.Posted
                        declaration
                || declaration.Evidence.Type != local.Owner
                || declaration.Evidence.Declaration
                    != association.Declaration)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceDeclarationRejected);
            }

            if (sharedOwner is null)
            {
                sharedOwner = local.Owner;
                sharedIdentity = relationship.DeclarationOwner;
                sharedDeclaration = association.Declaration;
                sharedAggregate = declaration.Evidence;
            }
            else if (sharedOwner.Value != local.Owner
                || sharedIdentity != relationship.DeclarationOwner
                || sharedDeclaration!.Value != association.Declaration)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedExplicitInterfaceComposition);
            }

            if (!CSharpDeclarationRepresentability
                    .NamedTypeSpellingsAreUnambiguous(
                        containingType.PrimitiveAlias
                            ?? containingType.OpenSelfIdentity,
                        relationship.DeclarationOwner,
                        accessor.Method.Signature))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }

            relationships.Add(
                accessor.PhysicalRowNumber,
                new(relationship, association));
        }

        if (sharedAggregate is null
            || sharedIdentity is null
            || !RootKindsMatch(aggregate.Root, sharedAggregate.Root)
            || !aggregate.Accessors
                .Select(accessor => accessor.Role)
                .Order()
                .SequenceEqual(
                    sharedAggregate.Accessors
                        .Select(accessor => accessor.Role)
                        .Order()))
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedExplicitInterfaceComposition);
        }
        if (!CSharpDeclarationRepresentability.TrySpellType(
                sharedIdentity,
                out CSharpTypeSpelling? spelling))
        {
            return Unavailable(
                CSharpAccessorDeclarationUnavailableReason
                    .TypeSpellingUnavailable);
        }

        context = new(
            sharedAggregate,
            sharedIdentity,
            spelling,
            relationships.ToImmutable());
        return null;

        static bool RootKindsMatch(
            MetadataAccessorRootDeclarationEvidence body,
            MetadataAccessorRootDeclarationEvidence declaration) =>
            body is MetadataAccessorRootDeclarationEvidence.Property
                    bodyProperty
                && declaration
                    is MetadataAccessorRootDeclarationEvidence.Property
                        declarationProperty
                && bodyProperty.Signature.IndexParameterTypes.IsEmpty
                    == declarationProperty.Signature.IndexParameterTypes.IsEmpty
            || body is MetadataAccessorRootDeclarationEvidence.Event
                && declaration
                    is MetadataAccessorRootDeclarationEvidence.Event;
    }

    static CSharpAccessorDeclarationRepresentabilityResult DecideProperty(
        CSharpAccessorDeclarationPost post,
        CSharpLanguageProfile profile,
        MetadataAccessorDeclarationEvidence aggregate,
        MetadataTypeDeclarationEvidence containingType,
        CSharpContainingDeclarationKind containingKind,
        MetadataAccessorRootDeclarationEvidence.Property property,
        MetadataAccessorSemanticsOccurrence target,
        ExplicitInterfaceContext? explicitInterface)
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
        var declarationProperty =
            explicitInterface?.Aggregate.Root
                as MetadataAccessorRootDeclarationEvidence.Property
            ?? property;
        if (property.Attributes != PropertyAttributes.None
            || declarationProperty.Attributes != PropertyAttributes.None)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedRootAttributes);
        }

        bool isIndexer =
            !property.Signature.IndexParameterTypes.IsEmpty;
        string metadataName =
            MetadataDeclarationText.RenderDeclarationName(
                declarationProperty);
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
        string declarationAccessibility;
        CSharpDeclarationModifierShape modifiers;
        if (explicitInterface is null)
        {
            if (accessibilities.Any(static value => value.Length == 0)
                || !CSharpAccessorDeclarationPolicy
                    .TrySelectPropertyAccessibility(
                        accessibilities,
                        out declarationAccessibility))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .AccessibilityUnavailable);
            }
            if (!TryGetModifierShape(
                    aggregate.Accessors,
                    out modifiers)
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
        }
        else
        {
            if (!TryGetModifierShape(
                    aggregate.Accessors,
                    out modifiers))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ModifierShapeUnavailable);
            }
            if (modifiers.IsStatic)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }
            if (accessibilities.Any(static value => value != "private")
                || modifiers.IsVirtual
                || modifiers.IsAbstract
                || modifiers.IsOverride
                || modifiers.IsSealed)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedExplicitInterfaceComposition);
            }
            declarationAccessibility = "private";
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
            CSharpAcceptedExplicitAccessorRelationship?
                explicitRelationship = null;
            if (explicitInterface is not null
                && !explicitInterface.Relationships.TryGetValue(
                    accessor.PhysicalRowNumber,
                    out explicitRelationship))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceEvidenceMismatch);
            }
            bindings.Add(
                new(
                    accessor,
                    accessor.PhysicalRowNumber
                            == target.PhysicalRowNumber
                        ? CSharpAccessorBodyPolicy.SelectedBody
                        : CSharpAccessorBodyPolicy.SiblingStub,
                    keyword,
                    explicitInterface is not null
                            || accessorAccessibility
                                == declarationAccessibility
                        ? null
                        : accessorAccessibility,
                    explicitRelationship));
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
                    bindings.MoveToImmutable(),
                    explicitInterface?.Aggregate,
                    explicitInterface?.Identity,
                    explicitInterface?.Spelling));
    }

    static CSharpAccessorDeclarationRepresentabilityResult DecideEvent(
        CSharpAccessorDeclarationPost post,
        CSharpLanguageProfile profile,
        MetadataAccessorDeclarationEvidence aggregate,
        MetadataTypeDeclarationEvidence containingType,
        CSharpContainingDeclarationKind containingKind,
        MetadataAccessorRootDeclarationEvidence.Event @event,
        MetadataAccessorSemanticsOccurrence target,
        ExplicitInterfaceContext? explicitInterface)
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
        var declarationEvent =
            explicitInterface?.Aggregate.Root
                as MetadataAccessorRootDeclarationEvidence.Event
            ?? @event;
        if (@event.Attributes != EventAttributes.None
            || declarationEvent.Attributes != EventAttributes.None)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedRootAttributes);
        }

        string metadataName =
            MetadataDeclarationText.RenderDeclarationName(
                declarationEvent);
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
        string declarationAccessibility;
        CSharpDeclarationModifierShape modifiers;
        if (explicitInterface is null)
        {
            if (accessibilities.Any(static value => value.Length == 0)
                || accessibilities
                    .Distinct(StringComparer.Ordinal).Count() != 1)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .AccessibilityUnavailable);
            }
            declarationAccessibility = accessibilities[0];
            if (!TryGetModifierShape(
                    aggregate.Accessors,
                    out modifiers)
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
        }
        else
        {
            if (!TryGetModifierShape(
                    aggregate.Accessors,
                    out modifiers))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ModifierShapeUnavailable);
            }
            if (modifiers.IsStatic)
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }
            if (accessibilities.Any(static value => value != "private")
                || modifiers.IsVirtual
                || modifiers.IsAbstract
                || modifiers.IsOverride
                || modifiers.IsSealed)
            {
                return Refuse(
                    CSharpAccessorDeclarationRefusalReason
                        .UnsupportedExplicitInterfaceComposition);
            }
            declarationAccessibility = "private";
        }

        if (!target.Method.HasBodyRva)
        {
            return Refuse(
                CSharpAccessorDeclarationRefusalReason
                    .MissingTargetBody);
        }

        var bindings =
            ImmutableArray.CreateBuilder<CSharpAcceptedAccessorBinding>(
                aggregate.Accessors.Length);
        foreach (MetadataAccessorSemanticsOccurrence accessor
            in aggregate.Accessors)
        {
            CSharpAcceptedExplicitAccessorRelationship?
                explicitRelationship = null;
            if (explicitInterface is not null
                && !explicitInterface.Relationships.TryGetValue(
                    accessor.PhysicalRowNumber,
                    out explicitRelationship))
            {
                return Unavailable(
                    CSharpAccessorDeclarationUnavailableReason
                        .ExplicitInterfaceEvidenceMismatch);
            }
            bindings.Add(
                new(
                    accessor,
                    accessor.PhysicalRowNumber
                            == target.PhysicalRowNumber
                        ? CSharpAccessorBodyPolicy.SelectedBody
                        : CSharpAccessorBodyPolicy.SiblingStub,
                    accessor.Role == MetadataAccessorSemanticsRole.AddOn
                        ? "add"
                        : "remove",
                    accessibility: null,
                    explicitRelationship));
        }
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
                    bindings.MoveToImmutable(),
                    explicitInterface?.Aggregate,
                    explicitInterface?.Identity,
                    explicitInterface?.Spelling));
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

        var head = new List<string>();
        if (request.ExplicitInterface is null)
        {
            head.Add(request.Accessibility);
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
        }
        if (request.Kind == CSharpAccessorDeclarationKind.Event)
            head.Add("event");
        head.Add(request.Type.Source);
        string explicitInterfacePrefix =
            request.ExplicitInterface is null
                ? ""
                : request.ExplicitInterface.Source + ".";
        head.Add(request.Kind == CSharpAccessorDeclarationKind.Indexer
            ? $"{explicitInterfacePrefix}this[{string.Join(
                ", ",
                request.Parameters.Select(parameter =>
                    $"{parameter.Type.Source} {parameter.Name}"))}]"
            : explicitInterfacePrefix + request.Name);

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
