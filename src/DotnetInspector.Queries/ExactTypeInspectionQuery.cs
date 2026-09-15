using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;

namespace DotnetInspector.Queries;

public sealed record ExactTypeInspectionRequest(
    int ContextIndex,
    string TypeSelector,
    ApiSurfaceScope Scope,
    ApiSurfaceProjectionLimits SurfaceLimits,
    string? AssemblyName = null,
    AssemblyReferenceIdentity? Library = null,
    string? CompileAssetId = null);

public sealed record ExactTypeForwardingHop(
    AssemblyReferenceIdentity SourceAssembly,
    AssemblyReferenceIdentity TargetAssembly,
    AssemblyResolutionScope Scope);

public sealed record ExactTypeCandidate(
    MetadataTypeDefinitionName Definition,
    MetadataTypeDefinitionAddress Address,
    ExactLibrarySourceCoordinate Declaration,
    ExactLibrarySourceCoordinate Supplier,
    RealizedMemberCoordinate SupplierSource,
    AssemblyReferenceIdentity SupplierAssembly,
    ImmutableArray<ExactTypeForwardingHop> ForwardingHops,
    string DeclarationAssetId,
    string SupplierAssetId);

public enum ExactTypeInspectionFailureKind
{
    InvalidRequest,
    DefinitionMismatch,
    ContextUnavailable,
    ContextLoadFailed,
    PopulationUnavailable,
    DeclarationInventoryIncomplete,
    TypeResolutionRejected,
    TypeResolutionUnavailable,
    TypeResolutionAmbiguous,
    ApiSurfaceRejected,
    ApiSurfaceFailed,
    ApiSurfaceIncomplete,
    AsyncClassificationUnavailable,
    ResolvedTypeMissing,
}

public sealed record ExactTypeInspectionFailure(
    ExactTypeInspectionFailureKind Kind,
    AssemblyReferenceIdentity? Assembly = null,
    WorkspaceContextLoadFailureKind? ContextLoadFailure = null,
    CandidateOpenFailureKind? CandidateOpenFailure = null,
    WorkspaceDeclarationPopulationFailure? PopulationFailure = null,
    ApiSurfaceProjectionLimit? SurfaceLimit = null);

public abstract record ExactTypeInspectionResult
{
    private protected ExactTypeInspectionResult(
        ExactTypeInspectionRequest request,
        WorkspaceDefinitionSnapshotIdentity definition,
        ImmutableArray<ExactTypeInspectionFailure> failures)
    {
        Request = request;
        Definition = definition;
        Failures = failures;
    }

    public ExactTypeInspectionRequest Request { get; }

    public WorkspaceDefinitionSnapshotIdentity Definition { get; }

    public ImmutableArray<ExactTypeInspectionFailure> Failures { get; }

    public sealed record Available : ExactTypeInspectionResult
    {
        internal Available(
            ExactTypeInspectionRequest request,
            WorkspaceDefinitionSnapshotIdentity definition,
            ExactTypeCandidate candidate,
            bool isContextUnique,
            ApiType type,
            ApiMemberInventoryResult members,
            ImmutableArray<ApiSurfaceInspectionFailure> inspectionFailures)
            : base(request, definition, [])
        {
            Candidate = candidate;
            IsContextUnique = isContextUnique;
            Type = type;
            Members = members;
            InspectionFailures = inspectionFailures;
        }

        public ExactTypeCandidate Candidate { get; }

        /// <summary>
        /// Whether the canonical definition has one declaring occurrence in
        /// the complete selected context, independent of the assembly selector.
        /// </summary>
        public bool IsContextUnique { get; }

        public ApiType Type { get; }

        public ApiMemberInventoryResult Members { get; }

        public ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures { get; }
    }

    public sealed record NotFound : ExactTypeInspectionResult
    {
        internal NotFound(
            ExactTypeInspectionRequest request,
            WorkspaceDefinitionSnapshotIdentity definition,
            ImmutableArray<MetadataTypeDefinitionName> suggestions)
            : base(request, definition, []) =>
            Suggestions = suggestions;

        public ImmutableArray<MetadataTypeDefinitionName> Suggestions { get; }
    }

    public sealed record Ambiguous : ExactTypeInspectionResult
    {
        internal Ambiguous(
            ExactTypeInspectionRequest request,
            WorkspaceDefinitionSnapshotIdentity definition,
            ImmutableArray<ExactTypeCandidate> candidates)
            : base(request, definition, []) =>
            Candidates = candidates;

        public ImmutableArray<ExactTypeCandidate> Candidates { get; }
    }

    public sealed record Incomplete : ExactTypeInspectionResult
    {
        internal Incomplete(
            ExactTypeInspectionRequest request,
            WorkspaceDefinitionSnapshotIdentity definition,
            ImmutableArray<ExactTypeCandidate> candidates,
            ImmutableArray<ExactTypeInspectionFailure> failures)
            : base(request, definition, failures) =>
            Candidates = candidates;

        public ImmutableArray<ExactTypeCandidate> Candidates { get; }
    }

    public sealed record Rejected : ExactTypeInspectionResult
    {
        internal Rejected(
            ExactTypeInspectionRequest request,
            WorkspaceDefinitionSnapshotIdentity definition,
            ImmutableArray<ExactTypeInspectionFailure> failures)
            : base(request, definition, failures)
        {
        }
    }
}

public static class ExactTypeInspectionQuery
{
    public static InspectionQuery<ExactTypeInspectionResult> Definition { get; } =
        new("Exact type inspection", InspectionCost.Unbounded);

    public static Task<ExactTypeInspectionResult> ExecuteAsync(
        ExactTypeInspectionRequest request,
        WorkspaceRealizationOperationLease operation,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(request, operation, binding, realization, cancellationToken));

    static ExactTypeInspectionResult Execute(
        ExactTypeInspectionRequest request,
        WorkspaceRealizationOperationLease operation,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(realization);
        cancellationToken.ThrowIfCancellationRequested();

        InspectionWorkspace workspace = operation.Workspace;
        WorkspaceDefinitionSnapshotIdentity definition =
            operation.Definition.Identity;
        if (request.ContextIndex < 0
            || request.ContextIndex >= operation.Definition.Plan.Contexts.Length
            || string.IsNullOrWhiteSpace(request.TypeSelector)
            || TypeMatcher.IsTypeGlobPattern(request.TypeSelector)
            || request.SurfaceLimits is null
            || request.CompileAssetId is not null
                && string.IsNullOrWhiteSpace(request.CompileAssetId)
            || request.Library is { Version: null }
            || request.AssemblyName is { } assemblyName
                && (assemblyName.Length > 1024
                    || !RealizedMemberCoordinate.IsAssemblySimpleName(assemblyName)
                    || TypeMatcher.IsTypeGlobPattern(assemblyName))
            || !Enum.IsDefined(request.Scope))
        {
            return Rejected(ExactTypeInspectionFailureKind.InvalidRequest);
        }

        WorkspaceContextInput planned =
            operation.Definition.Plan.Contexts[request.ContextIndex];
        if (planned.Members is not [WorkspaceMemberCoordinate.PackageMember package]
            || !string.Equals(package.PackageId, binding.Coordinate.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Version, binding.Coordinate.Version, StringComparison.Ordinal)
            || !string.Equals(planned.Framework, binding.Coordinate.Framework, StringComparison.Ordinal)
            || !string.Equals(planned.RuntimeIdentifier, binding.Coordinate.RuntimeIdentifier, StringComparison.Ordinal)
            || realization.SurfaceParticipants.Any(participant =>
                !ReferenceEquals(participant.Package, binding.Root.Identity)))
        {
            return Rejected(ExactTypeInspectionFailureKind.DefinitionMismatch);
        }

        WorkspaceDeclarationPopulationCapture capture =
            workspace.CapturePackageSurfaceDeclarationPopulation(
                request.ContextIndex, planned, binding, realization);
        if (capture is WorkspaceDeclarationPopulationCapture.Rejected rejected)
        {
            return new ExactTypeInspectionResult.Rejected(
                request,
                definition,
                [
                    new(
                        rejected.Failure == WorkspaceDeclarationPopulationFailure.ContextUnavailable
                            ? ExactTypeInspectionFailureKind.DefinitionMismatch
                            : ExactTypeInspectionFailureKind.PopulationUnavailable,
                        PopulationFailure: rejected.Failure),
                ]);
        }

        WorkspaceDeclarationPopulation selectedPopulation =
            ((WorkspaceDeclarationPopulationCapture.Captured)capture).Population;
        TypeDeclarationLocatorResult located =
            TypeDeclarationLocatorQuery.Execute(
                selectedPopulation,
                [
                    new TypeDeclarationLocatorRequest.Pattern(
                        request.TypeSelector),
                    new TypeDeclarationLocatorRequest.Pattern("*"),
                ],
                includeAll: request.Scope == ApiSurfaceScope.IncludeAll,
                cancellationToken: cancellationToken);
        if (located is TypeDeclarationLocatorResult.Rejected locatorRejected)
        {
            return new ExactTypeInspectionResult.Rejected(
                request,
                definition,
                [
                    new(
                        ExactTypeInspectionFailureKind.PopulationUnavailable,
                        PopulationFailure:
                            locatorRejected.PopulationFailure),
                ]);
        }

        var evaluated = (TypeDeclarationLocatorResult.Evaluated)located;
        var contextOccurrences = selectedPopulation.Receipt.Members
            .Select(static member => member.Occurrence).ToHashSet();
        TypeDeclarationLocatorMemberOutcome[] contextOutcomes =
            [.. evaluated.Members.Where(member => contextOccurrences.Contains(member.Member.Occurrence))];
        var occurrences = selectedPopulation.Receipt.Members
            .Where(member =>
                (request.Library is null
                    || AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        member.AssemblyIdentity, request.Library))
                && (request.AssemblyName is null
                    || string.Equals(
                        member.AssemblyIdentity.Name,
                        request.AssemblyName,
                        StringComparison.OrdinalIgnoreCase))
                && (request.CompileAssetId is null
                    || realization.SurfaceParticipants[member.Occurrence.MemberOrder]
                        .Asset.Id == request.CompileAssetId))
            .Select(static member => member.Occurrence)
            .ToHashSet();
        TypeDeclarationLocatorMemberOutcome[] selectedOutcomes =
            [.. contextOutcomes.Where(member => occurrences.Contains(member.Member.Occurrence))];
        if (selectedOutcomes.Length != occurrences.Count
            || selectedOutcomes.Any(static member => !member.IsComplete))
        {
            return new ExactTypeInspectionResult.Incomplete(
                request,
                definition,
                [],
                [
                    new(
                        ExactTypeInspectionFailureKind
                            .DeclarationInventoryIncomplete),
                ]);
        }
        ImmutableArray<TypeDeclarationLocatorCandidate> candidates =
            [.. evaluated.Answers[0].Candidates.Where(candidate =>
                occurrences.Contains(candidate.Observation.Occurrence))];
        ImmutableArray<TypeDeclarationLocatorCandidate> exactNames =
            [.. candidates.Where(candidate => string.Equals(
                candidate.Name.ToMetadataFullName(),
                request.TypeSelector,
                StringComparison.Ordinal))];
        if (!exactNames.IsEmpty)
            candidates = exactNames;
        if (candidates.IsEmpty)
        {
            ImmutableArray<MetadataTypeDefinitionName> suggestions =
                Suggestions(
                    [.. evaluated.Answers[1].Candidates.Where(candidate =>
                        occurrences.Contains(candidate.Observation.Occurrence))],
                    request.TypeSelector);
            return new ExactTypeInspectionResult.NotFound(
                request,
                definition,
                suggestions);
        }

        var choices = new List<ResolvedChoice>();
        var resolutionFailures =
            ImmutableArray.CreateBuilder<ExactTypeInspectionFailure>();
        foreach (TypeDeclarationLocatorCandidate candidate
            in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParticipant(
                    realization,
                    candidate.Observation.Occurrence,
                    out AssemblyContextParticipant root))
            {
                resolutionFailures.Add(
                    new(
                        ExactTypeInspectionFailureKind.ContextUnavailable,
                        candidate.Observation.AssemblyIdentity));
                continue;
            }

            AssemblyContextTypeResolutionResult resolution =
                AssemblyContextTypeResolutionQuery.Execute(
                    realization.SurfaceGroup,
                    root,
                    candidate.Name,
                    AssemblyResolutionScope.Any);
            switch (resolution)
            {
                case AssemblyContextTypeResolutionResult.Available
                    { Outcome: TypeResolutionOutcome.Resolved resolved }:
                    if (!TryParticipant(
                            realization,
                            resolved.Definition.Assembly.Assembly,
                            out AssemblyContextParticipant supplier,
                            out PackageAssemblyRoleParticipant supplierMember))
                    {
                        resolutionFailures.Add(
                            new(
                                ExactTypeInspectionFailureKind
                                    .ContextUnavailable,
                                resolved.Definition.Assembly.Assembly.Identity));
                        break;
                    }
                    AddChoice(
                        choices,
                        new ResolvedChoice(
                            candidate,
                            resolved,
                            supplier,
                            supplierMember,
                            realization.SurfaceParticipants[candidate.Observation.Occurrence.MemberOrder].Asset.Id,
                            binding));
                    break;
                case AssemblyContextTypeResolutionResult.Rejected rejectedResolution:
                    resolutionFailures.Add(
                        new(
                            ExactTypeInspectionFailureKind.TypeResolutionRejected,
                            rejectedResolution.Assembly.Identity,
                            CandidateOpenFailure:
                                rejectedResolution.Failure.Kind));
                    break;
                case AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy unsupported:
                    resolutionFailures.Add(
                        new(
                            ExactTypeInspectionFailureKind
                                .TypeResolutionUnavailable,
                            unsupported.Assembly.Identity));
                    break;
                case AssemblyContextTypeResolutionResult.Available available:
                    resolutionFailures.Add(
                        new(
                            available.Outcome
                                is TypeResolutionOutcome.Ambiguous
                                ? ExactTypeInspectionFailureKind
                                    .TypeResolutionAmbiguous
                                : ExactTypeInspectionFailureKind
                                    .TypeResolutionUnavailable,
                            available.Outcome.TerminalAssemblyIdentity));
                    break;
                default:
                    throw new InspectionQueryException(
                        "Unknown exact-type resolution outcome.");
            }
        }

        ImmutableArray<ExactTypeCandidate> detached =
            [.. choices.Select(Detach)];
        if (resolutionFailures.Count > 0)
        {
            return new ExactTypeInspectionResult.Incomplete(
                request,
                definition,
                detached,
                resolutionFailures.ToImmutable());
        }
        if (choices.Count != 1)
        {
            return new ExactTypeInspectionResult.Ambiguous(
                request,
                definition,
                detached);
        }

        ResolvedChoice selected = choices[0];
        AssemblyContextApiSurfaceResult projected =
            AssemblyContextApiSurfaceQuery.ExecuteBoundedResolved(
                realization.SurfaceGroup,
                request.Scope,
                request.SurfaceLimits,
                [selected.Supplier]);
        if (projected.Truncation is { } truncation)
        {
            return new ExactTypeInspectionResult.Incomplete(
                request,
                definition,
                detached,
                [
                    new(
                        ExactTypeInspectionFailureKind.ApiSurfaceIncomplete,
                        selected.Supplier.Assembly.Identity,
                        SurfaceLimit: truncation.Limit),
                ]);
        }

        AssemblyContextEntry<AssemblyApiSurface> projectedEntry =
            projected.Assemblies.Assemblies.Single();
        if (projectedEntry
            is AssemblyContextEntry<AssemblyApiSurface>.Rejected surfaceRejected)
        {
            return new ExactTypeInspectionResult.Rejected(
                request,
                definition,
                [
                    new(
                        ExactTypeInspectionFailureKind.ApiSurfaceRejected,
                        selected.Supplier.Assembly.Identity,
                        CandidateOpenFailure: surfaceRejected.Failure.Kind),
                ]);
        }
        if (projectedEntry
            is AssemblyContextEntry<AssemblyApiSurface>.Failed)
        {
            return Rejected(
                ExactTypeInspectionFailureKind.ApiSurfaceFailed,
                selected.Supplier.Assembly.Identity);
        }

        AssemblyApiSurface assemblySurface =
            ((AssemblyContextEntry<AssemblyApiSurface>.Available)
                projectedEntry).Value;
        ApiSurface surface = assemblySurface.Surface;
        ApiType? type = surface.Types.SingleOrDefault(candidate =>
            Equals(candidate.DefinitionName, selected.Resolution.Definition.Type));
        if (type is null)
        {
            return Rejected(
                ExactTypeInspectionFailureKind.ResolvedTypeMissing,
                selected.Supplier.Assembly.Identity);
        }

        type.IsForwarded =
            !selected.Resolution.Hops.IsDefaultOrEmpty;
        ApiSurfaceExtractor.PopulateDerivedTypes(surface, type);

        AssemblyImageAccessResult<bool> asyncEnrichment =
            realization.SurfaceGroup.UseAssemblySession(
                selected.Supplier,
                cancellationToken,
                (session, _) =>
                {
                    foreach (ApiMember member in type.Members)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (member.MetadataToken is not { } token
                            || MetadataTokens.EntityHandle(token).Kind
                                != HandleKind.MethodDefinition)
                            continue;

                        MethodBodySource methods = session.MethodBodies;
                        MethodOperandIdentity? identity =
                            methods.ResolveMethodIdentity(token);
                        if (identity is null)
                            return false;
                        MethodBodySelection? method = methods.ResolveMethod(
                            identity.DeclaringType,
                            identity.Name,
                            overloadIndex: 0,
                            publicOnly: false,
                            preferredToken: token);
                        if (method is null || method.MetadataToken != token)
                            return false;
                        member.IsAsync = method.AsyncClassification is not null;
                    }
                    return true;
                });
        if (asyncEnrichment
            is AssemblyImageAccessResult<bool>.Rejected asyncRejected)
        {
            return new ExactTypeInspectionResult.Rejected(
                request,
                definition,
                [
                    new(
                        ExactTypeInspectionFailureKind
                            .AsyncClassificationUnavailable,
                        asyncRejected.Assembly.Identity,
                        CandidateOpenFailure:
                            asyncRejected.Failure.Kind),
                ]);
        }
        if (asyncEnrichment is AssemblyImageAccessResult<bool>.Available
            { Value: false })
        {
            return Rejected(
                ExactTypeInspectionFailureKind.AsyncClassificationUnavailable,
                selected.Supplier.Assembly.Identity);
        }

        type.SourceAssemblyPath = null;
        type.SourceFilePath = null;
        type.AdditionalSourceFiles.Clear();
        foreach (ApiMember member in type.Members)
            member.SourceFilePath = null;
        return new ExactTypeInspectionResult.Available(
            request,
            definition,
            Detach(selected),
            evaluated.Answers[1].Candidates.Count(candidate =>
                candidate.Kind == AssemblyTypeDeclarationKind.Definition
                && candidate.Name.Equals(selected.Resolution.Definition.Type)
                && contextOccurrences.Contains(candidate.Observation.Occurrence)) == 1
                && contextOutcomes.Length == contextOccurrences.Count
                && contextOutcomes.All(static member => member.IsComplete),
            type,
            ApiInventoryQuery.Members(type),
            SelectedFailures(surface, type));

        ExactTypeInspectionResult.Rejected Rejected(
            ExactTypeInspectionFailureKind kind,
            AssemblyReferenceIdentity? assembly = null) =>
            new(
                request,
                definition,
                [new(kind, assembly)]);
    }

    static bool TryParticipant(
        PackageAssemblyContextRealization realization,
        WorkspaceDeclarationOccurrence occurrence,
        out AssemblyContextParticipant participant)
    {
        if (occurrence.MemberOrder >= 0
            && occurrence.MemberOrder < realization.SurfaceParticipants.Length)
        {
            participant = realization.SurfaceParticipants[occurrence.MemberOrder].Participant;
            return true;
        }

        participant = null!;
        return false;
    }

    static bool TryParticipant(
        PackageAssemblyContextRealization realization,
        ResolvedAssemblyReference assembly,
        out AssemblyContextParticipant participant,
        out PackageAssemblyRoleParticipant member)
    {
        foreach (PackageAssemblyRoleParticipant candidate in realization.SurfaceParticipants)
        {
            if (ReferenceEquals(
                    candidate.Participant.Assembly.Registration,
                    assembly.Registration))
            {
                participant = candidate.Participant;
                member = candidate;
                return true;
            }
        }

        participant = null!;
        member = null!;
        return false;
    }

    static void AddChoice(
        List<ResolvedChoice> choices,
        ResolvedChoice choice)
    {
        if (choices.Any(existing =>
                existing.Resolution.Definition.Address
                    == choice.Resolution.Definition.Address
                && ReferenceEquals(
                    existing.Supplier.Assembly.Registration,
                    choice.Supplier.Assembly.Registration)))
        {
            return;
        }

        choices.Add(choice);
    }

    static ExactTypeCandidate Detach(ResolvedChoice choice) =>
        new(
            choice.Resolution.Definition.Type,
            choice.Resolution.Definition.Address,
            choice.Declaration.Coordinate,
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    choice.Binding.Coordinate.PackageId,
                    choice.Binding.Coordinate.Version),
                new ManagedMetadataIdentity.Assembly(choice.Supplier.Assembly.Identity)),
            choice.Binding.Coordinate,
            choice.Supplier.Assembly.Identity,
            [
                .. choice.Resolution.Hops.Select(hop =>
                    new ExactTypeForwardingHop(
                        hop.SourceAssembly.Assembly.Identity,
                        hop.TargetReference,
                        hop.Scope)),
            ],
            choice.DeclarationAssetId,
            choice.SupplierMember.Asset.Id);

    static ImmutableArray<MetadataTypeDefinitionName> Suggestions(
        ImmutableArray<TypeDeclarationLocatorCandidate> candidates,
        string selector)
    {
        TypeDeclarationLocatorCandidate[] names =
        [
            .. candidates
                .DistinctBy(
                    static candidate => candidate.Name,
                    EqualityComparer<MetadataTypeDefinitionName>.Default)
                .Select(candidate => (
                    Candidate: candidate,
                    Distance: StringDistance.EditDistance(
                        TypeMatcher.GetSimpleName(
                            candidate.Name.ToMetadataFullName()),
                        TypeMatcher.GetSimpleName(selector))))
                .OrderBy(static candidate => candidate.Distance)
                .ThenBy(
                    static candidate =>
                        candidate.Candidate.Name.ToMetadataFullName(),
                    StringComparer.Ordinal)
                .Take(6)
                .Select(static candidate => candidate.Candidate),
        ];
        return [.. names.Select(static candidate => candidate.Name)];
    }

    static ImmutableArray<ApiSurfaceInspectionFailure> SelectedFailures(
        ApiSurface surface,
        ApiType type)
    {
        var subjects = new HashSet<int>();
        Add(type.MetadataToken);
        foreach (ApiMember member in type.Members)
        {
            Add(member.MetadataToken);
            Add(member.DeclarationMetadataToken);
            Add(member.GetterToken);
            Add(member.SetterToken);
            Add(member.AdderToken);
            Add(member.RemoverToken);
        }

        return
        [
            .. surface.ConstraintResolutionFailuresBySubject
                .Where(entry => subjects.Contains(entry.Key.SubjectToken))
                .SelectMany(static entry => entry.Value)
                .Concat(surface.InspectionFailures.Where(failure =>
                    failure.Operation != ApiSurface.ConstraintResolutionOperation
                    && (failure.OwningTypeDefinition is { } owner
                        ? owner.Equals(type.DefinitionName)
                        : !failure.AffectedTypeDefinitions.IsDefaultOrEmpty
                            ? failure.AffectedTypeDefinitions.Any(affected => affected.Equals(type.DefinitionName))
                            : failure.SubjectToken == 0
                                || subjects.Contains(failure.OwningTypeToken ?? failure.SubjectToken))))
                .Select(static failure => failure with
                {
                    SourceAssemblyPath = null,
                }),
        ];

        void Add(int? token)
        {
            if (token is int value)
                subjects.Add(value);
        }
    }

    sealed record ResolvedChoice(
        TypeDeclarationLocatorCandidate Declaration,
        TypeResolutionOutcome.Resolved Resolution,
        AssemblyContextParticipant Supplier,
        PackageAssemblyRoleParticipant SupplierMember,
        string DeclarationAssetId,
        PackageRootBinding Binding);
}
