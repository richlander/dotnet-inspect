using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{
    [Theory]
    [InlineData(FixtureIds.AnalysisCallOverloads, "Call", CallerUnsafeMode.Explicit, true)]
    [InlineData(FixtureIds.AnalysisCallOverloads, "CallGeneric", CallerUnsafeMode.None, true)]
    [InlineData(FixtureIds.AnalysisCallGenericScope, "Call", CallerUnsafeMode.None, false)]
    [InlineData(FixtureIds.AnalysisCallGenericScope, "CallVarArg", CallerUnsafeMode.None, false)]
    [InlineData(FixtureIds.AnalysisCallFunctionPointerScope, "Call", CallerUnsafeMode.None, true)]
    public void SameImageCalls_PreserveOpenIdentityAndGenericScope(
        string fixtureId,
        string callerName,
        CallerUnsafeMode expectedMode,
        bool expectedPresence)
    {
        string path = FixtureCatalog.Get(fixtureId).AssemblyPath();
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall call = Assert.Single(
            index.DirectCalls,
            candidate => candidate.Caller.Name == callerName
                && candidate.Callee.Name == "Invoke");

        Assert.Empty(index.Diagnostics);
        Assert.Equal(expectedMode, call.TargetCallerUnsafeMode);
        Assert.Equal(
            expectedMode == CallerUnsafeMode.Explicit,
            index.UnsafeEvidence.Any(evidence =>
                evidence.Member.Name == callerName
                && evidence.Reason == "Unsafe call"));
        Assert.Equal(
            expectedPresence,
            Planning.UnsafeEvidencePresence.HasEvidence(
                path,
                ImmutableArray.Create(File.ReadAllBytes(path))));
    }

    [Fact]
    public void
        SameImageCalls_AttributedLocalUsesPhysicalGenericScope()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog
                .Get(FixtureIds.AnalysisCallGenericScope)
                .AssemblyPath());
        MethodIdentity caller = Assert.Single(
            index.Methods,
            method =>
                method.Name
                    == "CallAttributedLocal");
        MethodIdentity target = Assert.Single(
            index.Methods,
            method =>
                method.DeclaringType.Name
                    == "Target`1"
                && method.Name == "Invoke");
        CallTreeNode child = Assert.Single(
            index.BuildCallTree(
                    caller.MetadataToken,
                    maxDepth: 2,
                    maxNodes: 10)
                .Children,
            node =>
                node.Member.Name == "Invoke");
        MethodLeverage leverage = Assert.Single(
            index.TopLeverage(
                count: 1,
                scope: method =>
                    method.MetadataToken
                        == target.MetadataToken));

        Assert.Equal(
            CallTreeStatus.Leaf,
            child.Status);
        Assert.Equal(
            3,
            leverage.DirectCallerCount);
    }

    [Fact]
    public void SameImageCalls_UseNormalizedCallerContracts()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        DirectCall pointerNone = Assert.Single(
            index.DirectCalls,
            call =>
                call.Caller.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && call.Caller.Name == "CallPointerNoneMethod"
                && call.Callee.Name == "PointerNoneMethod");
        Assert.Equal(
            CallerUnsafeMode.None,
            pointerNone.TargetCallerUnsafeMode);
        Assert.DoesNotContain(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name == "CallPointerNoneMethod"
                && evidence.Reason == "Unsafe call");

        DirectCall pointerFreeExplicit = Assert.Single(
            index.DirectCalls,
            call =>
                call.Caller.DeclaringType.Name
                    == "MemorySafetySpellingFixture"
                && call.Caller.Name
                    == "CallPointerFreeUnsafeMethod"
                && call.Callee.Name == "PointerFreeUnsafeMethod");
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            pointerFreeExplicit.TargetCallerUnsafeMode);
        Assert.Contains(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name
                    == "CallPointerFreeUnsafeMethod"
                && evidence.Reason == "Unsafe call"
                && evidence.OperandToken
                    == pointerFreeExplicit.OperandToken);

        foreach ((string caller, string callee) in new[]
        {
            ("ReadProperty", "get_Property"),
            ("SubscribeEvent", "add_Changed"),
            ("Create", ".ctor"),
        })
        {
            DirectCall call = Assert.Single(
                index.DirectCalls,
                item =>
                    item.Caller.DeclaringType.Name
                        == "AccessorContractFixtures"
                    && item.Caller.Name == caller
                    && item.Callee.Name == callee);
            Assert.Equal(
                CallerUnsafeMode.Explicit,
                call.TargetCallerUnsafeMode);
            Assert.Contains(
                index.UnsafeEvidence,
                evidence =>
                    evidence.Member.Name == caller
                    && evidence.Reason == "Unsafe call"
                    && evidence.OperandToken == call.OperandToken);
        }

        DirectCall setter = Assert.Single(
            index.DirectCalls,
            call =>
                call.Caller.DeclaringType.Name
                    == "AccessorContractFixtures"
                && call.Caller.Name == "WriteProperty"
                && call.Callee.Name == "set_Property");
        Assert.Equal(
            CallerUnsafeMode.None,
            setter.TargetCallerUnsafeMode);
        Assert.DoesNotContain(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name == "WriteProperty"
                && evidence.Reason == "Unsafe call");

        DirectCall constructedGeneric = Assert.Single(
            index.DirectCalls,
            call =>
                call.Caller.DeclaringType.Name
                    == "GenericCallerContractCalls"
                && call.Caller.Name == "CallConstructedInstance"
                && call.Callee.Name == "ContractChoice");
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            constructedGeneric.TargetCallerUnsafeMode);
        Assert.Equal(1, constructedGeneric.Callee.GenericArity);
        Assert.Equal(
            CallerUnsafeMode.None,
            Assert.Single(
                index.DeclaredMethods,
                method =>
                    method.DeclaringType.Name
                        == "GenericCallerContractFixture`1"
                    && method.Name == "ContractChoice"
                    && method.GenericArity == 0)
                .CallerUnsafeMode);
        Assert.Equal(
            CallerUnsafeMode.Explicit,
            Assert.Single(
                index.DeclaredMethods,
                method =>
                    method.DeclaringType.Name
                        == "GenericCallerContractFixture`1"
                    && method.Name == "ContractChoice"
                    && method.GenericArity == 1)
                .CallerUnsafeMode);
        Assert.Contains(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name == "CallConstructedInstance"
                && evidence.Reason == "Unsafe call"
                && evidence.OperandToken
                    == constructedGeneric.OperandToken);
    }

    [Fact]
    public void SameImageCalls_LegacyPointerContractRemainsImplicit()
    {
        var index = LibraryBodyIndex.Open(
            typeof(UnsafeEvidenceFixtures).Assembly.Location);

        DirectCall call = Assert.Single(
            index.DirectCalls,
            item =>
                item.Caller.Name
                    == nameof(
                        UnsafeEvidenceFixtures
                            .PointerExternCallerA)
                && item.Callee.Name
                    == nameof(
                        UnsafeEvidenceFixtures.PointerExtern));
        Assert.Equal(
            CallerUnsafeMode.Implicit,
            call.TargetCallerUnsafeMode);
        Assert.Contains(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name
                    == nameof(
                        UnsafeEvidenceFixtures
                            .PointerExternCallerA)
                && evidence.Reason == "Unsafe call"
                && evidence.OperandToken == call.OperandToken);
    }

    [Fact]
    public void SameImageCalls_UnavailableContractRemainsVisible()
    {
        LibraryBodyIndex index =
            OpenMemorySafetyContractImage(2, 1);

        DirectCall call = Assert.Single(
            index.DirectCalls,
            item =>
                item.Caller.Name == "CallsPointerOnly"
                && item.Callee.Name == "PointerOnly");
        Assert.Equal(
            CallerUnsafeMode.Unavailable,
            call.TargetCallerUnsafeMode);
        Assert.DoesNotContain(
            index.UnsafeEvidence,
            evidence =>
                evidence.Member.Name == "CallsPointerOnly"
                && evidence.Reason == "Unsafe call");
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void
        UnsafeEvidencePresence_UsesNormalizedSameImageCallerContract(
            int secondMarker,
            bool expected)
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2, secondMarker],
            includePointerSignature: false,
            callTarget: MemorySafetyCallTarget.AttributeOnly);

        Assert.Equal(
            expected,
            Planning.UnsafeEvidencePresence.HasEvidence(
                "AnalysisMemorySafety.dll",
                ImmutableArray.Create(image)));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ResolvesPointerFreeLocalTypeReferenceAlias()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget.LocalTypeReferenceAttributeOnly);

        Assert.True(
            Planning.UnsafeEvidencePresence.HasEvidence(
                "AnalysisMemorySafety.dll",
                ImmutableArray.Create(image)));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_DoesNotBindExternalSameNameReference()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget.ExternalSameNameAttributeOnly);

        Assert.False(
            Planning.UnsafeEvidencePresence.HasEvidence(
                "AnalysisMemorySafety.dll",
                ImmutableArray.Create(image)));
    }

    [Theory]
    [InlineData("AnalysisMemorySafety.dll", false, true)]
    [InlineData("ANALYSISMEMORYSAFETY.DLL", false, true)]
    [InlineData("Other.netmodule", false, false)]
    [InlineData("AnalysisMemorySafety.dll", true, true)]
    [InlineData("ANALYSISMEMORYSAFETY.DLL", true, true)]
    [InlineData("Other.netmodule", true, false)]
    [InlineData("AnalysisMemorySafety.dll", true, false, "Other.netmodule")]
    public void SameImageCalls_ModuleReferenceAliasesMatchPresence(
        string moduleName,
        bool includeLocalParameter,
        bool expected,
        string? parameterModuleName = null)
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget: MemorySafetyCallTarget.ModuleReferenceAttributeOnly,
            aliasModuleName: moduleName,
            includeLocalParameter: includeLocalParameter,
            parameterModuleName: parameterModuleName);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"analysis-module-alias-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .ImplementationProfiles);
            DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate => candidate.Caller.Name == "ModuleAlias");

            Assert.Equal(
                expected,
                Planning.UnsafeEvidencePresence.HasEvidence(
                    path,
                    ImmutableArray.Create(image)));
            Assert.Equal(
                expected ? CallerUnsafeMode.Explicit : (CallerUnsafeMode?)null,
                call.TargetCallerUnsafeMode);
            Assert.Equal(
                expected,
                index.UnsafeEvidence.Any(evidence =>
                    evidence.Member.Name == "ModuleAlias"
                    && evidence.Reason == "Unsafe call"
                    && evidence.OperandToken == call.OperandToken));
            MethodIdentity target = Assert.Single(
                index.Methods,
                method =>
                    method.Name == "ModuleAlias"
                    && method.MetadataToken
                        != call.Caller.MetadataToken);
            Assert.Equal(
                expected,
                index.OverloadRelationships().Any(
                    relationship =>
                        relationship.Caller.MetadataToken
                            == call.Caller.MetadataToken
                        && relationship.Callee.MetadataToken
                            == target.MetadataToken));
            Assert.Equal(
                expected ? 1 : 0,
                Assert.Single(
                    index.ImplementationProfiles(
                        method =>
                            method.MetadataToken
                                == target.MetadataToken))
                    .IncomingOverloadCallerCount);
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    path,
                    ImmutableArray.Create(image),
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures
                            .ImplementationProfiles));
            Assert.Equal(
                expected,
                execution.ImplementationProfiles
                    .OverloadRelationships.Any(
                        relationship =>
                            relationship.Caller.MetadataToken
                                == call.Caller.MetadataToken
                            && relationship.Callee.MetadataToken
                                == target.MetadataToken));
            Assert.Equal(
                expected ? 1 : 0,
                Assert.Single(
                    execution.ImplementationProfiles.Profiles,
                    method =>
                        method.Method.MetadataToken
                            == target.MetadataToken)
                    .IncomingOverloadCallerCount);
            Assert.Equal(
                expected ? 1 : 0,
                Assert.Single(
                    index.TopLeverage(
                        count: int.MaxValue,
                        scope: method =>
                            method.MetadataToken
                                == target.MetadataToken))
                    .DirectCallerCount);
            Assert.Equal(
                expected
                    ? CallTreeStatus.Leaf
                    : CallTreeStatus.External,
                Assert.Single(
                    index.BuildCallTree(
                        call.Caller.MetadataToken,
                        maxDepth: 2,
                        maxNodes: 10).Children)
                    .Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("AnalysisMemorySafety", 1, "Samples", "Target", "AttributeOnly", true)]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "Samples", "Target", "AttributeOnly", true)]
    [InlineData("analysismemorysafety", 1, "Samples", "Target", "AttributeOnly", true)]
    [InlineData("Other", 1, "Samples", "Target", "AttributeOnly", false)]
    [InlineData("ANALYSISMEMORYSAFETY", 9, "Samples", "Target", "AttributeOnly", false)]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "Samples", "Target", "AttributeOnly", false, "fr-FR")]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "Samples", "Target", "AttributeOnly", false, null, "0102030405060708")]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "samples", "Target", "AttributeOnly", false)]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "Samples", "target", "AttributeOnly", false)]
    [InlineData("ANALYSISMEMORYSAFETY", 1, "Samples", "Target", "attributeOnly", false)]
    public void SameImageCalls_AssemblyReferenceAliasesPreserveIdentity(
        string assemblyName,
        int majorVersion,
        string aliasNamespace,
        string aliasTypeName,
        string aliasMemberName,
        bool expected,
        string? culture = null,
        string? publicKeyToken = null)
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget: MemorySafetyCallTarget.AssemblyReferenceAttributeOnly,
            assemblyAlias: new(
                assemblyName,
                new Version(majorVersion, 0, 0, 0),
                culture,
                publicKeyToken),
            aliasNamespace: aliasNamespace,
            aliasTypeName: aliasTypeName,
            aliasMemberName: aliasMemberName);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"analysis-assembly-alias-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence);
            DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate => candidate.Caller.Name == "CallsAssemblyAlias");

            Assert.Empty(index.Diagnostics);
            Assert.Equal(
                expected,
                Planning.UnsafeEvidencePresence.HasEvidence(
                    path,
                    ImmutableArray.Create(image)));
            Assert.Equal(
                expected ? CallerUnsafeMode.Explicit : (CallerUnsafeMode?)null,
                call.TargetCallerUnsafeMode);
            Assert.Equal(
                expected,
                index.UnsafeEvidence.Any(evidence =>
                    evidence.Member.Name == "CallsAssemblyAlias"
                    && evidence.Reason == "Unsafe call"
                    && evidence.OperandToken == call.OperandToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        UnsafeEvidencePresence_RejectsAmbiguousSameImageCorrespondence()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget
                    .AmbiguousLocalTypeReferenceAttributeOnly);

        Assert.Throws<InvalidDataException>(
            () => Planning.UnsafeEvidencePresence.HasEvidence(
                "AnalysisMemorySafety.dll",
                ImmutableArray.Create(image)));
    }

    [Fact]
    public void
        SameImageCalls_LiteralPlusSegmentPreservesGenericArity()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget
                    .LiteralPlusConstructedAttributeOnly,
            localTypeName:
                "Outer`1+Target`1");
        string path = Path.Combine(
            "artifacts",
            $"analysis-literal-plus-arity-{Guid.NewGuid():N}.dll");
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(path);
            DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate =>
                    candidate.Caller.Name
                        == "CallsLiteralPlusConstructed");

            Assert.True(
                Planning.UnsafeEvidencePresence.HasEvidence(
                    path,
                    ImmutableArray.Create(image)));
            Assert.Equal(
                CallerUnsafeMode.Explicit,
                call.TargetCallerUnsafeMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        TopLeverage_DoesNotResolveAgainstBodyOnlySubset()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget
                    .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly);
        string path = Path.Combine(
            "artifacts",
            $"analysis-body-bodiless-ambiguity-{Guid.NewGuid():N}.dll");
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(path);
            MethodIdentity target = Assert.Single(
                index.Methods,
                method =>
                    method.Name == "AttributeOnly");
            MethodLeverage leverage = Assert.Single(
                index.TopLeverage(
                    count: 1,
                    scope: method =>
                        method.MetadataToken
                            == target.MetadataToken));

            Assert.Equal(
                0,
                leverage.DirectCallerCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        BuildCallTree_ClassifiesModuleAliasBodilessCallee()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget
                    .ModuleReferenceBodilessAttributeOnly);
        string path = Path.Combine(
            "artifacts",
            $"analysis-bodiless-module-alias-{Guid.NewGuid():N}.dll");
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence);
            MethodIdentity caller = Assert.Single(
                index.Methods,
                method =>
                    method.Name
                        == "CallsBodilessModuleAlias");

            CallTreeNode child = Assert.Single(
                index.BuildCallTree(
                    caller.MetadataToken,
                    maxDepth: 2,
                    maxNodes: 10).Children);

            Assert.Equal(
                CallTreeStatus.Bodiless,
                child.Status);
            Assert.Equal(
                "ModuleAlias",
                child.Member.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        SameImageCalls_ResolveMethodDefinitionParentVarArg()
    {
        byte[] image = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget:
                MemorySafetyCallTarget
                    .MethodDefinitionParentVarArgAttributeOnly);
        string path = Path.Combine(
            "artifacts",
            $"analysis-vararg-{Guid.NewGuid():N}.dll");
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence);

            DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate =>
                    candidate.Caller.Name
                        == "CallsVarArgAttributeOnly");
            Assert.Equal(
                CallerUnsafeMode.Explicit,
                call.TargetCallerUnsafeMode);
            Assert.Equal(
                1,
                call.Callee.RequiredParameterCount);
            Assert.Equal(
                2,
                call.Callee.ParameterTypes.Length);
            Assert.True(
                Planning.UnsafeEvidencePresence.HasEvidence(
                    path,
                    ImmutableArray.Create(image)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    enum MemorySafetyCallTarget
    {
        PointerOnly,
        AttributeOnly,
        LocalTypeReferenceAttributeOnly,
        ModuleReferenceAttributeOnly,
        ModuleReferenceBodilessAttributeOnly,
        AmbiguousLocalTypeReferenceAttributeOnly,
        BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly,
        LiteralPlusConstructedAttributeOnly,
        AssemblyReferenceAttributeOnly,
        ExternalSameNameAttributeOnly,
        MethodDefinitionParentVarArgAttributeOnly,
    }

}
