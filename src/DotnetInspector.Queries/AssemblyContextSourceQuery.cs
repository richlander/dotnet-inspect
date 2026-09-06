using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
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
        SourceFetcher sourceFetcher)
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
        SourceFetcher =
            sourceFetcher
            ?? throw new ArgumentNullException(nameof(sourceFetcher));
    }

    public HttpClient SymbolClient { get; }
    public IPdbStore PdbStore { get; }
    public IPackageSourceAuthorization PackageSourceAuthorization
    {
        get;
    }
    public SourceFetcher SourceFetcher { get; }
    public ISourceLinkIndexCache? SourceLinkCache { get; init; }
    public IReadOnlyList<string>? RepositoryPaths { get; init; }
    public NuGetSourceOptions? NuGetSourceOptions { get; init; }
    public bool CacheOnly { get; init; }
    public SymbolAcquisitionLimits? SymbolAcquisitionLimits { get; init; }

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
}

/// <summary>
/// Exact type request for a PDB-mapped-or-decompiled source query.
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
        : AssemblyMemberSource(Text);

    public sealed record Decompiled(
        string Text,
        MemberRenderResult Decompilation,
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
        DecompilerResult Decompilation,
        PdbTypeSourceInspection PdbAttempt)
        : AssemblyTypeSource(Text);
}

public abstract record AssemblyMemberSourceEntry(
    AssemblyContextSubject Subject,
    AssemblyMemberSourceRequest Request)
{
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
        MemberRenderResult? DecompiledAttempt = null)
        : AssemblyMemberSourceEntry(Subject, Request);
}

public abstract record AssemblyMemberPdbSourceAttempt
{
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
        MemberRenderResult Result)
        : AssemblyMemberDecompiledSourceAttempt;

    public sealed record Unavailable(
        MemberBodyProductionStatus Status,
        string? FailureDetail)
        : AssemblyMemberDecompiledSourceAttempt;
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
        DecompilerResult? DecompiledAttempt = null)
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
public static class AssemblyContextSourceQuery
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
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    cancellationToken)
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
                    provenance));
        }

        MemberRenderResult decompiled =
            DecompileMember(
                participant,
                request,
                target,
                retained,
                bindingPolicyVersion,
                cancellationToken);
        if (decompiled.IsComplete
            && decompiled.Text is { } decompiledText)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Decompiled(
                    decompiledText,
                    decompiled,
                    pdb.Inspection));
        }

        return new AssemblyMemberSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            pdb.Inspection,
            decompiled);
    }

    internal static async Task<AssemblyMemberSourceComparisonEntry>
        InspectMemberComparisonAsync(
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
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        AssemblyMemberPdbSourceAttempt pdbAttempt =
            pdb.Inspection.IsComplete
                && pdb.Inspection.Text is not null
                && pdb.Provenance is { } provenance
                    ? new AssemblyMemberPdbSourceAttempt.Available(
                        pdb.Inspection,
                        provenance)
                    : new AssemblyMemberPdbSourceAttempt.Unavailable(
                        pdb.Inspection);

        MemberRenderResult decompiled =
            DecompileMember(
                participant,
                request,
                target,
                retained,
                bindingPolicyVersion,
                cancellationToken);
        AssemblyMemberDecompiledSourceAttempt decompiledAttempt =
            decompiled.IsComplete
                && decompiled.Text is not null
                    ? new AssemblyMemberDecompiledSourceAttempt.Available(
                        decompiled)
                    : new AssemblyMemberDecompiledSourceAttempt.Unavailable(
                        decompiled.Status,
                        decompiled.Text);

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

    internal static async Task<MemberPdbInspection> InspectMemberPdbAsync(
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            "member",
            request.Member.Format(MemberAnchorFormat.Qualified));
        var sourceResult =
            await OpenSourceLinkAsync(
                    retained,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        PdbMemberSourceInspection inspection;
        AssemblyPdbSourceProvenance? provenance = null;
        if (sourceResult.Source is { } source)
        {
            Exception? disposalFailure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                inspection =
                    await PdbSourceAcquisition.AcquireMemberAsync(
                            source,
                            request.MetadataToken,
                            request.Member.MemberName,
                            findingSubject,
                            context.SourceFetcher,
                            context.RepositoryPaths,
                            cancellationToken,
                            allowLocalSource:
                                context.AllowLocalSourceReads)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                if (inspection.IsComplete)
                    provenance = PdbProvenance(source);
            }
            finally
            {
                disposalFailure = source.DisposeWithFailure();
            }
            ValidateAfterSourceDisposal(
                participant,
                bindingPolicyVersion,
                cancellationToken,
                disposalFailure);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            inspection =
                PdbSourceAcquisition
                    .MemberPdbAcquisitionFailed(
                        findingSubject,
                        sourceResult.Failure!);
        }

        return new MemberPdbInspection(
            inspection,
            provenance);
    }

    static MemberRenderResult DecompileMember(
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        (ApiType Type, ApiMember Member) target,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        var bindingPolicy =
            new CancellationObservingBindingPolicy(
                participant.BindingPolicy);
        MemberRenderResult decompiled =
            MemberBodyProducer.ProduceMember(
                target.Type,
                target.Member,
                retained.WithoutLocalPath(),
                bindingPolicy,
                printerOptions: request.PrinterOptions);
        bindingPolicy.ThrowIfObserved();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        return decompiled;
    }

    internal static async Task<AssemblyTypeSourceEntry> InspectTypeAsync(
        AssemblyContextSubject subject,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ApiType target,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            "type",
            request.Type.ToMetadataFullName());
        var sourceResult =
            await OpenSourceLinkAsync(
                    retained,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        PdbTypeSourceInspection pdbSource;
        if (sourceResult.Source is { } source)
        {
            AssemblyTypeSourceEntry.Available? pdbEntry = null;
            Exception? disposalFailure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                pdbSource =
                    await PdbSourceAcquisition.AcquireTypeAsync(
                            source,
                            request.Type,
                            findingSubject,
                            context.SourceFetcher,
                            context.RepositoryPaths,
                            cancellationToken,
                            allowLocalSource:
                                context.AllowLocalSourceReads)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                if (pdbSource.IsComplete
                    && pdbSource.Text is { } pdbText)
                {
                    pdbEntry =
                        new AssemblyTypeSourceEntry.Available(
                            subject,
                            request,
                            new AssemblyTypeSource.Pdb(
                                pdbText,
                                pdbSource,
                                PdbProvenance(source)));
                }
            }
            finally
            {
                disposalFailure = source.DisposeWithFailure();
            }
            ValidateAfterSourceDisposal(
                participant,
                bindingPolicyVersion,
                cancellationToken,
                disposalFailure);
            if (pdbEntry is not null)
                return pdbEntry;
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            pdbSource =
                PdbSourceAcquisition
                    .TypePdbAcquisitionFailed(
                        findingSubject,
                        sourceResult.Failure!);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        var bindingPolicy =
            new CancellationObservingBindingPolicy(
                participant.BindingPolicy);
        ResolvedAssemblyReference decompilerAssembly =
            retained.WithoutLocalPath();
        DecompilerResult decompiled =
            MemberBodyProducer.Project(
                target,
                decompilerAssembly,
                bindingPolicy,
                printerOptions: request.PrinterOptions);
        bindingPolicy.ThrowIfObserved();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        if (decompiled.Succeeded
            && decompiled.Output is { } decompiledText)
        {
            return new AssemblyTypeSourceEntry.Available(
                subject,
                request,
                new AssemblyTypeSource.Decompiled(
                    decompiledText,
                    decompiled,
                    pdbSource));
        }

        return new AssemblyTypeSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            pdbSource,
            decompiled);
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
            context.SymbolAcquisitionLimits);

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
            source.Context.LoadPdbFromStream(
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

    static AssemblyPdbSourceProvenance PdbProvenance(
        SourceLinkService source)
        => new(source.RepositoryUrl, source.CommitHash);

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

    static void ValidateAfterSourceDisposal(
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
                    AssemblyBindingSelection.Found(
                        Observe(selected.Assembly),
                        [.. selected.ShadowedAssemblies.Select(Observe)]),
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    AssemblyBindingSelection.Multiple(
                        [.. ambiguous.Assemblies.Select(Observe)]),
                _ => selection,
            };

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
        AssemblyPdbSourceProvenance? Provenance);

    sealed record TypeInspectionSeed(
        ResolvedAssemblyReference Retained,
        ApiType? Target);

    internal sealed record SourceLinkOpenResult(
        SourceLinkService? Source,
        Exception? Failure);
}
