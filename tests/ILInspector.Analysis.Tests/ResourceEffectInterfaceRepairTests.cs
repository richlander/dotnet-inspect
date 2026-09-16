using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerGenericTypeArgumentStaysOutsideMethodSlotFrame(bool decoy)
    {
        SyntheticParticipant participant = CreateInterfaceParticipant(
            generic: true, methodGeneric: true, includeInterfaceCall: false,
            callerGenericTypeArgument: true, addSwappedGenericDecoy: decoy);
        AssemblyReferenceIdentity assembly = participant.Participant.Assembly.Identity;
        var typeVariable = new ResourceTypeExpression.Variable(
            new(ResourceEffectGenericVariableKind.Type, 0));
        var methodVariable = new ResourceTypeExpression.Variable(
            new(ResourceEffectGenericVariableKind.Method, 0));
        var target = new ResourceEffectTargetSelector.Member(new ResourceEffectMemberSelector(
            new ResourceTypeExpression.Named(new ResourceAssemblySelector(
                assembly.Name, assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(assembly.Version!)),
                "N", [new ResourceTypeNameSegment("IContract", 1)], [typeVariable]),
            "Target", ResourceEffectMemberKind.Method, isStatic: false, genericArity: 1,
            ResourceEffectCallingConvention.Default, hasThis: true, explicitThis: false,
            [new(typeVariable, ResourceEffectRefKind.Value), new(methodVariable, ResourceEffectRefKind.Value)],
            CoreType("Void")));
        ResourceEffectAdmission admission = AdmitModels(Model(
            "example.caller-generic-frame", target,
            new ResourceEffect.Operation(ResourceOperationBoundary.Ordinary,
                ResourceOperationThrows.Possible, Guard: null)));

        var complete = Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
            ResourceEffectResolver.Resolve(participant.Policy, admission, [participant.Participant],
                cancellationToken: TestContext.Current.CancellationToken));
        ResolvedResourceEffect effect = Assert.Single(complete.Snapshot.Effects);
        var typeBinding = Assert.Single(effect.GenericBindings,
            binding => binding.Variable.Kind == ResourceEffectGenericVariableKind.Type);
        Assert.Equal(TypeRefKind.MethodGenericParameter, typeBinding.Value.Type.Kind);
        Assert.NotNull(typeBinding.Value.GenericScope);
        Assert.Equal("String", Assert.Single(effect.GenericBindings,
            binding => binding.Variable.Kind == ResourceEffectGenericVariableKind.Method).Value.Type.Name);
        ResourceEffectClosedInterfaceSlot slot = Assert.Single(effect.InterfaceApplications).ClosedSlot;
        Assert.Equal(2, slot.ParameterGenericScopes.Length);
        Assert.Equal(typeBinding.Value.GenericScope, slot.ParameterGenericScopes[0]);
        Assert.Null(slot.ParameterGenericScopes[1]);
    }

    [Fact]
    public void OneImplementationRetainsBothClosedInterfaceSlotProofs()
    {
        SyntheticParticipant participant =
            CreateDualInterfaceApplicationParticipant();
        ResourceEffectAdmission admission =
            DualInterfaceApplicationAdmission(participant);

        ResourceEffectResolutionOutcome.Complete complete =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        ResolvedResourceEffect effect = Assert.Single(
            complete.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition);
        Assert.IsType<ResourceEffect.Operation>(effect.Effect);
        Assert.Null(effect.InterfaceApplication);
        Assert.Equal(2, effect.InterfaceApplications.Length);

        ResolvedResourceEffectSource source =
            Assert.Single(effect.Sources);
        Assert.Same(
            admission.Models[0].Declarations[0],
            source.Declaration);
        Assert.Equal(2, source.InterfaceApplications.Length);
        Assert.Equal(
            ["Int32", "String"],
            source.InterfaceApplications
                .Select(application =>
                    Assert.Single(
                        application.InterfacePath.ClosedInterfaceType
                            .TypeArguments).Name)
                .Order(StringComparer.Ordinal));
        Assert.All(
            source.InterfaceApplications,
            application =>
            {
                Assert.Equal(
                    CatalogMemberCorrespondenceKind.Exact,
                    application.InterfaceDeclaration.Correspondence.Kind);
                Assert.Equal(
                    CatalogMemberCorrespondenceKind.Exact,
                    application.Implementation.Correspondence.Kind);
                Assert.Equal(
                    effect.DirectCall.Definition.MetadataToken,
                    application.Implementation.MetadataToken);
                Assert.Equal(
                    effect.DirectCall.Definition.ModuleVersionId,
                    application.Implementation.ModuleVersionId);
            });
    }

    [Fact]
    public void ResolutionReceiptTracksInterfaceProofAssociations()
    {
        SyntheticParticipant participant =
            CreateDualInterfaceApplicationParticipant();
        ResourceEffectAdmission admission =
            DualInterfaceApplicationAdmission(participant);
        ResourceEffectResolutionOutcome.Complete seed =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    participant.Policy,
                    admission,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ResolvedResourceEffect implementation = Assert.Single(
            seed.Snapshot.Effects,
            effect => !effect.DirectCall.Definition.IsInterfaceDefinition);
        ResolvedResourceEffect[] interfaceEffects =
        [
            .. seed.Snapshot.Effects.Where(
                effect => effect.DirectCall.Definition.IsInterfaceDefinition),
        ];
        Assert.Equal(2, interfaceEffects.Length);
        AdmittedResourceEffectDeclaration declaration =
            admission.Models[0].Declarations[0];
        ResourceEffectInterfaceApplication.Applied[] applications =
        [
            .. implementation.InterfaceApplications.Select(evidence =>
                new ResourceEffectInterfaceApplication.Applied(
                    Assert.Single(
                        interfaceEffects,
                        effect => Assert.Single(
                                effect.DirectCall.Call.Callee
                                    .DeclaringType.TypeArguments).Name
                            == Assert.Single(
                                evidence.InterfacePath
                                    .ClosedInterfaceType
                                    .TypeArguments).Name)
                        .DirectCall,
                    implementation.DirectCall,
                    evidence)),
        ];
        Assert.Equal(2, applications.Length);

        var calls = new DirectCallDefinitionResolutionOutcome.Completed(
            implementation.DirectCall.Catalog,
            implementation.DirectCall.Generation,
            [participant.Participant],
            [
                .. seed.Snapshot.Effects
                    .Select(effect =>
                        (DirectCallDefinitionResolution)effect.DirectCall),
            ]);
        ResourceEffectOccurrencePopulationReceipt population =
            ResourceEffectResolver.CreatePopulationReceipt(calls);
        var fullIndex = new ResourceEffectInterfaceApplicationIndex(
            calls.Catalog,
            calls.Generation,
            admission.Receipt,
            population,
            ImmutableDictionary<
                AdmittedResourceEffectDeclaration,
                ImmutableArray<ResourceEffectInterfaceApplication>>.Empty
                .Add(declaration, [.. applications]),
            globalGap: null);
        var oneProofIndex = new ResourceEffectInterfaceApplicationIndex(
            calls.Catalog,
            calls.Generation,
            admission.Receipt,
            population,
            ImmutableDictionary<
                AdmittedResourceEffectDeclaration,
                ImmutableArray<ResourceEffectInterfaceApplication>>.Empty
                .Add(declaration, [applications[0]]),
            globalGap: null);

        ResourceEffectResolutionOutcome.Complete full =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    ResourceEffectResolver.CreateRequest(
                        admission,
                        calls,
                        fullIndex),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ResourceEffectResolutionOutcome.Complete oneProof =
            Assert.IsType<ResourceEffectResolutionOutcome.Complete>(
                ResourceEffectResolver.Resolve(
                    ResourceEffectResolver.CreateRequest(
                        admission,
                        calls,
                        oneProofIndex),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            2,
            Assert.Single(
                    full.Snapshot.Effects,
                    effect =>
                        !effect.DirectCall.Definition.IsInterfaceDefinition)
                .InterfaceApplications.Length);
        Assert.Single(
            Assert.Single(
                    oneProof.Snapshot.Effects,
                    effect =>
                        !effect.DirectCall.Definition.IsInterfaceDefinition)
                .InterfaceApplications);
        Assert.NotEqual(
            full.Receipt.ContentHash,
            oneProof.Receipt.ContentHash);
    }

    [Fact]
    public void InterfaceApplicationIndexRejectsAdmissionMismatchInGeneration()
    {
        SyntheticParticipant participant =
            CreateDualInterfaceApplicationParticipant();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            Resolve(participant);
        ResourceEffectAdmission admission =
            DualInterfaceApplicationAdmission(participant);
        ResourceEffectAdmission otherAdmission =
            DualInterfaceApplicationAdmission(
                participant,
                "example.other-interface-admission");
        ResourceEffectOccurrencePopulationReceipt population =
            ResourceEffectResolver.CreatePopulationReceipt(calls);
        var index = new ResourceEffectInterfaceApplicationIndex(
            calls.Catalog,
            calls.Generation,
            otherAdmission.Receipt,
            population,
            ImmutableDictionary<
                AdmittedResourceEffectDeclaration,
                ImmutableArray<ResourceEffectInterfaceApplication>>.Empty,
            globalGap: null);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResourceEffectResolver.Resolve(
                    new ResourceEffectResolutionRequest(
                        admission,
                        admission.Receipt,
                        calls,
                        population,
                        index),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(calls.Catalog, index.Catalog);
        Assert.Same(calls.Generation, index.Generation);
        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .InterfaceApplicationAdmissionMismatch,
            rejected.Kind);
    }

    [Fact]
    public void InterfaceApplicationIndexRejectsPopulationMismatchInGeneration()
    {
        SyntheticParticipant participant =
            CreateDualInterfaceApplicationParticipant();
        DirectCallDefinitionResolutionOutcome.Completed calls =
            Resolve(participant);
        ResourceEffectAdmission admission =
            DualInterfaceApplicationAdmission(participant);
        ResourceEffectOccurrencePopulationReceipt population =
            ResourceEffectResolver.CreatePopulationReceipt(calls);
        var subset = new DirectCallDefinitionResolutionOutcome.Completed(
            calls.Catalog,
            calls.Generation,
            calls.Population,
            []);
        ResourceEffectOccurrencePopulationReceipt subsetPopulation =
            ResourceEffectResolver.CreatePopulationReceipt(subset);
        var index = new ResourceEffectInterfaceApplicationIndex(
            calls.Catalog,
            calls.Generation,
            admission.Receipt,
            subsetPopulation,
            ImmutableDictionary<
                AdmittedResourceEffectDeclaration,
                ImmutableArray<ResourceEffectInterfaceApplication>>.Empty,
            globalGap: null);

        ResourceEffectResolutionOutcome.Rejected rejected =
            Assert.IsType<ResourceEffectResolutionOutcome.Rejected>(
                ResourceEffectResolver.Resolve(
                    new ResourceEffectResolutionRequest(
                        admission,
                        admission.Receipt,
                        calls,
                        population,
                        index),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Same(calls.Generation, subset.Generation);
        Assert.NotEqual(
            population.ContentHash,
            subsetPopulation.ContentHash);
        Assert.Equal(
            ResourceEffectResolutionRejectionKind
                .InterfaceApplicationPopulationMismatch,
            rejected.Kind);
    }

    [Theory]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension.SelectorBindings)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension.InterfaceMethods)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension
            .MetadataAssociations)]
    [InlineData(
        ResourceEffectInterfaceApplicationWorkDimension.SlotComparisons)]
    public void AdditionalInterfaceWorkLimitsRemainVisible(
        ResourceEffectInterfaceApplicationWorkDimension dimension)
    {
        SyntheticParticipant participant =
            CreateDualInterfaceApplicationParticipant();
        ResourceEffectAdmission admission =
            DualInterfaceApplicationAdmission(participant);
        ResourceEffectInterfaceApplicationLimits limits = dimension switch
        {
            ResourceEffectInterfaceApplicationWorkDimension
                .SelectorBindings =>
                new(maxSelectorBindings: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .InterfaceMethods =>
                new(maxInterfaceMethods: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .MetadataAssociations =>
                new(maxMetadataAssociations: 1),
            ResourceEffectInterfaceApplicationWorkDimension
                .SlotComparisons =>
                new(maxSlotComparisons: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
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

        ResourceEffectResolutionGap gap = Assert.Single(
            incomplete.Gaps,
            gap => gap.InterfaceApplicationGap?.WorkDimension
                == dimension && gap.PhysicalInvocation is null);
        Assert.Equal(
            ResourceEffectResolutionGapKind.InterfaceApplicationIncomplete,
            gap.Kind);
        ResourceEffectInterfaceApplicationGap interfaceGap =
            Assert.IsType<ResourceEffectInterfaceApplicationGap>(
                gap.InterfaceApplicationGap);
        Assert.Equal(
            ResourceEffectInterfaceApplicationGapKind.WorkLimitExceeded,
            interfaceGap.Kind);
        Assert.Equal(1, interfaceGap.Limit);
        Assert.True(
            interfaceGap.RequiredWork > interfaceGap.Limit);
    }

    static ResourceEffectAdmission DualInterfaceApplicationAdmission(
        SyntheticParticipant participant,
        string identity = "example.dual-interface-application")
    {
        AssemblyReferenceIdentity assembly =
            participant.Participant.Assembly.Identity;
        var variable = new ResourceTypeExpression.Variable(
            new ResourceEffectGenericVariable(
                ResourceEffectGenericVariableKind.Type,
                0));
        var declaringType = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                assembly.Name,
                assembly.PublicKeyToken,
                ResourceAssemblyVersionPolicy.Exact(assembly.Version!)),
            "N",
            [new ResourceTypeNameSegment("I", 1)],
            [variable]);
        var target = new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                "Target",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                parameters: [],
                CoreType("Void")));
        return AdmitModels(
            Model(
                identity,
                target,
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Ordinary,
                    ResourceOperationThrows.Possible,
                    Guard: null)));
    }

    static SyntheticParticipant CreateDualInterfaceApplicationParticipant()
    {
        const string AssemblyName = "ResourceEffectInterfaceRepair";
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(AssemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(AssemblyName),
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
        TypeDefinitionHandle contract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("I`1"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle implementation =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Concrete"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(4));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Calls"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(5));
        metadata.AddGenericParameter(
            contract,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        TypeSpecificationHandle intContract =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    GenericInstanceSignature(contract, [0x08])));
        TypeSpecificationHandle stringContract =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    GenericInstanceSignature(contract, [0x0E])));
        metadata.AddInterfaceImplementation(
            implementation,
            intContract);
        metadata.AddInterfaceImplementation(
            implementation,
            stringContract);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var implementationIl = new BlobBuilder();
        implementationIl.WriteByte((byte)ILOpCode.Ret);
        int implementationBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(implementationIl));
        byte[] instanceVoidSignature = [0x20, 0x00, 0x01];
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(instanceVoidSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle firstGetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("get_First"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle secondGetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("get_Second"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle implementationTarget =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(instanceVoidSignature),
                implementationBody,
                MetadataTokens.ParameterHandle(1));

        PropertyDefinitionHandle firstProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("First"),
                metadata.GetOrAddBlob(new byte[] { 0x28, 0x00, 0x08 }));
        PropertyDefinitionHandle secondProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Second"),
                metadata.GetOrAddBlob(new byte[] { 0x28, 0x00, 0x08 }));
        metadata.AddPropertyMap(contract, firstProperty);
        metadata.AddMethodSemantics(
            firstProperty,
            MethodSemanticsAttributes.Getter,
            firstGetter);
        metadata.AddMethodSemantics(
            secondProperty,
            MethodSemanticsAttributes.Getter,
            secondGetter);

        MemberReferenceHandle intTarget =
            metadata.AddMemberReference(
                intContract,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(instanceVoidSignature));
        MemberReferenceHandle stringTarget =
            metadata.AddMemberReference(
                stringContract,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(instanceVoidSignature));
        var callerIl = new BlobBuilder();
        var callerInstructions = new InstructionEncoder(callerIl);
        callerInstructions.OpCode(ILOpCode.Ldnull);
        callerInstructions.OpCode(ILOpCode.Callvirt);
        callerInstructions.Token(intTarget);
        callerInstructions.OpCode(ILOpCode.Ldnull);
        callerInstructions.OpCode(ILOpCode.Callvirt);
        callerInstructions.Token(stringTarget);
        callerInstructions.OpCode(ILOpCode.Ldnull);
        callerInstructions.OpCode(ILOpCode.Callvirt);
        callerInstructions.Token(implementationTarget);
        callerInstructions.OpCode(ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            callerInstructions,
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        pe.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "resource-effect interface repair test"))!;
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                AssemblyName + ".dll",
                ImmutableArray.CreateRange(image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        return new SyntheticParticipant(
            image,
            new CatalogCallGraphParticipant(index, assembly),
            new ExactPolicy([assembly, CoreLibraryAssembly]));
    }
}
