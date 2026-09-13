using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed class ResolvedResourceEffectTests
{
    static string FixturePath =>
        FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();

    [Fact]
    public void ShippedArrayPoolModelResolvesFrameworkOperations()
    {
        ResourceEffectResolutionOutcome outcome =
            Resolve(ArrayPoolResourceEffectModel.Create());
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                outcome);

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
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.Effect is ResourceEffect.Authority
                && effect.Occurrence.Definition.Member.Name
                    == "get_Shared");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.Effect is ResourceEffect.Acquire
                && effect.Occurrence.Definition.Member.Name == "Rent");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.Effect is ResourceEffect.Release
                && effect.Occurrence.Definition.Member.Name == "Return"
                && effect.Occurrence.Definition.Member.ParameterTypes.Length
                    == 2);

        ResolvedResourceEffect rent = complete.Snapshot.Effects.First(
            effect => effect.Effect is ResourceEffect.Acquire);
        ResolvedResourceEffectGenericBinding binding =
            Assert.Single(rent.Bindings);
        Assert.Equal(
            ResourceEffectGenericVariableKind.Type,
            binding.Variable.Kind);
        Assert.Equal("Byte", binding.Value.Type.Name);
        Assert.NotNull(binding.Value.Definition);
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            Assert.Single(rent.ResourceKinds).Identity);
        Assert.Equal(
            complete.Receipt.Admission,
            rent.AdmissionReceipt);
        Assert.Equal(64, complete.Receipt.ContentHash.Length);
        Assert.Same(
            complete.Receipt.Population.Generation,
            rent.Occurrence.Generation);
    }

    [Fact]
    public void ArrayPoolModelDoesNotMatchSameNamedUserMethods()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(ArrayPoolResourceEffectModel.Create()));

        Assert.DoesNotContain(
            complete.Snapshot.Effects,
            effect =>
                effect.Occurrence.Definition.Member.DeclaringType.Name
                    .Contains("OwnershipSink", StringComparison.Ordinal));
    }

    [Fact]
    public void PhysicalInvocationIdentityRetainsDistinctCallSites()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(ArrayPoolResourceEffectModel.Create()));
        ResourceEffectInvocationOccurrence[] sharedCalls =
        [
            .. complete.Snapshot.Effects
                .Where(effect =>
                    effect.Effect is ResourceEffect.Authority)
                .Select(effect => effect.Occurrence),
        ];

        Assert.NotEmpty(sharedCalls);
        Assert.Equal(
            sharedCalls.Length,
            sharedCalls.Distinct().Count());
        Assert.All(
            sharedCalls,
            occurrence =>
            {
                Assert.NotEqual(
                    0,
                    occurrence.Call.EvidenceMethod.MetadataToken);
                Assert.NotEqual(0, occurrence.Call.OperandToken);
                Assert.Equal(
                    CallKind.Call,
                    occurrence.Call.Kind);
            });
    }

    [Fact]
    public void ResolutionWorkLimitRetainsPositiveMatchesAsIncomplete()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        int firstShared = index.DirectCalls
            .TakeWhile(call => call.Callee.Name != "get_Shared")
            .Count();
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new ResourceEffectResolutionLimits(
                        maxSelectorEvaluations: firstShared + 1),
                    index));

        Assert.NotEmpty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations,
            evaluation =>
                evaluation.Kind
                    == ResourceEffectTargetEvaluationKind.Incomplete
                && evaluation.Gaps.Any(gap =>
                    gap.Kind
                        == ResourceEffectResolutionGapKind
                            .WorkLimitExceeded
                    && gap.PhysicalInvocation is not null
                    && gap.CallKind is not null));
    }

    [Fact]
    public void PlanningWorkLimitIsVisibleBeforeResolution()
    {
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new ResourceEffectResolutionLimits(
                        maxInvocationOccurrences: 1)));

        Assert.All(
            incomplete.Evaluations,
            evaluation => Assert.Contains(
                evaluation.Gaps,
                gap =>
                    gap.WorkDimension
                        == ResourceEffectResolutionWorkDimension
                            .InvocationOccurrences
                    && gap.Limit == 1
                    && gap.RequiredWork == 2));
    }

    [Fact]
    public void InvocationBindingWorkLimitBoundsCachedCandidateComparisons()
    {
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new ResourceEffectResolutionLimits(
                        maxInvocationBindings: 1)));

        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .InvocationBindings
                && gap.Limit == 1
                && gap.RequiredWork > gap.Limit
                && gap.PhysicalInvocation is not null);
    }

    [Fact]
    public void VarargMethodDefinitionParentSearchConsumesBindingWork()
    {
        const string AssemblyName = "VarargParentBudget";
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    BuildVarargMethodDefinitionParentAssembly(
                        AssemblyName,
                        precedingMethods: 8,
                        parentName: "Target",
                        referenceName: "Target"),
                    AssemblyName,
                    SyntheticVarargMethodModel(
                        AssemblyName,
                        "Target"),
                    new ResourceEffectResolutionLimits(
                        maxInvocationBindings: 1)));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .InvocationBindings
                && gap.Limit == 1
                && gap.RequiredWork == 2);
    }

    [Fact]
    public void InvocationBindingWorkAtIntMaximumDoesNotOverflow()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new ResourceEffectResolutionLimits(
                        maxInvocationBindings: int.MaxValue)));

        Assert.NotEmpty(complete.Snapshot.Effects);
    }

    [Fact]
    public void MetadataAssociationWorkLimitIsVisible()
    {
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new ResourceEffectResolutionLimits(
                        maxMetadataAssociations: 1)));

        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .MetadataAssociations
                && gap.Limit == 1
                && gap.RequiredWork == 2);
    }

    [Fact]
    public void OtherPropertySemanticsRowsConsumeTheResolutionBudget()
    {
        const string AssemblyName = "ManyPropertySemantics";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x00, 0x00, 0x01],
            parameterAttributes: null,
            otherPropertySemanticsRows: 3);
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: []),
                    new ResourceEffectResolutionLimits(
                        maxMetadataAssociations: 2)));

        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .MetadataAssociations
                && gap.Limit == 2
                && gap.RequiredWork == 3);
    }

    [Fact]
    public void MalformedReservedConstructorIsUnsupported()
    {
        const string AssemblyName = "MalformedConstructor";
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public | MethodAttributes.Static,
                [0x00, 0x00, 0x01]),
            parameters: []);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                [0x10, 0x00, 0x00, 0x01]),
            parameters: []);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName
                    | MethodAttributes.Virtual,
                [0x00, 0x00, 0x01]),
            parameters: []);
        ResourceEffectParameterSelector objectParameter = new(
            CoreLibraryType("System", "Object"),
            ResourceEffectRefKind.Value);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                [0x00, 0x01, 0x01, 0x1C],
                ParameterAttributes.None),
            [objectParameter]);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                [0x00, 0x00, 0x08]),
            parameters: []);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".cctor",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                [0x00, 0x00, 0x01],
                methodGenericParameterRows: 1),
            parameters: []);
        AssertUnsupported(
            BuildDirectCallAssembly(
                AssemblyName,
                ".ctor",
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                [0x20, 0x01, 0x01, 0x1E, 0x00],
                ParameterAttributes.None,
                useNewObject: true),
            [
                new ResourceEffectParameterSelector(
                    CoreLibraryType("System", "Object"),
                    ResourceEffectRefKind.Value),
            ],
            isStatic: false);

        static void AssertUnsupported(
            byte[] image,
            ImmutableArray<ResourceEffectParameterSelector> parameters,
            bool isStatic = true)
        {
            ResourceEffectResolutionOutcome.Incomplete incomplete =
                Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                    ResolveSynthetic(
                        image,
                        AssemblyName,
                        SyntheticMethodModel(
                            AssemblyName,
                            isStatic ? ".cctor" : ".ctor",
                            ResourceEffectMemberKind.Constructor,
                            parameters,
                            isStatic)));

            Assert.Empty(incomplete.Effects);
            Assert.Contains(
                incomplete.Evaluations.SelectMany(
                    evaluation => evaluation.Gaps),
                gap =>
                    gap.Kind
                        == ResourceEffectResolutionGapKind
                            .UnsupportedSignature);
        }
    }

    [Fact]
    public void ValidInstanceConstructorResolves()
    {
        const string AssemblyName = "ValidConstructor";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            ".ctor",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            [0x20, 0x00, 0x01],
            useNewObject: true);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        ".ctor",
                        ResourceEffectMemberKind.Constructor,
                        parameters: [],
                        isStatic: false)));

        Assert.NotEmpty(complete.Snapshot.Effects);
    }

    [Fact]
    public void UnknownByRefDirectionIsUnsupported()
    {
        const string AssemblyName = "UnknownByRefDirection";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x00, 0x01, 0x01, 0x10, 0x08],
            ParameterAttributes.In | ParameterAttributes.Out);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        [
                            new ResourceEffectParameterSelector(
                                CoreLibraryType("System", "Int32"),
                                ResourceEffectRefKind.Ref),
                        ])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);

        ResourceEffectResolutionOutcome.Complete unmatched =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        [
                            new ResourceEffectParameterSelector(
                                CoreLibraryType("System", "Int32"),
                                ResourceEffectRefKind.Value),
                        ])));
        Assert.Empty(unmatched.Snapshot.Effects);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Unmatched,
            Assert.Single(unmatched.Evaluations).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousPropertySemanticsAreUnsupported(
        bool crossTypeAssociation)
    {
        const string AssemblyName = "AmbiguousPropertySemantics";
        MethodSemanticsAttributes[] semantics =
            crossTypeAssociation
                ? [MethodSemanticsAttributes.Getter]
                : [
                    MethodSemanticsAttributes.Getter,
                    MethodSemanticsAttributes.Setter,
                ];
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x00, 0x00, 0x01],
            parameterAttributes: null,
            propertySemantics: semantics,
            propertyOnDifferentType: crossTypeAssociation);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void EventSemanticsAreUnsupportedForMethodSelection()
    {
        const string AssemblyName = "EventSemantics";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x00, 0x00, 0x01],
            eventSemantics: [MethodSemanticsAttributes.Adder]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void UnrelatedEventSemanticsDoNotBlockOrdinaryMethod()
    {
        const string AssemblyName = "UnrelatedEventSemantics";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x00, 0x00, 0x01],
            eventSemantics: [MethodSemanticsAttributes.Adder],
            eventTargetsCaller: true);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [])));

        Assert.NotEmpty(complete.Snapshot.Effects);
    }

    [Fact]
    public void DuplicatePropertySemanticsAreUnsupported()
    {
        const string AssemblyName = "DuplicatePropertySemantics";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x00, 0x00, 0x08],
            propertySemantics:
            [
                MethodSemanticsAttributes.Getter,
                MethodSemanticsAttributes.Getter,
            ]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.PropertyGetter,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void InvalidPropertyAccessorSignatureIsUnsupported()
    {
        const string AssemblyName = "InvalidPropertyAccessor";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x00, 0x00, 0x01],
            propertySemantics: [MethodSemanticsAttributes.Getter]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.PropertyGetter,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void PropertyAccessorGenericMetadataIsUnsupported()
    {
        const string AssemblyName = "GenericPropertyAccessor";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x10, 0x00, 0x00, 0x08],
            propertySemantics: [MethodSemanticsAttributes.Getter]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.PropertyGetter,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void PropertyAccessorOutOfRangeGenericReferenceIsUnsupported()
    {
        const string AssemblyName = "InvalidPropertyGenericReference";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            [0x00, 0x00, 0x1E, 0x00],
            propertySemantics: [MethodSemanticsAttributes.Getter]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.PropertyGetter,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void OrdinaryMethodOutOfRangeGenericReferenceIsUnsupported()
    {
        const string AssemblyName = "InvalidMethodGenericReference";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x00, 0x01, 0x01, 0x13, 0x00],
            ParameterAttributes.None);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        [
                            new ResourceEffectParameterSelector(
                                CoreLibraryType(
                                    "System",
                                    "Object"),
                                ResourceEffectRefKind.Value),
                        ])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OrdinaryMethodStaticnessMismatchIsUnsupported(
        bool metadataStatic,
        bool signatureHasThis)
    {
        const string AssemblyName = "InvalidMethodStaticness";
        byte signatureHeader =
            signatureHasThis ? (byte)0x20 : (byte)0x00;
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public
                | (metadataStatic
                    ? MethodAttributes.Static
                    : 0),
            [signatureHeader, 0x00, 0x01]);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [],
                        isStatic: !signatureHasThis)));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void UnreadableDirectCallSignatureIsUnsupported()
    {
        const string AssemblyName = "UnreadableDirectCallSignature";
        byte[] signature = new byte[
            SignatureBlobGuard.DefaultMaxDepth + 4];
        signature[0] = 0x00;
        signature[1] = 0x00;
        signature.AsSpan(
                2,
                SignatureBlobGuard.DefaultMaxDepth + 1)
            .Fill(0x1D);
        signature[^1] = 0x1C;
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            signature);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void UnsupportedDeclaringTypeIsNotUnmatched()
    {
        const string AssemblyName = "UnsupportedDeclaringType";
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    BuildUnsupportedDeclaringTypeCallAssembly(
                        AssemblyName),
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [])));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void GenericMethodDefinitionCallWithoutInstantiationIsUnsupported()
    {
        const string AssemblyName = "UninstantiatedGenericMethod";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x10, 0x01, 0x00, 0x01],
            methodGenericParameterRows: 1);

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [],
                        genericArity: 1)));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void MalformedMethodGenericParameterOrdinalIsUnsupported()
    {
        const string AssemblyName = "InvalidMethodGenericOrdinal";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x10, 0x01, 0x00, 0x01],
            methodGenericParameterRows: 1,
            methodGenericParameterStartIndex: 1,
            instantiateGenericMethod: true);

        AssertUnsupportedGenericOrdinal(
            image,
            AssemblyName,
            methodGenericArity: 1,
            declaringTypeGenericArity: 0);
    }

    [Fact]
    public void MalformedTypeGenericParameterOrdinalIsUnsupported()
    {
        const string AssemblyName = "InvalidTypeGenericOrdinal";
        byte[] image = BuildDirectCallAssembly(
            AssemblyName,
            "Target",
            MethodAttributes.Public | MethodAttributes.Static,
            [0x00, 0x00, 0x01],
            typeGenericParameterRows: 1,
            typeGenericParameterStartIndex: 1);

        AssertUnsupportedGenericOrdinal(
            image,
            AssemblyName,
            methodGenericArity: 0,
            declaringTypeGenericArity: 1);
    }

    static void AssertUnsupportedGenericOrdinal(
        byte[] image,
        string assemblyName,
        int methodGenericArity,
        int declaringTypeGenericArity)
    {
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    image,
                    assemblyName,
                    SyntheticMethodModel(
                        assemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [],
                        genericArity: methodGenericArity,
                        declaringTypeGenericArity:
                            declaringTypeGenericArity)));

        Assert.Empty(incomplete.Effects);
        Assert.Contains(
            incomplete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void InterfaceMethodRemainsIncompleteUntilApplicationIsResolved()
    {
        const string AssemblyName = "DeferredInterfaceApplication";
        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    BuildInterfaceAndConcreteCallAssembly(AssemblyName),
                    AssemblyName,
                    SyntheticMethodModel(
                        AssemblyName,
                        "Target",
                        ResourceEffectMemberKind.Method,
                        parameters: [],
                        isStatic: false,
                        declaringTypeName: "IResource")));

        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);
        ResolvedResourceEffect effect =
            Assert.Single(evaluation.Effects);
        Assert.Equal(
            "ThroughInterface",
            effect.Occurrence.Call.Caller.Name);
        Assert.Contains(
            evaluation.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .CorrespondenceIncomplete);
    }

    [Fact]
    public void ProvenanceAssociationWorkLimitIsVisible()
    {
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    FixtureType("Entry"),
                    "VarargMarker",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 0,
                    ResourceEffectCallingConvention.VarArgs,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            CoreLibraryType("System", "Int32"),
                            ResourceEffectRefKind.Value),
                    ],
                    CoreLibraryType("System", "Void")));
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.provenance-limit.first",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.provenance-limit.second",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    admission,
                    new ResourceEffectResolutionLimits(
                        maxProvenanceAssociations: 1)));

        Assert.Single(incomplete.Effects);
        Assert.Single(incomplete.Effects[0].Provenances);
        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .ProvenanceAssociations
                && gap.Limit == 1
                && gap.RequiredWork == 2);
        Assert.Single(incomplete.Gaps);
    }

    [Fact]
    public void RetainedDiagnosticWorkLimitIsVisible()
    {
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    FixtureType("Entry"),
                    "VarargMarker",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 0,
                    ResourceEffectCallingConvention.VarArgs,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            CoreLibraryType("System", "Int32"),
                            ResourceEffectRefKind.Value),
                    ],
                    CoreLibraryType("System", "Void")));
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.diagnostic-limit.first",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.diagnostic-limit.second",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(
                    admission,
                    new ResourceEffectResolutionLimits(
                        maxSelectorEvaluations: 1,
                        maxRetainedDiagnostics: 1)));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.WorkLimitExceeded
                && gap.WorkDimension
                    == ResourceEffectResolutionWorkDimension
                        .RetainedDiagnostics
                && gap.Limit == 1
                && gap.RequiredWork == 2);
        Assert.Single(incomplete.Gaps);
    }

    [Fact]
    public void MethodGenericArgumentsBindWithoutSignatureUse()
    {
        ResourceEffectAdmission admission = MethodModel(
            "GenericMarker",
            genericArity: 1,
            parameters: [],
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                Guard: null));
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));

        ResolvedResourceEffect effect =
            Assert.Single(
                Assert.Single(complete.Evaluations).Effects.Where(
                    candidate =>
                        Assert.Single(candidate.Bindings)
                            .Value.Type.Name == "Byte"));
        ResolvedResourceEffectGenericBinding binding =
            Assert.Single(effect.Bindings);
        Assert.Equal(
            ResourceEffectGenericVariableKind.Method,
            binding.Variable.Kind);
        Assert.Equal("Byte", binding.Value.Type.Name);
        Assert.NotNull(binding.Value.Definition);
    }

    [Fact]
    public void IndeterminateDuplicateArtifactBindingIsIncomplete()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference first =
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect duplicate binding first"));
        ResolvedAssemblyReference second =
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect duplicate binding second"));
        var variable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Method,
            0);
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.array-empty",
                new ResourceEffectTargetSelector.Member(
                    new ResourceEffectMemberSelector(
                        CoreLibraryType("System", "Array"),
                        "Empty",
                        ResourceEffectMemberKind.Method,
                        isStatic: true,
                        genericArity: 1,
                        ResourceEffectCallingConvention.Default,
                        hasThis: false,
                        explicitThis: false,
                        parameters: [],
                        new ResourceTypeExpression.SzArray(
                            new ResourceTypeExpression.Variable(
                                variable)))),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResourceEffectResolver.Resolve(
                    admission,
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            FixturePath)),
                    [
                        new CatalogCallGraphParticipant(index, first),
                        new CatalogCallGraphParticipant(index, second),
                    ],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Incomplete,
            evaluation.Kind);
        Assert.NotEmpty(evaluation.Effects);
        Assert.All(
            evaluation.Effects.SelectMany(
                effect => effect.Bindings),
            binding => Assert.Equal(
                DefinitionJoinKind.Exact,
                binding.Value.Definition?.Kind));
        Assert.Contains(
            evaluation.Gaps,
            gap => gap.Kind
                == ResourceEffectResolutionGapKind
                    .CorrespondenceIncomplete);
    }

    [Fact]
    public void OpenGenericBindingsRetainTheirDeclaringScope()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(MethodModel(
                    "GenericMarker",
                    genericArity: 1,
                    parameters: [],
                    new ResourceEffect.Operation(
                        ResourceOperationBoundary.Ordinary,
                        ResourceOperationThrows.Possible,
                        Guard: null))));
        ResolvedResourceEffectType[] openBindings =
        [
            .. complete.Snapshot.Effects
                .SelectMany(effect => effect.Bindings)
                .Select(binding => binding.Value)
                .Where(value =>
                    value.Type.Kind
                        == TypeRefKind.MethodGenericParameter),
        ];

        Assert.Equal(2, openBindings.Length);
        Assert.All(
            openBindings,
            binding => Assert.NotNull(binding.GenericScope));
        Assert.NotEqual(
            openBindings[0].GenericScope,
            openBindings[1].GenericScope);
    }

    [Fact]
    public void EquivalentBoundGenericLocationsCoalesce()
    {
        ResourceEffectGenericVariable typeVariable = new(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceEffectGenericVariable methodVariable = new(
            ResourceEffectGenericVariableKind.Method,
            0);
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("GenericHost", 1)],
            [new ResourceTypeExpression.Variable(typeVariable)]);
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    declaringType,
                    "Target",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 1,
                    ResourceEffectCallingConvention.Default,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            new ResourceTypeExpression.Variable(typeVariable),
                            ResourceEffectRefKind.Value),
                    ],
                    CoreLibraryType("System", "Void")));
        ResourceKindIdentity kindIdentity = new(
            "example.generic-equivalence.resource");

        ResourceEffectModelDefinition Model(
            string name,
            ResourceEffectGenericVariable variable)
        {
            ResourceKindReference kind = new(kindIdentity, [variable]);
            return new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity(name),
                [
                    new ResourceKindDefinition(
                        kindIdentity,
                        1,
                        [Provenance(name, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectLocation.OperationSlot(
                                new ResourceEffectLocation.Parameter(0),
                                kind),
                            kind),
                        [Provenance(name, 1)]),
                ]);
        }

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.generic-equivalence.type",
                            typeVariable),
                        Model(
                            "example.generic-equivalence.method",
                            methodVariable))));

        ResolvedResourceEffect effect =
            Assert.Single(complete.Snapshot.Effects);
        Assert.Equal(2, effect.Sources.Length);
        Assert.All(
            effect.Bindings,
            binding => Assert.Equal(
                "Byte",
                binding.Value.Type.Name));
    }

    [Fact]
    public void VarargSelectorMatchesFixedParameterPrefix()
    {
        var identity = new ResourceEffectModelIdentity(
            "example.vararg-marker");
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);
        ResourceEffectAdmission admission = Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                identity,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                declaringType,
                                "VarargMarker",
                                ResourceEffectMemberKind.Method,
                                isStatic: true,
                                genericArity: 0,
                                ResourceEffectCallingConvention.VarArgs,
                                hasThis: false,
                                explicitThis: false,
                                [
                                    new ResourceEffectParameterSelector(
                                        CoreLibraryType("System", "Int32"),
                                        ResourceEffectRefKind.Value),
                                ],
                                CoreLibraryType("System", "Void"))),
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            Guard: null),
                        [Provenance(identity.Value, 0)]),
                ]));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(complete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Resolved,
            evaluation.Kind);
        Assert.Single(evaluation.Effects);
    }

    [Fact]
    public void VarargMemberReferenceRetainsMethodDefinitionParent()
    {
        ResourceEffectTargetSelector Target(string typeName) =>
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    FixtureType(typeName),
                    "Marker",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 0,
                    ResourceEffectCallingConvention.VarArgs,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            CoreLibraryType("System", "Int32"),
                            ResourceEffectRefKind.Value),
                    ],
                    CoreLibraryType("System", "Void")));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.first-vararg-host",
                            Target("FirstVarargHost"),
                            new ResourceEffect.Operation(
                                ResourceOperationBoundary.Ordinary,
                                ResourceOperationThrows.Possible,
                                Guard: null)),
                        Model(
                            "example.second-vararg-host",
                            Target("SecondVarargHost"),
                            new ResourceEffect.Operation(
                                ResourceOperationBoundary.Ordinary,
                                ResourceOperationThrows.Possible,
                                Guard: null)))));

        Assert.Collection(
            complete.Evaluations,
            evaluation =>
            {
                Assert.Equal(
                    ResourceEffectTargetEvaluationKind.Resolved,
                    evaluation.Kind);
                Assert.Equal(
                    "FirstVarargHost",
                    Assert.Single(evaluation.Effects)
                        .Occurrence.Definition.Member.DeclaringType.Name);
            },
            evaluation =>
            {
                Assert.Equal(
                    ResourceEffectTargetEvaluationKind.Resolved,
                    evaluation.Kind);
                Assert.Equal(
                    "SecondVarargHost",
                    Assert.Single(evaluation.Effects)
                        .Occurrence.Definition.Member.DeclaringType.Name);
            });
    }

    [Fact]
    public void VarargMemberReferenceCannotSelectDifferentMethodThanParent()
    {
        const string AssemblyName = "MismatchedVarargParent";
        var identity = new ResourceEffectModelIdentity(
            "example.mismatched-vararg-parent");
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                AssemblyName,
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "N",
            [new ResourceTypeNameSegment("Owner", 0)]);
        ResourceEffectAdmission admission = Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                identity,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                declaringType,
                                "B",
                                ResourceEffectMemberKind.Method,
                                isStatic: true,
                                genericArity: 0,
                                ResourceEffectCallingConvention.VarArgs,
                                hasThis: false,
                                explicitThis: false,
                                [
                                    new ResourceEffectParameterSelector(
                                        CoreLibraryType(
                                            "System",
                                            "Int32"),
                                        ResourceEffectRefKind.Value),
                                ],
                                CoreLibraryType(
                                    "System",
                                    "Void"))),
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            Guard: null),
                        [Provenance(identity.Value, 0)]),
                ]));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveSynthetic(
                    BuildVarargMethodDefinitionParentAssembly(
                        AssemblyName,
                        precedingMethods: 0,
                        parentName: "A",
                        referenceName: "B"),
                    AssemblyName,
                    admission));

        Assert.Empty(incomplete.Effects);
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Unsupported,
            evaluation.Kind);
        Assert.Contains(
            evaluation.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind.UnsupportedSignature);
    }

    [Fact]
    public void EquivalentResolvedGuardTypesCoalesce()
    {
        ResourceTypeExpression byteArray =
            new ResourceTypeExpression.SzArray(
                CoreLibraryType("System", "Byte"));
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    declaringType,
                    "ReturnRentedArrayToCaller",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 0,
                    ResourceEffectCallingConvention.Default,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            byteArray,
                            ResourceEffectRefKind.Value),
                    ],
                    byteArray));

        ResourceEffectModelDefinition Model(
            string name,
            ResourceEffectSignatureLocation expected) =>
            new(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity(name),
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            new ResourceEffectGuard.ExactRuntimeType(
                                new ResourceEffectLocation.Parameter(0),
                                expected)),
                        [Provenance(name, 0)]),
                ]);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.guard-parameter",
                            new ResourceEffectSignatureLocation.Parameter(0)),
                        Model(
                            "example.guard-return",
                            new ResourceEffectSignatureLocation.Return()))));

        ResolvedResourceEffect effect =
            Assert.Single(complete.Snapshot.Effects);
        Assert.Equal(2, effect.Sources.Length);
        Assert.NotNull(effect.GuardExpectedType);
        Assert.Equal(
            DefinitionJoinKind.Exact,
            effect.GuardExpectedType.Definition?.Kind);
    }

    [Fact]
    public void GenericMethodGuardUsesInstantiatedOccurrenceSignature()
    {
        ResourceEffectTargetSelector target = GenericEchoTarget();
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.generic-method-guard-parameter",
                            target,
                            GuardedOperation(
                                new ResourceEffectSignatureLocation
                                    .Parameter(0))),
                        Model(
                            "example.generic-method-guard-return",
                            target,
                            GuardedOperation(
                                new ResourceEffectSignatureLocation
                                    .Return())))));

        ResolvedResourceEffect effect =
            Assert.Single(complete.Snapshot.Effects);
        Assert.Equal(2, effect.Sources.Length);
        Assert.Equal("Byte", effect.GuardExpectedType?.Type.Name);
        Assert.Equal(
            DefinitionJoinKind.Exact,
            effect.GuardExpectedType?.Definition?.Kind);
    }

    [Fact]
    public void GenericDeclaringTypeGuardUsesInstantiatedOccurrenceSignature()
    {
        ResourceEffectGenericVariable typeVariable = new(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceEffectGenericVariable methodVariable = new(
            ResourceEffectGenericVariableKind.Method,
            0);
        ResourceTypeExpression.Variable typeArgument = new(typeVariable);
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    new ResourceTypeExpression.Named(
                        FixtureAssembly(),
                        "Ownership",
                        [new ResourceTypeNameSegment("GenericHost", 1)],
                        [typeArgument]),
                    "Target",
                    ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity: 1,
                    ResourceEffectCallingConvention.Default,
                    hasThis: false,
                    explicitThis: false,
                    [
                        new ResourceEffectParameterSelector(
                            typeArgument,
                            ResourceEffectRefKind.Value),
                    ],
                    CoreLibraryType("System", "Void")));
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.generic-type-guard",
                            target,
                            GuardedOperation(
                                new ResourceEffectSignatureLocation
                                    .Parameter(0))))));

        ResolvedResourceEffect effect =
            Assert.Single(complete.Snapshot.Effects);
        Assert.Equal("Byte", effect.GuardExpectedType?.Type.Name);
        Assert.Equal(
            DefinitionJoinKind.Exact,
            effect.GuardExpectedType?.Definition?.Kind);
        Assert.Contains(
            effect.Bindings,
            binding =>
                binding.Variable == typeVariable
                && binding.Value.Type.Name == "Byte");
        Assert.Contains(
            effect.Bindings,
            binding =>
                binding.Variable == methodVariable
                && binding.Value.Type.Name == "Byte");
    }

    [Fact]
    public void FacadeGuardResolutionCoalescesWithExactReceiptEvidence()
    {
        ResourceEffectTargetSelector target = ArrayEmptyTarget();
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.facade-guard-first",
                            target,
                            GuardedOperation(
                                new ResourceEffectSignatureLocation
                                    .Return())),
                        Model(
                            "example.facade-pass",
                            target,
                            new ResourceEffect.Pass(
                                new ResourceEffectLocation.Return(),
                                new ResourceEffectLocation.Return(),
                                Identity: null)),
                        Model(
                            "example.facade-guard-second",
                            target,
                            GuardedOperation(
                                new ResourceEffectSignatureLocation
                                    .Return())))));

        ImmutableArray<ResolvedResourceEffect> effects =
        [
            .. complete.Snapshot.Effects.Where(effect =>
                effect.Occurrence.Call.Caller.Name
                    == "CallExternalGenericMarker"),
        ];
        Assert.Equal(2, effects.Length);
        AssertCanonicalBoundOrder(effects);
        ResolvedResourceEffect guarded =
            Assert.Single(
                effects,
                effect => effect.Effect is ResourceEffect.Operation);
        Assert.Equal(2, guarded.Sources.Length);
        Assert.Equal(
            DefinitionJoinKind.Exact,
            guarded.GuardExpectedType?.Element?.Definition?.Kind);
        Assert.NotEqual(
            guarded.Occurrence.Call.Callee.DeclaringType.Assembly,
            guarded.Occurrence.Definition.Assembly.Name);
        Assert.Equal(64, complete.Receipt.ContentHash.Length);
    }

    [Fact]
    public void EquivalentResolvedGuardTypesCoalesceAcrossFacadeSpellings()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(MethodModel(
                    "GenericMarker",
                    genericArity: 1,
                    parameters: [],
                    new ResourceEffect.Operation(
                        ResourceOperationBoundary.Ordinary,
                        ResourceOperationThrows.Possible,
                        Guard: null))));
        ResolvedResourceEffectType resolved =
            complete.Snapshot.Effects
                .SelectMany(effect => effect.Bindings)
                .First(binding =>
                    binding.Value.Definition is not null)
                .Value;
        TypeRef firstRaw = TypeRef.Definition(
            "Facade.One",
            resolved.Type.Namespace,
            resolved.Type.Name);
        TypeRef secondRaw = TypeRef.Definition(
            "Facade.Two",
            resolved.Type.Namespace,
            resolved.Type.Name);
        var firstExpected = new ResolvedResourceEffectType(
            firstRaw,
            resolved.DefiningAssembly,
            resolved.Definition,
            resolved.GenericScope,
            resolved.Element,
            resolved.Arguments);
        var secondExpected = new ResolvedResourceEffectType(
            secondRaw,
            resolved.DefiningAssembly,
            resolved.Definition,
            resolved.GenericScope,
            resolved.Element,
            resolved.Arguments);
        ResolvedResourceEffect template =
            complete.Snapshot.Effects[0];
        MemberRef member = template.Occurrence.Call.Callee;
        var firstOccurrence = new ResourceEffectInvocationOccurrence(
            template.Occurrence.Catalog,
            template.Occurrence.Generation,
            template.Occurrence.Participant,
            template.Occurrence.Call with
            {
                Callee = member with
                {
                    ParameterTypes = [firstRaw],
                },
            },
            template.Occurrence.Definition);
        var secondOccurrence = new ResourceEffectInvocationOccurrence(
            template.Occurrence.Catalog,
            template.Occurrence.Generation,
            template.Occurrence.Participant,
            template.Occurrence.Call with
            {
                Callee = member with
                {
                    ReturnType = secondRaw,
                },
            },
            template.Occurrence.Definition);
        var first = new ResolvedResourceEffect(
            template.AdmissionReceipt,
            firstOccurrence,
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                new ResourceEffectGuard.ExactRuntimeType(
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectSignatureLocation.Parameter(0))),
            template.Bindings,
            template.ResourceKinds,
            firstExpected,
            template.Sources);
        var second = new ResolvedResourceEffect(
            template.AdmissionReceipt,
            secondOccurrence,
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                new ResourceEffectGuard.ExactRuntimeType(
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectSignatureLocation.Return())),
            template.Bindings,
            template.ResourceKinds,
            secondExpected,
            template.Sources);

        Assert.False(
            TypeRef.ExactSignatureEquals(
                firstRaw,
                secondRaw));
        Assert.Single(
            ResourceEffectResolver.Coalesce(
                [first, second]));
    }

    [Fact]
    public void CanonicalBoundEffectIncludesExactDefinitionIdentity()
    {
        ResolvedResourceEffect byteEffect =
            Assert.Single(
                Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                        Resolve(
                            Admit(
                                Model(
                                    "example.byte-definition",
                                    ArrayEmptyTarget(),
                                    GuardedOperation(
                                        new ResourceEffectSignatureLocation
                                            .Return())))))
                    .Snapshot.Effects.Where(effect =>
                        effect.Occurrence.Call.Caller.Name
                            == "CallExternalGenericMarker"));
        ResolvedResourceEffect markerEffect =
            Assert.Single(
                Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                        Resolve(
                            Admit(
                                Model(
                                    "example.marker-definition",
                                    GenericEchoTarget(),
                                    GuardedOperation(
                                        new ResourceEffectSignatureLocation
                                            .Parameter(0))))))
                    .Snapshot.Effects.Where(effect =>
                        effect.GuardExpectedType?.Definition is not null));
        ResolvedResourceEffectType firstResolved =
            byteEffect.GuardExpectedType!.Element!;
        ResolvedResourceEffectType secondResolved =
            markerEffect.GuardExpectedType!;
        TypeRef sameSpelling = TypeRef.Definition(
            "Same.Display.Assembly",
            "Same.Display",
            "Type");
        var firstType = new ResolvedResourceEffectType(
            sameSpelling,
            firstResolved.DefiningAssembly,
            firstResolved.Definition,
            firstResolved.GenericScope,
            firstResolved.Element,
            firstResolved.Arguments);
        var secondType = new ResolvedResourceEffectType(
            sameSpelling,
            firstResolved.DefiningAssembly,
            secondResolved.Definition,
            secondResolved.GenericScope,
            secondResolved.Element,
            secondResolved.Arguments);
        ResourceEffect operation = GuardedOperation(
            new ResourceEffectSignatureLocation.Return());
        var first = new ResolvedResourceEffect(
            byteEffect.AdmissionReceipt,
            byteEffect.Occurrence,
            operation,
            [],
            [],
            firstType,
            byteEffect.Sources);
        var second = new ResolvedResourceEffect(
            byteEffect.AdmissionReceipt,
            byteEffect.Occurrence,
            operation,
            [],
            [],
            secondType,
            byteEffect.Sources);

        Assert.True(
            TypeRef.ExactSignatureEquals(
                first.GuardExpectedType!.Type,
                second.GuardExpectedType!.Type));
        Assert.NotEqual(
            first.GuardExpectedType.Definition,
            second.GuardExpectedType.Definition);
        Assert.NotEqual(
            0,
            ResourceEffectResolver.CompareCanonicalBoundEffects(
                first,
                second));
    }

    [Fact]
    public void RefSelectorDoesNotMatchOutParameter()
    {
        ResourceEffectAdmission admission = MethodModel(
            "RefMarker",
            genericArity: 0,
            parameters:
            [
                new ResourceEffectParameterSelector(
                    CoreLibraryType("System", "Int32"),
                    ResourceEffectRefKind.Out),
            ],
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                Guard: null));
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));

        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Unmatched,
            Assert.Single(complete.Evaluations).Kind);
        Assert.Empty(complete.Snapshot.Effects);
    }

    [Fact]
    public void DuplicateParticipantsAreRejected()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect duplicate test"));
        var participant = new CatalogCallGraphParticipant(index, assembly);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResourceEffectResolver.Resolve(
                    ArrayPoolResourceEffectModel.Create(),
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            FixturePath)),
                    [participant, participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            ResourceEffectResolutionRejectionKind.DuplicateParticipant,
            rejected.Kind);
    }

    [Fact]
    public void CoreLibraryFacadeDoesNotIgnoreUntrustedAssemblySelector()
    {
        ResourceEffectTargetSelector.Member rent =
            Assert.IsType<ResourceEffectTargetSelector.Member>(
                ArrayPoolResourceEffectModel.Definition()
                    .TypedDeclarations[1]
                    .Target);
        ResourceTypeExpression.Named declaringType =
            Assert.IsType<ResourceTypeExpression.Named>(
                rent.Selector.DeclaringType);
        var unrelated = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                "Example.Unrelated",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            declaringType.Namespace,
            declaringType.Segments,
            declaringType.Arguments);
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.invalid-facade",
                new ResourceEffectTargetSelector.Member(
                    new ResourceEffectMemberSelector(
                        unrelated,
                        rent.Selector.MetadataName,
                        rent.Selector.Kind,
                        rent.Selector.IsStatic,
                        rent.Selector.GenericArity,
                        rent.Selector.CallingConvention,
                        rent.Selector.HasThis,
                        rent.Selector.ExplicitThis,
                        rent.Selector.Parameters,
                        rent.Selector.ReturnType)),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Unmatched,
            Assert.Single(complete.Evaluations).Kind);
    }

    [Fact]
    public void ExactTypeOutcomeRemainsIncompleteUntilTypeBindingExists()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[1]
                .Target;
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.exact-type-outcome",
                target,
                new ResourceEffect.Outcome(
                    new ResourceEffectLocalIdentity("result"),
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectOutcomeTest.ExactType(
                        "Example.MissingType"))));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(admission));
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Incomplete,
            evaluation.Kind);
        Assert.Empty(evaluation.Effects);
    }

    [Fact]
    public void StructuralFieldRemainsIncompleteUntilFieldBindingExists()
    {
        ResourceEffectTargetSelector.Member rent =
            Assert.IsType<ResourceEffectTargetSelector.Member>(
                ArrayPoolResourceEffectModel.Definition()
                    .TypedDeclarations[1]
                    .Target);
        var field = new ResourceEffectMemberSelector(
            rent.Selector.DeclaringType,
            "_missing",
            ResourceEffectMemberKind.Field,
            isStatic: false,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            parameters: [],
            CoreLibraryType("System", "Int32"));
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.structural-field",
                rent,
                new ResourceEffect.Derive(
                    new ResourceEffectLocation.StructuralField(
                        new ResourceEffectLocation.Receiver(),
                        field),
                    new ResourceEffectLocation.Return(),
                    ResourceDerivationRelation.Alias,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(admission));
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(incomplete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Incomplete,
            evaluation.Kind);
        Assert.Empty(evaluation.Effects);
    }

    [Fact]
    public void ConsumeIntoOperationThenReleaseIsCompatible()
    {
        ResourceEffectModelDefinition shipped =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectTargetSelector target =
            shipped.TypedDeclarations[3].Target;
        var operation = new ResourceEffectLocation.Operation(0);
        ResourceKindReference kind = new(
            ArrayPoolResourceEffectModel.BufferKind,
            [
                new ResourceEffectGenericVariable(
                    ResourceEffectGenericVariableKind.Type,
                    0),
            ]);
        var identity =
            new ResourceEffectModelIdentity("example.operation-release");
        var model = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            identity,
            [
                new ResourceKindDefinition(
                    ArrayPoolResourceEffectModel.BufferKind,
                    1,
                    [Provenance(identity.Value, 0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    target,
                    new ResourceEffect.Consume(
                        new ResourceEffectLocation.Parameter(0),
                        operation,
                        kind),
                    [Provenance(identity.Value, 1)]),
                new ResourceEffectTypedDeclaration(
                    target,
                    new ResourceEffect.Release(
                        operation,
                        new ResourceEffectCompletion.NormalReturn(),
                        kind,
                        new ResourceEffectLocation.Receiver(),
                        Observation: null),
                    [Provenance(identity.Value, 2)]),
            ]);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(Admit(model)));
        Assert.Contains(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Consume);
        Assert.Contains(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Release);
    }

    [Fact]
    public void OperationSlotKindNarrowsDomainWithoutChangingTransition()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[3]
                .Target;
        ResourceEffectGenericVariable variable = new(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceKindReference kind = new(
            ArrayPoolResourceEffectModel.BufferKind,
            [variable]);

        ResourceEffectModelDefinition Model(
            string name,
            ResourceEffect effect) =>
            new(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity(name),
                [
                    new ResourceKindDefinition(
                        ArrayPoolResourceEffectModel.BufferKind,
                        1,
                        [Provenance(name, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        effect,
                        [Provenance(name, 1)]),
                ]);

        ResourceEffectLocation.Parameter parameter =
            new(0);
        ResourceEffectLocation.Parameter operationSource =
            new(1);
        ResourceEffectModelDefinition OutcomeModel(
            string name,
            ResourceKindReference? effectKind)
        {
            ResourceEffectLocation.OperationSlot slot =
                new(operationSource, effectKind);
            return new(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity(name),
                [
                    new ResourceKindDefinition(
                        ArrayPoolResourceEffectModel.BufferKind,
                        1,
                        [Provenance(name, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            operationSource,
                            slot,
                            effectKind),
                        [Provenance(name, 1)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Release(
                            parameter,
                            new ResourceEffectCompletion.OutcomeCase(
                                slot,
                                new ResourceEffectOutcomeTest.Boolean(
                                    true)),
                            effectKind,
                            Correspondence: null,
                            Observation: null),
                        [Provenance(name, 2)]),
                ]);
        }
        ResourceEffectModelDefinition IndependenceModel(
            string name,
            ResourceKindReference? effectKind,
            bool independent)
        {
            ResourceEffectLocation.OperationSlot slot =
                new(operationSource, effectKind);
            return new(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity(name),
                [
                    new ResourceKindDefinition(
                        ArrayPoolResourceEffectModel.BufferKind,
                        1,
                        [Provenance(name, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            operationSource,
                            slot,
                            effectKind),
                        [Provenance(name, 1)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        independent
                            ? new ResourceEffect.Independent(
                                slot,
                                new ResourceEffectLocation.Receiver())
                            : new ResourceEffect.Pass(
                                slot,
                                new ResourceEffectLocation.Receiver(),
                                Identity: null),
                        [Provenance(name, 2)]),
                ]);
        }
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(
                    Admit(
                        Model(
                            "example.operation-slot-all-kinds",
                            new ResourceEffect.Consume(
                                parameter,
                                new ResourceEffectLocation.OperationSlot(
                                    parameter,
                                    kind: null),
                                Kind: null)),
                        Model(
                            "example.operation-slot-buffer-kind",
                            new ResourceEffect.Consume(
                                parameter,
                                new ResourceEffectLocation.OperationSlot(
                                    parameter,
                                    kind),
                                kind)))));

        Assert.NotEmpty(complete.Snapshot.Effects);
        Assert.All(
            complete.Snapshot.Effects.GroupBy(effect => effect.Occurrence),
            effects =>
            {
                Assert.Equal(2, effects.Count());
                Assert.Contains(
                    effects,
                    effect =>
                        Assert.IsType<ResourceEffect.Consume>(
                            effect.Effect).Kind is null);
                Assert.Contains(
                    effects,
                    effect =>
                        Assert.IsType<ResourceEffect.Consume>(
                            effect.Effect).Kind is not null);
            });

        ResourceEffectResolutionOutcome outcomeWithCompletion =
            Resolve(
                Admit(
                    OutcomeModel(
                        "example.outcome-slot-all-kinds",
                        effectKind: null),
                    OutcomeModel(
                        "example.outcome-slot-buffer-kind",
                        kind)));
        Assert.True(
            outcomeWithCompletion
                is ResourceEffectResolutionOutcome.Complete,
            outcomeWithCompletion
                is ResourceEffectResolutionOutcome.Conflict conflict
                    ? string.Join(
                        ", ",
                        conflict.Conflicts.SelectMany(
                            value => value.Effects).Select(
                                effect =>
                                    effect.Effect.GetType().Name))
                    : outcomeWithCompletion.GetType().Name);

        Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
            Resolve(
                Admit(
                    IndependenceModel(
                        "example.independent-slot-all-kinds",
                        effectKind: null,
                        independent: true),
                    IndependenceModel(
                        "example.pass-slot-buffer-kind",
                        kind,
                        independent: false))));

        Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
            Resolve(
                Admit(
                    Model(
                        "example.operation-slot-move",
                        new ResourceEffect.Move(
                            parameter,
                            parameter,
                            new ResourceEffectCompletion.Entry(),
                            Kind: null)),
                    Model(
                        "example.operation-slot-release",
                        new ResourceEffect.Release(
                            parameter,
                            new ResourceEffectCompletion.Entry(),
                            kind,
                            Correspondence: null,
                            Observation: null)))));

        ResolvedResourceEffect narrowed =
            complete.Snapshot.Effects.First(effect =>
                Assert.IsType<ResourceEffect.Consume>(
                    effect.Effect).Kind is not null);
        var otherKind = new ResourceKindReference(
            new ResourceKindIdentity(
                "example.operation-slot-other-kind"),
            [variable]);
        ResourceEffectLocation nested =
            new ResourceEffectLocation.OperationSlot(
                new ResourceEffectLocation.OperationSlot(
                    parameter,
                    kind),
                kind: null);
        Assert.True(
            ResourceEffectResolver.TryEffectiveKind(
                narrowed,
                direct: null,
                nested,
                out ResolvedResourceKindReference? effective));
        Assert.Equal(kind.Identity, effective?.Identity);
        Assert.False(
            ResourceEffectResolver.TryEffectiveKind(
                narrowed,
                direct: null,
                new ResourceEffectLocation.OperationSlot(
                    nested,
                    otherKind),
                out _));
    }

    [Fact]
    public void CompatibilityLimitPreservesKnownConflict()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[1]
                .Target;
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.operation-a",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.operation-b",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Transparent,
                    ResourceOperationThrows.Possible,
                    Guard: null)),
            Model(
                "example.operation-c",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Never,
                    Guard: null)),
            Model(
                "example.deferred-outcome",
                target,
                new ResourceEffect.Outcome(
                    new ResourceEffectLocalIdentity("result"),
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectOutcomeTest.ExactType(
                        "Example.MissingType"))));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                Resolve(
                    admission,
                    new ResourceEffectResolutionLimits(
                        maxCompatibilityComparisons: 1)));
        Assert.NotEmpty(conflict.Conflicts);
        Assert.Contains(
            conflict.Gaps,
            gap => gap.WorkDimension
                == ResourceEffectResolutionWorkDimension
                    .CompatibilityComparisons);

        ResourceEffectResolutionOutcome.Conflict diagnosticConflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                Resolve(
                    admission,
                    new ResourceEffectResolutionLimits(
                        maxRetainedDiagnostics: 1)));
        Assert.NotEmpty(diagnosticConflict.Conflicts);
        Assert.Single(diagnosticConflict.Gaps);
        Assert.Equal(
            ResourceEffectResolutionWorkDimension.RetainedDiagnostics,
            diagnosticConflict.Gaps[0].WorkDimension);
    }

    [Fact]
    public void AbsentOperationIsInert()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[1]
                .Target;
        ResourceEffectMemberSelector member =
            Assert.IsType<ResourceEffectTargetSelector.Member>(target)
                .Selector;
        ResourceEffectAdmission admission = Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                new ResourceEffectModelIdentity("example.absent"),
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                member.DeclaringType,
                                "MissingRent",
                                member.Kind,
                                member.IsStatic,
                                member.GenericArity,
                                member.CallingConvention,
                                member.HasThis,
                                member.ExplicitThis,
                                member.Parameters,
                                member.ReturnType)),
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            Guard: null),
                        [Provenance("example.absent", 0)]),
                ]));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));
        ResourceEffectTargetEvaluation evaluation =
            Assert.Single(complete.Evaluations);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Unmatched,
            evaluation.Kind);
        Assert.Empty(complete.Snapshot.Effects);
    }

    [Fact]
    public void ConflictingResolvedDeclarationsFailAtomically()
    {
        ResourceEffectModelDefinition shipped =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectTypedDeclaration returned =
            shipped.TypedDeclarations[3];
        var conflictingModel = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            new ResourceEffectModelIdentity("example.array-pool-conflict"),
            [
                new ResourceKindDefinition(
                    ArrayPoolResourceEffectModel.BufferKind,
                    1,
                    [Provenance("example.array-pool-conflict", 0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    returned.Target,
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        new ResourceKindReference(
                            ArrayPoolResourceEffectModel.BufferKind,
                            [
                                new ResourceEffectGenericVariable(
                                    ResourceEffectGenericVariableKind.Type,
                                    0),
                            ]),
                        Correspondence: null,
                        Observation: null),
                    [Provenance("example.array-pool-conflict", 1)]),
            ]);
        ResourceEffectAdmission admission =
            Admit(shipped, conflictingModel);

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                Resolve(admission));
        Assert.NotEmpty(conflict.Conflicts);
        Assert.All(
            conflict.Conflicts,
            evidence =>
            {
                AssertCanonicalBoundOrder(evidence.Effects);
                Assert.Contains(
                    evidence.Effects.SelectMany(effect => effect.Sources),
                    source =>
                        source.Model
                            == ArrayPoolResourceEffectModel.Identity);
                Assert.Contains(
                    evidence.Effects.SelectMany(effect => effect.Sources),
                    source =>
                        source.Model
                            == new ResourceEffectModelIdentity(
                                "example.array-pool-conflict"));
            });
    }

    [Fact]
    public void ResolvedEffectsUseOccurrenceMajorCanonicalOrder()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[1]
                .Target;
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.pass-first",
                target,
                new ResourceEffect.Pass(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectLocation.Return(),
                    Identity: null)),
            Model(
                "example.operation-second",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(admission));
        ImmutableArray<ResolvedResourceEffect> effects =
            complete.Snapshot.Effects;

        Assert.True(effects.Length >= 4);
        Assert.Equal(0, effects.Length % 2);
        for (int index = 0; index < effects.Length; index += 2)
        {
            Assert.Equal(
                effects[index].Occurrence,
                effects[index + 1].Occurrence);
            AssertCanonicalBoundOrder(
                effects[index..(index + 2)]);
        }
    }

    [Fact]
    public void ParticipantIdentityOrderIgnoresInputArrayOrder()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        var first = new CatalogCallGraphParticipant(
            index,
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect ordering first")));
        var second = new CatalogCallGraphParticipant(
            index,
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect ordering second")));
        ResourceEffectAdmission admission = Admit(
            Model(
                "example.participant-order",
                ArrayEmptyTarget(),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete forward =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(admission, [first, second]));
        ResourceEffectResolutionOutcome.Incomplete reverse =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                Resolve(admission, [second, first]));
        CatalogCallGraphParticipant[] forwardOrder =
        [
            .. forward.Effects.Select(
                effect => effect.Occurrence.Participant),
        ];
        CatalogCallGraphParticipant[] reverseOrder =
        [
            .. reverse.Effects.Select(
                effect => effect.Occurrence.Participant),
        ];

        Assert.True(
            forwardOrder.SequenceEqual(
                reverseOrder,
                ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public void KindlessAndSpecificEquivalentTransitionsAreCompatible()
    {
        ResourceEffectModelDefinition shipped =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectTypedDeclaration returned =
            shipped.TypedDeclarations[3];
        ResourceEffect.Release release =
            Assert.IsType<ResourceEffect.Release>(returned.Effect);
        ResourceEffectModelDefinition kindless = Model(
            "example.kindless-release",
            returned.Target,
            new ResourceEffect.Release(
                release.Source,
                release.When,
                Kind: null,
                release.Correspondence,
                release.Observation));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                Resolve(Admit(shipped, kindless)));

        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.Effect is ResourceEffect.Release
                    { Kind: null });
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.Effect is ResourceEffect.Release
                    { Kind: not null });
    }

    [Fact]
    public void UnguardedOperationOverlapsGuardedOperation()
    {
        ResourceEffectTargetSelector rentTarget =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[1]
                .Target;
        ResourceEffectModelDefinition unguarded = Model(
            "example.unguarded-operation",
            rentTarget,
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                Guard: null));
        ResourceEffectModelDefinition guarded = Model(
            "example.guarded-operation",
            rentTarget,
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Transparent,
                ResourceOperationThrows.Possible,
                new ResourceEffectGuard.ExactRuntimeType(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectSignatureLocation.Receiver())));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                Resolve(Admit(unguarded, guarded)));

        Assert.NotEmpty(conflict.Conflicts);
    }

    static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectAdmission admission,
        ResourceEffectResolutionLimits? limits = null,
        LibraryBodyIndex? index = null)
    {
        index ??= LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local(
                    "resolved resource-effect test"));
        return ResourceEffectResolver.Resolve(
            admission,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(
                    FixturePath)),
            [new CatalogCallGraphParticipant(index, assembly)],
            limits);
    }

    static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectAdmission admission,
        CatalogCallGraphParticipant[] participants) =>
        ResourceEffectResolver.Resolve(
            admission,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(
                    FixturePath)),
            participants,
            cancellationToken:
                TestContext.Current.CancellationToken);

    static ResourceEffectResolutionOutcome ResolveSynthetic(
        byte[] image,
        string assemblyName,
        ResourceEffectAdmission admission,
        ResourceEffectResolutionLimits? limits = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-resolved-effects-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, assemblyName + ".dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence);
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "synthetic resolved resource-effect test"));
            return ResourceEffectResolver.Resolve(
                admission,
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(path)),
                [new CatalogCallGraphParticipant(index, assembly)],
                limits,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static ResourceEffectAdmission SyntheticMethodModel(
        string assemblyName,
        string methodName,
        ResourceEffectMemberKind kind,
        ImmutableArray<ResourceEffectParameterSelector> parameters,
        bool isStatic = true,
        int genericArity = 0,
        string declaringTypeName = "Owner",
        int declaringTypeGenericArity = 0)
    {
        var identity = new ResourceEffectModelIdentity(
            $"example.{assemblyName.ToLowerInvariant()}");
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                assemblyName,
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "N",
            [
                new ResourceTypeNameSegment(
                    declaringTypeName,
                    declaringTypeGenericArity),
            ],
            [
                .. Enumerable.Range(
                        0,
                        declaringTypeGenericArity)
                    .Select(index =>
                        (ResourceTypeExpression)
                            new ResourceTypeExpression.Variable(
                                new ResourceEffectGenericVariable(
                                    ResourceEffectGenericVariableKind.Type,
                                    index))),
            ]);
        return Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                identity,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                declaringType,
                                methodName,
                                kind,
                                isStatic,
                                genericArity,
                                ResourceEffectCallingConvention.Default,
                                hasThis: !isStatic,
                                explicitThis: false,
                                parameters,
                                CoreLibraryType("System", "Void"))),
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            Guard: null),
                        [Provenance(identity.Value, 0)]),
                ]));
    }

    static ResourceEffectAdmission SyntheticVarargMethodModel(
        string assemblyName,
        string methodName)
    {
        var identity = new ResourceEffectModelIdentity(
            $"example.{assemblyName.ToLowerInvariant()}");
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                assemblyName,
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "N",
            [new ResourceTypeNameSegment("Owner", 0)]);
        return Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                identity,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                declaringType,
                                methodName,
                                ResourceEffectMemberKind.Method,
                                isStatic: true,
                                genericArity: 0,
                                ResourceEffectCallingConvention.VarArgs,
                                hasThis: false,
                                explicitThis: false,
                                [
                                    new ResourceEffectParameterSelector(
                                        CoreLibraryType(
                                            "System",
                                            "Int32"),
                                        ResourceEffectRefKind.Value),
                                ],
                                CoreLibraryType(
                                    "System",
                                    "Void"))),
                        new ResourceEffect.Operation(
                            ResourceOperationBoundary.Ordinary,
                            ResourceOperationThrows.Possible,
                            Guard: null),
                        [Provenance(identity.Value, 0)]),
                ]));
    }

    static byte[] BuildDirectCallAssembly(
        string assemblyName,
        string targetName,
        MethodAttributes targetAttributes,
        byte[] targetSignature,
        ParameterAttributes? parameterAttributes = null,
        int otherPropertySemanticsRows = 0,
        MethodSemanticsAttributes[]? propertySemantics = null,
        bool propertyOnDifferentType = false,
        MethodSemanticsAttributes[]? eventSemantics = null,
        int methodGenericParameterRows = 0,
        int methodGenericParameterStartIndex = 0,
        bool instantiateGenericMethod = false,
        int typeGenericParameterRows = 0,
        int typeGenericParameterStartIndex = 0,
        bool eventTargetsCaller = false,
        bool useNewObject = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(
                typeGenericParameterRows == 0
                    ? "Owner"
                    : $"Owner`{typeGenericParameterRows}"),
            baseType: objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0;
            index < typeGenericParameterRows;
            index++)
        {
            metadata.AddGenericParameter(
                owner,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{index}"),
                typeGenericParameterStartIndex + index);
        }
        TypeDefinitionHandle propertyOwner = owner;
        if (propertyOnDifferentType)
        {
            propertyOwner = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Other"),
                baseType: objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
        }

        ParameterHandle parameterList =
            MetadataTokens.ParameterHandle(1);
        if (parameterAttributes is { } attributes)
        {
            metadata.AddParameter(
                attributes,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var targetIl = new BlobBuilder();
        targetIl.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetIl));

        MethodDefinitionHandle target =
            metadata.AddMethodDefinition(
                targetAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(targetName),
                metadata.GetOrAddBlob(targetSignature),
                targetBody,
                parameterList);
        for (int index = 0;
            index < methodGenericParameterRows;
            index++)
        {
            metadata.AddGenericParameter(
                target,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{index}"),
                methodGenericParameterStartIndex + index);
        }
        EntityHandle callTarget = target;
        if (instantiateGenericMethod)
        {
            callTarget = metadata.AddMethodSpecification(
                target,
                metadata.GetOrAddBlob(
                    new byte[] { 0x0A, 0x01, 0x08 }));
        }
        var callerIl = new BlobBuilder();
        if (parameterAttributes is not null)
            callerIl.WriteByte((byte)ILOpCode.Ldnull);
        callerIl.WriteByte(
            (byte)(useNewObject
                ? ILOpCode.Newobj
                : ILOpCode.Call));
        callerIl.WriteInt32(MetadataTokens.GetToken(callTarget));
        if (useNewObject)
            callerIl.WriteByte((byte)ILOpCode.Pop);
        callerIl.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl));
        MethodDefinitionHandle caller =
            metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            callerBody,
            parameterAttributes is null
                ? parameterList
                : MetadataTokens.ParameterHandle(2));

        if (otherPropertySemanticsRows > 0
            || propertySemantics is not null)
        {
            PropertyDefinitionHandle property =
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(
                        new byte[] { 0x08, 0x00, 0x08 }));
            metadata.AddPropertyMap(propertyOwner, property);
            IEnumerable<MethodSemanticsAttributes> semantics =
                propertySemantics
                ?? Enumerable.Repeat(
                    MethodSemanticsAttributes.Other,
                    otherPropertySemanticsRows);
            foreach (MethodSemanticsAttributes value in semantics)
            {
                metadata.AddMethodSemantics(
                    property,
                    value,
                    target);
            }
        }
        if (eventSemantics is not null)
        {
            EventDefinitionHandle @event =
                metadata.AddEvent(
                    EventAttributes.None,
                    metadata.GetOrAddString("Changed"),
                    objectType);
            metadata.AddEventMap(owner, @event);
            foreach (MethodSemanticsAttributes value
                in eventSemantics)
            {
                metadata.AddMethodSemantics(
                    @event,
                    value,
                    eventTargetsCaller ? caller : target);
            }
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildInterfaceAndConcreteCallAssembly(
        string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle resourceInterface =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("IResource"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle resource =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Class
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Resource"),
                baseType: objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Calls"),
            baseType: objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddInterfaceImplementation(
            resource,
            resourceInterface);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var targetIl = new BlobBuilder();
        targetIl.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetIl));
        var interfaceCallIl = new BlobBuilder();
        interfaceCallIl.WriteByte((byte)ILOpCode.Ldnull);
        interfaceCallIl.WriteByte((byte)ILOpCode.Callvirt);
        interfaceCallIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)));
        interfaceCallIl.WriteByte((byte)ILOpCode.Ret);
        int interfaceCallBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(interfaceCallIl));
        var concreteCallIl = new BlobBuilder();
        concreteCallIl.WriteByte((byte)ILOpCode.Ldnull);
        concreteCallIl.WriteByte((byte)ILOpCode.Callvirt);
        concreteCallIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(2)));
        concreteCallIl.WriteByte((byte)ILOpCode.Ret);
        int concreteCallBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(concreteCallIl));

        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(
                new byte[] { 0x20, 0x00, 0x01 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Final
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(
                new byte[] { 0x20, 0x00, 0x01 }),
            targetBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ThroughInterface"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            interfaceCallBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ThroughConcrete"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            concreteCallBody,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildUnsupportedDeclaringTypeCallAssembly(
        string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            baseType: objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var declaringSignature = new BlobBuilder();
        for (int depth = 0;
            depth <= SignatureBlobGuard.DefaultMaxDepth;
            depth++)
        {
            declaringSignature.WriteByte(0x1D);
        }
        declaringSignature.WriteByte(0x12);
        declaringSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(owner) << 2);
        TypeSpecificationHandle declaringType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(declaringSignature));
        MemberReferenceHandle target =
            metadata.AddMemberReference(
                declaringType,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x01 }));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var callerIl = new BlobBuilder();
        callerIl.WriteByte((byte)ILOpCode.Call);
        callerIl.WriteInt32(MetadataTokens.GetToken(target));
        callerIl.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildVarargMethodDefinitionParentAssembly(
        string assemblyName,
        int precedingMethods,
        string parentName,
        string referenceName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            precedingMethods);
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            baseType: objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        byte[] varargSignature = [0x05, 0x01, 0x01, 0x08];
        var methodNames = Enumerable.Range(
                0,
                precedingMethods)
            .Select(index => $"Dummy{index}")
            .Append(parentName)
            .ToList();
        if (!string.Equals(
                parentName,
                referenceName,
                StringComparison.Ordinal))
        {
            methodNames.Add(referenceName);
        }
        MemberReferenceHandle reference =
            metadata.AddMemberReference(
                MetadataTokens.MethodDefinitionHandle(
                    precedingMethods + 1),
                metadata.GetOrAddString(referenceName),
                metadata.GetOrAddBlob(varargSignature));
        foreach (string _ in methodNames)
        {
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var methodBodies = new List<int>(methodNames.Count);
        foreach (string _ in methodNames)
        {
            var methodIl = new BlobBuilder();
            methodIl.WriteByte((byte)ILOpCode.Ret);
            methodBodies.Add(
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(methodIl)));
        }
        var callerIl = new BlobBuilder();
        callerIl.WriteByte((byte)ILOpCode.Ldc_i4_0);
        callerIl.WriteByte((byte)ILOpCode.Call);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(reference));
        callerIl.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl));

        for (int index = 0; index < methodNames.Count; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(methodNames[index]),
                metadata.GetOrAddBlob(varargSignature),
                methodBodies[index],
                MetadataTokens.ParameterHandle(index + 1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            callerBody,
            MetadataTokens.ParameterHandle(
                methodNames.Count + 1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ResourceEffectAdmission Admit(
        params ResourceEffectModelDefinition[] definitions) =>
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(definitions))
            .Admission;

    static ResourceEffectAdmission MethodModel(
        string methodName,
        int genericArity,
        ResourceEffectParameterSelector[] parameters,
        ResourceEffect effect)
    {
        var identity = new ResourceEffectModelIdentity(
            $"example.{methodName.ToLowerInvariant()}");
        ResourceTypeExpression.Named declaringType = new(
            new ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);
        return Admit(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                identity,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        new ResourceEffectTargetSelector.Member(
                            new ResourceEffectMemberSelector(
                                declaringType,
                                methodName,
                                ResourceEffectMemberKind.Method,
                                isStatic: true,
                                genericArity,
                                ResourceEffectCallingConvention.Default,
                                hasThis: false,
                                explicitThis: false,
                                [.. parameters],
                                CoreLibraryType("System", "Void"))),
                        effect,
                        [
                            new ResourceDeclarationProvenance(
                                identity,
                                ResourceDeclarationAuthority.ProductShipped,
                                new InertString(
                                    TextPolicy.Field,
                                    identity.Value),
                                0),
                        ]),
                ]));
    }

    static ResourceEffectModelDefinition Model(
        string name,
        ResourceEffectTargetSelector target,
        ResourceEffect effect)
    {
        var identity = new ResourceEffectModelIdentity(name);
        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            identity,
            [],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    target,
                    effect,
                    [
                        new ResourceDeclarationProvenance(
                            identity,
                            ResourceDeclarationAuthority.ProductShipped,
                            new InertString(TextPolicy.Field, name),
                            0),
                    ]),
            ]);
    }

    static ResourceEffectTargetSelector ArrayEmptyTarget()
    {
        ResourceEffectGenericVariable variable = new(
            ResourceEffectGenericVariableKind.Method,
            0);
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                CoreLibraryType("System", "Array"),
                "Empty",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                genericArity: 1,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                parameters: [],
                new ResourceTypeExpression.SzArray(
                    new ResourceTypeExpression.Variable(variable))));
    }

    static ResourceEffectTargetSelector GenericEchoTarget()
    {
        ResourceEffectGenericVariable variable = new(
            ResourceEffectGenericVariableKind.Method,
            0);
        ResourceTypeExpression.Variable variableType = new(variable);
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                FixtureType("Entry"),
                "GenericEcho",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                genericArity: 1,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        variableType,
                        ResourceEffectRefKind.Value),
                ],
                variableType));
    }

    static ResourceEffect.Operation GuardedOperation(
        ResourceEffectSignatureLocation expected) =>
        new(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            new ResourceEffectGuard.ExactRuntimeType(
                new ResourceEffectLocation.Return(),
                expected));

    static ResourceAssemblySelector FixtureAssembly() =>
        new(
            "ILInspector.Analysis.OwnershipFlowFixtures",
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);

    static ResourceTypeExpression.Named FixtureType(string name) =>
        new(
            FixtureAssembly(),
            "Ownership",
            [new ResourceTypeNameSegment(name, 0)]);

    static void AssertCanonicalBoundOrder(
        ImmutableArray<ResolvedResourceEffect> effects)
    {
        for (int index = 1; index < effects.Length; index++)
        {
            Assert.True(
                ResourceEffectResolver.CompareCanonicalBoundEffects(
                    effects[index - 1],
                    effects[index]) <= 0);
        }
    }

    static ResourceTypeExpression.Named CoreLibraryType(
        string @namespace,
        string name) =>
        new(
            new ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            @namespace,
            [new ResourceTypeNameSegment(name, 0)]);

    static ResourceDeclarationProvenance Provenance(
        string model,
        int ordinal) =>
        new(
            new ResourceEffectModelIdentity(model),
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(TextPolicy.Field, model),
            ordinal);
}
