using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class DirectCallDefinitionResolutionTests
{
    static string OwnershipFixturePath =>
        FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();

    [Fact]
    public void ArrayPoolCallsResolveTheirExactFrameworkDefinitions()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            OwnershipFixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                OwnershipFixturePath,
                AssemblyResolutionProvenance.Local(
                    "direct-call definition test"));
        var participant =
            new CatalogCallGraphParticipant(index, assembly);
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(
                OwnershipFixturePath));

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    policy,
                    [participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(index.DirectCalls.Length, completed.Results.Length);
        string frameworkAssembly =
            typeof(System.Buffers.ArrayPool<>)
                .Assembly.GetName().Name!;
        DirectCallDefinitionResolution.Resolved[] arrayPool =
        [
            .. completed.Results
                .OfType<DirectCallDefinitionResolution.Resolved>()
                .Where(result =>
                    result.Definition.Assembly.Name == frameworkAssembly
                    && result.Call.Callee.Name
                        is "get_Shared" or "Rent" or "Return"),
        ];
        Assert.NotEmpty(arrayPool);

        Assert.Contains(
            arrayPool,
            result =>
                result.Definition.Member.Name == "get_Shared"
                && result.Definition.Semantics
                    == DirectCallDefinitionSemantics.PropertyGetter);
        Assert.Contains(
            arrayPool,
            result =>
                result.Definition.Member.Name == "Rent"
                && result.Definition.Semantics
                    == DirectCallDefinitionSemantics.Method);
        Assert.Contains(
            arrayPool,
            result =>
                result.Definition.Member.Name == "Return"
                && result.Definition.Member.ParameterTypes.Length == 2);
        Assert.All(
            arrayPool,
            result =>
            {
                Assert.Equal(frameworkAssembly, result.Definition.Assembly.Name);
                Assert.Same(completed.Generation, result.Generation);
                Assert.Same(
                    completed.Generation,
                    result.Definition.Correspondence.Generation);
                Assert.Equal(
                    result.Call.EvidenceMethod.ModuleVersionId,
                    result.PhysicalInvocation.ModuleVersionId);
                Assert.NotEqual(0, result.Definition.MetadataToken);
            });
    }

    [Fact]
    public void SameNamedUserMethodDoesNotBecomeFrameworkIdentity()
    {
        DirectCallDefinitionResolutionOutcome.Completed completed =
            ResolveOwnershipFixture();
        DirectCallDefinitionResolution.Resolved local =
            Assert.Single(
                completed.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                        result.Call.Caller.Name
                            == "RentAndReturnThroughInstance"
                        && result.Call.Callee.Name == "Return"
                        && result.Call.Callee.DeclaringType.Name
                            .Contains(
                                "OwnershipSink",
                                StringComparison.Ordinal));

        Assert.Equal(
            local.Participant.CallGraph.ModuleIdentity.AssemblyIdentity,
            local.Definition.Assembly);
        Assert.NotEqual(
            typeof(System.Buffers.ArrayPool<>)
                .Assembly.GetName().Name,
            local.Definition.Assembly.Name);
    }

    [Fact]
    public void OrdinaryStaticAndInstanceMethodsSelectExactly()
    {
        SyntheticParticipant staticCall = CreateSynthetic(new());
        SyntheticParticipant instanceCall = CreateSynthetic(new()
        {
            TargetAttributes = MethodAttributes.Public,
            TargetSignature = [0x20, 0x00, 0x01],
            CallKind = CallKind.CallVirtual,
        });

        DirectCallDefinitionResolution.Resolved resolvedStatic =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(staticCall).Results));
        DirectCallDefinitionResolution.Resolved resolvedInstance =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(instanceCall).Results));

        Assert.False(resolvedStatic.Definition.Member.HasThis);
        Assert.True(resolvedInstance.Definition.Member.HasThis);
        Assert.Equal(
            resolvedInstance.Call.Callee.Name,
            resolvedInstance.Definition.Member.Name);
    }

    [Fact]
    public void MetadataStaticnessContradictionIsUnsupported()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetAttributes =
                MethodAttributes.Public | MethodAttributes.Static,
            TargetSignature = [0x20, 0x00, 0x01],
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void StaticCallVirtualIsUnsupported()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            CallKind = CallKind.CallVirtual,
            TargetAttributes =
                MethodAttributes.Public | MethodAttributes.Static,
            TargetSignature = [0x00, 0x00, 0x01],
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void ClosedMethodSpecResolvesButBareGenericDefinitionDoesNot()
    {
        var generic = new SyntheticOptions
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
        };

        DirectCallDefinitionResolution.Resolved closed =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(
                    Resolve(
                        CreateSynthetic(generic with
                        {
                            InstantiateGenericMethod = true,
                        })).Results));
        Assert.Single(closed.Call.Callee.TypeArguments);
        Assert.Equal(1, closed.Definition.Member.GenericArity);

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(
                Resolve(CreateSynthetic(generic)).Results));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeOwnedMethodSpecArgumentResolves(bool typeParameter)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature = typeParameter
                ? [0x0A, 0x01, 0x13, 0x00]
                : [0x0A, 0x01, 0x1E, 0x00],
            CallerMethodGenericParameterRows =
                typeParameter ? 0 : 1,
            TypeGenericParameterRows =
                typeParameter ? 1 : 0,
        });

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        TypeRef argument = Assert.Single(
            resolved.Call.Callee.TypeArguments);
        Assert.Equal(
            typeParameter
                ? TypeRefKind.GenericParameter
                : TypeRefKind.MethodGenericParameter,
            argument.Kind);
        Assert.Equal(0, argument.GenericParameterIndex);
    }

    [Fact]
    public void GenericAsyncFixtureResolvesScopeOwnedMethodSpec()
    {
        string path = typeof(
            ClassicAsyncFixtures
                .ClassicGenericMethodSelfSiblingFixture)
            .Assembly.Location;
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "generic MethodSpec direct-call test"));
        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(path)),
                    [new CatalogCallGraphParticipant(index, assembly)],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.Single(
                completed.Results
                    .OfType<DirectCallDefinitionResolution.Resolved>(),
                result =>
                    result.Call.Caller.Name == "ReadAsync"
                    && result.Call.EvidenceMethod.Name == "MoveNext"
                    && result.Call.Callee.Name == "Read"
                    && result.Call.Callee.DeclaringType.Name
                        .Contains(
                            "ClassicGenericMethodSelfSiblingFixture",
                            StringComparison.Ordinal));
        Assert.Equal(
            TypeRefKind.GenericParameter,
            Assert.Single(resolved.Call.Callee.TypeArguments).Kind);
        Assert.Equal("Read", resolved.Definition.Member.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedEvidenceScopeOrdinalsAreUnsupported(
        bool typeParameter)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature = typeParameter
                ? [0x0A, 0x01, 0x13, 0x00]
                : [0x0A, 0x01, 0x1E, 0x00],
            SeparateCallerType = typeParameter,
            CallerTypeGenericParameterRows =
                typeParameter ? 1 : 0,
            CallerTypeGenericParameterStartIndex =
                typeParameter ? 1 : 0,
            CallerMethodGenericParameterRows =
                typeParameter ? 0 : 1,
            CallerMethodGenericParameterStartIndex =
                typeParameter ? 0 : 1,
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void MalformedGenericInstanceMethodSpecIsUnsupported()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
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
                0x02,
                0x08,
                0x08,
            ],
        });

        Assert.Single(participant.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void ArrayAndGenericInstanceMethodSpecArgumentsResolve()
    {
        SyntheticParticipant array = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature =
                [0x0A, 0x01, 0x1D, 0x08],
        });
        SyntheticParticipant genericInstance = CreateSynthetic(new()
        {
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

        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(array).Results));
        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(genericInstance).Results));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ConstructedCustomModifierTypeSpecValidatesArity(
        int modifierArgumentCount,
        bool resolves)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            CallerMethodGenericParameterRows = 1,
            AddGenericArgumentType = true,
            AddConstructedModifierTypeSpecification = true,
            ConstructedModifierArgumentCount =
                modifierArgumentCount,
            MethodSpecSignature =
                [0x0A, 0x01, 0x20, 0x06, 0x08],
        });

        DirectCallDefinitionResolution result =
            Assert.Single(Resolve(participant).Results);
        if (!resolves)
        {
            Assert.IsType<
                DirectCallDefinitionResolution.Unsupported>(result);
            return;
        }

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<
                DirectCallDefinitionResolution.Resolved>(result);
        TypeRef modifier =
            Assert.Single(
                resolved.Call.Callee.TypeArguments)
                .ModifierType!;
        Assert.Equal(TypeRefKind.GenericInstance, modifier.Kind);
        Assert.Equal(
            TypeRef.MethodGenericParameter(0),
            Assert.Single(modifier.TypeArguments));
    }

    public static TheoryData<string, byte[]>
        NestedLegalMethodSpecificationArguments => new()
        {
            {
                "custom-modified",
                [0x0A, 0x01, 0x20, 0x08, 0x08]
            },
            {
                "array-of-pointer",
                [0x0A, 0x01, 0x1D, 0x0F, 0x08]
            },
            {
                "array-of-function-pointer",
                [0x0A, 0x01, 0x1D, 0x1B, 0x00, 0x00, 0x01]
            },
        };

    [Theory]
    [MemberData(nameof(NestedLegalMethodSpecificationArguments))]
    public void NestedLegalMethodSpecArgumentsResolve(
        string _,
        byte[] signature)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature = signature,
        });

        Assert.Single(participant.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(participant).Results));
    }

    public static TheoryData<string, byte[]>
        IllegalMethodSpecificationArguments => new()
        {
            { "byref", [0x0A, 0x01, 0x10, 0x08] },
            { "pointer", [0x0A, 0x01, 0x0F, 0x08] },
            { "pinned", [0x0A, 0x01, 0x45, 0x08] },
            { "void", [0x0A, 0x01, 0x01] },
            {
                "function pointer",
                [0x0A, 0x01, 0x1B, 0x00, 0x00, 0x01]
            },
            { "typed reference", [0x0A, 0x01, 0x16] },
        };

    [Theory]
    [MemberData(nameof(IllegalMethodSpecificationArguments))]
    public void IllegalMethodSpecArgumentsFromMetadataDoNotResolve(
        string _,
        byte[] signature)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            MethodSpecSignature = signature,
        });

        Assert.Empty(participant.Participant.CallGraph.DirectCalls);
        Assert.NotEmpty(participant.Participant.CallGraph.Diagnostics);
        Assert.Empty(Resolve(participant).Results);
    }

    [Fact]
    public void GenericInstanceArityUsesGenericParamRowsNotTypeName()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            AddGenericArgumentType = true,
            GenericArgumentTypeName = "UnconventionallyNamed",
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

        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void BareGenericTypeDefinitionMethodSpecArgumentIsUnsupported()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x10, 0x01, 0x00, 0x01],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            AddGenericArgumentType = true,
            GenericArgumentTypeName = "UnconventionallyNamed",
            MethodSpecSignature = [0x0A, 0x01, 0x12, 0x0C],
        });

        Assert.Single(participant.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    public static TheoryData<string, byte[]> IllegalOrdinarySignatures =>
        new()
        {
            {
                "byref-of-byref",
                [0x00, 0x00, 0x10, 0x10, 0x08]
            },
            {
                "array-of-byref",
                [0x00, 0x00, 0x1D, 0x10, 0x08]
            },
            {
                "array-of-void",
                [0x00, 0x00, 0x1D, 0x01]
            },
            {
                "pinned-return",
                [0x00, 0x00, 0x45, 0x08]
            },
        };

    [Theory]
    [MemberData(nameof(IllegalOrdinarySignatures))]
    public void NestedIllegalOrdinarySignaturesAreUnsupported(
        string _,
        byte[] signature)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = signature,
            TargetReturnValue = SyntheticStackValue.Null,
            PopCallReturn = true,
        });

        Assert.Single(participant.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void PointerReturnSignatureResolves()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature = [0x00, 0x00, 0x0F, 0x08],
            TargetReturnValue = SyntheticStackValue.NativeInt,
            PopCallReturn = true,
        });

        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void CustomModifiedMethodSignatureInstantiatesButStaysOpen()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature =
            [
                0x10,
                0x01,
                0x01,
                0x20,
                0x08,
                0x1E,
                0x00,
                0x20,
                0x08,
                0x1E,
                0x00,
            ],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            TargetReturnValue = SyntheticStackValue.Int32,
            CallArgumentKinds = [SyntheticStackValue.Int32],
            PopCallReturn = true,
        });

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        TypeRef instantiated =
            Assert.Single(resolved.Call.Callee.ParameterTypes);
        TypeRef open =
            Assert.Single(resolved.Call.Callee.OpenSignatureParameters);

        Assert.Equal(TypeRefKind.Unsupported, instantiated.Kind);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            instantiated.UnmodifiedType);
        Assert.Equal(TypeRefKind.Unsupported, open.Kind);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            open.UnmodifiedType!.Kind);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            resolved.Call.Callee.ReturnType.UnmodifiedType);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            resolved.Call.Callee.OpenSignatureReturn
                .UnmodifiedType!.Kind);
    }

    [Fact]
    public void FunctionPointerMethodSignatureInstantiatesButStaysOpen()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature =
            [
                0x10,
                0x01,
                0x01,
                0x01,
                0x1B,
                0x00,
                0x01,
                0x1E,
                0x00,
                0x1E,
                0x00,
            ],
            MethodGenericParameterRows = 1,
            InstantiateGenericMethod = true,
            CallArgumentKinds = [SyntheticStackValue.NativeInt],
        });

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        MethodSignature<TypeRef> instantiated =
            Assert.Single(resolved.Call.Callee.ParameterTypes)
                .FunctionPointerSignature!.Value;
        MethodSignature<TypeRef> open =
            Assert.Single(
                    resolved.Call.Callee.OpenSignatureParameters)
                .FunctionPointerSignature!.Value;

        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            instantiated.ReturnType);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            Assert.Single(instantiated.ParameterTypes));
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            open.ReturnType.Kind);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            Assert.Single(open.ParameterTypes).Kind);
    }

    [Fact]
    public void OpenMemberRefSignatureUsesCalleeGenericArity()
    {
        ExternalSyntheticParticipant matchingCandidate =
            CreateExternalSynthetic(new()
            {
                TargetSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x1E, 0x00],
                TargetMethodGenericParameterRows = 1,
                MemberReferenceSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x1E, 0x01],
                MethodSpecificationSignature =
                    [0x0A, 0x01, 0x08],
                CallerMethodGenericParameterRows = 2,
                CallArgumentKinds = [SyntheticStackValue.Int32],
            });

        Assert.Single(matchingCandidate.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(matchingCandidate).Results));

        ExternalSyntheticParticipant absentOverload =
            CreateExternalSynthetic(new()
            {
                TargetSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x08],
                TargetMethodGenericParameterRows = 1,
                MemberReferenceSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x1E, 0x01],
                MethodSpecificationSignature =
                    [0x0A, 0x01, 0x08],
                CallerMethodGenericParameterRows = 2,
                CallArgumentKinds = [SyntheticStackValue.Int32],
            });
        Assert.Single(absentOverload.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(absentOverload).Results));

        ExternalSyntheticParticipant validAbsentOverload =
            CreateExternalSynthetic(new()
            {
                TargetSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x08],
                TargetMethodGenericParameterRows = 1,
                MemberReferenceSignature =
                    [0x10, 0x01, 0x01, 0x01, 0x1E, 0x00],
                MethodSpecificationSignature =
                    [0x0A, 0x01, 0x08],
                CallerMethodGenericParameterRows = 2,
                CallArgumentKinds = [SyntheticStackValue.Int32],
            });
        Assert.Single(validAbsentOverload.Participant.CallGraph.DirectCalls);
        Assert.IsType<DirectCallDefinitionResolution.Unmatched>(
            Assert.Single(Resolve(validAbsentOverload).Results));
    }

    [Fact]
    public void MalformedDeclaringTypeGenericOrdinalIsUnsupported()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TypeGenericParameterRows = 1,
            SeparateCallerType = true,
            CallViaMalformedGenericDeclaringType = true,
        });

        DirectCallDefinitionResolution.Unsupported unsupported =
            Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
                Assert.Single(Resolve(participant).Results));
        TypeRef argument = Assert.Single(
            unsupported.Call.Callee.DeclaringType.TypeArguments);
        Assert.Equal(TypeRefKind.GenericParameter, argument.Kind);
        Assert.Equal(1, argument.GenericParameterIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedGenericParameterOrdinalsAreUnsupported(
        bool typeParameter)
    {
        SyntheticParticipant participant = CreateSynthetic(
            typeParameter
                ? new SyntheticOptions
                {
                    TypeGenericParameterRows = 1,
                    TypeGenericParameterStartIndex = 1,
                }
                : new SyntheticOptions
                {
                    TargetSignature = [0x10, 0x01, 0x00, 0x01],
                    MethodGenericParameterRows = 1,
                    MethodGenericParameterStartIndex = 1,
                    InstantiateGenericMethod = true,
                });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void ValidPropertyGetterRetainsMethodSemantics()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            AssemblyName = "ExecutablePropertyGetter",
            TargetAttributes =
                MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            TargetSignature = [0x00, 0x00, 0x08],
            TargetReturnValue = SyntheticStackValue.Int32,
            PopCallReturn = true,
            PropertySignature = [0x08, 0x00, 0x08],
            PropertySemantics = [MethodSemanticsAttributes.Getter],
        });

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        Assert.Equal(
            DirectCallDefinitionSemantics.PropertyGetter,
            resolved.Definition.Semantics);
        AssertCallerExecutes(participant);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MalformedPropertyOrAccessorSemanticsAreUnsupported(
        bool duplicateGetter,
        bool eventAssociation)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetAttributes =
                MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            TargetSignature = [0x00, 0x00, 0x08],
            TargetReturnValue = SyntheticStackValue.Int32,
            PopCallReturn = true,
            PropertySignature = [0x08, 0x00, 0x08],
            PropertySemantics = duplicateGetter
                ?
                [
                    MethodSemanticsAttributes.Getter,
                    MethodSemanticsAttributes.Getter,
                ]
                : null,
            EventSemantics = eventAssociation
                ? [MethodSemanticsAttributes.Adder]
                : null,
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(participant).Results));
    }

    [Fact]
    public void UnsupportedDeclaringTypeAndUnreadableSignatureStayVisible()
    {
        SyntheticParticipant unsupportedType = CreateSynthetic(new()
        {
            UnsupportedDeclaringType = true,
        });
        byte[] unreadable = new byte[
            SignatureBlobGuard.DefaultMaxDepth + 4];
        unreadable[0] = 0x00;
        unreadable[1] = 0x00;
        unreadable.AsSpan(
                2,
                SignatureBlobGuard.DefaultMaxDepth + 1)
            .Fill(0x1D);
        unreadable[^1] = 0x1C;
        SyntheticParticipant unreadableSignature = CreateSynthetic(new()
        {
            TargetSignature = unreadable,
            TargetReturnValue = SyntheticStackValue.Null,
            PopCallReturn = true,
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(unsupportedType).Results));
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(unreadableSignature).Results));
    }

    [Fact]
    public void PropertySettersAreUnsupportedAndFunctionPointersResolve()
    {
        SyntheticParticipant setter = CreateSynthetic(new()
        {
            TargetAttributes =
                MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            TargetSignature = [0x00, 0x01, 0x01, 0x08],
            CallArgumentKinds = [SyntheticStackValue.Int32],
            PropertySignature = [0x08, 0x00, 0x08],
            PropertySemantics = [MethodSemanticsAttributes.Setter],
        });
        SyntheticParticipant functionPointer = CreateSynthetic(new()
        {
            TargetSignature =
            [
                0x00,
                0x01,
                0x01,
                0x1B,
                0x00,
                0x00,
                0x01,
            ],
            CallArgumentKinds = [SyntheticStackValue.NativeInt],
        });

        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(Resolve(setter).Results));
        Assert.IsType<DirectCallDefinitionResolution.Resolved>(
            Assert.Single(Resolve(functionPointer).Results));
    }

    public static TheoryData<string, byte[], bool>
        FunctionPointerHeaders => new()
        {
            { "explicit-this-without-this", [0x40, 0x00, 0x01], false },
            { "explicit-this", [0x60, 0x00, 0x01], true },
            { "reserved-bit", [0x80, 0x00, 0x01], false },
            { "zero-arity-generic", [0x10, 0x00, 0x00, 0x01], false },
            {
                "nonzero-arity-generic",
                [0x10, 0x01, 0x01, 0x01, 0x08],
                false
            },
        };

    [Theory]
    [MemberData(nameof(FunctionPointerHeaders))]
    public void FunctionPointerHeadersAreValidated(
        string _,
        byte[] functionPointerSignature,
        bool isSupported)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetSignature =
            [
                0x00,
                0x01,
                0x01,
                0x1B,
                .. functionPointerSignature,
            ],
            CallArgumentKinds = [SyntheticStackValue.NativeInt],
        });

        DirectCallDefinitionResolution result =
            Assert.Single(Resolve(participant).Results);
        if (isSupported)
        {
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(result);
        }
        else
        {
            Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
                result);
        }
    }

    [Fact]
    public void UnmatchedAmbiguousUnsupportedAndIncompleteAreDistinct()
    {
        Assert.IsType<DirectCallDefinitionResolution.Unmatched>(
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    CallName = "Missing",
                    CallViaMemberReference = true,
                })).Results));
        Assert.IsType<DirectCallDefinitionResolution.Ambiguous>(
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    TargetCount = 2,
                    CallViaMemberReference = true,
                })).Results));
        Assert.IsType<DirectCallDefinitionResolution.Unsupported>(
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    CallKind = CallKind.NewObject,
                })).Results));
        Assert.IsType<DirectCallDefinitionResolution.Incomplete>(
            Assert.Single(
                Resolve(CreateSynthetic(new()
                {
                    MissingDeclaringType = true,
                })).Results));
    }

    [Fact]
    public void NewObjectConstructorResolvesExactMethodDefinition()
    {
        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(
                    Resolve(CreateSynthetic(new()
                    {
                        TargetName = ".ctor",
                        TargetAttributes =
                            MethodAttributes.Public
                            | MethodAttributes.SpecialName
                            | MethodAttributes.RTSpecialName,
                        TargetSignature = [0x20, 0x00, 0x01],
                        CallKind = CallKind.NewObject,
                        PopCallReturn = true,
                    })).Results));

        Assert.Equal(MemberKind.Constructor, resolved.Definition.Member.Kind);
        Assert.Equal(CallKind.NewObject, resolved.Call.Kind);
    }

    [Fact]
    public void TwoExactExternalCandidatesStayAmbiguousWithUnreadablePeer()
    {
        ExternalSyntheticParticipant scenario =
            CreateExternalSynthetic(new()
            {
                ExactTargetCount = 2,
                AddUnreadableTarget = true,
            });

        Assert.IsType<DirectCallDefinitionResolution.Ambiguous>(
            Assert.Single(Resolve(scenario).Results));
    }

    [Fact]
    public void DefinitionOpenFailureIsIncomplete()
    {
        ExternalSyntheticParticipant scenario =
            CreateExternalSynthetic(new());
        int opens = 0;
        ResolvedAssemblyReference unavailable =
            ResolvedAssemblyReference.Create(
                scenario.TargetAssembly.Identity,
                path: null,
                () => Interlocked.Increment(ref opens) <= 2
                    ? new MemoryStream(
                        scenario.TargetImage,
                        writable: false)
                    : throw new IOException(
                        "definition image unavailable"),
                AssemblyResolutionProvenance.Local(
                    "unavailable definition test"));

        DirectCallDefinitionResolution.Incomplete incomplete =
            Assert.IsType<DirectCallDefinitionResolution.Incomplete>(
                Assert.Single(
                    Resolve(
                        scenario,
                        new ExactPolicy(
                            [
                                scenario.Participant.Assembly,
                                unavailable,
                                CoreLibraryAssembly,
                            ])).Results));

        Assert.Equal(
            DirectCallDefinitionGapKind.DefinitionUnavailable,
            incomplete.Gap.Kind);
        Assert.True(opens >= 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactMethodDefSelectionIgnoresStructuralDuplicate(
        bool methodSpecification)
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            TargetCount = 2,
            TargetSignature = methodSpecification
                ? [0x10, 0x01, 0x00, 0x01]
                : [0x00, 0x00, 0x01],
            MethodGenericParameterRows =
                methodSpecification ? 1 : 0,
            InstantiateGenericMethod = methodSpecification,
        });

        DirectCallDefinitionResolution.Resolved resolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                Assert.Single(Resolve(participant).Results));
        Assert.Equal(
            resolved.Call.CalleeDefinitionToken,
            resolved.Definition.MetadataToken);
    }

    [Fact]
    public void EveryWorkLimitRetainsItsPhysicalCall()
    {
        AssertLimit(
            CreateSynthetic(new() { CallCount = 2 }),
            new DirectCallDefinitionResolutionLimits(
                maxInvocationOccurrences: 1),
            DirectCallDefinitionWorkDimension.InvocationOccurrences,
            resultIndex: 1);
        AssertLimit(
            CreateSynthetic(new()),
            new DirectCallDefinitionResolutionLimits(
                maxSignatureNodes: 1),
            DirectCallDefinitionWorkDimension.SignatureNodes);
        AssertLimit(
            CreateSynthetic(new()),
            new DirectCallDefinitionResolutionLimits(
                maxDefinitionCandidates: 1),
            DirectCallDefinitionWorkDimension.DefinitionCandidates);
        AssertLimit(
            CreateSynthetic(new()
            {
                CallViaMemberReference = true,
            }),
            new DirectCallDefinitionResolutionLimits(
                maxInvocationBindings: 1),
            DirectCallDefinitionWorkDimension.InvocationBindings);
        AssertLimit(
            CreateSynthetic(new()
            {
                TargetAttributes =
                    MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                TargetSignature = [0x00, 0x00, 0x08],
                PropertySignature = [0x08, 0x00, 0x08],
                TargetReturnValue = SyntheticStackValue.Int32,
                PopCallReturn = true,
                PropertySemantics = [MethodSemanticsAttributes.Getter],
            }),
            new DirectCallDefinitionResolutionLimits(
                maxMetadataAssociations: 1),
            DirectCallDefinitionWorkDimension.MetadataAssociations);
    }

    [Fact]
    public void InvocationOccurrenceLimitPublishesNoPartialSuccess()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            CallCount = 2,
        });

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(
                participant,
                new DirectCallDefinitionResolutionLimits(
                    maxInvocationOccurrences: 1));

        Assert.Equal(2, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(result);
                Assert.Equal(
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                    incomplete.Gap.Kind);
                Assert.Equal(
                    DirectCallDefinitionWorkDimension.InvocationOccurrences,
                    incomplete.Gap.WorkDimension);
                Assert.Equal(1, incomplete.Gap.Limit);
                Assert.Equal(2, incomplete.Gap.RequiredWork);
                Assert.Equal(
                    incomplete.PhysicalInvocation,
                    incomplete.Gap.PhysicalInvocation);
            });
        Assert.NotEqual(
            completed.Results[0].PhysicalInvocation,
            completed.Results[1].PhysicalInvocation);
    }

    [Fact]
    public void RepeatedOperandReusesPlanWithoutCollapsingInvocations()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            CallCount = 50,
        });

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(
                participant,
                new DirectCallDefinitionResolutionLimits(
                    maxSignatureNodes: 16));

        Assert.Equal(50, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
                Assert.IsType<
                    DirectCallDefinitionResolution.Resolved>(result));
        Assert.Equal(
            50,
            completed.Results
                .Select(result => result.PhysicalInvocation)
                .Distinct()
                .Count());
    }

    [Fact]
    public void InvocationBindingLimitPublishesNoPartialSuccess()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            CallCount = 2,
        });

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(
                participant,
                new DirectCallDefinitionResolutionLimits(
                    maxInvocationBindings: 1));

        Assert.Equal(2, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(result);
                Assert.Equal(
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                    incomplete.Gap.Kind);
                Assert.Equal(
                    DirectCallDefinitionWorkDimension.InvocationBindings,
                    incomplete.Gap.WorkDimension);
                Assert.Equal(1, incomplete.Gap.Limit);
                Assert.Equal(2, incomplete.Gap.RequiredWork);
                Assert.Equal(
                    incomplete.PhysicalInvocation,
                    incomplete.Gap.PhysicalInvocation);
            });
        Assert.NotEqual(
            completed.Results[0].PhysicalInvocation,
            completed.Results[1].PhysicalInvocation);
    }

    [Theory]
    [InlineData(DirectCallDefinitionWorkDimension.DefinitionCandidates)]
    [InlineData(DirectCallDefinitionWorkDimension.MetadataAssociations)]
    public void DiscoveryWorkLimitsPublishNoPartialSuccess(
        DirectCallDefinitionWorkDimension dimension)
    {
        DirectCallDefinitionResolutionLimits limits = dimension switch
        {
            DirectCallDefinitionWorkDimension.DefinitionCandidates =>
                new(maxDefinitionCandidates: 1),
            DirectCallDefinitionWorkDimension.MetadataAssociations =>
                new(maxMetadataAssociations: 1),
            _ => throw new InvalidOperationException(),
        };

        DirectCallDefinitionResolutionOutcome.Completed completed =
            ResolveOwnershipFixture(limits);

        Assert.NotEmpty(completed.Results);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(result);
                Assert.Equal(
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                    incomplete.Gap.Kind);
                Assert.Equal(dimension, incomplete.Gap.WorkDimension);
                Assert.NotNull(incomplete.Gap.Limit);
                Assert.True(
                    incomplete.Gap.RequiredWork > incomplete.Gap.Limit);
                Assert.Equal(
                    incomplete.PhysicalInvocation,
                    incomplete.Gap.PhysicalInvocation);
            });
        Assert.Single(
            completed.Results
                .Cast<DirectCallDefinitionResolution.Incomplete>()
                .Select(result => result.Gap.RequiredWork)
                .Distinct());
    }

    [Fact]
    public void SignatureNodeLimitPublishesNoPartialSuccess()
    {
        SyntheticParticipant participant = CreateSynthetic(new()
        {
            CallCount = 2,
        });

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(
                participant,
                new DirectCallDefinitionResolutionLimits(
                    maxSignatureNodes: 6));

        Assert.Equal(2, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(result);
                Assert.Equal(
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    incomplete.Gap.WorkDimension);
                Assert.Equal(
                    incomplete.PhysicalInvocation,
                    incomplete.Gap.PhysicalInvocation);
            });
        Assert.NotEqual(
            completed.Results[0].PhysicalInvocation,
            completed.Results[1].PhysicalInvocation);
    }

    [Fact]
    public void UnsupportedSignatureBeforeValidCallStillFailsBatchClosed()
    {
        SyntheticParticipant unsupported = CreateSynthetic(new()
        {
            AssemblyName = "UnsupportedBeforeBudgetLimit",
            TargetSignature = [0x00, 0x00, 0x1D, 0x01],
            TargetReturnValue = SyntheticStackValue.Null,
            PopCallReturn = true,
        });
        SyntheticParticipant valid = CreateSynthetic(new()
        {
            AssemblyName = "ValidAfterBudgetLimit",
        });
        var policy = new ExactPolicy(
        [
            unsupported.Participant.Assembly,
            valid.Participant.Assembly,
            CoreLibraryAssembly,
        ]);

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    policy,
                    [
                        unsupported.Participant,
                        valid.Participant,
                    ],
                    new DirectCallDefinitionResolutionLimits(
                        maxSignatureNodes: 4),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(result);
                Assert.Equal(
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                    incomplete.Gap.Kind);
                Assert.Equal(
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    incomplete.Gap.WorkDimension);
            });
    }

    [Fact]
    public void MalformedWrapperCannotHideSignatureNodesFromBatchLimit()
    {
        SyntheticParticipant malformed = CreateSynthetic(new()
        {
            AssemblyName = "MalformedHiddenSignatureNodes",
            AddGenericArgumentType = true,
            GenericArgumentTypeParameterRows = 1_000,
            TargetSignature =
                MalformedArrayByRefGenericSignature(1_000),
            TargetReturnValue = SyntheticStackValue.Null,
            PopCallReturn = true,
        });
        SyntheticParticipant valid = CreateSynthetic(new()
        {
            AssemblyName = "ValidAfterHiddenSignatureNodes",
        });
        var policy = new ExactPolicy(
        [
            malformed.Participant.Assembly,
            valid.Participant.Assembly,
            CoreLibraryAssembly,
        ]);

        DirectCallDefinitionResolutionOutcome.Completed completed =
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    policy,
                    [
                        malformed.Participant,
                        valid.Participant,
                    ],
                    new DirectCallDefinitionResolutionLimits(
                        maxSignatureNodes: 100),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, completed.Results.Length);
        Assert.All(
            completed.Results,
            result =>
            {
                DirectCallDefinitionResolution.Incomplete incomplete =
                    Assert.IsType<
                        DirectCallDefinitionResolution.Incomplete>(
                            result);
                Assert.Equal(
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                    incomplete.Gap.Kind);
                Assert.Equal(
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    incomplete.Gap.WorkDimension);
            });
    }

    [Fact]
    public void InterfaceCallResolvesOnlyItsStaticContractDefinition()
    {
        SyntheticParticipant participant = CreateInterfaceParticipant();
        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(participant);
        DirectCallDefinitionResolution.Resolved interfaceCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                completed.Results[0]);
        DirectCallDefinitionResolution.Resolved concreteCall =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                completed.Results[1]);

        Assert.True(interfaceCall.Definition.IsInterfaceDefinition);
        Assert.Equal(
            "IContract",
            interfaceCall.Definition.Member.DeclaringType.Name);
        Assert.False(interfaceCall.Call.ExactTarget);
        Assert.False(concreteCall.Definition.IsInterfaceDefinition);
        Assert.Equal(
            "Contract",
            concreteCall.Definition.Member.DeclaringType.Name);
        Assert.NotEqual(
            interfaceCall.Definition.MetadataToken,
            concreteCall.Definition.MetadataToken);

        Assembly loaded = Assembly.Load(participant.Image);
        Type loadedContract = loaded.GetType(
            "N.IContract",
            throwOnError: true)!;
        Assert.True(loadedContract.IsInterface);
        Assert.True(
            loadedContract.GetMethod("Target")!.IsAbstract);
    }

    [Fact]
    public void InvalidParticipantBatchesAreRejectedBeforeResolution()
    {
        SyntheticParticipant first = CreateSynthetic(new()
        {
            AssemblyName = "BatchFirst",
        });
        SyntheticParticipant second = CreateSynthetic(new()
        {
            AssemblyName = "BatchSecond",
        });

        Assert.Equal(
            DirectCallDefinitionRejectionKind.EmptyPopulation,
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Rejected>(
                    DirectCallDefinitionResolver.Resolve(
                        ExactPolicy.Empty,
                        [],
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Kind);
        Assert.Equal(
            DirectCallDefinitionRejectionKind.DuplicateParticipant,
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Rejected>(
                    DirectCallDefinitionResolver.Resolve(
                        first.Policy,
                        [first.Participant, first.Participant],
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Kind);

        LibraryBodyIndex noEvidence =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "no-evidence.dll",
                ImmutableArray.CreateRange(first.Image),
                LibraryBodyAnalysisFeatures.None);
        Assert.Equal(
            DirectCallDefinitionRejectionKind.MissingMethodEvidence,
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Rejected>(
                    DirectCallDefinitionResolver.Resolve(
                        first.Policy,
                        [new(noEvidence, first.Participant.Assembly)],
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Kind);
        Assert.Equal(
            DirectCallDefinitionRejectionKind
                .ParticipantSnapshotMismatch,
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Rejected>(
                    DirectCallDefinitionResolver.Resolve(
                        first.Policy,
                        [
                            new(
                                first.Participant.CallGraph,
                                second.Participant.Assembly),
                        ],
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Kind);

        bool unavailable = false;
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => unavailable
                    ? throw new IOException("unavailable")
                    : new MemoryStream(
                        first.Image,
                        writable: false),
                AssemblyResolutionProvenance.Local(
                    "unavailable participant test"))!;
        unavailable = true;
        Assert.Equal(
            DirectCallDefinitionRejectionKind.ParticipantUnavailable,
            Assert.IsType<
                DirectCallDefinitionResolutionOutcome.Rejected>(
                    DirectCallDefinitionResolver.Resolve(
                        first.Policy,
                        [new(first.Participant.CallGraph, descriptor)],
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Kind);
    }

    [Fact]
    public void DistinctRegistrationsOfOneImageRemainSeparateOccurrences()
    {
        SyntheticParticipant first = CreateSynthetic(new()
        {
            AssemblyName = "RepeatedPhysicalImage",
        });
        ResolvedAssemblyReference secondAssembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(first.Image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "second direct-call acquisition"))!;
        LibraryBodyIndex secondIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RepeatedPhysicalImage-second.dll",
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

        Assert.Equal(2, completed.Results.Length);
        Assert.NotEqual(
            completed.Results[0].PhysicalInvocation,
            completed.Results[1].PhysicalInvocation);
        Assert.Same(
            first.Participant,
            completed.Results[0].Participant);
        Assert.Same(second, completed.Results[1].Participant);
        DirectCallDefinitionResolution.Resolved firstResolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                completed.Results[0]);
        DirectCallDefinitionResolution.Resolved secondResolved =
            Assert.IsType<DirectCallDefinitionResolution.Resolved>(
                completed.Results[1]);
        Assert.Same(
            first.Participant.Assembly.Registration,
            firstResolved.Definition.Registration);
        Assert.Same(
            second.Assembly.Registration,
            secondResolved.Definition.Registration);
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        ResolveOwnershipFixture(
            DirectCallDefinitionResolutionLimits? limits = null)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            OwnershipFixturePath,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                OwnershipFixturePath,
                AssemblyResolutionProvenance.Local(
                    "direct-call definition test"));
        return Assert.IsType<
            DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            OwnershipFixturePath)),
                    [new CatalogCallGraphParticipant(index, assembly)],
                    limits,
                    cancellationToken:
                        TestContext.Current.CancellationToken));
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        ResolveOwnershipFixture(ImmutableArray<byte> image)
    {
        byte[] bytes = image.ToArray();
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedOwnershipFlowFixtures.dll",
                image,
                LibraryBodyAnalysisFeatures.MethodEvidence);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local(
                    "malformed direct-call definition test"))!;
        return Assert.IsType<
            DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            OwnershipFixturePath)),
                    [new CatalogCallGraphParticipant(index, assembly)],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
    }

    static DirectCallDefinitionResolutionOutcome.Completed Resolve(
        SyntheticParticipant participant,
        DirectCallDefinitionResolutionLimits? limits = null) =>
        Assert.IsType<
            DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    participant.Policy,
                    [participant.Participant],
                    limits,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

    static DirectCallDefinitionResolutionOutcome.Completed Resolve(
        ExternalSyntheticParticipant participant,
        IAssemblyBindingPolicy? policy = null) =>
        Assert.IsType<
            DirectCallDefinitionResolutionOutcome.Completed>(
                DirectCallDefinitionResolver.Resolve(
                    policy ?? participant.Policy,
                    [participant.Participant],
                    cancellationToken:
                        TestContext.Current.CancellationToken));

    static void AssertLimit(
        SyntheticParticipant participant,
        DirectCallDefinitionResolutionLimits limits,
        DirectCallDefinitionWorkDimension dimension,
        int resultIndex = 0)
    {
        DirectCallDefinitionResolutionOutcome.Completed completed =
            Resolve(participant, limits);
        DirectCallDefinitionResolution.Incomplete incomplete =
            Assert.IsType<DirectCallDefinitionResolution.Incomplete>(
                completed.Results[resultIndex]);

        Assert.Equal(dimension, incomplete.Gap.WorkDimension);
        Assert.NotNull(incomplete.Gap.Limit);
        Assert.True(
            incomplete.Gap.RequiredWork > incomplete.Gap.Limit);
        Assert.Equal(
            incomplete.PhysicalInvocation,
            incomplete.Gap.PhysicalInvocation);
        Assert.Equal(incomplete.Call.Kind, incomplete.Gap.CallKind);
    }

    static void AssertCallerExecutes(SyntheticParticipant participant)
    {
        Assembly assembly = Assembly.Load(participant.Image);
        Type owner = assembly.GetType(
            "N.Owner",
            throwOnError: true)!;
        MethodInfo caller = owner.GetMethod(
            "Caller",
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.Null(caller.Invoke(null, null));
    }

    static ExternalSyntheticParticipant CreateExternalSynthetic(
        ExternalSyntheticOptions options)
    {
        const string TargetAssemblyName = "ExternalDirectCallTarget";
        var targetMetadata = new MetadataBuilder();
        targetMetadata.AddModule(
            0,
            targetMetadata.GetOrAddString(
                TargetAssemblyName + ".dll"),
            targetMetadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        targetMetadata.AddAssembly(
            targetMetadata.GetOrAddString(TargetAssemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);
        targetMetadata.AddTypeDefinition(
            default,
            default,
            targetMetadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle targetType =
            targetMetadata.AddTypeDefinition(
                TypeAttributes.Public,
                targetMetadata.GetOrAddString("N"),
                targetMetadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        var targetBodies = new BlobBuilder();
        var targetBodyEncoder =
            new MethodBodyStreamEncoder(targetBodies);
        var targetIl = new BlobBuilder();
        var targetInstructions = new InstructionEncoder(targetIl);
        EmitValue(targetInstructions, options.TargetReturnValue);
        targetInstructions.OpCode(ILOpCode.Ret);
        int targetBody = targetBodyEncoder.AddMethodBody(
            targetInstructions);
        MethodDefinitionHandle firstTarget = default;
        for (int index = 0;
            index < options.ExactTargetCount;
            index++)
        {
            MethodDefinitionHandle target =
                targetMetadata.AddMethodDefinition(
                    options.TargetAttributes,
                    MethodImplAttributes.IL,
                    targetMetadata.GetOrAddString(
                        options.TargetName),
                    targetMetadata.GetOrAddBlob(
                        options.TargetSignature),
                    targetBody,
                    MetadataTokens.ParameterHandle(1));
            if (index == 0)
                firstTarget = target;
        }
        for (int index = 0;
            index < options.TargetMethodGenericParameterRows;
            index++)
        {
            targetMetadata.AddGenericParameter(
                firstTarget,
                GenericParameterAttributes.None,
                targetMetadata.GetOrAddString($"M{index}"),
                index);
        }
        if (options.AddUnreadableTarget)
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
            targetMetadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                targetMetadata.GetOrAddString("Target"),
                targetMetadata.GetOrAddBlob(unreadable),
                targetBody,
                MetadataTokens.ParameterHandle(1));
        }
        byte[] targetImage = Serialize(
            targetMetadata,
            targetBodies);
        ResolvedAssemblyReference targetAssembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(
                    targetImage,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "external direct-call target"))!;

        const string CallerAssemblyName = "ExternalDirectCallCaller";
        var callerMetadata = new MetadataBuilder();
        callerMetadata.AddModule(
            0,
            callerMetadata.GetOrAddString(
                CallerAssemblyName + ".dll"),
            callerMetadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        callerMetadata.AddAssembly(
            callerMetadata.GetOrAddString(CallerAssemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle targetReference =
            callerMetadata.AddAssemblyReference(
                callerMetadata.GetOrAddString(TargetAssemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle targetTypeReference =
            callerMetadata.AddTypeReference(
                targetReference,
                callerMetadata.GetOrAddString("N"),
                callerMetadata.GetOrAddString("Target"));
        MemberReferenceHandle memberReference =
            callerMetadata.AddMemberReference(
                targetTypeReference,
                callerMetadata.GetOrAddString(
                    options.TargetName),
                callerMetadata.GetOrAddBlob(
                    options.MemberReferenceSignature));
        EntityHandle callTarget = memberReference;
        if (options.MethodSpecificationSignature is { } specification)
        {
            callTarget = callerMetadata.AddMethodSpecification(
                memberReference,
                callerMetadata.GetOrAddBlob(specification));
        }
        callerMetadata.AddTypeDefinition(
            default,
            default,
            callerMetadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        callerMetadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            callerMetadata.GetOrAddString("N"),
            callerMetadata.GetOrAddString("Calls"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var callerBodies = new BlobBuilder();
        var callerIl = new BlobBuilder();
        var callerInstructions = new InstructionEncoder(
            callerIl,
            new ControlFlowBuilder());
        foreach (SyntheticStackValue value
            in options.CallArgumentKinds)
        {
            EmitValue(callerInstructions, value);
        }
        callerInstructions.Call(callTarget);
        if (options.PopCallReturn)
            callerInstructions.OpCode(ILOpCode.Pop);
        callerInstructions.OpCode(ILOpCode.Ret);
        int callerBody = new MethodBodyStreamEncoder(callerBodies)
            .AddMethodBody(callerInstructions, maxStack: 8);
        MethodDefinitionHandle caller =
            callerMetadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                callerMetadata.GetOrAddString("Call"),
                callerMetadata.GetOrAddBlob(
                    options.CallerMethodGenericParameterRows == 0
                        ? new byte[] { 0x00, 0x00, 0x01 }
                        :
                        [
                            0x10,
                            (byte)options
                                .CallerMethodGenericParameterRows,
                            0x00,
                            0x01,
                        ]),
                callerBody,
                MetadataTokens.ParameterHandle(1));
        for (int index = 0;
            index < options.CallerMethodGenericParameterRows;
            index++)
        {
            callerMetadata.AddGenericParameter(
                caller,
                GenericParameterAttributes.None,
                callerMetadata.GetOrAddString($"C{index}"),
                index);
        }
        byte[] callerImage = Serialize(
            callerMetadata,
            callerBodies);
        ResolvedAssemblyReference callerAssembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(
                    callerImage,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "external direct-call caller"))!;
        LibraryBodyIndex bodyIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                CallerAssemblyName + ".dll",
                ImmutableArray.CreateRange(callerImage),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        var participant =
            new CatalogCallGraphParticipant(bodyIndex, callerAssembly);
        return new(
            targetImage,
            targetAssembly,
            participant,
            new ExactPolicy(
                [
                    callerAssembly,
                    targetAssembly,
                    CoreLibraryAssembly,
                ]));
    }

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static SyntheticParticipant CreateSynthetic(
        SyntheticOptions options)
    {
        var metadata = new MetadataBuilder();
        Guid mvid = Guid.NewGuid();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(options.AssemblyName + ".dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(options.AssemblyName),
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
                options.OwnerTypeName
                    ?? (options.TypeGenericParameterRows == 0
                        ? "Owner"
                        : $"Owner`{options.TypeGenericParameterRows}")),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        int callerMethodRow = options.TargetCount + 1;
        TypeDefinitionHandle callerOwner = owner;
        if (options.SeparateCallerType)
        {
            callerOwner = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    options.CallerTypeGenericParameterRows == 0
                        ? "CallerOwner"
                        : $"CallerOwner`"
                            + options.CallerTypeGenericParameterRows),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    callerMethodRow));
        }
        TypeDefinitionHandle genericArgumentType = default;
        if (options.AddGenericArgumentType)
        {
            genericArgumentType = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    options.GenericArgumentTypeName ?? "Box`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    options.TargetCount + 2));
        }
        if (options.AddConstructedModifierTypeSpecification)
        {
            var modifier = new BlobBuilder();
            modifier.WriteByte(0x15);
            modifier.WriteByte(0x12);
            modifier.WriteByte(0x0C);
            modifier.WriteCompressedInteger(
                options.ConstructedModifierArgumentCount);
            modifier.WriteByte(0x1E);
            modifier.WriteByte(0x00);
            for (int index = 1;
                index < options.ConstructedModifierArgumentCount;
                index++)
            {
                modifier.WriteByte(0x08);
            }
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(modifier));
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int TargetBody()
        {
            var il = new BlobBuilder();
            var instructions = new InstructionEncoder(il);
            EmitValue(instructions, options.TargetReturnValue);
            instructions.OpCode(ILOpCode.Ret);
            return bodyEncoder.AddMethodBody(
                instructions);
        }

        MethodDefinitionHandle firstTarget = default;
        for (int index = 0; index < options.TargetCount; index++)
        {
            MethodDefinitionHandle target =
                metadata.AddMethodDefinition(
                    options.TargetAttributes,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(options.TargetName),
                    metadata.GetOrAddBlob(options.TargetSignature),
                    TargetBody(),
                    MetadataTokens.ParameterHandle(1));
            if (index == 0)
                firstTarget = target;
        }
        EntityHandle callTarget = firstTarget;
        if (options.InstantiateGenericMethod)
        {
            callTarget = metadata.AddMethodSpecification(
                firstTarget,
                metadata.GetOrAddBlob(
                    options.MethodSpecSignature
                        ?? [0x0A, 0x01, 0x08]));
        }
        if (options.MissingDeclaringType)
        {
            AssemblyReferenceHandle missingAssembly =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Missing"),
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
            TypeReferenceHandle missingType =
                metadata.AddTypeReference(
                    missingAssembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Owner"));
            callTarget = metadata.AddMemberReference(
                missingType,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x01 }));
        }
        else if (options.CallViaMemberReference)
        {
            callTarget = metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString(
                    options.CallName ?? options.TargetName),
                metadata.GetOrAddBlob(options.TargetSignature));
        }
        else if (options.UnsupportedDeclaringType)
        {
            TypeSpecificationHandle declaringType =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x1B,
                            0x00,
                            0x00,
                            0x01,
                        }));
            callTarget = metadata.AddMemberReference(
                declaringType,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(options.TargetSignature));
        }
        else if (options.CallViaMalformedGenericDeclaringType)
        {
            TypeSpecificationHandle declaringType =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x15,
                        0x12,
                        0x08,
                        0x01,
                        0x13,
                        0x01,
                    }));
            callTarget = metadata.AddMemberReference(
                declaringType,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(options.TargetSignature));
        }

        var callerIl = new BlobBuilder();
        var callerInstructions = new InstructionEncoder(
            callerIl,
            new ControlFlowBuilder());
        for (int index = 0; index < options.CallCount; index++)
        {
            if (options.CallKind == CallKind.CallVirtual)
                callerInstructions.OpCode(ILOpCode.Ldnull);
            foreach (SyntheticStackValue value
                in options.CallArgumentKinds)
            {
                EmitValue(callerInstructions, value);
            }
            callerInstructions.OpCode(options.CallKind switch
            {
                CallKind.Call => ILOpCode.Call,
                CallKind.CallVirtual => ILOpCode.Callvirt,
                CallKind.NewObject => ILOpCode.Newobj,
                _ => throw new InvalidOperationException(
                    "Synthetic calls must use a token-bearing call opcode."),
            });
            callerInstructions.Token(callTarget);
            if (options.PopCallReturn)
                callerInstructions.OpCode(ILOpCode.Pop);
        }
        callerInstructions.OpCode(ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            callerInstructions,
            maxStack: 8);
        MethodDefinitionHandle caller =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Caller"),
                metadata.GetOrAddBlob(
                    options.CallerMethodGenericParameterRows == 0
                        ? new byte[] { 0x00, 0x00, 0x01 }
                        : [0x10, 0x01, 0x00, 0x01]),
                callerBody,
                MetadataTokens.ParameterHandle(1));
        var genericParameterOwners = new List<(
            EntityHandle Owner,
            int Count,
            int Start,
            string Prefix)>
        {
            (
                firstTarget,
                options.MethodGenericParameterRows,
                options.MethodGenericParameterStartIndex,
                "M"),
            (
                owner,
                options.TypeGenericParameterRows,
                options.TypeGenericParameterStartIndex,
                "T"),
            (
                caller,
                options.CallerMethodGenericParameterRows,
                options.CallerMethodGenericParameterStartIndex,
                "C"),
            (
                callerOwner,
                options.CallerTypeGenericParameterRows,
                options.CallerTypeGenericParameterStartIndex,
                "CT"),
        };
        if (!genericArgumentType.IsNil)
        {
            genericParameterOwners.Add((
                genericArgumentType,
                options.GenericArgumentTypeParameterRows,
                0,
                "G"));
        }
        foreach (var parameterOwner
            in genericParameterOwners
                .Where(item => item.Count > 0)
                .OrderBy(item => item.Owner.Kind
                    == HandleKind.TypeDefinition
                        ? MetadataTokens.GetRowNumber(item.Owner) * 2
                        : MetadataTokens.GetRowNumber(item.Owner) * 2 + 1))
        {
            for (int index = 0;
                index < parameterOwner.Count;
                index++)
            {
                metadata.AddGenericParameter(
                    parameterOwner.Owner,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString(
                        $"{parameterOwner.Prefix}{index}"),
                    parameterOwner.Start + index);
            }
        }

        if (options.PropertySemantics is not null)
        {
            PropertyDefinitionHandle property =
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(
                        options.PropertySignature
                            ?? [0x08, 0x00, 0x08]));
            metadata.AddPropertyMap(owner, property);
            foreach (MethodSemanticsAttributes semantics
                in options.PropertySemantics)
            {
                metadata.AddMethodSemantics(
                    property,
                    semantics,
                    firstTarget);
            }
        }
        if (options.EventSemantics is not null)
        {
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Changed"),
                objectType);
            metadata.AddEventMap(owner, @event);
            foreach (MethodSemanticsAttributes semantics
                in options.EventSemantics)
            {
                metadata.AddMethodSemantics(
                    @event,
                    semantics,
                    firstTarget);
            }
        }

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
                    "synthetic direct-call definition test"))!;
        LibraryBodyIndex bodyIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                options.AssemblyName + ".dll",
                ImmutableArray.CreateRange(image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        var policy = new ExactPolicy(
            [assembly, CoreLibraryAssembly]);
        return new SyntheticParticipant(
            image,
            new CatalogCallGraphParticipant(bodyIndex, assembly),
            policy);
    }

    static void EmitValue(
        InstructionEncoder instructions,
        SyntheticStackValue value)
    {
        switch (value)
        {
            case SyntheticStackValue.None:
                return;
            case SyntheticStackValue.Int32:
                instructions.OpCode(ILOpCode.Ldc_i4_0);
                return;
            case SyntheticStackValue.NativeInt:
                instructions.OpCode(ILOpCode.Ldc_i4_0);
                instructions.OpCode(ILOpCode.Conv_i);
                return;
            case SyntheticStackValue.Null:
                instructions.OpCode(ILOpCode.Ldnull);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    static SyntheticParticipant CreateInterfaceParticipant(
        bool explicitImplementation = false,
        bool addPublicDecoy = false,
        bool addNonImplementer = false,
        bool addUnrelatedInterface = false,
        bool addDuplicateMethodImpl = false,
        bool methodImplBodyAsMemberReference = false,
        bool unresolvedInterfaceImpl = false,
        bool generic = false,
        bool fixedGenericInterface = false,
        bool addStringImplementationCall = false,
        bool includeInterfaceCall = true,
        bool methodGeneric = false,
        bool callerGenericTypeArgument = false,
        bool addSwappedGenericDecoy = false,
        bool malformedInterfaceImpl = false,
        bool malformedMethodImpl = false,
        bool invalidOnlyInterfaceImpl = false,
        bool wrappedTypeParameter = false,
        string assemblyName = "InterfaceDirectCalls",
        Version? assemblyVersion = null,
        byte[]? assemblyPublicKey = null,
        bool staticExplicitImplementation = false,
        string decoyMethodName = "Target",
        string nonImplementerMethodName = "Target")
    {
        assemblyVersion ??= new Version(1, 0, 0, 0);
        addPublicDecoy |= addSwappedGenericDecoy;
        bool genericInterface = generic || fixedGenericInterface;
        bool genericImplementation = generic;
        var metadata = new MetadataBuilder();
        var genericParameters = new List<(EntityHandle Owner, string Name)>();
        Guid mvid = Guid.NewGuid();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            assemblyVersion,
            default,
            assemblyPublicKey is null
                ? default
                : metadata.GetOrAddBlob(assemblyPublicKey),
            assemblyPublicKey is null
                ? default
                : AssemblyFlags.PublicKey,
            AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                default,
                default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle wrapper = default;
        if (wrappedTypeParameter)
        {
            wrapper = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Box`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            genericParameters.Add((wrapper, "T"));
        }
        TypeDefinitionHandle contract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    fixedGenericInterface
                        ? "IFixed`1"
                        : genericInterface
                            ? "IContract`1"
                            : "IContract"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle unrelatedInterface = default;
        if (addUnrelatedInterface)
        {
            unrelatedInterface = metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("IOther"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        }
        TypeDefinitionHandle implementation =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    fixedGenericInterface
                        ? "Fixed"
                        : genericImplementation
                            ? "Contract`1"
                            : "Contract"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        int implementationMethodCount =
            1 + (addPublicDecoy ? 1 : 0);
        if (addNonImplementer)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Lookalike"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    2 + implementationMethodCount));
        }
        TypeDefinitionHandle callsType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Calls"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    2
                    + implementationMethodCount
                    + (addNonImplementer ? 1 : 0)
                    + (staticExplicitImplementation ? 1 : 0)));
        if (invalidOnlyInterfaceImpl)
        {
            metadata.AddInterfaceImplementation(
                implementation,
                MetadataTokens.TypeSpecificationHandle(9999));
        }
        else if (unresolvedInterfaceImpl)
        {
            AssemblyReferenceHandle missing =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Missing.Interface"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle missingContract =
                metadata.AddTypeReference(
                    missing,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("IContract"));
            metadata.AddInterfaceImplementation(
                implementation,
                missingContract);
        }
        else if (genericImplementation)
        {
            genericParameters.Add((contract, "T"));
            genericParameters.Add((implementation, "T"));
            TypeSpecificationHandle openContract =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        GenericInstanceSignature(
                            contract,
                            [0x13, 0x00])));
            metadata.AddInterfaceImplementation(
                implementation,
                openContract);
        }
        else if (fixedGenericInterface)
        {
            genericParameters.Add((contract, "T"));
            TypeSpecificationHandle closedContract =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        GenericInstanceSignature(
                            contract,
                            [0x08])));
            metadata.AddInterfaceImplementation(
                implementation,
                closedContract);
        }
        else
        {
            metadata.AddInterfaceImplementation(
                implementation,
                contract);
        }
        if (malformedInterfaceImpl)
        {
            metadata.AddInterfaceImplementation(
                implementation,
                MetadataTokens.TypeSpecificationHandle(9999));
        }
        if (!unrelatedInterface.IsNil)
        {
            metadata.AddInterfaceImplementation(
                implementation,
                unrelatedInterface);
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var implementationIl = new BlobBuilder();
        implementationIl.WriteByte((byte)ILOpCode.Ret);
        int implementationBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(implementationIl));
        byte[] interfaceTypeParameter = wrappedTypeParameter
            ? GenericInstanceSignature(wrapper, [0x13, 0x00])
            : [0x13, 0x00];
        byte[] interfaceMethodSignature = staticExplicitImplementation
            ? [0x00, 0x00, 0x01]
            : methodGeneric
            ? genericInterface
                ? [0x30, 0x01, 0x02, 0x01, .. interfaceTypeParameter, 0x1E, 0x00]
                : [0x30, 0x01, 0x01, 0x01, 0x1E, 0x00]
            : genericInterface
                ? [0x20, 0x01, 0x01, .. interfaceTypeParameter]
                : [0x20, 0x00, 0x01];
        byte[] implementationMethodSignature = staticExplicitImplementation
            ? [0x00, 0x00, 0x01]
            : methodGeneric
            ? genericImplementation
                ? [0x30, 0x01, 0x02, 0x01, .. interfaceTypeParameter, 0x1E, 0x00]
                : fixedGenericInterface
                    ? [0x30, 0x01, 0x02, 0x01, 0x08, 0x1E, 0x00]
                    : [0x30, 0x01, 0x01, 0x01, 0x1E, 0x00]
            : genericImplementation
                ? [0x20, 0x01, 0x01, .. interfaceTypeParameter]
                : fixedGenericInterface
                    ? [0x20, 0x01, 0x01, 0x08]
                    : [0x20, 0x00, 0x01];
        MethodDefinitionHandle interfaceMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | (staticExplicitImplementation
                        ? MethodAttributes.Static
                        : 0),
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(interfaceMethodSignature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle implementationMethod =
            metadata.AddMethodDefinition(
                (explicitImplementation
                    ? MethodAttributes.Private
                    : MethodAttributes.Public)
                    | (staticExplicitImplementation
                        ? MethodAttributes.Static
                        : MethodAttributes.Final
                            | MethodAttributes.Virtual),
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    explicitImplementation
                        ? "ExplicitTarget"
                        : "Target"),
                metadata.GetOrAddBlob(implementationMethodSignature),
                implementationBody,
                MetadataTokens.ParameterHandle(1));
        if (methodGeneric)
        {
            genericParameters.Add((interfaceMethod, "U"));
            genericParameters.Add((implementationMethod, "U"));
        }
        MethodDefinitionHandle decoyMethod = default;
        if (addPublicDecoy)
        {
            decoyMethod = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(decoyMethodName),
                metadata.GetOrAddBlob(addSwappedGenericDecoy
                    ? new byte[] { 0x30, 0x01, 0x02, 0x01, 0x1E, 0x00, 0x13, 0x00 }
                    : implementationMethodSignature),
                implementationBody,
                MetadataTokens.ParameterHandle(1));
            if (methodGeneric)
                genericParameters.Add((decoyMethod, "U"));
        }
        if (explicitImplementation)
        {
            EntityHandle methodBody = implementationMethod;
            if (methodImplBodyAsMemberReference)
            {
                methodBody = metadata.AddMemberReference(
                    implementation,
                    metadata.GetOrAddString("ExplicitTarget"),
                    metadata.GetOrAddBlob(
                        implementationMethodSignature));
            }
            metadata.AddMethodImplementation(
                implementation,
                methodBody,
                interfaceMethod);
            if (addDuplicateMethodImpl)
            {
                metadata.AddMethodImplementation(
                    implementation,
                    implementationMethod,
                    interfaceMethod);
            }
        }
        if (malformedMethodImpl)
        {
            metadata.AddMethodImplementation(
                implementation,
                implementationMethod,
                MetadataTokens.MemberReferenceHandle(9999));
        }
        MethodDefinitionHandle nonImplementerMethod = default;
        if (addNonImplementer)
        {
            nonImplementerMethod = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(nonImplementerMethodName),
                metadata.GetOrAddBlob(implementationMethodSignature),
                implementationBody,
                MetadataTokens.ParameterHandle(1));
        }
        var callerIl = new BlobBuilder();
        var callerInstructions = new InstructionEncoder(
            callerIl,
            new ControlFlowBuilder());
        EntityHandle interfaceCallTarget = interfaceMethod;
        EntityHandle implementationCallTarget = implementationMethod;
        EntityHandle decoyCallTarget = decoyMethod;
        EntityHandle nonImplementerCallTarget = nonImplementerMethod;
        EntityHandle stringImplementationCallTarget = default;
        if (genericInterface)
        {
            TypeSpecificationHandle closedContract =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        GenericInstanceSignature(
                            contract,
                            callerGenericTypeArgument ? [0x1E, 0x00] : [0x08])));
            BlobHandle constructedSignature =
                metadata.GetOrAddBlob(interfaceMethodSignature);
            interfaceCallTarget = metadata.AddMemberReference(
                closedContract,
                metadata.GetOrAddString("Target"),
                constructedSignature);
            if (genericImplementation)
            {
                TypeSpecificationHandle closedImplementation =
                    metadata.AddTypeSpecification(
                        metadata.GetOrAddBlob(
                            GenericInstanceSignature(
                                implementation,
                                callerGenericTypeArgument ? [0x1E, 0x00] : [0x08])));
            implementationCallTarget = metadata.AddMemberReference(
                closedImplementation,
                metadata.GetOrAddString(
                    explicitImplementation
                        ? "ExplicitTarget"
                        : "Target"),
                    metadata.GetOrAddBlob(
                        implementationMethodSignature));
            if (!decoyMethod.IsNil)
            {
                decoyCallTarget = metadata.AddMemberReference(
                    closedImplementation,
                    metadata.GetOrAddString(decoyMethodName),
                        metadata.GetOrAddBlob(
                            implementationMethodSignature));
                }
                if (addStringImplementationCall)
                {
                    TypeSpecificationHandle stringImplementation =
                        metadata.AddTypeSpecification(
                            metadata.GetOrAddBlob(
                                GenericInstanceSignature(
                                    implementation,
                                    [0x0E])));
                    stringImplementationCallTarget =
                        metadata.AddMemberReference(
                            stringImplementation,
                            metadata.GetOrAddString("Target"),
                            metadata.GetOrAddBlob(
                                implementationMethodSignature));
                }
            }
        }
        if (methodGeneric)
        {
            interfaceCallTarget = metadata.AddMethodSpecification(
                interfaceCallTarget,
                metadata.GetOrAddBlob(
                    new byte[] { 0x0A, 0x01, 0x08 }));
            implementationCallTarget =
                metadata.AddMethodSpecification(
                    implementationCallTarget,
                    metadata.GetOrAddBlob(
                        new byte[] { 0x0A, 0x01, 0x0E }));
        }
        if (includeInterfaceCall)
        {
            if (!staticExplicitImplementation)
                callerInstructions.OpCode(ILOpCode.Ldnull);
            if (genericInterface && methodGeneric)
            {
                callerInstructions.OpCode(
                    callerGenericTypeArgument
                        ? ILOpCode.Ldarg_0
                        : ILOpCode.Ldc_i4_0);
            }
            if (genericInterface || methodGeneric)
                callerInstructions.OpCode(ILOpCode.Ldc_i4_0);
            callerInstructions.OpCode(
                staticExplicitImplementation
                    ? ILOpCode.Call
                    : ILOpCode.Callvirt);
            callerInstructions.Token(interfaceCallTarget);
        }
        if (!staticExplicitImplementation)
            callerInstructions.OpCode(ILOpCode.Ldnull);
        if (genericImplementation || fixedGenericInterface)
            callerInstructions.OpCode(callerGenericTypeArgument ? ILOpCode.Ldarg_0 : ILOpCode.Ldc_i4_0);
        else if (methodGeneric)
            callerInstructions.OpCode(ILOpCode.Ldnull);
        if (genericImplementation && methodGeneric)
        callerInstructions.OpCode(ILOpCode.Ldnull);
        callerInstructions.OpCode(
            staticExplicitImplementation
                ? ILOpCode.Call
                : ILOpCode.Callvirt);
        callerInstructions.Token(implementationCallTarget);
        if (!stringImplementationCallTarget.IsNil)
        {
            callerInstructions.OpCode(ILOpCode.Ldnull);
            callerInstructions.OpCode(ILOpCode.Ldnull);
            callerInstructions.OpCode(ILOpCode.Callvirt);
            callerInstructions.Token(stringImplementationCallTarget);
        }
        if (!decoyMethod.IsNil && !addSwappedGenericDecoy)
        {
            callerInstructions.OpCode(ILOpCode.Ldnull);
            if (genericImplementation)
                callerInstructions.OpCode(ILOpCode.Ldc_i4_0);
            callerInstructions.OpCode(ILOpCode.Callvirt);
            callerInstructions.Token(decoyCallTarget);
        }
        if (!nonImplementerMethod.IsNil)
        {
            callerInstructions.OpCode(ILOpCode.Ldnull);
            callerInstructions.OpCode(ILOpCode.Callvirt);
            callerInstructions.Token(nonImplementerCallTarget);
        }
        callerInstructions.OpCode(ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            callerInstructions,
            maxStack:
                genericInterface && methodGeneric ? 3 : genericInterface
                || genericImplementation
                || methodGeneric
                    ? 2
                    : 1);
        MethodDefinitionHandle caller =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Caller"),
                metadata.GetOrAddBlob(
                    callerGenericTypeArgument
                        ? new byte[] { 0x10, 0x01, 0x01, 0x01, 0x1E, 0x00 }
                        : new byte[] { 0x00, 0x00, 0x01 }),
                callerBody,
                MetadataTokens.ParameterHandle(1));
        if (callerGenericTypeArgument)
            genericParameters.Add((caller, "V"));
        foreach (var parameter in genericParameters.OrderBy(
            parameter => CodedIndex.TypeOrMethodDef(parameter.Owner)))
        {
            metadata.AddGenericParameter(parameter.Owner, GenericParameterAttributes.None,
                metadata.GetOrAddString(parameter.Name), 0);
        }

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
                    "interface direct-call definition test"))!;
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                assemblyName + ".dll",
                ImmutableArray.CreateRange(image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        return new SyntheticParticipant(
            image,
            new CatalogCallGraphParticipant(index, assembly),
            new ExactPolicy([assembly, CoreLibraryAssembly]));
    }

    static SyntheticParticipant CreateVersionSplitInterfaceCaller(
        SyntheticParticipant versionTwo)
    {
        string assemblyName =
            versionTwo.Participant.Assembly.Identity.Name;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle versionTwoReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    versionTwo.Participant.Assembly.Identity.Name),
                versionTwo.Participant.Assembly.Identity.Version
                    ?? throw new InvalidOperationException(
                        "Version-two fixture identity is unversioned."),
                default,
                versionTwo.Participant.Assembly.Identity.PublicKeyToken
                    is string publicKeyToken
                    ? metadata.GetOrAddBlob(
                        Convert.FromHexString(publicKeyToken))
                    : default,
                default,
                default);
        TypeReferenceHandle implementationType =
            metadata.AddTypeReference(
                versionTwoReference,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Implementation"));
        BlobHandle instanceVoidSignature =
            metadata.GetOrAddBlob(
                new byte[] { 0x20, 0x00, 0x01 });
        MemberReferenceHandle implementationTarget =
            metadata.AddMemberReference(
                implementationType,
                metadata.GetOrAddString("ExplicitTarget"),
                instanceVoidSignature);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("IContract"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Calls"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        var bodies = new BlobBuilder();
        var targetIl = new BlobBuilder();
        var targetInstructions = new InstructionEncoder(targetIl);
        targetInstructions.OpCode(ILOpCode.Ret);
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int targetBody =
            bodyEncoder.AddMethodBody(targetInstructions);
        MethodDefinitionHandle classTarget =
            metadata.AddMethodDefinition(
                MethodAttributes.Public,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Target"),
                instanceVoidSignature,
                targetBody,
                MetadataTokens.ParameterHandle(1));
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(
            il,
            new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldnull);
        instructions.OpCode(ILOpCode.Callvirt);
        instructions.Token(classTarget);
        instructions.OpCode(ILOpCode.Ldnull);
        instructions.OpCode(ILOpCode.Callvirt);
        instructions.Token(implementationTarget);
        instructions.OpCode(ILOpCode.Ret);
        int body = bodyEncoder
            .AddMethodBody(instructions, maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            body,
            MetadataTokens.ParameterHandle(1));

        byte[] image = Serialize(metadata, bodies);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "version-split interface caller"))!;
        LibraryBodyIndex bodyIndex =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                assemblyName + ".dll",
                ImmutableArray.CreateRange(image),
                LibraryBodyAnalysisFeatures.MethodEvidence);
        return new(
            image,
            new CatalogCallGraphParticipant(
                bodyIndex,
                assembly),
            new ExactPolicy(
                [
                    assembly,
                    versionTwo.Participant.Assembly,
                    CoreLibraryAssembly,
                ]));
    }

    static byte[] GenericInstanceSignature(
        TypeDefinitionHandle genericType,
        byte[] argumentSignature)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericType) << 2);
        signature.WriteCompressedInteger(1);
        signature.WriteBytes(argumentSignature);
        return signature.ToArray();
    }

    static ResolvedAssemblyReference CoreLibraryAssembly { get; } =
        ResolvedAssemblyReference.CreateFromPath(
            typeof(object).Assembly.Location,
            AssemblyResolutionProvenance.Local(
                "direct-call definition core library"));

    sealed record SyntheticParticipant(
        byte[] Image,
        CatalogCallGraphParticipant Participant,
        IAssemblyBindingPolicy Policy);

    sealed record ExternalSyntheticParticipant(
        byte[] TargetImage,
        ResolvedAssemblyReference TargetAssembly,
        CatalogCallGraphParticipant Participant,
        IAssemblyBindingPolicy Policy);

    sealed record ExternalSyntheticOptions
    {
        internal string TargetName { get; init; } = "Target";
        internal MethodAttributes TargetAttributes { get; init; } =
            MethodAttributes.Public | MethodAttributes.Static;
        internal byte[] TargetSignature { get; init; } =
            [0x00, 0x00, 0x01];
        internal byte[] MemberReferenceSignature { get; init; } =
            [0x00, 0x00, 0x01];
        internal byte[]? MethodSpecificationSignature { get; init; }
        internal int TargetMethodGenericParameterRows { get; init; }
        internal int CallerMethodGenericParameterRows { get; init; }
        internal int ExactTargetCount { get; init; } = 1;
        internal bool AddUnreadableTarget { get; init; }
        internal SyntheticStackValue TargetReturnValue { get; init; }
        internal SyntheticStackValue[] CallArgumentKinds { get; init; } =
            [];
        internal bool PopCallReturn { get; init; }
    }

    sealed record SyntheticOptions
    {
        internal string AssemblyName { get; init; } =
            "SyntheticDirectCalls";
        internal MethodAttributes TargetAttributes { get; init; } =
            MethodAttributes.Public | MethodAttributes.Static;
        internal string TargetName { get; init; } = "Target";
        internal byte[] TargetSignature { get; init; } =
            [0x00, 0x00, 0x01];
        internal int TargetCount { get; init; } = 1;
        internal int CallCount { get; init; } = 1;
        internal CallKind CallKind { get; init; } = CallKind.Call;
        internal string? CallName { get; init; }
        internal bool UnsupportedDeclaringType { get; init; }
        internal bool MissingDeclaringType { get; init; }
        internal bool CallViaMemberReference { get; init; }
        internal bool CallViaMalformedGenericDeclaringType { get; init; }
        internal int MethodGenericParameterRows { get; init; }
        internal int MethodGenericParameterStartIndex { get; init; }
        internal bool InstantiateGenericMethod { get; init; }
        internal byte[]? MethodSpecSignature { get; init; }
        internal int CallerMethodGenericParameterRows { get; init; }
        internal int CallerMethodGenericParameterStartIndex { get; init; }
        internal int TypeGenericParameterRows { get; init; }
        internal int TypeGenericParameterStartIndex { get; init; }
        internal string? OwnerTypeName { get; init; }
        internal bool SeparateCallerType { get; init; }
        internal int CallerTypeGenericParameterRows { get; init; }
        internal int CallerTypeGenericParameterStartIndex { get; init; }
        internal bool AddGenericArgumentType { get; init; }
        internal int GenericArgumentTypeParameterRows { get; init; } =
            1;
        internal string? GenericArgumentTypeName { get; init; }
        internal bool AddConstructedModifierTypeSpecification
            { get; init; }
        internal int ConstructedModifierArgumentCount { get; init; } =
            1;
        internal SyntheticStackValue TargetReturnValue { get; init; }
        internal SyntheticStackValue[] CallArgumentKinds { get; init; } =
            [];
        internal bool PopCallReturn { get; init; }
        internal byte[]? PropertySignature { get; init; }
        internal MethodSemanticsAttributes[]? PropertySemantics
            { get; init; }
        internal MethodSemanticsAttributes[]? EventSemantics
            { get; init; }
    }

    enum SyntheticStackValue
    {
        None,
        Int32,
        NativeInt,
        Null,
    }

    static byte[] MalformedArrayByRefGenericSignature(
        int genericArity)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x00);
        signature.WriteByte(0x1D);
        signature.WriteByte(0x10);
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteByte(0x0C);
        signature.WriteCompressedInteger(genericArity);
        for (int index = 0; index < genericArity; index++)
            signature.WriteByte(0x08);
        return signature.ToArray();
    }

    sealed class ExactPolicy(
        IEnumerable<ResolvedAssemblyReference> assemblies)
        : IAssemblyBindingPolicy
    {
        readonly ImmutableDictionary<
            AssemblyReferenceIdentity,
            ResolvedAssemblyReference> _assemblies =
                assemblies
                    .GroupBy(assembly => assembly.Identity)
                    .ToImmutableDictionary(
                        group => group.Key,
                        group => group.First());

        internal static ExactPolicy Empty { get; } = new([]);

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                request.Target switch
                {
                    AssemblyBindingTarget.IntrinsicCoreLibrary =>
                        AssemblyBindingSelection.Found(
                            CoreLibraryAssembly),
                    AssemblyBindingTarget.AssemblyReference reference
                        when _assemblies.TryGetValue(
                            reference.Identity,
                            out ResolvedAssemblyReference? assembly) =>
                        AssemblyBindingSelection.Found(assembly),
                    _ => AssemblyBindingSelection.NotFound(),
                });
    }

    sealed class ScopePolicy(
        ResolvedAssemblyReference anyAssembly,
        ResolvedAssemblyReference platformAssembly)
        : IAssemblyBindingPolicy
    {
        public List<AssemblyBindingRequest> Requests { get; } = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            Requests.Add(request);
            return new(
                Version,
                request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity == anyAssembly.Identity
                        ? AssemblyBindingSelection.Found(
                            request.Scope
                                == AssemblyResolutionScope.Platform
                                    ? platformAssembly
                                    : anyAssembly)
                        : AssemblyBindingSelection.NotFound());
        }
    }
}
