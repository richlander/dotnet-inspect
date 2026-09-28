using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Release gates for the first Producer Planning adoption
/// (docs/design/library-body-analysis-service.md#producer-planning-adoption).
/// </summary>
public sealed class ProducerPlanningTests
{
    [Fact]
    public void Presence_DescriptionIsPlannedWithoutASubject()
    {
        WorkDescription description = UnsafeEvidencePresence.Description;

        ProducerDeclaration producer = Assert.Single(description.Producers);
        Assert.Same(UnsafeEvidencePresenceProducer.Instance, producer);
        Assert.Equal(1, description.PassCount);
        Assert.Equal(
            ProducerTerminal.Exists,
            description.TerminalOf(producer));
        Assert.True(description.WasRequested(producer));
    }

    [Fact]
    public void Planner_RejectsMissingDependency()
    {
        var producer = new CountingProducer(
            "HasMissing",
            dependencies: () =>
                [new ProducerDependency(null!, ProducerDependencyKind.CompletionNeedsResult)]);

        AssertRejected(
            ProducerPlanner.Plan([new(producer)]),
            ("HasMissing", ProducerRejectionReason.MissingDependency));
    }

    [Fact]
    public void Planner_RejectsDependencyCycle()
    {
        CountingProducer? second = null;
        var first = new CountingProducer(
            "CycleA",
            dependencies: () =>
                [new ProducerDependency(second!, ProducerDependencyKind.CompletionNeedsResult)]);
        second = new CountingProducer(
            "CycleB",
            dependencies: () =>
                [new ProducerDependency(first, ProducerDependencyKind.CompletionNeedsResult)]);

        ProducerPlanResult.Rejected rejected =
            Assert.IsType<ProducerPlanResult.Rejected>(
                ProducerPlanner.Plan([new(first)]));
        Assert.Contains(
            rejected.Reasons,
            reason => reason.Reason == ProducerRejectionReason.DependencyCycle);
    }

    [Fact]
    public void Planner_RejectsUpwardTierDependency()
    {
        var higher = new CountingProducer("Higher", tier: 1);
        var lower = new CountingProducer(
            "Lower",
            tier: 0,
            dependencies: () =>
                [new ProducerDependency(higher, ProducerDependencyKind.CompletionNeedsResult)]);

        AssertRejected(
            ProducerPlanner.Plan([new(lower)]),
            ("Lower", ProducerRejectionReason.UpwardTierDependency));
    }

    [Fact]
    public void Planner_RejectsConflictingParameters()
    {
        var first = new CountingProducer("Same", parameters: "a");
        var second = new CountingProducer("Same", parameters: "b");

        AssertRejected(
            ProducerPlanner.Plan([new(first), new(second)]),
            ("Same", ProducerRejectionReason.ConflictingParameters));
    }

    [Fact]
    public void Presence_StopsAtFirstEvidence()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("First"),
            Method.Unsafe("Second"),
            Method.Safe("Third"));

        MethodDefinitionExecution execution = Run(
            image,
            UnsafeEvidencePresence.Description);
        ProducerResult<int> result = execution.ResultOf(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Stopped, result.Outcome);
        Assert.Equal(1, result.Value);
        ProducerParticipation participation = execution.Receipt.For(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(2, participation.UnitsAttempted);
        Assert.Equal(2, execution.Receipt.UnitsVisited);
        Assert.Equal(
            2,
            participation.Layers.Single(layer => layer.Layer == "Body").Acquired);
        Assert.Equal(
            2,
            participation.Layers.Single(layer => layer.Layer == "ModuleLookup").Acquired);

        MethodDefinitionExecution all = Run(
            image,
            Plan(new ProducerRequest(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Complete)));
        ProducerResult<int> allResult = all.ResultOf(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Complete, allResult.Outcome);
        Assert.Equal(1, allResult.Value);
        Assert.Equal(
            3,
            all.Receipt.For(UnsafeEvidencePresenceProducer.Instance)
                .UnitsAttempted);
    }

    [Fact]
    public void Presence_UnsafeDeclarationWithUnreadableBodyIsEvidence()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Pointer("Pointer", readableBody: false));

        Assert.True(UnsafeEvidencePresence.HasEvidence("Fixture.dll", image));
        MethodDefinitionExecution execution = Run(
            image,
            UnsafeEvidencePresence.Description);
        Assert.Equal(
            ProducerOutcome.Stopped,
            execution.ResultOf(UnsafeEvidencePresenceProducer.Instance)
                .Outcome);
        Assert.Equal(
            0,
            execution.Receipt.For(UnsafeEvidencePresenceProducer.Instance)
                .Layers.Single(layer => layer.Layer == "Body").Acquired);
    }

    [Fact]
    public void Presence_IncompleteDefinitionBeforeEvidenceFails()
    {
        ImmutableArray<byte> failsFirst = BuildImage(
            Method.Safe("Broken", readableBody: false),
            Method.Unsafe("Later"));

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "Fixture.dll",
                    failsFirst));
        Assert.Contains("N.Sample::Broken", exception.Message, StringComparison.Ordinal);
        ProducerResult<int> result = Run(
                failsFirst,
                UnsafeEvidencePresence.Description)
            .ResultOf(UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.False(result.HasValue);
        Assert.Equal("MethodDef 0x06000001", result.Failure!.Unit);

        ImmutableArray<byte> evidenceFirst = BuildImage(
            Method.Unsafe("Earlier"),
            Method.Safe("Broken", readableBody: false));
        Assert.True(
            UnsafeEvidencePresence.HasEvidence(
                "Fixture.dll",
                evidenceFirst));
    }

    [Fact]
    public void UndeclaredAccess_FailsVisibly()
    {
        ImmutableArray<byte> image = BuildImage(Method.Safe("Only"));

        var bodyReader = new BodyReadingProducer();
        Assert.Throws<ProducerContractException>(
            () => Run(image, Plan(new ProducerRequest(bodyReader))));

        var lookupReader = new LookupReadingProducer();
        Assert.Throws<ProducerContractException>(
            () => Run(image, Plan(new ProducerRequest(lookupReader))));

        var counter = new CountingProducer("Counter");
        var undeclared = new ResultReadingProducer(
            "Undeclared",
            counter,
            declare: false);
        Assert.Throws<ProducerContractException>(
            () => Run(
                image,
                Plan(new ProducerRequest(undeclared), new ProducerRequest(counter))));

        MethodDefinitionExecution execution = Run(
            image,
            UnsafeEvidencePresence.Description);
        Assert.Throws<ProducerContractException>(
            () => execution.ResultOf(counter));
    }

    [Fact]
    public void Presence_DescriptionAndReceiptAreMinimal()
    {
        MethodDefinitionExecution execution = Run(
            BuildImage(Method.Safe("A"), Method.Safe("B")),
            UnsafeEvidencePresence.Description);

        ProducerParticipation participation =
            Assert.Single(execution.Receipt.Producers);
        Assert.Equal(
            UnsafeEvidencePresenceProducer.Instance.Identity,
            participation.Producer);
        Assert.Equal(ProducerOutcome.Complete, participation.Outcome);
        Assert.Equal(
            [nameof(MethodDefinitionLayers.Body), nameof(MethodDefinitionLayers.ModuleLookup)],
            participation.Layers.Select(layer => layer.Layer));
    }

    [Fact]
    public void FailureContainment_SparesIndependentAndFailsDependents()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"),
            Method.Safe("C"));
        var failing = new CountingProducer("Failing", failAtUnit: 2);
        var independent = new CountingProducer("Independent");
        var dependent = new CountingProducer(
            "Dependent",
            dependencies: () =>
                [new ProducerDependency(failing, ProducerDependencyKind.CompletionNeedsResult)]);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(
                new ProducerRequest(failing),
                new ProducerRequest(independent),
                new ProducerRequest(dependent)));

        ProducerResult<int> failed = execution.ResultOf(failing);
        Assert.Equal(ProducerOutcome.Failed, failed.Outcome);
        Assert.Equal("MethodDef 0x06000002", failed.Failure!.Unit);
        ProducerResult<int> spared = execution.ResultOf(independent);
        Assert.Equal(ProducerOutcome.Complete, spared.Outcome);
        Assert.Equal(3, spared.Value);
        ProducerResult<int> blocked = execution.ResultOf(dependent);
        Assert.Equal(ProducerOutcome.PrerequisiteFailed, blocked.Outcome);
        Assert.Equal("Failing", blocked.FailedPrerequisite);
    }

    [Fact]
    public void Passes_ResultDependencyStartsALaterPass()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"));
        var counter = new CountingProducer("Counter");
        var reader = new ResultReadingProducer(
            "Reader",
            counter,
            declare: true);

        WorkDescription description = Plan(
            new ProducerRequest(reader));
        Assert.Equal(2, description.PassCount);
        Assert.Equal(1, description.VisitPassOf(counter));
        Assert.Equal(2, description.VisitPassOf(reader));

        MethodDefinitionExecution execution = Run(image, description);
        ProducerResult<int> sum = execution.ResultOf(reader);
        Assert.Equal(ProducerOutcome.Complete, sum.Outcome);
        Assert.Equal(4, sum.Value);
    }

    [Fact]
    public void Passes_SameUnitFactIsReadForTheCurrentUnitInALaterPass()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"),
            Method.Safe("C"));
        var rows = new RowProducer("Rows");
        var guard = new CountingProducer("Guard");
        var reader = new RowAfterGuardProducer("Reader", rows, guard);

        WorkDescription description = Plan(new ProducerRequest(reader));
        Assert.Equal(1, description.VisitPassOf(rows));
        Assert.Equal(2, description.VisitPassOf(reader));

        ProducerResult<string> result =
            Run(image, description).ResultOf(reader);
        Assert.Equal(ProducerOutcome.Complete, result.Outcome);
        Assert.Equal("1,2,3", result.Value);
    }

    [Fact]
    public void ScopeGuard_VisitsOnlyUnitsInTheAcceptedClasses()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"),
            Method.Safe("C"),
            Method.Safe("D"));
        var parity = new RowParityProducer("Parity");
        var even = new GuardedRowsProducer("EvenRows", parity, acceptedClasses: 1UL << 0);
        var odd = new GuardedRowsProducer("OddRows", parity, acceptedClasses: 1UL << 1);

        MethodDefinitionExecution execution =
            Run(image, Plan(new ProducerRequest(even), new ProducerRequest(odd)));

        Assert.Equal("2,4", execution.ResultOf(even).Value);
        Assert.Equal("1,3", execution.ResultOf(odd).Value);
        // A unit outside the guard is out of scope, not attempted or failed.
        ProducerParticipation evenParticipation = execution.Receipt.For(even);
        Assert.Equal(ProducerOutcome.Complete, evenParticipation.Outcome);
        Assert.Equal(2, evenParticipation.UnitsAttempted);
        Assert.Equal(0, evenParticipation.UnitsFailed);
        Assert.Equal(4, execution.Receipt.For(parity).UnitsAttempted);
    }

    [Theory]
    [InlineData("Sample", "1,2,3", 0b11UL)]
    [InlineData("Other", "", 0b11UL)]
    [InlineData("Sample", "1,2,3", ProducerDependency.AllUnitClasses)]
    [InlineData("Other", "", ProducerDependency.AllUnitClasses)]
    public void TypeScope_ExcludesWholeTypesForTheProducerAndEveryProducerItGuards(
        string scopedType,
        string expectedRows,
        ulong acceptedClasses)
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"),
            Method.Safe("C"));
        var classifier = new TypeScopedParityProducer("Parity", scopedType);
        var guarded = new GuardedRowsProducer("Rows", classifier, acceptedClasses);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(guarded)));

        Assert.Equal(expectedRows, execution.ResultOf(guarded).Value);
        int inScope = expectedRows.Length == 0 ? 0 : 3;
        Assert.Equal(inScope, execution.Receipt.For(classifier).UnitsAttempted);
        Assert.Equal(inScope, execution.Receipt.For(guarded).UnitsAttempted);
        Assert.Equal(ProducerOutcome.Complete, execution.Receipt.For(guarded).Outcome);
    }

    [Fact]
    public void TypeScope_AGuardExcludedTypeIsNotTestedByTheDependent()
    {
        // The dependent's own type predicate fails on every type, but its
        // guard excludes the only type with methods first, so the predicate
        // is never asked and the dependent completes with nothing in scope.
        ImmutableArray<byte> image = BuildImage(Method.Safe("A"), Method.Safe("B"));
        var classifier = new TypeScopedParityProducer("Parity", "Other");
        var guarded = new GuardedRowsProducer(
            "Rows",
            classifier,
            acceptedClasses: 0b11,
            failingTypeScope: true);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(guarded)));

        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(guarded).Outcome);
        Assert.Equal("", execution.ResultOf(guarded).Value);
        Assert.Equal(0, execution.Receipt.For(guarded).UnitsAttempted);
        Assert.Equal(0, execution.Receipt.For(guarded).UnitsFailed);
    }

    [Fact]
    public void TypeScope_AFailedTypePredicateIsContainedAsTheProducersFailure()
    {
        ImmutableArray<byte> image = BuildImage(Method.Safe("A"), Method.Safe("B"));
        var classifier = new TypeScopedParityProducer("Parity", "Sample");
        var guarded = new GuardedRowsProducer(
            "Rows",
            classifier,
            acceptedClasses: 0b11,
            failingTypeScope: true);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(guarded)));

        ProducerResult<string> result = execution.ResultOf(guarded);
        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Equal("(type scope)", result.Failure!.Unit);
        Assert.Equal(0, execution.Receipt.For(guarded).UnitsAttempted);
    }

    [Fact]
    public void TypeScope_AFailedTypePredicateFailsSamePassDependentsAsAPrerequisite()
    {
        // Both type predicates fail on the same type, and the dependency is
        // an ordinary same-pass visit dependency, not a guard. The dependency
        // is decided first, so the dependent reports its failed prerequisite
        // rather than a failure of its own.
        ImmutableArray<byte> image = BuildImage(Method.Safe("A"), Method.Safe("B"));
        var classifier = new FailingTypeScopeParityProducer("Parity");
        var guarded = new GuardedRowsProducer(
            "Rows",
            classifier,
            acceptedClasses: null,
            failingTypeScope: true);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(guarded)));

        Assert.Equal(ProducerOutcome.Failed, execution.Receipt.For(classifier).Outcome);
        ProducerResult<string> result = execution.ResultOf(guarded);
        Assert.Equal(ProducerOutcome.PrerequisiteFailed, result.Outcome);
        Assert.Equal("Parity", result.FailedPrerequisite);
    }

    [Fact]
    public void ScopeGuard_OnAProducerThatDoesNotClassifyIsAContractViolation()
    {
        ImmutableArray<byte> image = BuildImage(Method.Safe("A"));
        var rows = new RowProducer("Rows");
        var guarded = new GuardedRowsProducer("Guarded", rows, acceptedClasses: 1UL);

        Assert.Throws<ProducerContractException>(() =>
            Run(image, Plan(new ProducerRequest(guarded))));
    }

    [Fact]
    public void FailureContainment_CompletionFailureIsContained()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"));
        var failing = new CompletionFailingProducer("FailsInCompletion");
        var independent = new CountingProducer("Independent");
        var dependent = new CountingProducer(
            "Dependent",
            dependencies: () =>
                [new ProducerDependency(failing, ProducerDependencyKind.CompletionNeedsResult)]);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(
                new ProducerRequest(failing),
                new ProducerRequest(independent),
                new ProducerRequest(dependent)));

        ProducerResult<int> failed = execution.ResultOf(failing);
        Assert.Equal(ProducerOutcome.Failed, failed.Outcome);
        Assert.Equal("(completion)", failed.Failure!.Unit);
        Assert.Equal(2, execution.ResultOf(independent).Value);
        ProducerResult<int> blocked = execution.ResultOf(dependent);
        Assert.Equal(ProducerOutcome.PrerequisiteFailed, blocked.Outcome);
        Assert.Equal("FailsInCompletion", blocked.FailedPrerequisite);
        Assert.Equal(3, execution.Receipt.Producers.Length);
    }

    [Theory]
    [InlineData(ProducerTerminal.Complete)]
    [InlineData(ProducerTerminal.Exists)]
    public void ClosedQueryKernel_MatchesTheInterpretedExecutor(ProducerTerminal terminal)
    {
        ImmutableArray<byte>[] images =
        [
            BuildImage(Method.Safe("A"), Method.Safe("B"), Method.Safe("C")),
            BuildImage(Method.Safe("A"), Method.Safe("B", readableBody: false), Method.Safe("C")),
            BuildImage(Method.Unsafe("A"), Method.Safe("B")),
            BuildImage(),
        ];

        foreach (ImmutableArray<byte> image in images)
        {
            foreach (string scopedType in (string[])["Sample", "Other"])
            {
                var kernel = new BodySizeProducer("Kernel", scopedType, allowsKernel: true);
                var interpreted = new BodySizeProducer("Interpreted", scopedType, allowsKernel: false);

                MethodDefinitionExecution k = Run(image, Plan(new ProducerRequest(kernel, terminal)));
                MethodDefinitionExecution i = Run(image, Plan(new ProducerRequest(interpreted, terminal)));

                ProducerResult<int> kr = k.ResultOf(kernel);
                ProducerResult<int> ir = i.ResultOf(interpreted);
                Assert.Equal(ir.Outcome, kr.Outcome);
                Assert.Equal(ir.Value, kr.Value);
                Assert.Equal(ir.Failure?.Unit, kr.Failure?.Unit);
                Assert.Equal(i.Receipt.UnitsVisited, k.Receipt.UnitsVisited);
                ProducerParticipation kp = k.Receipt.For(kernel);
                ProducerParticipation ip = i.Receipt.For(interpreted);
                Assert.Equal(
                    (ip.UnitsAttempted, ip.UnitsCompleted, ip.UnitsFailed),
                    (kp.UnitsAttempted, kp.UnitsCompleted, kp.UnitsFailed));
                Assert.Equal(
                    ip.Layers.Select(layer => (layer.Layer, layer.Acquired)),
                    kp.Layers.Select(layer => (layer.Layer, layer.Acquired)));
            }
        }
    }

    [Fact]
    public void ClosedQueryKernel_DefersToTheInterpretedExecutorWhenALaterPassReadsItsFacts()
    {
        // A predicate alone in its pass would run as a kernel, but a reader in
        // a later pass needs its per-unit facts, which a kernel does not keep.
        ImmutableArray<byte> image = BuildImage(Method.Unsafe("A"), Method.Safe("B"), Method.Safe("C"));
        var kernel = new BodySizeProducer("Kernel", "Sample", allowsKernel: true);
        var interpreted = new BodySizeProducer("Interpreted", "Sample", allowsKernel: false);
        var kernelReader = new PredicateFactReader("KernelReader", kernel);
        var interpretedReader = new PredicateFactReader("InterpretedReader", interpreted);

        MethodDefinitionExecution k = Run(
            image,
            Plan(new ProducerRequest(kernel), new ProducerRequest(kernelReader)));
        MethodDefinitionExecution i = Run(
            image,
            Plan(new ProducerRequest(interpreted), new ProducerRequest(interpretedReader)));

        Assert.Equal(ProducerOutcome.Complete, k.ResultOf(kernel).Outcome);
        Assert.Equal(i.ResultOf(interpreted).Value, k.ResultOf(kernel).Value);
        ProducerResult<string> kr = k.ResultOf(kernelReader);
        ProducerResult<string> ir = i.ResultOf(interpretedReader);
        Assert.Equal(ProducerOutcome.Complete, kr.Outcome);
        Assert.Equal(ir.Value, kr.Value);
        Assert.Equal(i.Receipt.UnitsVisited, k.Receipt.UnitsVisited);
    }

    [Fact]
    public void Planner_RetainsUnitFactsOnlyAsLongAsTheirReadersNeedThem()
    {
        var rows = new RowProducer("Rows");
        var guard = new CountingProducer("Guard");
        var samePass = new GuardedRowsProducer("SamePass", new RowParityProducer("Parity"), acceptedClasses: 0b11);
        var laterPass = new RowAfterGuardProducer("LaterPass", rows, guard);

        WorkDescription description = Plan(
            new ProducerRequest(samePass),
            new ProducerRequest(laterPass));

        // Read only by a reader in a later pass: every unit's fact is kept.
        Assert.Equal(UnitFactRetention.AllUnits, RetentionOf(description, rows));
        // Read only by a reader in its own pass: the current unit's fact.
        Assert.Equal(
            UnitFactRetention.CurrentUnit,
            RetentionOf(description, samePass.Dependencies[0].Producer));
        // Read by no one: nothing is kept.
        Assert.Equal(UnitFactRetention.None, RetentionOf(description, guard));
        Assert.Equal(UnitFactRetention.None, RetentionOf(description, laterPass));

        static UnitFactRetention RetentionOf(WorkDescription description, ProducerDeclaration producer) =>
            description.TryGetIndex(producer, out int index)
                ? description.FactRetention[index]
                : throw new InvalidOperationException($"{producer} is not planned.");
    }

    [Fact]
    public void Planner_RejectsDistinctDeclarationsWithTheSameIdentity()
    {
        var first = new CountingProducer("Same", parameters: "p");
        var second = new CountingProducer(
            "Same",
            parameters: "p",
            dependencies: () =>
                [new ProducerDependency(null!, ProducerDependencyKind.CompletionNeedsResult)]);

        AssertRejected(
            ProducerPlanner.Plan([new(first), new(second)]),
            ("Same", ProducerRejectionReason.DuplicateDeclaration));
    }

    [Fact]
    public void FailureContainment_StoppedDependentStillNeedsItsCompletionPrerequisite()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"));
        var prerequisite = new CompletionFailingProducer("Prerequisite");
        var dependent = new SettlingProducer(
            "Dependent",
            () => [new ProducerDependency(prerequisite, ProducerDependencyKind.CompletionNeedsResult)]);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(new ProducerRequest(dependent, ProducerTerminal.Exists)));

        Assert.Equal(
            ProducerOutcome.Failed,
            execution.ResultOf(prerequisite).Outcome);
        ProducerResult<int> blocked = execution.ResultOf(dependent);
        Assert.Equal(ProducerOutcome.PrerequisiteFailed, blocked.Outcome);
        Assert.False(blocked.HasValue);
        Assert.Equal("Prerequisite", blocked.FailedPrerequisite);
    }

    [Fact]
    public void Planner_OrdersStagesNotProducers()
    {
        ImmutableArray<byte> image = BuildImage(
            Method.Safe("A"),
            Method.Safe("B"));
        RowsWithResultProducer? first = null;
        var second = new RowReadingProducer(
            "Second",
            () => [new ProducerDependency(first!, ProducerDependencyKind.VisitNeedsVisit)],
            () => first!);
        first = new RowsWithResultProducer(
            "First",
            () => [new ProducerDependency(second, ProducerDependencyKind.CompletionNeedsResult)],
            second);

        WorkDescription description = Plan(new ProducerRequest(first));
        Assert.Equal(1, description.PassCount);
        Assert.Equal(["First", "Second"], description.Producers.Select(p => p.Identity));
        Assert.Equal(["Second", "First"], description.CompletionOrder.Select(p => p.Identity));

        MethodDefinitionExecution execution = Run(image, description);
        Assert.Equal("1,2", execution.ResultOf(second).Value);
        Assert.Equal("rows=2;second=1,2", execution.ResultOf(first).Value);
    }

    [Fact]
    public void Planner_RejectsAGenuineStageCycle()
    {
        RowReadingProducer? first = null;
        RowReadingProducer? second = null;
        first = new RowReadingProducer(
            "LoopA",
            () => [new ProducerDependency(second!, ProducerDependencyKind.VisitNeedsVisit)],
            () => second!);
        second = new RowReadingProducer(
            "LoopB",
            () => [new ProducerDependency(first, ProducerDependencyKind.VisitNeedsVisit)],
            () => first);

        ProducerPlanResult.Rejected rejected =
            Assert.IsType<ProducerPlanResult.Rejected>(
                ProducerPlanner.Plan([new(first)]));
        Assert.Contains(
            rejected.Reasons,
            reason => reason.Reason == ProducerRejectionReason.DependencyCycle);
    }

    [Fact]
    public void FailureContainment_LateFailureRevokesAnAlreadyCompletedDependent()
    {
        ImmutableArray<byte> image = BuildImage(Method.Safe("A"));
        var failure = new CompletionFailingRowProducer("MFailure");
        var dependent = new RowReadingProducer(
            "ZDependent",
            () => [new ProducerDependency(failure, ProducerDependencyKind.VisitNeedsVisit)],
            () => failure);
        var consumer = new ResultConsumingProducer("AConsumer", dependent);

        MethodDefinitionExecution alone = Run(
            image,
            Plan(new ProducerRequest(dependent)));
        MethodDefinitionExecution withConsumer = Run(
            image,
            Plan(new ProducerRequest(dependent), new ProducerRequest(consumer)));

        foreach (MethodDefinitionExecution execution in new[] { alone, withConsumer })
        {
            Assert.Equal(ProducerOutcome.Failed, execution.ResultOf(failure).Outcome);
            ProducerResult<string> blocked = execution.ResultOf(dependent);
            Assert.Equal(ProducerOutcome.PrerequisiteFailed, blocked.Outcome);
            Assert.Equal("MFailure", blocked.FailedPrerequisite);
            Assert.Equal(
                ProducerOutcome.PrerequisiteFailed,
                execution.Receipt.For(dependent).Outcome);
        }

        ProducerResult<int> cascaded = withConsumer.ResultOf(consumer);
        Assert.Equal(ProducerOutcome.PrerequisiteFailed, cascaded.Outcome);
        Assert.Equal("ZDependent", cascaded.FailedPrerequisite);
    }

    [Fact]
    public void Presence_SettledAnswerIsNotReplacedByTheNextMetadataRead()
    {
        ImmutableArray<byte> localloc = ImmutableArray.Create(
            DotnetInspector.Fixtures.MetadataMethodPtrFixture
                .BuildTrailingOutOfRange([0x0A, 0xFE, 0x0F]));
        Assert.True(UnsafeEvidencePresence.HasEvidence("Fixture.dll", localloc));

        ImmutableArray<byte> invalidHeader = ImmutableArray.Create(
            DotnetInspector.Fixtures.MetadataMethodPtrFixture
                .BuildTrailingOutOfRange([0x00]));
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "Fixture.dll",
                    invalidHeader));
        Assert.Contains("<Module>::M0", exception.Message, StringComparison.Ordinal);
    }

    static WorkDescription Plan(params ProducerRequest[] requests) =>
        Assert.IsType<ProducerPlanResult.Accepted>(
            ProducerPlanner.Plan(requests)).Description;

    static void AssertRejected(
        ProducerPlanResult result,
        params (string Producer, ProducerRejectionReason Reason)[] expected)
    {
        ProducerPlanResult.Rejected rejected =
            Assert.IsType<ProducerPlanResult.Rejected>(result);
        Assert.Equal(
            expected,
            rejected.Reasons.Select(reason => (reason.Producer, reason.Reason)));
    }

    static MethodDefinitionExecution Run(
        ImmutableArray<byte> image,
        WorkDescription description)
    {
        using var peReader = new PEReader(image);
        return MethodDefinitionExecution.Execute(
            description,
            "Fixture.dll",
            peReader);
    }

    /// <summary>Counts visited units; optionally fails at a MethodDef row.</summary>
    sealed class CountingProducer(
        string identity,
        int tier = 0,
        Func<IReadOnlyList<ProducerDependency>>? dependencies = null,
        string? parameters = null,
        int failAtUnit = 0)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier,
            MethodDefinitionLayers.Declaration,
            dependencies,
            parameters)
    {
        internal override int Visit(scoped MethodDefinitionView view)
        {
            if ((view.Token & 0x00FF_FFFF) == failAtUnit)
                throw new BadImageFormatException("injected failure");
            return 1;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    /// <summary>Reads another producer's completed result during its visits.</summary>
    sealed class ResultReadingProducer(
        string identity,
        CountingProducer source,
        bool declare)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            declare
                ? () => [new ProducerDependency(source, ProducerDependencyKind.VisitNeedsResult)]
                : null)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.ResultOf(source).Value;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    /// <summary>Publishes each unit's MethodDef row number.</summary>
    sealed class RowProducer(string identity)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    /// <summary>
    /// Reads the same-unit fact of <paramref name="rows"/> in a pass after
    /// <paramref name="guard"/> completes.
    /// </summary>
    sealed class RowAfterGuardProducer(
        string identity,
        RowProducer rows,
        CountingProducer guard)
        : MethodDefinitionProducer<int, List<int>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            () =>
            [
                new ProducerDependency(rows, ProducerDependencyKind.VisitNeedsVisit),
                new ProducerDependency(guard, ProducerDependencyKind.VisitNeedsResult),
            ])
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.FactOf(rows);

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", accumulator);
    }

    /// <summary>Classifies each unit by MethodDef row parity: class 0 even, class 1 odd.</summary>
    sealed class RowParityProducer(string identity)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool ClassifiesUnits => true;

        internal override int UnitClass(int fact) => fact & 1;
    }

    /// <summary>Classifies by row parity, with a type scope naming the one type in scope.</summary>
    sealed class TypeScopedParityProducer(string identity, string scopedType)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool ClassifiesUnits => true;

        internal override int UnitClass(int fact) => fact & 1;

        internal override bool HasTypeScope => true;

        internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
            reader.StringComparer.Equals(type.Name, scopedType);
    }

    /// <summary>A body-reading predicate: the unit's IL body is non-empty.</summary>
    struct BodyReadPredicate : IMethodDefinitionPredicate
    {
        public bool Test(scoped MethodDefinitionView view) => view.GetBody().Size > 0;
    }

    /// <summary>An open query over <see cref="BodyReadPredicate"/>, with a type scope and a kernel toggle.</summary>
    sealed class BodySizeProducer(string identity, string scopedType, bool allowsKernel)
        : MethodDefinitionPredicateProducer<BodyReadPredicate>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body)
    {
        internal override bool AllowsKernel => allowsKernel;

        internal override bool HasTypeScope => true;

        internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
            reader.StringComparer.Equals(type.Name, scopedType)
            || reader.StringComparer.Equals(type.Name, "<Module>");
    }

    /// <summary>
    /// Reads a predicate's per-unit facts in a pass after the predicate completes.
    /// </summary>
    sealed class PredicateFactReader(string identity, BodySizeProducer predicate)
        : MethodDefinitionProducer<bool, List<bool>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            () =>
            [
                new ProducerDependency(predicate, ProducerDependencyKind.VisitNeedsVisit),
                new ProducerDependency(predicate, ProducerDependencyKind.VisitNeedsResult),
            ])
    {
        internal override bool Visit(scoped MethodDefinitionView view) =>
            view.FactOf(predicate);

        internal override List<bool> Seed() => [];

        internal override List<bool> Accumulate(List<bool> accumulator, bool fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<bool> accumulator,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", accumulator);
    }

    /// <summary>Classifies units by parity, but its type predicate always fails.</summary>
    sealed class FailingTypeScopeParityProducer(string identity)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool ClassifiesUnits => true;

        internal override int UnitClass(int fact) => fact & 1;

        internal override bool HasTypeScope => true;

        internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
            throw new BadImageFormatException("Guard type scope fixture failure.");
    }

    /// <summary>Publishes the rows of the units its scope guard accepts.</summary>
    sealed class GuardedRowsProducer(
        string identity,
        ProducerDeclaration guard,
        ulong? acceptedClasses,
        bool failingTypeScope = false)
        : MethodDefinitionProducer<int, List<int>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            () => [new ProducerDependency(guard, ProducerDependencyKind.VisitNeedsVisit, acceptedClasses)])
    {
        internal override bool HasTypeScope => failingTypeScope;

        internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
            throw new BadImageFormatException("Type scope fixture failure.");

        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", accumulator);
    }

    /// <summary>Visits normally, then fails recoverably in its completion.</summary>
    sealed class CompletionFailingProducer(string identity)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            throw new BadImageFormatException("injected completion failure");
    }

    /// <summary>Settles an Exists terminal on its first unit.</summary>
    sealed class SettlingProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool Settles(int fact) => true;
    }

    /// <summary>Reads a row-producing dependency's same-unit fact.</summary>
    sealed class RowReadingProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies,
        Func<MethodDefinitionProducer<int, List<int>, string>> source)
        : MethodDefinitionProducer<int, List<int>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.FactOf(source());

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", accumulator);
    }

    /// <summary>
    /// Publishes row numbers per unit, and in its completion reads another
    /// producer's completed result.
    /// </summary>
    sealed class RowsWithResultProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies,
        RowReadingProducer reader)
        : MethodDefinitionProducer<int, List<int>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion) =>
            $"rows={accumulator.Count};second={completion.ResultOf(reader).Value}";
    }

    /// <summary>Publishes row numbers per unit, then fails recoverably in its completion.</summary>
    sealed class CompletionFailingRowProducer(string identity)
        : MethodDefinitionProducer<int, List<int>, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override string Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion) =>
            throw new BadImageFormatException("injected completion failure");
    }

    /// <summary>Reads another producer's completed result in its completion.</summary>
    sealed class ResultConsumingProducer(
        string identity,
        RowReadingProducer source)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            () => [new ProducerDependency(source, ProducerDependencyKind.CompletionNeedsResult)])
    {
        internal override int Visit(scoped MethodDefinitionView view) => 0;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            completion.ResultOf(source).Value?.Length ?? -1;
    }

    /// <summary>Declares only the declaration layer, then asks for the module lookup.</summary>
    sealed class LookupReadingProducer()
        : MethodDefinitionProducer<int, int, int>(
            "LookupReader",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Lookup is null ? 0 : 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    /// <summary>Declares only the declaration layer, then asks for the body.</summary>
    sealed class BodyReadingProducer()
        : MethodDefinitionProducer<int, int, int>(
            "BodyReader",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.GetBody().Size;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed record Method(
        string Name,
        bool PointerReturn,
        bool UnsafeBody,
        bool ReadableBody)
    {
        public static Method Safe(string name, bool readableBody = true) =>
            new(name, false, false, readableBody);

        public static Method Unsafe(string name) =>
            new(name, false, true, true);

        public static Method Pointer(string name, bool readableBody) =>
            new(name, true, false, readableBody);
    }

    /// <summary>
    /// Builds an assembly with type N.Sample whose static methods are, in
    /// order, the given methods. An unreadable body points past the image.
    /// </summary>
    static ImmutableArray<byte> BuildImage(params Method[] methods)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Fixture.dll"),
            metadata.GetOrAddGuid(new Guid("6c1a4d1e-6b73-4bb1-9d52-0a8f6a2f3b11")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Fixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        foreach (Method method in methods)
        {
            var code = new BlobBuilder();
            if (method.UnsafeBody)
            {
                code.WriteByte((byte)ILOpCode.Calli);
                code.WriteInt32(0);
            }
            code.WriteByte((byte)ILOpCode.Ret);
            int offset = encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
            if (!method.ReadableBody)
                offset = 0x00FF_FFF0;

            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature()
                .Parameters(
                    0,
                    returnType =>
                    {
                        if (method.PointerReturn)
                            returnType.Type().Pointer().Int32();
                        else
                            returnType.Void();
                    },
                    _ => { });
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(method.Name),
                metadata.GetOrAddBlob(signature),
                offset,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }
}
