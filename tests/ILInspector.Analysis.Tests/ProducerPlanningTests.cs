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
        ProducerResult<bool> result = execution.ResultOf(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Stopped, result.Outcome);
        Assert.True(result.Value);
        ProducerParticipation participation = execution.Receipt.For(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(2, participation.UnitsAttempted);
        Assert.Equal(2, execution.Receipt.UnitsVisited);
        Assert.Equal(
            2,
            Assert.Single(participation.Layers).Acquired);

        MethodDefinitionExecution all = Run(
            image,
            Plan(new ProducerRequest(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.All)));
        ProducerResult<bool> allResult = all.ResultOf(
            UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Complete, allResult.Outcome);
        Assert.True(allResult.Value);
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
            Assert.Single(
                execution.Receipt.For(
                    UnsafeEvidencePresenceProducer.Instance).Layers)
                .Acquired);
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
        ProducerResult<bool> result = Run(
                failsFirst,
                UnsafeEvidencePresence.Description)
            .ResultOf(UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.False(result.HasValue);
        Assert.Equal("N.Sample::Broken", result.Failure!.Unit);

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
            nameof(MethodDefinitionLayers.Body),
            Assert.Single(participation.Layers).Layer);
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
        Assert.Equal("N.Sample::B", failed.Failure!.Unit);
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
        : MethodDefinitionProducer<int, int>(
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

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            facts.Sum();
    }

    /// <summary>Reads another producer's completed result during its visits.</summary>
    sealed class ResultReadingProducer(
        string identity,
        CountingProducer source,
        bool declare)
        : MethodDefinitionProducer<int, int>(
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

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            facts.Sum();
    }

    /// <summary>Publishes each unit's MethodDef row number.</summary>
    sealed class RowProducer(string identity)
        : MethodDefinitionProducer<int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            facts.Count;
    }

    /// <summary>
    /// Reads the same-unit fact of <paramref name="rows"/> in a pass after
    /// <paramref name="guard"/> completes.
    /// </summary>
    sealed class RowAfterGuardProducer(
        string identity,
        RowProducer rows,
        CountingProducer guard)
        : MethodDefinitionProducer<int, string>(
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

        internal override string Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", facts);
    }

    /// <summary>Visits normally, then fails recoverably in its completion.</summary>
    sealed class CompletionFailingProducer(string identity)
        : MethodDefinitionProducer<int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            throw new BadImageFormatException("injected completion failure");
    }

    /// <summary>Settles an Exists terminal on its first unit.</summary>
    sealed class SettlingProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies)
        : MethodDefinitionProducer<int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            facts.Sum();

        internal override bool Settles(int fact) => true;
    }

    /// <summary>Reads a row-producing dependency's same-unit fact.</summary>
    sealed class RowReadingProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies,
        Func<MethodDefinitionProducer<int, string>> source)
        : MethodDefinitionProducer<int, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.FactOf(source());

        internal override string Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            string.Join(",", facts);
    }

    /// <summary>
    /// Publishes row numbers per unit, and in its completion reads another
    /// producer's completed result.
    /// </summary>
    sealed class RowsWithResultProducer(
        string identity,
        Func<IReadOnlyList<ProducerDependency>> dependencies,
        RowReadingProducer reader)
        : MethodDefinitionProducer<int, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            dependencies)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override string Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            $"rows={facts.Count};second={completion.ResultOf(reader).Value}";
    }

    /// <summary>Publishes row numbers per unit, then fails recoverably in its completion.</summary>
    sealed class CompletionFailingRowProducer(string identity)
        : MethodDefinitionProducer<int, string>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token & 0x00FF_FFFF;

        internal override string Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            throw new BadImageFormatException("injected completion failure");
    }

    /// <summary>Reads another producer's completed result in its completion.</summary>
    sealed class ResultConsumingProducer(
        string identity,
        RowReadingProducer source)
        : MethodDefinitionProducer<int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration,
            () => [new ProducerDependency(source, ProducerDependencyKind.CompletionNeedsResult)])
    {
        internal override int Visit(scoped MethodDefinitionView view) => 0;

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            completion.ResultOf(source).Value?.Length ?? -1;
    }

    /// <summary>Declares only the declaration layer, then asks for the body.</summary>
    sealed class BodyReadingProducer()
        : MethodDefinitionProducer<int, int>(
            "BodyReader",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) =>
            view.GetBody().Size;

        internal override int Complete(
            IReadOnlyList<int> facts,
            MethodDefinitionCompletionView completion) =>
            facts.Count;
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
