using System.Collections.Immutable;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public enum CSharpTypeArtifactKind
{
    Field,
    Method,
    Property,
    Event,
}

public enum CSharpTypeArtifactRepresentationKind
{
    Declaration,
    TypeFrame,
    PhysicalBody,
}

public enum CSharpTypeArtifactRole
{
    Declaration,
    Getter,
    Setter,
    Init,
    Adder,
    Remover,
    BackingStorage,
    EnumStorage,
    DelegateSignature,
    LoweredImplementationHelper,
}

public enum CSharpTypeOrigin
{
    Unknown,
    Generated,
    NonGenerated,
}

public enum CSharpTypeBodyRole
{
    Method,
    Getter,
    Setter,
    Init,
    Adder,
    Remover,
}

public enum CSharpTypeBodyOutcome
{
    Available,
    Unavailable,
    Failed,
    NoBody,
}

public enum CSharpTypeDeclarationKind
{
    Field,
    EnumValue,
    Constructor,
    Finalizer,
    Method,
    Operator,
    Property,
    Event,
}

public enum CSharpTypeDeclarationPlacement
{
    Unclassified,
    Instance,
    Static,
}

public enum CSharpTypeAccessibility
{
    Unknown,
    Private,
    PrivateProtected,
    Protected,
    Internal,
    ProtectedInternal,
    Public,
}

public enum CSharpTypeRenderPartKind
{
    Fixed,
    Documentation,
    Attributes,
    Implementation,
}

public enum CSharpTypeRegionRole
{
    Documentation,
    Attributes,
    Signature,
    Implementation,
}

public enum CSharpTypeImplementationKind
{
    Body,
    Initializer,
    ImplicitAccessors,
}

public enum CSharpTypeBodyContributionRole
{
    FieldInitializer,
    PropertyInitializer,
    LoweredImplementation,
}

public enum CSharpTypeSourceKind
{
    Decompiled,
}

public enum CSharpTypeDocumentationCapability
{
    Unavailable,
    Absent,
    Available,
}

public enum CSharpTypeContractRelationshipCapability
{
    Unavailable,
    Available,
}

public sealed record CSharpTypeArtifactRepresentation(
    CSharpTypeArtifactRepresentationKind Kind,
    CSharpTypeArtifactRole Role,
    int? TargetId = null);

public sealed record CSharpTypePhysicalArtifact(
    int Id,
    MemberAnchor Anchor,
    int MetadataToken,
    CSharpTypeArtifactKind Kind,
    CSharpTypeOrigin Origin,
    CSharpTypeArtifactRepresentation Representation);

public sealed record CSharpTypePhysicalBody(
    int Id,
    MetadataMethodAddress Address,
    int ArtifactId,
    CSharpTypeBodyRole Role,
    bool HasManagedBody,
    CSharpTypeBodyOutcome Outcome,
    DecompilationFidelity? Fidelity,
    string Fingerprint,
    ImmutableArray<DecompilerDiagnostic> Diagnostics = default);

public sealed record CSharpTypeOwnedBodyReference(
    int BodyId,
    CSharpSourceRange FullRange,
    bool HasDrillDownDestination = true);

public sealed record CSharpTypeBodyContribution(
    int BodyId,
    CSharpTypeBodyContributionRole Role,
    CSharpSourceRange FullRange);

public sealed record CSharpTypeRenderPart(
    int Id,
    CSharpTypeRenderPartKind Kind,
    CSharpTypeRegionRole Region,
    string FullText,
    string SkeletonText,
    CSharpTypeImplementationKind? ImplementationKind = null,
    ImmutableArray<CSharpTypeOwnedBodyReference> OwnedBodies = default,
    ImmutableArray<CSharpTypeBodyContribution> Contributions = default);

public sealed record CSharpTypeFrame(
    ImmutableArray<CSharpTypeRenderPart> PrefixParts,
    string DeclarationSeparator,
    string Suffix);

public sealed record CSharpTypeDeclaration(
    int Id,
    int SourceOrder,
    MemberAnchor Anchor,
    int DeclarationToken,
    CSharpTypeDeclarationKind Kind,
    CSharpTypeAccessibility Accessibility,
    CSharpTypeDeclarationPlacement Placement,
    CSharpTypeOrigin Origin,
    ImmutableArray<CSharpTypeRenderPart> Parts);

public sealed record CSharpTypeDocumentSource(
    CSharpTypeSourceKind Kind,
    string AssemblyName,
    bool PdbSupplied,
    DecompilerSymbolSource Symbols,
    string RenderingPolicy);

internal sealed record CSharpTypeDocumentData(
    MetadataTypeDefinitionName TypeName,
    MetadataTypeDefinitionAddress TypeAddress,
    CSharpTypeDocumentSource Source,
    CSharpTypeFrame Frame,
    ImmutableArray<CSharpTypePhysicalArtifact> Artifacts,
    ImmutableArray<CSharpTypePhysicalBody> Bodies,
    ImmutableArray<CSharpTypeDeclaration> Declarations,
    CSharpTypeDocumentationCapability Documentation,
    CSharpTypeContractRelationshipCapability ContractRelationships);

public sealed class CSharpTypeDocument
{
    internal const int MaxDocumentNodes =
        MetadataSafetyPolicy.MaxSignatureTypeNodes;

    CSharpTypeDocument(
        CSharpTypeDocumentData data,
        CSharpDocumentRevision revision)
    {
        TypeName = data.TypeName;
        TypeAddress = data.TypeAddress;
        Source = data.Source;
        Frame = data.Frame;
        Artifacts = data.Artifacts;
        Bodies = data.Bodies;
        Declarations = data.Declarations;
        Documentation = data.Documentation;
        ContractRelationships = data.ContractRelationships;
        Revision = revision;
    }

    public MetadataTypeDefinitionName TypeName { get; }

    public MetadataTypeDefinitionAddress TypeAddress { get; }

    public CSharpTypeDocumentSource Source { get; }

    public CSharpTypeFrame Frame { get; }

    public ImmutableArray<CSharpTypePhysicalArtifact> Artifacts { get; }

    public ImmutableArray<CSharpTypePhysicalBody> Bodies { get; }

    public ImmutableArray<CSharpTypeDeclaration> Declarations { get; }

    public CSharpTypeDocumentationCapability Documentation { get; }

    public CSharpTypeContractRelationshipCapability ContractRelationships { get; }

    public CSharpDocumentRevision Revision { get; }

    public static CSharpTypeDocument Create(
        MetadataTypeDefinitionName typeName,
        MetadataTypeDefinitionAddress typeAddress,
        CSharpTypeDocumentSource source,
        CSharpTypeFrame frame,
        IEnumerable<CSharpTypePhysicalArtifact> artifacts,
        IEnumerable<CSharpTypePhysicalBody> bodies,
        IEnumerable<CSharpTypeDeclaration> declarations,
        CSharpTypeDocumentationCapability documentation,
        CSharpTypeContractRelationshipCapability contractRelationships =
            CSharpTypeContractRelationshipCapability.Unavailable)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(declarations);
        if (!Enum.IsDefined(documentation))
            throw new ArgumentOutOfRangeException(nameof(documentation));
        if (!Enum.IsDefined(contractRelationships))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contractRelationships));
        }

        var budget = new SnapshotBudget(MaxDocumentNodes);
        var data = new CSharpTypeDocumentData(
            typeName,
            typeAddress,
            SnapshotSource(source),
            SnapshotFrame(frame, budget),
            SnapshotArtifacts(artifacts, budget),
            SnapshotBodies(bodies, budget),
            SnapshotDeclarations(declarations, budget),
            documentation,
            contractRelationships);
        CSharpTypeDocumentValidator.Validate(data);
        var document = new CSharpTypeDocument(
            data,
            CSharpTypeDocumentRevision.Create(data));
        CSharpTypeDocumentJson.ValidateReplaySize(document);
        return document;
    }

    internal CSharpTypeDocumentData ToData()
        => new(
            TypeName,
            TypeAddress,
            Source,
            Frame,
            Artifacts,
            Bodies,
            Declarations,
            Documentation,
            ContractRelationships);

    static CSharpTypeDocumentSource SnapshotSource(CSharpTypeDocumentSource source)
        => source with { };

    static CSharpTypeFrame SnapshotFrame(
        CSharpTypeFrame frame,
        SnapshotBudget budget)
        => frame with
        {
            PrefixParts = SnapshotParts(frame.PrefixParts, budget),
        };

    static ImmutableArray<CSharpTypePhysicalArtifact> SnapshotArtifacts(
        IEnumerable<CSharpTypePhysicalArtifact> artifacts,
        SnapshotBudget budget)
    {
        var builder = ImmutableArray.CreateBuilder<CSharpTypePhysicalArtifact>();
        foreach (CSharpTypePhysicalArtifact artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ArgumentNullException.ThrowIfNull(artifact.Anchor);
            ArgumentNullException.ThrowIfNull(artifact.Representation);
            budget.Charge();
            builder.Add(artifact with
            {
                Anchor = artifact.Anchor with { },
                Representation = artifact.Representation with { },
            });
        }
        return builder.ToImmutable();
    }

    static ImmutableArray<CSharpTypePhysicalBody> SnapshotBodies(
        IEnumerable<CSharpTypePhysicalBody> bodies,
        SnapshotBudget budget)
    {
        var builder = ImmutableArray.CreateBuilder<CSharpTypePhysicalBody>();
        foreach (CSharpTypePhysicalBody body in bodies)
        {
            ArgumentNullException.ThrowIfNull(body);
            budget.Charge();
            budget.Charge(body.Diagnostics.IsDefault ? 0 : body.Diagnostics.Length);
            builder.Add(body with
            {
                Fingerprint = body.Fingerprint?.ToUpperInvariant()!,
                Diagnostics = body.Diagnostics.IsDefault
                    ? []
                    : [.. body.Diagnostics],
            });
        }
        return builder.ToImmutable();
    }

    static ImmutableArray<CSharpTypeDeclaration> SnapshotDeclarations(
        IEnumerable<CSharpTypeDeclaration> declarations,
        SnapshotBudget budget)
    {
        var builder = ImmutableArray.CreateBuilder<CSharpTypeDeclaration>();
        foreach (CSharpTypeDeclaration declaration in declarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            ArgumentNullException.ThrowIfNull(declaration.Anchor);
            budget.Charge();
            builder.Add(declaration with
            {
                Anchor = declaration.Anchor with { },
                Parts = SnapshotParts(declaration.Parts, budget),
            });
        }
        return builder.ToImmutable();
    }

    static ImmutableArray<CSharpTypeRenderPart> SnapshotParts(
        ImmutableArray<CSharpTypeRenderPart> parts,
        SnapshotBudget budget)
    {
        if (parts.IsDefault)
            throw new ArgumentException("Render parts must be initialized.", nameof(parts));

        var builder = ImmutableArray.CreateBuilder<CSharpTypeRenderPart>(parts.Length);
        foreach (CSharpTypeRenderPart part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            budget.Charge();
            budget.Charge(part.OwnedBodies.IsDefault ? 0 : part.OwnedBodies.Length);
            budget.Charge(part.Contributions.IsDefault ? 0 : part.Contributions.Length);
            builder.Add(part with
            {
                OwnedBodies = part.OwnedBodies.IsDefault
                    ? []
                    : [.. part.OwnedBodies.Select(static reference =>
                    {
                        ArgumentNullException.ThrowIfNull(reference);
                        return reference with { };
                    })],
                Contributions = part.Contributions.IsDefault
                    ? []
                    : [.. part.Contributions.Select(static contribution =>
                    {
                        ArgumentNullException.ThrowIfNull(contribution);
                        return contribution with { };
                    })],
            });
        }
        return builder.ToImmutable();
    }

    sealed class SnapshotBudget(int remaining)
    {
        int remainingNodes = remaining;

        internal void Charge(int count = 1)
        {
            if (count < 0 || count > remainingNodes)
            {
                throw new ArgumentException(
                    $"C# Type document exceeds the {MaxDocumentNodes} node budget.");
            }
            remainingNodes -= count;
        }
    }
}

static class CSharpTypeDocumentValidator
{
    internal static void Validate(CSharpTypeDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data.TypeName);
        ValidateTypeAddress(data.TypeAddress);
        ValidateSource(data.Source);
        ValidateText(data.Frame.DeclarationSeparator, "Declaration separator");
        ValidateText(data.Frame.Suffix, "Type-frame suffix");
        ValidateParts(data.Frame.PrefixParts, "Type frame");

        ValidateArtifacts(data.Artifacts, data.Declarations.Length, data.Bodies.Length);
        ValidateBodies(data.Bodies, data.Artifacts, data.TypeAddress.ModuleVersionId);
        ValidateArtifactBodyAssociations(data.Artifacts, data.Bodies);
        ValidateFrameBodyReferences(data.Frame, data.Bodies);
        ValidateDeclarations(data.Declarations, data.Artifacts, data.Bodies);
        ValidateArtifactBodyDeclarationRoles(
            data.Artifacts,
            data.Bodies,
            data.Declarations);
        ValidateArtifactCompleteness(data.Artifacts, data.Bodies, data.Declarations);
        ValidateBodyOwnershipAndContributionActivation(
            data.Frame,
            data.Artifacts,
            data.Bodies,
            data.Declarations);
        ValidateCapabilities(data);
        ValidateTextBudget(data);
        ValidateProjectionTextBudget(data);
        ValidateSkeletonExcludesBodyEvidence(data);
    }

    static void ValidateTypeAddress(MetadataTypeDefinitionAddress address)
    {
        if (address.ModuleVersionId == Guid.Empty)
            throw new ArgumentException("Type address requires a non-empty MVID.");
        ValidateToken(
            address.Definition.Value,
            0x02000000,
            "Type address");
    }

    static void ValidateSource(CSharpTypeDocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(source.Kind))
            throw new ArgumentOutOfRangeException(nameof(source.Kind));
        if (!Enum.IsDefined(source.Symbols))
            throw new ArgumentOutOfRangeException(nameof(source.Symbols));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.AssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.RenderingPolicy);
        ValidateText(source.AssemblyName, "Assembly name");
        ValidateText(source.RenderingPolicy, "Rendering policy");
    }

    static void ValidateArtifacts(
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        int declarationCount,
        int bodyCount)
    {
        if (artifacts.IsDefault)
            throw new ArgumentException("Artifact inventory must be initialized.");

        int previousToken = -1;
        var tokens = new HashSet<int>();
        var anchors = new HashSet<MemberAnchor>();
        for (int index = 0; index < artifacts.Length; index++)
        {
            CSharpTypePhysicalArtifact artifact = artifacts[index];
            if (artifact.Id != index)
            {
                throw new ArgumentException(
                    $"Artifact ids must be contiguous; expected {index}, found {artifact.Id}.");
            }
            ArgumentNullException.ThrowIfNull(artifact.Anchor);
            ArgumentNullException.ThrowIfNull(artifact.Representation);
            ValidateAnchor(artifact.Anchor, $"Artifact {artifact.Id}");
            if (!Enum.IsDefined(artifact.Kind))
                throw new ArgumentOutOfRangeException(nameof(artifact.Kind));
            if (!Enum.IsDefined(artifact.Origin))
                throw new ArgumentOutOfRangeException(nameof(artifact.Origin));
            if (!Enum.IsDefined(artifact.Representation.Kind))
                throw new ArgumentOutOfRangeException(nameof(artifact.Representation.Kind));
            if (!Enum.IsDefined(artifact.Representation.Role))
                throw new ArgumentOutOfRangeException(nameof(artifact.Representation.Role));

            ValidateArtifactToken(artifact);
            if (artifact.MetadataToken <= previousToken)
            {
                throw new ArgumentException(
                    "Artifacts must be in strictly increasing metadata-token order.");
            }
            previousToken = artifact.MetadataToken;
            if (!tokens.Add(artifact.MetadataToken))
                throw new ArgumentException("Artifact metadata tokens must be unique.");
            if (!anchors.Add(artifact.Anchor))
                throw new ArgumentException("Artifact member anchors must be unique.");

            int? target = artifact.Representation.TargetId;
            switch (artifact.Representation.Kind)
            {
                case CSharpTypeArtifactRepresentationKind.TypeFrame
                    when target is not null:
                    throw new ArgumentException(
                        "Type-frame artifact associations cannot carry a target id.");
                case CSharpTypeArtifactRepresentationKind.Declaration
                    when target is null
                        || target < 0
                        || target >= declarationCount:
                    throw new ArgumentException(
                        $"Artifact {artifact.Id} has an invalid declaration target.");
                case CSharpTypeArtifactRepresentationKind.PhysicalBody
                    when target is null
                        || target < 0
                        || target >= bodyCount:
                    throw new ArgumentException(
                        $"Artifact {artifact.Id} has an invalid physical-body target.");
            }
        }
    }

    static void ValidateBodies(
        ImmutableArray<CSharpTypePhysicalBody> bodies,
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        Guid documentModuleVersionId)
    {
        if (bodies.IsDefault)
            throw new ArgumentException("Physical body inventory must be initialized.");

        var addresses = new HashSet<MetadataMethodAddress>();
        for (int index = 0; index < bodies.Length; index++)
        {
            CSharpTypePhysicalBody body = bodies[index];
            if (body.Id != index)
            {
                throw new ArgumentException(
                    $"Physical body ids must be contiguous; expected {index}, found {body.Id}.");
            }
            if (body.ArtifactId < 0 || body.ArtifactId >= artifacts.Length)
                throw new ArgumentException($"Physical body {body.Id} has an invalid artifact id.");
            CSharpTypePhysicalArtifact artifact = artifacts[body.ArtifactId];
            if (artifact.Kind != CSharpTypeArtifactKind.Method)
                throw new ArgumentException($"Physical body {body.Id} is not owned by a MethodDef artifact.");
            if (artifact.MetadataToken != body.Address.Token)
                throw new ArgumentException($"Physical body {body.Id} does not match its MethodDef artifact.");
            if (body.Address.ModuleVersionId == Guid.Empty)
                throw new ArgumentException($"Physical body {body.Id} requires a non-empty MVID.");
            if (body.Address.ModuleVersionId != documentModuleVersionId)
            {
                throw new ArgumentException(
                    $"Physical body {body.Id} belongs to a different module than the document TypeDef.");
            }
            if (!Enum.IsDefined(body.Role))
                throw new ArgumentOutOfRangeException(nameof(body.Role));
            if (!Enum.IsDefined(body.Outcome))
                throw new ArgumentOutOfRangeException(nameof(body.Outcome));
            if (body.Fidelity is { } fidelity && !Enum.IsDefined(fidelity))
                throw new ArgumentOutOfRangeException(nameof(body.Fidelity));
            if (body.Outcome == CSharpTypeBodyOutcome.NoBody && body.HasManagedBody)
                throw new ArgumentException($"Physical body {body.Id} cannot be both bodyless and managed.");
            if (body.Outcome != CSharpTypeBodyOutcome.NoBody && !body.HasManagedBody)
                throw new ArgumentException($"Physical body {body.Id} without a managed body must use NoBody.");
            ValidateBodyOutcomeAndFidelity(body);
            ValidateFingerprint(body.Fingerprint, $"Physical body {body.Id}");
            ValidateDiagnostics(body.Diagnostics, $"Physical body {body.Id}");
            if (!addresses.Add(body.Address))
                throw new ArgumentException("Physical body addresses must be unique.");
        }

        static void ValidateBodyOutcomeAndFidelity(CSharpTypePhysicalBody body)
        {
            bool valid = body.Outcome switch
            {
                CSharpTypeBodyOutcome.Available =>
                    body.Fidelity is DecompilationFidelity.IlOnly
                        or DecompilationFidelity.StructuredOnly
                        or DecompilationFidelity.Partial
                        or DecompilationFidelity.Full,
                CSharpTypeBodyOutcome.Failed =>
                    body.Fidelity == DecompilationFidelity.Failed,
                CSharpTypeBodyOutcome.Unavailable
                    or CSharpTypeBodyOutcome.NoBody =>
                    body.Fidelity is null,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException(
                    $"Physical body {body.Id} has an invalid outcome/fidelity combination.");
            }
        }
    }

    static void ValidateDeclarations(
        ImmutableArray<CSharpTypeDeclaration> declarations,
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies)
    {
        if (declarations.IsDefault)
            throw new ArgumentException("Declaration population must be initialized.");

        int previousSourceOrder = -1;
        var anchors = new HashSet<MemberAnchor>();
        for (int index = 0; index < declarations.Length; index++)
        {
            CSharpTypeDeclaration declaration = declarations[index];
            if (declaration.Id != index)
            {
                throw new ArgumentException(
                    $"Declaration ids must be contiguous; expected {index}, found {declaration.Id}.");
            }
            if (declaration.SourceOrder <= previousSourceOrder)
                throw new ArgumentException("Declaration source order must be strictly increasing.");
            previousSourceOrder = declaration.SourceOrder;
            ArgumentNullException.ThrowIfNull(declaration.Anchor);
            ValidateAnchor(declaration.Anchor, $"Declaration {declaration.Id}");
            if (!anchors.Add(declaration.Anchor))
                throw new ArgumentException("Declaration member anchors must be unique.");
            if (!Enum.IsDefined(declaration.Kind))
                throw new ArgumentOutOfRangeException(nameof(declaration.Kind));
            if (!Enum.IsDefined(declaration.Accessibility))
                throw new ArgumentOutOfRangeException(nameof(declaration.Accessibility));
            if (!Enum.IsDefined(declaration.Placement))
                throw new ArgumentOutOfRangeException(nameof(declaration.Placement));
            if (!Enum.IsDefined(declaration.Origin))
                throw new ArgumentOutOfRangeException(nameof(declaration.Origin));
            ValidateDeclarationToken(declaration);
            ValidateParts(declaration.Parts, $"Declaration {declaration.Id}");
            if (!declaration.Parts.Any(static part =>
                part.Kind == CSharpTypeRenderPartKind.Fixed
                && !string.IsNullOrWhiteSpace(part.FullText)))
            {
                throw new ArgumentException(
                    $"Declaration {declaration.Id} requires a non-whitespace signature part.");
            }
            ValidateBodyReferences(declaration, artifacts, bodies);
        }
    }

    static void ValidateArtifactCompleteness(
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies,
        ImmutableArray<CSharpTypeDeclaration> declarations)
    {
        foreach (CSharpTypePhysicalArtifact method in artifacts.Where(
            static artifact => artifact.Kind == CSharpTypeArtifactKind.Method))
        {
            int bodyCount = bodies.Count(body => body.ArtifactId == method.Id);
            if (bodyCount != 1)
            {
                throw new ArgumentException(
                    $"Method artifact {method.Id} must own exactly one physical body row.");
            }
        }

        foreach (CSharpTypeDeclaration declaration in declarations)
        {
            int primaryCount = artifacts.Count(artifact =>
                artifact.Representation.Kind
                    == CSharpTypeArtifactRepresentationKind.Declaration
                && artifact.Representation.TargetId == declaration.Id
                && artifact.Representation.Role
                    == CSharpTypeArtifactRole.Declaration
                && artifact.Anchor == declaration.Anchor
                && artifact.MetadataToken == declaration.DeclarationToken);
            if (primaryCount != 1)
            {
                throw new ArgumentException(
                    $"Declaration {declaration.Id} must have exactly one primary physical artifact.");
            }
        }
    }

    static void ValidateCapabilities(CSharpTypeDocumentData data)
    {
        bool hasDocumentation = data.Frame.PrefixParts.Any(static part =>
                part.Kind == CSharpTypeRenderPartKind.Documentation)
            || data.Declarations.Any(static declaration =>
                declaration.Parts.Any(static part =>
                    part.Kind == CSharpTypeRenderPartKind.Documentation));
        if ((data.Documentation
                is CSharpTypeDocumentationCapability.Unavailable
                    or CSharpTypeDocumentationCapability.Absent)
            && hasDocumentation)
        {
            throw new ArgumentException(
                "Unavailable or absent documentation cannot carry documentation regions.");
        }
        if (data.ContractRelationships
            != CSharpTypeContractRelationshipCapability.Unavailable)
        {
            throw new ArgumentException(
                "Contract relationships are unavailable in document schema version 1.");
        }
    }

    static void ValidateBodyOwnershipAndContributionActivation(
        CSharpTypeFrame frame,
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies,
        ImmutableArray<CSharpTypeDeclaration> declarations)
    {
        int[] ownerByBody = Enumerable.Repeat(-1, bodies.Length).ToArray();
        foreach (CSharpTypeDeclaration declaration in declarations)
        {
            foreach (CSharpTypeOwnedBodyReference reference
                in declaration.Parts.SelectMany(static part => part.OwnedBodies))
            {
                if (ownerByBody[reference.BodyId] != -1)
                {
                    throw new ArgumentException(
                        $"Physical body {reference.BodyId} is owned more than once.");
                }
                ownerByBody[reference.BodyId] = declaration.Id;
            }
        }

        foreach (CSharpTypePhysicalBody body in bodies)
        {
            CSharpTypeArtifactRepresentation representation =
                artifacts[body.ArtifactId].Representation;
            if (body.HasManagedBody
                && representation.Kind
                    == CSharpTypeArtifactRepresentationKind.Declaration)
            {
                int expectedOwner = representation.TargetId!.Value;
                if (ownerByBody[body.Id] != expectedOwner)
                {
                    throw new ArgumentException(
                        $"Managed physical body {body.Id} must be owned exactly once by declaration {expectedOwner}.");
                }
            }
        }

        ValidateContributionActivation(
            frame.PrefixParts,
            "Type frame",
            ownerByBody);
        foreach (CSharpTypeDeclaration declaration in declarations)
        {
            ValidateContributionActivation(
                declaration.Parts,
                $"Declaration {declaration.Id}",
                ownerByBody);
        }

        static void ValidateContributionActivation(
            ImmutableArray<CSharpTypeRenderPart> parts,
            string owner,
            int[] ownerByBody)
        {
            foreach (CSharpTypeRenderPart part in parts)
            {
                if (part.OwnedBodies.IsEmpty
                    && part.Contributions.Length > 1
                    && part.Contributions.All(contribution =>
                        contribution.Role == part.Contributions[0].Role
                        && contribution.FullRange == part.Contributions[0].FullRange))
                {
                    continue;
                }
                int? activationOwner = null;
                bool hasContribution = false;
                foreach (int bodyId in part.OwnedBodies
                    .Select(static reference => reference.BodyId)
                    .Concat(part.Contributions.Select(
                        static contribution => contribution.BodyId)))
                {
                    int activationOwnerId = ownerByBody[bodyId];
                    if (!hasContribution)
                    {
                        activationOwner = activationOwnerId;
                        hasContribution = true;
                    }
                    else if (activationOwner != activationOwnerId)
                    {
                        throw new ArgumentException(
                            $"{owner} implementation part {part.Id} combines contributions requiring different Selected-body activation.");
                    }
                }
            }
        }
    }

    static void ValidateArtifactBodyDeclarationRoles(
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies,
        ImmutableArray<CSharpTypeDeclaration> declarations)
    {
        foreach (CSharpTypePhysicalArtifact artifact in artifacts)
        {
            CSharpTypeArtifactRepresentation representation =
                artifact.Representation;
            bool valid = representation.Role switch
            {
                CSharpTypeArtifactRole.Declaration =>
                    IsPrimaryDeclarationArtifact(
                        artifact,
                        representation,
                        declarations),
                CSharpTypeArtifactRole.Getter =>
                    IsAccessorArtifact(
                        artifact,
                        representation,
                        declarations,
                        CSharpTypeDeclarationKind.Property),
                CSharpTypeArtifactRole.Setter =>
                    IsAccessorArtifact(
                        artifact,
                        representation,
                        declarations,
                        CSharpTypeDeclarationKind.Property),
                CSharpTypeArtifactRole.Init =>
                    IsAccessorArtifact(
                        artifact,
                        representation,
                        declarations,
                        CSharpTypeDeclarationKind.Property),
                CSharpTypeArtifactRole.Adder =>
                    IsAccessorArtifact(
                        artifact,
                        representation,
                        declarations,
                        CSharpTypeDeclarationKind.Event),
                CSharpTypeArtifactRole.Remover =>
                    IsAccessorArtifact(
                        artifact,
                        representation,
                        declarations,
                        CSharpTypeDeclarationKind.Event),
                CSharpTypeArtifactRole.BackingStorage =>
                    artifact.Kind == CSharpTypeArtifactKind.Field
                    && representation.Kind
                        == CSharpTypeArtifactRepresentationKind.Declaration
                    && declarations[representation.TargetId!.Value].Kind
                        is CSharpTypeDeclarationKind.Property
                            or CSharpTypeDeclarationKind.Event,
                CSharpTypeArtifactRole.EnumStorage =>
                    artifact.Kind == CSharpTypeArtifactKind.Field
                    && representation.Kind
                        == CSharpTypeArtifactRepresentationKind.TypeFrame,
                CSharpTypeArtifactRole.DelegateSignature =>
                    artifact.Kind == CSharpTypeArtifactKind.Method
                    && representation.Kind
                        == CSharpTypeArtifactRepresentationKind.TypeFrame,
                CSharpTypeArtifactRole.LoweredImplementationHelper =>
                    artifact.Kind == CSharpTypeArtifactKind.Method
                    && representation.Kind
                        == CSharpTypeArtifactRepresentationKind.PhysicalBody,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException(
                    $"Artifact {artifact.Id} role is inconsistent with its kind, representation, or target.");
            }
        }

        foreach (CSharpTypePhysicalBody body in bodies)
        {
            CSharpTypePhysicalArtifact artifact = artifacts[body.ArtifactId];
            bool valid = (artifact.Representation.Role, body.Role) switch
            {
                (CSharpTypeArtifactRole.Declaration
                    or CSharpTypeArtifactRole.DelegateSignature
                    or CSharpTypeArtifactRole.LoweredImplementationHelper,
                    CSharpTypeBodyRole.Method) => true,
                (CSharpTypeArtifactRole.Getter, CSharpTypeBodyRole.Getter) => true,
                (CSharpTypeArtifactRole.Setter, CSharpTypeBodyRole.Setter) => true,
                (CSharpTypeArtifactRole.Init, CSharpTypeBodyRole.Init) => true,
                (CSharpTypeArtifactRole.Adder, CSharpTypeBodyRole.Adder) => true,
                (CSharpTypeArtifactRole.Remover, CSharpTypeBodyRole.Remover) => true,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException(
                    $"Physical body {body.Id} role is inconsistent with its artifact representation and declaration.");
            }
        }

        static bool IsPrimaryDeclarationArtifact(
            CSharpTypePhysicalArtifact artifact,
            CSharpTypeArtifactRepresentation representation,
            ImmutableArray<CSharpTypeDeclaration> declarations)
        {
            if (representation.Kind
                    != CSharpTypeArtifactRepresentationKind.Declaration)
            {
                return false;
            }

            CSharpTypeDeclaration declaration =
                declarations[representation.TargetId!.Value];
            return artifact.Kind == DeclarationArtifactKind(declaration.Kind)
                && artifact.Anchor == declaration.Anchor
                && artifact.MetadataToken == declaration.DeclarationToken;
        }

        static bool IsAccessorArtifact(
            CSharpTypePhysicalArtifact artifact,
            CSharpTypeArtifactRepresentation representation,
            ImmutableArray<CSharpTypeDeclaration> declarations,
            CSharpTypeDeclarationKind declarationKind)
            => artifact.Kind == CSharpTypeArtifactKind.Method
                && representation.Kind
                    == CSharpTypeArtifactRepresentationKind.Declaration
                && declarations[representation.TargetId!.Value].Kind
                    == declarationKind;

        static CSharpTypeArtifactKind DeclarationArtifactKind(
            CSharpTypeDeclarationKind kind)
            => kind switch
            {
                CSharpTypeDeclarationKind.Field
                    or CSharpTypeDeclarationKind.EnumValue =>
                    CSharpTypeArtifactKind.Field,
                CSharpTypeDeclarationKind.Constructor
                    or CSharpTypeDeclarationKind.Finalizer
                    or CSharpTypeDeclarationKind.Method
                    or CSharpTypeDeclarationKind.Operator =>
                    CSharpTypeArtifactKind.Method,
                CSharpTypeDeclarationKind.Property =>
                    CSharpTypeArtifactKind.Property,
                CSharpTypeDeclarationKind.Event =>
                    CSharpTypeArtifactKind.Event,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
    }

    static void ValidateArtifactBodyAssociations(
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies)
    {
        foreach (CSharpTypePhysicalArtifact artifact in artifacts)
        {
            if (artifact.Representation.Kind
                != CSharpTypeArtifactRepresentationKind.PhysicalBody)
            {
                continue;
            }

            int bodyId = artifact.Representation.TargetId!.Value;
            if (bodies[bodyId].ArtifactId != artifact.Id)
            {
                throw new ArgumentException(
                    $"Artifact {artifact.Id} is associated with a physical body owned by another artifact.");
            }
        }
    }

    static void ValidateFrameBodyReferences(
        CSharpTypeFrame frame,
        ImmutableArray<CSharpTypePhysicalBody> bodies)
    {
        foreach (CSharpTypeRenderPart part in frame.PrefixParts)
        {
            if (!part.OwnedBodies.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    $"Type frame part {part.Id} cannot own a physical body.");
            }

            var contributed =
                new HashSet<(int BodyId, CSharpTypeBodyContributionRole Role, int Start, int Length)>();
            foreach (CSharpTypeBodyContribution contribution in part.Contributions)
            {
                if (!Enum.IsDefined(contribution.Role))
                    throw new ArgumentOutOfRangeException(nameof(contribution.Role));
                if (contribution.BodyId < 0 || contribution.BodyId >= bodies.Length)
                {
                    throw new ArgumentException(
                        $"Type frame references an unknown physical body {contribution.BodyId}.");
                }
                ValidateRange(
                    contribution.FullRange,
                    part.FullText,
                    $"Type frame contribution from body {contribution.BodyId}");
                if (!contributed.Add((
                    contribution.BodyId,
                    contribution.Role,
                    contribution.FullRange.Start,
                    contribution.FullRange.Length)))
                {
                    throw new ArgumentException(
                        "Type frame contains a duplicate body contribution.");
                }
            }
        }
    }

    static void ValidateParts(
        ImmutableArray<CSharpTypeRenderPart> parts,
        string owner)
    {
        if (parts.IsDefault)
            throw new ArgumentException($"{owner} render parts must be initialized.");

        for (int index = 0; index < parts.Length; index++)
        {
            CSharpTypeRenderPart part = parts[index];
            if (part.Id != index)
            {
                throw new ArgumentException(
                    $"{owner} render-part ids must be contiguous; expected {index}, found {part.Id}.");
            }
            if (!Enum.IsDefined(part.Kind))
                throw new ArgumentOutOfRangeException(nameof(part.Kind));
            if (!Enum.IsDefined(part.Region))
                throw new ArgumentOutOfRangeException(nameof(part.Region));
            ValidateText(part.FullText, $"{owner} part {part.Id} full text");
            ValidateText(part.SkeletonText, $"{owner} part {part.Id} skeleton text");
            CSharpTypeRegionRole expectedRegion = part.Kind switch
            {
                CSharpTypeRenderPartKind.Fixed =>
                    CSharpTypeRegionRole.Signature,
                CSharpTypeRenderPartKind.Documentation =>
                    CSharpTypeRegionRole.Documentation,
                CSharpTypeRenderPartKind.Attributes =>
                    CSharpTypeRegionRole.Attributes,
                CSharpTypeRenderPartKind.Implementation =>
                    CSharpTypeRegionRole.Implementation,
                _ => throw new ArgumentOutOfRangeException(nameof(part.Kind)),
            };
            if (part.Region != expectedRegion)
            {
                throw new ArgumentException(
                    $"{owner} {part.Kind} part {part.Id} must use the {expectedRegion} region.");
            }

            if (part.Kind == CSharpTypeRenderPartKind.Implementation)
            {
                if (part.ImplementationKind is not { } implementationKind
                    || !Enum.IsDefined(implementationKind))
                {
                    throw new ArgumentException(
                        $"{owner} implementation part {part.Id} requires a valid implementation kind.");
                }
                if (part.OwnedBodies.Length > 1)
                {
                    throw new ArgumentException(
                        $"{owner} implementation part {part.Id} must be independently selectable for one owned body.");
                }
            }
            else
            {
                if (part.ImplementationKind is not null)
                {
                    throw new ArgumentException(
                        $"{owner} non-implementation part {part.Id} cannot carry an implementation kind.");
                }
                if (part.FullText != part.SkeletonText)
                {
                    throw new ArgumentException(
                        $"{owner} non-implementation part {part.Id} must have identical alternatives.");
                }
                if (!part.OwnedBodies.IsDefaultOrEmpty
                    || !part.Contributions.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        $"{owner} non-implementation part {part.Id} cannot carry body references.");
                }
            }

        }
    }

    static void ValidateSkeletonExcludesBodyEvidence(CSharpTypeDocumentData data)
    {
        long comparisonWork = 0;
        Charge(data.Frame.PrefixParts);
        foreach (CSharpTypeDeclaration declaration in data.Declarations)
            Charge(declaration.Parts);

        Validate(data.Frame.PrefixParts, "Type frame");
        foreach (CSharpTypeDeclaration declaration in data.Declarations)
        {
            Validate(
                declaration.Parts,
                $"Declaration {declaration.Id}");
        }

        void Charge(ImmutableArray<CSharpTypeRenderPart> parts)
        {
            foreach (CSharpTypeRenderPart part in parts)
            {
                if (part.OwnedBodies.IsDefaultOrEmpty
                    && part.Contributions.IsDefaultOrEmpty)
                {
                    continue;
                }

                AddWork(2L * part.SkeletonText.Length);
                foreach (CSharpTypeOwnedBodyReference reference in part.OwnedBodies)
                    Charge(reference.FullRange);
                foreach (CSharpTypeBodyContribution contribution in part.Contributions)
                    Charge(contribution.FullRange);

                void Charge(CSharpSourceRange range)
                {
                    if (range.Length > 0)
                    {
                        AddWork(
                            (4L * range.Length) + part.SkeletonText.Length);
                    }
                }
            }
        }

        void AddWork(long count)
        {
            comparisonWork = checked(comparisonWork + count);
            if (comparisonWork
                > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
            {
                throw new ArgumentException(
                    "C# Type document exceeds the body-evidence comparison budget.");
            }
        }

        static void Validate(
            ImmutableArray<CSharpTypeRenderPart> parts,
            string owner)
        {
            foreach (CSharpTypeRenderPart part in parts)
            {
                if (part.OwnedBodies.IsDefaultOrEmpty
                    && part.Contributions.IsDefaultOrEmpty)
                {
                    continue;
                }

                string skeleton = RemoveWhitespace(part.SkeletonText.AsSpan());
                foreach (CSharpTypeOwnedBodyReference reference in part.OwnedBodies)
                    Validate(reference.FullRange);
                foreach (CSharpTypeBodyContribution contribution in part.Contributions)
                    Validate(contribution.FullRange);

                void Validate(CSharpSourceRange range)
                {
                    if (range.Length == 0)
                        return;

                    string evidence = RemoveWhitespace(
                        part.FullText.AsSpan(range.Start, range.Length));
                    if (ContainsOrdinal(skeleton, evidence))
                    {
                        throw new ArgumentException(
                            $"{owner} body-backed implementation part {part.Id} skeleton retains body evidence modulo whitespace.");
                    }
                }
            }
        }

        static string RemoveWhitespace(ReadOnlySpan<char> text)
        {
            char[] characters = GC.AllocateUninitializedArray<char>(text.Length);
            int length = 0;
            foreach (char character in text)
            {
                if (!char.IsWhiteSpace(character))
                    characters[length++] = character;
            }
            return new string(characters, 0, length);
        }

        static bool ContainsOrdinal(string text, string value)
        {
            if (value.Length == 0)
                return true;

            var prefixLengths = new int[value.Length];
            for (int index = 1, prefixLength = 0; index < value.Length;)
            {
                if (value[index] == value[prefixLength])
                {
                    prefixLengths[index++] = ++prefixLength;
                }
                else if (prefixLength > 0)
                {
                    prefixLength = prefixLengths[prefixLength - 1];
                }
                else
                {
                    index++;
                }
            }

            for (int index = 0, matched = 0; index < text.Length;)
            {
                if (text[index] == value[matched])
                {
                    index++;
                    if (++matched == value.Length)
                        return true;
                }
                else if (matched > 0)
                {
                    matched = prefixLengths[matched - 1];
                }
                else
                {
                    index++;
                }
            }
            return false;
        }
    }

    static void ValidateBodyReferences(
        CSharpTypeDeclaration declaration,
        ImmutableArray<CSharpTypePhysicalArtifact> artifacts,
        ImmutableArray<CSharpTypePhysicalBody> bodies)
    {
        var owned = new HashSet<int>();
        foreach (CSharpTypeRenderPart part in declaration.Parts)
        {
            foreach (CSharpTypeOwnedBodyReference reference in part.OwnedBodies)
            {
                if (!owned.Add(reference.BodyId))
                {
                    throw new ArgumentException(
                        $"Declaration {declaration.Id} owns physical body {reference.BodyId} more than once.");
                }
                CSharpTypePhysicalBody body = ResolveBody(reference.BodyId, bodies, declaration.Id);
                CSharpTypePhysicalArtifact artifact = artifacts[body.ArtifactId];
                if (artifact.Representation.Kind
                        != CSharpTypeArtifactRepresentationKind.Declaration
                    || artifact.Representation.TargetId != declaration.Id)
                {
                    throw new ArgumentException(
                        $"Declaration {declaration.Id} cannot own physical body {reference.BodyId}.");
                }
                ValidateRange(
                    reference.FullRange,
                    part.FullText,
                    $"Declaration {declaration.Id} owned body {reference.BodyId}",
                    allowEmpty: true);
                if (reference.FullRange.Length == 0
                    && !AllowsEmptyEvidence(declaration, part, reference, body))
                {
                    throw new ArgumentException(
                        $"Declaration {declaration.Id} owned body {reference.BodyId} requires non-empty evidence unless it is an available empty body with identical alternatives.");
                }
            }

            var contributed = new HashSet<(int BodyId, CSharpTypeBodyContributionRole Role, int Start, int Length)>();
            foreach (CSharpTypeBodyContribution contribution in part.Contributions)
            {
                if (!Enum.IsDefined(contribution.Role))
                    throw new ArgumentOutOfRangeException(nameof(contribution.Role));
                ResolveBody(contribution.BodyId, bodies, declaration.Id);
                ValidateRange(
                    contribution.FullRange,
                    part.FullText,
                    $"Declaration {declaration.Id} contribution from body {contribution.BodyId}");
                if (!contributed.Add((
                    contribution.BodyId,
                    contribution.Role,
                    contribution.FullRange.Start,
                    contribution.FullRange.Length)))
                {
                    throw new ArgumentException(
                        $"Declaration {declaration.Id} contains a duplicate body contribution.");
                }
            }
        }
    }

    static CSharpTypePhysicalBody ResolveBody(
        int bodyId,
        ImmutableArray<CSharpTypePhysicalBody> bodies,
        int declarationId)
    {
        if (bodyId < 0 || bodyId >= bodies.Length)
        {
            throw new ArgumentException(
                $"Declaration {declarationId} references an unknown physical body {bodyId}.");
        }
        return bodies[bodyId];
    }

    static void ValidateArtifactToken(CSharpTypePhysicalArtifact artifact)
    {
        int expected = artifact.Kind switch
        {
            CSharpTypeArtifactKind.Field => 0x04000000,
            CSharpTypeArtifactKind.Method => 0x06000000,
            CSharpTypeArtifactKind.Event => 0x14000000,
            CSharpTypeArtifactKind.Property => 0x17000000,
            _ => throw new ArgumentOutOfRangeException(nameof(artifact.Kind)),
        };
        ValidateToken(artifact.MetadataToken, expected, $"Artifact {artifact.Id}");
    }

    static void ValidateDeclarationToken(CSharpTypeDeclaration declaration)
    {
        int expected = declaration.Kind switch
        {
            CSharpTypeDeclarationKind.Field or CSharpTypeDeclarationKind.EnumValue =>
                0x04000000,
            CSharpTypeDeclarationKind.Constructor
                or CSharpTypeDeclarationKind.Finalizer
                or CSharpTypeDeclarationKind.Method
                or CSharpTypeDeclarationKind.Operator =>
                0x06000000,
            CSharpTypeDeclarationKind.Event => 0x14000000,
            CSharpTypeDeclarationKind.Property => 0x17000000,
            _ => throw new ArgumentOutOfRangeException(nameof(declaration.Kind)),
        };
        ValidateToken(
            declaration.DeclarationToken,
            expected,
            $"Declaration {declaration.Id}");
    }

    static void ValidateToken(int token, int expectedTable, string owner)
    {
        if ((token & unchecked((int)0xFF000000)) != expectedTable
            || (token & 0x00FFFFFF) == 0)
        {
            throw new ArgumentException(
                $"{owner} token 0x{token:X8} has the wrong metadata table or row.");
        }
    }

    static void ValidateRange(
        CSharpSourceRange range,
        string text,
        string owner,
        bool allowEmpty = false)
    {
        int end;
        try
        {
            end = range.End;
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException(
                $"{owner} range overflows.",
                nameof(range),
                ex);
        }
        if ((!allowEmpty && range.Length == 0) || end > text.Length)
            throw new ArgumentOutOfRangeException(nameof(range), $"{owner} range is outside its full alternative.");
    }

    static bool IsEmptyBodyAlternative(string text, int bodyPosition)
    {
        int index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        if (index >= text.Length || text[index] != '{')
            return false;

        int contentStart = ++index;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        int contentEnd = index;
        if (index >= text.Length || text[index] != '}')
            return false;

        index++;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        return index == text.Length
            && bodyPosition >= contentStart
            && bodyPosition <= contentEnd;
    }

    static bool AllowsEmptyEvidence(
        CSharpTypeDeclaration declaration,
        CSharpTypeRenderPart part,
        CSharpTypeOwnedBodyReference reference,
        CSharpTypePhysicalBody body)
    {
        if (!body.HasManagedBody || part.FullText != part.SkeletonText)
            return false;
        if (body.Outcome == CSharpTypeBodyOutcome.Available
            && part.ImplementationKind == CSharpTypeImplementationKind.Body
            && IsEmptyBodyAlternative(part.FullText, reference.FullRange.Start))
        {
            return true;
        }
        if (reference.HasDrillDownDestination)
            return false;
        if (body.Outcome is CSharpTypeBodyOutcome.Unavailable or CSharpTypeBodyOutcome.Failed)
            return part.ImplementationKind is CSharpTypeImplementationKind.Body
                or CSharpTypeImplementationKind.ImplicitAccessors;
        return part.ImplementationKind == CSharpTypeImplementationKind.ImplicitAccessors
            && body.Outcome == CSharpTypeBodyOutcome.Available
            && ((declaration.Kind == CSharpTypeDeclarationKind.Property
                    && body.Role is CSharpTypeBodyRole.Getter or CSharpTypeBodyRole.Setter or CSharpTypeBodyRole.Init)
                || (declaration.Kind == CSharpTypeDeclarationKind.Event
                    && body.Role is CSharpTypeBodyRole.Adder or CSharpTypeBodyRole.Remover));
    }

    static void ValidateFingerprint(string fingerprint, string owner)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (fingerprint.Length != 64
            || fingerprint.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                $"{owner} fingerprint must be a 64-character SHA-256 hexadecimal value.");
        }
    }

    static void ValidateAnchor(MemberAnchor anchor, string owner)
    {
        ValidateAnchorText(anchor.StableSelector, $"{owner} stable selector");
        ValidateAnchorText(anchor.CanonicalSignature, $"{owner} canonical signature");
        ValidateAnchorText(anchor.TypeFullName, $"{owner} Type full name");
        ValidateAnchorText(anchor.MemberName, $"{owner} member name");
        ArgumentNullException.ThrowIfNull(anchor.Fingerprint);
        if (anchor.Fingerprint.Length != 10
            || anchor.Fingerprint.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                $"{owner} fingerprint must be a 10-character hexadecimal value.");
        }
    }

    static void ValidateAnchorText(string text, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MetadataSafetyPolicy.MaxStructuralSignatureChars)
            throw new ArgumentException($"{owner} exceeds the text budget.");
        ValidateText(text, owner);
    }

    static void ValidateDiagnostics(
        ImmutableArray<DecompilerDiagnostic> diagnostics,
        string owner)
    {
        if (diagnostics.IsDefault)
            throw new ArgumentException($"{owner} diagnostics must be initialized.");
        if (diagnostics.Length > MetadataSafetyPolicy.MaxRelationshipNodes)
            throw new ArgumentException($"{owner} has too many diagnostics.");

        foreach (DecompilerDiagnostic diagnostic in diagnostics)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Message);
            ValidateText(diagnostic.Id, $"{owner} diagnostic id");
            ValidateText(diagnostic.Message, $"{owner} diagnostic message");
        }
    }

    static void ValidateTextBudget(CSharpTypeDocumentData data)
    {
        int total = 0;
        void Add(string text)
        {
            try
            {
                total = checked(total + text.Length);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException("C# Type document text length overflows.", ex);
            }
        }

        Add(data.TypeName.Namespace);
        foreach (string segment in data.TypeName.Segments)
            Add(segment);
        Add(data.Source.AssemblyName);
        Add(data.Source.RenderingPolicy);
        foreach (CSharpTypeRenderPart part in data.Frame.PrefixParts)
        {
            Add(part.FullText);
            Add(part.SkeletonText);
        }
        Add(data.Frame.DeclarationSeparator);
        Add(data.Frame.Suffix);
        foreach (CSharpTypePhysicalArtifact artifact in data.Artifacts)
            AddAnchor(artifact.Anchor);
        foreach (CSharpTypePhysicalBody body in data.Bodies)
        {
            Add(body.Fingerprint);
            foreach (DecompilerDiagnostic diagnostic in body.Diagnostics)
            {
                Add(diagnostic.Id);
                Add(diagnostic.Message);
            }
        }
        foreach (CSharpTypeDeclaration declaration in data.Declarations)
        {
            AddAnchor(declaration.Anchor);
            foreach (CSharpTypeRenderPart part in declaration.Parts)
            {
                Add(part.FullText);
                Add(part.SkeletonText);
            }
        }
        if (total > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
        {
            throw new ArgumentException(
                "C# Type document exceeds the aggregate string budget.");
        }

        void AddAnchor(MemberAnchor anchor)
        {
            Add(anchor.StableSelector);
            Add(anchor.CanonicalSignature);
            Add(anchor.Fingerprint);
            Add(anchor.TypeFullName);
            Add(anchor.MemberName);
        }
    }

    static void ValidateProjectionTextBudget(CSharpTypeDocumentData data)
    {
        int total = 0;
        void Add(int length)
        {
            try
            {
                total = checked(total + length);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException(
                    "C# Type document projected text length overflows.",
                    ex);
            }
        }

        foreach (CSharpTypeRenderPart part in data.Frame.PrefixParts)
            Add(Math.Max(part.FullText.Length, part.SkeletonText.Length));
        try
        {
            Add(checked(
                data.Frame.DeclarationSeparator.Length
                * data.Declarations.Length));
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException(
                "C# Type document declaration separators overflow the projected text length.",
                ex);
        }
        foreach (CSharpTypeDeclaration declaration in data.Declarations)
        {
            foreach (CSharpTypeRenderPart part in declaration.Parts)
                Add(Math.Max(part.FullText.Length, part.SkeletonText.Length));
        }
        Add(data.Frame.Suffix.Length);

        if (total > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
        {
            throw new ArgumentException(
                "C# Type document exceeds the projected text budget.");
        }
    }

    static void ValidateText(string text, string owner)
    {
        ArgumentNullException.ThrowIfNull(text);
        AnnotatedSourceText.ValidateWellFormedUtf16(
            text,
            nameof(text),
            owner);
    }
}

public abstract record CSharpTypeDocumentOutcome
{
    private CSharpTypeDocumentOutcome()
    {
    }

    public sealed record Available : CSharpTypeDocumentOutcome
    {
        public Available(CSharpTypeDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (document.Bodies.Any(static body =>
                body.Outcome is CSharpTypeBodyOutcome.Unavailable
                    or CSharpTypeBodyOutcome.Failed))
            {
                throw new ArgumentException(
                    "An available document cannot contain failed body work.",
                    nameof(document));
            }
            Document = document;
        }

        public CSharpTypeDocument Document { get; }
    }

    public sealed record Incomplete : CSharpTypeDocumentOutcome
    {
        public Incomplete(
            CSharpTypeDocument document,
            ImmutableArray<int> failedBodyIds)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (failedBodyIds.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "An incomplete document requires failed body ids.",
                    nameof(failedBodyIds));
            }

            ImmutableArray<int> expected =
                [.. document.Bodies
                    .Where(static body =>
                        body.Outcome is CSharpTypeBodyOutcome.Unavailable
                            or CSharpTypeBodyOutcome.Failed)
                    .Select(static body => body.Id)];
            ImmutableArray<int> supplied =
                [.. failedBodyIds.Distinct().Order()];
            if (!expected.SequenceEqual(supplied))
            {
                throw new ArgumentException(
                    "Failed body ids must exactly identify incomplete body work.",
                    nameof(failedBodyIds));
            }

            Document = document;
            FailedBodyIds = supplied;
        }

        public CSharpTypeDocument Document { get; }

        public ImmutableArray<int> FailedBodyIds { get; }
    }

    public sealed record Unavailable : CSharpTypeDocumentOutcome
    {
        public Unavailable(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            AnnotatedSourceText.ValidateWellFormedUtf16(
                reason,
                nameof(reason),
                "Unavailable reason");
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed record Rejected : CSharpTypeDocumentOutcome
    {
        public Rejected(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            AnnotatedSourceText.ValidateWellFormedUtf16(
                reason,
                nameof(reason),
                "Rejected reason");
            Reason = reason;
        }

        public string Reason { get; }
    }
}
