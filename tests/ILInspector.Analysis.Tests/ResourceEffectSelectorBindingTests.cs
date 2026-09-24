using System.Collections.Immutable;
using System.Reflection;
using InertText;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    [Fact]
    public void ArrayPoolRentBindsExactTypeAndResourceKind()
    {
        var typeVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceKindReference kind = new(
            new ResourceKindIdentity("dotnet.array-pool.buffer"),
            [typeVariable]);
        AdmittedResourceEffectDeclaration declaration = Admit(
            ArrayPoolSelector(
                "Rent",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                [
                    new ResourceEffectParameterSelector(
                        CoreType("Int32"),
                        ResourceEffectRefKind.Value),
                ],
                new ResourceTypeExpression.SzArray(
                    new ResourceTypeExpression.Variable(
                        typeVariable))),
            new ResourceEffect.Acquire(
                kind,
                new ResourceEffectLocation.Return(),
                new ResourceEffectCompletion.NormalReturn(),
                Correspondence: null,
                Lender: null),
            kind);

        ResourceEffectSelectorBinding.Resolved[] bindings =
        [
            .. ResolveOwnershipFixture().Results
                .Select(result =>
                    ResourceEffectSelectorBinder.Bind(
                        declaration,
                        result))
                .OfType<ResourceEffectSelectorBinding.Resolved>(),
        ];

        ResourceEffectSelectorBinding.Resolved binding =
            Assert.Single(
                bindings,
                candidate =>
                    candidate.DirectCall.Call.Caller.Name
                        == "RentAndReturnDirectly");
        Assert.Equal("Rent", binding.DirectCall.Call.Callee.Name);
        ResolvedResourceEffectGenericBinding generic =
            Assert.Single(binding.GenericBindings);
        Assert.Equal(typeVariable, generic.Variable);
        Assert.Equal("Byte", generic.Value.Type.Name);
        ResolvedResourceKindReference resource =
            Assert.Single(binding.ResourceKinds);
        Assert.Equal(kind.Identity, resource.Identity);
        Assert.Same(
            generic.Value,
            Assert.Single(resource.Arguments));
    }

    [Fact]
    public void CoreLibraryFacadeAssemblyNameIsCaseInsensitive()
    {
        var typeVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceKindReference kind = new(
            new ResourceKindIdentity("dotnet.array-pool.buffer"),
            [typeVariable]);
        AdmittedResourceEffectDeclaration declaration = Admit(
            ArrayPoolSelector(
                "Rent",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                [
                    new ResourceEffectParameterSelector(
                        CoreType("Int32", "system.runtime"),
                        ResourceEffectRefKind.Value),
                ],
                new ResourceTypeExpression.SzArray(
                    new ResourceTypeExpression.Variable(
                        typeVariable))),
            new ResourceEffect.Acquire(
                kind,
                new ResourceEffectLocation.Return(),
                new ResourceEffectCompletion.NormalReturn(),
                Correspondence: null,
                Lender: null),
            kind);
        DirectCallDefinitionResolution[] calls =
        [
            .. ResolveOwnershipFixture().Results
                .Where(result => result.Call.Callee.Name == "Rent"),
        ];

        Assert.NotEmpty(calls);
        Assert.All(
            calls,
            result => Assert.IsType<ResourceEffectSelectorBinding.Resolved>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    result)));
    }

    [Fact]
    public void ArrayPoolReturnRejectsSameNamedUserMethod()
    {
        var typeVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Type,
            0);
        ResourceKindReference kind = new(
            new ResourceKindIdentity("dotnet.array-pool.buffer"),
            [typeVariable]);
        AdmittedResourceEffectDeclaration declaration = Admit(
            ArrayPoolSelector(
                "Return",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                [
                    new ResourceEffectParameterSelector(
                        new ResourceTypeExpression.SzArray(
                            new ResourceTypeExpression.Variable(
                                typeVariable)),
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        CoreType("Boolean"),
                        ResourceEffectRefKind.Value),
                ],
                CoreType("Void")),
            new ResourceEffect.Release(
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectCompletion.NormalReturn(),
                kind,
                Correspondence: null,
                Observation: null),
            kind);
        DirectCallDefinitionResolution.Resolved local =
            Assert.Single(
                ResolveOwnershipFixture().Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Callee.Name == "Return"
                    && result.Call.Callee.DeclaringType.Name.Contains(
                        "OwnershipSink",
                        StringComparison.Ordinal));

        Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
            ResourceEffectSelectorBinder.Bind(
                declaration,
                local));
    }

    [Fact]
    public void MethodSpecArgumentBindsWithoutSignatureUse()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
        });
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        var methodVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Method,
            0);
        ResourceKindReference kind = new(
            new ResourceKindIdentity("example.method-resource"),
            [methodVariable]);
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                genericArity: 1),
            new ResourceEffect.Acquire(
                kind,
                new ResourceEffectLocation.Return(),
                new ResourceEffectCompletion.NormalReturn(),
                Correspondence: null,
                Lender: null),
            kind);

        ResourceEffectSelectorBinding.Resolved binding =
            Assert.IsType<ResourceEffectSelectorBinding.Resolved>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    call));

        ResolvedResourceEffectGenericBinding generic =
            Assert.Single(binding.GenericBindings);
        Assert.Equal(methodVariable, generic.Variable);
        Assert.Equal("Int32", generic.Value.Type.Name);
        Assert.Same(
            generic.Value,
            Assert.Single(
                Assert.Single(binding.ResourceKinds).Arguments));
    }

    [Fact]
    public void OpenMethodSpecArgumentRetainsCallerScope()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature = [0x0A, 0x01, 0x1E, 0x00],
            CallerMethodGenericParameterRows = 1,
        });
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                genericArity: 1),
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                Guard: null));

        ResourceEffectSelectorBinding.Resolved binding =
            Assert.IsType<ResourceEffectSelectorBinding.Resolved>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    call));

        ResolvedResourceEffectType value =
            Assert.Single(binding.GenericBindings).Value;
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            value.Type.Kind);
        Assert.Equal(
            ResourceEffectGenericVariableKind.Method,
            value.GenericScope?.Kind);
        Assert.Equal(
            call.Call.EvidenceMethod.MetadataToken,
            value.GenericScope?.Owner.MethodToken);
    }

    [Fact]
    public void DirectCallTypeProjectionRetainsForwardingEvidence()
    {
        DirectCallTypeResolutionProjection[] forwarded =
        [
            .. ResolveOwnershipFixture().Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .SelectMany(result =>
                    result.TypeResolutions.Projections)
                .Where(projection =>
                    projection.Outcome is
                    {
                        Hops.IsEmpty: false,
                    }),
        ];

        Assert.NotEmpty(forwarded);
        Assert.All(
            forwarded,
            projection =>
                Assert.NotNull(
                    projection.Outcome?
                        .TerminalAssemblyIdentity));
    }

    [Fact]
    public void RepeatedTypeVariableMismatchIsUnmatched()
    {
        var typeVariable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Type,
            0);
        AdmittedResourceEffectDeclaration declaration = Admit(
            ArrayPoolSelector(
                "Return",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                [
                    new ResourceEffectParameterSelector(
                        new ResourceTypeExpression.SzArray(
                            new ResourceTypeExpression.Variable(
                                typeVariable)),
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        new ResourceTypeExpression.Variable(
                            typeVariable),
                        ResourceEffectRefKind.Value),
                ],
                CoreType("Void")),
            new ResourceEffect.Operation(
                ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible,
                Guard: null));
        DirectCallDefinitionResolution.Resolved[] frameworkReturns =
        [
            .. ResolveOwnershipFixture().Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .Where(result =>
                    result.Definition.Assembly.Name
                        == typeof(System.Buffers.ArrayPool<>)
                            .Assembly.GetName().Name
                    && result.Call.Callee.Name == "Return"),
        ];

        Assert.NotEmpty(frameworkReturns);
        Assert.All(
            frameworkReturns,
            frameworkReturn =>
                Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
                    ResourceEffectSelectorBinder.Bind(
                        declaration,
                        frameworkReturn)));
    }

    [Fact]
    public void UnknownByRefDirectionIsUnsupportedNotUnmatched()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x00, 0x01, 0x01, 0x10, 0x08],
            CallArgumentKinds = [SyntheticStackValue.NativeInt],
        });
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        ResourceEffectMemberSelector RefSelector(
            ResourceEffectRefKind refKind) =>
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                parameters:
                [
                    new ResourceEffectParameterSelector(
                        CoreType("Int32"),
                        refKind),
                ]);

        ResourceEffectSelectorBinding.Unsupported unsupported =
            Assert.IsType<ResourceEffectSelectorBinding.Unsupported>(
                ResourceEffectSelectorBinder.Bind(
                    Admit(
                        RefSelector(ResourceEffectRefKind.Ref),
                        OperationEffect()),
                    call));
        Assert.Equal(
            ResourceEffectSelectorBindingGapKind
                .UnsupportedSignature,
            unsupported.Gap.Kind);
        Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
            ResourceEffectSelectorBinder.Bind(
                Admit(
                    RefSelector(ResourceEffectRefKind.Value),
                    OperationEffect()),
                call));
    }

    [Fact]
    public void DirectCallFailuresRemainTypedWhenSelectorCouldMatch()
    {
        DirectCallDefinitionResolution ambiguous =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    TargetCount = 2,
                    CallViaMemberReference = true,
                })).Results);
        DirectCallDefinitionResolution unsupported =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    CallKind = CallKind.NewObject,
                })).Results);
        DirectCallDefinitionResolution incomplete =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    MissingDeclaringType = true,
                })).Results);

        ResourceEffectSelectorBinding.Ambiguous boundAmbiguous =
            Assert.IsType<ResourceEffectSelectorBinding.Ambiguous>(
                ResourceEffectSelectorBinder.Bind(
                    Admit(
                        SyntheticSelector(
                            "SyntheticDirectCalls",
                            "Target"),
                        OperationEffect()),
                    ambiguous));
        ResourceEffectSelectorBinding.Unsupported boundUnsupported =
            Assert.IsType<ResourceEffectSelectorBinding.Unsupported>(
                ResourceEffectSelectorBinder.Bind(
                    Admit(
                        SyntheticSelector(
                            "SyntheticDirectCalls",
                            "Target"),
                        OperationEffect()),
                    unsupported));
        ResourceEffectSelectorBinding.Incomplete boundIncomplete =
            Assert.IsType<ResourceEffectSelectorBinding.Incomplete>(
                ResourceEffectSelectorBinder.Bind(
                    Admit(
                        SyntheticSelector(
                            "Missing",
                            "Target"),
                        OperationEffect()),
                    incomplete));

        Assert.Same(
            Assert.IsType<DirectCallDefinitionResolution.Ambiguous>(
                ambiguous).Gap,
            boundAmbiguous.Gap.DirectCallGap);
        Assert.Same(
            Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
                unsupported).Gap,
            boundUnsupported.Gap.DirectCallGap);
        Assert.Same(
            Assert.IsType<DirectCallDefinitionResolution.Incomplete>(
                incomplete).Gap,
            boundIncomplete.Gap.DirectCallGap);
    }

    [Fact]
    public void MissingOverloadIsUnmatchedBesideVisibleUnsupportedCall()
    {
        DirectCallDefinitionResolution missing =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    CallName = "Missing",
                    CallViaMemberReference = true,
                })).Results);
        DirectCallDefinitionResolution unsupported =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    CallKind = CallKind.NewObject,
                })).Results);
        AdmittedResourceEffectDeclaration missingDeclaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Missing"),
            OperationEffect());
        AdmittedResourceEffectDeclaration targetDeclaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target"),
            OperationEffect());

        Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
            ResourceEffectSelectorBinder.Bind(
                missingDeclaration,
                missing));
        Assert.IsType<ResourceEffectSelectorBinding.Unsupported>(
            ResourceEffectSelectorBinder.Bind(
                targetDeclaration,
                unsupported));
    }

    [Fact]
    public void UnreadableMemberSignatureRemainsUnsupported()
    {
        byte[] unreadable = new byte[
            SignatureBlobGuard.DefaultMaxDepth + 4];
        unreadable[0] = 0x00;
        unreadable[1] = 0x00;
        unreadable.AsSpan(
                2,
                SignatureBlobGuard.DefaultMaxDepth + 1)
            .Fill(0x1D);
        unreadable[^1] = 0x1C;
        DirectCallDefinitionResolution unsupported =
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    TargetSignature = unreadable,
                    TargetReturnValue =
                        SyntheticStackValue.Null,
                    PopCallReturn = true,
                })).Results);

        Assert.IsType<ResourceEffectSelectorBinding.Unsupported>(
            ResourceEffectSelectorBinder.Bind(
                Admit(
                    SyntheticSelector(
                        "SyntheticDirectCalls",
                        "Target"),
                    OperationEffect()),
                unsupported));
    }

    [Fact]
    public void ExactAssemblyVersionMismatchIsUnmatched()
    {
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(
                    Resolve(CreateSynthetic(new())).Results));
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                assemblyVersion: new Version(2, 0, 0, 0)),
            OperationEffect());

        Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
            ResourceEffectSelectorBinder.Bind(
                declaration,
                call));
    }

    [Fact]
    public void TypeResolutionFailureIsIncompleteWithExactCause()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x00, 0x01, 0x01, 0x12, 0x05],
            CallArgumentKinds = [SyntheticStackValue.Null],
        });
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                parameters:
                [
                    new ResourceEffectParameterSelector(
                        RuntimeCoreType("Object"),
                        ResourceEffectRefKind.Value),
                ]),
            OperationEffect());

        ResourceEffectSelectorBinding.Incomplete binding =
            Assert.IsType<ResourceEffectSelectorBinding.Incomplete>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    call));

        Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            binding.Gap.TypeResolution);
    }

    [Fact]
    public void CoreLibraryFacadeDoesNotHideTypeResolutionFailure()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x00, 0x01, 0x01, 0x12, 0x05],
            CallArgumentKinds = [SyntheticStackValue.Null],
        });
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SyntheticDirectCalls",
                "Target",
                parameters:
                [
                    new ResourceEffectParameterSelector(
                        CoreType("Object"),
                        ResourceEffectRefKind.Value),
                ]),
            OperationEffect());

        ResourceEffectSelectorBinding.Incomplete binding =
            Assert.IsType<ResourceEffectSelectorBinding.Incomplete>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    call));

        Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            binding.Gap.TypeResolution);
    }

    [Fact]
    public void ForwarderFailuresClassifyAsIncomplete()
    {
        Assert.Equal(
            DirectCallTypeResolutionKind.Incomplete,
            DirectCallDefinitionResolver
                .ClassifyRejectedTypeResolution(
                    new TypeResolutionFailure
                        .ForwarderCycle()));
    }

    [Fact]
    public void DuplicateArtifactTypeProjectionRetainsJoinEvidence()
    {
        SyntheticParticipant first = CreateSynthetic(new()
        {
            AssemblyName = "SelectorDuplicateArtifact",
        });
        ResolvedAssemblyReference secondAssembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(
                    first.Image,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "second selector-binding acquisition"))!;
        LibraryBodyIndex secondIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "SelectorDuplicateArtifact-second.dll",
                ImmutableArray.CreateRange(first.Image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        var second = new CatalogCallGraphParticipant(
            secondIndex,
            secondAssembly);
        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                    DirectCallDefinitionResolver.Resolve(
                        new ExactPolicy(
                        [
                            first.Participant.Assembly,
                            secondAssembly,
                            CoreLibraryAssembly,
                        ]),
                        [first.Participant, second],
                        cancellationToken:
                            TestContext.Current.CancellationToken));

        DefinitionJoinTokenProjection.Issued[] projections =
        [
            .. completed.Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .SelectMany(result =>
                    result.TypeResolutions.Projections)
                .Select(projection =>
                    projection.DefinitionProjection)
                .OfType<
                    DefinitionJoinTokenProjection.Issued>()
                .Where(projection =>
                    projection.Token.Kind
                        == DefinitionJoinKind
                            .IndeterminateDuplicateArtifact),
        ];

        Assert.NotEmpty(projections);
        Assert.All(
            projections,
            projection =>
                Assert.NotNull(projection.Token.Evidence));
    }

    [Fact]
    public void DuplicateArtifactBindingGapRetainsJoinEvidence()
    {
        SyntheticParticipant first = CreateSynthetic(new()
        {
            AssemblyName = "SelectorDuplicateGenericArtifact",
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            AddGenericArgumentType = true,
            MethodSpecSignature =
            [
                0x0A,
                0x01,
                0x15,
                0x12,
                0x0C,
                0x01,
                0x08,
            ],
        });
        ResolvedAssemblyReference secondAssembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(
                    first.Image,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "second selector generic acquisition"))!;
        LibraryBodyIndex secondIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "SelectorDuplicateGenericArtifact-second.dll",
                ImmutableArray.CreateRange(first.Image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        var second = new CatalogCallGraphParticipant(
            secondIndex,
            secondAssembly);
        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                    DirectCallDefinitionResolver.Resolve(
                        new ExactPolicy(
                        [
                            first.Participant.Assembly,
                            secondAssembly,
                            CoreLibraryAssembly,
                        ]),
                        [first.Participant, second],
                        cancellationToken:
                            TestContext.Current.CancellationToken));
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                completed.Results[0]);
        AdmittedResourceEffectDeclaration declaration = Admit(
            SyntheticSelector(
                "SelectorDuplicateGenericArtifact",
                "Target",
                genericArity: 1),
            OperationEffect());

        ResourceEffectSelectorBinding.Incomplete binding =
            Assert.IsType<ResourceEffectSelectorBinding.Incomplete>(
                ResourceEffectSelectorBinder.Bind(
                    declaration,
                    call));
        DefinitionJoinTokenProjection.Issued projection =
            Assert.IsType<DefinitionJoinTokenProjection.Issued>(
                binding.Gap.DefinitionProjection);

        Assert.Equal(
            DefinitionJoinKind.IndeterminateDuplicateArtifact,
            projection.Token.Kind);
        Assert.NotNull(projection.Token.Evidence);
        Assert.NotNull(binding.Gap.TypeResolution);
    }

    [Fact]
    public void CoreLibraryFacadeRequiresTrustedAssemblyIdentity()
    {
        DirectCallDefinitionResolution.Resolved call =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(
                    Resolve(CreateSynthetic(new()
                    {
                        AssemblyName = "System.Runtime",
                    })).Results));
        ResourceEffectMemberSelector selector =
            new(
                new ResourceTypeExpression.Named(
                    new ResourceAssemblySelector(
                        "System.Runtime",
                        "b03f5f7f11d50a3a",
                        ResourceAssemblyVersionPolicy.Any,
                        allowCoreLibraryFacade: true),
                    "N",
                    [new ResourceTypeNameSegment("Owner", 0)]),
                "Target",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                parameters: [],
                CoreType("Void"));

        Assert.IsType<ResourceEffectSelectorBinding.Unmatched>(
            ResourceEffectSelectorBinder.Bind(
                Admit(selector, OperationEffect()),
                call));
    }

    static ResourceEffectMemberSelector ArrayPoolSelector(
        string metadataName,
        ResourceEffectMemberKind kind,
        bool isStatic,
        ImmutableArray<ResourceEffectParameterSelector> parameters,
        ResourceTypeExpression returnType)
    {
        var variable = new ResourceEffectGenericVariable(
            ResourceEffectGenericVariableKind.Type,
            0);
        AssemblyName assembly =
            typeof(System.Buffers.ArrayPool<>).Assembly.GetName();
        byte[]? tokenBytes = assembly.GetPublicKeyToken();
        string? token = tokenBytes is { Length: > 0 }
            ? Convert.ToHexString(tokenBytes).ToLowerInvariant()
            : null;
        var declaringType = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                assembly.Name!,
                token,
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System.Buffers",
            [new ResourceTypeNameSegment("ArrayPool", 1)],
            [new ResourceTypeExpression.Variable(variable)]);
        return new ResourceEffectMemberSelector(
            declaringType,
            metadataName,
            kind,
            isStatic,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: !isStatic,
            explicitThis: false,
            parameters,
            returnType);
    }

    static ResourceEffectMemberSelector SyntheticSelector(
        string assemblyName,
        string metadataName,
        int genericArity = 0,
        ImmutableArray<ResourceEffectParameterSelector> parameters =
            default,
        Version? assemblyVersion = null)
    {
        if (parameters.IsDefault)
            parameters = [];
        return new ResourceEffectMemberSelector(
            new ResourceTypeExpression.Named(
                new ResourceAssemblySelector(
                    assemblyName,
                    publicKeyToken: null,
                    ResourceAssemblyVersionPolicy.Exact(
                        assemblyVersion
                            ?? new Version(1, 0, 0, 0))),
                "N",
                [new ResourceTypeNameSegment("Owner", 0)]),
            metadataName,
            ResourceEffectMemberKind.Method,
            isStatic: true,
            genericArity,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            parameters,
            CoreType("Void"));
    }

    static ResourceTypeExpression.Named CoreType(
        string name,
        string assemblyName = "System.Runtime") =>
        new(
            new ResourceAssemblySelector(
                assemblyName,
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System",
            [new ResourceTypeNameSegment(name, 0)]);

    static ResourceTypeExpression.Named RuntimeCoreType(
        string name)
    {
        AssemblyName assembly = typeof(object).Assembly.GetName();
        byte[] tokenBytes = assembly.GetPublicKeyToken()!;
        return new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                assembly.Name!,
                Convert.ToHexString(tokenBytes).ToLowerInvariant(),
                ResourceAssemblyVersionPolicy.Exact(
                    assembly.Version!)),
            "System",
            [new ResourceTypeNameSegment(name, 0)]);
    }

    static ResourceEffect OperationEffect() =>
        new ResourceEffect.Operation(
            ResourceOperationBoundary.Ordinary,
            ResourceOperationThrows.Possible,
            Guard: null);

    static AdmittedResourceEffectDeclaration Admit(
        ResourceEffectMemberSelector selector,
        ResourceEffect effect,
        ResourceKindReference? kind = null)
    {
        ResourceEffectModelIdentity model =
            new("example.selector-binding");
        var provenance = new ResourceDeclarationProvenance(
            model,
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(
                TextPolicy.Field,
                "selector-binding-test"),
            0);
        ImmutableArray<ResourceKindDefinition> kinds =
            kind is null
                ? []
                :
                [
                    new ResourceKindDefinition(
                        kind.Identity,
                        kind.Arguments.Length,
                        [provenance]),
                ];
        var definition = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            model,
            kinds,
            declarations: [],
            [
                new ResourceEffectTypedDeclaration(
                    new ResourceEffectTargetSelector.Member(
                        selector),
                    effect,
                    [provenance]),
            ]);
        ResourceEffectAdmission admission =
            Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
                ResourceEffectAdmissionBuilder.Admit(
                    [definition])).Admission;
        return Assert.Single(
            Assert.Single(admission.Models).Declarations);
    }
}
