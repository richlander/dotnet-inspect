using System.Collections.Immutable;
using System.Reflection;

namespace DotnetInspector.Queries.Tests;

public sealed class AnalysisUniverseRealizationTests
{
    [Fact]
    public void AnalysisUniverseRealization_RequiresExactDescription()
    {
        using var workspace = new InspectionWorkspace();
        TestFixture first = TestFixture.Create(workspace);
        TestFixture second = TestFixture.Create(workspace);

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                first.Offer.IssueExecutionAccess(
                    second.Plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.DescriptionMismatch,
            rejected.Rejection.Reason);
    }

    [Fact]
    public void AnalysisUniverseRealization_RejectsForeignProviderOffer()
    {
        using var owner = new InspectionWorkspace();
        using var foreign = new InspectionWorkspace();
        TestFixture fixture = TestFixture.Create(owner);

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                foreign.IssueAnalysisUniverseExecutionAccess(
                    fixture.Offer,
                    fixture.Plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.ForeignProviderOffer,
            rejected.Rejection.Reason);
    }

    [Fact]
    public void AnalysisUniverseRealization_RejectsLookalikeCapabilityIdentity()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("lookalike");
        AnalysisUniverseCapabilityDescriptor lookalike =
            Capability("lookalike");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("lookalike", capability);
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [Registration(lookalike)]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                offer.IssueExecutionAccess(
                    plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.WrongCapabilityIdentity,
            rejected.Rejection.Reason);
        Assert.Same(requirement, rejected.Rejection.Requirement);
        Assert.Same(capability, rejected.Rejection.Capability);
    }

    [Fact]
    public void AnalysisUniverseRealization_BindsEveryPlanRequirementExactlyOnce()
    {
        using var workspace = new InspectionWorkspace();
        TestFixture fixture = TestFixture.Create(workspace);

        using AnalysisUniverseExecutionAccess access =
            Ready(fixture.Offer.IssueExecutionAccess(
                fixture.Plan,
                Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(fixture.Plan.UniverseRequirements.Length, access.Bindings.Length);
        Assert.All(
            fixture.Plan.UniverseRequirements.Select(
                (requirement, index) => (requirement, index)),
            entry => Assert.Same(
                entry.requirement,
                access.Bindings[entry.index].Requirement));
    }

    [Fact]
    public void AnalysisUniverseRealization_OneCapabilityMayBackSeveralRequirements()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("shared");
        AnalysisUniverseRequirementDescriptor first =
            Requirement("first", capability);
        AnalysisUniverseRequirementDescriptor second =
            Requirement("second", capability);
        int acquisitions = 0;
        var ownerAccess = new TestAccess();
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [
                Registration(
                    capability,
                    () =>
                    {
                        acquisitions++;
                        return ownerAccess;
                    }),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [first, second]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(1, acquisitions);
        Assert.Same(
            ownerAccess,
            access.GetBinding<TestAccess>(first).Access);
        Assert.Same(
            ownerAccess,
            access.GetBinding<TestAccess>(second).Access);
    }

    [Fact]
    public void AnalysisUniverseRealization_RejectsExtraneousExecutableBinding()
    {
        AnalysisUniverseCapabilityDescriptor expectedCapability =
            Capability("expected");
        AnalysisUniverseCapabilityDescriptor extraCapability =
            Capability("extra");
        AnalysisUniverseRequirementDescriptor expected =
            Requirement("expected", expectedCapability);
        AnalysisUniverseRequirementDescriptor extra =
            Requirement("extra", extraCapability);
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [expectedCapability],
            [Registration(expectedCapability)]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [expected]);
        int releases = 0;
        var handle = new AnalysisUniverseCapabilityHandle<TestAccess>(
            extraCapability,
            new AnalysisUniverseCapabilityLease<TestAccess>(
                new TestAccess(),
                () => releases++));
        var state = new AnalysisUniverseAccessState();

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                AnalysisUniverseExecutionAccess.Create(
                    plan,
                    offer.Realization,
                    [handle.CreateBinding(extra, state)],
                    [handle],
                    state));

        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.ExtraneousExecutableBinding,
            rejected.Rejection.Reason);
        Assert.Same(extra, rejected.Rejection.Requirement);
        Assert.Equal(1, releases);
    }

    [Fact]
    public void AnalysisUniverseRealization_PreservesProviderOrderAndFailures()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("ordered");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("ordered", capability);
        var firstFailure = new TestUniverseFailure("first");
        var secondFailure = new TestUniverseFailure("second");
        var ownerAccess = new TestAccess(
            population: ["third", "first", "second"]);
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [Registration(capability, () => ownerAccess)],
            failures: [firstFailure, secondFailure]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            ["third", "first", "second"],
            access.GetBinding<TestAccess>(requirement).Access.Population);
        Assert.Equal(
            [firstFailure, secondFailure],
            offer.Description.Failures);
    }

    [Fact]
    public void AnalysisUniverseRealization_UsesOwnerIssuedContextIdentity()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("contexts");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("contexts", capability);
        var first = new OwnerContext("context-a", "policy-1");
        var second = new OwnerContext("context-b", "policy-2");
        var ownerAccess = new TestAccess(contexts: [second, first]);
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [Registration(capability, () => ownerAccess)]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));
        ImmutableArray<OwnerContext> contexts =
            access.GetBinding<TestAccess>(requirement).Access.Contexts;

        Assert.Same(second, contexts[0]);
        Assert.Same(first, contexts[1]);
    }

    [Fact]
    public void AnalysisUniverseRealization_DoesNotUseBindingPolicyVersionAsContextIdentity()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("contexts");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("contexts", capability);
        var first = new OwnerContext("context-a", "policy-shared");
        var second = new OwnerContext("context-b", "policy-shared");
        var ownerAccess = new TestAccess(contexts: [first, second]);
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [Registration(capability, () => ownerAccess)]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));
        ImmutableArray<OwnerContext> contexts =
            access.GetBinding<TestAccess>(requirement).Access.Contexts;

        Assert.Equal(2, contexts.Length);
        Assert.NotSame(contexts[0], contexts[1]);
        Assert.Equal(contexts[0].PolicyVersion, contexts[1].PolicyVersion);
        Assert.NotEqual(contexts[0].Id, contexts[1].Id);
    }

    [Fact]
    public void AnalysisUniverseRealization_PreservesPopulationContextIncidence()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("incidence");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("incidence", capability);
        var first = new OwnerContext("context-a", "policy-1");
        var second = new OwnerContext("context-b", "policy-2");
        var ownerAccess = new TestAccess(
            population: ["participant-a", "participant-b"],
            contexts: [first, second],
            incidence:
            [
                new TestIncidence("participant-a", second),
                new TestIncidence("participant-b", first),
            ]);
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [Registration(capability, () => ownerAccess)]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));
        ImmutableArray<TestIncidence> incidence =
            access.GetBinding<TestAccess>(requirement).Access.Incidence;

        Assert.Equal(2, incidence.Length);
        Assert.Equal("participant-a", incidence[0].Participant);
        Assert.Same(second, incidence[0].Context);
        Assert.Equal("participant-b", incidence[1].Participant);
        Assert.Same(first, incidence[1].Context);
    }

    [Fact]
    public void AnalysisUniverseRealization_KeepsOwnerAccessAliveUntilRelease()
    {
        using var workspace = new InspectionWorkspace();
        int releases = 0;
        TestFixture fixture = TestFixture.Create(
            workspace,
            release: () => releases++);
        AnalysisUniverseExecutionAccess access =
            Ready(fixture.Offer.IssueExecutionAccess(
                fixture.Plan,
                Xunit.TestContext.Current.CancellationToken));
        AnalysisUniverseRequirementBinding<TestAccess> binding =
            access.GetBinding<TestAccess>(fixture.Requirements[0]);

        Assert.NotNull(binding.Access);
        Assert.Equal(0, releases);

        access.Dispose();
        access.Dispose();

        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => binding.Access);
    }

    [Fact]
    public void AnalysisUniverseRealization_IdentityValuesSurviveAccessReleaseWithinScope()
    {
        using var workspace = new InspectionWorkspace();
        var identity = new OwnerContext("context-a", "policy-1");
        TestFixture fixture = TestFixture.Create(
            workspace,
            access: new TestAccess(contexts: [identity]));
        AnalysisUniverseExecutionAccess access =
            Ready(fixture.Offer.IssueExecutionAccess(
                fixture.Plan,
                Xunit.TestContext.Current.CancellationToken));
        OwnerContext retained = access
            .GetBinding<TestAccess>(fixture.Requirements[0])
            .Access
            .Contexts[0];

        access.Dispose();

        Assert.Same(identity, retained);
        Assert.Equal("context-a", retained.Id);
    }

    [Fact]
    public void AnalysisUniverseRealization_RejectedIssuanceReleasesPartialAccess()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor firstCapability =
            Capability("first");
        AnalysisUniverseCapabilityDescriptor secondCapability =
            Capability("second");
        AnalysisUniverseRequirementDescriptor first =
            Requirement("first", firstCapability);
        AnalysisUniverseRequirementDescriptor second =
            Requirement("second", secondCapability);
        int releases = 0;
        var capabilityFailure = new TestCapabilityFailure("denied");
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [firstCapability, secondCapability],
            [
                Registration(
                    firstCapability,
                    release: () => releases++),
                new AnalysisUniverseCapabilityRegistration<TestAccess>(
                    secondCapability,
                    (_, _) =>
                        new AnalysisUniverseCapabilityAcquisition<TestAccess>
                            .Rejected(
                                new AnalysisUniverseCapabilityRejection(
                                    AnalysisUniverseCapabilityRejectionReason
                                        .AuthorizationDenied,
                                    capabilityFailure))),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [first, second]);

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                offer.IssueExecutionAccess(
                    plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(1, releases);
        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.CapabilityRejected,
            rejected.Rejection.Reason);
        Assert.Same(
            capabilityFailure,
            rejected.Rejection.CapabilityRejection!.Failure);
    }

    [Fact]
    public void AnalysisUniverseRealization_CancellationReleasesPartialAccess()
    {
        using var workspace = new InspectionWorkspace();
        using var cancellation = new CancellationTokenSource();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("cancelled");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("cancelled", capability);
        int releases = 0;
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [
                Registration(
                    capability,
                    () =>
                    {
                        cancellation.Cancel();
                        return new TestAccess();
                    },
                    () => releases++),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        Assert.IsType<AnalysisUniverseIssuanceResult.Cancelled>(
            offer.IssueExecutionAccess(plan, cancellation.Token));

        Assert.Equal(1, releases);

        using var secondWorkspace = new InspectionWorkspace();
        using var secondCancellation = new CancellationTokenSource();
        AnalysisUniverseCapabilityDescriptor firstCapability =
            Capability("first");
        AnalysisUniverseCapabilityDescriptor secondCapability =
            Capability("second");
        AnalysisUniverseRequirementDescriptor first =
            Requirement("first", firstCapability);
        AnalysisUniverseRequirementDescriptor second =
            Requirement("second", secondCapability);
        int firstReleases = 0;
        int secondAcquisitions = 0;
        AnalysisUniverseOffer secondOffer = CreateOffer(
            secondWorkspace,
            [firstCapability, secondCapability],
            [
                Registration(
                    firstCapability,
                    () =>
                    {
                        secondCancellation.Cancel();
                        return new TestAccess();
                    },
                    () => firstReleases++),
                Registration(
                    secondCapability,
                    () =>
                    {
                        secondAcquisitions++;
                        return new TestAccess();
                    }),
            ]);
        AnalysisRequestPlan secondPlan = CreatePlan(
            secondOffer.Description,
            [first, second]);

        Assert.IsType<AnalysisUniverseIssuanceResult.Cancelled>(
            secondOffer.IssueExecutionAccess(
                secondPlan,
                secondCancellation.Token));

        Assert.Equal(1, firstReleases);
        Assert.Equal(0, secondAcquisitions);
    }

    [Fact]
    public void AnalysisUniverseRealization_CancellationTakesPrecedenceOverCapabilityRejection()
    {
        using var workspace = new InspectionWorkspace();
        using var cancellation = new CancellationTokenSource();
        AnalysisUniverseCapabilityDescriptor firstCapability =
            Capability("first");
        AnalysisUniverseCapabilityDescriptor secondCapability =
            Capability("second");
        AnalysisUniverseRequirementDescriptor first =
            Requirement("first", firstCapability);
        AnalysisUniverseRequirementDescriptor second =
            Requirement("second", secondCapability);
        int releases = 0;
        var failure = new TestCapabilityFailure("denied");
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [firstCapability, secondCapability],
            [
                Registration(
                    firstCapability,
                    release: () => releases++),
                new AnalysisUniverseCapabilityRegistration<TestAccess>(
                    secondCapability,
                    (_, _) =>
                    {
                        cancellation.Cancel();
                        return new AnalysisUniverseCapabilityAcquisition<
                            TestAccess>.Rejected(
                                new AnalysisUniverseCapabilityRejection(
                                    AnalysisUniverseCapabilityRejectionReason
                                        .AuthorizationDenied,
                                    failure));
                    }),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [first, second]);

        Assert.IsType<AnalysisUniverseIssuanceResult.Cancelled>(
            offer.IssueExecutionAccess(plan, cancellation.Token));

        Assert.Equal(1, releases);
    }

    [Fact]
    public void AnalysisUniverseRealization_CloseDuringIssuancePublishesNoPartialAccess()
    {
        var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("closing");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("closing", capability);
        int releases = 0;
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [
                Registration(
                    capability,
                    () =>
                    {
                        workspace.Dispose();
                        return new TestAccess();
                    },
                    () => releases++),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                offer.IssueExecutionAccess(
                    plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.WorkspaceUnavailable,
            rejected.Rejection.Reason);
        Assert.Equal(1, releases);
    }

    [Fact]
    public void AnalysisUniverseRealization_DoesNotCacheAuthorizationOrPinMetadataGeneration()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor capability =
            Capability("authorization");
        AnalysisUniverseRequirementDescriptor requirement =
            Requirement("authorization", capability);
        int authorizations = 0;
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [capability],
            [
                Registration(
                    capability,
                    () => new TestAccess(
                        generation: $"generation-{++authorizations}")),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [requirement]);

        string first;
        using (AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken)))
        {
            first = access
                .GetBinding<TestAccess>(requirement)
                .Access
                .Generation;
        }
        string second;
        using (AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken)))
        {
            second = access
                .GetBinding<TestAccess>(requirement)
                .Access
                .Generation;
        }

        Assert.Equal(2, authorizations);
        Assert.Equal("generation-1", first);
        Assert.Equal("generation-2", second);
    }

    [Fact]
    public void AnalysisUniverseRealization_DoesNotExposeMutableWorkspace()
    {
        Type[] publicTypes =
        [
            typeof(AnalysisUniverseOffer),
            typeof(AnalysisUniverseRealization),
            typeof(AnalysisUniverseExecutionAccess),
            typeof(AnalysisUniverseRequirementBinding),
            typeof(AnalysisUniverseRequirementBinding<>),
        ];
        Type[] forbidden =
        [
            typeof(InspectionWorkspace),
            typeof(AssemblyContextGroup),
        ];

        foreach (Type type in publicTypes)
        {
            IEnumerable<Type> exposed = type
                .GetMembers(
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .SelectMany(MemberTypes)
                .SelectMany(FlattenTypeShape);

            Assert.DoesNotContain(exposed, forbidden.Contains);
        }
    }

    [Fact]
    public void AnalysisUniverseRealization_CompatiblePlansRequireIndependentAuthorization()
    {
        using var workspace = new InspectionWorkspace();
        int authorizations = 0;
        TestFixture fixture = TestFixture.Create(
            workspace,
            onAcquire: () => authorizations++);
        AnalysisRequestPlan compatible = CreatePlan(
            fixture.Offer.Description,
            fixture.Requirements);

        using AnalysisUniverseExecutionAccess first =
            Ready(fixture.Offer.IssueExecutionAccess(
                fixture.Plan,
                Xunit.TestContext.Current.CancellationToken));
        using AnalysisUniverseExecutionAccess second =
            Ready(fixture.Offer.IssueExecutionAccess(
                compatible,
                Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(2, authorizations);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AnalysisUniverseRealization_WiderBoundaryRequiresNewRealization()
    {
        using var workspace = new InspectionWorkspace();
        TestFixture narrow = TestFixture.Create(
            workspace,
            boundary: new TestBoundary("narrow"));
        AnalysisUniverseOffer wider = CreateOffer(
            workspace,
            narrow.Capabilities,
            narrow.Registrations,
            boundary: new TestBoundary("wide"));

        AnalysisUniverseIssuanceResult.Rejected rejected =
            Assert.IsType<AnalysisUniverseIssuanceResult.Rejected>(
                wider.IssueExecutionAccess(
                    narrow.Plan,
                    Xunit.TestContext.Current.CancellationToken));

        Assert.NotSame(narrow.Offer.Realization, wider.Realization);
        Assert.Equal(
            AnalysisUniverseIssuanceRejectionReason.DescriptionMismatch,
            rejected.Rejection.Reason);
    }

    [Fact]
    public void AnalysisUniverseRealization_SequentialExecutionUsesDeclaredOrder()
    {
        using var workspace = new InspectionWorkspace();
        AnalysisUniverseCapabilityDescriptor firstCapability =
            Capability("first");
        AnalysisUniverseCapabilityDescriptor secondCapability =
            Capability("second");
        AnalysisUniverseRequirementDescriptor first =
            Requirement("first", firstCapability);
        AnalysisUniverseRequirementDescriptor second =
            Requirement("second", secondCapability);
        var order = new List<string>();
        AnalysisUniverseOffer offer = CreateOffer(
            workspace,
            [firstCapability, secondCapability],
            [
                Registration(
                    firstCapability,
                    () =>
                    {
                        order.Add("first");
                        return new TestAccess();
                    }),
                Registration(
                    secondCapability,
                    () =>
                    {
                        order.Add("second");
                        return new TestAccess();
                    }),
            ]);
        AnalysisRequestPlan plan = CreatePlan(
            offer.Description,
            [first, second]);

        using AnalysisUniverseExecutionAccess access =
            Ready(offer.IssueExecutionAccess(
                plan,
                Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(["first", "second"], order);
        Assert.Equal([first, second], access.Bindings.Select(b => b.Requirement));
    }

    [Fact]
    public void AnalysisUniverseRealization_HasNoThreadingRequirement()
    {
        Type[] publicTypes =
        [
            typeof(AnalysisUniverseOffer),
            typeof(AnalysisUniverseRealization),
            typeof(AnalysisUniverseExecutionAccess),
            typeof(AnalysisUniverseCapabilityRegistration),
            typeof(AnalysisUniverseCapabilityRegistration<>),
            typeof(AnalysisUniverseCapabilityLease<>),
        ];
        Type[] forbidden =
        [
            typeof(Thread),
            typeof(Task),
            typeof(ValueTask),
        ];

        IEnumerable<Type> exposed = publicTypes
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            .SelectMany(MemberTypes)
            .SelectMany(FlattenTypeShape);

        Assert.DoesNotContain(exposed, forbidden.Contains);
    }

    static AnalysisUniverseCapabilityDescriptor Capability(
        string name) =>
        new(new AnalysisDeclarationId($"capability.{name}"), name);

    static AnalysisUniverseRequirementDescriptor Requirement(
        string name,
        AnalysisUniverseCapabilityDescriptor capability) =>
        new(
            new AnalysisDeclarationId($"requirement.{name}"),
            capability,
            [AnalysisQuestionMode.Census]);

    static AnalysisUniverseCapabilityRegistration<TestAccess> Registration(
        AnalysisUniverseCapabilityDescriptor capability,
        Func<TestAccess>? acquire = null,
        Action? release = null) =>
        new(
            capability,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new AnalysisUniverseCapabilityAcquisition<TestAccess>
                    .Ready(
                        new AnalysisUniverseCapabilityLease<TestAccess>(
                            acquire?.Invoke() ?? new TestAccess(),
                            release ?? (() => { })));
            });

    static AnalysisUniverseOffer CreateOffer(
        InspectionWorkspace workspace,
        IEnumerable<AnalysisUniverseCapabilityDescriptor> capabilities,
        IEnumerable<AnalysisUniverseCapabilityRegistration> registrations,
        TestBoundary? boundary = null,
        IEnumerable<IAnalysisUniverseFailure>? failures = null) =>
        workspace.CreateAnalysisUniverseOffer(
            new TestUniverseIdentity("universe"),
            boundary ?? new TestBoundary("requested"),
            boundary ?? new TestBoundary("realized"),
            capabilities,
            new TestCompleteness("complete"),
            registrations,
            failures);

    static AnalysisRequestPlan CreatePlan(
        AnalysisUniverseDescription universe,
        IEnumerable<AnalysisUniverseRequirementDescriptor> requirements)
    {
        var role = new AnalysisTargetRoleDescriptor(
            new AnalysisDeclarationId("target.workspace"),
            AnalysisTargetFunction.ReportDomain,
            minimumCount: 1,
            maximumCount: 1);
        var projection = new AnalysisProjectionDescriptor(
            new AnalysisDeclarationId("projection.rows"));
        var analysis = new AnalysisDescriptor(
            new AnalysisDeclarationId("analysis.realization-test"),
            revision: 1,
            InspectionCost.NetworkFree,
            [AnalysisQuestionMode.Census],
            [
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    [role]),
            ],
            requirements,
            [],
            [],
            [
                new AnalysisProjectionSupport(
                    projection,
                    [AnalysisQuestionMode.Census]),
            ]);
        var surface = new AnalysisReportSurface(
            AnalysisReportSurfaceKind.Workspace,
            new TestSurfaceIdentity("workspace"),
            [
                new AnalysisTargetBinding(
                    role,
                    new TestTargetIdentity("workspace")),
            ]);
        var request = new AnalysisRequest(
            analysis,
            surface,
            universe,
            AnalysisQuestionMode.Census,
            projection);

        return Assert
            .IsType<AnalysisRequestPlanResult.Accepted>(
                new AnalysisCapabilityCatalog([analysis])
                    .Plan(request, new AnalysisPlanningEnvironment()))
            .Plan;
    }

    static AnalysisUniverseExecutionAccess Ready(
        AnalysisUniverseIssuanceResult result) =>
        Assert.IsType<AnalysisUniverseIssuanceResult.Ready>(result).Access;

    static IEnumerable<Type> MemberTypes(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => [property.PropertyType],
            MethodInfo method =>
            [
                method.ReturnType,
                .. method.GetParameters().Select(parameter =>
                    parameter.ParameterType),
            ],
            ConstructorInfo constructor =>
            [
                .. constructor.GetParameters().Select(parameter =>
                    parameter.ParameterType),
            ],
            _ => [],
        };

    static IEnumerable<Type> FlattenTypeShape(Type type)
    {
        yield return type.IsGenericType
            ? type.GetGenericTypeDefinition()
            : type;
        if (type.HasElementType)
        {
            foreach (Type element in FlattenTypeShape(type.GetElementType()!))
                yield return element;
        }
        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in FlattenTypeShape(argument))
                yield return nested;
        }
    }

    sealed class TestFixture
    {
        TestFixture(
            AnalysisUniverseOffer offer,
            ImmutableArray<AnalysisUniverseCapabilityDescriptor> capabilities,
            ImmutableArray<AnalysisUniverseRequirementDescriptor> requirements,
            ImmutableArray<AnalysisUniverseCapabilityRegistration> registrations,
            AnalysisRequestPlan plan)
        {
            Offer = offer;
            Capabilities = capabilities;
            Requirements = requirements;
            Registrations = registrations;
            Plan = plan;
        }

        internal AnalysisUniverseOffer Offer { get; }
        internal ImmutableArray<AnalysisUniverseCapabilityDescriptor>
            Capabilities { get; }
        internal ImmutableArray<AnalysisUniverseRequirementDescriptor>
            Requirements { get; }
        internal ImmutableArray<AnalysisUniverseCapabilityRegistration>
            Registrations { get; }
        internal AnalysisRequestPlan Plan { get; }

        internal static TestFixture Create(
            InspectionWorkspace workspace,
            TestBoundary? boundary = null,
            TestAccess? access = null,
            Action? release = null,
            Action? onAcquire = null)
        {
            AnalysisUniverseCapabilityDescriptor capability =
                Capability("fixture");
            ImmutableArray<AnalysisUniverseCapabilityDescriptor>
                capabilities = [capability];
            ImmutableArray<AnalysisUniverseRequirementDescriptor>
                requirements = [Requirement("fixture", capability)];
            ImmutableArray<AnalysisUniverseCapabilityRegistration>
                registrations =
            [
                Registration(
                    capability,
                    () =>
                    {
                        onAcquire?.Invoke();
                        return access ?? new TestAccess();
                    },
                    release),
            ];
            AnalysisUniverseOffer offer = CreateOffer(
                workspace,
                capabilities,
                registrations,
                boundary);
            return new TestFixture(
                offer,
                capabilities,
                requirements,
                registrations,
                CreatePlan(offer.Description, requirements));
        }
    }

    sealed record TestUniverseIdentity(string Value)
        : IAnalysisUniverseIdentity;

    sealed record TestBoundary(string Value)
        : IAnalysisUniverseBoundary;

    sealed record TestCompleteness(string Value)
        : IAnalysisUniverseCompleteness;

    sealed record TestUniverseFailure(string Value)
        : IAnalysisUniverseFailure;

    sealed record TestCapabilityFailure(string Value)
        : IAnalysisUniverseCapabilityFailure;

    sealed record TestSurfaceIdentity(string Value)
        : IAnalysisReportSurfaceIdentity;

    sealed record TestTargetIdentity(string Value)
        : IAnalysisTargetIdentity;

    sealed record OwnerContext(
        string Id,
        string PolicyVersion);

    sealed record TestIncidence(
        string Participant,
        OwnerContext Context);

    sealed class TestAccess
    {
        internal TestAccess(
            IEnumerable<string>? population = null,
            IEnumerable<OwnerContext>? contexts = null,
            IEnumerable<TestIncidence>? incidence = null,
            string generation = "generation-1")
        {
            Population = population is null ? [] : [.. population];
            Contexts = contexts is null ? [] : [.. contexts];
            Incidence = incidence is null ? [] : [.. incidence];
            Generation = generation;
        }

        internal ImmutableArray<string> Population { get; }
        internal ImmutableArray<OwnerContext> Contexts { get; }
        internal ImmutableArray<TestIncidence> Incidence { get; }
        internal string Generation { get; }
    }
}
