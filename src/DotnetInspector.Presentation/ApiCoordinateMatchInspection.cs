using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Presentation;

/// <summary>Completes exact API matching with the same Content, Share, and diagnostics for either host.</summary>
public static class ApiCoordinateMatchInspection
{
    public static async Task<InspectionEnvelope<ApiCoordinateMatchContent>> ExecuteAsync(
        ApiCoordinateMatchRequest request,
        IPackageRootPayloadProvider provider,
        CancellationToken cancellationToken = default) =>
        Complete(Project(await ApiCoordinateMatchQuery.ExecuteAsync(
            request, provider, cancellationToken).ConfigureAwait(false)));

    public static async Task<InspectionEnvelope<ApiCoordinateMatchContent>> ExecuteAsync(
        InspectionWorkspace workspace,
        StructuralSubjectIdentity.TypeSubject source,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
    {
        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, before, after, cancellationToken)
                .ConfigureAwait(false);
        return Complete(Project(result.Detach()));
    }

    public static async Task<InspectionEnvelope<ApiCoordinateMatchContent>> ExecuteAsync(
        InspectionWorkspace workspace,
        StructuralSubjectIdentity.MemberSubject source,
        ApiDeclarationKind sourceKind,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
    {
        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, sourceKind, before, after, cancellationToken)
                .ConfigureAwait(false);
        return Complete(Project(result.Detach()));
    }

    static InspectionEnvelope<ApiCoordinateMatchContent> Complete(ApiCoordinateMatchContent content) =>
        new(
            InspectionContentKind.Result,
            content,
            new InspectionPortableProjection.NonProjectable(
                "correspondence/endpoints",
                InspectionPortableProjectionFailureReason.NotSupported),
            content.Status is ApiCoordinateMatchStatus.Exact or ApiCoordinateMatchStatus.Absent
                ? []
                : [new InspectionDiagnostic(
                    "api-match." + content.Stage.ToString().ToLowerInvariant(),
                    InspectionDiagnosticSeverity.Error, content.Summary, null)]);

    static ApiCoordinateMatchContent Project(ApiCoordinateMatchQueryResult result)
    {
        if (result.Correspondence is { } correspondence)
            return Project(correspondence);

        ApiCoordinateMatchEndpoint before = result.Before is { } observedBefore
            ? Endpoint(observedBefore)
            : RequestedEndpoint(result.Request, result.Request.SourceVersion);
        ApiCoordinateMatchEndpoint after = result.After is { } observedAfter
            ? Endpoint(observedAfter)
            : RequestedEndpoint(result.Request, result.Request.DestinationVersion);
        ApiCoordinateMatchStatus status = ApiCoordinateMatchStatus.Failed;
        ApiCoordinateMatchStage stage;
        ApiCoordinateMatchStageEvidence evidence;
        ImmutableArray<ApiCoordinateMatchLocation> candidates = [];
        switch (result.Stage)
        {
            case ApiCoordinateMatchQueryStage.Acquisition:
                stage = ApiCoordinateMatchStage.Acquisition;
                var acquisition = result.AcquisitionFailure
                    ?? throw new InvalidOperationException("Acquisition failed without owner evidence.");
                evidence = new(stage, "Unavailable", acquisition.FailureKind.ToString(),
                    Text($"{result.FailedEndpoint}: {acquisition.Message}"));
                break;
            case ApiCoordinateMatchQueryStage.Scope:
                stage = ApiCoordinateMatchStage.Scope;
                evidence = result.ScopeFailure switch
                {
                    WorkspaceScopeOperationResult.Failed failed =>
                        new(stage, "Failed", failed.Failure.ToString(), Text("The selected Package Roots could not be realized.")),
                    WorkspaceScopeOperationResult.Rejected rejected =>
                        new(stage, "Rejected", rejected.Reason.ToString(), Text("The endpoint Scope could not be committed.")),
                    WorkspaceScopeOperationResult.Unavailable unavailable =>
                        new(stage, "Unavailable", unavailable.RuntimeFailure.ToString(), Text("The Workspace is unavailable.")),
                    WorkspaceScopeOperationResult.Cancelled =>
                        new(stage, "Cancelled", null, Text("Endpoint preparation was cancelled.")),
                    WorkspaceScopeOperationResult.Superseded =>
                        new(stage, "Superseded", null, Text("Endpoint preparation was superseded.")),
                    _ => throw new InvalidOperationException("Unknown unsuccessful Scope result."),
                };
                break;
            case ApiCoordinateMatchQueryStage.Observation:
                stage = ApiCoordinateMatchStage.Observation;
                var observation = result.ObservationFailure
                    ?? throw new InvalidOperationException("Observation failed without owner evidence.");
                evidence = new(stage, "Unavailable", observation.Kind.ToString(),
                    Text($"{result.FailedEndpoint}: {observation.Detail}"));
                break;
            case ApiCoordinateMatchQueryStage.SourceSelection:
                stage = ApiCoordinateMatchStage.SourceSelection;
                ApiCoordinateSourceSelectionResult selection = result.SourceSelection
                    ?? throw new InvalidOperationException("Source selection failed without owner evidence.");
                status = selection.Status switch
                {
                    ApiCoordinateSourceSelectionStatus.Ambiguous => ApiCoordinateMatchStatus.Ambiguous,
                    ApiCoordinateSourceSelectionStatus.NotFound or ApiCoordinateSourceSelectionStatus.Refused =>
                        ApiCoordinateMatchStatus.Refused,
                    ApiCoordinateSourceSelectionStatus.Failed => ApiCoordinateMatchStatus.Failed,
                    _ => throw new InvalidOperationException("A successful source selection has no correspondence result."),
                };
                evidence = new(stage, selection.Status.ToString(),
                    selection.Failure?.MemberDiagnostic?.ToString() ?? selection.Failure?.Kind.ToString(),
                    Optional(selection.Failure?.Detail));
                if (result.Before is { } sourceObservation)
                {
                    candidates =
                    [
                        .. selection.Candidates.Select(candidate =>
                            Location(sourceObservation, candidate, null)),
                    ];
                    if (selection.MemberCandidates.Length > 0 && selection.Candidates.Length == 1)
                    {
                        ApiCoordinateMatchLocation type = candidates[0];
                        candidates =
                        [
                            .. selection.MemberCandidates.Select(member => type with
                            {
                                Member = Text(member.StableSelector),
                                Signature = Text(member.CanonicalSignature),
                            }),
                        ];
                    }
                }
                break;
            default:
                throw new InvalidOperationException("Unknown incomplete API match stage.");
        }
        return new()
        {
            Status = status,
            Stage = stage,
            Summary = evidence.Detail ?? Text("Exact API matching could not be completed."),
            Before = before,
            After = after,
            Candidates = candidates,
            Stages = [evidence],
        };
    }

    static ApiCoordinateMatchContent Project(ApiCoordinateCorrespondenceEvidence result)
    {
        CoordinateLibraryPairingEvidence pairing = result.LibraryPairing;
        var stages = ImmutableArray.CreateBuilder<ApiCoordinateMatchStageEvidence>();
        stages.Add(new(ApiCoordinateMatchStage.LibraryPairing, pairing.Status.ToString(),
            pairing.Failure?.Kind.ToString(), Optional(pairing.Failure?.Detail),
            CandidateCount: pairing.Candidates.Length));
        ApiCoordinateMatchStage stage = ApiCoordinateMatchStage.LibraryPairing;
        if (result.SourceBinding is { } binding)
        {
            stage = ApiCoordinateMatchStage.SourceBinding;
            stages.Add(new(stage, binding.Status.ToString(), binding.Reason.ToString(), Optional(binding.Detail)));
        }

        ImmutableArray<ApiCoordinateMatchHop> hops = [];
        if (result.Resolution is { } resolution)
        {
            stage = ApiCoordinateMatchStage.TypeResolution;
            stages.Add(ResolutionStage(resolution));
            if (resolution is CoordinateTypeResolutionEvidence.Available available)
            {
                hops =
                [
                    .. available.Outcome.Hops.Select(hop => new ApiCoordinateMatchHop(
                        ResolutionLocation(hop.SourceAssembly.Assembly),
                        Assembly(hop.TargetReference),
                        [.. hop.Declarations.Select(token => token.Value)],
                        hop.Scope)),
                ];
            }
        }
        if (result.Correspondence is { } declaration)
        {
            stage = ApiCoordinateMatchStage.DeclarationCorrespondence;
            stages.Add(new(stage, declaration.Status.ToString(),
                declaration.Reason.ToString(), Optional(declaration.Detail),
                CandidateCount: declaration.Candidates.Length));
        }
        if (result.Failure is { } failure)
        {
            stage = failure.Kind is ApiCoordinateCorrespondenceFailureKind.SourceRootUnavailable
                or ApiCoordinateCorrespondenceFailureKind.DestinationRootUnavailable
                ? ApiCoordinateMatchStage.Scope
                : stage;
            stages.Add(new(stage, result.Status.ToString(), failure.Kind.ToString(), Text(failure.Detail)));
        }

        ApiCoordinateMatchStatus status = result.Status switch
        {
            ApiCoordinateCorrespondenceStatus.Exact => ApiCoordinateMatchStatus.Exact,
            ApiCoordinateCorrespondenceStatus.Absent => ApiCoordinateMatchStatus.Absent,
            ApiCoordinateCorrespondenceStatus.Ambiguous => ApiCoordinateMatchStatus.Ambiguous,
            ApiCoordinateCorrespondenceStatus.Refused => ApiCoordinateMatchStatus.Refused,
            ApiCoordinateCorrespondenceStatus.Failed => ApiCoordinateMatchStatus.Failed,
            _ => throw new InvalidOperationException("Unknown API correspondence status."),
        };
        ImmutableArray<ApiCoordinateMatchLocation> candidates =
            pairing.Status == CoordinateLibraryPairingStatus.Ambiguous
                ? [.. pairing.Candidates.Select(candidate => LibraryLocation(candidate))]
                : [];
        if (result.Correspondence is { Candidates.IsEmpty: false } candidateResult)
        {
            CoordinateApiLibraryEvidence? defining =
                (result.Resolution as CoordinateTypeResolutionEvidence.Available)?.Outcome
                    is CoordinateTypeResolutionOutcomeEvidence.Resolved resolved
                    ? resolved.Definition.Assembly.Assembly.Library
                    : null;
            if (defining is not null)
            {
                candidates =
                [
                    .. candidateResult.Candidates.Select(candidate =>
                        LibraryLocation(defining) with
                        {
                            Type = Text(candidate.DeclaringType.ToEscapedFullName()),
                            Member = Optional(candidate.Member?.StableSelector),
                            Signature = Optional(candidate.Member?.CanonicalSignature),
                            ModuleVersionId = candidate.Endpoint.ModuleVersionId,
                            MetadataToken = candidate.Location?.MetadataToken,
                            DeclarationKind = candidate.Kind,
                        }),
                ];
            }
        }
        return new()
        {
            Status = status,
            Stage = stage,
            Summary = Text(status switch
            {
                ApiCoordinateMatchStatus.Exact => "Exact counterpart found.",
                ApiCoordinateMatchStatus.Absent when pairing.Status == CoordinateLibraryPairingStatus.Absent =>
                    "No corresponding Library exists in the selected destination API population.",
                ApiCoordinateMatchStatus.Absent =>
                    "No exact counterpart exists under the strict declaration profile.",
                ApiCoordinateMatchStatus.Ambiguous => "More than one candidate remains; no counterpart was selected.",
                _ => stages[^1].Detail?.ToString()
                    ?? $"Exact API matching stopped at {stage}: {stages[^1].Reason ?? stages[^1].Outcome}.",
            }),
            Before = Endpoint(pairing.Before),
            After = Endpoint(pairing.After),
            Source = Location(result.Source),
            DestinationEntry = pairing.Destination is { } entry ? LibraryLocation(entry) : null,
            Destination = result.Destination is { } destination
                ? Location(destination)
                : null,
            Candidates = candidates,
            ForwardingHops = hops,
            Stages = stages.ToImmutable(),
            Evidence = result,
        };
    }

    static ApiCoordinateMatchStageEvidence ResolutionStage(CoordinateTypeResolutionEvidence resolution)
    {
        const ApiCoordinateMatchStage stage = ApiCoordinateMatchStage.TypeResolution;
        if (resolution is CoordinateTypeResolutionEvidence.QueryRejected rejected)
            return new(stage, "QueryRejected", rejected.Failure.Kind.ToString(), Text(rejected.Failure.Detail));
        if (resolution is CoordinateTypeResolutionEvidence.UnsupportedBindingPolicy)
            return new(stage, "UnsupportedBindingPolicy", null,
                Text("Destination resolution requires an acquisition-free binding policy."));
        var outcome = ((CoordinateTypeResolutionEvidence.Available)resolution).Outcome;
        return outcome switch
        {
            CoordinateTypeResolutionOutcomeEvidence.Resolved
            {
                Definition.KindResolutionFailure: { } failure,
            } => new(
                stage,
                "ResolvedKindUnavailable",
                failure.GetType().Name,
                Text("The destination Type definition resolved, but its "
                    + "definition kind is unavailable."),
                Budget: FailureBudget(failure)),
            CoordinateTypeResolutionOutcomeEvidence.Resolved => new(stage, "Resolved", null, null),
            CoordinateTypeResolutionOutcomeEvidence.NotFound notFound =>
                new(stage, "NotFound", null, Text(notFound.Hops.IsEmpty
                    ? "The entry Library has no declaration for the exact Type."
                    : "An explicit forwarding route ended without a Type definition.")),
            CoordinateTypeResolutionOutcomeEvidence.Unbound unbound =>
                new(stage, "UnboundBinding", null,
                    Text("An explicit forwarding target is not bound in the selected destination Package."),
                    Target(unbound.Target)),
            CoordinateTypeResolutionOutcomeEvidence.Unavailable unavailable =>
                new(stage, "Unavailable", unavailable.Failure.Kind.ToString(),
                    Text("An explicit forwarding target is unavailable."), Target(unavailable.Target)),
            CoordinateTypeResolutionOutcomeEvidence.Ambiguous ambiguous =>
                new(stage, "Ambiguous", ambiguous.Ambiguity.GetType().Name,
                    Text("Destination Type resolution is ambiguous.")),
            CoordinateTypeResolutionOutcomeEvidence.Rejected failure =>
                new(stage, "Rejected", failure.Failure.GetType().Name,
                    Text("Destination Type resolution was rejected."),
                    Budget: FailureBudget(failure.Failure)),
            _ => throw new InvalidOperationException("Unknown destination Type resolution outcome."),
        };
    }

    static int? FailureBudget(
        CoordinateTypeResolutionFailureEvidence failure) =>
        failure switch
        {
            CoordinateTypeResolutionFailureEvidence.DeclarationBudgetExceeded
                limit => limit.Budget,
            CoordinateTypeResolutionFailureEvidence.DefinitionKindUnavailable
            {
                Failure:
                    MetadataTypeDefinitionKindFailure.BudgetExceeded limit,
            } => checked((int)limit.Budget),
            CoordinateTypeResolutionFailureEvidence.HopBudgetExceeded limit =>
                limit.Budget,
            CoordinateTypeResolutionFailureEvidence.RequestBudgetExceeded
                limit => limit.Budget,
            CoordinateTypeResolutionFailureEvidence.DiscoveryBudgetExceeded
                limit => limit.Budget,
            _ => null,
        };

    static ApiCoordinateMatchAssembly? Target(CoordinateAssemblyBindingTargetEvidence target) =>
        target is CoordinateAssemblyBindingTargetEvidence.AssemblyReference reference
            ? Assembly(reference.Identity)
            : null;

    static ApiCoordinateMatchLocation ResolutionLocation(CoordinateResolutionAssemblyEvidence assembly) =>
        assembly.Library is { } library
            ? LibraryLocation(library) with { ModuleVersionId = assembly.Registration.ModuleVersionId }
            : new(null, null, Assembly(assembly.Identity), null, null, null,
                assembly.Registration.ModuleVersionId, null);

    static ApiCoordinateMatchLocation Location(
        ApiCoordinateDeclarationEvidence declaration) =>
        LibraryLocation(declaration.Library) with
        {
            Type = Text(declaration.DeclaringType.ToEscapedFullName()),
            Member = Optional(declaration.Member?.StableSelector),
            Signature = Optional(declaration.Member?.CanonicalSignature),
            ModuleVersionId = declaration.Declaration?.Location.ModuleVersionId,
            MetadataToken = declaration.Declaration?.Location.MetadataToken,
            DeclarationKind = declaration.Kind,
        };

    static ApiCoordinateMatchLocation Location(
        CoordinatePackageObservation observation,
        StructuralSubjectIdentity subject,
        ApiDeclarationReference? declaration)
    {
        StructuralSubjectIdentity.TypeSubject type = subject switch
        {
            StructuralSubjectIdentity.TypeSubject value => value,
            StructuralSubjectIdentity.MemberSubject value => value.DeclaringType,
            _ => throw new InvalidOperationException("A match coordinate must identify a Type or Member."),
        };
        CoordinateApiLibraryObservation? library = observation.Libraries.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Subject.Identity.Registration, type.Library.Identity.Registration));
        MemberAnchor? member = (subject as StructuralSubjectIdentity.MemberSubject)?.Identity.Member;
        ApiCoordinateMatchLocation basis = library is null
            ? new(Endpoint(type.Library.Package.Descriptor), null,
                Assembly(type.Library.Identity.Assembly), null, null, null, null, null)
            : LibraryLocation(library);
        return basis with
        {
            Type = Text(type.Identity.Type.ToEscapedFullName()),
            Member = Optional(member?.StableSelector),
            Signature = Optional(member?.CanonicalSignature),
            ModuleVersionId = declaration?.Location.ModuleVersionId,
            MetadataToken = declaration?.Location.MetadataToken,
            DeclarationKind = declaration?.Kind,
        };
    }

    static ApiCoordinateMatchLocation LibraryLocation(CoordinateApiLibraryObservation library) =>
        new(Endpoint(library.Subject.Package.Descriptor), Text(library.Asset.Path),
            Assembly(library.Assembly), null, null, null, null, null);

    static ApiCoordinateMatchLocation LibraryLocation(CoordinateApiLibraryEvidence library) =>
        new(Endpoint(library.Package), Optional(library.Asset?.Path),
            Assembly(library.Assembly.Assembly), null, null, null, null, null);

    static ApiCoordinateMatchAssembly Assembly(AssemblyReferenceIdentity identity) =>
        new(Text(identity.Name), Optional(identity.Version?.ToString()),
            Optional(identity.Culture), Optional(identity.PublicKeyToken));

    static ApiCoordinateMatchEndpoint Endpoint(CoordinatePackageObservation observation) =>
        Endpoint(observation.Occurrence.Package);

    static ApiCoordinateMatchEndpoint Endpoint(WorkspacePackageDescriptor package) =>
        new(Text(package.PackageId), Text(package.PackageVersion), Optional(package.SelectedTargetFramework));

    static ApiCoordinateMatchEndpoint RequestedEndpoint(ApiCoordinateMatchRequest request, string version) =>
        new(Text(request.PackageId), Text(version), Optional(request.TargetFramework));

    static InertString Text(string value) => new(TextPolicy.Field, value);
    static InertString? Optional(string? value) => value is null ? null : Text(value);
}
