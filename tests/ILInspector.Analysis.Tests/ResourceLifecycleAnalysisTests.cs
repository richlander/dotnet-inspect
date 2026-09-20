using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;
using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

public sealed class ResourceLifecycleAnalysisTests
{
    static readonly ResourceEffectModelIdentity ThrowsNeverModel =
        new("test.resource-lifecycle-throws-never");

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

        Assert.DoesNotContain(
            root.Outcomes,
            outcome =>
                outcome.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);
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
