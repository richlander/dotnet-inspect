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
                    Guard: null)));

        ResourceEffectResolutionOutcome.Conflict conflict =
            Assert.IsType<ResourceEffectResolutionOutcome.Conflict>(
                Resolve(
                    admission,
                    new ResourceEffectResolutionLimits(
                        maxCompatibilityComparisons: 1)));
        Assert.NotEmpty(conflict.Conflicts);
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
