using System.Collections.Immutable;

using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    [Fact]
    public void ShippedArrayPoolModelResolvesFrameworkOperations()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    ResolveOwnershipFixture()));

        Assert.Collection(
            complete.Evaluations,
            evaluation => Assert.Equal(
                ResourceEffectTargetEvaluationKind.Resolved,
                evaluation.Kind),
            evaluation => Assert.Equal(
                ResourceEffectTargetEvaluationKind.Resolved,
                evaluation.Kind),
            evaluation => Assert.Equal(
                ResourceEffectTargetEvaluationKind.Unmatched,
                evaluation.Kind),
            evaluation => Assert.Equal(
                ResourceEffectTargetEvaluationKind.Resolved,
                evaluation.Kind));
        ResolvedResourceEffect authority =
            Assert.Single(
                complete.Snapshot.Effects.Where(
            effect =>
                effect.Effect is ResourceEffect.Authority
                && effect.DirectCall.Definition.Member.Name
                    == "get_Shared"
                && effect.DirectCall.Call.Caller.Name
                    == "RentAndReturnThroughHelper"));
        Assert.Equal(
            "Byte",
            Assert.Single(authority.AuthorityKeyArguments)
                .Type.Name);
        ResolvedResourceEffect rent = Assert.Single(
            complete.Snapshot.Effects.Where(
            effect =>
                effect.Effect is ResourceEffect.Acquire
                && effect.DirectCall.Call.Caller.Name
                    == "RentAndReturnThroughHelper"));
        Assert.Equal(
            "Byte",
            Assert.Single(rent.GenericBindings).Value.Type.Name);
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            Assert.Single(rent.ResourceKinds).Identity);
        Assert.IsType<ResourceEffectCompletion.NormalReturn>(
            rent.Applicability.Completion);
        Assert.Equal(64, complete.Receipt.ContentHash.Length);
        Assert.Same(
            complete.Receipt.Population.Generation,
            rent.DirectCall.Generation);
    }

    [Fact]
    public void ShippedArrayPoolModelRejectsSameNamedUserMethods()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    ResolveOwnershipFixture()));

        Assert.DoesNotContain(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Definition.Member.DeclaringType.Name
                    .Contains(
                        "OwnershipSink",
                        StringComparison.Ordinal));
    }

    [Fact]
    public void SelectorLimitRetainsPositiveEvidence()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        int firstShared = calls.Results
            .TakeWhile(result =>
                result.Call.Callee.Name != "get_Shared")
            .Count();

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    calls,
                    new ResourceEffectResolutionLimits(
                        maxSelectorEvaluations: firstShared + 1)));

        Assert.NotEmpty(incomplete.Effects);
        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .SelectorEvaluations);
    }

    [Fact]
    public void BodyDiagnosticPreventsCompletePopulationClaim()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixtureWithDiagnostics(1);
        CatalogCallGraphParticipant participant =
            Assert.Single(calls.Population);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    calls));

        Assert.NotEmpty(incomplete.Effects);
        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .PopulationIncomplete
                && gap.Participant == participant
                && gap.AnalysisDiagnostic is not null);
    }

    [Fact]
    public void EmptyAdmissionCannotHidePopulationFailure()
    {
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(
                    AdmitModels(),
                    ResolveOwnershipFixtureWithDiagnostics(100),
                    new ResourceEffectResolutionLimits(
                        maxRetainedGaps: 1)));

        Assert.Empty(incomplete.Evaluations);
        ResourceEffectResolutionGap gap =
            Assert.Single(incomplete.Gaps);
        Assert.Equal(
            ResourceEffectResolutionWorkDimension.RetainedGaps,
            gap.WorkDimension);
    }

    [Fact]
    public void CancelledEmptyAdmissionMintsNoReceipt()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ResourceEffectResolver.Resolve(
                AdmitModels(),
                ResolveOwnershipFixture(),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void MixedPositiveAndIncompleteEvidenceIsIncomplete()
    {
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved[] rents =
        [
            .. baseline.Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .Where(result =>
                    result.Definition.Member.Name == "Rent")
                .Take(2),
        ];
        Assert.Equal(2, rents.Length);
        DirectCallDefinitionResolution.Resolved unresolved = rents[1];
        var gap = new DirectCallDefinitionGap(
            DirectCallDefinitionGapKind.DefinitionUnavailable,
            unresolved.PhysicalInvocation,
            unresolved.Call.Kind);
        var incompleteCall =
            new DirectCallDefinitionResolution.Incomplete(
                baseline.Catalog,
                baseline.Generation,
                unresolved.Participant,
                unresolved.Call,
                gap);
        var mixedCalls =
            new DirectCallDefinitionResolutionOutcome.Completed(
                baseline.Catalog,
                baseline.Generation,
                baseline.Population,
                [rents[0], incompleteCall]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.mixed",
                RentTarget(),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(admission, mixedCalls));
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);

        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Incomplete,
            evaluation.Kind);
        Assert.Single(evaluation.Effects);
        Assert.Contains(
            evaluation.Gaps,
            item =>
                item.Kind
                    == ResourceEffectResolutionGapKind
                        .SelectorIncomplete);
    }

    [Fact]
    public void EqualEffectsCoalesceAllSources()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffect effect = new ResourceEffect.Operation(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            Guard: null);
        ResourceEffectAdmission admission = AdmitModels(
            Model("example.first", target, effect),
            Model("example.second", target, effect));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));

        Assert.NotEmpty(complete.Snapshot.Effects);
        Assert.All(
            complete.Snapshot.Effects,
            resolved => Assert.Equal(2, resolved.Sources.Length));
    }

    [Fact]
    public void CoalescenceRetainsDistinctDeclarationAssociations()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved rent =
            calls.Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .First(result =>
                    result.Definition.Member.Name == "Rent");
        ResourceEffectMemberSelector original =
            Assert.IsType<ResourceEffectTargetSelector.Member>(
                RentTarget()).Selector;
        ResourceTypeExpression.Named declaringType =
            Assert.IsType<ResourceTypeExpression.Named>(
                original.DeclaringType);
        AssemblyReferenceIdentity assembly = rent.Definition.Assembly;
        ResourceEffectTargetSelector any = TargetWithAssembly(
            original,
            declaringType,
            new ResourceAssemblySelector(
                assembly.Name,
                assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Any));
        ResourceEffectTargetSelector exact = TargetWithAssembly(
            original,
            declaringType,
            new ResourceAssemblySelector(
                assembly.Name,
                assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(
                    assembly.Version!)));
        var modelIdentity =
            new ResourceEffectModelIdentity("example.associations");
        ResourceDeclarationProvenance provenance =
            Provenance(modelIdentity.Value, 0);
        ResourceEffect effect = new ResourceEffect.Operation(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            Guard: null);
        var model = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            modelIdentity,
            [],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    any,
                    effect,
                    [provenance]),
                new ResourceEffectTypedDeclaration(
                    exact,
                    effect,
                    [provenance]),
            ]);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(AdmitModels(model), calls));

        Assert.NotEmpty(complete.Snapshot.Effects);
        Assert.All(
            complete.Snapshot.Effects,
            resolved => Assert.Equal(2, resolved.Sources.Length));
        Assert.All(
            complete.Snapshot.Effects,
            resolved => Assert.Equal(
                2,
                resolved.Sources
                    .Select(source => source.Declaration)
                    .Distinct()
                    .Count()));
    }

    [Fact]
    public void ConflictingEffectsFailAtomically()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.ordinary",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.transparent",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Transparent,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));

        Assert.NotEmpty(conflict.Conflicts);
        Assert.All(
            conflict.Conflicts,
            evidence => Assert.Equal(2, evidence.Effects.Length));
    }

    [Fact]
    public void InterfaceStaticCallEffectsStillConflict()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            Resolve(CreateInterfaceParticipant());
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                calls.Results[0]);
        Assert.True(interfaceCall.Definition.IsInterfaceDefinition);
        ResourceEffectTargetSelector target =
            TargetFor(interfaceCall.Definition);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-ordinary",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.interface-transparent",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Transparent,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                ResolveEffects(admission, calls));

        Assert.NotEmpty(conflict.Conflicts);
        Assert.Contains(
            conflict.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .DeferredInterfaceApplication);
    }

    [Fact]
    public void KnownConflictSurvivesCompatibilityExhaustion()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.boundary",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.transparent",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Transparent,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.throws",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Never,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture(),
                    new ResourceEffectResolutionLimits(
                        maxCompatibilityComparisons: 1)));

        Assert.NotEmpty(conflict.Conflicts);
        Assert.Contains(
            conflict.Gaps,
            gap =>
                gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .CompatibilityComparisons);
    }

    [Fact]
    public void KindlessAndSpecificEqualTransitionsAreCompatible()
    {
        ResourceEffectModelDefinition shipped =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectTypedDeclaration returned =
            shipped.TypedDeclarations[3];
        ResourceEffect.Release release =
            Assert.IsType<ResourceEffect.Release>(returned.Effect);
        ResourceEffectAdmission admission = AdmitModels(
            shipped,
            Model(
                "example.kindless",
                returned.Target,
                new ResourceEffect.Release(
                    release.Source,
                    release.When,
                    Kind: null,
                    release.Correspondence,
                    release.Observation)));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, ResolveOwnershipFixture()));
    }

    [Fact]
    public void DisjointSourcesDoNotConflict()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.receiver-release",
                target,
                new ResourceEffect.Release(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectCompletion.NormalReturn(),
                    Kind: null,
                    Correspondence: null,
                    Observation: null)),
            Model(
                "example.parameter-release",
                target,
                new ResourceEffect.Release(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectCompletion.NormalReturn(),
                    Kind: null,
                    Correspondence: null,
                    Observation: null)));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, ResolveOwnershipFixture()));
    }

    [Fact]
    public void CallBorrowAndNormalReturnReleaseAreCompatible()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.borrow",
                target,
                new ResourceEffect.Borrow(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectLocation.Return(),
                    ResourceBorrowAccess.Read,
                    new ResourceBorrowScope.Call(),
                    Kind: null,
                    Lender: null,
                    Materialization: null)),
            Model(
                "example.release",
                target,
                new ResourceEffect.Release(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectCompletion.NormalReturn(),
                    Kind: null,
                    Correspondence: null,
                    Observation: null)));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, ResolveOwnershipFixture()));
    }

    [Fact]
    public void DeferredOutcomeRetainsResolvedSiblingEffects()
    {
        ResourceEffectTargetSelector target = RentTarget();
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.operation",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.outcome",
                target,
                new ResourceEffect.Outcome(
                    new ResourceEffectLocalIdentity("result"),
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectOutcomeTest.NonNull())));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));

        Assert.NotEmpty(incomplete.Effects);
        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.DeferredKind
                    == ResourceEffectDeferredKind.Outcome);
    }

    [Fact]
    public void ReceiptIsStableForSameInputs()
    {
        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();

        ResourceEffectResolutionOutcome.Complete first =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));
        ResourceEffectResolutionOutcome.Complete second =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));

        Assert.Equal(first.Receipt, second.Receipt);
        Assert.Equal(
            first.Receipt.ContentHash,
            second.Receipt.ContentHash);
    }

    [Fact]
    public void ReceiptChangesWithMetadataGeneration()
    {
        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        ResourceEffectResolutionOutcome.Complete first =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));
        ResourceEffectResolutionOutcome.Complete second =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));

        Assert.NotEqual(first.Receipt, second.Receipt);
        Assert.NotEqual(
            first.Receipt.ContentHash,
            second.Receipt.ContentHash);
    }

    [Fact]
    public void RemovingModelContentChangesReceipt()
    {
        ResourceEffectModelDefinition full =
            ArrayPoolResourceEffectModel.Definition();
        var reduced = new ResourceEffectModelDefinition(
            full.Language,
            full.Identity,
            full.ResourceKinds,
            full.Declarations,
            full.TypedDeclarations.RemoveAt(0));
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    calls));
        ResourceEffectResolutionOutcome.Complete withoutAuthority =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    AdmitModels(reduced),
                    calls));

        Assert.NotEqual(
            complete.Receipt.ContentHash,
            withoutAuthority.Receipt.ContentHash);
        Assert.DoesNotContain(
            withoutAuthority.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Authority);
    }

    [Fact]
    public void ForeignAdmissionReceiptIsRejected()
    {
        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        ResourceEffectAdmission other = AdmitModels(
            Model(
                "example.other",
                RentTarget(),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        ResourceEffectResolutionRequest valid =
            ResourceEffectResolver.CreateRequest(admission, calls);
        var foreign = new ResourceEffectResolutionRequest(
            admission,
            other.Receipt,
            calls,
            valid.PopulationReceipt);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResolveEffects(foreign));

        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .AdmissionReceiptMismatch,
            rejected.Kind);
    }

    [Fact]
    public void StalePopulationReceiptIsRejected()
    {
        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        DirectCallDefinitionResolutionOutcome.Completed firstCalls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolutionOutcome.Completed secondCalls =
            ResolveOwnershipFixture();
        ResourceEffectResolutionRequest first =
            ResourceEffectResolver.CreateRequest(
                admission,
                firstCalls);
        var stale = new ResourceEffectResolutionRequest(
            admission,
            admission.Receipt,
            secondCalls,
            first.PopulationReceipt);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResolveEffects(stale));

        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .OccurrencePopulationReceiptMismatch,
            rejected.Kind);
    }

    static ResourceEffectTargetSelector RentTarget() =>
        ArrayPoolResourceEffectModel.Definition()
            .TypedDeclarations[1]
            .Target;

    static ResourceEffectTargetSelector TargetWithAssembly(
        ResourceEffectMemberSelector original,
        ResourceTypeExpression.Named declaringType,
        ResourceAssemblySelector assembly) =>
        new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                new ResourceTypeExpression.Named(
                    assembly,
                    declaringType.Namespace,
                    declaringType.Segments,
                    declaringType.Arguments),
                original.MetadataName,
                original.Kind,
                original.IsStatic,
                original.GenericArity,
                original.CallingConvention,
                original.HasThis,
                original.ExplicitThis,
                original.Parameters,
                original.ReturnType));

    static ResourceEffectTargetSelector TargetFor(
        DirectCallDefinitionOccurrence definition)
    {
        MemberRef member = definition.Member;
        TypeRef declaringType = member.DeclaringType;
        var selectorType = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                definition.Assembly.Name,
                definition.Assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(
                    definition.Assembly.Version!)),
            declaringType.Namespace,
            [new ResourceTypeNameSegment(declaringType.Name, 0)]);
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                selectorType,
                member.Name,
                ResourceEffectMemberKind.Method,
                isStatic: false,
                member.GenericArity,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                parameters: [],
                CoreType("Void")));
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        ResolveOwnershipFixtureWithDiagnostics(int count)
    {
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            ResolveOwnershipFixture();
        CatalogCallGraphParticipant original =
            Assert.Single(baseline.Population);
        MethodIdentity method = original.Index.Methods[0];
        ImmutableArray<AnalysisDiagnostic> diagnostics =
        [
            .. Enumerable.Range(0, count).Select(index =>
                new AnalysisDiagnostic(
                    method.MetadataToken + index,
                    method.Name,
                    "Synthetic body failure")),
        ];
        LibraryBodyIndex incompleteIndex = LibraryBodyIndex.FromEvidence(
            original.Index.Methods,
            original.Index.UnsafeEvidence,
            diagnostics: diagnostics,
            directCalls: original.Index.DirectCalls,
            moduleIdentity: original.Index.ModuleIdentity);
        var participant = new CatalogCallGraphParticipant(
            incompleteIndex,
            original.Assembly);
        return Assert.IsType<
            DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            OwnershipFixturePath)),
                    [participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
    }

    static ResourceEffectResolutionOutcome ResolveEffects(
        ResourceEffectAdmission admission,
        DirectCallDefinitionResolutionOutcome.Completed calls,
        ResourceEffectResolutionLimits? limits = null) =>
        ResourceEffectResolver.Resolve(
            admission,
            calls,
            limits,
            TestContext.Current.CancellationToken);

    static ResourceEffectResolutionOutcome ResolveEffects(
        ResourceEffectResolutionRequest request,
        ResourceEffectResolutionLimits? limits = null) =>
        ResourceEffectResolver.Resolve(
            request,
            limits,
            TestContext.Current.CancellationToken);

    static ResourceEffectModelDefinition Model(
        string identity,
        ResourceEffectTargetSelector target,
        ResourceEffect effect) =>
        new(
            ResourceEffectLanguageIdentity.Version1,
            new ResourceEffectModelIdentity(identity),
            [],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    target,
                    effect,
                    [Provenance(identity, 0)]),
            ]);

    static ResourceEffectAdmission AdmitModels(
        params ResourceEffectModelDefinition[] models) =>
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(models)).Admission;

    static ResourceDeclarationProvenance Provenance(
        string model,
        int ordinal) =>
        new(
            new ResourceEffectModelIdentity(model),
            ResourceDeclarationAuthority.CallerSupplied,
            new InertString(
                TextPolicy.Field,
                $"{model}.{ordinal}"),
            ordinal);
}
