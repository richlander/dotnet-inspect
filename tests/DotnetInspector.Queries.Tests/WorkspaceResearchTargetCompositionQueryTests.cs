using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

using ILInspector.Metadata;
using ILInspector.Research;

using static DotnetInspector.Queries.Tests.WorkspaceResearchTargetFixture;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceResearchTargetCompositionQueryTests
{
    [Fact]
    public void WorkspaceResearchTarget_DirectDefinitionRetainsRootAttempt()
    {
        using var fixture = Direct();
        WorkspaceResearchTargetPlan plan = fixture.PublicPlan();
        fixture.RetainAll();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();
        var receipt = ComposePublic(fixture, plan, 0);
        var attempt = plan.Resolution.Attempts.Single(item =>
            item.Outcome is ResearchTargetOutcome.Resolved);

        Assert.Same(attempt.Id, receipt.RootAttemptId);
        Assert.Same(attempt.Id, receipt.EffectiveAttemptId);
        Assert.Same(receipt.RootAttempt, receipt.EffectiveAttempt);
        Assert.Same(receipt.RootInput, receipt.TerminalInput);
        Assert.Empty(Resolved(receipt).Hops);
        Assert.Equal(receipt.EffectiveAttempt.Module.ModuleVersionId, receipt.EffectiveAttempt.Address!.Value.ModuleVersionId);
        Assert.Equal(Resolved(receipt).Definition.Address.ModuleVersionId, receipt.EffectiveAttempt.Address.Value.ModuleVersionId);
        Assert.Equal(receipt.EffectiveAttempt.MemberTokens.Method, receipt.EffectiveAttempt.Address.Value.Token);
        Assert.Equal(Resolved(receipt).Definition.Assembly.Assembly.Identity, receipt.EffectiveAttempt.Module.AssemblyIdentity);
        Assert.Equal(opens, fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void WorkspaceResearchTarget_ForwardedDefinitionSelectsExactTerminalAttempt()
    {
        using var fixture = Forwarded();
        WorkspaceResearchTargetPlan plan = fixture.PublicPlan();
        fixture.RetainAll();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();
        var receipt = ComposePublic(fixture, plan, 1);
        ResearchTargetAttempt terminal = plan.Resolution.Attempts.Single(item =>
            item.Outcome is ResearchTargetOutcome.Resolved);

        Assert.Same(terminal.Id, receipt.EffectiveAttemptId);
        Assert.Same(plan.Population.Before[1].Id, receipt.TerminalInput);
        Assert.NotSame(receipt.RootAttemptId, receipt.EffectiveAttemptId);
        Assert.Equal(Assert.IsType<ResearchTargetOutcome.Resolved>(terminal.Outcome).Address!.Value.Token,
            receipt.EffectiveAttempt.Address!.Value.Token);
        Assert.Single(Resolved(receipt).Hops);
        Assert.Equal(opens, fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void WorkspaceResearchTarget_ForwardedRootAttemptRemainsUnavailable()
    {
        using var fixture = Forwarded();
        WorkspaceResearchTargetPlan plan = fixture.PublicPlan();
        var root = plan.Resolution.Attempts.Single(item =>
            item.Request.Input == plan.Resolution.Domains.Single(domain =>
                domain.Key.Identity.Name == "Facade").Inputs[0].Input);
        var original = Assert.IsType<ResearchTargetOutcome.Unavailable>(root.Outcome);
        Assert.Equal(ResearchTargetDiagnosticKind.DeclaringTypeForwarded, original.Diagnostic.Kind);

        var receipt = ComposePublic(fixture, plan, 1);
        Assert.Same(root.Id, receipt.RootAttemptId);
        Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Unavailable>(receipt.RootAttempt);
        Assert.Same(original, root.Outcome);
    }

    [Fact]
    public void WorkspaceResearchTarget_MultiHopRetainsCompleteMetadataPath()
    {
        byte[] terminal = BuildAssembly("Terminal");
        byte[] bridge = BuildAssembly("Bridge", false, Identity(terminal));
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(bridge)), bridge, terminal);
        var receipt = ComposePublic(fixture, fixture.PublicPlan(), 2);
        var resolved = Resolved(receipt);

        Assert.Equal(["Facade", "Bridge"], resolved.Hops.Select(hop => hop.SourceAssembly.Assembly.Identity.Name));
        Assert.Equal(["Bridge", "Terminal"], resolved.Hops.Select(hop => hop.TargetReference.Name));
        Assert.All(resolved.Hops, hop =>
        {
            Assert.Equal(AssemblyResolutionScope.Any, hop.Scope);
            Assert.Equal(0x27000001, Assert.Single(hop.Declarations).Value);
            Assert.Same(hop.SourceAssembly.Assembly.Registration.Id,
                hop.SourceOccurrence.Assembly.Registration.Id);
        });
        Assert.Same(receipt.TerminalInput, resolved.Definition.Assembly.Assembly.Registration.Input);
    }

    [Fact]
    public void WorkspaceResearchTarget_UnboundTerminalIsUnavailable()
    {
        byte[] missing = BuildAssembly("Missing");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(missing)), BuildAssembly("Unrelated", false));
        var plan = fixture.Plan();
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            Execute(plan.Request(fixture)));
        Assert.Equal(WorkspaceResearchTargetCompositionUnavailability.MetadataResolutionUnavailable, result.Reason);
        Assert.IsType<WorkspaceTypeResolutionEvidence.Available>(result.Evidence);
        Assert.Null(result.TerminalAttempt);
    }

    [Fact]
    public void WorkspaceResearchTarget_ImageOpenFailureIsUnavailable()
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        fixture.Nodes[1].OnOpen = () => throw new IOException("image-open-sentinel");
        var query = Assert.IsType<AssemblyContextTypeResolutionResult.Rejected>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            Execute(plan.Request(fixture)));
        Assert.Equal(WorkspaceResearchTargetCompositionUnavailability.ParticipantImageUnavailable, result.Reason);
        var evidence = Assert.IsType<WorkspaceTypeResolutionEvidence.QueryRejected>(result.Evidence);
        Assert.Same(plan.Population.Before[1].Id, evidence.Input);
        Assert.Equal(query.Failure.Kind, evidence.Failure.Kind);
        Assert.Equal(query.Failure.Detail.ToString(), evidence.Failure.Detail.ToString());
        Assert.Equal(query.Failure.MetadataRootReason, evidence.Failure.MetadataRootReason);
        Assert.Null(result.TerminalAttempt);
        Assert.Null(result.Census);
        Assert.Equal(0, fixture.Nodes.Sum(node => node.Policy.Selections));
    }

    [Fact]
    public void WorkspaceResearchTarget_MissingAnyGroupParticipantIsRejected()
    {
        byte[] terminal = BuildAssembly("Terminal");
        byte[] bridge = BuildAssembly("Bridge", false, Identity(terminal));
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(bridge)), bridge, terminal, BuildAssembly("Unused", false));
        foreach (int missing in Enumerable.Range(0, fixture.Nodes.Length))
        {
            var plan = fixture.Plan(fixture.Population(
                Enumerable.Range(0, fixture.Nodes.Length).Where(index => index != missing).ToArray()));
            AssertPreQueryRejected(fixture, plan.Request(fixture),
                WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_DuplicatePopulationMemberIsRejected()
    {
        using var fixture = Direct();
        var plan = fixture.Plan(fixture.Population([0, 0]));
        AssertPreQueryRejected(fixture, plan.Request(fixture),
            WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
    }

    [Fact]
    public void WorkspaceResearchTarget_UnrelatedSameNameParticipantCannotSatisfyRoute()
    {
        byte[] actual = BuildAssembly("Terminal");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(actual)),
            BuildAssembly("Terminal", version: new Version(2, 0, 0, 0)));
        fixture.Nodes[0].Policy.Target = Descriptor(actual,
            () => throw new InvalidOperationException("Composition must not acquire an external candidate."));
        var plan = fixture.Plan();
        var result = Execute(plan.Request(fixture, terminal: 1));
        Assert.IsNotType<WorkspaceResearchTargetCompositionResult.Composed>(result);
        Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(result);
    }

    [Fact]
    public void WorkspaceResearchTarget_ReferenceOnlyTerminalIsUnavailable()
    {
        using var fixture = Forwarded();
        var plan = fixture.Plan(referenceOnly: 1);
        Assert.Equal(0, fixture.Nodes[1].Opens);
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            Execute(plan.Request(fixture, terminal: 1)));
        Assert.Equal(WorkspaceResearchTargetCompositionUnavailability.BlockedTerminalCensus, result.Reason);
        Assert.Equal(ResearchTargetDiagnosticKind.ReferenceOnlyInput,
            Assert.IsType<ResearchTargetOutcome.Unavailable>(plan.Resolution.Attempts[1].Outcome).Diagnostic.Kind);
        Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Unavailable>(result.TerminalAttempt);
    }

    [Fact]
    public void WorkspaceResearchTarget_BlockedTerminalDomainIsUnavailable()
    {
        byte[] terminal = BuildAssembly("Terminal");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(terminal)), terminal, BuildAssembly("Terminal"));
        var plan = fixture.Plan();
        Assert.True(plan.Scope.Domains.Single(domain => domain.Key.Identity.Name == "Terminal").IsAmbiguous);
        // Select the exact root definition so Metadata succeeds while the Research domain is blocked.
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            Execute(plan.Request(fixture, terminal: 1, root: 1)));
        Assert.Equal(WorkspaceResearchTargetCompositionUnavailability.BlockedTerminalCensus, result.Reason);
        Assert.Equal(ResearchTargetCensusHealth.Blocked, result.Census!.Health);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsExactAddressScope()
    {
        using var fixture = new WorkspaceResearchTargetFixture(BuildAssembly("Direct"));
        var plan = fixture.Plan(exact: true);
        AssertPreQueryRejected(fixture, plan.Request(fixture),
            WorkspaceResearchTargetCompositionRejection.UnsupportedRequestKind);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsWrongSideScopeAndDomainMappings()
    {
        using var fixture = Direct();
        var plan = fixture.Plan(fixture.Population(after: [0, 1]));
        ResearchTargetDomain domain = plan.Scope.Domains[0];
        ResearchTargetDomainSideCensus afterCensus = plan.Resolution.Censuses.Single(census =>
            census.Domain == domain && census.Side == ResearchComparisonSide.After);
        AssertPreQueryRejected(fixture, plan.Request(fixture, domain: domain, census: afterCensus),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        AssertPreQueryRejected(fixture, plan.Request(fixture, domain: plan.Scope.Domains[1],
            census: plan.Resolution.Censuses.Single(census =>
                census.Domain == domain && census.Side == ResearchComparisonSide.Before)),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        AssertPreQueryRejected(fixture, plan.Request(fixture, side: (QueryComparisonSide)123,
            domain: domain, census: afterCensus),
            WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsCrossSelectionScopeAttemptAndCensus()
    {
        using var fixture = Direct();
        var plan = fixture.Plan(selections: 2);
        ResearchTargetScope other = plan.Resolution.Scopes[1];
        ResearchTargetDomain otherDomain = other.Domains[0];
        ResearchTargetDomainSideCensus otherCensus = plan.Resolution.Censuses.Single(census =>
            census.Domain == otherDomain && census.Side == ResearchComparisonSide.Before);
        Assert.NotSame(plan.Scope.Id, other.Id);
        Assert.NotSame(plan.Scope.Domains[0].Attempts[0].Id, otherDomain.Attempts[0].Id);
        AssertPreQueryRejected(fixture, plan.Request(fixture, domain: otherDomain, census: otherCensus),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        AssertPreQueryRejected(fixture, plan.Request(fixture, census: otherCensus),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        var foreign = fixture.Plan(plan.Population);
        AssertPreQueryRejected(fixture, foreign.Request(fixture, scope: other, domain: otherDomain, census: otherCensus),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);

        // Borrow another selection's already owner-produced attempts without changing
        // their IDs, and place them in the first domain's purported census.
        ResearchTargetDomain original = plan.Scope.Domains[0];
        var crossed = new ResearchTargetDomain(original.Id, original.Key,
            original.Inputs, original.ConflictingInputs, otherDomain.Requests, otherDomain.Attempts);
        var crossedScope = new ResearchTargetScope(plan.Scope.Id, plan.Scope.DeclaringType,
            plan.Scope.Selector, plan.Scope.Kind, plan.Scope.Domains.Replace(original, crossed));
        var crossedResolution = new ResearchTargetResolution(plan.Resolution.Operation, [crossedScope]);
        var crossedCensus = crossedResolution.Censuses.Single(census =>
            census.Domain == crossed && census.Side == ResearchComparisonSide.Before);
        AssertPreQueryRejected(fixture, plan.Request(fixture, scope: crossedScope, domain: crossed,
            census: crossedCensus, resolution: crossedResolution),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsForeignOrIncompletePopulationReceipt()
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        var foreign = fixture.Plan(plan.Population);
        AssertPreQueryRejected(fixture, plan.Request(fixture, projected: foreign.Projected),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
        var incomplete = fixture.Plan(fixture.Population([0]));
        AssertPreQueryRejected(fixture, plan.Request(fixture, projected: incomplete.Projected),
            WorkspaceResearchTargetCompositionRejection.PopulationReceiptMismatch);
        var anotherPopulation = fixture.Plan();
        AssertPreQueryRejected(fixture, plan.Request(fixture, projected: anotherPopulation.Projected),
            WorkspaceResearchTargetCompositionRejection.PopulationReceiptMismatch);

        ResearchAdmittedQuestion question = Assert.Single(plan.Projected.Admission.Questions);
        var shortQuestion = new ResearchAdmittedQuestion(question.Id, [question.Before[0]], []);
        var shortAdmission = new ResearchAdmittedPopulation(plan.Projected.Admission.Profile,
            plan.Projected.Admission.Operation, [shortQuestion],
            shortQuestion.Inputs.Select(input =>
                KeyValuePair.Create(input.Occurrence, input)));
        AssertPreQueryRejected(fixture, plan.Request(fixture,
            projected: new(shortAdmission, plan.Projected.Receipt)),
            WorkspaceResearchTargetCompositionRejection.PopulationReceiptMismatch);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsExtraForeignPopulationMember()
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        using var narrow = fixture.CreateGroup([0]);
        AssertPreQueryRejected(fixture, plan.Request(fixture, group: narrow),
            WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
        var replacement = fixture.Plan(fixture.Population([1]));
        AssertPreQueryRejected(fixture, replacement.Request(fixture, group: narrow),
            WorkspaceResearchTargetCompositionRejection.PopulationMismatch);
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsBroaderResearchPopulation()
    {
        using var fixture = Direct();
        var narrow = fixture.Plan(fixture.Population([0]));
        var broader = fixture.Plan();
        using var group = fixture.CreateGroup([0]);
        AssertPreQueryRejected(fixture,
            narrow.Request(fixture, group: group, resolution: broader.Resolution),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);

        // Keep the original operation, selected scope, and census valid, but append
        // a foreign domain inside that scope. A narrow selected census cannot hide it.
        var broadenedScope = new ResearchTargetScope(narrow.Scope.Id,
            narrow.Scope.DeclaringType, narrow.Scope.Selector, narrow.Scope.Kind,
            narrow.Scope.Domains.Add(broader.Scope.Domains[1]));
        var broadened = new ResearchTargetResolution(narrow.Resolution.Operation, [broadenedScope]);
        AssertPreQueryRejected(fixture, narrow.Request(fixture, group: group, resolution: broadened,
            scope: broadenedScope, census: broadened.Censuses[0]),
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceResearchTarget_RequiresTerminalAssemblyModuleAndAddressAgreement(bool differentTypeRow)
    {
        Guid originalMvid = Guid.NewGuid();
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Terminal", mvid: originalMvid), BuildAssembly("Unused", false));
        var plan = fixture.Plan();
        var target = Assert.IsType<ResearchTargetOutcome.Resolved>(plan.Resolution.Attempts[0].Outcome);
        Assert.Equal(0x06000001, target.Address!.Value.Token);
        fixture.Nodes[0].Image = BuildAssembly("Terminal",
            mvid: differentTypeRow ? originalMvid : Guid.NewGuid(), leadingType: differentTypeRow);

        // A second owner-produced plan proves the other image is a genuine resolved
        // MethodDef at the same row, not a manually altered Research result.
        using var neighboring = new WorkspaceResearchTargetFixture(fixture.Nodes[0].Image);
        var other = Assert.IsType<ResearchTargetOutcome.Resolved>(
            neighboring.PublicPlan().Resolution.Attempts[0].Outcome);
        Assert.Equal(target.Address.Value.Token, other.Address!.Value.Token);
        if (differentTypeRow)
            Assert.NotEqual(target.Target.ApiType.MetadataToken, other.Target.ApiType.MetadataToken);
        else
            Assert.NotEqual(target.Address.Value.ModuleVersionId, other.Address.Value.ModuleVersionId);

        AssertRejected(Execute(plan.Request(fixture)),
            WorkspaceResearchTargetCompositionRejection.TerminalEvidenceMismatch);
        Assert.Equal(originalMvid, target.Address.Value.ModuleVersionId);
    }

    [Fact]
    public void WorkspaceResearchTarget_DivergentTerminalDomainsDoNotPair()
    {
        byte[] before = BuildAssembly("BeforeTerminal");
        byte[] after = BuildAssembly("AfterTerminal");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("BeforeFacade", false, Identity(before)), before,
            BuildAssembly("AfterFacade", false, Identity(after)), after);
        using var beforeGroup = fixture.CreateGroup([0, 1]);
        using var afterGroup = fixture.CreateGroup([2, 3]);
        var plan = fixture.Plan(fixture.Population([0, 1], [2, 3]));
        var left = Composed(Execute(plan.Request(fixture, terminal: 1, group: beforeGroup)));
        var right = Composed(Execute(plan.Request(fixture, terminal: 1, root: 2,
            side: QueryComparisonSide.After, group: afterGroup)));
        Assert.NotSame(left.Domain, right.Domain);
        Assert.DoesNotContain(plan.Resolution.Correspondences,
            item => item is ResearchTargetCorrespondenceOutcome.Paired);
        Assert.Contains(plan.Resolution.Correspondences, item =>
            item is ResearchTargetCorrespondenceOutcome.BeforeOnly && item.DomainId == left.Domain);
        Assert.Contains(plan.Resolution.Correspondences, item =>
            item is ResearchTargetCorrespondenceOutcome.AfterOnly && item.DomainId == right.Domain);
        foreach (var correspondence in plan.Resolution.Correspondences)
            Assert.IsType<WorkspaceResearchTargetHandoffResult.Unavailable>(
                WorkspaceResearchTargetHandoff.Execute(left, right, plan.Resolution, correspondence));
    }

    [Fact]
    public void WorkspaceResearchTarget_Direct()
    {
        using var fixture = Direct();
        WorkspaceResearchTargetPlan plan = fixture.PublicPlan(fixture.Population(after: [0, 1]));
        var before = ComposePublic(fixture, plan, 0);
        var after = ComposePublic(fixture, plan, 0, QueryComparisonSide.After);
        var correspondence = Assert.Single(plan.Resolution.Correspondences.OfType<ResearchTargetCorrespondenceOutcome.Paired>());
        var paired = Assert.IsType<WorkspaceResearchTargetHandoffResult.Paired>(
            WorkspaceResearchTargetHandoff.Execute(before, after, plan.Resolution, correspondence));
        Assert.Same(before, paired.WorkItem.Before);
        Assert.Same(after, paired.WorkItem.After);
        Assert.Same(correspondence, paired.WorkItem.Correspondence);
    }

    [Fact]
    public void WorkspaceResearchTarget_Direct_TypedProjectionAndAdmissionFailures()
    {
        using var fixture = Direct();
        var population = fixture.Population();
        QueryComparisonPopulation<ImplementationComparisonBinding> With(
            QueryComparisonInput<ImplementationComparisonBinding> input) =>
            new(population.Profile, population.Question, [input], [], null, null);
        var foreignQuestion = new QueryComparisonQuestionId(new QueryComparisonOperationId());
        var foreignInput = new QueryComparisonInput<ImplementationComparisonBinding>(
            new QueryComparisonInputId(foreignQuestion, QueryComparisonSide.Before), population.Before[0].Binding);
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();
        var projection = Assert.IsType<WorkspaceResearchTargetPlanningOutcome.Rejected>(
            WorkspaceResearchTargetPlanningQuery.Execute(With(foreignInput), TypeName, Selector,
                TestContext.Current.CancellationToken));
        Assert.Equal(QueryPopulationProjectionRejection.InputMappingMismatch, projection.Reason);

        // Projection constructs fresh complete occurrences, so no valid sealed population
        // can reach AdmissionRejected. Check its lossless wrapper using an owner rejection.
        var ownerAdmission = Assert.IsType<ResearchAdmissionOutcome.Rejected>(
            ResearchComparisonAdmission.Admit(new(ResearchComparisonProfile.ImplementationComparison,
                [new ResearchComparisonAdmissionQuestion([null], [])])));
        var admission = new WorkspaceResearchTargetPlanningOutcome.AdmissionRejected(ownerAdmission.Rejection);
        Assert.Same(ownerAdmission.Rejection, admission.Rejection);
        Assert.Equal(ResearchAdmissionRejectionKind.MissingInput, admission.Rejection.Kind);
        Assert.Equal(opens, fixture.Nodes.Select(node => node.Opens));
        Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Policy.Reads));
    }

    [Fact]
    public void WorkspaceResearchTarget_Direct_TypedPlanningFailurePreservesOwnerRejection()
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        var failed = Assert.IsType<ResearchTargetPlanningOutcome.Rejected>(ResearchTargetResolver.Resolve(
            new(plan.Projected.Admission, [],
                [new ResearchCarriedMemberSelection(
                    plan.Projected.Receipt.Questions[plan.Population.Question],
                    WorkspaceResearchTargetFixture.TypeName,
                    Selector)]),
            TestContext.Current.CancellationToken));
        Assert.Equal(ResearchTargetPlanningRejectionKind.MissingInputRole, failed.Rejection.Kind);
        // The public facade always supplies total implementation roles and one valid
        // carried selection. Its reserved PlanningRejected arm has no public trigger.
        var projected = new WorkspaceResearchTargetPlanningOutcome.PlanningRejected(failed.Rejection);
        Assert.Same(failed.Rejection, projected.Rejection);
        Assert.DoesNotContain(projected.GetType().GetProperties(),
            property => property.PropertyType == typeof(WorkspaceResearchTargetPlan));
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsForeignRootAndUnsupportedResolutionScope()
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        using var group = fixture.CreateGroup([1]);
        AssertPreQueryRejected(fixture, plan.Request(fixture, group: group),
            WorkspaceResearchTargetCompositionRejection.ForeignRoot);
        AssertPreQueryRejected(fixture, plan.Request(fixture, resolutionScope: (AssemblyResolutionScope)123),
            WorkspaceResearchTargetCompositionRejection.UnsupportedResolutionScope);
    }

    [Fact]
    public void AssemblyContextTypeResolutionQuery_GroupScopedResolution()
    {
        using var fixture = Forwarded();
        fixture.RetainAll();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();
        var result = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(result.Outcome);
        Assert.Same(fixture.Nodes[1].Assembly.Registration, resolved.Definition.Assembly.Assembly.Registration);
        Assert.Single(resolved.Hops);
        Assert.Equal(opens, fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void AssemblyContextTypeResolutionQuery_CandidateOpenRejection()
    {
        using var fixture = Direct();
        fixture.Nodes[1].OnOpen = () => throw new IOException("candidate-open-sentinel");
        var result = Assert.IsType<AssemblyContextTypeResolutionResult.Rejected>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        Assert.Same(fixture.Nodes[1].Assembly, result.Assembly);
        Assert.Equal(CandidateOpenFailureKind.Unreadable, result.Failure.Kind);
        Assert.Equal(0, fixture.Nodes.Sum(node => node.Policy.Selections));
    }

    [Theory]
    [InlineData("selected")]
    [InlineData("ambiguous")]
    [InlineData("shadow")]
    public void AssemblyContextTypeResolutionQuery_DoesNotAcquireOutsideGroup(string selectionKind)
    {
        byte[] external = BuildAssembly("External");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(external)), BuildAssembly("Unused", false));
        int opens = 0;
        var descriptor = Descriptor(external, () =>
        {
            opens++;
            return new MemoryStream(external, writable: false);
        });
        fixture.Nodes[0].Policy.SelectOverride = _ => selectionKind switch
        {
            "selected" => AssemblyBindingSelection.Found(descriptor),
            "ambiguous" => AssemblyBindingSelection.Multiple([descriptor, fixture.Nodes[1].Assembly]),
            "shadow" => AssemblyBindingCandidateDomain.Create(
                [fixture.Nodes[1].Assembly, descriptor])
                .Finalize([fixture.Nodes[1].Assembly]),
            _ => throw new ArgumentOutOfRangeException(nameof(selectionKind)),
        };
        var plan = fixture.Plan();
        var query = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        Assert.IsNotType<TypeResolutionOutcome.Resolved>(query.Outcome);
        var result = Execute(plan.Request(fixture));
        Assert.Equal(0, opens);
        Assert.True(fixture.Nodes[0].Policy.Selections > 0);
        Assert.IsNotType<WorkspaceResearchTargetCompositionResult.Composed>(result);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public void AssemblyContextTypeResolutionQuery_ParticipantVersionsCheckedOnCandidateRejection(
        int participant, int read)
    {
        using var fixture = Direct();
        fixture.Nodes[1].OnOpen = () => throw new IOException("candidate-rejection-version-check");
        fixture.ResetProbes();
        fixture.Nodes[participant].Policy.DriftOnRead = read;
        AssemblyContextTypeResolutionResult? published = null;
        Assert.Throws<InvalidOperationException>(() => published = AssemblyContextTypeResolutionQuery.Execute(
            fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        Assert.Null(published);
        Assert.Equal(read, fixture.Nodes[participant].Policy.Reads);
        Assert.Equal(read == 1 ? 0 : 1, fixture.Nodes[1].Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyContextTypeResolutionQuery_RootBindingPolicyVersionDriftThrows(bool postQuery) =>
        AssertQueryDrift(0, postQuery);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyContextTypeResolutionQuery_NonRootBindingPolicyVersionDriftThrows(bool postQuery) =>
        AssertQueryDrift(1, postQuery);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceResearchTarget_RootBindingPolicyVersionDriftThrows(bool postQuery) =>
        AssertCompositionDrift(0, postQuery);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceResearchTarget_NonRootBindingPolicyVersionDriftThrows(bool postQuery) =>
        AssertCompositionDrift(1, postQuery);

    [Theory]
    [MemberData(nameof(PublicationPaths))]
    public void WorkspaceResearchTarget_PrePublicationBindingPolicyVersionDriftThrows(
        PublicationPath path, int participant)
    {
        using var fixture = CreatePublicationPath(path, out var request);
        fixture.ResetProbes();
        WorkspaceResearchTargetCompositionResult baseline = ExecutePublicationPath(path, fixture, request);
        AssertPublicationPath(path, baseline);
        int[] finalReads = fixture.Nodes.Select(node => node.Policy.Reads).ToArray();
        int finalRead = finalReads[participant];
        Assert.True(finalRead >= 4);

        fixture.ResetProbes();
        fixture.Nodes[participant].Policy.DriftOnRead = finalRead;
        WorkspaceResearchTargetCompositionResult? published = null;
        var error = Assert.Throws<InvalidOperationException>(() =>
            published = ExecutePublicationPath(path, fixture, request));

        Assert.Contains("before composition publication", error.Message, StringComparison.Ordinal);
        Assert.Null(published);
        Assert.Equal(finalReads, fixture.Nodes.Select(node => node.Policy.Reads));
        // A later read returns the captured token; it must not rescue this invocation.
        Assert.Same(fixture.Group.BindingPolicyVersion, fixture.Nodes[participant].Policy.Version);
    }

    [Fact]
    public void WorkspaceResearchTarget_PublicationTailUsesOnlyInertLocals()
    {
        using var fixture = Forwarded();
        var plan = fixture.Plan();
        var request = plan.Request(fixture, terminal: 1);
        fixture.ResetProbes();
        Composed(Execute(request));
        int[] finalReads = fixture.Nodes.Select(node => node.Policy.Reads).ToArray();
        fixture.ResetProbes();
        var trace = new List<string>();
        bool sweeping = false;
        foreach (ImageNode node in fixture.Nodes)
        {
            node.Policy.Trace = trace;
            node.OnOpen = () =>
            {
                Assert.False(sweeping, "Acquisition callback after the final sweep started.");
                trace.Add("open");
            };
            node.Policy.OnSelect = () =>
                Assert.False(sweeping, "Binding-selection callback after the final sweep started.");
        }
        fixture.Nodes[0].Policy.OnVersion = read =>
        {
            if (read == finalReads[0])
            {
                sweeping = true;
                trace.Clear();
                trace.Add("version:0");
                // Releasing the live group here makes late image/group access observable;
                // the already-materialized receipt remains publishable.
                fixture.Group.Dispose();
            }
        };
        var receipt = Composed(Execute(request));
        Assert.True(sweeping);
        Assert.Equal(["version:0", "version:1"], trace);
        Assert.Equal(finalReads, fixture.Nodes.Select(node => node.Policy.Reads));
        Assert.Single(Resolved(receipt).Hops);

        // The concrete sealed owner types do not offer virtual getter probes. Inspect the
        // Release IL as well: the tail may read only the policy Version and local array.
        MethodInfo publisher = typeof(WorkspaceResearchTargetCompositionQuery)
            .GetMethod("Publish", BindingFlags.NonPublic | BindingFlags.Static)!;
        var instructions = Instructions(publisher).ToArray();
        var calls = instructions.Where(item => item.Method is not null).Select(item => item.Method!).ToArray();
        Assert.Contains(calls, method => method.DeclaringType == typeof(IAssemblyBindingPolicy)
            && method.Name == "get_Version");
        Assert.All(calls, method =>
        {
            bool permitted = method.DeclaringType == typeof(IAssemblyBindingPolicy) && method.Name == "get_Version"
                || method.DeclaringType == typeof(ImmutableArray<IAssemblyBindingPolicy>)
                    && method.Name is "get_Length" or "get_Item" or "GetEnumerator"
                || method.DeclaringType == typeof(ImmutableArray<IAssemblyBindingPolicy>.Enumerator)
                    && method.Name is "get_Current" or "MoveNext"
                || method.DeclaringType == typeof(object) && method.Name == nameof(ReferenceEquals)
                || method.DeclaringType == typeof(InvalidOperationException) && method.IsConstructor;
            Assert.True(permitted, $"Unexpected live publication-tail call: {method.DeclaringType}.{method.Name}");
        });
        Assert.DoesNotContain(instructions, item => item.Code == OpCodes.Ldfld || item.Code == OpCodes.Ldsfld);

        MethodInfo execute = typeof(WorkspaceResearchTargetCompositionQuery)
            .GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Static)!;
        var execution = Instructions(execute).ToArray();
        int publishIndex = Array.FindLastIndex(execution, item => item.Method == publisher);
        Assert.True(publishIndex >= 0);
        Assert.Equal(OpCodes.Ret, Assert.Single(execution.Skip(publishIndex + 1)).Code);
        Assert.Equal(7, execution.Count(item => item.Code == OpCodes.Ret));
    }

    [Fact]
    public void WorkspaceResearchTarget_PublishesNoPartialReceiptOnFailure()
    {
        foreach (PublicationPath path in Enum.GetValues<PublicationPath>().Where(path => path != PublicationPath.Composed))
        {
            using var fixture = CreatePublicationPath(path, out var request);
            var result = ExecutePublicationPath(path, fixture, request);
            AssertPublicationPath(path, result);
            Assert.IsNotType<WorkspaceResearchTargetCompositionResult.Composed>(result);
            Assert.DoesNotContain(result.GetType().GetProperties(),
                property => property.PropertyType == typeof(WorkspaceResearchTargetCompositionReceipt));
        }
        using var cancelledFixture = Direct();
        var plan = cancelledFixture.Plan();
        cancelledFixture.ResetProbes();
        WorkspaceResearchTargetCompositionResult? published = null;
        Assert.Throws<OperationCanceledException>(() => published =
            WorkspaceResearchTargetCompositionQuery.Execute(plan.Request(cancelledFixture),
                new CancellationToken(canceled: true)));
        Assert.Null(published);
        Assert.All(cancelledFixture.Nodes, node => Assert.Equal(0, node.Policy.Reads));
    }

    public enum PublicationPath
    {
        Composed,
        MetadataUnavailable,
        QueryRejected,
        UnsupportedBindingPolicy,
        TerminalAttemptMismatch,
        TerminalEvidenceMismatch,
        RootAttemptMismatch,
        BlockedTerminalCensus,
        TerminalAttemptUnavailable,
        TerminalParticipantMismatch,
        TerminalInputMismatch,
    }

    public static IEnumerable<object[]> PublicationPaths() =>
        Enum.GetValues<PublicationPath>().SelectMany(path =>
            new[] { new object[] { path, 0 }, new object[] { path, 1 } });

    [Fact]
    public void WorkspaceResearchTarget_PublicationPathManifestIsExhaustive()
    {
        Type[] resultArms = typeof(WorkspaceResearchTargetCompositionResult).Assembly.GetTypes()
            .Where(type => type.BaseType == typeof(WorkspaceResearchTargetCompositionResult)).ToArray();
        Assert.Equal(
            new[]
            {
                typeof(WorkspaceResearchTargetCompositionResult.Composed),
                typeof(WorkspaceResearchTargetCompositionResult.Rejected),
                typeof(WorkspaceResearchTargetCompositionResult.Unavailable),
            }.OrderBy(type => type.Name),
            resultArms.OrderBy(type => type.Name));

        WorkspaceResearchTargetCompositionRejection[] preQuery =
        [
            WorkspaceResearchTargetCompositionRejection.ForeignRoot,
            WorkspaceResearchTargetCompositionRejection.PopulationMismatch,
            WorkspaceResearchTargetCompositionRejection.PopulationReceiptMismatch,
            WorkspaceResearchTargetCompositionRejection.ResearchResolutionMismatch,
            WorkspaceResearchTargetCompositionRejection.UnsupportedRequestKind,
            WorkspaceResearchTargetCompositionRejection.UnsupportedResolutionScope,
        ];
        Dictionary<PublicationPath, WorkspaceResearchTargetCompositionRejection> postQuery = new()
        {
            [PublicationPath.UnsupportedBindingPolicy] = WorkspaceResearchTargetCompositionRejection.UnsupportedBindingPolicy,
            [PublicationPath.TerminalParticipantMismatch] = WorkspaceResearchTargetCompositionRejection.TerminalParticipantMismatch,
            [PublicationPath.TerminalInputMismatch] = WorkspaceResearchTargetCompositionRejection.TerminalInputMismatch,
            [PublicationPath.TerminalAttemptMismatch] = WorkspaceResearchTargetCompositionRejection.TerminalAttemptMismatch,
            [PublicationPath.TerminalEvidenceMismatch] = WorkspaceResearchTargetCompositionRejection.TerminalEvidenceMismatch,
            [PublicationPath.RootAttemptMismatch] = WorkspaceResearchTargetCompositionRejection.RootAttemptMismatch,
        };
        Assert.Equal(Enum.GetValues<WorkspaceResearchTargetCompositionRejection>().Order(),
            preQuery.Concat(postQuery.Values).Order());
        Dictionary<PublicationPath, WorkspaceResearchTargetCompositionUnavailability> unavailable = new()
        {
            [PublicationPath.QueryRejected] = WorkspaceResearchTargetCompositionUnavailability.ParticipantImageUnavailable,
            [PublicationPath.MetadataUnavailable] = WorkspaceResearchTargetCompositionUnavailability.MetadataResolutionUnavailable,
            [PublicationPath.BlockedTerminalCensus] = WorkspaceResearchTargetCompositionUnavailability.BlockedTerminalCensus,
            [PublicationPath.TerminalAttemptUnavailable] = WorkspaceResearchTargetCompositionUnavailability.TerminalAttemptUnavailable,
        };
        Assert.Equal(Enum.GetValues<WorkspaceResearchTargetCompositionUnavailability>().Order(),
            unavailable.Values.Order());
        Assert.Equal(Enum.GetValues<PublicationPath>().Order(),
            postQuery.Keys.Concat(unavailable.Keys).Append(PublicationPath.Composed).Order());
        Assert.Equal(Enum.GetValues<PublicationPath>().Length * 2, PublicationPaths().Count());
    }

    static WorkspaceResearchTargetCompositionResult ExecutePublicationPath(
        PublicationPath path, WorkspaceResearchTargetFixture fixture,
        WorkspaceResearchTargetCompositionRequest request)
    {
        if (path is not (PublicationPath.TerminalParticipantMismatch or PublicationPath.TerminalInputMismatch))
            return Execute(request);

        // These two defensive guards are unreachable through Execute: the retained-group
        // policy excludes foreign registrations and ValidateReceipt establishes an immutable
        // exact input bijection. Exercise the real join/publisher below that boundary with
        // owner-produced Metadata/Research evidence and a deliberately inconsistent map.
        // Reflection only invokes the private join; no result or owner object is mutated.
        AssemblyBindingPolicyVersion captured = fixture.Group.BindingPolicyVersion;
        ImmutableArray<IAssemblyBindingPolicy> policies = [.. fixture.Nodes.Select(node => node.Policy)];
        Assert.True(WorkspaceResearchTargetCompositionValidator.TryMapPopulation(
            request, fixture.Group.Participants, out var inputs));
        Assert.True(WorkspaceResearchTargetCompositionValidator.ValidateReceipt(request));
        Assert.True(WorkspaceResearchTargetCompositionValidator.ValidateResolution(request));
        var metadata = AssemblyContextTypeResolutionQuery.Execute(
            fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any);
        Assert.IsType<TypeResolutionOutcome.Resolved>(
            Assert.IsType<AssemblyContextTypeResolutionResult.Available>(metadata).Outcome);
        if (path == PublicationPath.TerminalParticipantMismatch)
            inputs.Remove(fixture.Nodes[1].Assembly.Registration);
        else
            inputs[fixture.Nodes[1].Assembly.Registration] =
                new QueryComparisonInputId(request.Question, request.Side);
        var projection = new WorkspaceProjectionContext(inputs);
        MethodInfo join = typeof(WorkspaceResearchTargetCompositionQuery)
            .GetMethod("ComposePending", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pending = Assert.IsAssignableFrom<WorkspaceResearchTargetCompositionResult>(
            join.Invoke(null, [request, inputs, fixture.Nodes[0].Assembly.Registration, metadata, projection]));
        return WorkspaceResearchTargetCompositionQuery.Publish(policies, captured, pending);
    }

    static WorkspaceResearchTargetFixture CreatePublicationPath(
        PublicationPath path, out WorkspaceResearchTargetCompositionRequest request)
    {
        WorkspaceResearchTargetFixture fixture;
        PlanningSession plan;
        switch (path)
        {
            case PublicationPath.MetadataUnavailable:
                fixture = new(BuildAssembly("MissingType", false), BuildAssembly("Unused", false));
                plan = fixture.Plan();
                break;
            case PublicationPath.QueryRejected:
                fixture = Direct();
                plan = fixture.Plan();
                fixture.Nodes[1].OnOpen = () => throw new IOException("final-publication-open-failure");
                break;
            case PublicationPath.UnsupportedBindingPolicy:
                fixture = Direct();
                plan = fixture.Plan();
                var group = fixture.CreateGroup(fixture.Nodes.Select((node, index) =>
                    new AssemblyContextParticipant(node.Assembly,
                        index == 0 ? new UnattestedPolicy(node.Policy) : node.Policy)));
                request = plan.Request(fixture, group: group);
                return fixture;
            case PublicationPath.TerminalEvidenceMismatch:
                fixture = Direct();
                plan = fixture.Plan();
                fixture.Nodes[0].Image = BuildAssembly("Direct");
                break;
            case PublicationPath.RootAttemptMismatch:
                fixture = new(BuildAssembly("Facade", false), BuildAssembly("Terminal"));
                plan = fixture.Plan();
                fixture.Nodes[0].Image = BuildAssembly("Facade", false, fixture.Nodes[1].Assembly.Identity);
                request = plan.Request(fixture, terminal: 1);
                return fixture;
            case PublicationPath.BlockedTerminalCensus:
                fixture = Direct();
                plan = fixture.Plan(referenceOnly: 0);
                break;
            case PublicationPath.TerminalAttemptUnavailable:
                fixture = Direct();
                plan = fixture.Plan(selector: MemberTargetSelector.Parse("Absent"));
                break;
            case PublicationPath.TerminalAttemptMismatch:
                fixture = Direct();
                plan = fixture.Plan();
                request = plan.Request(fixture, terminal: 1);
                return fixture;
            case PublicationPath.Composed:
                fixture = Direct();
                plan = fixture.Plan();
                break;
            case PublicationPath.TerminalParticipantMismatch:
            case PublicationPath.TerminalInputMismatch:
                fixture = Forwarded();
                plan = fixture.Plan();
                request = plan.Request(fixture, terminal: 1);
                return fixture;
            default:
                throw new ArgumentOutOfRangeException(nameof(path));
        }
        request = plan.Request(fixture);
        return fixture;
    }

    static void AssertPublicationPath(PublicationPath path, WorkspaceResearchTargetCompositionResult result)
    {
        switch (path)
        {
            case PublicationPath.Composed:
                Composed(result);
                break;
            case PublicationPath.TerminalAttemptMismatch:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.TerminalAttemptMismatch);
                break;
            case PublicationPath.UnsupportedBindingPolicy:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.UnsupportedBindingPolicy);
                break;
            case PublicationPath.TerminalEvidenceMismatch:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.TerminalEvidenceMismatch);
                break;
            case PublicationPath.RootAttemptMismatch:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.RootAttemptMismatch);
                break;
            case PublicationPath.TerminalParticipantMismatch:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.TerminalParticipantMismatch);
                break;
            case PublicationPath.TerminalInputMismatch:
                AssertRejected(result, WorkspaceResearchTargetCompositionRejection.TerminalInputMismatch);
                break;
            default:
                var unavailable = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(result);
                Assert.Equal(path switch
                {
                    PublicationPath.MetadataUnavailable => WorkspaceResearchTargetCompositionUnavailability.MetadataResolutionUnavailable,
                    PublicationPath.QueryRejected => WorkspaceResearchTargetCompositionUnavailability.ParticipantImageUnavailable,
                    PublicationPath.BlockedTerminalCensus => WorkspaceResearchTargetCompositionUnavailability.BlockedTerminalCensus,
                    PublicationPath.TerminalAttemptUnavailable => WorkspaceResearchTargetCompositionUnavailability.TerminalAttemptUnavailable,
                    _ => throw new ArgumentOutOfRangeException(nameof(path)),
                }, unavailable.Reason);
                break;
        }
    }

    static void AssertQueryDrift(int participant, bool postQuery)
    {
        using var fixture = Direct();
        fixture.RetainAll();
        fixture.ResetProbes();
        AssemblyContextTypeResolutionResult Run() => AssemblyContextTypeResolutionQuery.Execute(
            fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any);
        Assert.IsType<AssemblyContextTypeResolutionResult.Available>(Run());
        int lastRead = fixture.Nodes[participant].Policy.Reads;
        foreach (int read in postQuery ? new[] { lastRead - 1, lastRead } : [1])
        {
            fixture.ResetProbes();
            fixture.Nodes[participant].Policy.DriftOnRead = read;
            AssemblyContextTypeResolutionResult? published = null;
            Assert.Throws<InvalidOperationException>(() => published = Run());
            Assert.Null(published);
            Assert.Same(fixture.Group.BindingPolicyVersion, fixture.Nodes[participant].Policy.Version);
        }
    }

    static void AssertCompositionDrift(int participant, bool postQuery)
    {
        using var fixture = Direct();
        var plan = fixture.Plan();
        var request = plan.Request(fixture);
        fixture.ResetProbes();
        Composed(Execute(request));
        int publicationRead = fixture.Nodes[participant].Policy.Reads;
        fixture.ResetProbes();
        int driftRead = postQuery ? publicationRead - 1 : 1;
        fixture.Nodes[participant].Policy.DriftOnRead = driftRead;
        WorkspaceResearchTargetCompositionResult? published = null;
        var error = Assert.Throws<InvalidOperationException>(() => published = Execute(request));
        Assert.Equal("A participant binding-policy snapshot changed during composition.", error.Message);
        Assert.Null(published);
        Assert.Equal(driftRead, fixture.Nodes[participant].Policy.Reads);
        Assert.Same(fixture.Group.BindingPolicyVersion, fixture.Nodes[participant].Policy.Version);
    }

    static IEnumerable<(OpCode Code, MethodBase? Method)> Instructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        Dictionary<short, OpCode> opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);
        for (int position = 0; position < il.Length;)
        {
            short value = il[position++];
            if (value == 0xfe)
                value = unchecked((short)(0xfe00 | il[position++]));
            OpCode opcode = opcodes[value];
            MethodBase? target = opcode.OperandType == OperandType.InlineMethod
                ? method.Module.ResolveMethod(BitConverter.ToInt32(il, position)) : null;
            int length = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, position) * 4,
                _ => 4,
            };
            position += length;
            yield return (opcode, target);
        }
    }

    static WorkspaceResearchTargetFixture Direct() =>
        new(BuildAssembly("Direct"), BuildAssembly("Unrelated", false));

    static WorkspaceResearchTargetFixture Forwarded()
    {
        byte[] terminal = BuildAssembly("Terminal");
        return new(BuildAssembly("Facade", false, Identity(terminal)), terminal);
    }

    static WorkspaceResearchTargetCompositionResult Execute(WorkspaceResearchTargetCompositionRequest request) =>
        WorkspaceResearchTargetCompositionQuery.Execute(request);

    static WorkspaceResearchTargetCompositionReceipt Composed(WorkspaceResearchTargetCompositionResult result) =>
        Assert.IsType<WorkspaceResearchTargetCompositionResult.Composed>(result).Receipt;

    static WorkspaceMetadataEvidence.Outcome.Resolved Resolved(WorkspaceResearchTargetCompositionReceipt receipt) =>
        Assert.IsType<WorkspaceMetadataEvidence.Outcome.Resolved>(receipt.Evidence.Outcome);

    static WorkspaceResearchTargetCompositionReceipt ComposePublic(
        WorkspaceResearchTargetFixture fixture, WorkspaceResearchTargetPlan plan, int terminal,
        QueryComparisonSide side = QueryComparisonSide.Before)
    {
        string assembly = fixture.Nodes[terminal].Assembly.Identity.Name;
        var domain = plan.Scope.Domains.Single(item => item.Key.Identity.Name == assembly);
        var census = plan.Resolution.Censuses.Single(item => item.Domain == domain
            && item.Side == QueryPopulationProjection.ResearchSide(side));
        return Composed(plan.Compose(fixture.Group, fixture.Nodes[0].Participant, side, domain, census,
            AssemblyResolutionScope.Any));
    }

    static void AssertRejected(WorkspaceResearchTargetCompositionResult result,
        WorkspaceResearchTargetCompositionRejection reason) =>
        Assert.Equal(reason, Assert.IsType<WorkspaceResearchTargetCompositionResult.Rejected>(result).Reason);

    static void AssertPreQueryRejected(WorkspaceResearchTargetFixture fixture,
        WorkspaceResearchTargetCompositionRequest request, WorkspaceResearchTargetCompositionRejection reason)
    {
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();
        var result = Execute(request);
        AssertRejected(result, reason);
        Assert.Null(Assert.IsType<WorkspaceResearchTargetCompositionResult.Rejected>(result).Evidence);
        Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Policy.Reads));
        Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Policy.Selections));
        Assert.Equal(opens, fixture.Nodes.Select(node => node.Opens));
    }
}
