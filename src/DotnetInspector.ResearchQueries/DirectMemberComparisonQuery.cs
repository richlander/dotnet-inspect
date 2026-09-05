using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>An idless physical designation attached to its borrowed source occurrence.</summary>
public sealed record DirectMemberComparisonEndpoint(
    AssemblyContextParticipant Participant,
    MetadataMethodAddress? Address);

public sealed class DirectMemberComparisonRequest
{
    public DirectMemberComparisonRequest(
        DirectMemberComparisonEndpoint before,
        DirectMemberComparisonEndpoint after,
        IEnumerable<ResearchProducerKind> producers)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(before.Participant);
        ArgumentNullException.ThrowIfNull(after.Participant);
        ArgumentNullException.ThrowIfNull(producers);
        Before = before;
        After = after;
        Producers = [.. producers];
        if (Producers.IsEmpty
            || Producers.Any(kind => !Enum.IsDefined(kind))
            || Producers.Distinct().Count() != Producers.Length)
        {
            throw new ArgumentException(
                "Select a nonempty, distinct set of C# and/or IL producers.", nameof(producers));
        }
    }

    public DirectMemberComparisonEndpoint Before { get; }
    public DirectMemberComparisonEndpoint After { get; }
    public ImmutableArray<ResearchProducerKind> Producers { get; }
}

/// <summary>Compares exactly two designated physical methods without correspondence inference.</summary>
public static class DirectMemberComparisonQuery
{
    public static InspectionQuery<LocalComparisonQueryResult> Definition { get; } =
        new("Direct member comparison", InspectionCost.Unbounded);

    public static LocalComparisonQueryResult Execute(
        AssemblyContextGroup group,
        DirectMemberComparisonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(request);
        return new Invocation(group, request, cancellationToken).Execute();
    }

    sealed class Invocation(
        AssemblyContextGroup group,
        DirectMemberComparisonRequest request,
        CancellationToken cancellationToken)
    {
        LocalComparisonQueryIdentity? _identity;
        QueryToResearchPopulationReceipt? _receipt;
        QueryComparisonSide? _side;

        internal LocalComparisonQueryResult Execute()
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.Before.Address is null)
                    return MissingAddress(QueryComparisonSide.Before);
                if (request.After.Address is null)
                    return MissingAddress(QueryComparisonSide.After);
                return Borrow(request.Before, QueryComparisonSide.Before,
                    before => Borrow(request.After, QueryComparisonSide.After,
                        after => Compare(before, after)));
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return Failure(new LocalComparisonQueryFailure.Cancelled(exception));
            }
            catch (Exception exception)
                when (AssemblyContextQueryExecutor.IsArtifactFailure(exception))
            {
                return Failure(new LocalComparisonQueryFailure.Failed(exception));
            }
        }

        LocalComparisonQueryResult Borrow(
            DirectMemberComparisonEndpoint endpoint,
            QueryComparisonSide side,
            Func<ImplementationComparisonBinding, LocalComparisonQueryResult> compare)
        {
            _side = side;
            AssemblyImageAccessResult<LocalComparisonQueryResult> access =
                group.UseSnapshot(endpoint.Participant, cancellationToken, snapshot =>
                {
                    var subject = new AssemblyContextSubject(endpoint.Participant.Assembly);
                    IAssemblyReferenceResolver resolver =
                        AssemblyContextAnalysisSource.Resolver(group, subject);
                    LibraryBodyIndex? index = null;
                    try
                    {
                        index = LibraryBodyIndex.OpenFromPrefetchedImage(
                            AssemblyContextAnalysisSource.Name(subject),
                            snapshot.Content,
                            LibraryBodyAnalysisFeatures.MethodEvidence,
                            resolver,
                            bodyScope: new HashSet<int> { endpoint.Address!.Value.Token });
                        return compare(new(
                            snapshot.RetainAssemblyReference(endpoint.Participant.Assembly),
                            resolver, index));
                    }
                    finally
                    {
                        index?.ReleaseCallGraphCaches();
                    }
                });
            return access switch
            {
                AssemblyImageAccessResult<LocalComparisonQueryResult>.Available available =>
                    available.Value,
                AssemblyImageAccessResult<LocalComparisonQueryResult>.Rejected rejected =>
                    Failure(new LocalComparisonQueryFailure.AccessRejected(rejected.Failure), side),
                _ => throw new InvalidOperationException("Unknown assembly image access result."),
            };
        }

        LocalComparisonQueryResult Compare(
            ImplementationComparisonBinding before,
            ImplementationComparisonBinding after)
        {
            _side = null;
            cancellationToken.ThrowIfCancellationRequested();
            var sealedOutcome = QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest([before], [after]));
            if (sealedOutcome is QueryPopulationSealingOutcome.Rejected rejected)
                return Failure(new LocalComparisonQueryFailure.PopulationRejected(rejected.Rejection));
            var population = (QueryComparisonPopulation<ImplementationComparisonBinding>)
                ((QueryPopulationSealingOutcome.Sealed)sealedOutcome).Population;
            _identity = new(population);
            QueryPopulationProjectionOutcome projection = QueryPopulationProjection.Execute(population);
            if (projection is QueryPopulationProjectionOutcome.AdmissionRejected admissionRejected)
                return Failure(new LocalComparisonQueryFailure.AdmissionRejected(admissionRejected.Rejection));
            if (projection is QueryPopulationProjectionOutcome.Rejected projectionRejected)
                throw new InvalidOperationException($"Population projection failed: {projectionRejected.Reason}.");
            ProjectedQueryPopulation projected =
                ((QueryPopulationProjectionOutcome.Projected)projection).Population;
            _receipt = projected.Receipt;

            Selection? beforeSelection = Select(before, request.Before.Address!.Value,
                QueryComparisonSide.Before, out LocalComparisonQueryResult? failure);
            if (failure is not null)
                return failure;
            Selection? afterSelection = Select(after, request.After.Address!.Value,
                QueryComparisonSide.After, out failure);
            if (failure is not null)
                return failure;
            _side = null;
            ResearchComparisonQuestionId question = _receipt.Questions[_identity.Question];
            ResearchAdmittedInput beforeInput = projected.Admission.GetInput(
                _receipt.Inputs[_identity.Before].Research);
            ResearchAdmittedInput afterInput = projected.Admission.GetInput(
                _receipt.Inputs[_identity.After].Research);
            var planningRequest = new ResearchTargetPlanningRequest(
                projected.Admission,
                projected.Admission.Inputs.Select(input =>
                    new ResearchTargetInputRoleAssignment(input, ResearchTargetInputRole.Implementation)),
                [
                    beforeSelection!.Exact(question, beforeInput, request.Before.Address.Value),
                    afterSelection!.Exact(question, afterInput, request.After.Address.Value),
                ]);
            ResearchTargetPlanningOutcome planning =
                ResearchTargetResolver.Resolve(planningRequest, cancellationToken);
            if (planning is ResearchTargetPlanningOutcome.Rejected planningRejected)
                return Failure(new LocalComparisonQueryFailure.PlanningRejected(planningRejected.Rejection));
            ResearchTargetResolution resolution =
                ((ResearchTargetPlanningOutcome.Planned)planning).Resolution;
            ResearchTargetAttempt beforeAttempt = resolution.Attempts.Single(attempt =>
                ReferenceEquals(attempt.Request.Input, beforeInput.Id));
            ResearchTargetAttempt afterAttempt = resolution.Attempts.Single(attempt =>
                ReferenceEquals(attempt.Request.Input, afterInput.Id));
            ResearchDesignatedPairOutcome designation = ResearchDesignatedPairAdmission.Admit(
                projected.Admission, resolution, beforeAttempt, afterAttempt);
            if (designation is ResearchDesignatedPairOutcome.Rejected pairRejected)
                return Failure(new LocalComparisonQueryFailure.DesignationRejected(pairRejected));
            if (designation is ResearchDesignatedPairOutcome.Unavailable unavailable)
            {
                QueryComparisonSide? side = unavailable.Endpoints.Length == 1
                    ? unavailable.Endpoints.Single().Side == ResearchComparisonSide.Before
                        ? QueryComparisonSide.Before : QueryComparisonSide.After
                    : null;
                return Failure(new LocalComparisonQueryFailure.DesignationUnavailable(unavailable), side);
            }

            ResearchDesignatedPair pair = ((ResearchDesignatedPairOutcome.Admitted)designation).Pair;
            return new LocalComparisonPublication(_identity, projected).Run(
                new ResearchProducerSessionRequest(projected.Admission, pair, request.Producers),
                cancellationToken);
        }

        Selection? Select(
            ImplementationComparisonBinding binding,
            MetadataMethodAddress address,
            QueryComparisonSide side,
            out LocalComparisonQueryResult? failure)
        {
            _side = side;
            cancellationToken.ThrowIfCancellationRequested();
            using Stream stream = binding.Assembly.OpenRead();
            using var image = new PEReader(stream);
            MetadataReader reader = image.GetMetadataReader();
            int row = MetadataTokens.GetRowNumber(address.Handle);
            if (row <= 0 || row > reader.MethodDefinitions.Count)
            {
                failure = Failure(new LocalComparisonQueryFailure.InvalidDesignation(
                    DirectMemberDesignationFailureKind.MissingMethod, []), side);
                return null;
            }

            ApiSurface surface = ApiSurfaceExtractor.Extract(image,
                includeAll: true, typesOnly: false, includeCompilerGenerated: true);
            foreach (ApiType type in surface.Types)
            {
                foreach (ApiMember member in type.Members)
                {
                    ResearchTargetRelationshipRole? role =
                        member.MetadataToken == address.Token ? ResearchTargetRelationshipRole.Method
                        : member.GetterToken == address.Token ? ResearchTargetRelationshipRole.Getter
                        : member.SetterToken == address.Token ? ResearchTargetRelationshipRole.Setter
                        : member.AdderToken == address.Token ? ResearchTargetRelationshipRole.Adder
                        : member.RemoverToken == address.Token ? ResearchTargetRelationshipRole.Remover
                        : null;
                    if (role is null)
                        continue;
                    string selector = ApiMemberIdentity.GetMemberAnchor(type, member).StableSelector;
                    if (role != ResearchTargetRelationshipRole.Method)
                    {
                        int?[] tokens = member.Kind == "property"
                            ? [member.GetterToken, member.SetterToken]
                            : [member.AdderToken, member.RemoverToken];
                        int ordinal = Array.IndexOf(
                            tokens.Where(token => token is not null).ToArray(), address.Token) + 1;
                        selector += $":{ordinal}";
                    }
                    failure = null;
                    return new(type.DefinitionName?.ToMetadataFullName() ?? type.FullName,
                        MemberTargetSelector.Parse(selector), role.Value);
                }
            }

            failure = Failure(new LocalComparisonQueryFailure.InvalidDesignation(
                DirectMemberDesignationFailureKind.MetadataSelectionUnavailable,
                [.. surface.InspectionFailures]), side);
            return null;
        }

        LocalComparisonQueryResult MissingAddress(QueryComparisonSide side) =>
            Failure(new LocalComparisonQueryFailure.InvalidDesignation(
                DirectMemberDesignationFailureKind.MissingAddress, []), side);

        LocalComparisonQueryResult.NonSuccess Failure(
            LocalComparisonQueryFailure failure,
            QueryComparisonSide? side = null)
            => new(_identity, _receipt, side ?? _side, failure);
    }

    sealed record Selection(
        string Type,
        MemberTargetSelector Selector,
        ResearchTargetRelationshipRole Role)
    {
        internal ResearchExactAddressMemberSelection Exact(
            ResearchComparisonQuestionId question,
            ResearchAdmittedInput input,
            MetadataMethodAddress address)
            => new(question, input, Type, Selector, address, Role);
    }
}
