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
/// Explicit host capabilities for authored-source and pathless portable-PDB
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

    /// <summary>
    /// Allows checksum-authenticated reads from absolute paths recorded in the
    /// portable PDB. Disabled by default so content-only hosts never touch the
    /// filesystem implicitly.
    /// </summary>
    public bool AllowLocalSourceReads { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Exact type request for an authored-or-decompiled source query. Printer
/// options affect only decompiled fallback; authored source remains unchanged.
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
/// decompiled fallback; authored source remains unchanged.
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

        return new AssemblyMemberSourceRequest(
            AssemblyTypeSourceRequest.GetDefinitionName(type),
            ApiMemberIdentity.GetMemberAnchor(type, member),
            metadataToken,
            printerOptions);
    }
}

public sealed record AssemblyAuthoredSourceProvenance(
    string? RepositoryUrl,
    string? Revision);

public enum AssemblySourceFailureKind
{
    TargetNotFound,
    AuthoredAndDecompiledUnavailable,
    InspectionFailed,
}

public sealed record AssemblySourceFailure(
    AssemblySourceFailureKind Kind,
    string Detail,
    Exception? Error = null);

public abstract record AssemblyMemberSource(string Text)
{
    public sealed record Authored(
        string Text,
        AuthoredMemberSourceInspection Inspection,
        AssemblyAuthoredSourceProvenance Provenance)
        : AssemblyMemberSource(Text);

    public sealed record Decompiled(
        string Text,
        MemberRenderResult Decompilation,
        AuthoredMemberSourceInspection AuthoredAttempt)
        : AssemblyMemberSource(Text);
}

public abstract record AssemblyTypeSource(string Text)
{
    public sealed record Authored(
        string Text,
        AuthoredTypeSourceInspection Inspection,
        AssemblyAuthoredSourceProvenance Provenance)
        : AssemblyTypeSource(Text);

    public sealed record Decompiled(
        string Text,
        DecompilerResult Decompilation,
        AuthoredTypeSourceInspection AuthoredAttempt)
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
        AuthoredMemberSourceInspection? AuthoredAttempt = null,
        MemberRenderResult? DecompiledAttempt = null)
        : AssemblyMemberSourceEntry(Subject, Request);
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
        AuthoredTypeSourceInspection? AuthoredAttempt = null,
        DecompilerResult? DecompiledAttempt = null)
        : AssemblyTypeSourceEntry(Subject, Request);
}

/// <summary>
/// Returns checksum-verified authored source when available, otherwise
/// product-owned decompiled C#, for one participant in a binding-consistent
/// assembly context group.
/// </summary>
public static class AssemblyContextSourceQuery
{
    public static InspectionQuery<AssemblyMemberSourceEntry>
        MemberDefinition { get; } =
        new(
            "Assembly context member source",
            InspectionCost.Moderated);

    public static InspectionQuery<AssemblyTypeSourceEntry>
        TypeDefinition { get; } =
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

    static async Task<AssemblyMemberSourceEntry> InspectMemberAsync(
        AssemblyContextSubject subject,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        (ApiType Type, ApiMember Member) target,
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
        AuthoredMemberSourceInspection authored;
        if (sourceResult.Source is { } source)
        {
            AssemblyMemberSourceEntry.Available? authoredEntry = null;
            Exception? disposalFailure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                authored =
                    await AuthoredSourceAcquisition.AcquireMemberAsync(
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
                if (authored.IsComplete
                    && authored.Text is { } authoredText)
                {
                    authoredEntry =
                        new AssemblyMemberSourceEntry.Available(
                            subject,
                            request,
                            new AssemblyMemberSource.Authored(
                                authoredText,
                                authored,
                                Provenance(source)));
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
            if (authoredEntry is not null)
                return authoredEntry;
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            authored =
                AuthoredSourceAcquisition
                    .MemberPdbAcquisitionFailed(
                        findingSubject,
                        sourceResult.Failure!);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        var bindingPolicy =
            new CancellationObservingBindingPolicy(
                participant.BindingPolicy,
                bindingPolicyVersion);
        ResolvedAssemblyReference decompilerAssembly =
            retained.WithoutLocalPath();
        MemberRenderResult decompiled =
            MemberBodyProducer.ProduceMember(
                target.Type,
                target.Member,
                decompilerAssembly,
                bindingPolicy,
                printerOptions: request.PrinterOptions);
        bindingPolicy.ThrowIfObserved();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);
        if (decompiled.IsComplete
            && decompiled.Text is { } decompiledText)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Decompiled(
                    decompiledText,
                    decompiled,
                    authored));
        }

        return new AssemblyMemberSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            authored,
            decompiled);
    }

    static async Task<AssemblyTypeSourceEntry> InspectTypeAsync(
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
        AuthoredTypeSourceInspection authored;
        if (sourceResult.Source is { } source)
        {
            AssemblyTypeSourceEntry.Available? authoredEntry = null;
            Exception? disposalFailure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                authored =
                    await AuthoredSourceAcquisition.AcquireTypeAsync(
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
                if (authored.IsComplete
                    && authored.Text is { } authoredText)
                {
                    authoredEntry =
                        new AssemblyTypeSourceEntry.Available(
                            subject,
                            request,
                            new AssemblyTypeSource.Authored(
                                authoredText,
                                authored,
                                Provenance(source)));
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
            if (authoredEntry is not null)
                return authoredEntry;
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            authored =
                AuthoredSourceAcquisition
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
                participant.BindingPolicy,
                bindingPolicyVersion);
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
                    authored));
        }

        return new AssemblyTypeSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            authored,
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
            cancellationToken);

    internal static async Task<SourceLinkOpenResult> OpenSourceLinkAsync(
        ResolvedAssemblyReference retained,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken)
    {
        SourceLinkService source;
        try
        {
            source =
                SourceLinkService.OpenMetadataOnly(
                    retained,
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
    {
        ApiType? type = ResolveType(session, request.Type);
        if (type is null)
            return null;

        ApiMember? match = null;
        foreach (ApiMember candidate in type.Members)
        {
            if (candidate.MetadataToken != request.MetadataToken
                || ApiMemberIdentity.GetMemberAnchor(type, candidate)
                    != request.Member)
            {
                continue;
            }

            if (match is not null)
                return null;
            match = candidate;
        }

        return match is null ? null : (type, match);
    }

    static AssemblyAuthoredSourceProvenance Provenance(
        SourceLinkService source)
        => new(source.RepositoryUrl, source.CommitHash);

    static AssemblySourceFailure TargetNotFound(string detail)
        => new(
            AssemblySourceFailureKind.TargetNotFound,
            detail);

    static AssemblySourceFailure BothUnavailable()
        => new(
            AssemblySourceFailureKind
                .AuthoredAndDecompiledUnavailable,
            "Neither authored nor decompiled source is available for the selected target.");

    static AssemblySourceFailure InspectionFailure(Exception error)
        => new(
            AssemblySourceFailureKind.InspectionFailed,
            $"Source inspection failed: {error.Message}",
            error);

    static bool IsInspectionFailure(Exception error)
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

    static void EnsureBindingPolicyVersion(
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

    sealed class CancellationObservingBindingPolicy(
        IAssemblyBindingPolicy inner,
        AssemblyBindingPolicyVersion expectedVersion)
        : IAssemblyBindingPolicy
    {
        ExceptionDispatchInfo? _cancellation;
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> _observedAssemblies =
                new(ReferenceEqualityComparer.Instance);

        public AssemblyBindingPolicyVersion Version
        {
            get
            {
                EnsureVersion();
                return expectedVersion;
            }
        }

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            EnsureVersion();
            try
            {
                AssemblyBindingSelection selection =
                    inner.Select(request);
                EnsureVersion();
                return ObserveSelectedAssemblies(selection);
            }
            catch (OperationCanceledException ex)
            {
                ObserveCancellation(ex);
                throw;
            }
        }

        internal void ThrowIfObserved() =>
            Volatile.Read(ref _cancellation)?.Throw();

        void EnsureVersion()
        {
            if (!ReferenceEquals(
                    inner.Version,
                    expectedVersion))
            {
                throw new InvalidOperationException(
                    "The participant binding-policy snapshot changed during source inspection.");
            }
        }

        AssemblyBindingSelection ObserveSelectedAssemblies(
            AssemblyBindingSelection selection)
            => selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    AssemblyBindingSelection.Found(
                        Observe(selected.Assembly)),
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
    }

    sealed record MemberInspectionSeed(
        ResolvedAssemblyReference Retained,
        (ApiType Type, ApiMember Member)? Target);

    sealed record TypeInspectionSeed(
        ResolvedAssemblyReference Retained,
        ApiType? Target);

    internal sealed record SourceLinkOpenResult(
        SourceLinkService? Source,
        Exception? Failure);
}
