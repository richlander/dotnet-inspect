using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.Artifacts;
using DotnetInspector.Fixtures;

using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research.Tests;

public class ResearchProducerSessionTests
{
    const string SampleType =
        "ILInspector.Research.TargetFixtures.TargetSample";

    [Fact]
    public void ResearchProducerCatalog_AdmitsEveryDeclaredLocalKind()
    {
        Assert.Equal(
            Enum.GetValues<ResearchProducerKind>(),
            ResearchProducerCatalog.Kinds);
    }

    [Fact]
    public void ResearchProducerSession_RejectsForeignPopulationResolutionAndCatalogShapes()
    {
        SessionFixture first = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        SessionFixture second = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = first.Resolve(
            SampleType,
            "Method");

        Assert.Equal(
            ResearchProducerRejectionKind.ForeignResolution,
            Reject(
                new ResearchProducerSessionRequest(
                    second.Population,
                    resolution,
                    ResearchProducerCatalog.Kinds)).Kind);
        Assert.Equal(
            ResearchProducerRejectionKind.EmptyProducerSelection,
            Reject(
                new ResearchProducerSessionRequest(
                    first.Population,
                    resolution,
                    [])).Kind);
        Assert.Equal(
            ResearchProducerRejectionKind.DuplicateProducerKind,
            Reject(
                new ResearchProducerSessionRequest(
                    first.Population,
                    resolution,
                    [
                        ResearchProducerKind.CSharp,
                        ResearchProducerKind.CSharp,
                    ])).Kind);
        Assert.Equal(
            ResearchProducerRejectionKind.UnknownProducerKind,
            Reject(
                new ResearchProducerSessionRequest(
                    first.Population,
                    resolution,
                    [(ResearchProducerKind)int.MaxValue])).Kind);

        var broken = new ResearchTargetResolution(
            first.Population.Operation,
            resolution.Scopes,
            [],
            []);
        Assert.Equal(
            ResearchProducerRejectionKind.InvalidIdentityClosure,
            Reject(
                new ResearchProducerSessionRequest(
                    first.Population,
                    broken,
                    [ResearchProducerKind.CSharp])).Kind);

        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            FixtureCatalog.ResearchTargetSample.AssemblyPath());
        ResearchAdmittedPopulation bodySignal =
            Assert.IsType<ResearchAdmissionOutcome.Admitted>(
                ResearchComparisonAdmission.Admit(
                    new ResearchComparisonAdmissionRequest(
                        ResearchComparisonProfile.BodySignal,
                        [
                            new ResearchComparisonAdmissionQuestion(
                                [new BodySignalComparisonInputOccurrence(bodyIndex)],
                                []),
                        ]))).Population;
        Assert.Equal(
            ResearchProducerRejectionKind.UnsupportedProfile,
            Reject(
                new ResearchProducerSessionRequest(
                    bodySignal,
                    resolution,
                    [ResearchProducerKind.CSharp])).Kind);
    }

    [Fact]
    public void ResearchProducerWorkItems_DeriveExactOrderedCorrespondenceCatalogProduct()
    {
        int beforeOpens = 0;
        int afterOpens = 0;
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(
                FixtureCatalog.ResearchTargetSample.AssemblyPath(),
                () => beforeOpens++),
            Occurrence(
                FixtureCatalog.ResearchTargetSample.AssemblyPath(),
                () => afterOpens++));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        int beforeResolutionOpens = beforeOpens;
        int afterResolutionOpens = afterOpens;

        var invoker = new TrackingInvoker();
        ResearchProducerCompletion completion = Complete(
            new ResearchProducerSessionRequest(
                fixture.Population,
                resolution,
                [
                    ResearchProducerKind.IlBody,
                    ResearchProducerKind.CSharp,
                ]),
            invoker);

        Assert.Equal(2, completion.WorkItems.Length);
        Assert.Collection(
            completion.WorkItems,
            item =>
            {
                Assert.Same(resolution.Correspondences[0], item.Correspondence);
                Assert.Equal(ResearchProducerKind.CSharp, item.Producer);
            },
            item =>
            {
                Assert.Same(resolution.Correspondences[0], item.Correspondence);
                Assert.Equal(ResearchProducerKind.IlBody, item.Producer);
            });
        Assert.Collection(
            completion.Results,
            result => Assert.IsType<
                ResearchProducerWorkOutcome.ProducedCSharp>(result.Outcome),
            result => Assert.IsType<
                ResearchProducerWorkOutcome.ProducedIlBody>(result.Outcome));
        Assert.True(beforeOpens > beforeResolutionOpens);
        Assert.True(afterOpens > afterResolutionOpens);
        Assert.Equal(2, invoker.CSharpSources.Count);
        Assert.Collection(
            completion.Cleanup,
            outcome => Assert.Same(
                fixture.Population.Inputs[1].Id,
                outcome.Input),
            outcome => Assert.Same(
                fixture.Population.Inputs[0].Id,
                outcome.Input));
    }

    [Fact]
    public void ResearchProducerAdapters_MapExactCorrespondenceEvidence()
    {
        SessionFixture pairedFixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution paired = pairedFixture.Resolve(
            SampleType,
            "Method");
        ResearchProducerCompletion pairedCompletion = Complete(
            Request(pairedFixture, paired));

        var csharp = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedCSharp>(
                pairedCompletion.Results[0].Outcome);
        var il = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedIlBody>(
                pairedCompletion.Results[1].Outcome);
        Assert.NotNull(csharp.Result.BodyDiff);
        Assert.NotNull(il.Result.MemberDiff);
        Assert.Equal(
            FindingInspectionState.Complete,
            Assert.IsType<
                FindingComparison<CSharpCanonicalLine>.Complete>(
                    csharp.Result.Findings.Value).Transition.Old);
        Assert.Equal(
            FindingInspectionState.Complete,
            Assert.IsType<
                FindingComparison<CanonicalIlOperation>.Complete>(
                    il.Result.Findings.Value).Transition.New);

        SessionFixture absentFixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution absent = absentFixture.Resolve(
            "ILInspector.Research.TargetFixtures.NotHere",
            "Method");
        ResearchProducerCompletion absentCompletion = Complete(
            Request(absentFixture, absent));
        Assert.All(
            absentCompletion.Results,
            result =>
            {
                object native = result.Outcome switch
                {
                    ResearchProducerWorkOutcome.ProducedCSharp produced =>
                        produced.Result.Findings.Value,
                    ResearchProducerWorkOutcome.ProducedIlBody produced =>
                        produced.Result.Findings.Value,
                    _ => throw new Xunit.Sdk.XunitException(
                        "Both-absent correspondence must invoke each adapter."),
                };
                FindingInspectionTransition transition = native switch
                {
                    FindingComparison<CSharpCanonicalLine>.Complete complete =>
                        complete.Transition,
                    FindingComparison<CanonicalIlOperation>.Complete complete =>
                        complete.Transition,
                    _ => throw new Xunit.Sdk.XunitException(
                        "Both-absent comparison must complete."),
                };
                Assert.Equal(
                    FindingInspectionState.SubjectAbsent,
                    transition.Old);
                Assert.Equal(
                    FindingInspectionState.SubjectAbsent,
                    transition.New);
            });
        Assert.Empty(absentCompletion.Cleanup);
    }

    [Fact]
    public void ResearchProducerAdapters_RetainOneSidedNativeResults()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.DiffV1.AssemblyPath()),
            Occurrence(FixtureCatalog.DiffV2.AssemblyPath()));
        ResearchTargetResolution ordinal = fixture.Resolve(
            "DiffFixtureSample.ConstructorRemovalSample",
            ".ctor:1");
        MemberAnchor beforeAnchor = Assert.Single(
            ordinal.Correspondences
                .OfType<
                    ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable>(),
            outcome => outcome.Attempt.Request.Side
                == ResearchComparisonSide.Before)
            .Target.Anchor;
        ResearchTargetResolution oneSided = fixture.Resolve(
            "DiffFixtureSample.ConstructorRemovalSample",
            new MemberTargetSelector(
                beforeAnchor.StableSelector,
                ".ctor",
                DigestPrefix: beforeAnchor.Fingerprint));
        Assert.IsType<ResearchTargetCorrespondenceOutcome.BeforeOnly>(
            Assert.Single(oneSided.Correspondences));

        ResearchProducerCompletion completion = Complete(
            Request(fixture, oneSided));
        var csharp = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedCSharp>(
                completion.Results[0].Outcome);
        var il = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedIlBody>(
                completion.Results[1].Outcome);
        Assert.Null(csharp.Result.BodyDiff);
        Assert.Null(il.Result.MemberDiff);
        Assert.Equal(
            FindingInspectionState.SubjectAbsent,
            Assert.IsType<
                FindingComparison<CSharpCanonicalLine>.Complete>(
                    csharp.Result.Findings.Value).Transition.New);
        Assert.Equal(
            FindingInspectionState.SubjectAbsent,
            Assert.IsType<
                FindingComparison<CanonicalIlOperation>.Complete>(
                    il.Result.Findings.Value).Transition.New);
    }

    [Fact]
    public void ResearchProducerAdapters_ClassifyNoEndpointInsideResearch()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.DiffV1.AssemblyPath()),
            Occurrence(FixtureCatalog.DiffV2.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            "DiffFixtureSample.BodyStateSample",
            "BodyState");

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution));
        var csharp = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedCSharp>(
                completion.Results[0].Outcome);
        var il = Assert.IsType<
            ResearchProducerWorkOutcome.ProducedIlBody>(
                completion.Results[1].Outcome);
        FindingInspectionTransition csharpTransition = Assert.IsType<
            FindingComparison<CSharpCanonicalLine>.Complete>(
                csharp.Result.Findings.Value).Transition;
        FindingInspectionTransition ilTransition = Assert.IsType<
            FindingComparison<CanonicalIlOperation>.Complete>(
                il.Result.Findings.Value).Transition;

        Assert.Equal(
            FindingInspectionState.NoApplicableInput,
            csharpTransition.Old);
        Assert.Equal(FindingInspectionState.Complete, csharpTransition.New);
        Assert.Equal(
            FindingInspectionState.NoApplicableInput,
            ilTransition.Old);
        Assert.Equal(FindingInspectionState.Complete, ilTransition.New);
        Assert.Null(csharp.Result.BodyDiff);
        Assert.Null(il.Result.MemberDiff);
    }

    [Fact]
    public void ResearchProducerSession_UsesOnlyValidatedAdmittedInputAccess()
    {
        string v1 = FixtureCatalog.DiffV1.AssemblyPath();
        string v2 = FixtureCatalog.DiffV2.AssemblyPath();
        int beforeOpens = 0;
        LibraryBodyIndex beforeIndex = LibraryBodyIndex.Open(v1);
        var changed = new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                beforeIndex.ModuleIdentity.AssemblyIdentity!,
                v1,
                () =>
                {
                    beforeOpens++;
                    return File.OpenRead(beforeOpens == 1 ? v1 : v2);
                },
                AssemblyResolutionProvenance.Project(
                    "DiffFixtures.V1",
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            beforeIndex);
        SessionFixture fixture = SessionFixture.Create(
            changed,
            Occurrence(v1));
        ResearchTargetResolution resolution = fixture.Resolve(
            "DiffFixtureSample.BodyStateSample",
            "BodyState");
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);

        Assert.Equal(0, invoker.InvocationCount);
        Assert.All(
            completion.Results,
            result => Assert.Equal(
                ResearchProducerUnavailableKind.ModuleIdentityMismatch,
                Assert.IsType<
                    ResearchProducerWorkOutcome.Unavailable>(result.Outcome)
                    .Reason.Kind));
        Assert.Single(completion.Cleanup);
    }

    [Fact]
    public void ResearchProducerSession_InvokesOnlyTotalNativeAdapters()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);

        Assert.Collection(
            invoker.EndpointShapes,
            shape => Assert.Equal(
                (ResearchProducerKind.CSharp, "Present", "Present"),
                shape),
            shape => Assert.Equal(
                (ResearchProducerKind.IlBody, "Present", "Present"),
                shape));
        Assert.Equal(2, completion.Results.Length);
    }

    [Fact]
    public void ResearchProducerResults_RetainExactNativeTopologyAndPayload()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);

        Assert.Same(
            Assert.Single(invoker.CSharpResults),
            Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
                completion.Results[0].Outcome).Result);
        Assert.Same(
            Assert.Single(invoker.IlResults),
            Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
                completion.Results[1].Outcome).Result);
    }

    [Fact]
    public void ResearchProducerSession_AccountsForTheExactWorkBudget()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);

        Assert.Equal(
            resolution.Correspondences.Length
                * ResearchProducerCatalog.Kinds.Length,
            completion.WorkItems.Length);
        Assert.Equal(completion.WorkItems.Length, completion.Results.Length);
        Assert.Equal(completion.WorkItems.Length, invoker.InvocationCount);
        Assert.Equal(
            completion.WorkItems.Length,
            completion.WorkItems.Select(item => item.Id).Distinct(
                ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void ResearchProducerSession_OwnsOnlyExactInputStages()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            (SampleType, "Method"),
            (SampleType, "Many:1"));
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            new ResearchProducerSessionRequest(
                fixture.Population,
                resolution,
                [ResearchProducerKind.CSharp]),
            invoker);

        Assert.Equal(4, invoker.CSharpSources.Count);
        Assert.Same(
            invoker.CSharpSources[0],
            invoker.CSharpSources[2]);
        Assert.Same(
            invoker.CSharpSources[1],
            invoker.CSharpSources[3]);
        Assert.Equal(2, completion.Cleanup.Length);
    }

    [Fact]
    public void ResearchProducerWorkItems_KeepUnavailableCorrespondenceWithoutInvocation()
    {
        int beforeOpens = 0;
        int afterOpens = 0;
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(
                FixtureCatalog.DiffV1.AssemblyPath(),
                () => beforeOpens++),
            Occurrence(
                FixtureCatalog.DiffV2.AssemblyPath(),
                () => afterOpens++));
        ResearchTargetResolution resolution = fixture.Resolve(
            "DiffFixtureSample.ConstructorRemovalSample",
            ".ctor:1");
        int beforeSession = beforeOpens;
        int afterSession = afterOpens;
        var invoker = new TrackingInvoker();

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);

        Assert.Equal(
            resolution.Correspondences.Length
                * ResearchProducerCatalog.Kinds.Length,
            completion.WorkItems.Length);
        Assert.All(
            completion.Results,
            result => Assert.Equal(
                ResearchProducerUnavailableKind.CorrespondenceUnavailable,
                Assert.IsType<
                    ResearchProducerWorkOutcome.Unavailable>(result.Outcome)
                    .Reason.Kind));
        Assert.Equal(0, invoker.InvocationCount);
        Assert.Equal(beforeSession, beforeOpens);
        Assert.Equal(afterSession, afterOpens);
        Assert.Empty(completion.Cleanup);
    }

    [Fact]
    public void ResearchProducerException_IsLocalAndDoesNotSuppressIndependentWork()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var invoker = new TrackingInvoker
        {
            ThrowCSharp = true,
        };

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution),
            invoker);
        var failed = Assert.IsType<
            ResearchProducerWorkOutcome.Failed>(
                completion.Results[0].Outcome);
        Assert.Equal(
            ResearchProducerDiagnosticKind.ProducerException,
            failed.Diagnostic.Kind);
        Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results[1].Outcome);
        Assert.Equal(2, invoker.InvocationCount);
        Assert.Equal(2, completion.Cleanup.Length);
        Assert.All(
            completion.Cleanup,
            outcome => Assert.IsType<
                ResearchProducerCleanupOutcome.Succeeded>(outcome));
    }

    [Fact]
    public void ResearchProducerCancellation_ExposesNoPartialWorkOrCompletion()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        using var cancellation = new CancellationTokenSource();
        var invoker = new TrackingInvoker
        {
            AfterCSharp = cancellation.Cancel,
        };

        var cancelled =
            Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
                ResearchProducerSession.Run(
                    Request(fixture, resolution),
                    invoker,
                    cancellation.Token));

        Assert.Equal(1, invoker.InvocationCount);
        Assert.Equal(2, cancelled.Cleanup.Length);
        Assert.All(
            cancelled.Cleanup,
            outcome => Assert.IsType<
                ResearchProducerCleanupOutcome.Succeeded>(outcome));

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        var notInvoked = new TrackingInvoker();
        var beforeAcquisition =
            Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
                ResearchProducerSession.Run(
                    Request(fixture, resolution),
                    notInvoked,
                    alreadyCancelled.Token));
        Assert.Equal(0, notInvoked.InvocationCount);
        Assert.Empty(beforeAcquisition.Cleanup);
    }

    [Fact]
    public void ResearchProducerCleanup_FailurePreventsCompletionWithoutSuppressingCleanup()
    {
        int beforeOpens = 0;
        int afterOpens = 0;
        string path = FixtureCatalog.ResearchTargetSample.AssemblyPath();
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(
                path,
                () => beforeOpens++,
                () => beforeOpens > 1),
            Occurrence(
                path,
                () => afterOpens++));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");

        var failed = Assert.IsType<ResearchProducerSessionOutcome.Failed>(
            ResearchProducerSession.Run(
                Request(fixture, resolution),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ResearchProducerDiagnosticKind.CleanupFailed,
            failed.Diagnostic.Kind);
        Assert.Equal(2, failed.Cleanup.Length);
        Assert.Single(
            failed.Cleanup,
            outcome => outcome
                is ResearchProducerCleanupOutcome.Failed);
        Assert.Single(
            failed.Cleanup,
            outcome => outcome
                is ResearchProducerCleanupOutcome.Succeeded);
    }

    [Fact]
    public void ResearchProducerWorkItemIdentities_AreFreshOwnerIssuedReferences()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var request = Request(fixture, resolution);

        ResearchProducerCompletion first = Complete(request);
        ResearchProducerCompletion second = Complete(request);

        Assert.NotSame(first.Session, second.Session);
        Assert.Same(first.Operation, second.Operation);
        Assert.Equal(first.WorkItems.Length, second.WorkItems.Length);
        for (int index = 0; index < first.WorkItems.Length; index++)
        {
            Assert.NotSame(
                first.WorkItems[index].Id,
                second.WorkItems[index].Id);
            Assert.Same(
                first.WorkItems[index].Correspondence,
                second.WorkItems[index].Correspondence);
        }
    }

    [Fact]
    public void ResearchProducerCleanup_AccountsForEveryOwnedResourceExactlyOnce()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution));

        Assert.Collection(
            completion.Cleanup,
            outcome => Assert.Same(
                fixture.Population.Inputs[1].Id,
                outcome.Input),
            outcome => Assert.Same(
                fixture.Population.Inputs[0].Id,
                outcome.Input));
        Assert.Equal(
            completion.Cleanup.Length,
            completion.Cleanup.Select(outcome => outcome.Input).Distinct(
                ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void ResearchProducerCompletion_AccountsForEveryWorkItemExactlyOnce()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");

        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution));

        Assert.Equal(completion.WorkItems.Length, completion.Results.Length);
        for (int index = 0; index < completion.WorkItems.Length; index++)
            Assert.Same(completion.WorkItems[index], completion.Results[index].Item);
        Assert.Equal(
            completion.WorkItems.Length,
            completion.Results.Select(result => result.Item).Distinct(
                ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void ResearchProducerCompletion_RejectsBrokenCrossLinksAndNativeKinds()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var request = Request(fixture, resolution);
        ResearchProducerCompletion valid = Complete(request);
        ImmutableArray<ResearchProducerWorkResult> wrongNativeKind =
            valid.Results.SetItem(
                0,
                new ResearchProducerWorkResult(
                    valid.WorkItems[0],
                    valid.Results[1].Outcome));

        Assert.False(
            ResearchProducerSessionValidator.TryCreateCompletion(
                request,
                valid.Session,
                valid.WorkItems,
                wrongNativeKind,
                [
                    .. valid.Cleanup.Reverse()
                        .Select(outcome => outcome.Input),
                ],
                valid.Cleanup,
                out _));

        var wrongSubject = new TrackingInvoker
        {
            ReturnWrongCSharpSubject = true,
        };
        var failed = Assert.IsType<ResearchProducerSessionOutcome.Failed>(
            ResearchProducerSession.Run(
                request,
                wrongSubject,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ResearchProducerDiagnosticKind.ProducerContractViolation,
            failed.Diagnostic.Kind);
        Assert.Equal(2, wrongSubject.InvocationCount);
        Assert.False(
            ResearchProducerSessionValidator.TryCreateCompletion(
                request,
                valid.Session,
                valid.WorkItems,
                valid.Results.RemoveAt(0),
                [
                    .. valid.Cleanup.Reverse()
                        .Select(outcome => outcome.Input),
                ],
                valid.Cleanup,
                out _));
    }

    [Fact]
    public void ResearchProducerCompletion_RetainsNoBorrowedResourcesOrPresentation()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        ResearchProducerCompletion completion = Complete(
            Request(fixture, resolution));
        Type[] forbidden =
        [
            typeof(ResolvedAssemblyReference),
            typeof(IAssemblyReferenceResolver),
            typeof(LibraryBodyIndex),
            typeof(MetadataSource),
            typeof(Stream),
            typeof(Delegate),
        ];

        Type[] producerTypes =
        [
            typeof(ResearchProducerCompletion),
            typeof(ResearchProducerSessionId),
            typeof(ResearchProducerWorkItem),
            typeof(ResearchProducerWorkItemId),
            typeof(ResearchProducerWorkResult),
            typeof(ResearchProducerWorkOutcome.ProducedCSharp),
            typeof(ResearchProducerWorkOutcome.ProducedIlBody),
            typeof(ResearchProducerWorkOutcome.Unavailable),
            typeof(ResearchProducerWorkOutcome.Failed),
            typeof(ResearchProducerCleanupOutcome.Succeeded),
            typeof(ResearchProducerCleanupOutcome.Failed),
        ];
        foreach (Type type in producerTypes)
        {
            Assert.DoesNotContain(
                type.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic),
                field => forbidden.Any(
                    denied => denied.IsAssignableFrom(field.FieldType)));
        }
        Assert.Same(
            fixture.Population.Operation,
            completion.Operation);
    }

    [Fact]
    public void ResearchProducerCancellation_RetainsEveryCleanupOutcome()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var invoker = new TrackingInvoker
        {
            AfterCSharp = cancellation.Cancel,
        };

        var cancelled =
            Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
                ResearchProducerSession.Run(
                    Request(fixture, resolution),
                    invoker,
                    cancellation.Token));

        Assert.Collection(
            cancelled.Cleanup,
            outcome => Assert.Same(
                fixture.Population.Inputs[1].Id,
                outcome.Input),
            outcome => Assert.Same(
                fixture.Population.Inputs[0].Id,
                outcome.Input));
    }

    [Fact]
    public void ResearchProducerCancellation_RetryMintsFreshSessionAndWorkItems()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(
            SampleType,
            "Method");
        var request = Request(fixture, resolution);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
            ResearchProducerSession.Run(
                request,
                new TrackingInvoker(),
                cancellation.Token));

        ResearchProducerCompletion first = Complete(request);
        ResearchProducerCompletion second = Complete(request);
        Assert.NotSame(first.Session, second.Session);
        for (int index = 0; index < first.WorkItems.Length; index++)
            Assert.NotSame(first.WorkItems[index].Id, second.WorkItems[index].Id);
    }

    static ResearchProducerSessionRequest Request(
        SessionFixture fixture,
        ResearchTargetResolution resolution)
        => new(
            fixture.Population,
            resolution,
            ResearchProducerCatalog.Kinds);

    static ResearchProducerRejection Reject(
        ResearchProducerSessionRequest request)
        => Assert.IsType<ResearchProducerSessionOutcome.Rejected>(
            ResearchProducerSession.Run(request)).Rejection;

    static ResearchProducerCompletion Complete(
        ResearchProducerSessionRequest request,
        IResearchProducerInvoker? invoker = null)
        => Assert.IsType<ResearchProducerSessionOutcome.Completed>(
            invoker is null
                ? ResearchProducerSession.Run(request)
                : ResearchProducerSession.Run(request, invoker))
            .Completion;

    static ImplementationComparisonInputOccurrence Occurrence(
        string path,
        Action? onOpen = null,
        Func<bool>? throwOnDispose = null)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        return new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                index.ModuleIdentity.AssemblyIdentity!,
                path,
                () =>
                {
                    onOpen?.Invoke();
                    Stream stream = File.OpenRead(path);
                    return throwOnDispose?.Invoke() == true
                        ? new ThrowOnDisposeStream(stream)
                        : stream;
                },
                AssemblyResolutionProvenance.Project(
                    Path.GetFileNameWithoutExtension(path),
                    tfm: null,
                    rid: null)),
            new NullResolver(),
            index);
    }

    sealed class SessionFixture
    {
        SessionFixture(ResearchAdmittedPopulation population)
            => Population = population;

        public ResearchAdmittedPopulation Population { get; }

        public static SessionFixture Create(
            ImplementationComparisonInputOccurrence before,
            ImplementationComparisonInputOccurrence after)
        {
            ResearchAdmittedPopulation population =
                Assert.IsType<ResearchAdmissionOutcome.Admitted>(
                    ResearchComparisonAdmission.Admit(
                        new ResearchComparisonAdmissionRequest(
                            ResearchComparisonProfile.ImplementationComparison,
                            [
                                new ResearchComparisonAdmissionQuestion(
                                    [before],
                                    [after]),
                            ]))).Population;
            return new SessionFixture(population);
        }

        public ResearchTargetResolution Resolve(
            string declaringType,
            string selector)
            => Resolve(
                declaringType,
                MemberTargetSelector.Parse(selector));

        public ResearchTargetResolution Resolve(
            string declaringType,
            MemberTargetSelector selector)
            => Resolve((declaringType, selector));

        public ResearchTargetResolution Resolve(
            params (string DeclaringType, string Selector)[] selections)
            => Resolve(
                [
                    .. selections.Select(
                        selection => (
                            selection.DeclaringType,
                            MemberTargetSelector.Parse(selection.Selector))),
                ]);

        ResearchTargetResolution Resolve(
            params (string DeclaringType, MemberTargetSelector Selector)[]
                selections)
        {
            ImmutableArray<ResearchTargetInputRoleAssignment?> roles =
            [
                .. Population.Inputs.Select(
                    input => new ResearchTargetInputRoleAssignment(
                        input,
                        ResearchTargetInputRole.Implementation)),
            ];
            return Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
                ResearchTargetResolver.Resolve(
                    new ResearchTargetPlanningRequest(
                        Population,
                        roles,
                        [
                            .. selections.Select(
                                selection =>
                                    new ResearchCarriedMemberSelection(
                                        Population.Questions[0].Id,
                                        selection.DeclaringType,
                                        selection.Selector)),
                        ]))).Resolution;
        }
    }

    sealed class TrackingInvoker : IResearchProducerInvoker
    {
        public bool ThrowCSharp { get; init; }

        public bool ReturnWrongCSharpSubject { get; init; }

        public Action? AfterCSharp { get; init; }

        public int InvocationCount { get; private set; }

        public List<MetadataSource> CSharpSources { get; } = [];

        public List<CSharpMemberEndpointComparison> CSharpResults { get; } = [];

        public List<IlMemberEndpointComparison> IlResults { get; } = [];

        public List<(ResearchProducerKind Kind, string Old, string New)>
            EndpointShapes { get; } = [];

        public CSharpMemberEndpointComparison CompareCSharp(
            CSharpMemberDiffEndpoint oldEndpoint,
            CSharpMemberDiffEndpoint newEndpoint)
        {
            InvocationCount++;
            if (ThrowCSharp)
                throw new InvalidOperationException("Injected producer escape.");
            if (ReturnWrongCSharpSubject)
            {
                var wrong = new FindingSubject("wrong", "wrong");
                return CSharpBodyDiff.CompareMemberEndpoints(
                    new CSharpMemberDiffEndpoint.SubjectAbsent(wrong),
                    new CSharpMemberDiffEndpoint.SubjectAbsent(wrong));
            }
            EndpointShapes.Add(
                (
                    ResearchProducerKind.CSharp,
                    oldEndpoint.GetType().Name,
                    newEndpoint.GetType().Name));
            CSharpSources.Add(
                Assert.IsType<CSharpMemberDiffEndpoint.Present>(oldEndpoint)
                    .Source);
            CSharpSources.Add(
                Assert.IsType<CSharpMemberDiffEndpoint.Present>(newEndpoint)
                    .Source);
            CSharpMemberEndpointComparison result =
                CSharpBodyDiff.CompareMemberEndpoints(
                    oldEndpoint,
                    newEndpoint);
            CSharpResults.Add(result);
            AfterCSharp?.Invoke();
            return result;
        }

        public IlMemberEndpointComparison CompareIl(
            IlMemberDiffEndpoint oldEndpoint,
            IlMemberDiffEndpoint newEndpoint)
        {
            InvocationCount++;
            EndpointShapes.Add(
                (
                    ResearchProducerKind.IlBody,
                    oldEndpoint.GetType().Name,
                    newEndpoint.GetType().Name));
            IlMemberEndpointComparison result =
                IlAssemblyDiff.CompareMemberEndpoints(
                oldEndpoint,
                newEndpoint);
            IlResults.Add(result);
            return result;
        }
    }

    sealed class NullResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => null;
    }

    sealed class ThrowOnDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin)
            => inner.Seek(offset, origin);

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
            throw new IOException("Injected cleanup failure.");
        }
    }
}
