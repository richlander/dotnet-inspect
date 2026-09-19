using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries;

/// <summary>
/// Explicit host capabilities for PDB-mapped source and pathless portable-PDB
/// acquisition.
/// </summary>
public sealed class AssemblyContextSourceQueryContext
{
    public AssemblyContextSourceQueryContext(
        HttpClient symbolClient,
        IPdbStore pdbStore,
        IPackageSourceAuthorization packageSourceAuthorization,
        SourceFetch sourceFetcher)
    {
        SymbolClient =
            symbolClient
            ?? throw new ArgumentNullException(nameof(symbolClient));
        PdbStore =
            pdbStore
            ?? throw new ArgumentNullException(nameof(pdbStore));
        PackageSourceAuthorization =
            packageSourceAuthorization
            ?? throw new ArgumentNullException(
                nameof(packageSourceAuthorization));
        SourceFetch =
            sourceFetcher
            ?? throw new ArgumentNullException(nameof(sourceFetcher));
    }

    public HttpClient SymbolClient { get; }
    public IPdbStore PdbStore { get; }
    public IPackageSourceAuthorization PackageSourceAuthorization
    {
        get;
    }
    public SourceFetch SourceFetch { get; }
    public ISourceLinkIndexCache? SourceLinkCache { get; init; }
    public IReadOnlyList<string>? RepositoryPaths { get; init; }
    public NuGetSourceOptions? NuGetSourceOptions { get; init; }
    /// <summary>Optional PDB acquisition fallback; authoritative package/Platform provenance takes precedence.</summary>
    public PackageCoordinate? PdbFallbackPackage { get; init; }
    public bool CacheOnly { get; init; }
    public SymbolAcquisitionLimits? SymbolAcquisitionLimits { get; init; }
    public int MaxDecompilerBodyProjections { get; init; } =
        CSharpDecompilerService.DefaultMaxBodyProjections;

    /// <summary>Authored settlement bounds for member Source and same-member comparison.</summary>
    public SourceHouseLimits MemberSourceLimits { get; init; } = DefaultSourceLimits();

    /// <summary>Authored settlement time after upstream PDB acquisition, excluding decompilation.</summary>
    public TimeSpan MemberSourceTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Authored settlement bounds for type Source.</summary>
    public SourceHouseLimits TypeSourceLimits { get; init; } = DefaultSourceLimits();

    /// <summary>Type authored settlement time after upstream PDB acquisition.</summary>
    public TimeSpan TypeSourceTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Authored settlement bounds for the selected-member pair query only.</summary>
    public SourceHouseLimits MemberSourcePairLimits { get; init; } = DefaultSourceLimits();

    /// <summary>Per-endpoint settlement time after upstream PDB acquisition.</summary>
    public TimeSpan MemberSourcePairTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Allows checksum-authenticated reads from absolute paths recorded in the
    /// portable PDB. Disabled by default so content-only hosts never touch the
    /// filesystem implicitly.
    /// </summary>
    public bool AllowLocalSourceReads { get; init; }

    /// <summary>
    /// Allows loading a matching portable PDB beside the retained assembly's
    /// optional path. Disabled by default for content-only hosts.
    /// </summary>
    public bool AllowAdjacentPdbReads { get; init; }
    public Action<string>? Log { get; init; }

    static SourceHouseLimits DefaultSourceLimits() => new(
        maximumAssemblyBytes: (int)AssemblyImageSnapshot.DefaultMaxRetainedImageBytes,
        maximumPortablePdbBytes: (int)AssemblyImageSnapshot.DefaultMaxRetainedImageBytes,
        targetBounds: new(65_536, 1_000_000, 100_000, 100_000, 8_000_000, 256_000_000),
        sourceLinkReadLimits: new(512 * 1024 * 1024, 16_000_000, 100_000),
        maximumDocuments: 1_000_000,
        maximumTargetMappings: 1_000_000,
        maximumCandidateAttempts: 3,
        maximumSourceBytes: 64 * 1024 * 1024,
        maximumSourceTextCharacters: 64 * 1024 * 1024);
}

/// <summary>
/// Exact type request for a PDB-mapped-or-decompiled source query, or one
/// explicitly selected authored document without decompiler substitution.
/// Printer options affect only decompiled fallback; PDB source remains
/// unchanged.
/// </summary>
public sealed record AssemblyTypeSourceRequest
{
    public AssemblyTypeSourceRequest(
        MetadataTypeDefinitionName type,
        PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        PrinterOptions = printerOptions;
    }

    public MetadataTypeDefinitionName Type { get; }
    public PrinterOptions? PrinterOptions { get; }
    public string? OriginalDocumentPath { get; private init; }

    public static AssemblyTypeSourceRequest AuthoredDocument(
        MetadataTypeDefinitionName type,
        string originalDocumentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(originalDocumentPath);
        return new(type) { OriginalDocumentPath = originalDocumentPath };
    }

    public static AssemblyTypeSourceRequest From(
        ApiType type,
        PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new AssemblyTypeSourceRequest(
            GetDefinitionName(type),
            printerOptions);
    }

    internal static MetadataTypeDefinitionName GetDefinitionName(
        ApiType type)
    {
        if (type.DefinitionName is { } definitionName)
            return definitionName;

        if (type.MetadataName is not { Length: > 0 } metadataName)
        {
            throw new ArgumentException(
                "The API type does not carry an exact metadata lookup name.",
                nameof(type));
        }
        if (metadataName.Contains('+', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The API type does not carry an unambiguous exact metadata lookup name.",
                nameof(type));
        }

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(
                type.Namespace ?? "",
                [metadataName]);
        return result is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new ArgumentException(
                "The API type carries an invalid metadata lookup name.",
                nameof(type));
    }
}

/// <summary>
/// Exact method request: physical MethodDef token plus the API member anchor
/// that the token is expected to denote. Printer options affect only
/// decompiled fallback; PDB source remains unchanged.
/// </summary>
public sealed record AssemblyMemberSourceRequest
{
    public AssemblyMemberSourceRequest(
        MetadataTypeDefinitionName type,
        MemberAnchor member,
        int metadataToken,
        PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        if (MetadataTokens.EntityHandle(metadataToken).Kind
            != HandleKind.MethodDefinition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadataToken),
                "Member source requests require a MethodDef token.");
        }

        Type = type;
        Member = member;
        MetadataToken = metadataToken;
        PrinterOptions = printerOptions;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MemberAnchor Member { get; }
    public int MetadataToken { get; }
    public PrinterOptions? PrinterOptions { get; }
    public bool IncludeAuthoredParts { get; private init; }
    public bool AllowDecompiledFallback { get; private init; } = true;

    public AssemblyMemberSourceRequest WithAuthoredParts(bool allowDecompiledFallback = false) =>
        this with
        {
            IncludeAuthoredParts = true,
            AllowDecompiledFallback = allowDecompiledFallback,
        };

    public static AssemblyMemberSourceRequest From(
        ApiType type,
        ApiMember member,
        PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        if (member.MetadataToken is not { } metadataToken)
        {
            throw new ArgumentException(
                "The API member does not carry a MethodDef token.",
                nameof(member));
        }

        MetadataTypeDefinitionName requestType =
            AssemblyTypeSourceRequest.GetDefinitionName(type);
        MemberAnchor requestMember =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        if (member.Kind == "extension-method")
        {
            if (member.DeclaringTypeDefinitionName is not { } declaringType
                || string.IsNullOrWhiteSpace(
                    member.DeclaringTypeCanonicalName))
            {
                throw new ArgumentException(
                    "A projected extension method must retain its exact declaring type identity.",
                    nameof(member));
            }

            requestType = declaringType;
            requestMember = requestMember with
            {
                StableSelector =
                    $"{ApiMemberIdentity.GetMemberSelectorName(member.Name)}"
                    + $"~{requestMember.Fingerprint}",
                TypeFullName = member.DeclaringTypeCanonicalName,
            };
        }

        return new AssemblyMemberSourceRequest(
            requestType,
            requestMember,
            metadataToken,
            printerOptions);
    }
}

public sealed record AssemblyPdbSourceProvenance(
    string? RepositoryUrl,
    string? Revision);

public enum AssemblySourceFailureKind
{
    TargetNotFound,
    PdbAndDecompiledUnavailable,
    InspectionFailed,
    AuthoredDocumentUnavailable,
    AuthoredMemberPartsUnavailable,
}

public sealed record AssemblySourceFailure(
    AssemblySourceFailureKind Kind,
    string Detail,
    Exception? Error = null);

public abstract record AssemblyMemberSource(string Text)
{
    public sealed record Pdb(
        string Text,
        PdbMemberSourceInspection Inspection,
        AssemblyPdbSourceProvenance Provenance)
        : AssemblyMemberSource(Text)
    {
        public SourceHouseAuthoredMemberDocument? MemberDocument { get; init; }
    }

    public sealed record Decompiled(
        string Text,
        CSharpDecompilationAttempt Decompilation,
        PdbMemberSourceInspection PdbAttempt)
        : AssemblyMemberSource(Text);
}

public abstract record AssemblyTypeSource(string Text)
{
    public sealed record Pdb(
        string Text,
        PdbTypeSourceInspection Inspection,
        AssemblyPdbSourceProvenance Provenance)
        : AssemblyTypeSource(Text);

    public sealed record Decompiled(
        string Text,
        CSharpDecompilationAttempt Decompilation,
        PdbTypeSourceInspection PdbAttempt)
        : AssemblyTypeSource(Text);
}

public abstract record AssemblyMemberSourceEntry(
    AssemblyContextSubject Subject,
    AssemblyMemberSourceRequest Request)
{
    public SourceHouseOutcome? HouseOutcome { get; init; }
    public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }

    public sealed record Available(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblyMemberSource Source)
        : AssemblyMemberSourceEntry(Subject, Request);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        CandidateOpenFailure Failure)
        : AssemblyMemberSourceEntry(Subject, Request);

    public sealed record Unavailable(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblySourceFailure Failure,
        PdbMemberSourceInspection? PdbAttempt = null,
        CSharpDecompilationAttempt? DecompiledAttempt = null)
        : AssemblyMemberSourceEntry(Subject, Request);
}

public abstract record AssemblyMemberPdbSourceAttempt
{
    public SourceHouseOutcome? HouseOutcome { get; init; }
    public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }

    public sealed record Available(
        PdbMemberSourceInspection Inspection,
        AssemblyPdbSourceProvenance Provenance)
        : AssemblyMemberPdbSourceAttempt;

    public sealed record Unavailable(
        PdbMemberSourceInspection Inspection)
        : AssemblyMemberPdbSourceAttempt;
}

public abstract record AssemblyMemberDecompiledSourceAttempt
{
    public sealed record Available(
        CSharpDecompilationAttempt Result)
        : AssemblyMemberDecompiledSourceAttempt;

    public sealed record Unavailable(
        CSharpDecompilationAttempt Result)
        : AssemblyMemberDecompiledSourceAttempt
    {
        public CSharpDecompilationStatus Status => Result.Status;
        public string FailureDetail => Result.DiagnosticSummary;
    }
}

public abstract record AssemblyMemberSourceComparisonEntry(
    AssemblyContextSubject Subject,
    AssemblyMemberSourceRequest Request)
{
    public sealed record Available(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblyMemberPdbSourceAttempt Pdb,
        AssemblyMemberDecompiledSourceAttempt Decompiled)
        : AssemblyMemberSourceComparisonEntry(Subject, Request);

    public sealed record Unavailable(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblyMemberPdbSourceAttempt.Unavailable Pdb,
        AssemblyMemberDecompiledSourceAttempt.Unavailable Decompiled)
        : AssemblyMemberSourceComparisonEntry(Subject, Request);

    public sealed record NotFound(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblySourceFailure Failure)
        : AssemblyMemberSourceComparisonEntry(Subject, Request);

    public sealed record Failed(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        AssemblySourceFailure Failure)
        : AssemblyMemberSourceComparisonEntry(Subject, Request);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        AssemblyMemberSourceRequest Request,
        CandidateOpenFailure Failure)
        : AssemblyMemberSourceComparisonEntry(Subject, Request);
}

public abstract record AssemblyTypeSourceEntry(
    AssemblyContextSubject Subject,
    AssemblyTypeSourceRequest Request)
{
    public SourceHouseOutcome? HouseOutcome { get; init; }
    public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }

    public sealed record Available(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        AssemblyTypeSource Source)
        : AssemblyTypeSourceEntry(Subject, Request);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        CandidateOpenFailure Failure)
        : AssemblyTypeSourceEntry(Subject, Request);

    public sealed record Unavailable(
        AssemblyContextSubject Subject,
        AssemblyTypeSourceRequest Request,
        AssemblySourceFailure Failure,
        PdbTypeSourceInspection? PdbAttempt = null,
        CSharpDecompilationAttempt? DecompiledAttempt = null)
        : AssemblyTypeSourceEntry(Subject, Request);
}

/// <summary>
/// Independently attempts checksum-verified PDB source and product-owned
/// decompilation for one exact member resolution.
/// </summary>
public static class AssemblyContextSourceComparisonQuery
{
    public static InspectionQuery<AssemblyMemberSourceComparisonEntry>
        Definition
    { get; } =
        new(
            "Assembly context member source comparison",
            InspectionCost.Moderated);

    public static Task<AssemblyMemberSourceComparisonEntry> ExecuteAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
        => AssemblyContextSourceQuery.ExecuteComparisonAsync(
            group,
            participant,
            request,
            context,
            cancellationToken);
}

/// <summary>
/// Returns checksum-verified PDB-mapped source when available, otherwise
/// product-owned decompiled C#, for one participant in a binding-consistent
/// assembly context group.
/// </summary>
public static partial class AssemblyContextSourceQuery
{
    public static InspectionQuery<AssemblyMemberSourceEntry>
        MemberDefinition
    { get; } =
        new(
            "Assembly context member source",
            InspectionCost.Moderated);

    public static InspectionQuery<AssemblyTypeSourceEntry>
        TypeDefinition
    { get; } =
        new(
            "Assembly context type source",
            InspectionCost.Moderated);

    public static async Task<AssemblyMemberSourceEntry>
        ExecuteMemberAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<MemberInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant,
                cancellationToken,
                (session, retained) => new MemberInspectionSeed(
                    retained,
                    ResolveMember(session, request)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberSourceEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }

        if (access
            is AssemblyImageAccessResult<
                MemberInspectionSeed>.Rejected rejected)
        {
            return new AssemblyMemberSourceEntry.Rejected(
                subject,
                request,
                rejected.Failure);
        }
        if (access
            is not AssemblyImageAccessResult<
                MemberInspectionSeed>.Available available)
        {
            throw new InvalidOperationException(
                "Unknown assembly image access result.");
        }
        if (available.Value.Target is not { } target)
        {
            return new AssemblyMemberSourceEntry.Unavailable(
                subject,
                request,
                TargetNotFound(
                    "The selected participant does not declare the requested method."));
        }

        try
        {
            return await InspectMemberAsync(
                    group,
                    subject,
                    participant,
                    request,
                    context,
                    target,
                    available.Value.Retained,
                    bindingPolicyVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberSourceEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }
    }

    internal static async Task<AssemblyMemberSourceComparisonEntry>
        ExecuteComparisonAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<MemberInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant,
                cancellationToken,
                (session, retained) => new MemberInspectionSeed(
                    retained,
                    ResolveMember(session, request)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberSourceComparisonEntry.Failed(
                subject,
                request,
                InspectionFailure(ex));
        }

        if (access
            is AssemblyImageAccessResult<
                MemberInspectionSeed>.Rejected rejected)
        {
            return new AssemblyMemberSourceComparisonEntry.Rejected(
                subject,
                request,
                rejected.Failure);
        }
        if (access
            is not AssemblyImageAccessResult<
                MemberInspectionSeed>.Available available)
        {
            throw new InvalidOperationException(
                "Unknown assembly image access result.");
        }
        if (available.Value.Target is not { } target)
        {
            return new AssemblyMemberSourceComparisonEntry.NotFound(
                subject,
                request,
                TargetNotFound(
                    "The selected participant does not declare the requested method."));
        }

        try
        {
            return await InspectMemberComparisonAsync(
                    group,
                    subject,
                    participant,
                    request,
                    context,
                    target,
                    available.Value.Retained,
                    bindingPolicyVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyMemberSourceComparisonEntry.Failed(
                subject,
                request,
                InspectionFailure(ex));
        }
    }

    public static async Task<AssemblyTypeSourceEntry> ExecuteTypeAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<TypeInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant,
                cancellationToken,
                (session, retained) => new TypeInspectionSeed(
                    retained,
                    ResolveType(session, request.Type)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyTypeSourceEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }

        if (access
            is AssemblyImageAccessResult<
                TypeInspectionSeed>.Rejected rejected)
        {
            return new AssemblyTypeSourceEntry.Rejected(
                subject,
                request,
                rejected.Failure);
        }
        if (access
            is not AssemblyImageAccessResult<
                TypeInspectionSeed>.Available available)
        {
            throw new InvalidOperationException(
                "Unknown assembly image access result.");
        }
        if (available.Value.Target is not { } target)
        {
            return new AssemblyTypeSourceEntry.Unavailable(
                subject,
                request,
                TargetNotFound(
                    "The selected participant does not declare the requested type."));
        }

        try
        {
            return await InspectTypeAsync(
                    group,
                    subject,
                    participant,
                    request,
                    context,
                    target,
                    available.Value.Retained,
                    bindingPolicyVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInspectionFailure(ex))
        {
            return new AssemblyTypeSourceEntry.Unavailable(
                subject,
                request,
                InspectionFailure(ex));
        }
    }

    internal static async Task<AssemblyMemberSourceEntry> InspectMemberAsync(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        (ApiType Type, ApiMember Member) target,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        MemberPdbInspection pdb =
            await InspectMemberPdbAsync(
                    group,
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    context.MemberSourceLimits,
                    context.MemberSourceTimeout,
                    cancellationToken,
                    retainSymbols: request.AllowDecompiledFallback)
                .ConfigureAwait(false);
        if (pdb.Inspection.IsComplete
            && pdb.Inspection.Text is { } pdbText
            && pdb.Provenance is { } provenance)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Pdb(
                    pdbText,
                    pdb.Inspection,
                    provenance)
                {
                    MemberDocument = (pdb.HouseOutcome as SourceHouseOutcome.Available)
                        ?.Source.MemberDocument,
                })
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        if (!request.AllowDecompiledFallback)
        {
            return new AssemblyMemberSourceEntry.Unavailable(
                subject,
                request,
                new(AssemblySourceFailureKind.AuthoredMemberPartsUnavailable,
                    "The requested verified authored member parts are unavailable."),
                pdb.Inspection)
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        CSharpDecompilationAttempt decompiled =
            DecompileMember(
                participant,
                request,
                target,
                retained,
                bindingPolicyVersion,
                pdb.PdbImage,
                context.MaxDecompilerBodyProjections,
                cancellationToken);
        if (decompiled.IsAvailable
            && decompiled.Text is { } decompiledText)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Decompiled(
                    decompiledText,
                    decompiled,
                    pdb.Inspection))
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        return new AssemblyMemberSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            pdb.Inspection,
            decompiled)
        {
            HouseOutcome = pdb.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
        };
    }

    internal static async Task<AssemblyMemberSourceComparisonEntry>
        InspectMemberComparisonAsync(
            AssemblyContextGroup group,
            AssemblyContextSubject subject,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            (ApiType Type, ApiMember Member) target,
            ResolvedAssemblyReference retained,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            CancellationToken cancellationToken)
    {
        MemberPdbInspection pdb =
            await InspectMemberPdbAsync(
                    group,
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    context.MemberSourceLimits,
                    context.MemberSourceTimeout,
                    cancellationToken,
                    retainSymbols: true)
                .ConfigureAwait(false);
        AssemblyMemberPdbSourceAttempt pdbAttempt = pdb.ToAttempt();

        CSharpDecompilationAttempt decompiled =
            DecompileMember(
                participant,
                request,
                target,
                retained,
                bindingPolicyVersion,
                pdb.PdbImage,
                context.MaxDecompilerBodyProjections,
                cancellationToken);
        AssemblyMemberDecompiledSourceAttempt decompiledAttempt =
            decompiled.IsAvailable
                && decompiled.Text is not null
                    ? new AssemblyMemberDecompiledSourceAttempt.Available(
                        decompiled)
                    : new AssemblyMemberDecompiledSourceAttempt.Unavailable(
                        decompiled);

        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);

        if (pdbAttempt is AssemblyMemberPdbSourceAttempt.Available
            || decompiledAttempt
                is AssemblyMemberDecompiledSourceAttempt.Available)
        {
            return new AssemblyMemberSourceComparisonEntry.Available(
                subject,
                request,
                pdbAttempt,
                decompiledAttempt);
        }

        return new AssemblyMemberSourceComparisonEntry.Unavailable(
            subject,
            request,
            (AssemblyMemberPdbSourceAttempt.Unavailable)pdbAttempt,
            (AssemblyMemberDecompiledSourceAttempt.Unavailable)
                decompiledAttempt);
    }

    static CSharpDecompilationAttempt DecompileMember(
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        (ApiType Type, ApiMember Member) target,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        ImmutableArray<byte>? pdbImage,
        int maxBodyProjections,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        var bindingPolicy =
            new CancellationObservingBindingPolicy(
                participant.BindingPolicy);
        CSharpDecompilationAttempt decompiled =
            CSharpDecompilerService.ProduceMember(
                target.Type,
                target.Member,
                retained.WithoutLocalPath(),
                bindingPolicy,
                pdbImage: pdbImage,
                printerOptions: request.PrinterOptions,
                maxBodyProjections: maxBodyProjections,
                cancellationToken: cancellationToken);
        bindingPolicy.ThrowIfObserved();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        return decompiled;
    }

    internal static async Task<AssemblyTypeSourceEntry> InspectTypeAsync(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ApiType target,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        TypePdbInspection pdb =
            await InspectTypePdbAsync(
                    group,
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    context.TypeSourceLimits,
                    context.TypeSourceTimeout,
                    cancellationToken,
                    retainSymbols: request.OriginalDocumentPath is null)
                .ConfigureAwait(false);
        if (pdb.Inspection.IsComplete
            && pdb.Inspection.Text is { } pdbText
            && pdb.Provenance is { } provenance)
        {
            return new AssemblyTypeSourceEntry.Available(
                subject,
                request,
                new AssemblyTypeSource.Pdb(
                    pdbText,
                    pdb.Inspection,
                    provenance))
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        if (request.OriginalDocumentPath is not null)
        {
            return new AssemblyTypeSourceEntry.Unavailable(
                subject,
                request,
                new(
                    AssemblySourceFailureKind.AuthoredDocumentUnavailable,
                    "The selected authored source document is unavailable."),
                pdb.Inspection)
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }
        var bindingPolicy =
            new CancellationObservingBindingPolicy(
                participant.BindingPolicy);
        ResolvedAssemblyReference decompilerAssembly =
            retained.WithoutLocalPath();
        CSharpDecompilationAttempt decompiled =
            CSharpDecompilerService.ProduceType(
                target,
                decompilerAssembly,
                bindingPolicy,
                pdbImage: pdb.PdbImage,
                printerOptions: request.PrinterOptions,
                maxBodyProjections: context.MaxDecompilerBodyProjections,
                cancellationToken: cancellationToken);
        bindingPolicy.ThrowIfObserved();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        if (decompiled.IsAvailable
            && decompiled.Text is { } decompiledText)
        {
            return new AssemblyTypeSourceEntry.Available(
                subject,
                request,
                new AssemblyTypeSource.Decompiled(
                    decompiledText,
                    decompiled,
                    pdb.Inspection))
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        return new AssemblyTypeSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            pdb.Inspection,
            decompiled)
        {
            HouseOutcome = pdb.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
        };
    }

    static Task AcquirePdbAsync(
        SourceLinkService source,
        ResolvedAssemblyReference retained,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken)
        => PdbAcquisitionService.AcquireAsync(
            source.Context,
            retained,
            context.SymbolClient,
            context.PdbStore,
            context.PackageSourceAuthorization,
            context.Log,
            context.CacheOnly,
            context.NuGetSourceOptions,
            cancellationToken,
            context.SymbolAcquisitionLimits,
            context.PdbFallbackPackage?.PackageId,
            context.PdbFallbackPackage?.Version);

    internal static async Task<SourceLinkOpenResult> OpenSourceLinkAsync(
        ResolvedAssemblyReference retained,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int maxEmbeddedPdbBytes =
            context.SymbolAcquisitionLimits is { } acquisitionLimits
                ? (int)Math.Min(
                    Math.Min(
                        acquisitionLimits.MaxPortablePdbBytes,
                        acquisitionLimits.MaxExpandedPdbBytes),
                    int.MaxValue)
                : int.MaxValue;
        var readLimits = new SourceLinkReadLimits(
            maxEmbeddedPdbBytes,
            maxMapBytes: int.MaxValue,
            maxMappings: int.MaxValue);
        SourceLinkService source;
        try
        {
            source =
                SourceLinkService.OpenEmbeddedPdbOnly(
                    retained,
                    readLimits,
                    context.Log,
                    context.SourceLinkCache);
        }
        catch (Exception ex) when (IsPdbAcquisitionFailure(ex))
        {
            return new SourceLinkOpenResult(
                Source: null,
                ex);
        }

        bool ownershipEnded = false;
        try
        {
            try
            {
                LoadAdjacentPdb(source, retained, context, cancellationToken);
                await AcquirePdbAsync(
                        source,
                        retained,
                        context,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPdbAcquisitionFailure(ex))
            {
                Exception? disposalFailure =
                    source.DisposeWithFailure();
                ownershipEnded = true;
                cancellationToken.ThrowIfCancellationRequested();
                ThrowSourceDisposalFailure(disposalFailure);
                return new SourceLinkOpenResult(
                    Source: null,
                    ex);
            }
            ownershipEnded = true;
            return new SourceLinkOpenResult(
                source,
                Failure: null);
        }
        finally
        {
            if (!ownershipEnded)
                source.Dispose();
        }
    }

    static void LoadAdjacentPdb(
        SourceLinkService source,
        ResolvedAssemblyReference retained,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken)
    {
        if (!context.AllowAdjacentPdbReads
            || source.Context.HasPdb
            || retained.Path is not { } assemblyPath)
            return;

        string path = Path.ChangeExtension(assemblyPath, ".pdb");
        FileStream? owned;
        try
        {
            owned = File.OpenRead(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        try
        {
            if (context.SymbolAcquisitionLimits is { } limits
                && owned.Length > Math.Min(limits.MaxPortablePdbBytes, limits.MaxExpandedPdbBytes))
            {
                throw new InvalidDataException(
                    "The adjacent portable PDB exceeds the source query's acquisition byte limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            FileStream transferred = owned;
            owned = null;
            source.LoadPdbFromStream(
                transferred,
                pdbLocation: "Standalone",
                portablePdbPath: path,
                throwOnReadFailure: true);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    static ApiType? ResolveType(
        AssemblyInspectionSession session,
        MetadataTypeDefinitionName type)
    {
        ApiType? match = null;
        foreach (ApiType candidate
            in session.ApiSurface(includeAll: true).Types)
        {
            if (candidate.DefinitionName != type)
                continue;
            if (match is not null)
                return null;
            match = candidate;
        }

        return match;
    }

    static (ApiType Type, ApiMember Member)? ResolveMember(
        AssemblyInspectionSession session,
        AssemblyMemberSourceRequest request)
        => ResolveMember(
            session,
            request.Type,
            request.Member,
            request.MetadataToken);

    internal static (ApiType Type, ApiMember Member)? ResolveMember(
        AssemblyInspectionSession session,
        MetadataTypeDefinitionName typeName,
        MemberAnchor member,
        int? metadataToken = null)
    {
        ApiType? type = ResolveType(session, typeName);
        if (type is null)
            return null;

        ApiMember? match = null;
        foreach (ApiMember candidate in type.Members)
        {
            if (candidate.MetadataToken is not { } token
                || MetadataTokens.EntityHandle(token).Kind
                    != HandleKind.MethodDefinition
                || (metadataToken is { } expectedToken
                    && token != expectedToken)
                || ApiMemberIdentity.GetMemberAnchor(type, candidate)
                    != member)
            {
                continue;
            }

            if (match is not null)
                return null;
            match = candidate;
        }

        if (match is not null)
            return (type, match);

        foreach (ApiMember accessor in type.Members.SelectMany(
            owner => ApiMemberAccessors.Create(owner, type)))
        {
            if ((metadataToken is { } expectedToken
                    && accessor.MetadataToken != expectedToken)
                || ApiMemberIdentity.GetMemberAnchor(type, accessor)
                    != member)
            {
                continue;
            }

            if (match is not null)
                return null;
            match = accessor;
        }

        if (match is null)
            return null;

        type.Members = [match];
        return (type, match);
    }

    static AssemblySourceFailure TargetNotFound(string detail)
        => new(
            AssemblySourceFailureKind.TargetNotFound,
            detail);

    static AssemblySourceFailure BothUnavailable()
        => new(
            AssemblySourceFailureKind
                .PdbAndDecompiledUnavailable,
            "Neither PDB-mapped nor decompiled source is available for the selected target.");

    internal static AssemblySourceFailure InspectionFailure(Exception error)
        => new(
            AssemblySourceFailureKind.InspectionFailed,
            $"Source inspection failed: {error.Message}",
            error);

    internal static bool IsInspectionFailure(Exception error)
        => error is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException
            or ArgumentException;

    static bool IsPdbAcquisitionFailure(Exception error)
        => error is not (OperationCanceledException
            or OutOfMemoryException
            or StackOverflowException
            or AccessViolationException);

    internal static void EnsureBindingPolicyVersion(
        AssemblyContextParticipant participant,
        AssemblyBindingPolicyVersion expected)
    {
        if (!ReferenceEquals(
                participant.BindingPolicy.Version,
                expected))
        {
            throw new InvalidOperationException(
                "The participant binding-policy snapshot changed during source inspection.");
        }
    }

    internal static void ValidateAfterSourceDisposal(
        AssemblyContextParticipant participant,
        AssemblyBindingPolicyVersion expectedVersion,
        CancellationToken cancellationToken,
        Exception? disposalFailure)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disposalFailure is OperationCanceledException cancellation)
            ExceptionDispatchInfo.Capture(cancellation).Throw();
        EnsureBindingPolicyVersion(
            participant,
            expectedVersion);
        ThrowSourceDisposalFailure(disposalFailure);
    }

    static void ThrowSourceDisposalFailure(
        Exception? disposalFailure)
    {
        if (disposalFailure is null)
            return;
        if (disposalFailure is OperationCanceledException cancellation)
            ExceptionDispatchInfo.Capture(cancellation).Throw();
        if (IsInspectionFailure(disposalFailure))
            ExceptionDispatchInfo.Capture(disposalFailure).Throw();
        if (IsPdbAcquisitionFailure(disposalFailure))
        {
            throw new InvalidOperationException(
                "PDB disposal failed.",
                disposalFailure);
        }
        ExceptionDispatchInfo.Capture(disposalFailure).Throw();
    }

    internal sealed class CancellationObservingBindingPolicy(
        IAssemblyBindingPolicy inner)
        : AssemblyBindingPolicyFacade(inner)
    {
        ExceptionDispatchInfo? _cancellation;
        ExceptionDispatchInfo? _inspectionFailure;
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> _observedAssemblies =
                new(ReferenceEqualityComparer.Instance);

        public override AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            try
            {
                return base.Select(request);
            }
            catch (OperationCanceledException ex)
            {
                ObserveCancellation(ex);
                throw;
            }
            catch (Exception ex) when (IsInspectionFailure(ex))
            {
                ObserveInspectionFailure(ex);
                throw;
            }
        }

        internal void ThrowIfObserved()
        {
            Volatile.Read(ref _cancellation)?.Throw();
            Volatile.Read(ref _inspectionFailure)?.Throw();
        }

        protected override void ObserveForeignSnapshot() =>
            ObserveInspectionFailure(
                new InvalidOperationException(
                    "The participant binding-policy snapshot changed during source inspection."));

        protected override AssemblyBindingSelection TransformSelection(
            AssemblyBindingSelection selection)
            => selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    FinalizeSelected(
                        Observe(selected.Assembly),
                        [.. selected.ShadowedAssemblies.Select(Observe)]),
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    FinalizeAmbiguous(
                        [.. ambiguous.Assemblies.Select(Observe)],
                        [.. ambiguous.ShadowedAssemblies.Select(Observe)]),
                AssemblyBindingSelection.CompositionRequired required =>
                    AssemblyBindingSelection.RequireComposition(
                        AssemblyBindingCandidateDomain.Create(
                        [
                            .. required.Domain.Candidates.Select(
                                Observe),
                        ])),
                _ => selection,
            };

        static AssemblyBindingSelection FinalizeSelected(
            ResolvedAssemblyReference selected,
            ImmutableArray<ResolvedAssemblyReference> shadows) =>
            shadows.IsEmpty
                ? AssemblyBindingSelection.Found(selected)
                : AssemblyBindingCandidateDomain.Create(
                    [selected, .. shadows])
                    .Finalize([selected]);

        static AssemblyBindingSelection FinalizeAmbiguous(
            ImmutableArray<ResolvedAssemblyReference> active,
            ImmutableArray<ResolvedAssemblyReference> shadows) =>
            shadows.IsEmpty
                ? AssemblyBindingSelection.Multiple(active)
                : AssemblyBindingCandidateDomain.Create(
                    [.. active, .. shadows])
                    .Finalize(active);

        ResolvedAssemblyReference Observe(
            ResolvedAssemblyReference assembly)
        {
            lock (_observedAssemblies)
            {
                if (_observedAssemblies.TryGetValue(
                        assembly.Registration,
                        out ResolvedAssemblyReference? observed))
                {
                    return observed;
                }

                observed =
                    assembly.ObserveOpenReadCancellation(
                        ObserveCancellation);
                _observedAssemblies.Add(
                    assembly.Registration,
                    observed);
                return observed;
            }
        }

        void ObserveCancellation(OperationCanceledException error) =>
            Interlocked.CompareExchange(
                ref _cancellation,
                ExceptionDispatchInfo.Capture(error),
                comparand: null);

        void ObserveInspectionFailure(Exception error) =>
            Interlocked.CompareExchange(
                ref _inspectionFailure,
                ExceptionDispatchInfo.Capture(error),
                comparand: null);
    }

    sealed record MemberInspectionSeed(
        ResolvedAssemblyReference Retained,
        (ApiType Type, ApiMember Member)? Target);

    internal sealed record MemberPdbInspection(
        PdbMemberSourceInspection Inspection,
        AssemblyPdbSourceProvenance? Provenance,
        ImmutableArray<byte>? PdbImage)
    {
        public SourceHouseOutcome? HouseOutcome { get; init; }
        public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }

        public AssemblyMemberPdbSourceAttempt ToAttempt()
        {
            AssemblyMemberPdbSourceAttempt attempt =
                Inspection.IsComplete && Inspection.Text is not null && Provenance is not null
                    ? new AssemblyMemberPdbSourceAttempt.Available(Inspection, Provenance)
                    : new AssemblyMemberPdbSourceAttempt.Unavailable(Inspection);
            return attempt with { HouseOutcome = HouseOutcome, LibraryFailure = LibraryFailure };
        }
    }

    internal sealed record TypePdbInspection(
        PdbTypeSourceInspection Inspection,
        AssemblyPdbSourceProvenance? Provenance,
        ImmutableArray<byte>? PdbImage)
    {
        public SourceHouseOutcome? HouseOutcome { get; init; }
        public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }
    }

    sealed record TypeInspectionSeed(
        ResolvedAssemblyReference Retained,
        ApiType? Target);

    internal sealed record SourceLinkOpenResult(
        SourceLinkService? Source,
        Exception? Failure);
}
