using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public enum ApiCoordinateSourceSelectionStatus
{
    Selected,
    NotFound,
    Ambiguous,
    Refused,
    Failed,
}

public enum ApiCoordinateSourceSelectionFailureKind
{
    RootUnavailable,
    EndpointMismatch,
    PopulationUnavailable,
    ProjectionTruncated,
    ImageUnavailable,
    MetadataMalformed,
    LibraryNotFound,
    TypeNotFound,
    AmbiguousType,
    MemberSelection,
    AccessorSelection,
}

public sealed record ApiCoordinateSourceSelectionFailure(
    ApiCoordinateSourceSelectionFailureKind Kind,
    string Detail,
    ArtifactRootFailure? RootFailure = null,
    CandidateOpenFailure? ImageFailure = null,
    ApiSurfaceProjectionTruncation? Truncation = null,
    MemberTargetDiagnosticKind? MemberDiagnostic = null);

/// <summary>A detached source selection, not a destination correspondence claim.</summary>
public sealed class ApiCoordinateSourceSelectionResult
{
    internal ApiCoordinateSourceSelectionResult(
        ApiCoordinateSourceSelectionStatus status,
        StructuralSubjectIdentity? subject = null,
        ImmutableArray<StructuralSubjectIdentity.TypeSubject> candidates = default,
        ImmutableArray<MemberAnchor> memberCandidates = default,
        ApiCoordinateSourceSelectionFailure? failure = null,
        ImmutableArray<ExactTypeApiInspectionFailure> inspectionFailures = default,
        MemberTargetKind? memberKind = null)
    {
        Status = status;
        Subject = subject;
        Candidates = candidates.IsDefault ? [] : candidates;
        MemberCandidates = memberCandidates.IsDefault ? [] : memberCandidates;
        Failure = failure;
        InspectionFailures = inspectionFailures.IsDefault ? [] : inspectionFailures;
        MemberKind = memberKind;
    }

    public ApiCoordinateSourceSelectionStatus Status { get; }
    public StructuralSubjectIdentity? Subject { get; }
    public ImmutableArray<StructuralSubjectIdentity.TypeSubject> Candidates { get; }
    public ImmutableArray<MemberAnchor> MemberCandidates { get; }
    public ApiCoordinateSourceSelectionFailure? Failure { get; }
    public ImmutableArray<ExactTypeApiInspectionFailure> InspectionFailures { get; }
    public MemberTargetKind? MemberKind { get; }
}

/// <summary>Resolves a user selector once, solely in its observed source Package.</summary>
public static class ApiCoordinateSourceSelectionQuery
{
    public static ApiSurfaceProjectionLimits DefaultLimits { get; } =
        new(64, 20_000, 250_000, 1_000, 20_000, 5_000_000, 16_000_000);

    public static async ValueTask<ApiCoordinateSourceSelectionResult> ExecuteAsync(
        InspectionWorkspace workspace,
        CoordinatePackageObservation source,
        ApiCoordinateMatchRequest request,
        ApiSurfaceProjectionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                source.Occurrence.Package.PackageId, request.PackageId)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                source.Occurrence.Package.PackageVersion, request.SourceVersion))
        {
            return Stop(ApiCoordinateSourceSelectionStatus.Refused,
                ApiCoordinateSourceSelectionFailureKind.EndpointMismatch,
                "The source observation does not name the requested Package endpoint.");
        }
        ArtifactRootResult<ApiCoordinateSourceSelectionResult> access =
            await workspace.ExecutePackageRootQueryAsync(
                source.Correspondence,
                source.Generation,
                (realization, token) => ValueTask.FromResult(
                    Select(realization, source, request, limits ?? DefaultLimits, token)),
                source.BindingPolicy,
                cancellationToken).ConfigureAwait(false);
        return access switch
        {
            ArtifactRootResult<ApiCoordinateSourceSelectionResult>.Available available => available.Value,
            ArtifactRootResult<ApiCoordinateSourceSelectionResult>.Rejected rejected =>
                new(ApiCoordinateSourceSelectionStatus.Failed, failure: new(
                    ApiCoordinateSourceSelectionFailureKind.RootUnavailable,
                    "The exact source Package Root is unavailable.",
                    RootFailure: rejected.Failure)),
            _ => throw new InvalidOperationException("Unknown source Root access result."),
        };
    }

    static ApiCoordinateSourceSelectionResult Select(
        PackageAssemblyContextRealization realization,
        CoordinatePackageObservation source,
        ApiCoordinateMatchRequest request,
        ApiSurfaceProjectionLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateApiLibraryObservation[] libraries =
        [
            .. source.Libraries.Where(library => request.Library is null
                || StringComparer.OrdinalIgnoreCase.Equals(library.Assembly.Name, request.Library)
                || StringComparer.OrdinalIgnoreCase.Equals(library.Asset.Path, request.Library)),
        ];
        if (request.Library is not null && libraries.Length == 0)
            return Stop(ApiCoordinateSourceSelectionStatus.NotFound,
                ApiCoordinateSourceSelectionFailureKind.LibraryNotFound,
                "The requested source Library is not in the selected API population.");
        if (!realization.HasAssemblyContexts)
            return Stop(ApiCoordinateSourceSelectionStatus.NotFound,
                ApiCoordinateSourceSelectionFailureKind.TypeNotFound,
                "The source Package has an empty selected API population.");

        AssemblyContextGroup group = realization.SurfaceGroup;
        AssemblyContextParticipant[] participants =
        [
            .. group.Participants.Where(participant => libraries.Any(library =>
                ReferenceEquals(library.Subject.Identity.Registration,
                    NavigationRegistrationIdentity.From(participant.Assembly.Registration)))),
        ];
        if (participants.Length != libraries.Length)
            return Stop(ApiCoordinateSourceSelectionStatus.Failed,
                ApiCoordinateSourceSelectionFailureKind.PopulationUnavailable,
                "The source observation does not join to the exact current API participants.");

        AssemblyContextApiSurfaceResult projection = AssemblyContextApiSurfaceQuery.ExecuteBounded(
            group, request.IncludeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            limits, participants);
        if (projection.Truncation is { } truncation)
            return new(ApiCoordinateSourceSelectionStatus.Failed, failure: new(
                ApiCoordinateSourceSelectionFailureKind.ProjectionTruncated,
                "Source selection exceeded its API projection budget.", Truncation: truncation));

        var candidates = new List<(StructuralSubjectIdentity.TypeSubject Subject, ApiType Type)>();
        var failures = ImmutableArray.CreateBuilder<ExactTypeApiInspectionFailure>();
        foreach (AssemblyContextEntry<AssemblyApiSurface> entry in projection.Assemblies.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (entry)
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected:
                    return new(ApiCoordinateSourceSelectionStatus.Failed, failure: new(
                        ApiCoordinateSourceSelectionFailureKind.ImageUnavailable,
                        "A source API image could not be read.", ImageFailure: rejected.Failure));
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    return Stop(ApiCoordinateSourceSelectionStatus.Failed,
                        ApiCoordinateSourceSelectionFailureKind.MetadataMalformed, failed.Error.Message);
                case AssemblyContextEntry<AssemblyApiSurface>.Available available:
                    CoordinateApiLibraryObservation library = libraries.Single(candidate =>
                        ReferenceEquals(candidate.Subject.Identity.Registration,
                            NavigationRegistrationIdentity.From(available.Subject.Registration)));
                    failures.AddRange(available.Value.InspectionFailures.Select(ExactTypeApiInspectionFailure.From));
                    foreach (ApiType type in available.Value.Surface.Types)
                    {
                        if (type.DefinitionName is not { } name)
                            return Stop(ApiCoordinateSourceSelectionStatus.Failed,
                                ApiCoordinateSourceSelectionFailureKind.MetadataMalformed,
                                "A projected source Type has no exact Metadata definition name.");
                        candidates.Add((StructuralSubjectIdentity.ForType(library.Subject, name), type));
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown source API projection result.");
            }
        }
        if (failures.Count > 0)
            return new(ApiCoordinateSourceSelectionStatus.Failed,
                failure: new(ApiCoordinateSourceSelectionFailureKind.MetadataMalformed,
                    "Source API projection reported incomplete metadata evidence."),
                inspectionFailures: failures.ToImmutable());

        var matching = candidates.Where(candidate =>
            candidate.Subject.Identity.Type.ToEscapedFullName().Equals(
                request.Type, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Count == 0)
            matching = candidates.Where(candidate => TypeMatcher.MatchesExactTypeName(
                candidate.Subject.Identity.Type.ToEscapedFullName(), request.Type)).ToList();
        if (matching.Count == 0)
            matching = candidates.Where(candidate => TypeMatcher.MatchesTypeFilter(
                candidate.Subject.Identity.Type.ToEscapedFullName(), request.Type)).ToList();
        if (matching.Count == 0)
            return Stop(ApiCoordinateSourceSelectionStatus.NotFound,
                ApiCoordinateSourceSelectionFailureKind.TypeNotFound,
                "No source Type matches the requested selector.");
        if (matching.Count > 1)
            return new(ApiCoordinateSourceSelectionStatus.Ambiguous,
                candidates: [.. matching.Select(candidate => candidate.Subject)],
                failure: new(ApiCoordinateSourceSelectionFailureKind.AmbiguousType,
                    "The source selector identifies more than one Type declaration."));

        (StructuralSubjectIdentity.TypeSubject subject, ApiType selectedType) = matching[0];
        if (request.Member is null)
            return new(ApiCoordinateSourceSelectionStatus.Selected, subject);

        MemberTargetSelector selector = MemberTargetSelector.Parse(request.Member);
        MemberTargetResolution member = MemberTargetResolver.Resolve(selectedType, selector);
        if (member.Target is not { } target)
        {
            MemberTargetDiagnostic diagnostic = member.Diagnostic
                ?? throw new InvalidOperationException("A failed Member selection has no diagnostic.");
            return new(diagnostic.Kind is MemberTargetDiagnosticKind.AmbiguousMember
                    or MemberTargetDiagnosticKind.DigestAmbiguous
                ? ApiCoordinateSourceSelectionStatus.Ambiguous
                : ApiCoordinateSourceSelectionStatus.Refused,
                candidates: [subject],
                memberCandidates: [.. diagnostic.Candidates.Select(candidate => candidate.Anchor)],
                failure: new(ApiCoordinateSourceSelectionFailureKind.MemberSelection,
                    diagnostic.Message, MemberDiagnostic: diagnostic.Kind));
        }
        if (selector.OverloadIndex is not null
            && target.Kind is MemberTargetKind.Property or MemberTargetKind.Event
            && (selector.DigestPrefix is not null || member.Candidates.Count == 1))
        {
            return Stop(ApiCoordinateSourceSelectionStatus.Refused,
                ApiCoordinateSourceSelectionFailureKind.AccessorSelection,
                "An accessor ordinal selects body intent. Match the Property/Event declaration without an accessor ordinal.");
        }
        return new(ApiCoordinateSourceSelectionStatus.Selected,
            StructuralSubjectIdentity.ForMember(subject, target.Anchor),
            memberKind: target.Kind);
    }

    static ApiCoordinateSourceSelectionResult Stop(
        ApiCoordinateSourceSelectionStatus status,
        ApiCoordinateSourceSelectionFailureKind kind,
        string detail) => new(status, failure: new(kind, detail));
}
