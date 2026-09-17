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
        ExternalSameNameAttributeOnly,
        MethodDefinitionParentVarArgAttributeOnly,
    }

}
