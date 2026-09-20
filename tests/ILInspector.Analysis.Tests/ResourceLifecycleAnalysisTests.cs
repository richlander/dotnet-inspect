using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;
using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

public sealed class ResourceLifecycleAnalysisTests
{
    static readonly ResourceEffectModelIdentity ThrowsNeverModel =
        new("test.resource-lifecycle-throws-never");
    static readonly ResourceEffectModelIdentity InterfaceLifecycleModel =
        new("test.resource-lifecycle-interface");
    static readonly ResourceKindIdentity InterfaceBufferKind =
        new("test.resource-lifecycle.interface-buffer");

    [Fact]
    public void OccurrenceOnlyRequest_DoesNotSelectLifecycle()
    {
        LibraryBodyAnalysisExecution execution = Analyze(
            LibraryBodyAnalysisRequest.CreateResourceOccurrences(
                ArrayPoolResourceEffectModel.Create()));

        Assert.True(execution.ResourceOccurrences.WasRequested);
        Assert.False(execution.ResourceLifecycle.WasRequested);
        Assert.Empty(execution.ResourceLifecycle.Methods);
    }

    [Fact]
    public void LifecycleRequest_PublishesCleanDirectRelease()
    {
        LibraryBodyAnalysisExecution execution = Analyze();
        ResourceLifecycleRootResult root =
            Root(execution, "RentAndReturnDirectly");

        Assert.True(execution.ResourceLifecycle.WasRequested);
        Assert.Same(
            execution.Receipt,
            execution.ResourceLifecycle.Receipt);
        Assert.Equal(
            execution.ResourceOccurrences.AdmissionReceipt,
            execution.ResourceLifecycle.AdmissionReceipt);
        Assert.Empty(root.Outcomes);
        Assert.True(root.IsComplete);
        Assert.False(execution.HasMaterializedCompatibilityIndex);
    }

    [Fact]
    public void LifecycleRequest_DerivesSupportedMisuseOutcomes()
    {
        LibraryBodyAnalysisExecution execution = Analyze();

        AssertOutcome(
            execution,
            "RentWithoutReturn",
            ResourceLifecycleOutcomeKind.MissingReleaseOnNormalPath);
        AssertOutcome(
            execution,
            "RentUseAfterReturn",
            ResourceLifecycleOutcomeKind.UseAfterRelease);
        AssertOutcome(
            execution,
            "RentDoubleReturn",
            ResourceLifecycleOutcomeKind.DoubleRelease);
        AssertOutcome(
            execution,
            "RentAndReturnToCaller",
            ResourceLifecycleOutcomeKind.InvalidTransfer);
        AssertOutcome(
            execution,
            "RentAndStoreDirectly",
            ResourceLifecycleOutcomeKind.InvalidTransfer);
    }

    [Fact]
    public void LifecycleRequest_AppliesInterfaceEffectsToConcreteCalls()
    {
        ResourceEffectAdmissionOutcome outcome =
            ResourceEffectAdmissionBuilder.Admit(
                [InterfaceLifecycleDefinition()]);
        ResourceEffectAdmission admission = Assert.IsType<
            ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;

        ResourceLifecycleRootResult root = Root(
            Analyze(
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    admission)),
            "AcquireAndReleaseThroughConcreteInterface");

        Assert.True(root.IsComplete);
        Assert.Empty(root.Outcomes);
    }

    [Theory]
    [InlineData("RentAndReturnOnEitherBranch")]
    [InlineData("RentAndReturnOnSomeBranches")]
    public void LifecycleRequest_MarksMultipleReleaseSitesIncomplete(
        string methodName)
    {
        ResourceLifecycleRootResult root = Root(Analyze(), methodName);

        Assert.False(root.IsComplete);
        ResourceLifecycleLimitation limitation = Assert.Single(
            root.Limitations,
            candidate =>
                candidate.Kind
                    == ResourceLifecycleLimitationKind.UnsupportedFlow);
        Assert.Contains(
            "multiple release sites",
            limitation.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleRequest_DerivesExactUnprotectedBoundary()
    {
        LibraryBodyAnalysisExecution execution = Analyze();
        ResourceLifecycleOutcome outcome = Assert.Single(
            Root(execution, "RentAcrossThrowingBoundary")
                .Outcomes,
            candidate =>
                candidate.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);

        ResourceLifecycleBoundaryEvidence boundary =
            Assert.Single(outcome.Boundaries);
        Assert.Equal(outcome.PrimaryOffset, boundary.ILOffset);
        Assert.Equal("ObserveResource", boundary.Call.Callee.Name);
    }

    [Fact]
    public void LifecycleRequest_CreditsFinallyCleanup()
    {
        LibraryBodyAnalysisExecution execution = Analyze();
        ResourceLifecycleRootResult root =
            Root(execution, "RentAcrossProtectedThrowingBoundary");

        Assert.True(
            root.IsComplete,
            string.Join(
                "; ",
                root.Limitations.Select(static limitation =>
                    limitation.Detail)));
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);
    }

    [Theory]
    [InlineData(
        "RentAcrossUnprotectedBoundaryWithUnrelatedMethodGroup",
        "ObserveResource")]
    [InlineData(
        "RentWithLeadingMethodGroup",
        "OwnershipSinkWithLeadingCallback")]
    public void LifecycleRequest_PreservesLegacyReportableMethodGroupShapes(
        string methodName,
        string boundaryName)
    {
        ResourceLifecycleRootResult root = Root(
            Analyze(),
            methodName);

        ResourceLifecycleOutcome outcome = Assert.Single(
            root.Outcomes,
            candidate =>
                candidate.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
        Assert.Contains(
            outcome.Boundaries,
            boundary =>
                boundary.Call.Callee.Name == boundaryName);
    }

    [Fact]
    public void LifecycleRequest_DoesNotCreditConditionalFinallyCleanup()
    {
        LibraryResourceLifecycleAnalysisResult lifecycle =
            Analyze().ResourceLifecycle;
        ResourceLifecycleMethodResult method = Assert.Single(
            lifecycle.Methods,
            result =>
                result.Method.Name
                    == "RentAcrossConditionalFinally");
        ResourceLifecycleRootResult root = Assert.Single(method.Roots);

        Assert.False(root.IsComplete);
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
        Assert.Contains(
            root.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceLifecycleLimitationKind.ExceptionFlow
                && limitation.Detail.Contains(
                    "not proven",
                    StringComparison.Ordinal));

        var inspection = ResourceLifecycleAnalysis.Inspect(
            lifecycle with { Methods = [method] },
            new FindingSubject("fixture", "fixture"));
        Assert.IsType<
            FindingInspection<ResourceLifecycleOccurrence>.Failed>(
                inspection.Value);
    }

    [Fact]
    public void LifecycleRequest_DoesNotCreditThrowingCleanupSetup()
    {
        ResourceLifecycleRootResult root =
            Root(Analyze(), "RentAcrossThrowingCleanupSetup");

        Assert.False(root.IsComplete);
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
        Assert.Contains(
            root.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceLifecycleLimitationKind.ExceptionFlow);
    }

    [Fact]
    public void LifecycleRequest_DoesNotCreditConstructorCleanupSetup()
    {
        ResourceLifecycleRootResult root =
            Root(Analyze(), "RentAcrossConstructorCleanupSetup");

        Assert.False(root.IsComplete);
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
        Assert.Contains(
            root.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceLifecycleLimitationKind.ExceptionFlow);
    }

    [Fact]
    public void LifecycleRequest_DoesNotCreditFallibleLegacySetup()
    {
        ResourceLifecycleRootResult root =
            Root(Analyze(), "RentAcrossArrayClearCleanupSetup");

        Assert.False(root.IsComplete);
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
        Assert.Contains(
            root.Limitations,
            limitation =>
                limitation.Kind
                    == ResourceLifecycleLimitationKind.ExceptionFlow
                && limitation.Detail.Contains(
                    "not proven nonthrowing",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LifecycleRequest_CreditsExactThrowsNeverBoundary()
    {
        ResourceEffectAdmissionOutcome outcome =
            ResourceEffectAdmissionBuilder.Admit(
                [
                    ArrayPoolResourceEffectModel.Definition(),
                    ThrowsNeverDefinition(),
                ]);
        ResourceEffectAdmission admission = Assert.IsType<
            ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;

        ResourceLifecycleRootResult root = Root(
            Analyze(
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    admission)),
            "RentAcrossThrowingBoundary");

        Assert.DoesNotContain(
            root.Outcomes,
            candidate =>
                candidate.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);
    }

    [Fact]
    public void LifecycleRequest_PreservesLegacyNonThrowingSetupBoundary()
    {
        ResourceLifecycleRootResult root =
            Root(Analyze(), "RentAndForwardExternally");

        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
    }

    [Theory]
    [InlineData("RentWithMethodGroup")]
    [InlineData("RentAddressThenObserve")]
    public void LifecycleRequest_PreservesLegacyAmbiguousFlowSuppression(
        string methodName)
    {
        ResourceLifecycleRootResult root = Root(Analyze(), methodName);

        Assert.False(root.IsComplete);
        Assert.NotEmpty(root.Limitations);
        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                    == ResourceLifecycleOutcomeKind
                        .ExceptionalCleanupMissing);
    }

    [Fact]
    public void LifecycleRequest_MatchesNestedThrowsNeverDeclaration()
    {
        ResourceEffectAdmissionOutcome outcome =
            ResourceEffectAdmissionBuilder.Admit(
                [
                    ArrayPoolResourceEffectModel.Definition(),
                    ThrowsNeverDefinition(
                        new(
                            new ResourceAssemblySelector(
                                "ILInspector.Analysis.OwnershipFlowFixtures",
                                publicKeyToken: null,
                                ResourceAssemblyVersionPolicy.Any),
                            "Ownership",
                            [
                                new ResourceTypeNameSegment("Entry", 0),
                                new ResourceTypeNameSegment(
                                    "OwnershipSink",
                                    0),
                            ])),
                ]);
        ResourceEffectAdmission admission = Assert.IsType<
            ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;

        ResourceLifecycleRootResult root = Root(
            Analyze(
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    admission)),
            "RentAcrossNestedThrowingBoundary");

        Assert.DoesNotContain(
            root.Outcomes,
            candidate =>
                candidate.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);
    }

    [Fact]
    public void LifecycleRequest_KeepsIndependentRootComplete()
    {
        LibraryBodyAnalysisExecution execution = Analyze();
        ResourceLifecycleMethodResult method = Assert.Single(
            execution.ResourceLifecycle.Methods,
            result =>
                result.Method.Name
                == "RentTwoWithSecondAddress");

        Assert.Equal(2, method.Roots.Length);
        Assert.Contains(method.Roots, root => root.IsComplete);
        Assert.Contains(method.Roots, root => !root.IsComplete);
    }

    [Fact]
    public void LifecycleProjection_DoesNotFlattenIncompletenessToEmpty()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        LibraryResourceLifecycleAnalysisResult lifecycle =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create()))
            .ResourceLifecycle;

        var inspection = ResourceLifecycleAnalysis.Inspect(
            lifecycle,
            new FindingSubject("fixture", "fixture"));

        Assert.IsType<
            FindingInspection<ResourceLifecycleOccurrence>.Failed>(
                inspection.Value);
    }

    [Fact]
    public void LifecycleProjection_DoesNotIgnoreLibraryLimitation()
    {
        LibraryResourceLifecycleAnalysisResult lifecycle =
            Analyze().ResourceLifecycle;
        ResourceLifecycleMethodResult completeMethod = Assert.Single(
            lifecycle.Methods,
            method => method.Method.Name == "RentAndReturnDirectly");
        lifecycle = lifecycle with
        {
            Methods = [completeMethod],
            Limitations =
            [
                new ResourceLifecycleLimitation(
                    ResourceLifecycleLimitationKind.ResourceOccurrence,
                    "Interface application coverage was incomplete."),
            ],
        };

        var inspection = ResourceLifecycleAnalysis.Inspect(
            lifecycle,
            new FindingSubject("fixture", "fixture"));

        Assert.IsType<
            FindingInspection<ResourceLifecycleOccurrence>.Failed>(
                inspection.Value);
    }

    static LibraryBodyAnalysisExecution Analyze(
        LibraryBodyAnalysisRequest? request = null)
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        return LibraryBodyAnalysisService.ExecutePath(
            path,
            request
                ?? LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create()),
            resolver);
    }

    static ResourceLifecycleRootResult Root(
        LibraryBodyAnalysisExecution execution,
        string methodName) =>
        Assert.Single(
            Assert.Single(
                execution.ResourceLifecycle.Methods,
                result => result.Method.Name == methodName)
                .Roots);

    static void AssertOutcome(
        LibraryBodyAnalysisExecution execution,
        string methodName,
        ResourceLifecycleOutcomeKind kind)
    {
        ResourceLifecycleRootResult root = Root(execution, methodName);
        Assert.True(
            root.IsComplete,
            string.Join(
                "; ",
                root.Limitations.Select(limitation =>
                    $"{limitation.Kind}: {limitation.Detail}")));
        Assert.Contains(
            root.Outcomes,
            outcome => outcome.Kind == kind);
    }

    static ResourceEffectModelDefinition ThrowsNeverDefinition(
        ResourceTypeExpression.Named? declaringType = null)
    {
        ResourceTypeExpression.Named byteType = CoreType("Byte");
        return new(
            ResourceEffectLanguageIdentity.Version1,
            ThrowsNeverModel,
            [],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    new ResourceEffectTargetSelector.Member(
                        new ResourceEffectMemberSelector(
                            declaringType ?? FixtureEntryType(),
                            "ObserveResource",
                            ResourceEffectMemberKind.Method,
                            isStatic: true,
                            genericArity: 0,
                            ResourceEffectCallingConvention.Default,
                            hasThis: false,
                            explicitThis: false,
                            [
                                new ResourceEffectParameterSelector(
                                    new ResourceTypeExpression.SzArray(
                                        byteType),
                                    ResourceEffectRefKind.Value),
                            ],
                            CoreType("Void"))),
                    new ResourceEffect.Operation(
                        ResourceOperationBoundary.Ordinary,
                        ResourceOperationThrows.Never,
                        Guard: null),
                    [
                        new ResourceDeclarationProvenance(
                            ThrowsNeverModel,
                            ResourceDeclarationAuthority.CallerSupplied,
                            new InertString(
                                TextPolicy.Field,
                                "resource-lifecycle-test:0"),
                            0),
                    ]),
            ]);
    }

    static ResourceEffectModelDefinition InterfaceLifecycleDefinition()
    {
        ResourceTypeExpression.Named byteType = CoreType("Byte");
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(byteType);
        ResourceTypeExpression.Named pool = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [
                new ResourceTypeNameSegment("Entry", 0),
                new ResourceTypeNameSegment(
                    "IOwnershipResourcePool",
                    0),
            ]);
        ResourceKindReference buffer =
            new(InterfaceBufferKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            InterfaceLifecycleModel,
            [
                new ResourceKindDefinition(
                    InterfaceBufferKind,
                    arity: 0,
                    [
                        new ResourceDeclarationProvenance(
                            InterfaceLifecycleModel,
                            ResourceDeclarationAuthority.CallerSupplied,
                            new InertString(
                                TextPolicy.Field,
                                "resource-lifecycle-interface:0"),
                            0),
                    ]),
            ],
            [],
            [
                Declaration(
                    pool,
                    "Acquire",
                    [CoreType("Int32")],
                    byteArray,
                    new ResourceEffect.Acquire(
                        buffer,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    ordinal: 1),
                Declaration(
                    pool,
                    "Release",
                    [byteArray],
                    CoreType("Void"),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        buffer,
                        Correspondence: null,
                        Observation: null),
                    ordinal: 2),
            ]);
    }

    static ResourceEffectTypedDeclaration Declaration(
        ResourceTypeExpression.Named declaringType,
        string name,
        ImmutableArray<ResourceTypeExpression> parameterTypes,
        ResourceTypeExpression returnType,
        ResourceEffect effect,
        int ordinal) =>
        new(
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    declaringType,
                    name,
                    ResourceEffectMemberKind.Method,
                    isStatic: false,
                    genericArity: 0,
                    ResourceEffectCallingConvention.Default,
                    hasThis: true,
                    explicitThis: false,
                    [
                        .. parameterTypes.Select(parameter =>
                            new ResourceEffectParameterSelector(
                                parameter,
                                ResourceEffectRefKind.Value)),
                    ],
                    returnType)),
            effect,
            [
                new ResourceDeclarationProvenance(
                    InterfaceLifecycleModel,
                    ResourceDeclarationAuthority.CallerSupplied,
                    new InertString(
                        TextPolicy.Field,
                        $"resource-lifecycle-interface:{ordinal}"),
                    ordinal),
            ]);

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
}
