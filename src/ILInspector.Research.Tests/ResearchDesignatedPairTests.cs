using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Fixtures;

using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public partial class ResearchProducerSessionTests
{
    [Fact]
    public void ResearchDesignatedPair_AdmitsExactSideLocalMethods()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.DiffV2.AssemblyPath()));
        ResearchTargetResolution resolution = ResolveExactPair(
            fixture, SampleType, "Method", "DiffFixtureSample.BodyStateSample", "BodyState");
        ResearchTargetAttempt before = Attempt(resolution, 0, ResearchComparisonSide.Before);
        ResearchTargetAttempt after = Attempt(resolution, 1, ResearchComparisonSide.After);
        ResearchDesignatedPair pair = Admit(fixture, resolution, before, after);

        Assert.Same(resolution, pair.Resolution);
        Assert.Same(before, pair.Before);
        Assert.Same(after, pair.After);
        Assert.Same(fixture.Population.Operation, pair.Operation);
        Assert.Same(fixture.Population.Questions[0].Id, pair.Question);
        Assert.NotSame(before.Request.Scope, after.Request.Scope);
        Assert.NotSame(before.Request.Domain, after.Request.Domain);
        Assert.NotEqual(
            Target(before).Anchor.CanonicalSignature,
            Target(after).Anchor.CanonicalSignature);

        SessionFixture same = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution sameResolution =
            ResolveExactPair(same, SampleType, "Method", SampleType, "Method");
        ResearchDesignatedPair samePair = Admit(
            same,
            sameResolution,
            Attempt(sameResolution, 0, ResearchComparisonSide.Before),
            Attempt(sameResolution, 1, ResearchComparisonSide.After));
        Assert.Equal(Target(samePair.Before).Address, Target(samePair.After).Address);
        Assert.NotSame(samePair.Before.Request.Input, samePair.After.Request.Input);
        Assert.Contains(
            sameResolution.Censuses,
            census => ReferenceEquals(census.Scope, samePair.Before.Request.Scope)
                && census.Side == ResearchComparisonSide.After
                && census.Health == ResearchTargetCensusHealth.Blocked);
        Assert.NotSame(
            samePair,
            Admit(same, sameResolution, samePair.Before, samePair.After));
        Assert.Equal(2, Complete(new(same.Population, samePair, ResearchProducerCatalog.Kinds))
            .Results.Length);
    }

    [Fact]
    public void ResearchDesignatedPair_PreservesAssociationFailures()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(SampleType, "Method");
        ResearchTargetAttempt before = Attempt(resolution, 0, ResearchComparisonSide.Before);
        ResearchTargetAttempt after = Attempt(resolution, 0, ResearchComparisonSide.After);
        ResearchTargetResolution other = fixture.Resolve(SampleType, "Method");

        Assert.Equal(
            ResearchDesignatedPairRejectionKind.ForeignAttempt,
            Assert.IsType<ResearchDesignatedPairOutcome.Rejected>(
                ResearchDesignatedPairAdmission.Admit(
                    fixture.Population, resolution, other.Attempts[0], after)).Kind);
        Assert.Equal(
            ResearchDesignatedPairRejectionKind.WrongSide,
            Assert.IsType<ResearchDesignatedPairOutcome.Rejected>(
                ResearchDesignatedPairAdmission.Admit(
                    fixture.Population, resolution, after, before)).Kind);
        Assert.Equal(
            ResearchDesignatedPairRejectionKind.MissingEndpoint,
            Assert.IsType<ResearchDesignatedPairOutcome.Rejected>(
                ResearchDesignatedPairAdmission.Admit(
                    fixture.Population, resolution, null, after)).Kind);
        var anotherPopulation = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        Assert.Equal(
            ResearchDesignatedPairRejectionKind.ForeignResolution,
            Assert.IsType<ResearchDesignatedPairOutcome.Rejected>(
                ResearchDesignatedPairAdmission.Admit(
                    anotherPopulation.Population, resolution, before, after)).Kind);
        Assert.Equal(
            ResearchProducerRejectionKind.ForeignResolution,
            Reject(new(anotherPopulation.Population,
                Admit(fixture, resolution, before, after), ResearchProducerCatalog.Kinds)).Kind);

        foreach (string selector in new[] { "Field", "NotHere", "Overloaded" })
        {
            ResearchTargetResolution unavailable = fixture.Resolve(SampleType, selector);
            var outcome = Assert.IsType<ResearchDesignatedPairOutcome.Unavailable>(
                ResearchDesignatedPairAdmission.Admit(
                    fixture.Population,
                    unavailable,
                    Attempt(unavailable, 0, ResearchComparisonSide.Before),
                    Attempt(unavailable, 0, ResearchComparisonSide.After)));
            Assert.Equal(
                new[] { ResearchComparisonSide.Before, ResearchComparisonSide.After },
                outcome.Endpoints.Select(endpoint => endpoint.Side));
            Assert.All(outcome.Endpoints, endpoint =>
            {
                Assert.Contains(endpoint.Attempt, unavailable.Attempts);
                Assert.Contains(endpoint.Census, unavailable.Censuses);
                Assert.Equal(
                    selector == "Field"
                        ? ResearchDesignatedPairUnavailableKind.EndpointAddressUnavailable
                        : ResearchDesignatedPairUnavailableKind.TargetUnavailable,
                    endpoint.Kind);
            });
        }

        var population = Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.ImplementationComparison,
                    [
                        new ResearchComparisonAdmissionQuestion(
                            [Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath())], []),
                        new ResearchComparisonAdmissionQuestion(
                            [], [Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath())]),
                    ]))).Population;
        var twoQuestions = new SessionFixture(population);
        ResearchTargetResolution crossQuestion = twoQuestions.ResolveSelections(
            new ResearchCarriedMemberSelection(
                population.Questions[0].Id, SampleType, MemberTargetSelector.Parse("Method")),
            new ResearchCarriedMemberSelection(
                population.Questions[1].Id, SampleType, MemberTargetSelector.Parse("Method")));
        Assert.Equal(
            ResearchDesignatedPairRejectionKind.CrossQuestion,
            Assert.IsType<ResearchDesignatedPairOutcome.Rejected>(
                ResearchDesignatedPairAdmission.Admit(
                    population, crossQuestion,
                    crossQuestion.Attempts[0], crossQuestion.Attempts[1])).Kind);

        SessionFixture images = SessionFixture.Create(
            Occurrence(FixtureCatalog.DiffV1.AssemblyPath()),
            Occurrence(FixtureCatalog.DiffV2.AssemblyPath()));
        const string bodyType = "DiffFixtureSample.BodyStateSample";
        ResearchTargetResolution imageTargets = images.Resolve(bodyType, "BodyState");
        var wrongImage = new ResearchExactAddressMemberSelection(
            images.Population.Questions[0].Id,
            images.Population.Inputs[0],
            bodyType,
            MemberTargetSelector.Parse("BodyState"),
            Target(Attempt(imageTargets, 0, ResearchComparisonSide.After)).Address!.Value,
            ResearchTargetRelationshipRole.Method);
        ResearchTargetResolution wrong = images.ResolveSelections(
            wrongImage,
            new ResearchCarriedMemberSelection(
                images.Population.Questions[0].Id,
                bodyType, MemberTargetSelector.Parse("BodyState")));
        var wrongOutcome = Assert.IsType<ResearchDesignatedPairOutcome.Unavailable>(
            ResearchDesignatedPairAdmission.Admit(
                images.Population, wrong,
                Attempt(wrong, 0, ResearchComparisonSide.Before),
                Attempt(wrong, 1, ResearchComparisonSide.After)));
        ResearchDesignatedPairUnavailable failedBefore = Assert.Single(wrongOutcome.Endpoints);
        Assert.Equal(ResearchComparisonSide.Before, failedBefore.Side);
        Assert.Equal(ResearchTargetCensusHealth.Blocked, failedBefore.Census.Health);
    }

    [Fact]
    public void ResearchDesignatedPair_DoesNotRequireCorrespondence()
    {
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution roles = fixture.Resolve(
            (SampleType, "Value:1"), (SampleType, "Value:2"), (SampleType, "NotHere"));
        ResearchDesignatedPair pair = Admit(
            fixture, roles,
            Attempt(roles, 0, ResearchComparisonSide.Before),
            Attempt(roles, 1, ResearchComparisonSide.After));
        Assert.NotEqual(Target(pair.Before).Role, Target(pair.After).Role);
        Assert.Contains(roles.Correspondences, value =>
            value is ResearchTargetCorrespondenceOutcome.Absent);
        Assert.Equal(2, Complete(new(fixture.Population, pair, ResearchProducerCatalog.Kinds))
            .Results.Length);

        ImplementationComparisonInputOccurrence original =
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath());
        var withoutBodyIdentity = new ImplementationComparisonInputOccurrence(
            original.Assembly,
            original.Resolver,
            LibraryBodyIndex.FromEvidence([], [], moduleIdentity: original.BodyIndex.ModuleIdentity));
        SessionFixture incomplete = SessionFixture.Create(
            withoutBodyIdentity,
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution missingKey = incomplete.Resolve(SampleType, "Method");
        Assert.Contains(missingKey.Correspondences, value => value is
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable
            {
                Taint.Kind: ResearchTargetTaintKind.BodyIdentityUnavailable,
            });
        ResearchDesignatedPair noKey = Admit(
            incomplete, missingKey,
            Attempt(missingKey, 0, ResearchComparisonSide.Before),
            Attempt(missingKey, 0, ResearchComparisonSide.After));
        Assert.Null(Target(noKey.Before).BodyIdentity);
        Assert.Equal(2, Complete(new(incomplete.Population, noKey, ResearchProducerCatalog.Kinds))
            .Results.Length);

        SessionFixture drift = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetCorrespondenceV1.AssemblyPath()),
            Occurrence(FixtureCatalog.ResearchTargetCorrespondenceV2.AssemblyPath()));
        ResearchTargetResolution divergent = drift.Resolve("CorrespondenceIdentity.Outer.Inner", "M");
        Assert.All(divergent.Correspondences, outcome => Assert.Equal(
            ResearchTargetTaintKind.SelectionDrift,
            Assert.IsType<ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(outcome)
                .Taint.Kind));
        ResearchDesignatedPair divergentPair = Admit(
            drift, divergent,
            Attempt(divergent, 0, ResearchComparisonSide.Before),
            Attempt(divergent, 0, ResearchComparisonSide.After));
        Assert.Equal(2, Complete(new(drift.Population, divergentPair, ResearchProducerCatalog.Kinds))
            .Results.Length);
        Assert.All(divergent.Correspondences, outcome =>
            Assert.IsType<ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(outcome));
    }

    [Fact]
    public void ResearchDesignatedSession_RetainsExactPairAndNativeResults()
    {
        int beforeOpens = 0;
        int afterOpens = 0;
        int unrelatedOpens = 0;
        var population = Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.ImplementationComparison,
                    [
                        new ResearchComparisonAdmissionQuestion(
                            [Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath(),
                                () => beforeOpens++)],
                            [Occurrence(FixtureCatalog.DiffV2.AssemblyPath(), () => afterOpens++)]),
                        new ResearchComparisonAdmissionQuestion(
                            [Occurrence(FixtureCatalog.DiffV1.AssemblyPath(), () => unrelatedOpens++)],
                            []),
                    ]))).Population;
        var fixture = new SessionFixture(population);
        ResearchTargetResolution resolution = ResolveExactPair(
            fixture, SampleType, "Method", "DiffFixtureSample.BodyStateSample", "BodyState",
            new ResearchCarriedMemberSelection(
                population.Questions[1].Id, "DiffFixtureSample.BodyStateSample",
                MemberTargetSelector.Parse("Missing")));
        ResearchDesignatedPair pair = Admit(
            fixture, resolution,
            Attempt(resolution, 0, ResearchComparisonSide.Before),
            Attempt(resolution, 1, ResearchComparisonSide.After));
        int beforeAdmission = beforeOpens;
        int afterAdmission = afterOpens;
        int unrelatedBeforeSession = unrelatedOpens;
        Assert.True(resolution.Correspondences.Length > 2);
        Admit(fixture, resolution, pair.Before, pair.After);
        Assert.Equal(beforeAdmission, beforeOpens);
        Assert.Equal(afterAdmission, afterOpens);
        int beforeAfterCSharp = 0;
        int afterAfterCSharp = 0;
        var invoker = new TrackingInvoker
        {
            AfterCSharp = () =>
            {
                beforeAfterCSharp = beforeOpens;
                afterAfterCSharp = afterOpens;
            },
        };
        var request = new ResearchProducerSessionRequest(fixture.Population, pair,
            [ResearchProducerKind.IlBody, ResearchProducerKind.CSharp]);
        ResearchProducerCompletion completion = Complete(request, invoker);
        Assert.Equal(ResearchProducerCatalog.Kinds, completion.WorkItems.Select(item => item.Producer));
        Assert.Equal(2, invoker.InvocationCount);
        Assert.True(beforeOpens > beforeAdmission);
        Assert.True(afterOpens > afterAdmission);
        Assert.Equal(2, completion.Cleanup.Length);
        Assert.Equal(beforeAfterCSharp, beforeOpens);
        Assert.Equal(afterAfterCSharp, afterOpens);
        Assert.Equal(unrelatedBeforeSession, unrelatedOpens);
        Assert.All(completion.WorkItems, item => Assert.Same(
            pair, Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(item.Basis).Pair));
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results[0].Outcome).Result;
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results[1].Outcome).Result;
        Assert.Same(invoker.CSharpResults[0], csharp);
        Assert.Same(invoker.IlResults[0], il);
        Assert.Equal(Target(pair.Before).Anchor.CanonicalSignature, csharp.Old.Key);
        Assert.Equal(Target(pair.After).Anchor.CanonicalSignature, csharp.New.Key);
        Assert.Equal(csharp.Old.Key, il.Old.Identity);
        Assert.Equal(csharp.New.Key, il.New.Identity);
        Assert.NotNull(csharp.BodyDiff);
        Assert.NotNull(il.MemberDiff);

        Assert.False(ResearchProducerSessionValidator.TryCreateCompletion(
            request, completion.Session, completion.WorkItems,
            completion.Results.SetItem(0,
                new ResearchProducerWorkResult(completion.WorkItems[0], completion.Results[1].Outcome)),
            [.. completion.Cleanup.Reverse().Select(item => item.Input)],
            completion.Cleanup, out _));

        SessionFixture bodyless = SessionFixture.Create(
            Occurrence(FixtureCatalog.DiffV1.AssemblyPath()),
            Occurrence(FixtureCatalog.DiffV2.AssemblyPath()));
        ResearchTargetResolution bodies = bodyless.Resolve("DiffFixtureSample.BodyStateSample", "BodyState");
        ResearchDesignatedPair bodyPair = Admit(
            bodyless, bodies,
            Attempt(bodies, 0, ResearchComparisonSide.Before),
            Attempt(bodies, 0, ResearchComparisonSide.After));
        ResearchProducerCompletion bodyResult = Complete(
            new(bodyless.Population, bodyPair, ResearchProducerCatalog.Kinds));
        var csharpBody = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            bodyResult.Results[0].Outcome).Result;
        var ilBody = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            bodyResult.Results[1].Outcome).Result;
        Assert.Equal(FindingInspectionState.NoApplicableInput,
            Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
                csharpBody.Findings.Value).Transition.Old);
        Assert.Equal(FindingInspectionState.NoApplicableInput,
            Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
                ilBody.Findings.Value).Transition.Old);
        Assert.Null(csharpBody.BodyDiff);
        Assert.Null(ilBody.MemberDiff);

        byte[] brokenImage = BrokenMethodImage();
        LibraryBodyIndex brokenIndex = LibraryBodyIndex.OpenFromPrefetchedImage(
            "BrokenMethod.dll", [.. brokenImage], LibraryBodyAnalysisFeatures.MethodEvidence);
        var brokenOccurrence = new ImplementationComparisonInputOccurrence(
            ResolvedAssemblyReference.Create(
                brokenIndex.ModuleIdentity.AssemblyIdentity!,
                path: null,
                () => new MemoryStream(brokenImage, writable: false),
                AssemblyResolutionProvenance.Project("BrokenMethod", tfm: null, rid: null)),
            new NullResolver(),
            brokenIndex);
        SessionFixture nativeFailure = SessionFixture.Create(
            brokenOccurrence, Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution broken = nativeFailure.Resolve(SampleType, "Method");
        ResearchDesignatedPair brokenPair = Admit(
            nativeFailure, broken,
            Attempt(broken, 0, ResearchComparisonSide.Before),
            Attempt(broken, 0, ResearchComparisonSide.After));
        ResearchProducerCompletion nativeFailed = Complete(
            new(nativeFailure.Population, brokenPair, ResearchProducerCatalog.Kinds));
        var failedCSharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            nativeFailed.Results[0].Outcome).Result;
        var failedIl = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            nativeFailed.Results[1].Outcome).Result;
        Assert.IsType<FindingComparison<CSharpCanonicalLine>.Failed>(failedCSharp.Findings.Value);
        Assert.IsType<FindingComparison<CanonicalIlOperation>.Failed>(failedIl.Findings.Value);
        Assert.Null(failedCSharp.BodyDiff);
        Assert.Null(failedIl.MemberDiff);
    }

    [Fact]
    public void ResearchDesignatedSession_PreservesAtomicTermination()
    {
        bool unreadable = false;
        bool failCleanup = false;
        SessionFixture fixture = SessionFixture.Create(
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath(),
                () =>
                {
                    if (unreadable)
                        throw new IOException("Unavailable test input.");
                },
                () => failCleanup),
            Occurrence(FixtureCatalog.ResearchTargetSample.AssemblyPath()));
        ResearchTargetResolution resolution = fixture.Resolve(SampleType, "Method");
        ResearchDesignatedPair pair = Admit(
            fixture, resolution,
            Attempt(resolution, 0, ResearchComparisonSide.Before),
            Attempt(resolution, 0, ResearchComparisonSide.After));
        var request = new ResearchProducerSessionRequest(
            fixture.Population, pair, ResearchProducerCatalog.Kinds);
        ResearchProducerCompletion first = Complete(request);
        ResearchProducerCompletion second = Complete(request);
        Assert.NotSame(first.Session, second.Session);
        for (int i = 0; i < first.WorkItems.Length; i++)
        {
            Assert.NotSame(first.WorkItems[i].Id, second.WorkItems[i].Id);
            Assert.Same(first.WorkItems[i].Basis, second.WorkItems[i].Basis);
        }
        Assert.Collection(first.Cleanup,
            value => Assert.Same(pair.After.Request.Input, value.Input),
            value => Assert.Same(pair.Before.Request.Input, value.Input));

        using var cancellation = new CancellationTokenSource();
        var finalInvoker = new TrackingInvoker { AfterCSharp = cancellation.Cancel };
        var cancelled = Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
            ResearchProducerSession.Run(
                new ResearchProducerSessionRequest(
                    fixture.Population, pair, [ResearchProducerKind.CSharp]),
                finalInvoker, cancellation.Token));
        Assert.Equal(1, finalInvoker.InvocationCount);
        Assert.Equal(2, cancelled.Cleanup.Length);
        Assert.All(cancelled.Cleanup,
            value => Assert.IsType<ResearchProducerCleanupOutcome.Succeeded>(value));
        Assert.Empty(Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
            ResearchProducerSession.Run(request, cancellation.Token)).Cleanup);

        unreadable = true;
        var notInvoked = new TrackingInvoker();
        ResearchProducerCompletion unavailable = Complete(request, notInvoked);
        Assert.Equal(0, notInvoked.InvocationCount);
        Assert.Empty(unavailable.Cleanup);
        Assert.All(unavailable.Results, result =>
        {
            ResearchProducerUnavailable reason =
                Assert.IsType<ResearchProducerWorkOutcome.Unavailable>(result.Outcome).Reason;
            Assert.Equal(ResearchProducerUnavailableKind.InputUnreadable, reason.Kind);
            Assert.Same(pair.Before.Request.Input, reason.Input);
        });

        unreadable = false;
        failCleanup = true;
        var failed = Assert.IsType<ResearchProducerSessionOutcome.Failed>(
            ResearchProducerSession.Run(request, TestContext.Current.CancellationToken));
        Assert.Equal(ResearchProducerDiagnosticKind.CleanupFailed, failed.Diagnostic.Kind);
        Assert.Collection(failed.Cleanup,
            value => Assert.IsType<ResearchProducerCleanupOutcome.Succeeded>(value),
            value => Assert.IsType<ResearchProducerCleanupOutcome.Failed>(value));

        using var cleanupCancellation = new CancellationTokenSource();
        var cleanupCancelled = Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(
            ResearchProducerSession.Run(request,
                new TrackingInvoker { AfterCSharp = cleanupCancellation.Cancel },
                cleanupCancellation.Token));
        Assert.Collection(cleanupCancelled.Cleanup,
            value => Assert.IsType<ResearchProducerCleanupOutcome.Succeeded>(value),
            value => Assert.IsType<ResearchProducerCleanupOutcome.Failed>(value));
    }

    static ResearchTargetAttempt Attempt(
        ResearchTargetResolution resolution, int scope, ResearchComparisonSide side)
        => Assert.Single(resolution.Attempts, attempt =>
            ReferenceEquals(attempt.Request.Scope, resolution.Scopes[scope].Id)
            && attempt.Request.Side == side);

    static byte[] BrokenMethodImage()
    {
        byte[] image = File.ReadAllBytes(FixtureCatalog.ResearchTargetSample.AssemblyPath());
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.MethodDefinitions
            .Select(reader.GetMethodDefinition)
            .First(method => reader.GetString(method.Name) == "Method");
        int rva = method.RelativeVirtualAddress;
        SectionHeader section = pe.PEHeaders.SectionHeaders.Single(
            section => rva >= section.VirtualAddress
                && rva < section.VirtualAddress + section.SizeOfRawData);
        // Preserve real metadata and provenance, but make native body decoding fail.
        image[section.PointerToRawData + rva - section.VirtualAddress] = 0;
        return image;
    }

    static ResearchTargetOutcome.Resolved Target(ResearchTargetAttempt attempt)
        => Assert.IsType<ResearchTargetOutcome.Resolved>(attempt.Outcome);

    static ResearchDesignatedPair Admit(
        SessionFixture fixture, ResearchTargetResolution resolution,
        ResearchTargetAttempt before, ResearchTargetAttempt after)
        => Assert.IsType<ResearchDesignatedPairOutcome.Admitted>(
            ResearchDesignatedPairAdmission.Admit(
                fixture.Population, resolution, before, after)).Pair;

    static ResearchTargetResolution ResolveExactPair(
        SessionFixture fixture,
        string beforeType, string beforeSelector,
        string afterType, string afterSelector,
        params ResearchMemberSelectionOccurrence[] additionalSelections)
    {
        ResearchTargetResolution resolved = fixture.Resolve(
            (beforeType, beforeSelector), (afterType, afterSelector));
        return fixture.ResolveSelections(
        [
            Exact(0, beforeType, beforeSelector, ResearchComparisonSide.Before),
            Exact(1, afterType, afterSelector, ResearchComparisonSide.After),
            .. additionalSelections,
        ]);

        ResearchExactAddressMemberSelection Exact(
            int scope, string type, string selector, ResearchComparisonSide side)
        {
            ResearchTargetAttempt attempt = Attempt(resolved, scope, side);
            ResearchTargetOutcome.Resolved target = Target(attempt);
            return new(
                attempt.Request.Question,
                fixture.Population.GetInput(attempt.Request.Input),
                type, MemberTargetSelector.Parse(selector), target.Address!.Value, target.Role);
        }
    }
}
