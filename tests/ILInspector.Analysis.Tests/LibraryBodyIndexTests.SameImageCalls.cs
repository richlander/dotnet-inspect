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
            LibraryBodyIndex.HasUnsafeEvidence(
                path,
                ImmutableArray.Create(File.ReadAllBytes(path))));
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
            LibraryBodyIndex.HasUnsafeEvidence(
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
            LibraryBodyIndex.HasUnsafeEvidence(
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
            LibraryBodyIndex.HasUnsafeEvidence(
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
                LibraryBodyAnalysisFeatures.MethodEvidence);
            DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate => candidate.Caller.Name == "CallsModuleAlias");

            Assert.Equal(
                expected,
                LibraryBodyIndex.HasUnsafeEvidence(
                    path,
                    ImmutableArray.Create(image)));
            Assert.Equal(
                expected ? CallerUnsafeMode.Explicit : (CallerUnsafeMode?)null,
                call.TargetCallerUnsafeMode);
            Assert.Equal(
                expected,
                index.UnsafeEvidence.Any(evidence =>
                    evidence.Member.Name == "CallsModuleAlias"
                    && evidence.Reason == "Unsafe call"
                    && evidence.OperandToken == call.OperandToken));
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
                LibraryBodyIndex.HasUnsafeEvidence(
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
                LibraryBodyIndex.HasUnsafeEvidence(
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
        AssemblyReferenceAttributeOnly,
        ExternalSameNameAttributeOnly,
        MethodDefinitionParentVarArgAttributeOnly,
    }

}
