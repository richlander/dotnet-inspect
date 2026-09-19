using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum ApiDeclarationKind
{
    Type,
    Method,
    Property,
    Event,
    Field,
}

public enum ApiDeclarationMetadataTable
{
    TypeDefinition,
    MethodDefinition,
    PropertyDefinition,
    EventDefinition,
    FieldDefinition,
    ExportedType,
}

public enum ApiDeclarationCorrespondenceStatus
{
    Exact,
    Absent,
    Ambiguous,
    Refused,
    Failed,
}

public enum ApiDeclarationCorrespondenceReason
{
    None,
    MissingDeclaringType,
    MissingSourceDeclaration,
    NoExactDeclarationUnderProfile,
    DuplicateTypeDeclarations,
    MultipleExactDeclarations,
    ForwardedTypeRequiresExplicitImage,
    ModuleExportRequiresExplicitImage,
    UnaddressableApiIdentity,
    InvalidEndpointAssociation,
    InvalidPhysicalLocation,
    MalformedMetadata,
    IncompleteCandidateSet,
    WorkLimitExceeded,
    UnreadableImage,
    InvalidImage,
    UnsupportedMetadataFormat,
}

public enum ApiDeclarationCorrespondenceStage
{
    None,
    SourceAcquisition,
    SourceTypeLookup,
    SourceDeclarationBinding,
    SourceAddressability,
    SourceProjection,
    DestinationAcquisition,
    DestinationTypeLookup,
    DestinationCandidateScan,
    DestinationAddressability,
    DestinationProjection,
}

/// <summary>
/// Erasing identity of one exact assembly acquisition registration. The weak
/// exact-object memoizer preserves reference equality without retaining the
/// registration or its artifact authority.
/// </summary>
public sealed class ApiDeclarationRegistrationIdentity
{
    static readonly ConditionalWeakTable<
        AssemblyAcquisitionRegistration,
        ApiDeclarationRegistrationIdentity> Identities = new();

    ApiDeclarationRegistrationIdentity()
    {
    }

    internal static ApiDeclarationRegistrationIdentity From(
        AssemblyAcquisitionRegistration registration)
        => Identities.GetValue(registration, static _ => new());

    /// <summary>
    /// Returns whether this identity was minted for the supplied live
    /// acquisition registration.
    /// </summary>
    public bool Matches(AssemblyAcquisitionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return ReferenceEquals(this, From(registration));
    }
}

/// <summary>
/// Exact acquired-image evidence retained without retaining an image opener,
/// reader, acquisition registration, or artifact authority.
/// </summary>
public sealed record ApiDeclarationEndpoint(
    ApiDeclarationRegistrationIdentity Registration,
    AssemblyReferenceIdentity Identity,
    Guid ModuleVersionId);

/// <summary>
/// Physical metadata location scoped to the actual image MVID.
/// </summary>
public readonly record struct MetadataDeclarationLocation(
    Guid ModuleVersionId,
    ApiDeclarationMetadataTable Table,
    int MetadataToken)
{
    public MetadataTypeDefinitionAddress? TypeAddress { get; init; }
    public MetadataMethodAddress? MethodAddress { get; init; }
}

/// <summary>
/// One exact, addressable API declaration in one acquired image.
/// </summary>
public sealed record ApiDeclarationReference
{
    internal ApiDeclarationReference(
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationKind kind,
        MetadataDeclarationLocation location,
        MemberAnchor? member)
    {
        Endpoint = endpoint;
        DeclaringType = declaringType;
        Kind = kind;
        Location = location;
        Member = member;
    }

    public ApiDeclarationEndpoint Endpoint { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public ApiDeclarationKind Kind { get; }
    public MetadataDeclarationLocation Location { get; }
    public MemberAnchor? Member { get; }
}

/// <summary>
/// Typed member selection paired with an exact declaring-Type lookup name.
/// </summary>
public sealed record ApiDeclarationMemberSelection
{
    public ApiDeclarationMemberSelection(
        ApiDeclarationKind kind,
        MemberAnchor anchor)
    {
        if (kind is not (
                ApiDeclarationKind.Method
                or ApiDeclarationKind.Property
                or ApiDeclarationKind.Event
                or ApiDeclarationKind.Field))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "A member selection must use a supported Member declaration kind.");
        }

        ArgumentNullException.ThrowIfNull(anchor);
        Kind = kind;
        Anchor = anchor;
    }

    public ApiDeclarationKind Kind { get; }
    public MemberAnchor Anchor { get; }
}

/// <summary>
/// One destination candidate relevant to a declaration-correspondence result.
/// </summary>
public sealed record ApiDeclarationCandidateEvidence(
    ApiDeclarationEndpoint Endpoint,
    MetadataTypeDefinitionName DeclaringType,
    ApiDeclarationKind Kind,
    MetadataDeclarationLocation? Location,
    MemberAnchor? Member,
    AssemblyReferenceIdentity? ForwardedTarget = null,
    ModuleFileReference? Module = null);

/// <summary>
/// Result of binding an exact source declaration to its acquired image.
/// </summary>
public sealed record ApiDeclarationBindingResult(
    ApiDeclarationCorrespondenceStatus Status,
    ApiDeclarationCorrespondenceReason Reason,
    ApiDeclarationCorrespondenceStage Stage,
    ApiDeclarationReference? Declaration,
    ImmutableArray<ApiDeclarationCandidateEvidence> Candidates,
    string? Detail)
{
    public bool IsExact =>
        Status == ApiDeclarationCorrespondenceStatus.Exact;
}

/// <summary>
/// Directional strict correspondence for one already-bound source
/// declaration and one explicitly designated destination image.
/// </summary>
public sealed record ApiDeclarationCorrespondenceResult(
    ApiDeclarationCorrespondenceStatus Status,
    ApiDeclarationCorrespondenceReason Reason,
    ApiDeclarationCorrespondenceStage Stage,
    ApiDeclarationReference Source,
    ApiDeclarationEndpoint? Destination,
    ApiDeclarationReference? Target,
    ImmutableArray<ApiDeclarationCandidateEvidence> Candidates,
    string? Detail)
{
    public bool IsExact =>
        Status == ApiDeclarationCorrespondenceStatus.Exact;
}

/// <summary>
/// Binds and matches exact TypeDef, MethodDef, Property, Event, and Field
/// declarations without exposing metadata readers to callers.
/// </summary>
public static class ApiDeclarationCorrespondence
{
    static readonly Limits DefaultLimits = new(
        MetadataSafetyPolicy.MaxMemorySafetyProjectionIntegrityRows,
        MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        MetadataSafetyPolicy.MaxCorrespondenceCandidates);

    public static ApiDeclarationBindingResult BindSource(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationMemberSelection? member = null,
        CancellationToken cancellationToken = default)
        => BindSource(
            source,
            declaringType,
            member,
            DefaultLimits,
            cancellationToken);

    internal static ApiDeclarationBindingResult BindSource(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationMemberSelection? member,
        Limits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        SnapshotOutcome sourceSnapshot = OpenSnapshot(
            source,
            ApiDeclarationCorrespondenceStage.SourceAcquisition);
        if (sourceSnapshot.Failure is { } sourceFailure)
            return BindingFailure(sourceFailure);

        AssemblyImageSnapshot snapshot = sourceSnapshot.Snapshot!;
        ApiDeclarationEndpoint endpoint = Endpoint(snapshot);
        using AssemblyImage image = AssemblyImage.Open(snapshot);
        MetadataReader reader = image.GetMetadataReader();
        try
        {
            ValidateProjectionIntegrity(
                reader,
                limits,
                cancellationToken);
            TypeLookup sourceType = LookupType(
                reader,
                endpoint,
                declaringType,
                ApiDeclarationCorrespondenceStage.SourceTypeLookup);
            if (sourceType.Failure is { } typeFailure)
                return BindingFailure(typeFailure, sourceType.Candidates);

            if (member is null)
            {
                ApiDeclarationReference declaration = CreateTypeReference(
                    reader,
                    endpoint,
                    declaringType,
                    sourceType.Handle);
                _ = new StructuralSignatureBuilder(
                        reader,
                        requireUniqueLocalDefinitions: true)
                    .BuildType(sourceType.Handle);
                return BindingExact(declaration);
            }

            MemberBinding binding = FindMemberByAnchor(
                reader,
                endpoint,
                declaringType,
                sourceType.Handle,
                member,
                limits,
                cancellationToken);
            if (binding.Failure is { } memberFailure)
                return BindingFailure(memberFailure, binding.Candidates);

            var signatures = new StructuralSignatureBuilder(
                reader,
                requireUniqueLocalDefinitions: true);
            _ = signatures.BuildType(sourceType.Handle);
            _ = BuildMemberKey(
                reader,
                signatures,
                member.Kind,
                HandleFromLocation(
                    reader,
                    binding.Declaration!.Location,
                    member.Kind));
            return BindingExact(binding.Declaration!);
        }
        catch (IncompleteCandidateSetException)
        {
            return BindingFailure(Failure(
                ApiDeclarationCorrespondenceStatus.Failed,
                ApiDeclarationCorrespondenceReason.IncompleteCandidateSet,
                ApiDeclarationCorrespondenceStage.SourceDeclarationBinding,
                "The source metadata relationships do not expose a complete candidate set."));
        }
        catch (BudgetExceededException)
        {
            return BindingFailure(Failure(
                ApiDeclarationCorrespondenceStatus.Failed,
                ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
                ApiDeclarationCorrespondenceStage.SourceProjection,
                "The source declaration scan exceeded its finite work limit."));
        }
        catch (Exception ex) when (IsMetadataFailure(ex))
        {
            return BindingFailure(Failure(
                ApiDeclarationCorrespondenceStatus.Failed,
                ApiDeclarationCorrespondenceReason.MalformedMetadata,
                ApiDeclarationCorrespondenceStage.SourceProjection,
                "Required source declaration metadata could not be decoded."));
        }
    }

    public static ApiDeclarationCorrespondenceResult Match(
        ResolvedAssemblyReference sourceAssembly,
        ApiDeclarationReference source,
        ResolvedAssemblyReference destinationAssembly,
        CancellationToken cancellationToken = default)
        => Match(
            sourceAssembly,
            source,
            destinationAssembly,
            DefaultLimits,
            cancellationToken);

    internal static ApiDeclarationCorrespondenceResult Match(
        ResolvedAssemblyReference sourceAssembly,
        ApiDeclarationReference source,
        ResolvedAssemblyReference destinationAssembly,
        Limits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationAssembly);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (!source.Endpoint.Registration.Matches(
                sourceAssembly.Registration))
        {
            return CorrespondenceFailure(
                source,
                destination: null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.InvalidEndpointAssociation,
                    ApiDeclarationCorrespondenceStage.SourceAcquisition,
                    "The source descriptor is not the declaration's acquisition registration."));
        }

        SnapshotOutcome sourceSnapshot = OpenSnapshot(
            sourceAssembly,
            ApiDeclarationCorrespondenceStage.SourceAcquisition);
        if (sourceSnapshot.Failure is { } sourceAcquisitionFailure)
        {
            return CorrespondenceFailure(
                source,
                destination: null,
                sourceAcquisitionFailure);
        }

        AssemblyImageSnapshot sourceImage = sourceSnapshot.Snapshot!;
        if (!EndpointMatches(source.Endpoint, sourceImage))
        {
            return CorrespondenceFailure(
                source,
                destination: null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.InvalidEndpointAssociation,
                    ApiDeclarationCorrespondenceStage.SourceAcquisition,
                    "The opened source image does not match the declaration endpoint."));
        }

        SnapshotOutcome destinationSnapshot = OpenSnapshot(
            destinationAssembly,
            ApiDeclarationCorrespondenceStage.DestinationAcquisition);
        if (destinationSnapshot.Failure is { } destinationAcquisitionFailure)
        {
            return CorrespondenceFailure(
                source,
                destination: null,
                destinationAcquisitionFailure);
        }

        AssemblyImageSnapshot destinationImage =
            destinationSnapshot.Snapshot!;
        ApiDeclarationEndpoint destinationEndpoint =
            Endpoint(destinationImage);

        using AssemblyImage sourceOwner = AssemblyImage.Open(sourceImage);
        using AssemblyImage destinationOwner =
            AssemblyImage.Open(destinationImage);
        MetadataReader sourceReader = sourceOwner.GetMetadataReader();
        MetadataReader destinationReader =
            destinationOwner.GetMetadataReader();
        ApiDeclarationCorrespondenceStage failureStage =
            ApiDeclarationCorrespondenceStage.SourceProjection;

        try
        {
            ValidateProjectionIntegrity(
                sourceReader,
                limits,
                cancellationToken);
            failureStage =
                ApiDeclarationCorrespondenceStage.DestinationProjection;
            ValidateProjectionIntegrity(
                destinationReader,
                limits,
                cancellationToken);

            failureStage =
                ApiDeclarationCorrespondenceStage.SourceProjection;
            SourceProjection sourceProjection = RevalidateSource(
                sourceReader,
                source,
                limits,
                cancellationToken);
            if (sourceProjection.Failure is { } sourceFailure)
            {
                return CorrespondenceFailure(
                    source,
                    destinationEndpoint,
                    sourceFailure,
                    sourceProjection.Candidates);
            }

            failureStage =
                ApiDeclarationCorrespondenceStage.DestinationTypeLookup;
            TypeLookup destinationType = LookupType(
                destinationReader,
                destinationEndpoint,
                source.DeclaringType,
                ApiDeclarationCorrespondenceStage.DestinationTypeLookup);
            if (destinationType.Failure is { } destinationTypeFailure)
            {
                return CorrespondenceFailure(
                    source,
                    destinationEndpoint,
                    destinationTypeFailure,
                    destinationType.Candidates);
            }

            failureStage =
                ApiDeclarationCorrespondenceStage.DestinationProjection;
            var destinationSignatures =
                new StructuralSignatureBuilder(
                    destinationReader,
                    requireUniqueLocalDefinitions: true);
            string destinationTypeKey =
                destinationSignatures.BuildType(destinationType.Handle);
            ApiDeclarationReference destinationTypeDeclaration =
                CreateTypeReference(
                    destinationReader,
                    destinationEndpoint,
                    source.DeclaringType,
                    destinationType.Handle);
            if (!StringComparer.Ordinal.Equals(
                    sourceProjection.TypeKey,
                    destinationTypeKey))
            {
                return CorrespondenceFailure(
                    source,
                    destinationEndpoint,
                    Failure(
                        ApiDeclarationCorrespondenceStatus.Absent,
                        ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
                        ApiDeclarationCorrespondenceStage.DestinationProjection,
                        "The destination declaring Type differs under the strict profile."),
                    [Candidate(destinationTypeDeclaration)]);
            }

            if (source.Kind == ApiDeclarationKind.Type)
            {
                return CorrespondenceExact(
                    source,
                    destinationEndpoint,
                    destinationTypeDeclaration);
            }

            failureStage =
                ApiDeclarationCorrespondenceStage.DestinationCandidateScan;
            MemberMatch match = MatchMember(
                destinationReader,
                destinationEndpoint,
                source,
                sourceProjection.MemberKey!,
                destinationType.Handle,
                destinationSignatures,
                limits,
                cancellationToken);
            if (match.Failure is { } matchFailure)
            {
                return CorrespondenceFailure(
                    source,
                    destinationEndpoint,
                    matchFailure,
                    match.Candidates);
            }

            return CorrespondenceExact(
                source,
                destinationEndpoint,
                match.Target!);
        }
        catch (IncompleteCandidateSetException)
        {
            return CorrespondenceFailure(
                source,
                destinationEndpoint,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.IncompleteCandidateSet,
                    failureStage,
                    "Metadata relationships do not expose a complete candidate set."));
        }
        catch (BudgetExceededException)
        {
            return CorrespondenceFailure(
                source,
                destinationEndpoint,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
                    failureStage,
                    "Declaration correspondence exceeded its finite work limit."));
        }
        catch (Exception ex) when (IsMetadataFailure(ex))
        {
            return CorrespondenceFailure(
                source,
                destinationEndpoint,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.MalformedMetadata,
                    failureStage,
                    "Required declaration metadata could not be decoded."));
        }
    }

    static SourceProjection RevalidateSource(
        MetadataReader reader,
        ApiDeclarationReference source,
        Limits limits,
        CancellationToken cancellationToken)
    {
        TypeLookup sourceType = LookupType(
            reader,
            source.Endpoint,
            source.DeclaringType,
            ApiDeclarationCorrespondenceStage.SourceTypeLookup);
        if (sourceType.Failure is { } typeFailure)
            return new(null, null, typeFailure, sourceType.Candidates);

        if (!LocationMatches(
                reader,
                source.Location,
                ApiDeclarationKind.Type,
                sourceType.Handle)
            && source.Kind == ApiDeclarationKind.Type)
        {
            return new(
                null,
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.InvalidPhysicalLocation,
                    ApiDeclarationCorrespondenceStage.SourceDeclarationBinding,
                    "The source Type location no longer identifies the selected declaration."),
                []);
        }

        var signatures = new StructuralSignatureBuilder(
            reader,
            requireUniqueLocalDefinitions: true);
        string typeKey = signatures.BuildType(sourceType.Handle);
        if (source.Kind == ApiDeclarationKind.Type)
            return new(typeKey, null, null, []);

        if (source.Member is null)
        {
            return new(
                null,
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.InvalidPhysicalLocation,
                    ApiDeclarationCorrespondenceStage.SourceDeclarationBinding,
                    "The source Member declaration has no retained MemberAnchor."),
                []);
        }

        ApiDeclarationMemberSelection selection =
            new(source.Kind, source.Member);
        MemberBinding binding = FindMemberByAnchor(
            reader,
            source.Endpoint,
            source.DeclaringType,
            sourceType.Handle,
            selection,
            limits,
            cancellationToken);
        if (binding.Failure is { } memberFailure)
            return new(null, null, memberFailure, binding.Candidates);
        if (binding.Declaration!.Location != source.Location)
        {
            return new(
                null,
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.InvalidPhysicalLocation,
                    ApiDeclarationCorrespondenceStage.SourceDeclarationBinding,
                    "The source location does not identify the bound Member declaration."),
                binding.Candidates);
        }

        EntityHandle handle = HandleFromLocation(
            reader,
            binding.Declaration.Location,
            source.Kind);
        string memberKey = BuildMemberKey(
            reader,
            signatures,
            source.Kind,
            handle);
        return new(typeKey, memberKey, null, []);
    }

    static MemberBinding FindMemberByAnchor(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName declaringType,
        TypeDefinitionHandle declaringTypeHandle,
        ApiDeclarationMemberSelection selection,
        Limits limits,
        CancellationToken cancellationToken)
    {
        List<ApiDeclarationReference> matches = [];
        var projectionBudget = new ProjectionWorkBudget(
            MetadataSafetyPolicy.MaxClassificationScanWorkChars);
        MethodAnchorProjector? methodProjector = null;
        int scanned = 0;
        foreach (EntityHandle handle in EnumerateMembers(
                     reader,
                     declaringTypeHandle,
                     selection.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned > limits.MaxMemberRows)
                throw new BudgetExceededException();

            // MemberAnchor retains the exact raw name, so unlike a signature
            // projection this lookup fact cannot exclude an aliasing candidate.
            string name = ReadMemberName(reader, selection.Kind, handle);
            if (!StringComparer.Ordinal.Equals(
                    name,
                    selection.Anchor.MemberName))
            {
                continue;
            }

            if (selection.Kind == ApiDeclarationKind.Method)
            {
                methodProjector ??= new(
                    reader,
                    declaringTypeHandle,
                    projectionBudget);
            }
            MemberAnchor anchor = CreateAnchor(
                reader,
                declaringTypeHandle,
                selection.Kind,
                handle,
                name,
                methodProjector,
                projectionBudget);
            if (anchor == selection.Anchor)
            {
                matches.Add(CreateMemberReference(
                    reader,
                    endpoint,
                    declaringType,
                    selection.Kind,
                    handle,
                    anchor));
            }
        }

        ImmutableArray<ApiDeclarationCandidateEvidence> candidates =
            [.. matches.Select(Candidate)];
        return matches.Count switch
        {
            0 => new(
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Absent,
                    ApiDeclarationCorrespondenceReason.MissingSourceDeclaration,
                    ApiDeclarationCorrespondenceStage.SourceDeclarationBinding,
                    "The source image does not contain the selected Member declaration."),
                candidates),
            1 => new(matches[0], null, candidates),
            _ => new(
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Refused,
                    ApiDeclarationCorrespondenceReason.UnaddressableApiIdentity,
                    ApiDeclarationCorrespondenceStage.SourceAddressability,
                    "Multiple physical source Members share the selected MemberAnchor."),
                candidates),
        };
    }

    static MemberMatch MatchMember(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        ApiDeclarationReference source,
        string sourceKey,
        TypeDefinitionHandle declaringTypeHandle,
        StructuralSignatureBuilder signatures,
        Limits limits,
        CancellationToken cancellationToken)
    {
        List<ApiDeclarationReference> relevant = [];
        List<ApiDeclarationReference> exact = [];
        var projectionBudget = new ProjectionWorkBudget(
            MetadataSafetyPolicy.MaxClassificationScanWorkChars);
        MethodAnchorProjector? methodProjector = null;
        int scanned = 0;
        foreach (EntityHandle handle in EnumerateMembers(
                     reader,
                     declaringTypeHandle,
                     source.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned > limits.MaxMemberRows)
                throw new BudgetExceededException();

            // The strict candidate set is same-kind and same-name; project no
            // unrelated signature before this exact ordinal lookup succeeds.
            string name = ReadMemberName(reader, source.Kind, handle);
            if (!StringComparer.Ordinal.Equals(
                    name,
                    source.Member!.MemberName))
            {
                continue;
            }

            if (source.Kind == ApiDeclarationKind.Method)
            {
                methodProjector ??= new(
                    reader,
                    declaringTypeHandle,
                    projectionBudget);
            }
            MemberAnchor anchor = CreateAnchor(
                reader,
                declaringTypeHandle,
                source.Kind,
                handle,
                name,
                methodProjector,
                projectionBudget);
            ApiDeclarationReference candidate = CreateMemberReference(
                reader,
                endpoint,
                source.DeclaringType,
                source.Kind,
                handle,
                anchor);
            relevant.Add(candidate);
            if (relevant.Count > limits.MaxCandidates)
                throw new BudgetExceededException();

            string candidateKey = BuildMemberKey(
                reader,
                signatures,
                source.Kind,
                handle);
            if (StringComparer.Ordinal.Equals(
                    sourceKey,
                    candidateKey))
            {
                exact.Add(candidate);
            }
        }

        ImmutableArray<ApiDeclarationCandidateEvidence> candidates =
            [.. relevant.Select(Candidate)];
        if (exact.Count == 0)
        {
            return new(
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Absent,
                    ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
                    ApiDeclarationCorrespondenceStage.DestinationCandidateScan,
                    "No destination declaration matches the strict profile."),
                candidates);
        }
        if (exact.Count > 1)
        {
            return new(
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Ambiguous,
                    ApiDeclarationCorrespondenceReason.MultipleExactDeclarations,
                    ApiDeclarationCorrespondenceStage.DestinationCandidateScan,
                    "Multiple destination declarations match the strict profile."),
                [.. exact.Select(Candidate)]);
        }

        ApiDeclarationReference selected = exact[0];
        int addressMatches = relevant.Count(
            candidate => candidate.Member == selected.Member);
        if (addressMatches != 1)
        {
            return new(
                null,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Refused,
                    ApiDeclarationCorrespondenceReason.UnaddressableApiIdentity,
                    ApiDeclarationCorrespondenceStage.DestinationAddressability,
                    "The strict destination match is not uniquely addressable by MemberAnchor."),
                candidates);
        }

        return new(selected, null, candidates);
    }

    static TypeLookup LookupType(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName name,
        ApiDeclarationCorrespondenceStage stage)
    {
        TypeDeclarationResult result =
            MetadataTypeDeclarationProbe.Probe(reader, name);
        return result switch
        {
            TypeDeclarationResult.Defined defined => new(
                TypeDefinitionHandleFromToken(defined.Definition),
                null,
                []),
            TypeDeclarationResult.Missing => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Absent,
                    ApiDeclarationCorrespondenceReason.MissingDeclaringType,
                    stage,
                    "The image does not declare the exact Type name."),
                []),
            TypeDeclarationResult.Forwarded forwarded => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Refused,
                    ApiDeclarationCorrespondenceReason.ForwardedTypeRequiresExplicitImage,
                    stage,
                    "The exact Type is forwarded and requires another explicit image."),
                ForwardedCandidates(
                    endpoint,
                    name,
                    forwarded.Declarations,
                    forwarded.Target)),
            TypeDeclarationResult.ExportedFromModule exported => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Refused,
                    ApiDeclarationCorrespondenceReason.ModuleExportRequiresExplicitImage,
                    stage,
                    "The exact Type is exported from another module."),
                ModuleExportCandidates(
                    endpoint,
                    name,
                    exported.Declarations,
                    exported.Module)),
            TypeDeclarationResult.Ambiguous ambiguous => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Ambiguous,
                    ApiDeclarationCorrespondenceReason.DuplicateTypeDeclarations,
                    stage,
                    "Multiple declarations compete for the exact Type name."),
                TypeCandidates(
                    reader,
                    endpoint,
                    name,
                    ambiguous.Candidates)),
            TypeDeclarationResult.BudgetExceeded => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
                    stage,
                    "The exact Type declaration lookup exceeded its "
                        + "finite work limit."),
                []),
            TypeDeclarationResult.Rejected => new(
                default,
                Failure(
                    ApiDeclarationCorrespondenceStatus.Failed,
                    ApiDeclarationCorrespondenceReason.MalformedMetadata,
                    stage,
                    "The exact Type declaration could not be read."),
                []),
            _ => throw new InvalidOperationException(
                "Unknown Type declaration result."),
        };
    }

    static ImmutableArray<ApiDeclarationCandidateEvidence> TypeCandidates(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName name,
        ImmutableArray<TypeDeclarationCandidate> candidates)
    {
        var result =
            ImmutableArray.CreateBuilder<ApiDeclarationCandidateEvidence>();
        foreach (TypeDeclarationCandidate candidate in candidates)
        {
            switch (candidate)
            {
                case TypeDeclarationCandidate.Definition definition:
                    TypeDefinitionHandle handle =
                        TypeDefinitionHandleFromToken(definition.Token);
                    result.Add(Candidate(CreateTypeReference(
                        reader,
                        endpoint,
                        name,
                        handle)));
                    break;
                case TypeDeclarationCandidate.Forwarder forwarder:
                    result.AddRange(ForwardedCandidates(
                        endpoint,
                        name,
                        forwarder.Declarations,
                        forwarder.Target));
                    break;
                case TypeDeclarationCandidate.ModuleExport module:
                    result.AddRange(ModuleExportCandidates(
                        endpoint,
                        name,
                        module.Declarations,
                        module.Module));
                    break;
            }
        }
        return result.ToImmutable();
    }

    static ImmutableArray<ApiDeclarationCandidateEvidence>
        ForwardedCandidates(
            ApiDeclarationEndpoint endpoint,
            MetadataTypeDefinitionName name,
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        =>
        [
            .. declarations.Select(token =>
                new ApiDeclarationCandidateEvidence(
                    endpoint,
                    name,
                    ApiDeclarationKind.Type,
                    ExportedTypeLocation(endpoint, token),
                    Member: null,
                    ForwardedTarget: target)),
        ];

    static ImmutableArray<ApiDeclarationCandidateEvidence>
        ModuleExportCandidates(
            ApiDeclarationEndpoint endpoint,
            MetadataTypeDefinitionName name,
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        =>
        [
            .. declarations.Select(token =>
                new ApiDeclarationCandidateEvidence(
                    endpoint,
                    name,
                    ApiDeclarationKind.Type,
                    ExportedTypeLocation(endpoint, token),
                    Member: null,
                    Module: module)),
        ];

    static MetadataDeclarationLocation ExportedTypeLocation(
        ApiDeclarationEndpoint endpoint,
        ExportedTypeToken token)
        => new(
            endpoint.ModuleVersionId,
            ApiDeclarationMetadataTable.ExportedType,
            token.Value);

    static string BuildMemberKey(
        MetadataReader reader,
        StructuralSignatureBuilder signatures,
        ApiDeclarationKind kind,
        EntityHandle handle)
        => kind switch
        {
            ApiDeclarationKind.Method =>
                signatures.BuildDeclarationMethod(
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)handle)),
            ApiDeclarationKind.Property =>
                signatures.BuildDeclarationProperty(
                    reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)handle),
                    ReadAccessorStaticness(
                        reader,
                        reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)handle)
                            .GetAccessors(),
                        ApiDeclarationKind.Property)),
            ApiDeclarationKind.Event =>
                signatures.BuildDeclarationEvent(
                    reader.GetEventDefinition(
                        (EventDefinitionHandle)handle),
                    ReadAccessorStaticness(
                        reader,
                        reader.GetEventDefinition(
                            (EventDefinitionHandle)handle)
                            .GetAccessors(),
                        ApiDeclarationKind.Event)),
            ApiDeclarationKind.Field =>
                signatures.BuildDeclarationField(
                    reader.GetFieldDefinition(
                        (FieldDefinitionHandle)handle)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Only Member declarations have Member profile keys."),
        };

    static bool ReadAccessorStaticness(
        MetadataReader reader,
        PropertyAccessors accessors,
        ApiDeclarationKind kind)
        => ReadAccessorStaticness(
            reader,
            [accessors.Getter, accessors.Setter, .. accessors.Others],
            kind);

    static bool ReadAccessorStaticness(
        MetadataReader reader,
        EventAccessors accessors,
        ApiDeclarationKind kind)
        => ReadAccessorStaticness(
            reader,
            [
                accessors.Adder,
                accessors.Remover,
                accessors.Raiser,
                .. accessors.Others,
            ],
            kind);

    static bool ReadAccessorStaticness(
        MetadataReader reader,
        IEnumerable<MethodDefinitionHandle> accessors,
        ApiDeclarationKind kind)
    {
        bool? isStatic = null;
        foreach (MethodDefinitionHandle accessor in accessors)
        {
            if (accessor.IsNil)
                continue;
            bool current =
                (reader.GetMethodDefinition(accessor).Attributes
                    & MethodAttributes.Static) != 0;
            if (isStatic is bool established
                && established != current)
            {
                throw new BadImageFormatException(
                    $"The {kind} accessors contradict each other about staticness.");
            }
            isStatic = current;
        }

        return isStatic
            ?? throw new BadImageFormatException(
                $"The {kind} has no accessor from which staticness can be established.");
    }

    static IEnumerable<EntityHandle> EnumerateMembers(
        MetadataReader reader,
        TypeDefinitionHandle declaringType,
        ApiDeclarationKind kind)
    {
        TypeDefinition type = reader.GetTypeDefinition(declaringType);
        return kind switch
        {
            ApiDeclarationKind.Method =>
                type.GetMethods().Select(static handle => (EntityHandle)handle),
            ApiDeclarationKind.Property =>
                type.GetProperties().Select(static handle => (EntityHandle)handle),
            ApiDeclarationKind.Event =>
                type.GetEvents().Select(static handle => (EntityHandle)handle),
            ApiDeclarationKind.Field =>
                type.GetFields().Select(static handle => (EntityHandle)handle),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Only Member declaration kinds can be enumerated."),
        };
    }

    static MemberAnchor CreateAnchor(
        MetadataReader reader,
        TypeDefinitionHandle declaringType,
        ApiDeclarationKind kind,
        EntityHandle handle,
        string name,
        MethodAnchorProjector? methodProjector,
        ProjectionWorkBudget projectionBudget)
        => kind switch
        {
            ApiDeclarationKind.Method =>
                (methodProjector
                    ?? throw new InvalidOperationException(
                        "Method projection requires a method projector."))
                .Create(
                    (MethodDefinitionHandle)handle,
                    name),
            ApiDeclarationKind.Property =>
                CreateNonMethodAnchor(
                    projectionBudget,
                    (ref int remaining) =>
                    {
                        PropertyDefinition definition =
                            reader.GetPropertyDefinition(
                                (PropertyDefinitionHandle)handle);
                        return ApiMemberIdentity.CreatePropertyAnchor(
                            reader,
                            declaringType,
                            definition,
                            ref remaining);
                    }),
            ApiDeclarationKind.Event =>
                CreateNonMethodAnchor(
                    projectionBudget,
                    (ref int remaining) =>
                    {
                        EventDefinition definition =
                            reader.GetEventDefinition(
                                (EventDefinitionHandle)handle);
                        return ApiMemberIdentity.CreateEventAnchor(
                            reader,
                            declaringType,
                            definition,
                            ref remaining);
                    }),
            ApiDeclarationKind.Field =>
                CreateNonMethodAnchor(
                    projectionBudget,
                    (ref int remaining) =>
                    {
                        FieldDefinition definition =
                            reader.GetFieldDefinition(
                                (FieldDefinitionHandle)handle);
                        return ApiMemberIdentity.CreateFieldAnchor(
                            reader,
                            declaringType,
                            definition,
                            ref remaining);
                    }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    delegate MemberAnchor NonMethodAnchorFactory(ref int remaining);

    static MemberAnchor CreateNonMethodAnchor(
        ProjectionWorkBudget budget,
        NonMethodAnchorFactory create)
    {
        int remaining = budget.Remaining;
        try
        {
            return create(ref remaining);
        }
        catch (BadImageFormatException) when (remaining <= 0)
        {
            throw new BudgetExceededException();
        }
        finally
        {
            budget.SetRemaining(remaining);
        }
    }

    static string ReadMemberName(
        MetadataReader reader,
        ApiDeclarationKind kind,
        EntityHandle handle)
        => MetadataSafetyPolicy.ReadStructuralString(
            reader,
            kind switch
            {
                ApiDeclarationKind.Method =>
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)handle).Name,
                ApiDeclarationKind.Property =>
                    reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)handle).Name,
                ApiDeclarationKind.Event =>
                    reader.GetEventDefinition(
                        (EventDefinitionHandle)handle).Name,
                ApiDeclarationKind.Field =>
                    reader.GetFieldDefinition(
                        (FieldDefinitionHandle)handle).Name,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });

    static ApiDeclarationReference CreateTypeReference(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName name,
        TypeDefinitionHandle handle)
    {
        MetadataTypeDefinitionAddress address = new(
            endpoint.ModuleVersionId,
            TypeDefinitionToken.FromHandle(reader, handle));
        return new ApiDeclarationReference(
            endpoint,
            name,
            ApiDeclarationKind.Type,
            new MetadataDeclarationLocation(
                endpoint.ModuleVersionId,
                ApiDeclarationMetadataTable.TypeDefinition,
                address.Definition.Value)
            {
                TypeAddress = address,
            },
            member: null);
    }

    static ApiDeclarationReference CreateMemberReference(
        MetadataReader reader,
        ApiDeclarationEndpoint endpoint,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationKind kind,
        EntityHandle handle,
        MemberAnchor anchor)
    {
        int token = MetadataTokens.GetToken(handle);
        MetadataMethodAddress? methodAddress =
            kind == ApiDeclarationKind.Method
                ? MetadataMethodAddress.Create(
                    reader,
                    (MethodDefinitionHandle)handle)
                : null;
        return new ApiDeclarationReference(
            endpoint,
            declaringType,
            kind,
            new MetadataDeclarationLocation(
                endpoint.ModuleVersionId,
                Table(kind),
                token)
            {
                MethodAddress = methodAddress,
            },
            anchor);
    }

    static ApiDeclarationCandidateEvidence Candidate(
        ApiDeclarationReference declaration)
        => new(
            declaration.Endpoint,
            declaration.DeclaringType,
            declaration.Kind,
            declaration.Location,
            declaration.Member);

    static ApiDeclarationMetadataTable Table(ApiDeclarationKind kind)
        => kind switch
        {
            ApiDeclarationKind.Type =>
                ApiDeclarationMetadataTable.TypeDefinition,
            ApiDeclarationKind.Method =>
                ApiDeclarationMetadataTable.MethodDefinition,
            ApiDeclarationKind.Property =>
                ApiDeclarationMetadataTable.PropertyDefinition,
            ApiDeclarationKind.Event =>
                ApiDeclarationMetadataTable.EventDefinition,
            ApiDeclarationKind.Field =>
                ApiDeclarationMetadataTable.FieldDefinition,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static EntityHandle HandleFromLocation(
        MetadataReader reader,
        MetadataDeclarationLocation location,
        ApiDeclarationKind kind)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(
            location.MetadataToken);
        if (!LocationMatches(reader, location, kind, handle))
        {
            throw new BadImageFormatException(
                "The declaration location is not valid in this metadata image.");
        }
        return handle;
    }

    static bool LocationMatches(
        MetadataReader reader,
        MetadataDeclarationLocation location,
        ApiDeclarationKind kind,
        EntityHandle expected)
    {
        if (location.ModuleVersionId
            != MetadataModuleIdentity.ReadVersionId(reader)
            || location.Table != Table(kind))
        {
            return false;
        }

        EntityHandle actual;
        try
        {
            actual = MetadataTokens.EntityHandle(location.MetadataToken);
        }
        catch (ArgumentException)
        {
            return false;
        }
        if (actual.Kind != HandleKindFor(kind)
            || actual != expected)
        {
            return false;
        }

        int row = MetadataTokens.GetRowNumber(actual);
        return row > 0 && row <= RowCount(reader, kind);
    }

    static HandleKind HandleKindFor(ApiDeclarationKind kind)
        => kind switch
        {
            ApiDeclarationKind.Type => HandleKind.TypeDefinition,
            ApiDeclarationKind.Method => HandleKind.MethodDefinition,
            ApiDeclarationKind.Property => HandleKind.PropertyDefinition,
            ApiDeclarationKind.Event => HandleKind.EventDefinition,
            ApiDeclarationKind.Field => HandleKind.FieldDefinition,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static int RowCount(
        MetadataReader reader,
        ApiDeclarationKind kind)
        => reader.GetTableRowCount(kind switch
        {
            ApiDeclarationKind.Type => TableIndex.TypeDef,
            ApiDeclarationKind.Method => TableIndex.MethodDef,
            ApiDeclarationKind.Property => TableIndex.Property,
            ApiDeclarationKind.Event => TableIndex.Event,
            ApiDeclarationKind.Field => TableIndex.Field,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    static TypeDefinitionHandle TypeDefinitionHandleFromToken(
        TypeDefinitionToken token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token.Value);
        if (handle.Kind != HandleKind.TypeDefinition)
        {
            throw new BadImageFormatException(
                "The declaration result did not carry a TypeDef token.");
        }
        return (TypeDefinitionHandle)handle;
    }

    static void ValidateProjectionIntegrity(
        MetadataReader reader,
        Limits limits,
        CancellationToken cancellationToken)
    {
        long rows = (long)reader.GetTableRowCount(TableIndex.TypeDef)
            + reader.GetTableRowCount(TableIndex.NestedClass)
            + reader.GetTableRowCount(TableIndex.MethodDef)
            + reader.GetTableRowCount(TableIndex.Field)
            + reader.GetTableRowCount(TableIndex.Property)
            + reader.GetTableRowCount(TableIndex.Event)
            + reader.GetTableRowCount(TableIndex.MethodSemantics);
        if (rows > limits.MaxProjectionRows)
            throw new BudgetExceededException();

        int nested = 0;
        int methods = 0;
        int fields = 0;
        int properties = 0;
        int events = 0;
        int semantics = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (TypeDefinitionHandle nestedHandle
                     in type.GetNestedTypes())
            {
                nested++;
                if (reader.GetTypeDefinition(nestedHandle).GetDeclaringType()
                    != typeHandle)
                {
                    throw new IncompleteCandidateSetException();
                }
            }

            foreach (MethodDefinitionHandle methodHandle
                     in type.GetMethods())
            {
                methods++;
                if (reader.GetMethodDefinition(methodHandle).GetDeclaringType()
                    != typeHandle)
                {
                    throw new IncompleteCandidateSetException();
                }
            }

            foreach (FieldDefinitionHandle fieldHandle
                     in type.GetFields())
            {
                fields++;
                if (reader.GetFieldDefinition(fieldHandle).GetDeclaringType()
                    != typeHandle)
                {
                    throw new IncompleteCandidateSetException();
                }
            }

            foreach (PropertyDefinitionHandle propertyHandle
                     in type.GetProperties())
            {
                properties++;
                PropertyDefinition property =
                    reader.GetPropertyDefinition(propertyHandle);
                if (property.GetDeclaringType() != typeHandle)
                    throw new IncompleteCandidateSetException();
                PropertyAccessors accessors = property.GetAccessors();
                semantics += CountAndValidateAccessors(
                    reader,
                    typeHandle,
                    [accessors.Getter, accessors.Setter, .. accessors.Others]);
            }

            foreach (EventDefinitionHandle eventHandle
                     in type.GetEvents())
            {
                events++;
                EventDefinition eventDefinition =
                    reader.GetEventDefinition(eventHandle);
                if (eventDefinition.GetDeclaringType() != typeHandle)
                    throw new IncompleteCandidateSetException();
                EventAccessors accessors = eventDefinition.GetAccessors();
                semantics += CountAndValidateAccessors(
                    reader,
                    typeHandle,
                    [
                        accessors.Adder,
                        accessors.Remover,
                        accessors.Raiser,
                        .. accessors.Others,
                    ]);
            }
        }

        if (nested != reader.GetTableRowCount(TableIndex.NestedClass)
            || methods != reader.GetTableRowCount(TableIndex.MethodDef)
            || fields != reader.GetTableRowCount(TableIndex.Field)
            || properties != reader.GetTableRowCount(TableIndex.Property)
            || events != reader.GetTableRowCount(TableIndex.Event)
            || semantics != reader.GetTableRowCount(TableIndex.MethodSemantics))
        {
            throw new IncompleteCandidateSetException();
        }
    }

    static int CountAndValidateAccessors(
        MetadataReader reader,
        TypeDefinitionHandle owner,
        IEnumerable<MethodDefinitionHandle> accessors)
    {
        int count = 0;
        foreach (MethodDefinitionHandle accessor in accessors)
        {
            if (accessor.IsNil)
                continue;
            count++;
            if (reader.GetMethodDefinition(accessor).GetDeclaringType()
                != owner)
            {
                throw new IncompleteCandidateSetException();
            }
        }
        return count;
    }

    static SnapshotOutcome OpenSnapshot(
        ResolvedAssemblyReference assembly,
        ApiDeclarationCorrespondenceStage stage)
    {
        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.Open(
                assembly,
                static length =>
                    length <= AssemblyImageSnapshot.DefaultMaxRetainedImageBytes,
                static _ => { });
        if (result is AssemblyImageSnapshotResult.Ready ready)
            return new(ready.Snapshot, null);

        CandidateOpenFailure failure =
            ((AssemblyImageSnapshotResult.Rejected)result).Failure;
        ApiDeclarationCorrespondenceReason reason = failure.Kind switch
        {
            CandidateOpenFailureKind.ResourceBudget =>
                ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                ApiDeclarationCorrespondenceReason.UnsupportedMetadataFormat,
            CandidateOpenFailureKind.InvalidImage =>
                ApiDeclarationCorrespondenceReason.InvalidImage,
            _ => ApiDeclarationCorrespondenceReason.UnreadableImage,
        };
        return new(
            null,
            Failure(
                ApiDeclarationCorrespondenceStatus.Failed,
                reason,
                stage,
                failure.Detail));
    }

    static ApiDeclarationEndpoint Endpoint(
        AssemblyImageSnapshot snapshot)
        => new(
            ApiDeclarationRegistrationIdentity.From(
                snapshot.Registration),
            snapshot.Identity,
            snapshot.ModuleVersionId);

    static bool EndpointMatches(
        ApiDeclarationEndpoint endpoint,
        AssemblyImageSnapshot snapshot)
        => endpoint.Registration.Matches(snapshot.Registration)
            && endpoint.Identity.IsEquivalentTo(snapshot.Identity)
            && endpoint.ModuleVersionId == snapshot.ModuleVersionId;

    static ApiDeclarationBindingResult BindingExact(
        ApiDeclarationReference declaration)
        => new(
            ApiDeclarationCorrespondenceStatus.Exact,
            ApiDeclarationCorrespondenceReason.None,
            ApiDeclarationCorrespondenceStage.None,
            declaration,
            [Candidate(declaration)],
            Detail: null);

    static ApiDeclarationBindingResult BindingFailure(
        OperationFailure failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> candidates = default)
        => new(
            failure.Status,
            failure.Reason,
            failure.Stage,
            Declaration: null,
            candidates.IsDefault ? [] : candidates,
            failure.Detail);

    static ApiDeclarationCorrespondenceResult CorrespondenceExact(
        ApiDeclarationReference source,
        ApiDeclarationEndpoint destination,
        ApiDeclarationReference target)
        => new(
            ApiDeclarationCorrespondenceStatus.Exact,
            ApiDeclarationCorrespondenceReason.None,
            ApiDeclarationCorrespondenceStage.None,
            source,
            destination,
            target,
            [Candidate(target)],
            Detail: null);

    static ApiDeclarationCorrespondenceResult CorrespondenceFailure(
        ApiDeclarationReference source,
        ApiDeclarationEndpoint? destination,
        OperationFailure failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> candidates = default)
        => new(
            failure.Status,
            failure.Reason,
            failure.Stage,
            source,
            destination,
            Target: null,
            candidates.IsDefault ? [] : candidates,
            failure.Detail);

    static OperationFailure Failure(
        ApiDeclarationCorrespondenceStatus status,
        ApiDeclarationCorrespondenceReason reason,
        ApiDeclarationCorrespondenceStage stage,
        string detail)
        => new(status, reason, stage, detail);

    static bool IsMetadataFailure(Exception exception)
        => exception is BadImageFormatException
            or ArgumentException
            or ArgumentOutOfRangeException
            or InvalidOperationException
            or IndexOutOfRangeException
            or OverflowException
            or IncompleteCandidateSetException;

    internal sealed record Limits(
        int MaxProjectionRows,
        int MaxMemberRows,
        int MaxCandidates);

    sealed class ProjectionWorkBudget(int remaining)
    {
        public int Remaining { get; private set; } = remaining;

        public void Charge(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            Charge(text.Length);
        }

        public void Charge(int work)
        {
            if (work < 0 || work > Remaining)
            {
                Remaining = 0;
                throw new BudgetExceededException();
            }
            Remaining -= work;
        }

        public void SetRemaining(int value)
            => Remaining = Math.Max(0, value);
    }

    sealed class MethodAnchorProjector
    {
        readonly MetadataReader _reader;
        readonly TypeDefinitionHandle _declaringType;
        readonly GenericContext _typeContext;
        readonly byte _typeNullableContext;
        readonly HashSet<MethodDefinitionHandle>
            _explicitImplementationBodies;
        readonly ProjectionWorkBudget _budget;

        public MethodAnchorProjector(
            MetadataReader reader,
            TypeDefinitionHandle declaringType,
            ProjectionWorkBudget budget)
        {
            _reader = reader;
            _declaringType = declaringType;
            _budget = budget;
            TypeDefinition type =
                reader.GetTypeDefinition(declaringType);
            _typeContext = GenericContext.ForType(
                reader,
                type,
                budget.Charge);
            _typeNullableContext =
                NullabilityReader.GetTypeNullableContext(
                    reader,
                    declaringType,
                    budget.Charge);
            _explicitImplementationBodies =
                ApiSurfaceExtractor.GetExplicitImplementationBodies(
                    reader,
                    declaringType,
                    type,
                    beforeDecodeWork: budget.Charge)
                .Keys
                .ToHashSet();
        }

        public MemberAnchor Create(
            MethodDefinitionHandle methodHandle,
            string methodName)
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(methodHandle);
            var signature =
                ApiSurfaceExtractor.GetMethodSignatureForIdentity(
                    _reader,
                    _typeContext,
                    methodHandle,
                    method,
                    _typeNullableContext,
                    _budget.Charge,
                    _budget.Charge);
            if (signature.IsDegraded)
            {
                throw new BadImageFormatException(
                    "A relevant method signature could not be completely decoded.");
            }
            bool isFinalizer =
                string.Equals(
                    methodName,
                    "Finalize",
                    StringComparison.Ordinal)
                && ApiSurfaceExtractor.IsFinalizerMethod(
                    _reader,
                    methodHandle,
                    _budget.Charge);
            string kind = ApiSurfaceExtractor.ClassifyMethodKind(
                methodName,
                isFinalizer,
                _explicitImplementationBodies.Contains(methodHandle));
            var member = new ApiMember
            {
                Name = methodName,
                Kind = kind,
                Signature = signature.Text,
                SignatureModel = signature.Model,
                SignatureDecodeStatus = signature.IsDegraded
                    ? SignatureDecodeStatus.Degraded
                    : null,
            };

            int remaining = _budget.Remaining;
            try
            {
                return ApiMemberIdentity.CreateProjectedMethodAnchor(
                    _reader,
                    _declaringType,
                    member,
                    ref remaining);
            }
            catch (BadImageFormatException) when (remaining <= 0)
            {
                throw new BudgetExceededException();
            }
            finally
            {
                _budget.SetRemaining(remaining);
            }
        }
    }

    sealed class BudgetExceededException : Exception;
    sealed class IncompleteCandidateSetException : Exception;

    sealed record OperationFailure(
        ApiDeclarationCorrespondenceStatus Status,
        ApiDeclarationCorrespondenceReason Reason,
        ApiDeclarationCorrespondenceStage Stage,
        string Detail);

    sealed record SnapshotOutcome(
        AssemblyImageSnapshot? Snapshot,
        OperationFailure? Failure);

    sealed record TypeLookup(
        TypeDefinitionHandle Handle,
        OperationFailure? Failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> Candidates);

    sealed record MemberBinding(
        ApiDeclarationReference? Declaration,
        OperationFailure? Failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> Candidates);

    sealed record MemberMatch(
        ApiDeclarationReference? Target,
        OperationFailure? Failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> Candidates);

    sealed record SourceProjection(
        string? TypeKey,
        string? MemberKey,
        OperationFailure? Failure,
        ImmutableArray<ApiDeclarationCandidateEvidence> Candidates);
}
