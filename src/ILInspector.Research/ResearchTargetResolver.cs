using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// Research-owned side-local target request planning and terminal attempt
/// resolution for the implementation-comparison profile.
/// </summary>
/// <remarks>
/// <para>
/// Planning is inert: it mints one scope per member-selection occurrence, one
/// domain per version-erased assembly identity inside that scope, and one
/// request plus one attempt per required side-local input evaluation. It reads
/// only the acquisition-owned <c>AssemblyReferenceIdentity</c> of each admitted
/// descriptor; it opens nothing.
/// </para>
/// <para>
/// Resolution borrows each implementation input only while it resolves, staging
/// one <see cref="MetadataSource"/> per admitted input for all of that input's
/// requests. It validates the live image against the acquisition descriptor and
/// the Analysis-issued module identity before it resolves anything, reuses
/// <see cref="MemberTargetResolver"/> unchanged, and derives the relationship
/// role only from the selected member's MethodDef tokens after selection
/// succeeds.
/// </para>
/// <para>
/// This boundary establishes no correspondence, absence proof, census key,
/// producer topology, or work item. It produces one complete typed attempt set
/// per planned domain and nothing more.
/// </para>
/// <para>
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c>,
/// <c>ResearchTargetAttempts_AccountForEveryRequestExactlyOnce</c>,
/// <c>ResearchTargetScopes_DeriveBijectivelyFromSelectionOccurrences</c>,
/// <c>ResearchTargetResolution_StagesEachAdmittedInputOnce</c>,
/// <c>ResearchTargetInputValidation_RejectsMismatchedModuleEvidence</c>, and
/// <c>ResearchTargetCancellation_ExposesNoPartialPopulationOrResult</c> gate
/// these properties.
/// </para>
/// </remarks>
public static class ResearchTargetResolver
{
    /// <summary>
    /// Plans and resolves every side-local target for one admitted
    /// implementation-comparison population.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was observed. No partial plan,
    /// identity, or result is exposed.
    /// </exception>
    public static ResearchTargetPlanningOutcome Resolve(
        ResearchTargetPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ResearchTargetPlanningRejection? rejection = Validate(request);
        if (rejection is not null)
            return new ResearchTargetPlanningOutcome.Rejected(rejection);

        IReadOnlyList<PlannedScope> plan = Plan(request, cancellationToken);
        ResolveAttempts(plan, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ResearchTargetResolution resolution = Materialize(
            request.Population.Operation,
            plan);
        ResearchTargetResolutionValidator.Validate(
            request,
            resolution,
            ValidationEvidence(plan));
        cancellationToken.ThrowIfCancellationRequested();
        return new ResearchTargetPlanningOutcome.Planned(resolution);
    }

    // ---------------------------------------------------------------- validate

    static ResearchTargetPlanningRejection? Validate(
        ResearchTargetPlanningRequest request)
    {
        ResearchAdmittedPopulation population = request.Population;
        if (population.Profile
            != ResearchComparisonProfile.ImplementationComparison)
        {
            return Reject(
                ResearchTargetPlanningRejectionKind.UnsupportedProfile,
                new ResearchTargetPlanningLocation.Operation(),
                $"The {population.Profile} profile supplies no typed Metadata target evidence.");
        }

        HashSet<ResearchAdmittedInput> admitted = new(
            population.Inputs,
            ReferenceEqualityComparer.Instance);
        Dictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles = new(
            ReferenceEqualityComparer.Instance);

        for (int index = 0; index < request.InputRoles.Length; index++)
        {
            ResearchTargetPlanningLocation location =
                new ResearchTargetPlanningLocation.InputRole(index);
            ResearchTargetInputRoleAssignment? assignment =
                request.InputRoles[index];
            if (assignment is null)
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.MissingInputRole,
                    location,
                    "A role assignment must not be null.");
            }

            if (!Enum.IsDefined(assignment.Role))
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.UndeclaredInputRole,
                    location,
                    "A role assignment must carry a declared role.");
            }

            if (!admitted.Contains(assignment.Input))
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.ForeignInputRole,
                    location,
                    "A role assignment must name an input this population admitted.");
            }

            if (!roles.TryAdd(assignment.Input, assignment.Role))
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.DuplicateInputRole,
                    location,
                    "An admitted input must carry exactly one role assignment.");
            }
        }

        if (roles.Count != admitted.Count)
        {
            return Reject(
                ResearchTargetPlanningRejectionKind.MissingInputRole,
                new ResearchTargetPlanningLocation.Operation(),
                "Every admitted input must carry an explicit role assignment.");
        }

        if (request.Selections.Length == 0)
        {
            return Reject(
                ResearchTargetPlanningRejectionKind.MissingSelections,
                new ResearchTargetPlanningLocation.Operation(),
                "A planning request must contain at least one selection occurrence.");
        }

        HashSet<ResearchComparisonQuestionId> questions = new(
            population.Questions.Select(static question => question.Id),
            ReferenceEqualityComparer.Instance);
        HashSet<ResearchMemberSelectionOccurrence> seen = new(
            ReferenceEqualityComparer.Instance);

        for (int index = 0; index < request.Selections.Length; index++)
        {
            ResearchTargetPlanningLocation location =
                new ResearchTargetPlanningLocation.Selection(index);
            ResearchMemberSelectionOccurrence? selection =
                request.Selections[index];
            if (selection is null)
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.MissingSelection,
                    location,
                    "A selection occurrence must not be null.");
            }

            if (!questions.Contains(selection.Question))
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.ForeignQuestion,
                    location,
                    "A selection must name a question this population admitted.");
            }

            if (!seen.Add(selection))
            {
                return Reject(
                    ResearchTargetPlanningRejectionKind.DuplicateSelection,
                    location,
                    "The same selection-occurrence instance was requested more than once.");
            }

            if (selection is ResearchExactAddressMemberSelection exact)
            {
                if (!Enum.IsDefined(exact.AssertedRole))
                {
                    return Reject(
                        ResearchTargetPlanningRejectionKind
                            .UndeclaredRelationshipRole,
                        location,
                        "An exact-address selection must assert a declared relationship role.");
                }

                if (!admitted.Contains(exact.Input)
                    || !ReferenceEquals(exact.Input.Question, exact.Question))
                {
                    return Reject(
                        ResearchTargetPlanningRejectionKind.ForeignInput,
                        location,
                        "An exact-address selection must designate an input its question admitted.");
                }
            }
        }

        return null;

        static ResearchTargetPlanningRejection Reject(
            ResearchTargetPlanningRejectionKind kind,
            ResearchTargetPlanningLocation location,
            string summary)
            => new(kind, location, summary);
    }

    // -------------------------------------------------------------------- plan

    static IReadOnlyList<PlannedScope> Plan(
        ResearchTargetPlanningRequest request,
        CancellationToken cancellationToken)
    {
        ResearchAdmittedPopulation population = request.Population;
        Dictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles =
            RoleMap(request);
        Dictionary<ResearchComparisonQuestionId, ResearchAdmittedQuestion>
            byQuestion = new(ReferenceEqualityComparer.Instance);
        foreach (ResearchAdmittedQuestion admitted in population.Questions)
            byQuestion.Add(admitted.Id, admitted);

        List<PlannedScope> scopes = [];
        foreach (ResearchMemberSelectionOccurrence? nullable in request.Selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResearchMemberSelectionOccurrence selection = nullable!;
            ResearchAdmittedQuestion question = byQuestion[selection.Question];
            ResearchTargetScopeId scopeId = new(selection.Question);
            ResearchAdmittedInput? designated =
                (selection as ResearchExactAddressMemberSelection)?.Input;

            List<PlannedDomain> domains = [];
            foreach (DomainCandidates candidates in
                GroupByDomain(question))
            {
                PlannedDomain domain = new(
                    new ResearchTargetDomainId(scopeId),
                    candidates.Key,
                    [.. candidates.Conflicting]);
                foreach (ResearchAdmittedInput input in candidates.Inputs)
                {
                    ResearchTargetInputRole role = roles[input];
                    if (designated is not null
                        && !ReferenceEquals(designated, input))
                    {
                        domain.Dispositions.Add(
                            new ResearchTargetInputDisposition(
                                input.Id,
                                role,
                                ResearchTargetDispositionKind.NotRequested,
                                ResearchTargetNotRequestedReason
                                    .ExactAddressDesignatesAnotherInput,
                                request: null));
                        continue;
                    }

                    ResearchExactAddressMemberSelection? exact =
                        selection as ResearchExactAddressMemberSelection;
                    ResearchTargetRequest targetRequest = new(
                        new ResearchTargetRequestId(domain.Id, input.Id),
                        selection.DeclaringTypeFullName,
                        selection.Selector,
                        selection.Kind,
                        exact?.Address,
                        exact?.AssertedRole);
                    domain.Requests.Add(
                        new PlannedRequest(
                            targetRequest,
                            input,
                            role,
                            domain.ConflictingInputs.Length != 0,
                            exact));
                    domain.Dispositions.Add(
                        new ResearchTargetInputDisposition(
                            input.Id,
                            role,
                            ResearchTargetDispositionKind.Requested,
                            notRequestedReason: null,
                            targetRequest.Id));
                }

                domains.Add(domain);
            }

            scopes.Add(new PlannedScope(scopeId, selection, domains));
        }

        return scopes;
    }

    static Dictionary<ResearchAdmittedInput, ResearchTargetInputRole> RoleMap(
        ResearchTargetPlanningRequest request)
    {
        Dictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles = new(
            ReferenceEqualityComparer.Instance);
        foreach (ResearchTargetInputRoleAssignment? assignment in
            request.InputRoles)
        {
            roles.Add(assignment!.Input, assignment.Role);
        }

        return roles;
    }

    /// <summary>
    /// Groups one question's admitted inputs into version-erased domains,
    /// preserving admitted order, and records the complete conflicting input-ID
    /// set for any domain that holds more than one input on one side.
    /// </summary>
    internal static IReadOnlyList<DomainCandidates> GroupByDomain(
        ResearchAdmittedQuestion question)
    {
        Dictionary<ResearchTargetDomainKey, List<ResearchAdmittedInput>> groups =
            [];
        List<ResearchTargetDomainKey> order = [];
        foreach (ResearchAdmittedInput input in question.Inputs)
        {
            ResearchTargetDomainKey key = DomainKey(input);
            if (!groups.TryGetValue(key, out List<ResearchAdmittedInput>? members))
            {
                members = [];
                groups.Add(key, members);
                order.Add(key);
            }

            members.Add(input);
        }

        List<DomainCandidates> candidates = new(order.Count);
        foreach (ResearchTargetDomainKey key in order)
        {
            List<ResearchAdmittedInput> members = groups[key];
            List<ResearchComparisonInputId> conflicting = [];
            foreach (ResearchComparisonSide side in Sides)
            {
                List<ResearchAdmittedInput> sideMembers =
                    [.. members.Where(input => input.Side == side)];
                if (sideMembers.Count > 1)
                    conflicting.AddRange(sideMembers.Select(input => input.Id));
            }

            candidates.Add(new DomainCandidates(key, members, conflicting));
        }

        return candidates;
    }

    static ResearchTargetDomainKey DomainKey(ResearchAdmittedInput input)
        => ResearchTargetDomainKey.From(
            ((ImplementationComparisonInputOccurrence)input.Occurrence)
                .Assembly.Identity);

    // ----------------------------------------------------------------- resolve

    static void ResolveAttempts(
        IReadOnlyList<PlannedScope> plan,
        CancellationToken cancellationToken)
    {
        List<PlannedRequest> all =
        [
            .. plan.SelectMany(static scope => scope.Domains)
                .SelectMany(static domain => domain.Requests),
        ];

        // Blocked and reference-only requests terminate without opening the
        // borrowed input at all.
        List<PlannedRequest> open = [];
        foreach (PlannedRequest planned in all)
        {
            if (planned.DomainAmbiguous)
            {
                planned.Outcome = Unavailable(
                    ResearchTargetDiagnosticKind.DomainAmbiguous);
            }
            else if (planned.Role == ResearchTargetInputRole.ReferenceOnly)
            {
                planned.Outcome = Unavailable(
                    ResearchTargetDiagnosticKind.ReferenceOnlyInput);
            }
            else
            {
                open.Add(planned);
            }
        }

        // Stage each admitted input once for every request that needs it.
        foreach (IGrouping<ResearchAdmittedInput, PlannedRequest> staged in
            open.GroupBy(
                static planned => planned.Input,
                (IEqualityComparer<ResearchAdmittedInput>)
                    ReferenceEqualityComparer.Instance))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveStagedInput(
                staged.Key,
                [.. staged],
                cancellationToken);
        }
    }

    static void ResolveStagedInput(
        ResearchAdmittedInput input,
        IReadOnlyList<PlannedRequest> requests,
        CancellationToken cancellationToken)
    {
        var occurrence = (ImplementationComparisonInputOccurrence)input.Occurrence;
        MetadataSource? source;
        try
        {
            source = MetadataSource.OpenWithoutSymbols(
                occurrence.Assembly,
                occurrence.Resolver);
        }
        catch (Exception exception) when (IsExpectedInputFailure(exception))
        {
            SetInputEvidence(
                requests,
                ResearchTargetInputValidationEvidence.Unreadable);
            Terminate(
                requests,
                ResearchTargetDiagnosticKind.InputUnreadable);
            return;
        }

        using (source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataReader reader;
            ApiSurface surface;
            try
            {
                reader = source.Reader;
                ResearchTargetInputValidationEvidence evidence =
                    CaptureInputEvidence(reader, occurrence);
                SetInputEvidence(requests, evidence);
                if (ValidateImage(evidence, occurrence) is
                    ResearchTargetDiagnosticKind invalid)
                {
                    Terminate(requests, invalid);
                    return;
                }

                surface = ApiSurfaceExtractor.Extract(
                    source.Pe,
                    includeAll: true,
                    typesOnly: false,
                    includeCompilerGenerated: true);
                SetInputEvidence(
                    requests,
                    evidence with { Surface = surface });
            }
            catch (Exception exception) when (
                IsExpectedMetadataReadFailure(exception))
            {
                SetInputEvidence(
                    requests,
                    ResearchTargetInputValidationEvidence.Unreadable);
                Terminate(
                    requests,
                    ResearchTargetDiagnosticKind.InputUnreadable);
                return;
            }

            foreach (PlannedRequest planned in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    planned.Outcome = ResolveRequest(
                        planned,
                        surface,
                        reader,
                        occurrence.BodyIndex.ModuleIdentity);
                }
                catch (Exception exception) when (
                    IsExpectedTargetResolutionFailure(exception))
                {
                    planned.TargetResolutionFailed = true;
                    planned.Outcome = Failed(
                        ResearchTargetDiagnosticKind.ResolutionFailed);
                }
            }
        }
    }

    /// <summary>
    /// Validates that the live image, the acquisition descriptor, and the
    /// Analysis body index all name the same assembly and the same module.
    /// </summary>
    static ResearchTargetInputValidationEvidence CaptureInputEvidence(
        MetadataReader reader,
        ImplementationComparisonInputOccurrence occurrence)
    {
        bool isAssembly = reader.IsAssembly;
        AssemblyReferenceIdentity? identity = isAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
            : null;
        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return new ResearchTargetInputValidationEvidence(
            ReadFailed: false,
            isAssembly,
            identity,
            moduleVersionId,
            occurrence.Assembly.Registration.ModuleVersionId,
            reader.MethodDefinitions.Count,
            Surface: null);
    }

    static ResearchTargetDiagnosticKind? ValidateImage(
        ResearchTargetInputValidationEvidence evidence,
        ImplementationComparisonInputOccurrence occurrence)
    {
        LibraryBodyModuleIdentity analysis = occurrence.BodyIndex.ModuleIdentity;
        if (!evidence.IsAssembly)
            return ResearchTargetDiagnosticKind.StandaloneModule;
        if (analysis.AssemblyIdentity is null)
            return ResearchTargetDiagnosticKind.AssemblyIdentityMismatch;

        AssemblyReferenceIdentity live = evidence.LiveAssemblyIdentity!;
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                occurrence.Assembly.Identity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                analysis.AssemblyIdentity))
        {
            return ResearchTargetDiagnosticKind.AssemblyIdentityMismatch;
        }

        return evidence.LiveModuleVersionId == analysis.ModuleVersionId
                && (evidence.ArtifactModuleVersionId is not Guid artifact
                    || artifact == evidence.LiveModuleVersionId)
            ? null
            : ResearchTargetDiagnosticKind.ModuleIdentityMismatch;
    }

    static ResearchTargetOutcome ResolveRequest(
        PlannedRequest planned,
        ApiSurface surface,
        MetadataReader reader,
        LibraryBodyModuleIdentity module)
    {
        string intent = planned.Request.DeclaringTypeFullName;
        List<ApiType> declaringTypes =
        [
            .. surface.Types.Where(
                candidate => string.Equals(
                    MetadataFullName(candidate),
                    intent,
                    StringComparison.Ordinal)),
        ];
        int forwarders = surface.TypeForwarders.Count(
            forwarder => string.Equals(
                MetadataFullName(forwarder),
                intent,
                StringComparison.Ordinal));
        int failedTypeDefinitions =
            CountFailedTypeDefinitions(surface, intent);
        bool nestedUnderForwarder = surface.TypeForwarders.Any(
            forwarder => IsNestedUnder(
                intent,
                MetadataFullName(forwarder)));
        int exactDeclarations =
            declaringTypes.Count + forwarders + failedTypeDefinitions;
        if (exactDeclarations > 1
            || (declaringTypes.Count + failedTypeDefinitions != 0
                && nestedUnderForwarder))
        {
            return Failed(
                ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous);
        }

        if (declaringTypes.Count == 0)
        {
            if (forwarders == 1 || nestedUnderForwarder)
            {
                return Unavailable(
                    ResearchTargetDiagnosticKind.DeclaringTypeForwarded);
            }

            if (FindPotentiallyCoveringFailure(
                    surface,
                    intent,
                    memberAbsence: false) is not null)
            {
                return Failed(
                    ResearchTargetDiagnosticKind.IncompleteMetadataSurface);
            }

            return new ResearchTargetOutcome.NotFound(
                metadataDiagnostic: null,
                new ResearchTargetDiagnostic(
                    ResearchTargetDiagnosticKind.DeclaringTypeAbsent),
                candidates: []);
        }

        ApiType declaring = declaringTypes[0];
        planned.DeclaringType = declaring;
        MemberTargetResolution resolution = MemberTargetResolver.Resolve(
            declaring,
            planned.Request.Selector,
            kindFilter: null);
        planned.MetadataResolution = resolution;
        ImmutableArray<MemberTargetCandidate> candidates =
            [.. resolution.Candidates];

        if (resolution.Diagnostic is { } diagnostic)
        {
            if (MapDiagnosticKind(diagnostic.Kind)
                    == ResearchTargetOutcomeKind.NotFound
                && FindPotentiallyCoveringFailure(
                    surface,
                    intent,
                    memberAbsence: true) is not null)
            {
                return Failed(
                    ResearchTargetDiagnosticKind.IncompleteMetadataSurface);
            }

            return MapDiagnosticKind(diagnostic.Kind) switch
            {
                ResearchTargetOutcomeKind.NotFound =>
                    new ResearchTargetOutcome.NotFound(
                        diagnostic,
                        researchDiagnostic: null,
                        candidates),
                ResearchTargetOutcomeKind.Ambiguous =>
                    new ResearchTargetOutcome.Ambiguous(diagnostic, candidates),
                ResearchTargetOutcomeKind.Rejected =>
                    new ResearchTargetOutcome.Rejected(diagnostic, candidates),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(diagnostic),
                    diagnostic.Kind,
                    "Unmapped Metadata target diagnostic kind."),
            };
        }

        if (resolution.Target is not { } target)
        {
            return Failed(ResearchTargetDiagnosticKind.ResolutionFailed);
        }

        int? bodyToken = target.Body?.MetadataToken;
        ResearchTargetRelationshipRole? role = DeriveRole(
            target.ApiMember.Member,
            bodyToken);
        if (role is null)
        {
            return Failed(
                ResearchTargetDiagnosticKind.RelationshipRoleEvidenceMismatch);
        }

        MetadataMethodAddress? address = null;
        if (role != ResearchTargetRelationshipRole.None)
        {
            if (TryCreateAddress(reader, bodyToken!.Value) is not { } created)
            {
                return Failed(
                    ResearchTargetDiagnosticKind.InvalidMethodDefinitionToken);
            }

            address = created;
        }

        if (planned.Exact is { } exact)
        {
            if (address != exact.Address)
                return Failed(ResearchTargetDiagnosticKind.AddressEvidenceMismatch);
            if (role != exact.AssertedRole)
            {
                return Failed(
                    ResearchTargetDiagnosticKind.RelationshipRoleEvidenceMismatch);
            }
        }

        return new ResearchTargetOutcome.Resolved(
            target,
            address,
            role.Value,
            module,
            candidates);
    }

    static ApiSurfaceInspectionFailure? FindPotentiallyCoveringFailure(
        ApiSurface surface,
        string declaringTypeFullName,
        bool memberAbsence)
        => surface.InspectionFailures.FirstOrDefault(
            failure =>
                failure.Operation
                    != ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                && failure.Operation
                    != ApiSurfaceInspectionFailure
                        .EnumAttributeTypeIndexOperation
                && ((memberAbsence
                        && failure.OwningTypeDefinition is not null)
                    || MayAffectType(failure, declaringTypeFullName)));

    static int CountFailedTypeDefinitions(
        ApiSurface surface,
        string declaringTypeFullName)
        => surface.InspectionFailures.Count(
            failure =>
                failure.OwningTypeDefinition is { } owner
                && string.Equals(
                    owner.ToMetadataFullName(),
                    declaringTypeFullName,
                    StringComparison.Ordinal));

    static bool MayAffectType(
        ApiSurfaceInspectionFailure failure,
        string declaringTypeFullName)
    {
        if (failure.OwningTypeDefinition is { } owner)
        {
            return string.Equals(
                owner.ToMetadataFullName(),
                declaringTypeFullName,
                StringComparison.Ordinal);
        }

        if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
        {
            return failure.AffectedTypeDefinitions.Any(
                affected => string.Equals(
                    affected.ToMetadataFullName(),
                    declaringTypeFullName,
                    StringComparison.Ordinal));
        }

        return true;
    }

    /// <summary>
    /// The single terminal arm each Metadata target diagnostic maps onto. The
    /// mapping is exhaustive over <see cref="MemberTargetDiagnosticKind"/>: an
    /// unmapped member throws rather than silently degrading to a failure.
    /// <c>ResearchTargetAttempts_MapEveryMetadataDiagnosticKind</c> derives its
    /// expected set from that declaration and gates this mapping.
    /// </summary>
    internal static ResearchTargetOutcomeKind MapDiagnosticKind(
        MemberTargetDiagnosticKind kind)
        => kind switch
        {
            MemberTargetDiagnosticKind.MissingMember
                or MemberTargetDiagnosticKind.DigestNotFound =>
                ResearchTargetOutcomeKind.NotFound,
            MemberTargetDiagnosticKind.AmbiguousMember
                or MemberTargetDiagnosticKind.DigestAmbiguous =>
                ResearchTargetOutcomeKind.Ambiguous,
            MemberTargetDiagnosticKind.ConflictingSelectors
                or MemberTargetDiagnosticKind.OverloadOutOfRange =>
                ResearchTargetOutcomeKind.Rejected,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unmapped Metadata target diagnostic kind."),
        };

    /// <summary>
    /// The relationship role of one selected member, derived only from its
    /// MethodDef tokens and the resolved body token. Returns
    /// <see langword="null"/> when the body token names no accessor or physical
    /// member of the selected member, which is a Research validation failure
    /// rather than a role.
    /// </summary>
    static ResearchTargetRelationshipRole? DeriveRole(
        ApiMember member,
        int? bodyToken)
    {
        if (bodyToken is not { } token)
        {
            return member.MetadataToken is null
                && member.GetterToken is null
                && member.SetterToken is null
                && member.AdderToken is null
                && member.RemoverToken is null
                    ? ResearchTargetRelationshipRole.None
                    : null;
        }

        ResearchTargetRelationshipRole? role = null;
        int matches = 0;
        Match(member.MetadataToken, ResearchTargetRelationshipRole.Method);
        Match(member.GetterToken, ResearchTargetRelationshipRole.Getter);
        Match(member.SetterToken, ResearchTargetRelationshipRole.Setter);
        Match(member.AdderToken, ResearchTargetRelationshipRole.Adder);
        Match(member.RemoverToken, ResearchTargetRelationshipRole.Remover);
        return matches == 1 ? role : null;

        void Match(
            int? candidate,
            ResearchTargetRelationshipRole candidateRole)
        {
            if (candidate != token)
                return;

            matches++;
            role = candidateRole;
        }
    }

    /// <summary>
    /// Creates a durable address only when the token is a MethodDef whose row
    /// exists in this module's MethodDef table. A token from another table is
    /// never masked into a MethodDef handle.
    /// </summary>
    internal static MetadataMethodAddress? TryCreateAddress(
        MetadataReader reader,
        int token)
    {
        EntityHandle entity;
        try
        {
            entity = MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (entity.IsNil || entity.Kind != HandleKind.MethodDefinition)
            return null;

        int row = MetadataTokens.GetRowNumber(entity);
        if (row < 1 || row > reader.MethodDefinitions.Count)
            return null;

        return MetadataMethodAddress.Create(
            reader,
            (MethodDefinitionHandle)entity);
    }

    static string MetadataFullName(ApiType type)
        => type.DefinitionName?.ToMetadataFullName() ?? type.FullName;

    static string MetadataFullName(TypeForwarder forwarder)
        => forwarder.DefinitionName?.ToMetadataFullName() ?? forwarder.TypeName;

    static bool IsNestedUnder(string candidate, string potentialRoot)
        => candidate.Length > potentialRoot.Length
            && candidate.StartsWith(potentialRoot, StringComparison.Ordinal)
            && candidate[potentialRoot.Length] == '.';

    static void Terminate(
        IReadOnlyList<PlannedRequest> requests,
        ResearchTargetDiagnosticKind kind)
    {
        foreach (PlannedRequest planned in requests)
            planned.Outcome = Failed(kind);
    }

    static void SetInputEvidence(
        IReadOnlyList<PlannedRequest> requests,
        ResearchTargetInputValidationEvidence evidence)
    {
        foreach (PlannedRequest planned in requests)
            planned.InputEvidence = evidence;
    }

    static ResearchTargetOutcome Unavailable(ResearchTargetDiagnosticKind kind)
        => new ResearchTargetOutcome.Unavailable(
            new ResearchTargetDiagnostic(kind));

    static ResearchTargetOutcome Failed(ResearchTargetDiagnosticKind kind)
        => new ResearchTargetOutcome.Failed(new ResearchTargetDiagnostic(kind));

    /// <summary>
    static bool IsExpectedInputFailure(Exception exception)
        => exception is BadImageFormatException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ObjectDisposedException
            or ArgumentException;

    static bool IsExpectedMetadataReadFailure(Exception exception)
        => exception is BadImageFormatException
            or IOException
            or FormatException
            or OverflowException
            or ArgumentException;

    static bool IsExpectedTargetResolutionFailure(Exception exception)
        => exception is BadImageFormatException
            or FormatException
            or OverflowException;

    // ------------------------------------------------------------- materialize

    static ResearchTargetResolution Materialize(
        ResearchComparisonOperationId operation,
        IReadOnlyList<PlannedScope> plan)
    {
        var scopes = ImmutableArray.CreateBuilder<ResearchTargetScope>(plan.Count);
        foreach (PlannedScope scope in plan)
        {
            var domains =
                ImmutableArray.CreateBuilder<ResearchTargetDomain>(
                    scope.Domains.Count);
            foreach (PlannedDomain domain in scope.Domains)
            {
                var attempts =
                    ImmutableArray.CreateBuilder<ResearchTargetAttempt>(
                        domain.Requests.Count);
                var requests =
                    ImmutableArray.CreateBuilder<ResearchTargetRequest>(
                        domain.Requests.Count);
                foreach (PlannedRequest planned in domain.Requests)
                {
                    requests.Add(planned.Request);
                    attempts.Add(
                        new ResearchTargetAttempt(
                            new ResearchTargetAttemptId(planned.Request.Id),
                            planned.Request,
                            planned.Outcome
                                ?? throw new InvalidOperationException(
                                    "Every planned request must reach a terminal outcome.")));
                }

                domains.Add(
                    new ResearchTargetDomain(
                        domain.Id,
                        domain.Key,
                        [.. domain.Dispositions],
                        domain.ConflictingInputs,
                        requests.MoveToImmutable(),
                        attempts.MoveToImmutable()));
            }

            scopes.Add(
                new ResearchTargetScope(
                    scope.Id,
                    scope.Selection.DeclaringTypeFullName,
                    scope.Selection.Selector,
                    scope.Selection.Kind,
                    domains.MoveToImmutable()));
        }

        return new ResearchTargetResolution(operation, scopes.MoveToImmutable());
    }

    static ImmutableArray<ResearchTargetValidationEvidence> ValidationEvidence(
        IReadOnlyList<PlannedScope> plan)
        =>
        [
            .. plan.SelectMany(static scope => scope.Domains)
                .SelectMany(static domain => domain.Requests)
                .Select(
                    static planned => new ResearchTargetValidationEvidence(
                        planned.Request,
                        planned.Input,
                        planned.Role,
                        planned.InputEvidence,
                        planned.DeclaringType,
                        planned.MetadataResolution,
                        planned.TargetResolutionFailed,
                        planned.Outcome
                            ?? throw new InvalidOperationException(
                                "Every planned request must reach a terminal outcome."))),
        ];

    static ReadOnlySpan<ResearchComparisonSide> Sides =>
        [ResearchComparisonSide.Before, ResearchComparisonSide.After];

    internal sealed record DomainCandidates(
        ResearchTargetDomainKey Key,
        IReadOnlyList<ResearchAdmittedInput> Inputs,
        IReadOnlyList<ResearchComparisonInputId> Conflicting);

    sealed class PlannedScope(
        ResearchTargetScopeId id,
        ResearchMemberSelectionOccurrence selection,
        IReadOnlyList<PlannedDomain> domains)
    {
        public ResearchTargetScopeId Id { get; } = id;

        public ResearchMemberSelectionOccurrence Selection { get; } = selection;

        public IReadOnlyList<PlannedDomain> Domains { get; } = domains;
    }

    sealed class PlannedDomain(
        ResearchTargetDomainId id,
        ResearchTargetDomainKey key,
        ImmutableArray<ResearchComparisonInputId> conflictingInputs)
    {
        public ResearchTargetDomainId Id { get; } = id;

        public ResearchTargetDomainKey Key { get; } = key;

        public ImmutableArray<ResearchComparisonInputId> ConflictingInputs { get; }
            = conflictingInputs;

        public List<ResearchTargetInputDisposition> Dispositions { get; } = [];

        public List<PlannedRequest> Requests { get; } = [];
    }

    sealed class PlannedRequest(
        ResearchTargetRequest request,
        ResearchAdmittedInput input,
        ResearchTargetInputRole role,
        bool domainAmbiguous,
        ResearchExactAddressMemberSelection? exact)
    {
        public ResearchTargetRequest Request { get; } = request;

        public ResearchAdmittedInput Input { get; } = input;

        public ResearchTargetInputRole Role { get; } = role;

        public bool DomainAmbiguous { get; } = domainAmbiguous;

        public ResearchExactAddressMemberSelection? Exact { get; } = exact;

        public ResearchTargetInputValidationEvidence? InputEvidence { get; set; }

        public ApiType? DeclaringType { get; set; }

        public MemberTargetResolution? MetadataResolution { get; set; }

        public bool TargetResolutionFailed { get; set; }

        public ResearchTargetOutcome? Outcome { get; set; }
    }
}

/// <summary>
/// Short-lived borrowed evidence used only by the final construction
/// validator. It is discarded before the inert resolution is returned.
/// </summary>
internal sealed record ResearchTargetValidationEvidence(
    ResearchTargetRequest Request,
    ResearchAdmittedInput Input,
    ResearchTargetInputRole Role,
    ResearchTargetInputValidationEvidence? InputEvidence,
    ApiType? DeclaringType,
    MemberTargetResolution? MetadataResolution,
    bool TargetResolutionFailed,
    ResearchTargetOutcome Outcome);

internal sealed record ResearchTargetInputValidationEvidence(
    bool ReadFailed,
    bool IsAssembly,
    AssemblyReferenceIdentity? LiveAssemblyIdentity,
    Guid LiveModuleVersionId,
    Guid? ArtifactModuleVersionId,
    int MethodDefinitionCount,
    ApiSurface? Surface)
{
    internal static ResearchTargetInputValidationEvidence Unreadable { get; } =
        new(
            ReadFailed: true,
            IsAssembly: false,
            LiveAssemblyIdentity: null,
            LiveModuleVersionId: Guid.Empty,
            ArtifactModuleVersionId: null,
            MethodDefinitionCount: 0,
            Surface: null);
}
