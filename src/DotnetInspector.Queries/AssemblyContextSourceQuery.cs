using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Decompiler;
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
/// Exact type request for an authored-or-decompiled source query.
/// </summary>
public sealed record AssemblyTypeSourceRequest
{
    public AssemblyTypeSourceRequest(
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    public MetadataTypeDefinitionName Type { get; }

    public static AssemblyTypeSourceRequest From(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new AssemblyTypeSourceRequest(
            GetDefinitionName(type));
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
/// that the token is expected to denote.
/// </summary>
public sealed record AssemblyMemberSourceRequest
{
    public AssemblyMemberSourceRequest(
        MetadataTypeDefinitionName type,
        MemberAnchor member,
        int metadataToken)
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
    }

    public MetadataTypeDefinitionName Type { get; }
    public MemberAnchor Member { get; }
    public int MetadataToken { get; }

    public static AssemblyMemberSourceRequest From(
        ApiType type,
        ApiMember member)
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
            metadataToken);
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
        AssemblyImageAccessResult<MemberInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant.Assembly,
                (session, retained) => new MemberInspectionSeed(
                    retained,
                    ResolveMember(session, request)));
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
        AssemblyImageAccessResult<TypeInspectionSeed> access;
        try
        {
            access = group.UseAssemblySession(
                participant.Assembly,
                (session, retained) => new TypeInspectionSeed(
                    retained,
                    ResolveType(session, request.Type)));
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
        CancellationToken cancellationToken)
    {
        using SourceLinkService source =
            SourceLinkService.OpenMetadataOnly(
                retained,
                context.Log,
                context.SourceLinkCache);
        await AcquirePdbAsync(
                source,
                retained,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        var findingSubject = new FindingSubject(
            "member",
            request.Member.Format(MemberAnchorFormat.Qualified));
        AuthoredMemberSourceInspection authored =
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
        if (authored.IsComplete && authored.Text is { } authoredText)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Authored(
                    authoredText,
                    authored,
                    Provenance(source)));
        }

        MemberRenderResult decompiled =
            MemberBodyProducer.ProduceMember(
                target.Type,
                target.Member,
                retained,
                participant.BindingPolicy);
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
        CancellationToken cancellationToken)
    {
        using SourceLinkService source =
            SourceLinkService.OpenMetadataOnly(
                retained,
                context.Log,
                context.SourceLinkCache);
        await AcquirePdbAsync(
                source,
                retained,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        var findingSubject = new FindingSubject(
            "type",
            request.Type.ToMetadataFullName());
        AuthoredTypeSourceInspection authored =
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
        if (authored.IsComplete && authored.Text is { } authoredText)
        {
            return new AssemblyTypeSourceEntry.Available(
                subject,
                request,
                new AssemblyTypeSource.Authored(
                    authoredText,
                    authored,
                    Provenance(source)));
        }

        DecompilerResult decompiled =
            MemberBodyProducer.Project(
                target,
                retained,
                participant.BindingPolicy);
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

    sealed record MemberInspectionSeed(
        ResolvedAssemblyReference Retained,
        (ApiType Type, ApiMember Member)? Target);

    sealed record TypeInspectionSeed(
        ResolvedAssemblyReference Retained,
        ApiType? Target);
}
