using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed class ResourceOccurrenceAnalysisTests
{
    static readonly ResourceEffectModelIdentity PathologicalModel =
        new("test.resource-occurrence.pathological");
    static readonly ResourceEffectModelIdentity ValueFlowModel =
        new("test.resource-occurrence.value-flow");
    static readonly ResourceEffectModelIdentity ConflictOrdinaryModel =
        new("test.resource-occurrence.conflict-ordinary");
    static readonly ResourceEffectModelIdentity ConflictTransparentModel =
        new("test.resource-occurrence.conflict-transparent");
    static readonly ResourceEffectModelIdentity UnsupportedTargetModel =
        new("test.resource-occurrence.unsupported-target");
    static readonly ResourceEffectModelIdentity MixedTargetModel =
        new("test.resource-occurrence.mixed-target");
    static readonly ResourceEffectModelIdentity AuthorityTargetModel =
        new("test.resource-occurrence.authority-target");
    static readonly ResourceKindIdentity FirstKind =
        new("test.resource-occurrence.first");
    static readonly ResourceKindIdentity SecondKind =
        new("test.resource-occurrence.second");

    [Fact]
    public void ExecutePath_PublishesRootBoundArrayPoolOccurrences()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));

        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    admission),
                resolver);

        Assert.True(execution.ResourceOccurrences.WasRequested);
        Assert.True(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.JsonWireContractFlow));
        Assert.Same(
            execution.Receipt,
            execution.ResourceOccurrences.Receipt);
        Assert.Equal(
            admission.Receipt,
            execution.ResourceOccurrences.AdmissionReceipt);
        Assert.False(execution.HasMaterializedCompatibilityIndex);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentAndReturnDirectly");
        ResourceOccurrenceRoot.Acquisition root =
            Assert.IsType<ResourceOccurrenceRoot.Acquisition>(
                Assert.Single(method.Roots));
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            Assert.Single(root.ResourceKinds).Identity);
        Assert.Single(root.Authorities);
        Assert.Contains(
            method.Occurrences,
            occurrence =>
                occurrence.Root == root
                && occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Acquisition));
        Assert.Contains(
            method.Occurrences,
            occurrence =>
                occurrence.Root == root
                && occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Release));
        Assert.True(method.IsComplete);
        Assert.Empty(method.Limitations);
        Assert.Empty(execution.ResourceOccurrences.Limitations);
        Assert.True(execution.ResourceOwnership.WasRequested);
        Assert.Same(
            execution.Receipt,
            execution.ResourceOwnership.Receipt);
        Assert.Equal(
            admission.Receipt,
            execution.ResourceOwnership.AdmissionReceipt);
        ResourceOwnershipMethodSummary summary =
            Assert.Single(
                execution.ResourceOwnership.Methods,
                result =>
                    result.Method.Name
                    == "RentAndReturnDirectly");
        ResourceOwnershipAcquisitionFlow ownership =
            Assert.Single(summary.Acquisitions);
        Assert.Same(root, ownership.Obligation);
        Assert.Contains(
            ownership.Uses,
            use =>
                use.Kind == ResourceOwnershipUseKind.Released
                && Assert.Single(use.ResourceKinds).Identity
                    == ArrayPoolResourceEffectModel.BufferKind);
        Assert.Empty(execution.CallGraph.OwnershipEvidence);
        Assert.NotEmpty(
            execution.CallGraph.ResourceOwnershipSummaries);
    }

    [Fact]
    public void
        ExecutePath_OwnershipRecoversReleaseDomainForMixedSourceLocal()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);
        ResourceOccurrenceAnalysisResult occurrences =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                method =>
                    method.Method.Name
                    == "RentOrAllocateStoreThenReturn");
        ResourceOccurrenceLimitation limitation =
            Assert.Single(
                occurrences.Limitations,
                candidate =>
                    candidate.Effect is ResourceEffect.Release
                    && candidate.Root is null);
        Assert.Contains(
            limitation.ResourceKinds,
            kind =>
                kind.Identity
                    == ArrayPoolResourceEffectModel.BufferKind);

        ResourceOwnershipMethodSummary summary =
            Assert.Single(
                execution.ResourceOwnership.Methods,
                method =>
                    method.Method.Name
                    == "RentOrAllocateStoreThenReturn");
        ResourceOwnershipAcquisitionFlow ownership =
            Assert.Single(summary.Acquisitions);
        ResourceOwnershipUse release =
            Assert.Single(
                ownership.Uses,
                use =>
                    use.Kind == ResourceOwnershipUseKind.Released);
        Assert.Contains(
            release.ResourceKinds,
            kind =>
                kind.Identity
                    == ArrayPoolResourceEffectModel.BufferKind);
        Assert.False(ownership.IsComplete);
    }

    [Fact]
    public void ExecutePath_AssociatesAuthorityByBoundTargetProvenance()
    {
        ResourceEffectAdmissionOutcome admission =
            ResourceEffectAdmissionBuilder.Admit(
                [AuthorityTargetDefinition()]);
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    Assert.IsType<
                        ResourceEffectAdmissionOutcome.Admitted>(
                            admission).Admission),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name == "RentAndReturnFromHelper");
        ResourceOccurrenceRoot.Acquisition root =
            Assert.IsType<ResourceOccurrenceRoot.Acquisition>(
                Assert.Single(method.Roots));
        ResourceOccurrenceAuthority authority =
            Assert.Single(root.Authorities);
        ResourceEffect.Authority effect =
            Assert.IsType<ResourceEffect.Authority>(
                authority.Effect.Effect);

        Assert.IsType<ResourceEffectLocation.Return>(effect.Target);
    }

    [Fact]
    public void ExecutePath_KeepsIndependentAcquisitionRootsSeparate()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);

        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentTwoAndReturnDirectly");

        Assert.Equal(2, method.Roots.Length);
        foreach (ResourceOccurrenceRoot root in method.Roots)
        {
            Assert.Contains(
                method.Occurrences,
                occurrence =>
                    occurrence.Root == root
                    && occurrence.Operations.Contains(
                        ResourceOccurrenceOperationKind.Acquisition));
            Assert.Contains(
                method.Occurrences,
                occurrence =>
                    occurrence.Root == root
                    && occurrence.Operations.Contains(
                        ResourceOccurrenceOperationKind.Release));
        }
        Assert.True(method.IsComplete);
        Assert.Equal(
            method.Occurrences
                .Select(occurrence => occurrence.ILOffset)
                .Order(),
            method.Occurrences.Select(
                occurrence => occurrence.ILOffset));
    }

    static ResourceEffectModelDefinition AuthorityTargetDefinition()
    {
        ResourceTypeExpression.Named byteType = CoreType("Byte");
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(byteType);
        ResourceTypeExpression.Named arrayPool = new(
            new ResourceAssemblySelector(
                "System.Buffers",
                "cc7b13ffcd2ddd51",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System.Buffers",
            [new ResourceTypeNameSegment("ArrayPool", 1)],
            [byteType]);
        ResourceKindReference first = new(FirstKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            AuthorityTargetModel,
            [
                new ResourceKindDefinition(
                    FirstKind,
                    arity: 0,
                    [Provenance(AuthorityTargetModel, 0)]),
            ],
            [],
            [
                Declaration(
                    AuthorityTargetModel,
                    FixtureEntryType(),
                    "ReturnRentedArrayToCaller",
                    [byteArray],
                    byteArray,
                    new ResourceEffect.Acquire(
                        first,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        new ResourceEffectLocation.Parameter(0),
                        Lender: null),
                    1),
                Declaration(
                    AuthorityTargetModel,
                    arrayPool,
                    "Rent",
                    [CoreType("Int32")],
                    byteArray,
                    new ResourceEffect.Authority(
                        first,
                        new ResourceEffectLocation.Return(),
                        new ResourceAuthorityKey.Value()),
                    2,
                    isStatic: false),
                Declaration(
                    AuthorityTargetModel,
                    arrayPool,
                    "Rent",
                    [CoreType("Int32")],
                    byteArray,
                    new ResourceEffect.Authority(
                        first,
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceAuthorityKey.Value()),
                    3,
                    isStatic: false),
            ]);
    }

    [Fact]
    public void ExecutePath_GroupsSameCallEffectsForOneRoot()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentAndUseFrameworkWrappers");
        ResourceOccurrenceRoot root = Assert.Single(method.Roots);

        ResourceOccurrence grouped = method.Occurrences.First(
            occurrence =>
                occurrence.Root == root
                && occurrence.Effects.Any(effect =>
                    effect.Effect is ResourceEffect.Derive)
                && occurrence.Effects.Any(effect =>
                    effect.Effect is ResourceEffect.Operation));

        Assert.Contains(
            ResourceOccurrenceOperationKind.UnsupportedValueFlow,
            grouped.Operations);
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Root == root
                && limitation.Effect is ResourceEffect.Derive);
    }

    [Fact]
    public void ExecutePath_PublishesStorageAndReturnTerminals()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);

        ResourceOccurrenceAnalysisResult returned =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentAndReturnToCaller");
        Assert.Contains(
            returned.Occurrences,
            occurrence =>
                occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.ReturnToCaller));

        ResourceOccurrenceAnalysisResult stored =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentAndStoreDirectly");
        Assert.Contains(
            stored.Occurrences,
            occurrence =>
                occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Storage)
                && occurrence.Storage?.Identity is not null);
    }

    [Fact]
    public void ExecutePath_ScopesSameCallEffectsToTheirResolvedRoots()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    PathologicalAdmission()),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "ExerciseTwoResourceDomains");
        Assert.Equal(2, method.Roots.Length);
        ResourceOccurrenceRoot first = Assert.Single(
            method.Roots,
            root => Assert.Single(root.ResourceKinds).Identity
                == FirstKind);
        ResourceOccurrenceRoot second = Assert.Single(
            method.Roots,
            root => Assert.Single(root.ResourceKinds).Identity
                == SecondKind);
        int sharedOffset = Assert.Single(
            method.Occurrences
                .Where(occurrence =>
                    occurrence.Root == first
                    && occurrence.Effects.Any(effect =>
                        effect.Effect is ResourceEffect.Pass)))
            .ILOffset;
        ResourceOccurrence secondOccurrence =
            Assert.Single(
                method.Occurrences,
                occurrence =>
                    occurrence.Root == second
                    && occurrence.ILOffset == sharedOffset);

        Assert.Contains(
            secondOccurrence.Effects,
            effect => effect.Effect is ResourceEffect.Release);
        Assert.DoesNotContain(
            secondOccurrence.Effects,
            effect => effect.Effect is ResourceEffect.Pass);
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Root == first
                && limitation.Effect is ResourceEffect.Pass);
        Assert.DoesNotContain(
            method.Limitations,
            limitation =>
                limitation.Root == second
                && limitation.Effect is ResourceEffect.Pass);
        Assert.Contains(
            method.Occurrences,
            occurrence =>
                occurrence.Root == first
                && occurrence.ILOffset == sharedOffset
                && occurrence.Effects.Any(effect =>
                    effect.Effect is ResourceEffect.Authority));
        Assert.DoesNotContain(
            secondOccurrence.Effects,
            effect => effect.Effect is ResourceEffect.Authority);
    }

    [Fact]
    public void ExecutePath_KeepsUnresolvedEffectValueFlowVisible()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        ResourceEffectAdmissionOutcome admission =
            ResourceEffectAdmissionBuilder.Admit(
                [
                    ArrayPoolResourceEffectModel.Definition(),
                    ValueFlowDefinition(),
                ]);
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    Assert.IsType<
                        ResourceEffectAdmissionOutcome.Admitted>(
                            admission).Admission),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name
                    == "RentAddressThenObserve");

        Assert.Single(method.Roots);
        Assert.Contains(
            method.Occurrences,
            occurrence =>
                occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Acquisition));
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceOccurrenceLimitationKind.ValueFlow
                && limitation.Effect is ResourceEffect.Pass
                && limitation.Root is null);
    }

    [Fact]
    public void ExecutePath_KeepsUnresolvedOrdinaryValueFlowVisible()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name == "RentAddressThenObserve");

        Assert.Single(method.Roots);
        Assert.False(method.IsComplete);
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Kind == ResourceOccurrenceLimitationKind.ValueFlow
                && limitation.Effect is null
                && limitation.Call is not null);
    }

    [Fact]
    public void ExecutePath_DoesNotRunResourceOccurrencesWithoutAdmission()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.All));

        Assert.False(execution.ResourceOccurrences.WasRequested);
        Assert.Empty(execution.ResourceOccurrences.Methods);
        Assert.Empty(execution.ResourceOccurrences.Limitations);
        Assert.False(execution.HasMaterializedCompatibilityIndex);
    }

    [Fact]
    public void ExecutePath_DoesNotCollectValueFlowForMethodEvidenceAlone()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        DirectCall release = Assert.Single(
            execution.CallGraph.DirectCalls,
            call =>
                call.Caller.Name == "RentAndReturnDirectly"
                && call.Callee.Name == "Return");

        Assert.Empty(release.ResolvedArgumentValues);
        Assert.Null(release.ResolvedReceiverValue);
        Assert.False(execution.ResourceOccurrences.WasRequested);
    }

    [Fact]
    public void ExecutePath_ReportsMissingResolutionAuthority()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath(),
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()));

        Assert.True(execution.ResourceOccurrences.WasRequested);
        Assert.False(execution.ResourceOccurrences.IsComplete);
        ResourceOccurrenceLimitation limitation =
            Assert.Single(execution.ResourceOccurrences.Limitations);
        Assert.Equal(
            ResourceOccurrenceLimitationKind.EffectResolution,
            limitation.Kind);
        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .OccurrencePopulationRejected,
            limitation.EffectResolutionRejection);
        Assert.Empty(execution.ResourceOccurrences.Methods);
        Assert.True(execution.ResourceOwnership.WasRequested);
        Assert.False(execution.ResourceOwnership.IsComplete);
        Assert.NotEmpty(execution.ResourceOwnership.Methods);
        Assert.All(
            execution.ResourceOwnership.Methods,
            static summary => Assert.False(summary.IsComplete));
    }

    [Fact]
    public void AnalyzePath_RejectsFocusedOccurrenceRequest()
    {
        Assert.Throws<ArgumentException>(() =>
            LibraryBodyAnalysisService.AnalyzePath(
                FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath(),
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create())));
    }

    [Fact]
    public void ExecuteImage_PublishesOccurrencesWithoutReopeningSourceName()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "resource-occurrence-source-does-not-exist.dll",
                [.. File.ReadAllBytes(path)],
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);

        Assert.Contains(
            execution.ResourceOccurrences.Methods,
            result =>
                result.Method.Name == "RentAndReturnDirectly"
                && result.Occurrences.Any(occurrence =>
                    occurrence.Operations.Contains(
                        ResourceOccurrenceOperationKind.Release)));
    }

    [Fact]
    public void ExecutePath_PreservesUnaffectedCallsWhenEffectsConflict()
    {
        ResourceEffectAdmissionOutcome admission =
            ResourceEffectAdmissionBuilder.Admit(
                [
                    ArrayPoolResourceEffectModel.Definition(),
                    ConflictDefinition(
                        ConflictOrdinaryModel,
                        ResourceOperationBoundary.Ordinary),
                    ConflictDefinition(
                        ConflictTransparentModel,
                        ResourceOperationBoundary.Transparent),
                ]);
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    Assert.IsType<
                        ResourceEffectAdmissionOutcome.Admitted>(
                            admission).Admission),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name == "RentTwoAndReturnDirectly");

        Assert.Equal(
            2,
            method.Occurrences.Count(occurrence =>
                occurrence.Operations.Contains(
                    ResourceOccurrenceOperationKind.Release)));
        Assert.Contains(
            execution.ResourceOccurrences.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceOccurrenceLimitationKind.EffectResolution
                && limitation.Message.Contains(
                    "conflict",
                    StringComparison.Ordinal));
        ResourceOwnershipMethodSummary unaffected =
            Assert.Single(
                execution.ResourceOwnership.Methods,
                summary => summary.Method.Name == "StoreRentedArray");
        Assert.True(unaffected.IsComplete);
        Assert.Contains(
            Assert.Single(unaffected.Parameters).Uses,
            use => use.Kind == ResourceOwnershipUseKind.Stored);
        Assert.False(execution.ResourceOwnership.IsComplete);
    }

    [Fact]
    public void ExecutePath_ReportsUnsupportedAcquisitionTarget()
    {
        ResourceEffectAdmissionOutcome admission =
            ResourceEffectAdmissionBuilder.Admit(
                [UnsupportedTargetDefinition()]);
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    Assert.IsType<
                        ResourceEffectAdmissionOutcome.Admitted>(
                            admission).Admission),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name == "ExerciseTwoResourceDomains");

        Assert.Empty(method.Roots);
        Assert.Empty(method.Occurrences);
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Kind == ResourceOccurrenceLimitationKind.ValueFlow
                && limitation.Effect is ResourceEffect.Acquire);
        ResourceOwnershipMethodSummary ownership =
            Assert.Single(
                execution.ResourceOwnership.Methods,
                summary =>
                    summary.Method.MetadataToken
                        == method.Method.MetadataToken);
        Assert.False(ownership.IsComplete);
        Assert.False(execution.ResourceOwnership.IsComplete);
    }

    [Fact]
    public void ExecutePath_DoesNotAttachUnsupportedAcquisitionToValidRoot()
    {
        ResourceEffectAdmissionOutcome admission =
            ResourceEffectAdmissionBuilder.Admit(
                [MixedTargetDefinition()]);
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                    Assert.IsType<
                        ResourceEffectAdmissionOutcome.Admitted>(
                            admission).Admission),
                resolver);
        ResourceOccurrenceAnalysisResult method =
            Assert.Single(
                execution.ResourceOccurrences.Methods,
                result =>
                    result.Method.Name == "RentAndReturnFromHelper");
        ResourceOccurrence occurrence =
            Assert.Single(
                method.Occurrences,
                candidate =>
                    candidate.Operations.Contains(
                        ResourceOccurrenceOperationKind.Acquisition));
        ResourceOccurrenceEffect effect =
            Assert.Single(
                occurrence.Effects,
                candidate => candidate.Effect is ResourceEffect.Acquire);
        ResourceEffect.Acquire acquisition =
            Assert.IsType<ResourceEffect.Acquire>(effect.Effect);

        Assert.IsType<ResourceEffectLocation.Return>(acquisition.Target);
        Assert.Contains(
            method.Limitations,
            limitation =>
                limitation.Kind == ResourceOccurrenceLimitationKind.ValueFlow
                && limitation.Effect is ResourceEffect.Acquire
                {
                    Target: ResourceEffectLocation.Parameter,
                });
    }

    static ResourceEffectAdmission PathologicalAdmission()
    {
        ResourceTypeExpression.Named entry = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);
        ResourceTypeExpression.Named byteType = CoreType("Byte");
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(byteType);
        ResourceTypeExpression.Named voidType = CoreType("Void");
        ResourceKindReference first = new(FirstKind, []);
        ResourceKindReference second = new(SecondKind, []);
        ResourceEffectModelDefinition definition = new(
            ResourceEffectLanguageIdentity.Version1,
            PathologicalModel,
            [
                new ResourceKindDefinition(
                    FirstKind,
                    arity: 0,
                    [Provenance(PathologicalModel, 0)]),
                new ResourceKindDefinition(
                    SecondKind,
                    arity: 0,
                    [Provenance(PathologicalModel, 1)]),
            ],
            [],
            [
                Declaration(
                    PathologicalModel,
                    entry,
                    "AcquireFirstResource",
                    [],
                    byteArray,
                    new ResourceEffect.Acquire(
                        first,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    2),
                Declaration(
                    PathologicalModel,
                    entry,
                    "AcquireSecondResource",
                    [],
                    byteArray,
                    new ResourceEffect.Acquire(
                        second,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    3),
                Declaration(
                    PathologicalModel,
                    entry,
                    "ObserveTwoResources",
                    [byteArray, byteArray],
                    voidType,
                    new ResourceEffect.Pass(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectLocation.Parameter(0),
                        Identity: null),
                    4),
                Declaration(
                    PathologicalModel,
                    entry,
                    "ObserveTwoResources",
                    [byteArray, byteArray],
                    voidType,
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(1),
                        new ResourceEffectCompletion.NormalReturn(),
                        second,
                        Correspondence: null,
                        Observation: null),
                    5),
                Declaration(
                    PathologicalModel,
                    entry,
                    "ObserveTwoResources",
                    [byteArray, byteArray],
                    voidType,
                    new ResourceEffect.Authority(
                        first,
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceAuthorityKey.Value()),
                    6),
            ]);
        ResourceEffectAdmissionOutcome outcome =
            ResourceEffectAdmissionBuilder.Admit([definition]);
        return Assert.IsType<
            ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;
    }

    static ResourceEffectTypedDeclaration Declaration(
        ResourceEffectModelIdentity model,
        ResourceTypeExpression.Named declaringType,
        string name,
        ImmutableArray<ResourceTypeExpression> parameters,
        ResourceTypeExpression returnType,
        ResourceEffect effect,
        int ordinal,
        bool isStatic = true) =>
        new(
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    declaringType,
                    name,
                    ResourceEffectMemberKind.Method,
                    isStatic,
                    genericArity: 0,
                    ResourceEffectCallingConvention.Default,
                    hasThis: !isStatic,
                    explicitThis: false,
                    [
                        .. parameters.Select(parameter =>
                            new ResourceEffectParameterSelector(
                                parameter,
                                ResourceEffectRefKind.Value)),
                    ],
                    returnType)),
            effect,
            [Provenance(model, ordinal)]);

    static ResourceEffectModelDefinition ValueFlowDefinition()
    {
        ResourceTypeExpression.Named entry = FixtureEntryType();
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(CoreType("Byte"));
        return new(
            ResourceEffectLanguageIdentity.Version1,
            ValueFlowModel,
            [],
            [],
            [
                Declaration(
                    ValueFlowModel,
                    entry,
                    "ObserveResource",
                    [byteArray],
                    CoreType("Void"),
                    new ResourceEffect.Pass(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectLocation.Parameter(0),
                        Identity: null),
                    0),
            ]);
    }

    static ResourceEffectModelDefinition ConflictDefinition(
        ResourceEffectModelIdentity model,
        ResourceOperationBoundary boundary) =>
        new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [],
            [
                Declaration(
                    model,
                    FixtureEntryType(),
                    "ObserveResource",
                    [
                        new ResourceTypeExpression.SzArray(
                            CoreType("Byte")),
                    ],
                    CoreType("Void"),
                    new ResourceEffect.Operation(
                        boundary,
                        ResourceOperationThrows.Possible,
                        Guard: null),
                    0),
            ]);

    static ResourceEffectModelDefinition UnsupportedTargetDefinition()
    {
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(CoreType("Byte"));
        ResourceKindReference first = new(FirstKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            UnsupportedTargetModel,
            [
                new ResourceKindDefinition(
                    FirstKind,
                    arity: 0,
                    [Provenance(UnsupportedTargetModel, 0)]),
            ],
            [],
            [
                Declaration(
                    UnsupportedTargetModel,
                    FixtureEntryType(),
                    "ObserveTwoResources",
                    [byteArray, byteArray],
                    CoreType("Void"),
                    new ResourceEffect.Acquire(
                        first,
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    1),
            ]);
    }

    static ResourceEffectModelDefinition MixedTargetDefinition()
    {
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(CoreType("Byte"));
        ResourceKindReference first = new(FirstKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            MixedTargetModel,
            [
                new ResourceKindDefinition(
                    FirstKind,
                    arity: 0,
                    [Provenance(MixedTargetModel, 0)]),
            ],
            [],
            [
                Declaration(
                    MixedTargetModel,
                    FixtureEntryType(),
                    "ReturnRentedArrayToCaller",
                    [byteArray],
                    byteArray,
                    new ResourceEffect.Acquire(
                        first,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    1),
                Declaration(
                    MixedTargetModel,
                    FixtureEntryType(),
                    "ReturnRentedArrayToCaller",
                    [byteArray],
                    byteArray,
                    new ResourceEffect.Acquire(
                        first,
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    2),
            ]);
    }

    static ResourceTypeExpression.Named FixtureEntryType() =>
        new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);

    static ResourceTypeExpression.Named CoreType(string name) =>
        new(
            new ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System",
            [new ResourceTypeNameSegment(name, 0)]);

    static ResourceDeclarationProvenance Provenance(
        ResourceEffectModelIdentity model,
        int ordinal) =>
        new(
            model,
            ResourceDeclarationAuthority.CallerSupplied,
            new InertString(
                TextPolicy.Field,
                $"resource-occurrence-test:{ordinal}"),
            ordinal);
}
