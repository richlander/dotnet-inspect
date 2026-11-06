using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record ImplementationProfileFamilySelection
{
    public ImplementationProfileFamilySelection(
        string typeDefinitionId,
        IEnumerable<string> stableSelectors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);
        ArgumentNullException.ThrowIfNull(stableSelectors);

        ImmutableArray<string> selectors =
            [.. stableSelectors];
        if (selectors.Length < 2)
        {
            throw new ArgumentException(
                "An implementation-profile family requires at least two "
                    + "stable Member selectors.",
                nameof(stableSelectors));
        }
        if (selectors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Implementation-profile family selectors cannot be blank.",
                nameof(stableSelectors));
        }
        if (selectors.Distinct(StringComparer.Ordinal).Count()
            != selectors.Length)
        {
            throw new ArgumentException(
                "Implementation-profile family selectors must be distinct.",
                nameof(stableSelectors));
        }

        TypeDefinitionId = typeDefinitionId;
        StableSelectors = selectors;
    }

    public string TypeDefinitionId { get; }
    public ImmutableArray<string> StableSelectors { get; }

    internal int? PreparedTypeDefinitionToken
    { get; init; }

    public static ImplementationProfileFamilySelection CreatePrepared(
        ApiType type,
        IEnumerable<ApiMember> members)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(members);

        ImmutableArray<ApiMember> family = [.. members];
        if (family.Any(member =>
                member.Kind is not ("method" or "extension-method")))
        {
            throw new ArgumentException(
                "Implementation-profile family selection accepts public "
                    + "methods only.",
                nameof(members));
        }
        string[] names =
        [
            .. family.Select(member => member.Name)
                .Distinct(StringComparer.Ordinal),
        ];
        if (names.Length != 1)
        {
            throw new ArgumentException(
                "Implementation-profile family selection requires one "
                    + "method name.",
                nameof(members));
        }
        ImmutableArray<ApiMember> completeFamily =
        [
            .. type.Members.Where(member =>
                member.Name == names[0]
                && member.Kind is "method" or "extension-method"),
        ];
        HashSet<string> requestedSelectors =
        [
            .. family.Select(member =>
                ApiMemberIdentity
                    .GetMemberAnchor(type, member)
                    .StableSelector),
        ];
        if (completeFamily.Length != family.Length
            || completeFamily.Any(member =>
                !requestedSelectors.Contains(
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .StableSelector)))
        {
            throw new ArgumentException(
                $"Implementation-profile selection for "
                    + $"'{AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)}"
                    + $".{names[0]}' must name the complete public overload "
                    + "family.",
                nameof(members));
        }

        var selection = new ImplementationProfileFamilySelection(
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
            family.Select(member =>
                ApiMemberIdentity
                    .GetMemberAnchor(type, member)
                    .StableSelector));
        return selection with
        {
            PreparedTypeDefinitionToken =
                completeFamily.All(member => member.Kind == "method")
                    ? type.MetadataToken
                    : null,
        };
    }
}

public sealed record AssemblyImplementationProfileFamilyInspection(
    ImmutableArray<ImplementationProfilePublicMember> Members,
    ImmutableArray<AssemblyImplementationProfileMember> Profiles,
    ImplementationProfilePopulationCoverageReceipt Coverage,
    ImmutableArray<OverloadCallRelationship> OverloadRelationships,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures)
{
    internal ImmutableArray<MethodDefinitionSourceReceipt>
        MethodSourceReceipts
    { get; init; } = [];

    internal ImmutableArray<WorkReceipt> WorkReceipts
    { get; init; } = [];

    internal MethodDefinitionSourceReceipt? MethodSourceReceipt
        => MethodSourceReceipts.Length == 1
            ? MethodSourceReceipts[0]
            : null;

    internal WorkReceipt? WorkReceipt
        => WorkReceipts.Length == 1
            ? WorkReceipts[0]
            : null;
}

/// <summary>
/// Collects implementation profiles for one exact public method-overload
/// family selected by product-issued API identities.
/// </summary>
public static class AssemblyContextImplementationProfileFamilyQuery
{
    static readonly ExactMethodAnalysisBundleProducer FocusedProducer =
        ExactMethodAnalysisBundleProducer.Instance;
    static readonly WorkDescription FocusedWork =
        CreateFocusedWork(FocusedProducer);

    public static InspectionQuery<
        AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>>
        Definition { get; } =
        new(
            "Assembly context implementation profile family",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<
        AssemblyImplementationProfileFamilyInspection> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            ImplementationProfileFamilySelection selection)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(selection);

        AssemblyContextEntry<SelectedFamily> selected =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                participant,
                session => SelectFamily(
                    selection.PreparedTypeDefinitionToken is { } typeToken
                        ? session.ApiTypeSurface(
                            ApiSurfaceExtractionScope.Public,
                            typeToken)
                        : session.CompatibilityApiSurface(
                            ApiSurfaceExtractionScope.Public),
                    selection));
        return selected switch
        {
            AssemblyContextEntry<SelectedFamily>.Rejected rejected =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<SelectedFamily>.Failed failed =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<SelectedFamily>.Available available =>
                AnalyzeParticipant(
                    group,
                    participant,
                    available),
            _ => throw new InvalidOperationException(
                $"Unknown implementation-profile family selection outcome "
                    + $"'{selected.GetType().Name}'."),
        };
    }

    static AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>
        AnalyzeParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextEntry<SelectedFamily>.Available selected) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) =>
            {
                EnsureSameSubject(subject, selected.Subject);
                return Analyze(
                    group,
                    subject,
                    snapshot,
                    selected.Value);
            });

    static AssemblyImplementationProfileFamilyInspection Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        SelectedFamily family)
    {
        var resolver = AssemblyContextAnalysisSource.Resolver(
            group,
            subject);
        FocusedProfileExecution focused =
            ExecuteFocused(
                subject,
                snapshot,
                family.DeclaredBodyTokens);
        ExactMethodImplementationProfileComposition analysis =
            focused.Composition;

        ImmutableArray<AssemblyImplementationProfileMember>
            attributedProfiles =
        [
            .. AssemblyContextImplementationProfilesQuery
                .AttributeProfiles(
                    analysis.Profiles,
                    family.ByDeclaredBodyToken)
                .Where(profile => !profile.PublicMembers.IsEmpty),
        ];
        ImmutableHashSet<int> declaredTokens =
            family.DeclaredBodyTokens;
        ImmutableArray<OverloadCallRelationship> relationships =
        [
            .. analysis.OverloadRelationships.Where(
                relationship =>
                    declaredTokens.Contains(
                        relationship.Caller.MetadataToken)
                    && declaredTokens.Contains(
                        relationship.Callee.MetadataToken)),
        ];
        ImmutableArray<ImplementationProfilePublicMember> members =
            CompleteMembers(
                family.Members,
                attributedProfiles);
        ImplementationProfilePopulationCoverageReceipt coverage =
            ScopeCoverage(
                analysis.Coverage,
                attributedProfiles.Select(profile => profile.Profile),
                declaredTokens);
        var result =
            new AssemblyImplementationProfileFamilyInspection(
                members,
                attributedProfiles,
                coverage,
                relationships,
                analysis.GeneratedFrameworkTypes,
                coverage.Diagnostics,
                family.InspectionFailures)
            {
                MethodSourceReceipts = focused.SourceReceipts,
                WorkReceipts = focused.WorkReceipts,
            };
        resolver.ValidateForPublication();
        return result;
    }

    static FocusedProfileExecution ExecuteFocused(
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        ImmutableHashSet<int> methodTokens)
    {
        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. methodTokens
                .Order()
                .Select(MethodHandle),
        ];
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        MethodDefinitionGeneratedExpansionLimits expansionLimits =
            MethodDefinitionGeneratedExpansionLimits.Default;
        MethodDefinitionSourceBreadth breadth =
            MethodDefinitionSourceBreadth
                .ExactMethods(methods)
                .IncludeGeneratedExecutionBodies(
                    expansionLimits);
        int maximumBodies = checked(
            methods.Length
                + expansionLimits.MaximumGeneratedMethods);
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies,
            session.ImageLength);
        if (TryExecuteFocusedFamily(
                session,
                subject,
                breadth,
                methodTokens,
                limits,
                out FocusedProfileExecution? execution))
        {
            return execution;
        }

        if (TryExecuteFocusedFamily(
                session,
                subject,
                MethodDefinitionSourceBreadth.ExactMethods(
                    methods),
                methodTokens,
                limits,
                out execution))
        {
            return execution;
        }

        return ExecuteFocusedRecovery(
            session,
            subject,
            methods,
            methodTokens,
            limits);
    }

    static bool TryExecuteFocusedFamily(
        AssemblyInspectionSession session,
        AssemblyContextSubject subject,
        MethodDefinitionSourceBreadth breadth,
        ImmutableHashSet<int> methodTokens,
        MethodDefinitionTerminalWorkLimits limits,
        [NotNullWhen(true)] out FocusedProfileExecution? focused)
    {
        try
        {
            AssemblyAnalysisExecution<
                ExactMethodAnalysisBundleProducerResult> execution =
                    ExecuteFocusedOperation(
                        session,
                        subject,
                        breadth,
                        limits);
            ProducerResult<ExactMethodAnalysisBundleProducerResult>
                producerResult =
                    execution.ResultOf(FocusedProducer);
            if (producerResult.HasValue)
            {
                MethodDefinitionSourceReceipt sourceReceipt =
                    (MethodDefinitionSourceReceipt)
                        execution.SourceReceipt;
                focused =
                    new(
                        ExactMethodImplementationProfileComposer.Compose(
                            producerResult.Value!,
                            sourceReceipt,
                            methodTokens),
                        [sourceReceipt],
                        [execution.WorkReceipt]);
                return true;
            }
        }
        catch (BadImageFormatException)
        {
        }

        focused = null;
        return false;
    }

    static FocusedProfileExecution ExecuteFocusedRecovery(
        AssemblyInspectionSession session,
        AssemblyContextSubject subject,
        ImmutableArray<MethodDefinitionHandle> methods,
        ImmutableHashSet<int> methodTokens,
        MethodDefinitionTerminalWorkLimits limits)
    {
        var bodies =
            ImmutableArray.CreateBuilder<
                ExactMethodAnalysisBundleBody>();
        var origins =
            ImmutableArray.CreateBuilder<
                MethodDefinitionGeneratedExpansionOrigin>();
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var unavailableTokens =
            ImmutableArray.CreateBuilder<int>();
        var sourceReceipts =
            ImmutableArray.CreateBuilder<
                MethodDefinitionSourceReceipt>();
        var workReceipts =
            ImmutableArray.CreateBuilder<WorkReceipt>();

        foreach (MethodDefinitionHandle method in methods)
        {
            int token = MetadataTokens.GetToken(method);
            bool completedWithGeneratedBodies =
                TryExecuteFocusedMethod(
                    session,
                    subject,
                    method,
                    includeGeneratedBodies: true,
                    limits,
                    out AssemblyAnalysisExecution<
                        ExactMethodAnalysisBundleProducerResult>?
                            execution,
                    out string? failure);
            bool completedWithoutGeneratedBodies =
                !completedWithGeneratedBodies
                && TryExecuteFocusedMethod(
                    session,
                    subject,
                    method,
                    includeGeneratedBodies: false,
                    limits,
                    out execution,
                    out failure);
            if (completedWithGeneratedBodies
                || completedWithoutGeneratedBodies)
            {
                ProducerResult<ExactMethodAnalysisBundleProducerResult>
                    producerResult =
                        execution!.ResultOf(FocusedProducer);
                ExactMethodAnalysisBundleProducerResult result =
                    producerResult.Value!;
                bodies.AddRange(result.Bodies);
                MethodDefinitionSourceReceipt sourceReceipt =
                    (MethodDefinitionSourceReceipt)
                        execution.SourceReceipt;
                origins.AddRange(
                    sourceReceipt.Coverage.GeneratedExpansion
                        .Origins);
                sourceReceipts.Add(sourceReceipt);
                workReceipts.Add(execution.WorkReceipt);
                if (completedWithoutGeneratedBodies
                    && !result.Bodies.Any(body =>
                        body.Method.Diagnostics.Any()
                        || body.Optimization.Allocation.Diagnostics
                            .Any()))
                {
                    diagnostics.Add(
                        new(
                            MethodToken: 0,
                            "Method Query Source",
                            "Generated-body expansion was unavailable; "
                                + "the exact declared MethodDef was "
                                + "analyzed without generated-body "
                                + "expansion.",
                            SourceMethodToken: token));
                }
                continue;
            }

            unavailableTokens.Add(token);
            diagnostics.Add(
                new(
                    token,
                    $"0x{token:X8}",
                    failure
                        ?? "Exact Method profile analysis failed."));
        }

        var combinedResult =
            new ExactMethodAnalysisBundleProducerResult(
            [
                .. bodies
                    .GroupBy(static body =>
                        body.Method.MethodToken)
                    .Select(static group =>
                        group.First())
                    .OrderBy(static body =>
                        body.Method.MethodToken),
            ]);
        return new(
            ExactMethodImplementationProfileComposer.Compose(
                combinedResult,
                sourceReceipt: null,
                methodTokens,
                diagnostics,
                origins,
                unavailableTokens),
            sourceReceipts.ToImmutable(),
            workReceipts.ToImmutable());
    }

    static bool TryExecuteFocusedMethod(
        AssemblyInspectionSession session,
        AssemblyContextSubject subject,
        MethodDefinitionHandle method,
        bool includeGeneratedBodies,
        MethodDefinitionTerminalWorkLimits limits,
        out AssemblyAnalysisExecution<
            ExactMethodAnalysisBundleProducerResult>? execution,
        out string? failure)
    {
        try
        {
            MethodDefinitionSourceBreadth breadth =
                MethodDefinitionSourceBreadth.ExactMethods(
                    method);
            if (includeGeneratedBodies)
            {
                breadth =
                    breadth.IncludeGeneratedExecutionBodies();
            }

            execution =
                ExecuteFocusedOperation(
                    session,
                    subject,
                    breadth,
                    limits);
            ProducerResult<ExactMethodAnalysisBundleProducerResult>
                producerResult =
                    execution.ResultOf(FocusedProducer);
            if (producerResult.HasValue)
            {
                failure = null;
                return true;
            }

            failure =
                ((MethodDefinitionSourceReceipt)
                    execution.SourceReceipt)
                    .SourceFailure?.Message
                ?? producerResult.Failure?.Message
                ?? producerResult.Critical?.Message
                ?? "The focused implementation-profile producer failed.";
            execution = null;
            return false;
        }
        catch (BadImageFormatException ex)
        {
            execution = null;
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    static AssemblyAnalysisExecution<
        ExactMethodAnalysisBundleProducerResult> ExecuteFocusedOperation(
            AssemblyInspectionSession session,
            AssemblyContextSubject subject,
            MethodDefinitionSourceBreadth breadth,
            MethodDefinitionTerminalWorkLimits limits)
    {
        MethodDefinitionSourceRequest<
            ExactMethodAnalysisBundleProducerResult> request =
                MethodDefinitionSourceRequest<
                    ExactMethodAnalysisBundleProducerResult>.Create(
                        FocusedWork,
                        FocusedProducer,
                        breadth,
                        limits);
        AssemblyAnalysisOperation<
            ExactMethodAnalysisBundleProducerResult> operation =
                AssemblyAnalysisOperation<
                    ExactMethodAnalysisBundleProducerResult>.Create(
                        AssemblyContextAnalysisSource.Name(subject),
                        request.AsAssemblyAnalysisSource());
        AssemblyAnalysisServiceResult<
            ExactMethodAnalysisBundleProducerResult> serviceResult =
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
        if (serviceResult
            is AssemblyAnalysisServiceResult<
                ExactMethodAnalysisBundleProducerResult>.Rejected rejected)
        {
            throw new InvalidOperationException(
                "The focused implementation-profile operation was rejected: "
                    + $"{rejected.Kind}.");
        }

        return ((AssemblyAnalysisServiceResult<
            ExactMethodAnalysisBundleProducerResult>.Completed)
                serviceResult)
            .Execution;
    }

    static MethodDefinitionHandle MethodHandle(int token)
    {
        EntityHandle entity = MetadataTokens.EntityHandle(token);
        if (entity.Kind != HandleKind.MethodDefinition)
        {
            throw new ArgumentException(
                $"Token 0x{token:X8} is not a MethodDef token.",
                nameof(token));
        }

        return (MethodDefinitionHandle)entity;
    }

    static WorkDescription CreateFocusedWork<TResult>(
        ProducerDeclaration<TResult> producer) =>
        ProducerPlanner.Plan(
            [
                new ProducerRequest(
                    producer,
                    ProducerTerminal.Rows),
            ])
        is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new ProducerContractException(
                "The exact-family implementation-profile request must "
                    + "plan.");

    static SelectedFamily SelectFamily(
        ApiSurface surface,
        ImplementationProfileFamilySelection selection)
    {
        ApiType[] matchingTypes =
        [
            .. surface.Types.Where(type =>
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)
                    == selection.TypeDefinitionId),
        ];
        if (matchingTypes.Length != 1)
        {
            throw new InspectionQueryException(
                matchingTypes.Length == 0
                    ? $"Public API Type '{selection.TypeDefinitionId}' "
                        + "was not found."
                    : $"Public API Type '{selection.TypeDefinitionId}' "
                        + "is ambiguous.");
        }

        ApiType type = matchingTypes[0];
        ImmutableArray<FamilyCandidate> candidates =
        [
            .. type.Members.Select(member =>
                Candidate(type, member)),
        ];
        var selected =
            ImmutableArray.CreateBuilder<FamilyCandidate>(
                selection.StableSelectors.Length);
        foreach (string selector in selection.StableSelectors)
        {
            FamilyCandidate[] matches =
            [
                .. candidates.Where(candidate =>
                    candidate.PublicMember.StableSelector == selector),
            ];
            if (matches.Length != 1)
            {
                throw new InspectionQueryException(
                    matches.Length == 0
                        ? $"Public API Member selector '{selector}' was not "
                            + $"found on '{selection.TypeDefinitionId}'."
                        : $"Public API Member selector '{selector}' is "
                            + $"ambiguous on '{selection.TypeDefinitionId}'.");
            }
            selected.Add(matches[0]);
        }

        ImmutableArray<FamilyCandidate> selectedMembers =
            selected.MoveToImmutable();
        if (selectedMembers.Any(candidate => !candidate.IsMethod))
        {
            throw new InspectionQueryException(
                "Implementation-profile family selection accepts public "
                    + "methods only.");
        }

        string[] names =
        [
            .. selectedMembers
                .Select(candidate => candidate.PublicMember.Member)
                .Distinct(StringComparer.Ordinal),
        ];
        if (names.Length != 1)
        {
            throw new InspectionQueryException(
                "Implementation-profile selectors must name one method "
                    + "overload family.");
        }

        ImmutableArray<FamilyCandidate> family =
        [
            .. candidates.Where(candidate =>
                candidate.IsMethod
                && candidate.PublicMember.Member == names[0]),
        ];
        if (family.Length < 2)
        {
            throw new InspectionQueryException(
                $"Public method '{selection.TypeDefinitionId}.{names[0]}' "
                    + "does not have multiple overloads.");
        }

        HashSet<string> requested =
            selection.StableSelectors.ToHashSet(StringComparer.Ordinal);
        if (family.Any(candidate =>
                !requested.Contains(
                    candidate.PublicMember.StableSelector))
            || requested.Count != family.Length)
        {
            throw new InspectionQueryException(
                $"Implementation-profile selection for "
                    + $"'{selection.TypeDefinitionId}.{names[0]}' must name "
                    + "the complete public overload family.");
        }

        var byDeclaredBodyToken =
            new Dictionary<
                int,
                List<ImplementationProfilePublicMember>>();
        foreach (FamilyCandidate candidate in family)
        {
            foreach (int bodyToken in candidate.DeclaredBodyTokens)
            {
                if (!byDeclaredBodyToken.TryGetValue(
                        bodyToken,
                        out List<
                            ImplementationProfilePublicMember>? owners))
                {
                    owners = [];
                    byDeclaredBodyToken.Add(bodyToken, owners);
                }
                owners.Add(candidate.PublicMember);
            }
        }

        HashSet<int> familyTokens =
        [
            .. family.SelectMany(candidate =>
                candidate.DeclaredBodyTokens),
        ];
        if (type.MetadataToken is { } typeToken)
            familyTokens.Add(typeToken);
        foreach (FamilyCandidate candidate in family)
        {
            if (candidate.MemberToken is { } memberToken)
                familyTokens.Add(memberToken);
            if (candidate.DeclarationToken is { } declarationToken)
                familyTokens.Add(declarationToken);
        }

        return new SelectedFamily(
            [.. family.Select(candidate => candidate.PublicMember)],
            byDeclaredBodyToken.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray()),
            [
                .. family.SelectMany(candidate =>
                    candidate.DeclaredBodyTokens),
            ],
            [
                .. surface.InspectionFailures.Where(
                    failure => familyTokens.Contains(
                        failure.SubjectToken)),
            ]);
    }

    static FamilyCandidate Candidate(
        ApiType type,
        ApiMember member)
    {
        ImmutableArray<int> bodyTokens =
        [
            .. CallGraphMemberResolver
                .CreateBodySelectors(type, member)
                .Select(selector => selector.BodyToken)
                .Distinct()
                .Order(),
        ];
        return new(
            member,
            new ImplementationProfilePublicMember(
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                member.Name,
                ApiMemberIdentity
                    .GetMemberAnchor(type, member)
                    .StableSelector,
                []),
            bodyTokens,
            member.Kind is "method" or "extension-method",
            member.MetadataToken,
            member.DeclarationMetadataToken);
    }

    static ImmutableArray<ImplementationProfilePublicMember>
        CompleteMembers(
            ImmutableArray<ImplementationProfilePublicMember> members,
            ImmutableArray<AssemblyImplementationProfileMember> profiles)
    {
        Dictionary<PublicMemberKey, ImmutableArray<int>> bodyTokens =
            profiles
                .SelectMany(profile =>
                    profile.PublicMembers.Select(member =>
                        new
                        {
                            Key = PublicMemberKey.Create(member),
                            member.BodyTokens,
                        }))
                .GroupBy(item => item.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .SelectMany(item => item.BodyTokens)
                        .Distinct()
                        .Order()
                        .ToImmutableArray());
        return
        [
            .. members.Select(member =>
                member with
                {
                    BodyTokens = bodyTokens.GetValueOrDefault(
                        PublicMemberKey.Create(member),
                        []),
                }),
        ];
    }

    // Narrows an execution receipt to the declared methods in scope and the
    // physical bodies profiled for them, so scope-excluded bodies elsewhere in
    // the assembly never appear as unavailable.
    internal static ImplementationProfilePopulationCoverageReceipt ScopeCoverage(
        ImplementationProfilePopulationCoverageReceipt coverage,
        IEnumerable<MethodImplementationProfile> profiles,
        IReadOnlySet<int> declaredTokens)
    {
        ImmutableArray<MethodIdentity> profiledBodies =
        [
            .. profiles
                .Select(profile => profile.EvidenceMethod)
                .Distinct()
                .OrderBy(method => method.MetadataToken),
        ];
        ImmutableArray<ImplementationProfileUnavailableBody>
            unavailableBodies =
        [
            .. coverage.UnavailableBodies.Where(body =>
                declaredTokens.Contains(body.MethodToken)
                || body.Diagnostic?.SourceMethodToken is { } sourceToken
                    && declaredTokens.Contains(sourceToken)),
        ];
        HashSet<int> scopedTokens =
        [
            .. declaredTokens,
            .. profiledBodies.Select(
                method => method.MetadataToken),
            .. unavailableBodies.Select(body => body.MethodToken),
        ];
        return coverage with
        {
            DeclaredMethods =
            [
                .. coverage.DeclaredMethods.Where(method =>
                    declaredTokens.Contains(method.MetadataToken)),
            ],
            ManagedMethodBodies =
            [
                .. coverage.ManagedMethodBodies.Where(method =>
                    scopedTokens.Contains(method.MetadataToken)),
            ],
            ProfiledEvidenceBodies = profiledBodies,
            UnavailableBodies = unavailableBodies,
            Diagnostics =
            [
                .. coverage.Diagnostics.Where(diagnostic =>
                    scopedTokens.Contains(diagnostic.MethodToken)
                    || diagnostic.SourceMethodToken is { } sourceToken
                        && declaredTokens.Contains(sourceToken)),
            ],
        };
    }

    static void EnsureSameSubject(
        AssemblyContextSubject actual,
        AssemblyContextSubject expected)
    {
        if (!ReferenceEquals(actual.Registration, expected.Registration))
        {
            throw new InspectionQueryException(
                "Implementation-profile family selection belongs to another "
                    + "assembly-context participant.");
        }
    }

    sealed record SelectedFamily(
        ImmutableArray<ImplementationProfilePublicMember> Members,
        IReadOnlyDictionary<
            int,
            ImmutableArray<ImplementationProfilePublicMember>>
                ByDeclaredBodyToken,
        ImmutableHashSet<int> DeclaredBodyTokens,
        ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

    sealed record FocusedProfileExecution(
        ExactMethodImplementationProfileComposition Composition,
        ImmutableArray<MethodDefinitionSourceReceipt> SourceReceipts,
        ImmutableArray<WorkReceipt> WorkReceipts);

    sealed record FamilyCandidate(
        ApiMember Member,
        ImplementationProfilePublicMember PublicMember,
        ImmutableArray<int> DeclaredBodyTokens,
        bool IsMethod,
        int? MemberToken,
        int? DeclarationToken);

    readonly record struct PublicMemberKey(
        string TypeDefinitionId,
        string StableSelector)
    {
        public static PublicMemberKey Create(
            ImplementationProfilePublicMember member) =>
            new(member.TypeDefinitionId, member.StableSelector);
    }
}
