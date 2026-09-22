using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    [Fact]
    public void ShippedArrayPoolModelBoundsDefinitionResolutionToCandidates()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            OwnershipFixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                OwnershipFixturePath,
                AssemblyResolutionProvenance.Local(
                    "resource-effect candidate-selection test"));
        var participant =
            new CatalogCallGraphParticipant(index, assembly);
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(
                OwnershipFixturePath));

        ResourceEffectResolutionOutcome initial =
            ResourceEffectResolver.Resolve(
                policy,
                ArrayPoolResourceEffectModel.Create(),
                [participant],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        int candidateCount =
            Receipt(initial).Population.Results.Length;

        Assert.InRange(
            candidateCount,
            1,
            index.DirectCalls.Length - 1);
        Assert.DoesNotContain(
            Receipt(initial).Population.Results,
            result =>
                result.Call.Callee.DeclaringType.Name.Contains(
                    "OwnershipSink",
                    StringComparison.Ordinal));
        ResourceEffectResolutionOutcome bounded =
            ResourceEffectResolver.Resolve(
                policy,
                ArrayPoolResourceEffectModel.Create(),
                [participant],
                directCallLimits:
                    new(maxInvocationOccurrences: candidateCount),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(initial.GetType(), bounded.GetType());
        Assert.Equal(
            candidateCount,
            Receipt(bounded).Population.Results.Length);

        static ResourceEffectResolutionReceipt Receipt(
            ResourceEffectResolutionOutcome outcome) =>
            outcome switch
            {
                ResourceEffectResolutionOutcome.Complete complete =>
                    complete.Receipt,
                ResourceEffectResolutionOutcome.Incomplete incomplete =>
                    incomplete.Receipt,
                ResourceEffectResolutionOutcome.Conflict conflict =>
                    conflict.Receipt,
                _ => throw new InvalidOperationException(
                    "Candidate selection unexpectedly rejected resolution."),
            };
    }

    [Fact]
    public void ShippedArrayPoolModelResolvesFrameworkOperations()
    {
        ResourceEffectResolutionOutcome outcome = ResolveEffects(
            ArrayPoolResourceEffectModel.Create(),
            ResolveOwnershipFixture());
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                outcome);

        Assert.Contains(
            complete.Evaluations,
            evaluation =>
                evaluation.Declaration.Effect
                    is ResourceEffect.Authority
                && evaluation.Kind
                    == ResourceEffectTargetEvaluationKind.Resolved);
        Assert.Contains(
            complete.Evaluations,
            evaluation =>
                evaluation.Declaration.Effect
                    is ResourceEffect.Acquire
                && evaluation.Kind
                    == ResourceEffectTargetEvaluationKind.Resolved);
        Assert.Contains(
            complete.Evaluations,
            evaluation =>
                evaluation.Declaration.Effect
                    is ResourceEffect.Release
                && evaluation.Kind
                    == ResourceEffectTargetEvaluationKind.Resolved);
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
    public void ShippedArrayPoolModelResolvesFrameworkWrapperOperations()
    {
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    ResolveOwnershipFixture()));
        ResolvedResourceEffect[] wrappers =
        [
            .. complete.Snapshot.Effects.Where(effect =>
                effect.DirectCall.Call.Caller.Name
                    == "RentAndUseFrameworkWrappers"
                && effect.Effect
                    is ResourceEffect.Derive
                        or ResourceEffect.Operation),
        ];

        Assert.NotEmpty(wrappers);
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == ".ctor"
                && effect.DirectCall.Call.Kind == CallKind.Call);
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == "op_Implicit");
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == "Slice");
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == "get_Span");
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == "AsSpan");
        Assert.Contains(
            wrappers,
            effect =>
                effect.DirectCall.Definition.Member.Name == "AsMemory");
        Assert.All(
            wrappers,
            effect => Assert.DoesNotContain(
                "Ownership",
                effect.DirectCall.Definition.Assembly.Name,
                StringComparison.Ordinal));
    }

    [Fact]
    public void SelectorLimitRetainsPositiveEvidence()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(
                    ArrayPoolResourceEffectModel.Create(),
                    calls,
                    new ResourceEffectResolutionLimits(
                        maxSelectorEvaluations: calls.Results.Length)));

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
    public void InterfaceEffectAppliesToExactImplicitImplementation()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant();
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        Assert.True(interfaceCall.Definition.IsInterfaceDefinition);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-application",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, complete.Snapshot.Effects.Length);
        ResolvedResourceEffect implementation =
            Assert.Single(
                complete.Snapshot.Effects,
                effect => !effect.DirectCall.Definition
                    .IsInterfaceDefinition);
        ResourceEffectInterfaceApplicationEvidence evidence =
            Assert.IsType<
                ResourceEffectInterfaceApplicationEvidence>(
                    implementation.InterfaceApplication);
        Assert.Equal(
            interfaceCall.Definition.MetadataToken,
            evidence.InterfaceDeclaration.MetadataToken);
        Assert.Equal(
            implementation.DirectCall.Definition.MetadataToken,
            evidence.Implementation.MetadataToken);
        Assert.Equal(
            implementation.DirectCall.Catalog,
            evidence.InterfacePath.Catalog);
        Assert.Same(
            implementation.DirectCall.Generation,
            evidence.InterfacePath.Generation);
        Assert.Same(
            complete.Receipt.Population.Generation,
            evidence.InterfacePath.Generation);
        Assert.IsType<
            ResourceEffectMethodImplementationEvidence.Implicit>(
                evidence.Method);
        Assert.DoesNotContain(
            complete.Evaluations.SelectMany(
                evaluation => evaluation.Gaps),
            gap => gap.Kind
                == ResourceEffectResolutionGapKind
                    .DeferredInterfaceApplication);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceEffectAppliesWithoutInterfaceInvocation(
        bool explicitImplementation)
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            explicitImplementation: explicitImplementation,
            includeInterfaceCall: false);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-application-without-interface-call",
                InterfaceTarget(participant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation =
            Assert.Single(complete.Snapshot.Effects);
        Assert.NotNull(implementation.InterfaceApplication);
        Assert.Equal(
            explicitImplementation
                ? "ExplicitTarget"
                : "Target",
            implementation.DirectCall.Definition.Member.Name);
    }

    [Fact]
    public void VersionAgnosticSelectorRetainsSecondVersionImplementationCandidate()
    {
        const string AssemblyName = "VersionSplitInterfaceTarget";
        SyntheticParticipant interfaceParticipant =
            CreateInterfaceParticipant(
                explicitImplementation: true,
                includeInterfaceCall: false,
                assemblyName: AssemblyName,
                assemblyVersion: new Version(2, 0, 0, 0));
        SyntheticParticipant caller =
            CreateVersionSplitInterfaceCaller(interfaceParticipant);
        ResourceEffectTargetSelector.Member exact =
            Assert.IsType<ResourceEffectTargetSelector.Member>(
                InterfaceTarget(interfaceParticipant));
        ResourceTypeExpression.Named declaringType =
            exact.Selector.DeclaringType;
        ResourceEffectTargetSelector target = TargetWithAssembly(
            exact.Selector,
            declaringType,
            new ResourceAssemblySelector(
                declaringType.Assembly.SimpleName,
                declaringType.Assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Any));
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.version-agnostic-interface",
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome outcome =
            ResourceEffectResolver.Resolve(
                caller.Policy,
                admission,
                [caller.Participant],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        ResourceEffectResolutionReceipt receipt = outcome switch
        {
            ResourceEffectResolutionOutcome.Complete complete =>
                complete.Receipt,
            ResourceEffectResolutionOutcome.Incomplete incomplete =>
                incomplete.Receipt,
            _ => throw new Xunit.Sdk.XunitException(
                $"Unexpected outcome {outcome.GetType().Name}."),
        };
        Assert.Equal(2, receipt.Population.Results.Length);
        Assert.Contains(
            receipt.Population.Results,
            result =>
                result.Call.Callee.Name
                    == "ExplicitTarget");
    }

    [Fact]
    public void InterfaceCandidateSelectionRejectsDisjointMemberName()
    {
        SyntheticParticipant interfaceParticipant =
            CreateInterfaceParticipant(includeInterfaceCall: false);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-candidate-name",
                InterfaceTarget(interfaceParticipant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        SyntheticParticipant unrelated = CreateSynthetic(new()
        {
            TargetName = "Unrelated",
            TargetAttributes = System.Reflection.MethodAttributes.Public,
            TargetSignature = [0x20, 0x00, 0x01],
            CallKind = CallKind.CallVirtual,
        });
        var selector =
            new ResourceEffectDirectCallCandidateSelector(admission);

        Assert.DoesNotContain(
            unrelated.Participant.CallGraph.DirectCalls,
            call => selector.Includes(unrelated.Participant, call));
    }

    [Fact]
    public void InterfaceCandidateSelectionRejectsExternalNonMethodImplName()
    {
        SyntheticParticipant interfaceParticipant =
            CreateInterfaceParticipant(includeInterfaceCall: false);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.external-interface-candidate-name",
                InterfaceTarget(interfaceParticipant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        ExternalSyntheticParticipant unrelated =
            CreateExternalSynthetic(new()
            {
                TargetName = "Unrelated",
                TargetAttributes =
                    System.Reflection.MethodAttributes.Public,
                TargetSignature = [0x20, 0x00, 0x01],
                MemberReferenceSignature = [0x20, 0x00, 0x01],
                CallArgumentKinds = [SyntheticStackValue.Null],
            });
        var selector =
            new ResourceEffectDirectCallCandidateSelector(
                admission,
                unrelated.Policy);

        Assert.DoesNotContain(
            unrelated.Participant.CallGraph.DirectCalls,
            call => selector.Includes(
                unrelated.Participant,
                call));
    }

    [Fact]
    public void InterfaceApplicationReceiptTracksGeneration()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant();
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-receipt",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete first =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ResourceEffectResolutionOutcome.Complete second =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.NotEqual(
            first.Receipt.ContentHash,
            second.Receipt.ContentHash);
    }

    [Fact]
    public void ExplicitMethodImplWinsOverPublicImplicitDecoy()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            explicitImplementation: true,
            addPublicDecoy: true);
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.explicit-interface-application",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect[] implementations =
        [
            .. complete.Snapshot.Effects.Where(effect =>
                !effect.DirectCall.Definition.IsInterfaceDefinition),
        ];
        ResolvedResourceEffect implementation =
            Assert.Single(implementations);
        Assert.Equal(
            "ExplicitTarget",
            implementation.DirectCall.Definition.Member.Name);
        ResourceEffectInterfaceApplicationEvidence evidence =
            Assert.IsType<
                ResourceEffectInterfaceApplicationEvidence>(
                    implementation.InterfaceApplication);
        Assert.IsType<
            ResourceEffectMethodImplementationEvidence.Explicit>(
                evidence.Method);
        Assert.DoesNotContain(
            complete.Snapshot.Effects,
            effect => effect.DirectCall.Definition.Member.Name == "Target"
                && !effect.DirectCall.Definition.IsInterfaceDefinition);
    }

    [Fact]
    public void SameSignatureMethodWithoutInterfaceImplIsNotApplicable()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            addNonImplementer: true);
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-lookalike",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.DoesNotContain(
            complete.Snapshot.Effects,
            effect => effect.DirectCall.Definition.Member.DeclaringType.Name
                == "Lookalike");
        Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.InterfaceApplication is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedGenericInterfaceApplicationUsesExactSubstitution(
        bool explicitImplementation)
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            explicitImplementation: explicitImplementation,
            addPublicDecoy: explicitImplementation,
            generic: true);
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.generic-interface-application",
                GenericTargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        Assert.IsType<ResourceEffectSelectorBinding.Resolved>(
            ResourceEffectSelectorBinder.Bind(
                admission.Models[0].Declarations[0],
                interfaceCall));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect applied = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.InterfaceApplication is not null);
        Assert.Equal(
            explicitImplementation
                ? "ExplicitTarget"
                : "Target",
            applied.DirectCall.Definition.Member.Name);
        if (explicitImplementation)
        {
            Assert.IsType<
                ResourceEffectMethodImplementationEvidence.Explicit>(
                    applied.InterfaceApplication!.Method);
        }
        else
        {
            Assert.IsType<
                ResourceEffectMethodImplementationEvidence.Implicit>(
                    applied.InterfaceApplication!.Method);
        }
        Assert.Equal(
            "Int32",
            Assert.Single(
                    applied.InterfaceApplication.InterfacePath
                        .ClosedInterfaceType.TypeArguments)
                .Name);
    }

    [Fact]
    public void ClosedInterfaceAppliesOnlyToMatchingConstructedType()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            generic: true,
            addStringImplementationCall: true);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.closed-interface-identity",
                ClosedInterfaceTarget(
                    participant,
                    "IContract",
                    CoreType("Int32")),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation = Assert.Single(
            complete.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition);
        Assert.Equal(
            "Int32",
            Assert.Single(
                    implementation.DirectCall.Call.Callee
                        .DeclaringType.TypeArguments)
                .Name);
        Assert.Single(implementation.InterfaceApplications);
        Assert.DoesNotContain(
            complete.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition
                && effect.DirectCall.Call.Callee.DeclaringType
                    .TypeArguments.Any(
                        argument => argument.Name == "String"));
    }

    [Fact]
    public void ClosedInterfaceAppliesToNonGenericImplementation()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            fixedGenericInterface: true);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.fixed-interface-application",
                ClosedInterfaceTarget(
                    participant,
                    "IFixed",
                    CoreType("Int32")),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation = Assert.Single(
            complete.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition);
        Assert.Equal(
            "Fixed",
            implementation.DirectCall.Definition.Member.DeclaringType.Name);
        ResourceEffectInterfaceApplicationEvidence application =
            Assert.Single(implementation.InterfaceApplications);
        Assert.Equal(
            "Int32",
            Assert.Single(
                    application.InterfacePath.ClosedInterfaceType
                        .TypeArguments)
                .Name);
    }

    [Fact]
    public void ConcreteOnlyInvocationReceivesInterfaceEffect()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            includeInterfaceCall: false);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.concrete-only-interface-application",
                InterfaceTarget(participant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation =
            Assert.Single(complete.Snapshot.Effects);
        Assert.False(
            implementation.DirectCall.Definition.IsInterfaceDefinition);
        Assert.Single(implementation.InterfaceApplications);
        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Resolved,
            Assert.Single(complete.Evaluations).Kind);
    }

    [Fact]
    public void MethodGenericApplicationBindsConcreteInvocationArgument()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            methodGeneric: true);
        ResourceEffectAdmission admission = AdmitModels(
            MethodGenericInterfaceModel(participant));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect interfaceEffect = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.DirectCall.Definition.IsInterfaceDefinition);
        ResolvedResourceEffect implementation = Assert.Single(
            complete.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition);
        Assert.Equal(
            "Int32",
            Assert.Single(interfaceEffect.GenericBindings).Value.Type.Name);
        ResolvedResourceEffectGenericBinding binding =
            Assert.Single(implementation.GenericBindings);
        Assert.Equal(
            ResourceEffectGenericVariableKind.Method,
            binding.Variable.Kind);
        Assert.Equal("String", binding.Value.Type.Name);
        Assert.Equal(
            "String",
            Assert.Single(
                    Assert.Single(implementation.ResourceKinds).Arguments)
                .Type.Name);
        Assert.Single(implementation.InterfaceApplications);
    }

    [Fact]
    public void EqualDirectAndInterfaceEffectsCoalesceProofAssociations()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant();
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        DirectCallDefinitionResolution.Resolved implementationCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[1]);
        ResourceEffect effect = new ResourceEffect.Operation(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            Guard: null);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-source",
                TargetFor(interfaceCall.Definition),
                effect),
            Model(
                "example.direct-source",
                TargetFor(implementationCall.Definition),
                effect));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation = Assert.Single(
            complete.Snapshot.Effects,
            resolved =>
                !resolved.DirectCall.Definition.IsInterfaceDefinition);
        Assert.Equal(2, implementation.Sources.Length);
        Assert.Equal(
            2,
            implementation.Sources
                .Select(source => source.Declaration)
                .Distinct()
                .Count());
        ResourceEffectInterfaceApplicationEvidence application =
            Assert.Single(implementation.InterfaceApplications);
        Assert.Same(application, implementation.InterfaceApplication);
        ResolvedResourceEffectSource interfaceSource = Assert.Single(
            implementation.Sources,
            source => source.Model.Value == "example.interface-source");
        ResolvedResourceEffectSource directSource = Assert.Single(
            implementation.Sources,
            source => source.Model.Value == "example.direct-source");
        Assert.Same(
            application,
            Assert.Single(interfaceSource.InterfaceApplications));
        Assert.Empty(directSource.InterfaceApplications);
    }

    [Fact]
    public void EqualInterfaceDeclarationsCoalesceDistinctModelSources()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            includeInterfaceCall: false);
        ResourceEffectTargetSelector target =
            InterfaceTarget(participant);
        ResourceEffect effect = new ResourceEffect.Operation(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            Guard: null);
        ResourceEffectAdmission admission = AdmitModels(
            Model("example.interface-first", target, effect),
            Model("example.interface-second", target, effect));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect implementation =
            Assert.Single(complete.Snapshot.Effects);
        Assert.Equal(2, implementation.Sources.Length);
        Assert.Equal(
            ["example.interface-first", "example.interface-second"],
            implementation.Sources
                .Select(source => source.Model.Value)
                .Order(StringComparer.Ordinal));
        Assert.All(
            implementation.Sources,
            source => Assert.Single(source.InterfaceApplications));
    }

    [Fact]
    public void UnresolvedConcreteOccurrencePreventsCompleteInterfaceAbsence()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            addPublicDecoy: true,
            includeInterfaceCall: false);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.incomplete-concrete-interface-coverage",
                InterfaceTarget(participant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    directCallLimits:
                        new DirectCallDefinitionResolutionLimits(
                            maxInvocationOccurrences: 1),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            ResourceEffectTargetEvaluationKind.Incomplete,
            Assert.Single(incomplete.Evaluations).Kind);
        Assert.Contains(
            incomplete.Gaps,
            gap => gap.Kind
                == ResourceEffectResolutionGapKind
                    .InterfaceApplicationIncomplete);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("incomplete")]
    [InlineData("malformed-interface-only")]
    [InlineData("malformed-interface")]
    [InlineData("malformed-method")]
    public void InterfaceFailuresRemainVisibleWithoutInterfaceCall(
        string failure)
    {
        SyntheticParticipant participant = failure switch
        {
            "unsupported" => CreateInterfaceParticipant(
                explicitImplementation: true,
                methodImplBodyAsMemberReference: true,
                includeInterfaceCall: false),
            "incomplete" => CreateInterfaceParticipant(
                unresolvedInterfaceImpl: true,
                includeInterfaceCall: false),
            "malformed-interface-only" => CreateInterfaceParticipant(
                includeInterfaceCall: false,
                invalidOnlyInterfaceImpl: true),
            "malformed-interface" => CreateInterfaceParticipant(
                includeInterfaceCall: false,
                malformedInterfaceImpl: true),
            "malformed-method" => CreateInterfaceParticipant(
                includeInterfaceCall: false,
                malformedMethodImpl: true),
            _ => throw new InvalidOperationException(
                $"Unknown failure '{failure}'."),
        };
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.concrete-only-interface-failure",
                InterfaceTarget(participant),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResourceEffectResolutionGapKind expected = failure switch
        {
            "unsupported"
                or "malformed-interface-only"
                or "malformed-interface"
                or "malformed-method" =>
                ResourceEffectResolutionGapKind
                    .InterfaceApplicationUnsupported,
            _ =>
                ResourceEffectResolutionGapKind
                    .InterfaceApplicationIncomplete,
        };
        Assert.Contains(
            incomplete.Gaps,
            gap => gap.Kind == expected);
        Assert.DoesNotContain(
            incomplete.Effects,
            effect => effect.DirectCall.Definition.IsInterfaceDefinition);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("unsupported")]
    [InlineData("incomplete")]
    public void InterfaceApplicationFailuresPreventImplicitFallback(
        string failure)
    {
        SyntheticParticipant participant = failure switch
        {
            "ambiguous" => CreateInterfaceParticipant(
                explicitImplementation: true,
                addPublicDecoy: true,
                addDuplicateMethodImpl: true),
            "unsupported" => CreateInterfaceParticipant(
                explicitImplementation: true,
                addPublicDecoy: true,
                methodImplBodyAsMemberReference: true),
            "incomplete" => CreateInterfaceParticipant(
                unresolvedInterfaceImpl: true),
            _ => throw new InvalidOperationException(
                $"Unknown failure '{failure}'."),
        };
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-failure",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResourceEffectResolutionGapKind expected = failure switch
        {
            "ambiguous" =>
                ResourceEffectResolutionGapKind
                    .InterfaceApplicationAmbiguous,
            "unsupported" =>
                ResourceEffectResolutionGapKind
                    .InterfaceApplicationUnsupported,
            _ =>
                ResourceEffectResolutionGapKind
                    .InterfaceApplicationIncomplete,
        };
        Assert.Contains(
            incomplete.Gaps,
            gap => gap.Kind == expected);
        Assert.DoesNotContain(
            incomplete.Effects,
            effect => effect.InterfaceApplication is not null);
        Assert.Contains(
            incomplete.Effects,
            effect => effect.DirectCall.Definition.IsInterfaceDefinition);
    }

    [Theory]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .CandidateApplications)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .InterfaceImplementations)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .MethodImplementations)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .CandidateMethods)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .SignatureNodes)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .RetainedApplications)]
    public void InterfaceApplicationLimitsRemainVisible(
        ResourceEffectInterfaceApplicationWorkDimension dimension)
    {
        SyntheticParticipant participant = dimension switch
        {
            ResourceEffectInterfaceApplicationWorkDimension
                .CandidateApplications
                or ResourceEffectInterfaceApplicationWorkDimension
                    .RetainedApplications =>
                CreateInterfaceParticipant(addNonImplementer: true),
            ResourceEffectInterfaceApplicationWorkDimension
                .InterfaceImplementations =>
                CreateInterfaceParticipant(addUnrelatedInterface: true),
            ResourceEffectInterfaceApplicationWorkDimension
                .MethodImplementations =>
                CreateInterfaceParticipant(
                    explicitImplementation: true,
                    addDuplicateMethodImpl: true),
            ResourceEffectInterfaceApplicationWorkDimension
                .CandidateMethods =>
                CreateInterfaceParticipant(
                    explicitImplementation: true,
                    addPublicDecoy: true),
            _ => CreateInterfaceParticipant(),
        };
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                baseline.Results[0]);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.interface-limit",
                TargetFor(interfaceCall.Definition),
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
        ResourceEffectInterfaceApplicationLimits limits = dimension switch
        {
            ResourceEffectInterfaceApplicationWorkDimension
                .CandidateApplications =>
                new(maxCandidateApplications: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .InterfaceImplementations =>
                new(maxInterfaceImplementations: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .MethodImplementations =>
                new(maxMethodImplementations: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .CandidateMethods =>
                new(maxCandidateMethods: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .SignatureNodes =>
                new(maxSignatureNodes: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .RetainedApplications =>
                new(maxRetainedApplications: 1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension)),
        };

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    interfaceLimits: limits,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Contains(
            incomplete.Gaps,
            gap => gap.InterfaceApplicationGap?.WorkDimension
                == dimension);
        if (dimension
            is ResourceEffectInterfaceApplicationWorkDimension
                    .CandidateApplications
                or ResourceEffectInterfaceApplicationWorkDimension
                    .RetainedApplications)
        {
            Assert.Contains(
                incomplete.Effects,
                effect => effect.InterfaceApplication is not null);
        }
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
    public void OutcomeRetainsResolvedSourceAndFiniteTest()
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

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    admission,
                    ResolveOwnershipFixture()));

        ResolvedResourceEffect[] outcomes =
        [
            .. complete.Snapshot.Effects.Where(
                effect => effect.Effect is ResourceEffect.Outcome),
        ];
        Assert.NotEmpty(outcomes);
        Assert.All(
            outcomes,
            outcome =>
            {
                Assert.IsType<ResolvedResourceEffectLocation.Boundary>(
                    outcome.Binding.Outcome!.Source);
                Assert.IsType<ResourceEffectOutcomeTest.NonNull>(
                    outcome.Binding.Outcome.Test.Declaration);
            });
    }

    [Fact]
    public void OccurrenceReferencesBindExactFieldCallbackOutcomeSlotAndGuard()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectAdmission admission =
            OccurrenceBindingAdmission(apply);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));

        ResolvedResourceEffect field = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Pass
                {
                    Source: ResourceEffectLocation.StructuralField,
                });
        ResolvedResourceEffectLocation.Field fieldLocation =
            Assert.IsType<ResolvedResourceEffectLocation.Field>(
                field.Binding.Locations.Single(location =>
                    location is ResolvedResourceEffectLocation.Field));
        Assert.Equal("Child", fieldLocation.Definition.MetadataName);
        Assert.NotEqual(0, fieldLocation.Definition.MetadataToken);
        Assert.Equal(
            "Byte",
            fieldLocation.Definition.FieldType.Type.Name);

        ResolvedResourceEffect callback = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Callback);
        Assert.Equal(
            1,
            callback.Binding.Callback!.Contract
                .DelegateParameterIndex);
        Assert.NotEqual(
            0,
            callback.Binding.Callback.Contract.InvokeMetadataToken);
        Assert.Equal(
            "Byte",
            Assert.Single(
                callback.Binding.Callback.Contract.ParameterTypes)
                .Type.Name);
        Assert.Equal(
            "Byte",
            callback.Binding.Callback.Contract.ReturnType.Type.Name);

        ResolvedResourceEffect callbackFlow = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Pass
                {
                    Source: ResourceEffectLocation.CallbackParameter,
                    Target: ResourceEffectLocation.CallbackReturn,
                });
        ResolvedResourceEffectLocation.CallbackParameter callbackParameter =
            Assert.IsType<
                ResolvedResourceEffectLocation.CallbackParameter>(
                    callbackFlow.Binding.Locations.Single(location =>
                        location is
                            ResolvedResourceEffectLocation
                                .CallbackParameter));
        ResolvedResourceEffectLocation.CallbackReturn callbackReturn =
            Assert.IsType<ResolvedResourceEffectLocation.CallbackReturn>(
                callbackFlow.Binding.Locations.Single(location =>
                    location is
                        ResolvedResourceEffectLocation.CallbackReturn));
        Assert.Equal("Byte", callbackParameter.Type.Type.Name);
        Assert.Equal("Byte", callbackReturn.Type.Type.Name);
        Assert.Same(
            callbackParameter.Callback,
            callbackReturn.Callback);

        ResolvedResourceEffect outcome = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Outcome);
        Assert.Equal(
            "BindingRejectedOutcome",
            outcome.Binding.Outcome!.Test.ExactType!.Name.Segments[^1]);

        ResolvedResourceEffect consume = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Consume);
        ResolvedResourceEffectLocation.OperationSlot slot =
            Assert.IsType<ResolvedResourceEffectLocation.OperationSlot>(
                consume.Binding.Locations.Single(location =>
                    location is
                        ResolvedResourceEffectLocation.OperationSlot));
        Assert.Equal(
            new ResourceKindIdentity("example.child"),
            slot.Kind!.Identity);
        Assert.Equal("Byte", Assert.Single(slot.Kind.Arguments).Type.Name);

        ResolvedResourceEffect operation = Assert.Single(
            complete.Snapshot.Effects,
            effect => effect.Effect is ResourceEffect.Operation);
        Assert.Equal(
            ResolvedResourceEffectBoundaryLocationKind.Parameter,
            Assert.IsType<ResolvedResourceEffectLocation.Boundary>(
                operation.Binding.Guard!.Subject).Kind);
        Assert.Equal(
            "Byte",
            operation.Binding.Guard.ExpectedType.Type.Name);
    }

    [Fact]
    public void MissingStructuralFieldEvidenceIsIncomplete()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectModelDefinition model =
            OccurrenceBindingModel(apply);
        ResourceEffectTypedDeclaration field =
            model.TypedDeclarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Pass
                {
                    Source: ResourceEffectLocation.StructuralField,
                });
        var original =
            (ResourceEffectLocation.StructuralField)
                ((ResourceEffect.Pass)field.Effect).Source;
        ResourceEffectMemberSelector selector = original.Selector;
        var wrongField = new ResourceEffectLocation.StructuralField(
            original.Root,
            new ResourceEffectMemberSelector(
                selector.DeclaringType,
                "ChildCount",
                selector.Kind,
                selector.IsStatic,
                selector.GenericArity,
                selector.CallingConvention,
                selector.HasThis,
                selector.ExplicitThis,
                selector.Parameters,
                selector.ReturnType));
        ResourceEffect.Pass pass = (ResourceEffect.Pass)field.Effect;
        var replaced = new ResourceEffectTypedDeclaration(
            field.Target,
            new ResourceEffect.Pass(
                wrongField,
                pass.Target,
                pass.Identity),
            field.Provenances);
        var broken = new ResourceEffectModelDefinition(
            model.Language,
            model.Identity,
            model.ResourceKinds,
            model.Declarations,
            model.TypedDeclarations.Replace(field, replaced));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(AdmitModels(broken), calls));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .OccurrenceIncomplete
                && gap.OccurrenceGap?.Kind
                    == ResourceEffectOccurrenceBindingGapKind
                        .StructuralField);
    }

    [Theory]
    [InlineData("ChildCount", "scalar")]
    [InlineData("ChildCounts", "array")]
    [InlineData("ChildBox", "generic")]
    public void ConcreteStructuralFieldTypeBindsFromFieldMetadata(
        string fieldName,
        string shape)
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectModelDefinition model =
            OccurrenceBindingModel(apply);
        ResourceEffectTypedDeclaration field =
            model.TypedDeclarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Pass
                {
                    Source: ResourceEffectLocation.StructuralField,
                });
        var original =
            (ResourceEffectLocation.StructuralField)
                ((ResourceEffect.Pass)field.Effect).Source;
        ResourceEffectMemberSelector selector = original.Selector;
        ResourceTypeExpression returnType = shape switch
        {
            "scalar" => CoreType("Int32"),
            "array" => new ResourceTypeExpression.SzArray(
                CoreType("Int32")),
            "generic" => new ResourceTypeExpression.Named(
                ((ResourceTypeExpression.Named)selector.DeclaringType)
                    .Assembly,
                "Ownership",
                [new ResourceTypeNameSegment("BindingBox", 1)],
                [
                    new ResourceTypeExpression.Variable(
                        new ResourceEffectGenericVariable(
                            ResourceEffectGenericVariableKind.Type,
                            0)),
                ]),
            _ => throw new InvalidOperationException(
                $"Unknown test shape '{shape}'."),
        };
        var concreteField = new ResourceEffectLocation.StructuralField(
            original.Root,
            new ResourceEffectMemberSelector(
                selector.DeclaringType,
                fieldName,
                selector.Kind,
                selector.IsStatic,
                selector.GenericArity,
                selector.CallingConvention,
                selector.HasThis,
                selector.ExplicitThis,
                selector.Parameters,
                returnType));
        ResourceEffect.Pass pass = (ResourceEffect.Pass)field.Effect;
        var replaced = new ResourceEffectTypedDeclaration(
            field.Target,
            new ResourceEffect.Pass(
                concreteField,
                pass.Target,
                pass.Identity),
            field.Provenances);
        var concrete = new ResourceEffectModelDefinition(
            model.Language,
            model.Identity,
            model.ResourceKinds,
            model.Declarations,
            [replaced]);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(AdmitModels(concrete), calls));
        ResolvedResourceEffectLocation.Field resolved =
            Assert.IsType<ResolvedResourceEffectLocation.Field>(
                Assert.Single(
                    complete.Snapshot.Effects).Binding.Locations.Single(
                        location =>
                            location
                                is ResolvedResourceEffectLocation.Field));

        Assert.Equal(fieldName, resolved.Definition.MetadataName);
        Assert.Equal(
            shape switch
            {
                "scalar" => TypeRefKind.Definition,
                "array" => TypeRefKind.SzArray,
                "generic" => TypeRefKind.GenericInstance,
                _ => throw new InvalidOperationException(),
            },
            resolved.Definition.FieldType.Type.Kind);
    }

    [Fact]
    public void NonDelegateCallbackContractIsUnsupported()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.non-delegate-callback",
                target,
                new ResourceEffect.Callback(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceBorrowScope.Callback(0),
                    ResourceCallbackExecution.Synchronous,
                    ResourceCallbackCardinality.ExactlyOnce)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(admission, calls));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .OccurrenceUnsupported
                && gap.OccurrenceGap?.Kind
                    == ResourceEffectOccurrenceBindingGapKind
                        .CallbackContract);
    }

    [Fact]
    public void CallbackContractRejectsOutOfRangeDelegateGenericParameter()
    {
        ImmutableArray<byte> image =
            MalformedBindingCallbackImage();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture(image);
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindMalformedCallback"
                    && result.Definition.Member.Name == "Apply");
        AssemblyReferenceIdentity identity = apply.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceEffectGenericVariable extraVariable =
            new(ResourceEffectGenericVariableKind.Type, 1);
        ResourceTypeExpression.Variable value = new(variable);
        ResourceTypeExpression.Variable extra = new(extraVariable);
        var callback = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingCallback", 1)],
            [value]);
        var owner = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOwnerWithExtra", 2)],
            [value, extra]);
        var outcome = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOutcome", 0)]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                owner,
                "Apply",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        value,
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        callback,
                        ResourceEffectRefKind.Value),
                ],
                outcome));
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.malformed-callback-generic",
                target,
                new ResourceEffect.Callback(
                    new ResourceEffectLocation.Parameter(1),
                    new ResourceBorrowScope.Callback(1),
                    ResourceCallbackExecution.Synchronous,
                    ResourceCallbackCardinality.ExactlyOnce)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(admission, calls));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .OccurrenceUnsupported
                && gap.OccurrenceGap?.Kind
                    == ResourceEffectOccurrenceBindingGapKind
                        .CallbackContract);
    }

    [Fact]
    public void StructuralFieldRejectsOutOfRangeDeclaringTypeGenericParameter()
    {
        ImmutableArray<byte> image =
            MalformedBindingFieldImage();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture(image);
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "InvokeMalformedField"
                    && result.Definition.Member.Name == "Use");
        AssemblyReferenceIdentity identity = apply.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        ResourceEffectGenericVariable valueVariable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceEffectGenericVariable extraVariable =
            new(ResourceEffectGenericVariableKind.Type, 1);
        ResourceTypeExpression.Variable value = new(valueVariable);
        ResourceTypeExpression.Variable extra = new(extraVariable);
        var box = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingBox", 1)],
            [value]);
        var owner = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOwnerWithExtra", 2)],
            [value, extra]);
        var outcome = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOutcome", 0)]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                owner,
                "Use",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        box,
                        ResourceEffectRefKind.Value),
                ],
                outcome));
        var field = new ResourceEffectMemberSelector(
            box,
            "Value",
            ResourceEffectMemberKind.Field,
            isStatic: false,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            [],
            extra);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.malformed-field-generic",
                target,
                new ResourceEffect.Pass(
                    new ResourceEffectLocation.StructuralField(
                        new ResourceEffectLocation.Parameter(0),
                        field),
                    new ResourceEffectLocation.Return(),
                    Identity: null)));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(admission, calls));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .OccurrenceUnsupported
                && gap.OccurrenceGap?.Kind
                    == ResourceEffectOccurrenceBindingGapKind
                        .StructuralField);
    }

    [Fact]
    public void OpenGenericStructuralFieldTypeBindsFromFieldMetadata()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name
                        == "BindOpenGenericReferences"
                    && result.Definition.Member.Name == "Apply");
        (
            ResourceEffectTargetSelector target,
            ResourceTypeExpression.Named owner,
            ResourceTypeExpression.Named box) =
                OpenGenericSignatureTarget(apply);
        var field = new ResourceEffectMemberSelector(
            owner,
            "Child",
            ResourceEffectMemberKind.Field,
            isStatic: false,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            [],
            box);
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.open-generic-field",
                target,
                new ResourceEffect.Pass(
                    new ResourceEffectLocation.StructuralField(
                        new ResourceEffectLocation.Receiver(),
                        field),
                    new ResourceEffectLocation.Return(),
                    Identity: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));

        Assert.Equal(3, complete.Snapshot.Effects.Length);
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindOpenGenericReferences");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindClosedGenericReferences");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindGuidGenericReferences");
    }

    [Fact]
    public void OpenGenericCallbackReturnTypeBindsFromMetadata()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name
                        == "BindOpenGenericReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OpenGenericSignatureTarget(apply).Target;
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.open-generic-callback",
                target,
                new ResourceEffect.Callback(
                    new ResourceEffectLocation.Parameter(1),
                    new ResourceBorrowScope.Callback(1),
                    ResourceCallbackExecution.Synchronous,
                    ResourceCallbackCardinality.ExactlyOnce)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));

        Assert.Equal(3, complete.Snapshot.Effects.Length);
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindOpenGenericReferences");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindClosedGenericReferences");
        Assert.Contains(
            complete.Snapshot.Effects,
            effect =>
                effect.DirectCall.Call.Caller.Name
                    == "BindGuidGenericReferences");
    }

    [Fact]
    public void MissingExactOutcomeTypeIsIncomplete()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectModelDefinition model =
            OccurrenceBindingModel(apply);
        ResourceEffectTypedDeclaration outcome =
            model.TypedDeclarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Outcome);
        ResourceEffect.Outcome declared =
            (ResourceEffect.Outcome)outcome.Effect;
        var replaced = new ResourceEffectTypedDeclaration(
            outcome.Target,
            new ResourceEffect.Outcome(
                declared.Identity,
                declared.Source,
                new ResourceEffectOutcomeTest.ExactType(
                    "Ownership.MissingBindingOutcome")),
            outcome.Provenances);
        var broken = new ResourceEffectModelDefinition(
            model.Language,
            model.Identity,
            model.ResourceKinds,
            model.Declarations,
            model.TypedDeclarations.Replace(outcome, replaced));

        ResourceEffectResolutionOutcome.Incomplete incomplete =
            Assert.IsType<ResourceEffectResolutionOutcome.Incomplete>(
                ResolveEffects(AdmitModels(broken), calls));

        Assert.Contains(
            incomplete.Gaps,
            gap =>
                gap.Kind
                    == ResourceEffectResolutionGapKind
                        .OccurrenceIncomplete
                && gap.OccurrenceGap?.Kind
                    == ResourceEffectOccurrenceBindingGapKind.OutcomeType);
    }

    [Fact]
    public void DisjointBooleanOutcomeCompletionsDoNotConflict()
    {
        ResourceEffectTargetSelector target =
            ArrayPoolResourceEffectModel.Definition()
                .TypedDeclarations[3]
                .Target;
        ResourceEffectAdmission admission = AdmitModels(
            Model(
                "example.boolean-true-release",
                target,
                new ResourceEffect.Release(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectCompletion.OutcomeCase(
                        new ResourceEffectLocation.Parameter(1),
                        new ResourceEffectOutcomeTest.Boolean(true)),
                    Kind: null,
                    Correspondence:
                        new ResourceEffectLocation.Receiver(),
                    Observation: null)),
            Model(
                "example.boolean-false-release",
                target,
                new ResourceEffect.Release(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectCompletion.OutcomeCase(
                        new ResourceEffectLocation.Parameter(1),
                        new ResourceEffectOutcomeTest.Boolean(false)),
                    Kind: null,
                    Correspondence: null,
                    Observation: null)));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, ResolveOwnershipFixture()));

        Assert.Contains(
            complete.Snapshot.Effects,
            effect => effect.Binding.Completion?.Outcome?.Test.Declaration
                is ResourceEffectOutcomeTest.Boolean { Value: true });
        Assert.Contains(
            complete.Snapshot.Effects,
            effect => effect.Binding.Completion?.Outcome?.Test.Declaration
                is ResourceEffectOutcomeTest.Boolean { Value: false });
    }

    [Fact]
    public void EnumOutcomeCompletionsUseUnderlyingConstants()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved getStatus =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindEnumOutcomeAliases"
                    && result.Definition.Member.Name == "GetBindingStatus");
        ResourceEffectTargetSelector target = EnumOutcomeTarget(getStatus);

        ResourceEffectResolutionOutcome.Conflict aliases =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                ResolveEffects(
                    AdmitModels(
                        EnumReleaseModel(
                            "example.enum-rejected",
                            target,
                            "Rejected",
                            observation: false),
                        EnumReleaseModel(
                            "example.enum-retry",
                            target,
                            "Retry",
                            observation: true)),
                    calls));
        Assert.NotEmpty(aliases.Conflicts);

        ResourceEffectResolutionOutcome.Complete distinct =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(
                    AdmitModels(
                        EnumReleaseModel(
                            "example.enum-rejected-distinct",
                            target,
                            "Rejected",
                            observation: false),
                        EnumReleaseModel(
                            "example.enum-accepted",
                            target,
                            "Accepted",
                            observation: true)),
                    calls));
        Assert.Contains(
            distinct.Snapshot.Effects,
            effect => effect.Binding.Completion?.Outcome?.Test.EnumConstant
                is { MetadataName: "Rejected", Value: 0 });
        Assert.Contains(
            distinct.Snapshot.Effects,
            effect => effect.Binding.Completion?.Outcome?.Test.EnumConstant
                is { MetadataName: "Accepted", Value: 1 });
    }

    [Fact]
    public void EqualSubstitutedResourceKindsBindOnce()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name
                        == "BindCollapsedResourceKinds"
                    && result.Definition.Member.Name == "Apply");
        AssemblyReferenceIdentity identity = apply.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        ResourceEffectGenericVariable typeVariable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceEffectGenericVariable methodVariable =
            new(ResourceEffectGenericVariableKind.Method, 0);
        ResourceTypeExpression.Variable type = new(typeVariable);
        ResourceTypeExpression.Variable method = new(methodVariable);
        ResourceTypeExpression.Named owner = new(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingGenericOwner", 1)],
            [type]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                owner,
                "Apply",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 1,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        type,
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        method,
                        ResourceEffectRefKind.Value),
                ],
                method));
        ResourceKindIdentity identityKind =
            new("example.collapsed-kind");
        ResourceKindReference typeKind =
            new(identityKind, [typeVariable]);
        ResourceKindReference methodKind =
            new(identityKind, [methodVariable]);
        var model = new ResourceEffectModelIdentity(
            "example.collapsed-kinds");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        identityKind,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectLocation.OperationSlot(
                                new ResourceEffectLocation.Parameter(0),
                                typeKind),
                            typeKind),
                        [Provenance(model.Value, 1)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Release(
                            new ResourceEffectLocation.OperationSlot(
                                new ResourceEffectLocation.Parameter(0),
                                typeKind),
                            new ResourceEffectCompletion.NormalReturn(),
                            methodKind,
                            Correspondence: null,
                            Observation: null),
                        [Provenance(model.Value, 2)]),
                ]));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));
        ResolvedResourceEffect effect =
            Assert.Single(
                complete.Snapshot.Effects,
                value => value.Effect is ResourceEffect.Release);
        ResolvedResourceEffectLocation.OperationSlot slot =
            Assert.IsType<ResolvedResourceEffectLocation.OperationSlot>(
                effect.Binding.Locations.Single(location =>
                    location is
                        ResolvedResourceEffectLocation.OperationSlot));

        Assert.Single(effect.ResourceKinds);
        Assert.Equal(Assert.Single(effect.ResourceKinds), slot.Kind);
    }

    [Fact]
    public void EmptyOperationSlotKindIntersectionDoesNotConflict()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceKindIdentity first = new("example.slot-first");
        ResourceKindIdentity second = new("example.slot-second");
        ResourceKindReference firstKind = new(first, [variable]);
        ResourceKindReference secondKind = new(second, [variable]);
        ResourceEffectLocation.OperationSlot slot = new(
            new ResourceEffectLocation.Parameter(0),
            firstKind);
        var model = new ResourceEffectModelIdentity(
            "example.empty-slot-intersection");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        first,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                    new ResourceKindDefinition(
                        second,
                        arity: 1,
                        [Provenance(model.Value, 1)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            slot,
                            firstKind),
                        [Provenance(model.Value, 2)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Move(
                            slot,
                            new ResourceEffectLocation.Return(),
                            new ResourceEffectCompletion.NormalReturn(),
                            secondKind),
                        [Provenance(model.Value, 3)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Release(
                            slot,
                            new ResourceEffectCompletion.NormalReturn(),
                            Kind: null,
                            Correspondence: null,
                            Observation: null),
                        [Provenance(model.Value, 4)]),
                ]));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, calls));
    }

    [Fact]
    public void EmptyOperationSlotKindIntersectionDoesNotConflictWithIndependence()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceKindIdentity first = new("example.independent-first");
        ResourceKindIdentity second = new("example.independent-second");
        ResourceKindReference firstKind = new(first, [variable]);
        ResourceKindReference secondKind = new(second, [variable]);
        ResourceEffectLocation.OperationSlot slot = new(
            new ResourceEffectLocation.Parameter(0),
            firstKind);
        var model = new ResourceEffectModelIdentity(
            "example.independent-empty-domain");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        first,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                    new ResourceKindDefinition(
                        second,
                        arity: 1,
                        [Provenance(model.Value, 1)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            slot,
                            firstKind),
                        [Provenance(model.Value, 2)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Move(
                            slot,
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectCompletion.NormalReturn(),
                            secondKind),
                        [Provenance(model.Value, 3)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Independent(
                            slot,
                            new ResourceEffectLocation.Parameter(0)),
                        [Provenance(model.Value, 4)]),
                ]));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, calls));
    }

    [Fact]
    public void EmptyBorrowKindDomainDoesNotConflictThroughLender()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceKindIdentity first = new("example.borrow-lender-first");
        ResourceKindIdentity second = new("example.borrow-lender-second");
        ResourceKindReference firstKind = new(first, [variable]);
        ResourceKindReference secondKind = new(second, [variable]);
        ResourceEffectLocation.OperationSlot slot = new(
            new ResourceEffectLocation.Parameter(0),
            firstKind);
        var model = new ResourceEffectModelIdentity(
            "example.borrow-lender-empty-domain");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        first,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                    new ResourceKindDefinition(
                        second,
                        arity: 1,
                        [Provenance(model.Value, 1)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            slot,
                            firstKind),
                        [Provenance(model.Value, 2)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Borrow(
                            slot,
                            new ResourceEffectLocation.Return(),
                            ResourceBorrowAccess.Read,
                            new ResourceBorrowScope.Call(),
                            secondKind,
                            new ResourceEffectLocation.Receiver(),
                            Materialization: null),
                        [Provenance(model.Value, 3)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Independent(
                            new ResourceEffectLocation.Receiver(),
                            new ResourceEffectLocation.Return()),
                        [Provenance(model.Value, 4)]),
                ]));

        Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResolveEffects(admission, calls));
    }

    [Fact]
    public void AcquireLenderConflictDoesNotUseLenderKindDomain()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceKindIdentity first = new("example.acquire-lender-first");
        ResourceKindIdentity second = new("example.acquire-lender-second");
        ResourceKindReference firstKind = new(first, [variable]);
        ResourceKindReference secondKind = new(second, [variable]);
        ResourceEffectLocation.OperationSlot slot = new(
            new ResourceEffectLocation.Parameter(0),
            firstKind);
        var model = new ResourceEffectModelIdentity(
            "example.acquire-lender-dependency");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        first,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                    new ResourceKindDefinition(
                        second,
                        arity: 1,
                        [Provenance(model.Value, 1)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Parameter(0),
                            slot,
                            firstKind),
                        [Provenance(model.Value, 2)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Acquire(
                            secondKind,
                            new ResourceEffectLocation.Return(),
                            new ResourceEffectCompletion.NormalReturn(),
                            Correspondence: null,
                            Lender: slot),
                        [Provenance(model.Value, 3)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Independent(
                            slot,
                            new ResourceEffectLocation.Return()),
                        [Provenance(model.Value, 4)]),
                ]));

        Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
            ResolveEffects(admission, calls));
    }

    [Fact]
    public void CompletionOperationSlotKindIsResolved()
    {
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved apply =
            Assert.Single(
                calls.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "BindOccurrenceReferences"
                    && result.Definition.Member.Name == "Apply");
        ResourceEffectTargetSelector target =
            OccurrenceBindingModel(apply).TypedDeclarations[0].Target;
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceKindIdentity identity =
            new("example.completion-slot-kind");
        ResourceKindReference kind = new(identity, [variable]);
        ResourceEffectLocation.OperationSlot slot = new(
            new ResourceEffectLocation.Return(),
            kind);
        var model = new ResourceEffectModelIdentity(
            "example.completion-slot-kind");
        ResourceEffectAdmission admission = AdmitModels(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new ResourceKindDefinition(
                        identity,
                        arity: 1,
                        [Provenance(model.Value, 0)]),
                ],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Consume(
                            new ResourceEffectLocation.Return(),
                            slot,
                            kind),
                        [Provenance(model.Value, 1)]),
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Release(
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectCompletion.OutcomeCase(
                                slot,
                                new ResourceEffectOutcomeTest.Null()),
                            Kind: null,
                            Correspondence: null,
                            Observation: null),
                        [Provenance(model.Value, 2)]),
                ]));

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResolveEffects(admission, calls));
        ResolvedResourceEffect release =
            Assert.Single(
                complete.Snapshot.Effects,
                effect => effect.Effect is ResourceEffect.Release);
        ResolvedResourceEffectLocation.OperationSlot resolvedSlot =
            Assert.IsType<ResolvedResourceEffectLocation.OperationSlot>(
                release.Binding.Completion!.Outcome!.Source);

        Assert.Equal(
            Assert.Single(release.ResourceKinds),
            resolvedSlot.Kind);
    }

    [Fact]
    public void ExactTypeCanonicalizationDistinguishesDefiningAssemblies()
    {
        TypeRef displayed =
            TypeRef.Definition("Collision", "Example", "Value");
        var firstAssembly = new AssemblyReferenceIdentity(
            "First",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var secondAssembly = new AssemblyReferenceIdentity(
            "Second",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var first = new ResolvedResourceEffectType(
            displayed,
            firstAssembly,
            definition: null,
            genericScope: null,
            element: null,
            arguments: []);
        var second = new ResolvedResourceEffectType(
            displayed,
            secondAssembly,
            definition: null,
            genericScope: null,
            element: null,
            arguments: []);

        Assert.Equal(
            first.Type.ToDisplayString(),
            second.Type.ToDisplayString());
        Assert.NotEqual(
            ResolvedResourceEffectCanonicalizer.Type(first),
            ResolvedResourceEffectCanonicalizer.Type(second));
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

    [Fact]
    public void ForeignInterfaceApplicationGenerationIsRejected()
    {
        ResourceEffectAdmission admission =
            ArrayPoolResourceEffectModel.Create();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolutionOutcome.Completed otherCalls =
            ResolveOwnershipFixture();
        ResourceEffectResolutionRequest valid =
            ResourceEffectResolver.CreateRequest(admission, calls);
        var foreignApplications =
            new ResourceEffectInterfaceApplicationIndex(
                otherCalls.Catalog,
                otherCalls.Generation,
                admission.Receipt,
                ResourceEffectResolver.CreatePopulationReceipt(otherCalls),
                ImmutableDictionary<AdmittedResourceEffectDeclaration,
                    ImmutableArray<ResourceEffectInterfaceApplication>>.Empty,
                globalGap: null);
        var foreign = new ResourceEffectResolutionRequest(
            admission,
            admission.Receipt,
            calls,
            valid.PopulationReceipt,
            foreignApplications);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResolveEffects(foreign));

        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .InterfaceApplicationGenerationMismatch,
            rejected.Kind);
    }

    static ResourceEffectTargetSelector RentTarget() =>
        ArrayPoolResourceEffectModel.Definition()
            .TypedDeclarations[1]
            .Target;

    static ResourceEffectAdmission OccurrenceBindingAdmission(
        DirectCallDefinitionResolution.Resolved apply) =>
        AdmitModels(OccurrenceBindingModel(apply));

    static ResourceEffectTargetSelector EnumOutcomeTarget(
        DirectCallDefinitionResolution.Resolved getStatus)
    {
        AssemblyReferenceIdentity identity = getStatus.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        var declaringType = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("Entry", 0)]);
        var status = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingStatus", 0)]);
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                "GetBindingStatus",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                parameters: [],
                status));
    }

    static ResourceEffectModelDefinition EnumReleaseModel(
        string identity,
        ResourceEffectTargetSelector target,
        string member,
        bool observation) =>
        Model(
            identity,
            target,
            new ResourceEffect.Release(
                new ResourceEffectLocation.Return(),
                new ResourceEffectCompletion.OutcomeCase(
                    new ResourceEffectLocation.Return(),
                    new ResourceEffectOutcomeTest.Enum(member)),
                Kind: null,
                Correspondence: observation
                    ? new ResourceEffectLocation.Return()
                    : null,
                Observation: null));

    static ResourceEffectModelDefinition OccurrenceBindingModel(
        DirectCallDefinitionResolution.Resolved apply)
    {
        AssemblyReferenceIdentity identity = apply.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceTypeExpression.Variable value = new(variable);
        ResourceTypeExpression.Named owner = new(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOwner", 1)],
            [value]);
        ResourceTypeExpression.Named callback = new(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingCallback", 1)],
            [value]);
        ResourceTypeExpression.Named outcome = new(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOutcome", 0)]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                owner,
                "Apply",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        value,
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        callback,
                        ResourceEffectRefKind.Value),
                ],
                outcome));
        var field = new ResourceEffectMemberSelector(
            owner,
            "Child",
            ResourceEffectMemberKind.Field,
            isStatic: false,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            [],
            value);
        ResourceKindIdentity child = new("example.child");
        ResourceKindReference childReference = new(child, [variable]);
        var model = new ResourceEffectModelIdentity(
            "example.occurrence-bindings");
        int ordinal = 0;

        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [
                new ResourceKindDefinition(
                    child,
                    arity: 1,
                    [Provenance(model.Value, ordinal++)]),
            ],
            [],
            [
                Declaration(
                    new ResourceEffect.Pass(
                        new ResourceEffectLocation.StructuralField(
                            new ResourceEffectLocation.Receiver(),
                            field),
                        new ResourceEffectLocation.Return(),
                        Identity: null)),
                Declaration(
                    new ResourceEffect.Callback(
                        new ResourceEffectLocation.Parameter(1),
                        new ResourceBorrowScope.Callback(1),
                        ResourceCallbackExecution.Synchronous,
                        ResourceCallbackCardinality.ExactlyOnce)),
                Declaration(
                    new ResourceEffect.Pass(
                        new ResourceEffectLocation.CallbackParameter(1, 0),
                        new ResourceEffectLocation.CallbackReturn(1),
                        ResourcePassIdentity.Preserve)),
                Declaration(
                    new ResourceEffect.Outcome(
                        new ResourceEffectLocalIdentity("rejected"),
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectOutcomeTest.ExactType(
                            "Ownership.BindingRejectedOutcome"))),
                Declaration(
                    new ResourceEffect.Consume(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectLocation.OperationSlot(
                            new ResourceEffectLocation.Parameter(0),
                            childReference),
                        childReference)),
                Declaration(
                    new ResourceEffect.Operation(
                        ResourceOperationBoundary.Ordinary,
                        ResourceOperationThrows.Possible,
                        new ResourceEffectGuard.ExactRuntimeType(
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectSignatureLocation.Parameter(
                                0)))),
            ]);

        ResourceEffectTypedDeclaration Declaration(
            ResourceEffect effect) =>
            new(
                target,
                effect,
                [Provenance(model.Value, ordinal++)]);
    }

    static (
        ResourceEffectTargetSelector Target,
        ResourceTypeExpression.Named Owner,
        ResourceTypeExpression.Named Box)
        OpenGenericSignatureTarget(
            DirectCallDefinitionResolution.Resolved apply)
    {
        AssemblyReferenceIdentity identity = apply.Definition.Assembly;
        var assembly = new ResourceAssemblySelector(
            identity.Name,
            identity.PublicKeyToken,
            ResourceAssemblyVersionPolicy.Exact(identity.Version!));
        ResourceEffectGenericVariable variable =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceTypeExpression.Variable value = new(variable);
        var box = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingBox", 1)],
            [value]);
        var callback = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingBoxCallback", 1)],
            [value]);
        var owner = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOpenOwner", 1)],
            [value]);
        var outcome = new ResourceTypeExpression.Named(
            assembly,
            "Ownership",
            [new ResourceTypeNameSegment("BindingOutcome", 0)]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                owner,
                "Apply",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        value,
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        callback,
                        ResourceEffectRefKind.Value),
                ],
                outcome));
        return (target, owner, box);
    }

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

    static ResourceEffectTargetSelector InterfaceTarget(
        SyntheticParticipant participant)
    {
        AssemblyReferenceIdentity identity =
            participant.Participant.Assembly.Identity;
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                new ResourceTypeExpression.Named(
                    new ResourceAssemblySelector(
                        identity.Name,
                        identity.PublicKeyToken,
                        ResourceAssemblyVersionPolicy.Exact(
                            identity.Version!)),
                    "N",
                    [new ResourceTypeNameSegment("IContract", 0)]),
                "Target",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                parameters: [],
                CoreType("Void")));
    }

    static ResourceEffectTargetSelector ClosedInterfaceTarget(
        SyntheticParticipant participant,
        string metadataName,
        ResourceTypeExpression argument)
    {
        AssemblyReferenceIdentity identity =
            participant.Participant.Assembly.Identity;
        var declaringType = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                identity.Name,
                identity.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(
                    identity.Version!)),
            "N",
            [new ResourceTypeNameSegment(metadataName, 1)],
            [argument]);
        return new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                "Target",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        argument,
                        ResourceEffectRefKind.Value),
                ],
                CoreType("Void")));
    }

    static ResourceEffectModelDefinition MethodGenericInterfaceModel(
        SyntheticParticipant participant)
    {
        AssemblyReferenceIdentity assembly =
            participant.Participant.Assembly.Identity;
        var methodVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Method,
            0);
        var methodType = new ResourceTypeExpression.Variable(
            methodVariable);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                new ResourceTypeExpression.Named(
                    new ResourceAssemblySelector(
                        assembly.Name,
                        assembly.PublicKeyToken,
                        ResourceAssemblyVersionPolicy.Exact(
                            assembly.Version!)),
                    "N",
                    [new ResourceTypeNameSegment("IContract", 0)]),
                "Target",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 1,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        methodType,
                        ResourceEffectRefKind.Value),
                ],
                CoreType("Void")));
        var identity = new ResourceEffectModelIdentity(
            "example.method-interface-application");
        var kindIdentity = new ResourceKindIdentity(
            "example.method-interface-resource");
        ResourceKindReference kind = new(
            kindIdentity,
            [methodVariable]);
        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            identity,
            [
                new ResourceKindDefinition(
                    kindIdentity,
                    arity: 1,
                    [Provenance(identity.Value, 0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    target,
                    new ResourceEffect.Acquire(
                        kind,
                        new ResourceEffectLocation.Receiver(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    [Provenance(identity.Value, 1)]),
            ]);
    }

    static ResourceEffectTargetSelector GenericTargetFor(
        DirectCallDefinitionOccurrence definition)
    {
        MemberRef member = definition.Member;
        string metadataName = member.DeclaringType.Name;
        int aritySeparator = metadataName.LastIndexOf('`');
        if (aritySeparator >= 0)
            metadataName = metadataName[..aritySeparator];
        var variable = new ResourceTypeExpression.Variable(
            new ResourceEffectGenericVariable(
                ResourceEffectGenericVariableKind.Type,
                0));
        var selectorType = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                definition.Assembly.Name,
                definition.Assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(
                    definition.Assembly.Version!)),
            member.DeclaringType.Namespace,
            [new ResourceTypeNameSegment(
                metadataName,
                1)],
            [variable]);
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
                parameters:
                [
                    new ResourceEffectParameterSelector(
                        variable,
                        ResourceEffectRefKind.Value),
                ],
                CoreType("Void")));
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        ResolveOwnershipFixtureWithDiagnostics(int count)
    {
        DirectCallDefinitionResolutionOutcome.Completed baseline =
            ResolveOwnershipFixture();
        CatalogCallGraphParticipant original =
            Assert.Single(baseline.Population);
        MethodIdentity method = original.CallGraph.Methods[0];
        ImmutableArray<AnalysisDiagnostic> diagnostics =
        [
            .. Enumerable.Range(0, count).Select(index =>
                new AnalysisDiagnostic(
                    method.MetadataToken + index,
                    method.Name,
                    "Synthetic body failure")),
        ];
        LibraryBodyIndex incompleteIndex = LibraryBodyIndex.FromEvidence(
            original.CallGraph.Methods,
            unsafeEvidence: [],
            diagnostics: diagnostics,
            directCalls: original.CallGraph.DirectCalls,
            moduleIdentity: original.CallGraph.ModuleIdentity);
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
        params ResourceEffectModelDefinition[] models)
    {
        ResourceEffectAdmissionOutcome outcome =
            ResourceEffectAdmissionBuilder.Admit(models);
        if (outcome is ResourceEffectAdmissionOutcome.Admitted admitted)
            return admitted.Admission;

        ResourceEffectAdmissionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome);
        Assert.Fail(
            string.Join(
                Environment.NewLine,
                rejected.Diagnostics.Select(diagnostic =>
                    diagnostic.Diagnostic.Message.ToString())));
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    static ImmutableArray<byte> MalformedBindingCallbackImage()
    {
        byte[] image = File.ReadAllBytes(OwnershipFixturePath);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle callback = reader.TypeDefinitions.Single(
            handle =>
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                return reader.StringComparer.Equals(
                        type.Namespace,
                        "Ownership")
                    && reader.StringComparer.Equals(
                        type.Name,
                        "BindingCallback`1");
            });
        MethodDefinitionHandle invoke = reader
            .GetTypeDefinition(callback)
            .GetMethods()
            .Single(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "Invoke"));
        BlobHandle signature =
            reader.GetMethodDefinition(invoke).Signature;
        int blob = MetadataStreamOffset(
                image,
                pe.PEHeaders.MetadataStartOffset,
                "#Blob")
            + MetadataTokens.GetHeapOffset(signature);
        Assert.Equal(6, image[blob]);
        Assert.True(
            image.AsSpan(blob + 1, 6)
                .SequenceEqual(
                    new byte[]
                    {
                        0x20,
                        0x01,
                        0x13,
                        0x00,
                        0x13,
                        0x00,
                    }));
        image[blob + 4] = 0x01;
        image[blob + 6] = 0x01;
        return ImmutableArray.Create(image);
    }

    static ImmutableArray<byte> MalformedBindingFieldImage()
    {
        byte[] image = File.ReadAllBytes(OwnershipFixturePath);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle owner = reader.TypeDefinitions.Single(
            handle =>
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                return reader.StringComparer.Equals(
                        type.Namespace,
                        "Ownership")
                    && reader.StringComparer.Equals(
                        type.Name,
                        "BindingBox`1");
            });
        FieldDefinitionHandle child = reader
            .GetTypeDefinition(owner)
            .GetFields()
            .Single(handle => reader.StringComparer.Equals(
                reader.GetFieldDefinition(handle).Name,
                "Value"));
        BlobHandle signature =
            reader.GetFieldDefinition(child).Signature;
        int blob = MetadataStreamOffset(
                image,
                pe.PEHeaders.MetadataStartOffset,
                "#Blob")
            + MetadataTokens.GetHeapOffset(signature);
        Assert.Equal(3, image[blob]);
        Assert.True(
            image.AsSpan(blob + 1, 3)
                .SequenceEqual(
                    new byte[] { 0x06, 0x13, 0x00 }));
        image[blob + 3] = 0x01;
        return ImmutableArray.Create(image);
    }

    static int MetadataStreamOffset(
        byte[] image,
        int metadataRoot,
        string streamName)
    {
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataRoot + 12, 4));
        int position = metadataRoot + 16
            + ((versionLength + 3) & ~3);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(position + 2, 2));
        position += 4;
        for (int i = 0; i < streamCount; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(position, 4));
            position += 8;
            int nameStart = position;
            while (image[position] != 0)
                position++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                position - nameStart);
            position = (position + 4) & ~3;
            if (name == streamName)
                return metadataRoot + offset;
        }

        throw new BadImageFormatException(
            $"Metadata stream {streamName} was not found.");
    }

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
