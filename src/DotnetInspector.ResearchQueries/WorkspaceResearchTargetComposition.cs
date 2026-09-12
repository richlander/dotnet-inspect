using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// One side-local composition over an existing sealed admission and complete
/// Research resolution. Only Queries orchestration holding the original
/// population receipt can construct this request.
/// </summary>
internal sealed class WorkspaceResearchTargetCompositionRequest
{
    internal WorkspaceResearchTargetCompositionRequest(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        AssemblyBindingPolicyVersion capturedVersion,
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        ProjectedQueryPopulation projected,
        ResearchTargetResolution resolution,
        QueryComparisonQuestionId question,
        QueryComparisonSide side,
        ResearchTargetScope scope,
        MetadataTypeDefinitionName declaringType,
        ResearchTargetDomain domain,
        ResearchTargetDomainSideCensus census,
        AssemblyResolutionScope resolutionScope)
        : this(
            group, root, capturedVersion, population, projected, resolution,
            question, side, scope, declaringType, resolutionScope,
            domain, census)
    {
    }

    internal WorkspaceResearchTargetCompositionRequest(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        AssemblyBindingPolicyVersion capturedVersion,
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        ProjectedQueryPopulation projected,
        ResearchTargetResolution resolution,
        QueryComparisonQuestionId question,
        QueryComparisonSide side,
        ResearchTargetScope scope,
        MetadataTypeDefinitionName declaringType,
        AssemblyResolutionScope resolutionScope)
        : this(
            group, root, capturedVersion, population, projected, resolution,
            question, side, scope, declaringType, resolutionScope,
            domain: null, census: null)
    {
    }

    WorkspaceResearchTargetCompositionRequest(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        AssemblyBindingPolicyVersion capturedVersion,
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        ProjectedQueryPopulation projected,
        ResearchTargetResolution resolution,
        QueryComparisonQuestionId question,
        QueryComparisonSide side,
        ResearchTargetScope scope,
        MetadataTypeDefinitionName declaringType,
        AssemblyResolutionScope resolutionScope,
        ResearchTargetDomain? domain,
        ResearchTargetDomainSideCensus? census)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(capturedVersion);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(projected);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(declaringType);
        if ((domain is null) != (census is null))
            throw new ArgumentException(
                "A terminal domain and census must be supplied together.");
        Group = group;
        Root = root;
        CapturedVersion = capturedVersion;
        Population = population;
        Projected = projected;
        Resolution = resolution;
        Question = question;
        Side = side;
        Scope = scope;
        DeclaringType = declaringType;
        Domain = domain;
        Census = census;
        ResolutionScope = resolutionScope;
    }

    public AssemblyContextGroup Group { get; }
    public AssemblyContextParticipant Root { get; }
    public AssemblyBindingPolicyVersion CapturedVersion { get; }
    public QueryComparisonPopulation<ImplementationComparisonBinding> Population { get; }
    internal ProjectedQueryPopulation Projected { get; }
    public ResearchTargetResolution Resolution { get; }
    public QueryComparisonQuestionId Question { get; }
    public QueryComparisonSide Side { get; }
    public ResearchTargetScope Scope { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public ResearchTargetDomain? Domain { get; }
    public ResearchTargetDomainSideCensus? Census { get; }
    public AssemblyResolutionScope ResolutionScope { get; }
}

public enum WorkspaceResearchTargetCompositionRejection
{
    ForeignRoot,
    PopulationMismatch,
    PopulationReceiptMismatch,
    ResearchResolutionMismatch,
    UnsupportedRequestKind,
    UnsupportedResolutionScope,
    TerminalParticipantMismatch,
    TerminalInputMismatch,
    TerminalAttemptMismatch,
    TerminalEvidenceMismatch,
    RootAttemptMismatch,
    UnsupportedBindingPolicy,
}

public enum WorkspaceResearchTargetCompositionUnavailability
{
    ParticipantImageUnavailable,
    MetadataResolutionUnavailable,
    BlockedTerminalCensus,
    TerminalAttemptUnavailable,
}

/// <summary>Successful association evidence, with neither workspace nor image authority.</summary>
public sealed class WorkspaceResearchTargetCompositionReceipt
{
    internal WorkspaceResearchTargetCompositionReceipt(
        WorkspaceProjectionOperationId projectionOperation,
        WorkspaceGroupOccurrenceId group,
        WorkspaceBindingPolicyVersionId bindingVersion,
        QueryComparisonQuestionId question,
        QueryComparisonSide side,
        AssemblyResolutionScope resolutionScope,
        QueryComparisonInputId rootInput,
        QueryComparisonInputId terminalInput,
        WorkspaceResearchTargetAttemptEvidence rootAttempt,
        WorkspaceResearchTargetAttemptEvidence.Resolved effectiveAttempt,
        WorkspaceResearchTargetCensusEvidence census,
        WorkspaceTypeResolutionEvidence.Available evidence)
    {
        ProjectionOperation = projectionOperation;
        Group = group;
        BindingVersion = bindingVersion;
        Question = question;
        Side = side;
        ResolutionScope = resolutionScope;
        RootInput = rootInput;
        TerminalInput = terminalInput;
        RootAttempt = rootAttempt;
        EffectiveAttempt = effectiveAttempt;
        Census = census;
        Evidence = evidence;
    }

    public WorkspaceProjectionOperationId ProjectionOperation { get; }
    public WorkspaceGroupOccurrenceId Group { get; }
    public WorkspaceBindingPolicyVersionId BindingVersion { get; }
    public QueryComparisonOperationId Operation => Question.Operation;
    public QueryComparisonQuestionId Question { get; }
    public QueryComparisonSide Side { get; }
    public AssemblyResolutionScope ResolutionScope { get; }
    public QueryComparisonInputId RootInput { get; }
    public QueryComparisonInputId TerminalInput { get; }
    public ResearchTargetAttemptId RootAttemptId => RootAttempt.Id;
    public ResearchTargetAttemptId EffectiveAttemptId => EffectiveAttempt.Id;
    public WorkspaceResearchTargetAttemptEvidence RootAttempt { get; }
    public WorkspaceResearchTargetAttemptEvidence.Resolved EffectiveAttempt { get; }
    public ResearchTargetScopeId Scope => EffectiveAttempt.Request.Scope;
    public ResearchTargetDomainId Domain => EffectiveAttempt.Request.Domain;
    public WorkspaceResearchTargetCensusEvidence Census { get; }
    public WorkspaceTypeResolutionEvidence.Available Evidence { get; }
}

public abstract class WorkspaceResearchTargetCompositionResult
{
    private WorkspaceResearchTargetCompositionResult() { }

    public sealed class Composed : WorkspaceResearchTargetCompositionResult
    {
        internal Composed(WorkspaceResearchTargetCompositionReceipt receipt) => Receipt = receipt;
        public WorkspaceResearchTargetCompositionReceipt Receipt { get; }
    }

    public sealed class Unavailable : WorkspaceResearchTargetCompositionResult
    {
        internal Unavailable(
            WorkspaceResearchTargetCompositionUnavailability reason,
            WorkspaceTypeResolutionEvidence evidence,
            WorkspaceResearchTargetAttemptEvidence? rootAttempt,
            WorkspaceResearchTargetAttemptEvidence? terminalAttempt = null,
            WorkspaceResearchTargetCensusEvidence? census = null)
        {
            Reason = reason;
            Evidence = evidence;
            RootAttempt = rootAttempt;
            TerminalAttempt = terminalAttempt;
            Census = census;
        }
        public WorkspaceResearchTargetCompositionUnavailability Reason { get; }
        public WorkspaceTypeResolutionEvidence Evidence { get; }
        public WorkspaceResearchTargetAttemptEvidence? RootAttempt { get; }
        public WorkspaceResearchTargetAttemptEvidence? TerminalAttempt { get; }
        public WorkspaceResearchTargetCensusEvidence? Census { get; }
    }

    public sealed class Rejected : WorkspaceResearchTargetCompositionResult
    {
        internal Rejected(
            WorkspaceResearchTargetCompositionRejection reason,
            WorkspaceTypeResolutionEvidence? evidence = null,
            WorkspaceResearchTargetAttemptEvidence? rootAttempt = null,
            WorkspaceResearchTargetAttemptEvidence? terminalAttempt = null,
            WorkspaceResearchTargetCensusEvidence? census = null)
        {
            Reason = reason;
            Evidence = evidence;
            RootAttempt = rootAttempt;
            TerminalAttempt = terminalAttempt;
            Census = census;
        }
        public WorkspaceResearchTargetCompositionRejection Reason { get; }
        public WorkspaceTypeResolutionEvidence? Evidence { get; }
        public WorkspaceResearchTargetAttemptEvidence? RootAttempt { get; }
        public WorkspaceResearchTargetAttemptEvidence? TerminalAttempt { get; }
        public WorkspaceResearchTargetCensusEvidence? Census { get; }
    }
}

/// <summary>Joins a retained Metadata terminal definition to one existing Research attempt.</summary>
public static class WorkspaceResearchTargetCompositionQuery
{
    public static InspectionQuery<WorkspaceResearchTargetCompositionResult> Definition { get; } =
        new("Workspace Research target composition", InspectionCost.Unbounded);

    internal static WorkspaceResearchTargetCompositionResult Execute(
        WorkspaceResearchTargetCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<AssemblyContextParticipant> participants = request.Group.Participants;
        AssemblyAcquisitionRegistration rootRegistration = request.Root.Assembly.Registration;

        // Checks 1-5 do not invoke Metadata, Research, or a binding policy.
        AssemblyContextParticipant? root = participants.FirstOrDefault(
            participant => ReferenceEquals(participant.Assembly.Registration, rootRegistration));
        if (root is null)
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.ForeignRoot);
        if (!WorkspaceResearchTargetCompositionValidator.TryMapPopulation(
                request, participants, out var inputs))
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
        if (!WorkspaceResearchTargetCompositionValidator.ValidateReceipt(request))
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.PopulationReceiptMismatch);
        if (!WorkspaceResearchTargetCompositionValidator.ValidateResolution(request))
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        if (request.Scope.Kind != ResearchTargetRequestKind.Carried)
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.UnsupportedRequestKind);
        if (!Enum.IsDefined(request.ResolutionScope))
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.UnsupportedResolutionScope);

        AssemblyBindingPolicyVersion captured = request.CapturedVersion;
        ImmutableArray<IAssemblyBindingPolicy> policies =
            [.. participants.Select(participant => participant.BindingPolicy)];
        if (!ReferenceEquals(captured, request.Group.BindingPolicyVersion))
            throw new InvalidOperationException("Composition must use the group's captured binding-policy version.");
        EnsureVersions(policies, captured);
        AssemblyContextTypeResolutionResult resolution = AssemblyContextTypeResolutionQuery.Execute(
            request.Group, root, request.DeclaringType, request.ResolutionScope);
        EnsureVersions(policies, captured);

        var projection = new WorkspaceProjectionContext(inputs);
        WorkspaceResearchTargetCompositionResult pending =
            ComposePending(request, inputs, rootRegistration, resolution, projection);
        cancellationToken.ThrowIfCancellationRequested();
        return Publish(policies, captured, pending);
    }

    static WorkspaceResearchTargetCompositionResult ComposePending(
        WorkspaceResearchTargetCompositionRequest request,
        Dictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs,
        AssemblyAcquisitionRegistration rootRegistration,
        AssemblyContextTypeResolutionResult resolution,
        WorkspaceProjectionContext projection)
    {
        QueryComparisonInputId rootInput = inputs[rootRegistration];
        QueryToResearchPopulationReceipt populationReceipt = request.Projected.Receipt;
        ResearchTargetDomain? domain = request.Domain;
        ResearchTargetDomainSideCensus? census = request.Census;
        ResearchTargetAttempt? rootAttempt = WorkspaceResearchTargetCompositionValidator.UniqueAttempt(
            request.Scope, populationReceipt.Inputs[rootInput].Research);
        WorkspaceTypeResolutionEvidence projectedEvidence =
            WorkspaceTypeResolutionProjectionManifest.QueryResult.Project(projection, resolution);
        if (projectedEvidence is WorkspaceTypeResolutionEvidence.QueryRejected queryRejected)
        {
            return new WorkspaceResearchTargetCompositionResult.Unavailable(
                WorkspaceResearchTargetCompositionUnavailability.ParticipantImageUnavailable, queryRejected,
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt));
        }

        if (projectedEvidence is WorkspaceTypeResolutionEvidence.UnsupportedBindingPolicy unsupportedPolicy)
        {
            return new WorkspaceResearchTargetCompositionResult.Rejected(
                WorkspaceResearchTargetCompositionRejection.UnsupportedBindingPolicy, unsupportedPolicy,
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt));
        }

        if (resolution is not AssemblyContextTypeResolutionResult.Available available)
            throw new InvalidOperationException("Unknown assembly-context type-resolution result.");
        var evidence = (WorkspaceTypeResolutionEvidence.Available)projectedEvidence;
        if (available.Outcome is not TypeResolutionOutcome.Resolved resolved)
            return new WorkspaceResearchTargetCompositionResult.Unavailable(
                WorkspaceResearchTargetCompositionUnavailability.MetadataResolutionUnavailable, evidence,
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt));

        ResolvedTypeDefinition terminal = resolved.Definition;
        AssemblyAcquisitionRegistration registration = terminal.Assembly.Assembly.Registration;
        if (!inputs.TryGetValue(registration, out QueryComparisonInputId? terminalInput)
            || resolved.Hops.Any(hop =>
                !inputs.ContainsKey(hop.SourceAssembly.Assembly.Registration)
                || !ReferenceEquals(hop.SourceOccurrence.Assembly.Registration,
                    hop.SourceAssembly.Assembly.Registration)))
            return Reject(WorkspaceResearchTargetCompositionRejection.TerminalParticipantMismatch);
        if (!populationReceipt.Inputs.TryGetValue(terminalInput, out var mapped)
            || !request.Projected.Admission.TryGetInput(mapped.Research, out var admitted)
            || admitted.Occurrence is not ImplementationComparisonInputOccurrence occurrence
            || !ReferenceEquals(occurrence.Assembly.Registration, registration)
            || mapped.Side != request.Side)
            return Reject(WorkspaceResearchTargetCompositionRejection.TerminalInputMismatch);

        ResearchTargetAttempt? attempt =
            WorkspaceResearchTargetCompositionValidator.UniqueAttempt(request.Scope, mapped.Research);
        domain ??= request.Scope.Domains.SingleOrDefault(candidate =>
                candidate.Attempts.Any(item => ReferenceEquals(item, attempt)));
        if (census is null && domain is not null)
        {
            ResearchComparisonSide side =
                QueryPopulationProjection.ResearchSide(request.Side);
            census = request.Resolution.Censuses.SingleOrDefault(candidate =>
                ReferenceEquals(candidate.Domain, domain)
                && candidate.Side == side);
        }
        if (attempt is null
            || domain is null
            || census is null
            || !ReferenceEquals(attempt.Request.Domain, domain.Id)
            || !domain.Attempts.Any(item => ReferenceEquals(item, attempt))
            || !census.Attempts.Any(item => ReferenceEquals(item, attempt)))
            return Reject(WorkspaceResearchTargetCompositionRejection.TerminalAttemptMismatch, attempt);

        if (census.Health != ResearchTargetCensusHealth.Healthy)
            return Unavailable(WorkspaceResearchTargetCompositionUnavailability.BlockedTerminalCensus);
        if (attempt.Outcome is not ResearchTargetOutcome.Resolved target)
            return Unavailable(WorkspaceResearchTargetCompositionUnavailability.TerminalAttemptUnavailable);
        if (!WorkspaceResearchTargetCompositionValidator.ValidateTerminal(request, terminal, target, occurrence))
            return Reject(WorkspaceResearchTargetCompositionRejection.TerminalEvidenceMismatch, attempt);

        bool direct = resolved.Hops.IsEmpty;
        if (rootAttempt is null
            || direct != ReferenceEquals(rootRegistration, registration)
            || direct && (!ReferenceEquals(rootAttempt, attempt) || rootAttempt.Outcome is not ResearchTargetOutcome.Resolved)
            || !direct && rootAttempt.Outcome is not ResearchTargetOutcome.Unavailable
            {
                Diagnostic.Kind: ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            })
            return Reject(WorkspaceResearchTargetCompositionRejection.RootAttemptMismatch, attempt);

        var effectiveEvidence = (WorkspaceResearchTargetAttemptEvidence.Resolved)
            WorkspaceResearchTargetEvidenceProjection.Attempt(projection, attempt)!;
        WorkspaceResearchTargetAttemptEvidence rootEvidence = ReferenceEquals(rootAttempt, attempt)
            ? effectiveEvidence
            : WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt)!;
        var receipt = new WorkspaceResearchTargetCompositionReceipt(
            projection.Operation, new(projection.Operation), projection.Version(request.CapturedVersion),
            request.Question, request.Side, request.ResolutionScope, rootInput, terminalInput,
            rootEvidence, effectiveEvidence, WorkspaceResearchTargetEvidenceProjection.Census(census), evidence);
        return new WorkspaceResearchTargetCompositionResult.Composed(receipt);

        WorkspaceResearchTargetCompositionResult.Rejected Reject(
            WorkspaceResearchTargetCompositionRejection reason, ResearchTargetAttempt? terminalAttempt = null)
            => new(reason, evidence,
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt),
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, terminalAttempt),
                census is null
                    ? null
                    : WorkspaceResearchTargetEvidenceProjection.Census(census));
        WorkspaceResearchTargetCompositionResult.Unavailable Unavailable(
            WorkspaceResearchTargetCompositionUnavailability reason)
            => new(reason, evidence,
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, rootAttempt),
                WorkspaceResearchTargetEvidenceProjection.Attempt(projection, attempt),
                WorkspaceResearchTargetEvidenceProjection.Census(census));
    }

    static void EnsureVersions(ImmutableArray<IAssemblyBindingPolicy> policies, AssemblyBindingPolicyVersion captured)
    {
        foreach (IAssemblyBindingPolicy policy in policies)
            if (!ReferenceEquals(policy.Version, captured))
                throw new InvalidOperationException("A participant binding-policy snapshot changed during composition.");
    }

    // The pending value is entirely inert. From this point on, the only owner
    // reads are one Version per participant; there are no callbacks or cleanup
    // actions after the sweep, and every post-query arm uses this same tail.
    internal static WorkspaceResearchTargetCompositionResult Publish(
        ImmutableArray<IAssemblyBindingPolicy> policies,
        AssemblyBindingPolicyVersion captured,
        WorkspaceResearchTargetCompositionResult pending)
    {
        bool unchanged = true;
        foreach (IAssemblyBindingPolicy policy in policies)
            if (!ReferenceEquals(policy.Version, captured))
                unchanged = false;
        if (!unchanged)
            throw new InvalidOperationException("A participant binding-policy snapshot changed before composition publication.");
        return pending;
    }
}

internal static class WorkspaceResearchTargetCompositionValidator
{
    internal static bool TryMapPopulation(
        WorkspaceResearchTargetCompositionRequest request,
        ImmutableArray<AssemblyContextParticipant> participants,
        out Dictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs)
    {
        inputs = new(ReferenceEqualityComparer.Instance);
        if (!Enum.IsDefined(request.Side))
            return false;
        var members = new HashSet<AssemblyAcquisitionRegistration>(ReferenceEqualityComparer.Instance);
        foreach (var participant in participants)
            if (!members.Add(participant.Assembly.Registration))
                return false;
        var side = request.Side == QueryComparisonSide.Before ? request.Population.Before : request.Population.After;
        if (side.Length != members.Count)
            return false;
        foreach (var input in side)
        {
            AssemblyAcquisitionRegistration registration = input.Binding.Assembly.Registration;
            if (!members.Contains(registration)
                || !inputs.TryAdd(registration, input.Id)
                || input.Id.Side != request.Side)
                return false;
        }
        return inputs.Count == members.Count;
    }

    internal static bool ValidateReceipt(WorkspaceResearchTargetCompositionRequest request)
    {
        var population = request.Population;
        var receipt = request.Projected.Receipt;
        var admission = request.Projected.Admission;
        if (!ReferenceEquals(request.Question, population.Question)
            || receipt.Profile != population.Profile
            || admission.Profile != QueryPopulationProjection.ResearchProfile(population.Profile)
            || !ReferenceEquals(receipt.Operation.Query, population.Operation)
            || !ReferenceEquals(receipt.Operation.Research, admission.Operation)
            || receipt.Questions.Count != 1 || admission.Questions.Length != 1
            || !receipt.Questions.TryGetValue(request.Question, out var question)
            || !ReferenceEquals(question, admission.Questions[0].Id)
            || !ReferenceEquals(question.Operation, admission.Operation)
            || receipt.Inputs.Count != population.InputIds.Length
            || receipt.Inputs.Count != admission.Inputs.Length
            || population.Before.Any(input => input.Id.Side != QueryComparisonSide.Before)
            || population.After.Any(input => input.Id.Side != QueryComparisonSide.After)
            || !ExactSet(population.InputIds, population.Inputs.Select(input => input.Id))
            || !ExactSet(receipt.Inputs.Keys, population.InputIds)
            || !ExactSet(receipt.Inputs.Values.Select(pair => pair.Research), admission.Inputs.Select(input => input.Id)))
            return false;

        foreach (var input in population.Inputs)
        {
            if (!receipt.Inputs.TryGetValue(input.Id, out var pair)
                || !ReferenceEquals(pair.Query, input.Id)
                || !ReferenceEquals(input.Id.Question, population.Question)
                || !ReferenceEquals(input.Id.Operation, population.Operation)
                || pair.Side != input.Id.Side
                || pair.Research.Side != QueryPopulationProjection.ResearchSide(pair.Side)
                || !ReferenceEquals(pair.Research.Question, question)
                || !ReferenceEquals(pair.Research.Operation, admission.Operation)
                || !admission.TryGetInput(pair.Research, out var admitted)
                || admitted.Occurrence is not ImplementationComparisonInputOccurrence occurrence
                || !ReferenceEquals(occurrence.Assembly, input.Binding.Assembly)
                || !ReferenceEquals(occurrence.Resolver, input.Binding.Resolver)
                || !ReferenceEquals(occurrence.BodyIndex, input.Binding.BodyIndex))
                return false;
        }
        return true;
    }

    internal static bool ValidateResolution(WorkspaceResearchTargetCompositionRequest request)
    {
        ResearchTargetResolution resolution = request.Resolution;
        var receipt = request.Projected.Receipt;
        var researchInputs = receipt.Inputs.Values.Select(pair => pair.Research).ToArray();
        ResearchComparisonQuestionId question = receipt.Questions[request.Question];
        ResearchComparisonSide side = QueryPopulationProjection.ResearchSide(request.Side);
        bool exactTerminalSelection = request.Domain is not null
            && request.Census is not null;
        if (!ReferenceEquals(resolution.Operation, receipt.Operation.Research)
            || resolution.Scopes.Count(scope => ReferenceEquals(scope, request.Scope)) != 1
            || !ReferenceEquals(request.Scope.Question, question)
            || !ReferenceEquals(request.Scope.Id.Operation, resolution.Operation)
            || !request.Scope.DeclaringType.Equals(request.DeclaringType)
            || (request.Domain is null) != (request.Census is null)
            || exactTerminalSelection
                && (request.Scope.Domains.Count(domain =>
                        ReferenceEquals(domain, request.Domain)) != 1
                    || !ReferenceEquals(request.Domain!.Scope, request.Scope.Id)
                    || !ReferenceEquals(request.Census!.Domain, request.Domain)
                    || !ReferenceEquals(request.Census.Scope, request.Scope.Id)
                    || request.Census.Side != side
                    || resolution.Censuses.Count(census =>
                        ReferenceEquals(census, request.Census)) != 1)
            || !ExactSet(resolution.Scopes.Select(scope => scope.Id), resolution.Scopes.Select(scope => scope.Id))
            || !ExactSet(resolution.Domains.Select(domain => domain.Id), resolution.Domains.Select(domain => domain.Id))
            || !ExactSet(resolution.Domains, resolution.Scopes.SelectMany(scope => scope.Domains))
            || !ExactSet(resolution.Requests, resolution.Domains.SelectMany(domain => domain.Requests))
            || !ExactSet(resolution.Attempts, resolution.Domains.SelectMany(domain => domain.Attempts)))
            return false;

        // Independently verify the complete resolution, including other domains
        // and other selection occurrences; a narrower census cannot hide inputs.
        foreach (var scope in resolution.Scopes)
        {
            if (!ReferenceEquals(scope.Question, question)
                || !ExactSet(scope.Domains.SelectMany(domain => domain.Inputs).Select(input => input.Input), researchInputs))
                return false;
            foreach (var domain in scope.Domains)
            {
                if (!ReferenceEquals(domain.Scope, scope.Id)
                    || !ExactSet(domain.Requests.Select(target => target.Id),
                        domain.Attempts.Select(attempt => attempt.Request.Id)))
                    return false;
                foreach (var disposition in domain.Inputs)
                {
                    if (!ReferenceEquals(disposition.Input.Question, question)
                        || scope.Kind == ResearchTargetRequestKind.Carried
                            && disposition.Kind != ResearchTargetDispositionKind.Requested
                        || !researchInputs.Any(input => ReferenceEquals(input, disposition.Input)))
                        return false;
                    var targets = domain.Requests.Where(target => ReferenceEquals(target.Input, disposition.Input)).ToArray();
                    if (disposition.Kind == ResearchTargetDispositionKind.Requested)
                    {
                        if (targets.Length != 1 || !ReferenceEquals(disposition.Request, targets[0].Id))
                            return false;
                    }
                    else if (disposition.Kind != ResearchTargetDispositionKind.NotRequested
                        || targets.Length != 0 || disposition.Request is not null)
                        return false;
                }
                foreach (var target in domain.Requests)
                {
                    if (!ReferenceEquals(target.Domain, domain.Id)
                        || !ReferenceEquals(target.Scope, scope.Id)
                        || !ReferenceEquals(target.Question, question)
                        || !ReferenceEquals(target.Input.Question, question)
                        || target.Side != target.Input.Side
                        || target.Kind != scope.Kind
                        || !ReferenceEquals(target.DeclaringType, scope.DeclaringType)
                        || target.Selector != scope.Selector
                        || !domain.Inputs.Any(input => ReferenceEquals(input.Input, target.Input)))
                        return false;
                }
                foreach (var attempt in domain.Attempts)
                    if (!ReferenceEquals(attempt.Id.Request, attempt.Request.Id)
                        || !domain.Requests.Any(target => ReferenceEquals(target, attempt.Request)))
                        return false;
                foreach (ResearchComparisonSide censusSide in
                    new[] { ResearchComparisonSide.Before, ResearchComparisonSide.After })
                {
                    var censuses = resolution.Censuses.Where(census =>
                        ReferenceEquals(census.Domain, domain) && census.Side == censusSide).ToArray();
                    if (censuses.Length != 1
                        || !ReferenceEquals(censuses[0].Scope, scope.Id)
                        || !ExactSet(censuses[0].Inputs, domain.Inputs.Where(input => input.Side == censusSide))
                        || !ExactSet(censuses[0].Attempts, domain.Attempts.Where(attempt => attempt.Request.Side == censusSide)))
                        return false;
                }
            }
        }
        return resolution.Censuses.Length == resolution.Domains.Length * 2;
    }

    internal static ResearchTargetAttempt? UniqueAttempt(
        ResearchTargetScope scope, ResearchComparisonInputId input)
    {
        ResearchTargetAttempt? found = null;
        foreach (var attempt in scope.Domains.SelectMany(domain => domain.Attempts))
        {
            if (!ReferenceEquals(attempt.Request.Input, input))
                continue;
            if (found is not null)
                return null;
            found = attempt;
        }
        return found;
    }

    internal static bool ValidateTerminal(
        WorkspaceResearchTargetCompositionRequest request,
        ResolvedTypeDefinition definition,
        ResearchTargetOutcome.Resolved target,
        ImplementationComparisonInputOccurrence input)
    {
        var assembly = definition.Assembly.Assembly;
        var address = definition.Address;
        var member = target.Target.ApiMember.Member;
        if (!ReferenceEquals(definition.Occurrence.Assembly.Registration, assembly.Registration)
            || !definition.Type.Equals(request.DeclaringType)
            || !definition.Type.Equals(target.Target.ApiType.DefinitionName)
            || target.Target.ApiType.MetadataToken != address.Definition.Value
            || !ReferenceEquals(target.Module, input.BodyIndex.ModuleIdentity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(target.Module.AssemblyIdentity, assembly.Identity)
            || target.Module.ModuleVersionId != address.ModuleVersionId
            || assembly.Registration.ModuleVersionId is { } boundMvid && boundMvid != address.ModuleVersionId
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(input.Assembly.Identity, assembly.Identity)
            || target.Target.NormalizedSelector != request.Scope.Selector.NormalizedSelector)
            return false;

        int?[] memberTokens =
            [member.MetadataToken, member.GetterToken, member.SetterToken, member.AdderToken, member.RemoverToken];
        if (target.Role == ResearchTargetRelationshipRole.None)
            return target.Address is null && target.Target.Body?.MetadataToken is null
                && memberTokens.All(token => token is null);
        if (target.Address is not { } method || method.ModuleVersionId != address.ModuleVersionId
            || (method.Token & unchecked((int)0xff000000)) != 0x06000000
            || (method.Token & 0x00ffffff) == 0
            || target.Target.Body?.MetadataToken != method.Token)
            return false;

        int? roleToken = target.Role switch
        {
            ResearchTargetRelationshipRole.Method => member.MetadataToken,
            ResearchTargetRelationshipRole.Getter => member.GetterToken,
            ResearchTargetRelationshipRole.Setter => member.SetterToken,
            ResearchTargetRelationshipRole.Adder => member.AdderToken,
            ResearchTargetRelationshipRole.Remover => member.RemoverToken,
            _ => null,
        };
        return roleToken == method.Token && memberTokens.Count(token => token == method.Token) == 1;
    }

    internal static bool ExactSet<T>(IEnumerable<T> left, IEnumerable<T> right) where T : class
    {
        HashSet<T> leftSet = new(ReferenceEqualityComparer.Instance);
        HashSet<T> rightSet = new(ReferenceEqualityComparer.Instance);
        foreach (var value in left)
            if (!leftSet.Add(value))
                return false;
        foreach (var value in right)
            if (!rightSet.Add(value))
                return false;
        return leftSet.SetEquals(rightSet);
    }
}
