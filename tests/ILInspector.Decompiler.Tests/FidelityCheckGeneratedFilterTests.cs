using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Collection(ConsoleMutatorCollection.Name)]
public class FidelityCheckGeneratedFilterTests
{
    [Fact]
    public void SelectReturnToSenderTargets_UsesStableSetAcrossMetadataOrder()
    {
        var firstAssembly = CompileFixture("""
            public static class StableSelectionFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
                public static int Gamma(int value) => value + 3;
            }
            """, assemblyName: "StableSelection");
        var reorderedAssembly = CompileFixture("""
            public static class StableSelectionFixture
            {
                public static int Gamma(int value) => value + 3;
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """, assemblyName: "StableSelection");
        try
        {
            var first = FidelityCheck.SelectReturnToSenderTargets([firstAssembly], cap: 2)
                .Select(target => $"{target.Type}::{target.Method}{target.Signature}")
                .Order(StringComparer.Ordinal)
                .ToArray();
            var reordered = FidelityCheck.SelectReturnToSenderTargets([reorderedAssembly], cap: 2)
                .Select(target => $"{target.Type}::{target.Method}{target.Signature}")
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(first, reordered);
        }
        finally
        {
            DeleteFixture(firstAssembly);
            DeleteFixture(reorderedAssembly);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_UsesGenericArityInStableRanking()
    {
        var firstAssembly = CompileFixture("""
            public static class StableGenericArityFixture
            {
                public static int Pick() => 1;
                public static int Pick<T>() => 2;
            }
            """, assemblyName: "StableGenericArity");
        var reorderedAssembly = CompileFixture("""
            public static class StableGenericArityFixture
            {
                public static int Pick<T>() => 2;
                public static int Pick() => 1;
            }
            """, assemblyName: "StableGenericArity");
        try
        {
            var first = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [firstAssembly],
                    cap: 1));
            var reordered = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [reorderedAssembly],
                    cap: 1));

            Assert.Equal(first.Signature, reordered.Signature);
        }
        finally
        {
            DeleteFixture(firstAssembly);
            DeleteFixture(reorderedAssembly);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_AppliesGlobalCapAndTypeFilterBeforeSampling()
    {
        var firstAssembly = CompileFixture("""
            public static class FirstAssemblyFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """, assemblyName: "FirstAssembly");
        string excludedMethods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 32)
                .Select(index =>
                    $"    public static int Hidden{index}(int value) => value + {index + 7};"));
        var secondAssembly = CompileFixture($$"""
            using System.CodeDom.Compiler;

            public static class IncludedType
            {
                public static int Gamma(int value) => value + 3;
                public static int Delta(int value) => value + 4;

                [GeneratedCode("fixture", "1.0")]
                public static int Generated(int value) => value + 5;

                public static class NestedType
                {
                    public static int Nested(int value) => value + 6;
                }
            }

            public static class ExcludedType
            {
            {{excludedMethods}}
            }
            """, assemblyName: "SecondAssembly");
        try
        {
            var globallyCapped = FidelityCheck.SelectReturnToSenderTargets(
                [firstAssembly, secondAssembly],
                cap: 3);

            Assert.Equal(3, globallyCapped.Count);
            Assert.Equal(2, globallyCapped.Count(target => target.AssemblyPath == firstAssembly));
            Assert.Equal(1, globallyCapped.Count(target => target.AssemblyPath == secondAssembly));
            Assert.All(globallyCapped, target => Assert.NotNull(target.Address));

            var reversed = FidelityCheck.SelectReturnToSenderTargets(
                [secondAssembly, firstAssembly],
                cap: 3);
            Assert.All(reversed, target => Assert.Equal(secondAssembly, target.AssemblyPath));

            Assert.Equal(
                2,
                FidelityCheck.SelectReturnToSenderTargets([firstAssembly], cap: 5).Count);

            var filtered = FidelityCheck.SelectReturnToSenderTargets(
                [secondAssembly],
                cap: 1,
                typeFilter: "IncludedType");
            var filteredTarget = Assert.Single(filtered);
            Assert.Equal("IncludedType", filteredTarget.Type);

            using var source = MetadataSource.Open(secondAssembly);
            var unfilteredWinner = Assert.Single(
                IrImporter.GetStableSampleCandidates(source, sampleSize: 1));
            Assert.Equal("ExcludedType", unfilteredWinner.TypeName);
        }
        finally
        {
            DeleteFixture(firstAssembly);
            DeleteFixture(secondAssembly);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsUnrepresentableArtifactIdentitiesBeforeSampling()
    {
        string assemblyPath = CreateUnrepresentableIdentityFixture();
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Equal(4, selected.Count);
            Assert.Contains(selected, target => target.Method == "Good");
            Assert.Contains(selected, target => target.Method == "event");
            Assert.Contains(
                selected,
                target => target.Method == "op_Subtraction");
            Assert.Contains(
                selected,
                target => target.Method == "op_Division");
            Assert.DoesNotContain(selected, target => target.Type.Contains(
                "bad-namespace",
                StringComparison.Ordinal));
            Assert.DoesNotContain(
                selected,
                target => target.Method is
                    "bad-name"
                    or "BadSignature"
                    or "BadConstraint"
                    or "op_Addition"
                    or "op_Multiply"
                    or "op_UnaryNegation");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsInvalidConstraintSignaturesBeforeSampling()
    {
        string assemblyPath = CreateInvalidConstraintSignatureFixture();
        try
        {
            Assert.Empty(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsUnrepresentableMethodDeclarationsBeforeSampling()
    {
        string assemblyPath = CreateUnrepresentableMethodDeclarationFixture();
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));

            Assert.Equal("MethodDeclarationNeighborFixture", target.Type);
            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsUnrepresentedSignatureCustomModifiersBeforeSampling()
    {
        string assemblyPath = CreateCustomModifiedSignatureFixture();
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            var target = Assert.Single(selected);
            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_IncludesRepresentedReadOnlyByRefModifiers()
    {
        string assemblyPath = CompileFixture("""
            public static class ReadOnlyByRefFixture
            {
                private static int _value;

                public static ref readonly int Read(in int value)
                    => ref value;

                public static ref readonly int Value => ref _value;

                public static ref int Mutable => ref _value;
            }
            """);
        try
        {
            ApiType type = Assert.Single(
                AssemblyReader.ExtractApiSurface(
                    assemblyPath,
                    includeAll: true)!
                    .Types,
                type => type.Name == "ReadOnlyByRefFixture");
            foreach (string propertyName in new[] { "Value", "Mutable" })
            {
                ApiMember property = Assert.Single(
                    type.Members,
                    member => member.Name == propertyName);
                ApiAccessor getter = Assert.Single(
                    property.SignatureModel!.Accessors);
                Assert.True(
                    property.SignatureModel
                        .ReturnTypeCustomModifiersAreRepresentable);
                Assert.Equal(
                    ApiPrimitiveType.Int32,
                    property.SignatureModel.ReturnTypeShape!.Primitive);
                Assert.True(getter.CustomModifiersAreRepresentable);
                Assert.True(getter.SignatureMatchesDeclaration);
            }

            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(selected, target => target.Method == "Read");
            Assert.Contains(selected, target => target.Method == "get_Value");
            Assert.Contains(selected, target => target.Method == "get_Mutable");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsMalformedPropertyDeclarationsBeforeSampling()
    {
        string assemblyPath = CreateMalformedPropertyFixture();
        try
        {
            ApiType type = Assert.Single(
                AssemblyReader.ExtractApiSurface(
                    assemblyPath,
                    includeAll: true)!
                    .Types,
                type => type.Name == "MalformedPropertyFixture");
            AssertAccessorFacts("this[]", expected: true);
            AssertAccessorFacts("Vararg", expected: false);
            AssertAccessorFacts("ModifiedRef", expected: false);
            AssertAccessorFacts("MismatchedRef", expected: false);
            AssertAccessorFacts("GoodEvent", expected: true);
            AssertAccessorFacts("VarargEvent", expected: false);
            AssertAccessorFacts("BadEventSignature", expected: false);

            ApiMember voidProperty = Assert.Single(
                type.Members,
                member => member.Name == "Void");
            Assert.False(
                CSharpMemberArtifactEligibility.IsRepresentable(
                    type,
                    voidProperty));

            ApiType malformedIndexerType = Assert.Single(
                AssemblyReader.ExtractApiSurface(
                    assemblyPath,
                    includeAll: true)!
                    .Types,
                type => type.Name == "MalformedIndexerShapeFixture");
            List<ApiMember> malformedIndexers =
                malformedIndexerType.Members
                    .Where(member => member.Kind == "property")
                    .OrderBy(
                        member =>
                            member.SignatureModel!.Parameters.Count)
                    .ToList();
            Assert.Equal(2, malformedIndexers.Count);
            Assert.Null(
                malformedIndexers[0].SignatureModel!
                    .IsIndexerDeclaration);
            Assert.True(
                malformedIndexers[1].SignatureModel!
                    .IsIndexerDeclaration);
            Assert.All(
                malformedIndexers,
                member => Assert.False(
                    CSharpMemberArtifactEligibility.IsRepresentable(
                        malformedIndexerType,
                        member)));

            void AssertAccessorFacts(string name, bool expected)
            {
                List<ApiAccessor> accessors = Assert.Single(
                        type.Members,
                        member => member.Name == name)
                    .SignatureModel!
                    .Accessors;
                Assert.NotEmpty(accessors);
                if (expected)
                {
                    Assert.All(
                        accessors,
                        accessor =>
                        {
                            Assert.True(
                                accessor
                                    .MethodDeclarationHeaderIsRepresentable);
                            Assert.True(
                                accessor.CustomModifiersAreRepresentable);
                            Assert.True(
                                accessor.SignatureMatchesDeclaration);
                        });
                }
                else
                {
                    Assert.Contains(
                        accessors,
                        accessor =>
                            accessor.MethodDeclarationHeaderIsRepresentable
                                != true
                            || accessor.CustomModifiersAreRepresentable
                                != true
                            || accessor.SignatureMatchesDeclaration != true);
                }
            }

            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(selected, target => target.Method == "Good");
            Assert.Contains(
                selected,
                target => target.Method == "get_Item");
            Assert.Contains(
                selected,
                target => target.Method == "add_GoodEvent");
            Assert.Contains(
                selected,
                target => target.Method == "remove_GoodEvent");
            Assert.DoesNotContain(
                selected,
                target => target.Method is
                    "get_BadIndexer"
                    or "get_Ordinary"
                    or "get_Vararg"
                    or "get_ModifiedRef"
                    or "get_MismatchedRef"
                    or "get_Void"
                    or "get_BadItem"
                    or "add_VarargEvent"
                    or "remove_VarargEvent"
                    or "add_BadEventSignature"
                    or "remove_BadEventSignature");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsMalformedAccessorNamesBeforeSampling()
    {
        string assemblyPath = CreateAccessorNameFixture();
        try
        {
            ApiType type = Assert.Single(
                AssemblyReader.ExtractApiSurface(
                    assemblyPath,
                    includeAll: true)!
                    .Types,
                type => type.Name == "AccessorNameFixture");
            foreach (string name in new[]
            {
                "Value",
                "Changed",
                "INameProperty.Value",
                "INameEvent.Changed",
            })
            {
                ApiMember member = Assert.Single(
                    type.Members,
                    member => member.Name == name);
                Assert.All(
                    member.SignatureModel!.Accessors,
                    accessor => Assert.True(
                        accessor.NameMatchesDeclaration));
                Assert.True(
                    CSharpMemberArtifactEligibility.IsRepresentable(
                        type,
                        member));
            }
            foreach (string name in new[]
            {
                "BadGetter",
                "BadSetter",
                "BadAdder",
                "BadRemover",
            })
            {
                Assert.Contains(
                    Assert.Single(
                            type.Members,
                            member => member.Name == name)
                        .SignatureModel!
                        .Accessors,
                    accessor => accessor.NameMatchesDeclaration != true);
            }

            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: 4,
                typeFilter: "AccessorNameFixture");

            Assert.True(
                selected.Count == 4,
                $"Selected: {string.Join(", ", selected.Select(target => target.Method))}");
            Assert.Equal(
                [
                    "add_Changed",
                    "get_Value",
                    "remove_Changed",
                    "set_Value",
                ],
                selected.Select(target => target.Method)
                    .Order(StringComparer.Ordinal));
            Assert.DoesNotContain(
                selected,
                target => target.Method.Contains(
                    "Wrong",
                    StringComparison.Ordinal));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsPrivateScopeMembersBeforeSampling()
    {
        string assemblyPath = CreatePrivateScopeMemberFixture();
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));

            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsUnrepresentableOperatorsBeforeSampling()
    {
        string assemblyPath = CreateForeignOperatorOperandFixture();
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));

            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsVarargAndContradictoryHeadersBeforeSampling()
    {
        string assemblyPath = CreateMethodHeaderFixture();
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));

            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_UsesMethodSemanticsNotAccessorPrefixes()
    {
        string assemblyPath = CreateMethodSemanticsFixture();
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));

            Assert.Equal("get_Ordinary", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsNonPrivateExplicitInterfaceImplementationBeforeSampling()
    {
        string assemblyPath = CreateExplicitInterfaceAccessibilityFixture();
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(
                selected,
                target =>
                    target.Type == "ValidExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MalformedExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsMalformedExplicitFinalizerBeforeSampling()
    {
        string assemblyPath = CreateExplicitFinalizerFixture();
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(
                selected,
                target =>
                    target.Type == "ValidFinalizerFixture"
                    && target.Method == "Finalize");
            Assert.Contains(
                selected,
                target =>
                    target.Type == "ImplicitFinalizerFixture"
                    && target.Method == "Finalize");
            Assert.Contains(
                selected,
                target =>
                    target.Type == "FinalizerNeighborFixture"
                    && target.Method == "Good");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MalformedFinalizerFixture"
                    && target.Method == "Finalize");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "UnauthenticatedFinalizerFixture"
                    && target.Method == "Finalize");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_RejectsUnavailableCanonicalSignaturesBeforeSampling()
    {
        string assemblyPath = CreateUnavailableCanonicalSignatureFixture();
        try
        {
            Assert.Empty(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_IncludesRepresentableGenericMethods()
    {
        var assemblyPath = CompileFixture("""
            public static class GenericOnlyFixture
            {
                public static int Pick<T, U, V, W>()
                    where T : U
                    where U : System.IDisposable
                    where V : unmanaged
                    where W : struct => 1;
            }
            """);
        try
        {
            var target = Assert.Single(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: 1));

            Assert.Equal("GenericOnlyFixture", target.Type);
            Assert.Equal("Pick", target.Method);
            Assert.Equal("mss1:4(0:)n", target.Signature);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void FidelityCheckCommand_DispatchesRaisedToNativeAndLoweredToLegacy()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneDispatchFixture
            {
                public static int Target(int value) => value + 1;
            }
            """);
        try
        {
            HarnessRun raised = RunHarness(
                "--fidelity-check",
                "--compile-cap",
                "1",
                assemblyPath);
            Assert.True(
                raised.ExitCode == 0,
                $"Raised harness exit {raised.ExitCode}:{Environment.NewLine}{raised.Output}");
            Assert.Contains(
                "Standalone fidelity engine: product-artifact RTS (raised; compile-back-floor=false)",
                raised.Output);
            Assert.DoesNotContain("COMPILE-BACK over", raised.Output);

            HarnessRun lowered = RunHarness(
                "--fidelity-check",
                "--lowered",
                "--compile-cap",
                "1",
                assemblyPath);
            Assert.True(
                lowered.ExitCode == 0,
                $"Lowered harness exit {lowered.ExitCode}:{Environment.NewLine}{lowered.Output}");
            Assert.Contains("COMPILE-BACK over", lowered.Output);
            Assert.DoesNotContain(
                "Standalone fidelity engine: product-artifact RTS",
                lowered.Output);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_RejectsCompileBackFloor()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneFloorFixture
            {
                public static int Target(int value) => value + 1;
            }
            """);
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FidelityCheck.RunReturnToSender(
                    [assemblyPath],
                    cap: 1,
                    maxExamples: 5,
                    timings: false,
                    zeroSignalGuard: 0,
                    (_, targets) => Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            targets.Select(target => NativeResult(target)).ToArray(),
                            CompileBackFloorAppliedMethods: 1))));

            Assert.Contains("applied the compile-back floor", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_ReportsCompleteNativePopulationInSelectedOrder()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneNativeFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """);
        var originalOut = Console.Out;
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        string? originalCluster = Environment.GetEnvironmentVariable("CB_CLUSTER");
        string? originalDump = Environment.GetEnvironmentVariable("CB_DUMP");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            Environment.SetEnvironmentVariable("CB_CLUSTER", "1");
            Environment.SetEnvironmentVariable("CB_DUMP", null);
            var expected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: 4);
            var requested = new List<FidelityCheck.CompileBackTarget>();
            using var writer = new StringWriter();
            Console.SetOut(writer);

            int exitCode = await FidelityCheck.RunReturnToSender(
                [assemblyPath],
                cap: 4,
                maxExamples: 5,
                timings: true,
                zeroSignalGuard: 0,
                (path, targets) =>
                {
                    Assert.Equal(assemblyPath, path);
                    requested.AddRange(targets);
                    return Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            targets.Select((target, index) => NativeResult(
                                target,
                                index == 0
                                    ? FidelityCheck.CompileBackStatus.Exact
                                    : FidelityCheck.CompileBackStatus.NotFull))
                                .ToArray(),
                            CompileBackFloorAppliedMethods: 0));
                });

            Assert.Equal(0, exitCode);
            Assert.Equal(
                expected.Select(TargetIdentity),
                requested.Select(TargetIdentity));
            Assert.All(requested, target => Assert.NotNull(target.Address));
            string output = writer.ToString();
            Assert.Contains(
                "Standalone fidelity engine: product-artifact RTS (raised; compile-back-floor=false)",
                output);
            Assert.Contains(
                "Standalone candidate population: 2 planned (global cap 4; 2 evaluated)",
                output);
            Assert.Contains($"exact (contract v{FidelityCheck.CurrentContractVersion}): 1", output);
            Assert.Contains("not Full           : 1", output);
            Assert.Contains(
                "Legacy reconstruction controls CB_CLUSTER and CB_DUMP apply only to --lowered.",
                output);
            Assert.Contains("RTS fidelity timings:", output);
            Assert.Contains("target selection :", output);
            Assert.Contains("RTS evaluation   :", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            Environment.SetEnvironmentVariable("CB_CLUSTER", originalCluster);
            Environment.SetEnvironmentVariable("CB_DUMP", originalDump);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_ZeroSignalGuardStopsAfterNativeProbe()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneZeroSignalFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
                public static int Gamma(int value) => value + 3;
            }
            """);
        var originalOut = Console.Out;
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            int evaluated = 0;
            using var writer = new StringWriter();
            Console.SetOut(writer);

            int exitCode = await FidelityCheck.RunReturnToSender(
                [assemblyPath],
                cap: int.MaxValue,
                maxExamples: 5,
                timings: false,
                zeroSignalGuard: 2,
                (_, targets) =>
                {
                    evaluated += targets.Count;
                    return Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            targets.Select(target => NativeResult(
                                target,
                                FidelityCheck.CompileBackStatus.ContextFail,
                                "native-context-failure"))
                                .ToArray(),
                            CompileBackFloorAppliedMethods: 0));
                });

            Assert.Equal(0, exitCode);
            Assert.Equal(2, evaluated);
            string output = writer.ToString();
            Assert.Contains(
                $"Standalone candidate population: 3 planned (global cap {int.MaxValue}; 2 evaluated)",
                output);
            Assert.Contains(
                "zero-signal guard : stopped after 2 of requested 3",
                output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_ZeroSignalGuardEvaluatesOnlyRemainderAfterUsefulProbe()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneRemainderFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
                public static int Gamma(int value) => value + 3;
            }
            """);
        var originalOut = Console.Out;
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);
            var batches = new List<IReadOnlyList<FidelityCheck.CompileBackTarget>>();
            using var writer = new StringWriter();
            Console.SetOut(writer);

            int exitCode = await FidelityCheck.RunReturnToSender(
                [assemblyPath],
                cap: int.MaxValue,
                maxExamples: 5,
                timings: false,
                zeroSignalGuard: 2,
                (_, targets) =>
                {
                    batches.Add(targets.ToArray());
                    return Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            targets.Select((target, index) => NativeResult(
                                target,
                                batches.Count == 1 && index == 0
                                    ? FidelityCheck.CompileBackStatus.Exact
                                    : FidelityCheck.CompileBackStatus.ContextFail,
                                "mixed-probe"))
                                .ToArray(),
                            CompileBackFloorAppliedMethods: 0));
                });

            Assert.Equal(0, exitCode);
            Assert.Equal([2, 1], batches.Select(batch => batch.Count));
            Assert.Equal(
                selected.Select(TargetIdentity),
                batches.SelectMany(batch => batch).Select(TargetIdentity));
            Assert.Contains(
                "zero-signal guard : probe 2 found useful or mixed signal; continued to requested cap",
                writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_RejectsResultCardinalityMismatch()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneCardinalityFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """);
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FidelityCheck.RunReturnToSender(
                    [assemblyPath],
                    cap: 2,
                    maxExamples: 5,
                    timings: false,
                    zeroSignalGuard: 0,
                    (_, targets) => Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            [NativeResult(targets[0])],
                            CompileBackFloorAppliedMethods: 0))));

            Assert.Contains("returned 1 results for 2 selected methods", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_RejectsResultOrderMismatch()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneOrderFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """);
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FidelityCheck.RunReturnToSender(
                    [assemblyPath],
                    cap: 2,
                    maxExamples: 5,
                    timings: false,
                    zeroSignalGuard: 0,
                    (_, targets) => Task.FromResult(
                        new ReturnToSenderFidelityEvaluator.Evaluation(
                            targets.Reverse().Select(target => NativeResult(target)).ToArray(),
                            CompileBackFloorAppliedMethods: 0))));

            Assert.Contains("out of target order", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunReturnToSender_ReportsMissingOutputAndContextFailure()
    {
        var assemblyPath = CompileFixture("""
            public static class StandaloneContextFixture
            {
                public static int Alpha(int value) => value + 1;
                public static int Beta(int value) => value + 2;
            }
            """);
        var originalOut = Console.Out;
        string? originalType = Environment.GetEnvironmentVariable("CB_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("CB_TYPE", null);
            using var missingWriter = new StringWriter();
            Console.SetOut(missingWriter);
            int exitCode = await FidelityCheck.RunReturnToSender(
                [assemblyPath],
                cap: 2,
                maxExamples: 5,
                timings: false,
                zeroSignalGuard: 0,
                (path, targets) => ReturnToSenderFidelityEvaluator.EvaluateAsync(
                    path,
                    targets,
                    "product-artifact RTS; compile-back-floor=false",
                    FidelityCheck.CaptureMode.ProductArtifact,
                    () => Task.FromResult<IReadOnlyList<ReturnToSender.Result>>([])));

            Assert.Equal(0, exitCode);
            Assert.Contains("context fail       : 2 (100.00%)", missingWriter.ToString());
            Assert.Contains("return-to-sender-target-unavailable", missingWriter.ToString());

            using var contextWriter = new StringWriter();
            Console.SetOut(contextWriter);
            exitCode = await FidelityCheck.RunReturnToSender(
                [assemblyPath],
                cap: 2,
                maxExamples: 5,
                timings: false,
                zeroSignalGuard: 0,
                (path, targets) => ReturnToSenderFidelityEvaluator.EvaluateAsync(
                    path,
                    targets,
                    "product-artifact RTS; compile-back-floor=false",
                    FidelityCheck.CaptureMode.ProductArtifact,
                    () => throw new InvalidOperationException(
                        "Compilation reference preparation failed.")));

            Assert.Equal(0, exitCode);
            Assert.Contains("context fail       : 2 (100.00%)", contextWriter.ToString());
            Assert.Contains(
                "return-to-sender-context-unavailable: Compilation reference preparation failed.",
                contextWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("CB_TYPE", originalType);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_PreservesIteratorPropertyDeclarationOrder()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class IteratorPropertyFixture
            {
                public IEnumerable<int> Before() { yield return 1; }
                public IEnumerable<int> Values { get { yield return 2; } }
                public IEnumerable<int> After() { yield return 3; }
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath)
                .Where(result => result.Type == "IteratorPropertyFixture")
                .ToList();

            foreach (var method in new[] { "Before", "get_Values", "After" })
            {
                var result = Assert.Single(results, result => result.Method == method);
                Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                    $"{method}: {result.Status}: {result.Detail}");
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_SkipsGeneratedCodeTypesAndMethods()
    {
        var assemblyPath = CompileFixture("""
            using System.CodeDom.Compiler;

            public class Normal
            {
                public int Echo(int value) => value + 1;
            }

            [GeneratedCode("fixture", "1.0")]
            public class GeneratedType
            {
                public int Hidden() => 42;
            }

            public class Mixed
            {
                [GeneratedCode("fixture", "1.0")]
                public int Hidden() => 42;

                public int Visible() => 7;
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "Normal" && result.Method == "Echo");
            Assert.Contains(results, result => result.Type == "Mixed" && result.Method == "Visible");
            Assert.DoesNotContain(results, result => result.Type == "GeneratedType");
            Assert.DoesNotContain(results, result => result.Type == "Mixed" && result.Method == "Hidden");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_IncludesCompilerGeneratedAutoPropertyAccessors()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyFixture
            {
                public AutoPropertyFixture(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == ".ctor");
            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == "get_Value");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_SkipsCompilerGeneratedRecordMethodsButKeepsAccessors()
    {
        var assemblyPath = CompileFixture("""
            public record GeneratedRecord(int Value);
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "GeneratedRecord" && result.Method == "get_Value");
            Assert.DoesNotContain(results, result =>
                result.Type == "GeneratedRecord" &&
                result.Method is "ToString" or "PrintMembers" or "GetHashCode");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsConstructorAssignedAutoProperties()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyPairFixture
            {
                public AutoPropertyPairFixture(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Left { get; }
                public int Right { get; }
            }
            """);
        try
        {
            var ctor = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "AutoPropertyPairFixture" && result.Method == ".ctor");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, ctor.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_UsesProductWholeMemberForOrdinaryConstructors()
    {
        var assemblyPath = CompileFixture("""
            using System;
            using System.Runtime.CompilerServices;

            [AttributeUsage(AttributeTargets.Constructor)]
            internal sealed class ConstructorTagAttribute : Attribute
            {
            }

            public class ConstructorWholeMemberFixture
            {
                private readonly int _value;

                [ConstructorTag]
                [SkipLocalsInit]
                private ConstructorWholeMemberFixture()
                {
                    _value = 42;
                }

                public ConstructorWholeMemberFixture(int value)
                {
                    _value = value;
                }

                public static ConstructorWholeMemberFixture CreateDefault() => new();
                public int Value => _value;
            }

            public sealed class DerivedConstructorWholeMemberFixture
                : ConstructorWholeMemberFixture
            {
                public DerivedConstructorWholeMemberFixture() : base(1)
                {
                }
            }
            """, allowUnsafe: true);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeHandle = Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "ConstructorWholeMemberFixture");
            var type = reader.GetTypeDefinition(typeHandle);
            int constructorOverload = -1;
            MethodDefinitionHandle target = default;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor")
                    continue;
                constructorOverload++;
                if (method.GetParameters().Count(
                        parameterHandle => reader.GetParameter(parameterHandle).SequenceNumber > 0) == 0)
                {
                    target = methodHandle;
                    break;
                }
            }
            Assert.False(target.IsNil);

            using var source = MetadataSource.Open(assemblyPath);
            var wholeMember = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                target,
                targeted: true,
                isPrimaryConstructor: false);

            Assert.NotNull(wholeMember);
            Assert.Contains(
                "private ConstructorWholeMemberFixture()",
                wholeMember.Value.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain("[ConstructorTag]", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains(
                "[global::System.Runtime.CompilerServices.SkipLocalsInit]",
                wholeMember.Value.Text,
                StringComparison.Ordinal);
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                target,
                targeted: true,
                isPrimaryConstructor: true));

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    type => type == "ConstructorWholeMemberFixture",
                    method => method.Method == ".ctor"
                        && method.Overload == constructorOverload));

            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_UsesProductWholeMemberForFinalizer()
    {
        var assemblyPath = CompileFixture("""
            public sealed class FinalizerWholeMemberFixture
            {
                private static bool _finalized;

                ~FinalizerWholeMemberFixture() => _finalized = true;
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "FinalizerWholeMemberFixture"));
            var finalizer = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "Finalize");

            using var source = MetadataSource.Open(assemblyPath);
            var wholeMember = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                finalizer,
                targeted: true,
                isPrimaryConstructor: false);

            Assert.NotNull(wholeMember);
            Assert.IsType<Microsoft.CodeAnalysis.CSharp.Syntax.DestructorDeclarationSyntax>(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseMemberDeclaration(
                    wholeMember.Value.Text));

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "FinalizerWholeMemberFixture",
                    method => method.Method == "Finalize"));
            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);

            var batchResult = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "FinalizerWholeMemberFixture"),
                candidate => candidate.Method == "Finalize");
            Assert.True(batchResult.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, batchResult.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_DeclinesProductLiteralWholeMemberForVbFinalizer()
    {
        string assemblyPath = FixtureCatalog.DecompilerVbFinalizer.AssemblyPath();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        var type = reader.GetTypeDefinition(Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "Handle"));
        var finalizer = Assert.Single(
            type.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "Finalize");

        using var source = MetadataSource.Open(assemblyPath);
        var wholeMember = FidelityCheck.TryRenderTargetMember(
            pe,
            source,
            finalizer,
            targeted: true,
            isPrimaryConstructor: false);

        Assert.Null(wholeMember);

        var result = Assert.Single(
            FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName == "Handle",
                method => method.Method == "Finalize"));
        Assert.False(result.UsedProductWholeMember);
        Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
        Assert.Contains("CS0250", result.Detail);
    }

    [Fact]
    public void Evaluate_UsesProductWholePropertyForAccessors()
    {
        var assemblyPath = CompileFixture("""
            using System.ComponentModel;

            public sealed class PropertyWholeMemberFixture
            {
                private int _value;

                [Description("marker")]
                public int Value
                {
                    get => _value;
                    private set => _value = value;
                }

                public int this[int offset]
                {
                    get => _value + offset;
                    set => _value = value - offset;
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeHandle = Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "PropertyWholeMemberFixture");
            var type = reader.GetTypeDefinition(typeHandle);
            var valueProperty = Assert.Single(
                type.GetProperties(),
                handle => reader.GetString(reader.GetPropertyDefinition(handle).Name) == "Value");
            var valueAccessors = reader.GetPropertyDefinition(valueProperty).GetAccessors();

            using var source = MetadataSource.Open(assemblyPath);
            var wholeMember = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                valueAccessors.Setter,
                targeted: true,
                isPrimaryConstructor: false);

            Assert.NotNull(wholeMember);
            Assert.Contains("public int Value", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains("private set", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("[Description", wholeMember.Value.Text, StringComparison.Ordinal);

            var results = FidelityCheck.Evaluate(assemblyPath)
                .Where(result => result.Type == "PropertyWholeMemberFixture"
                    && result.Method is "get_Value" or "set_Value" or "get_Item" or "set_Item")
                .ToList();

            Assert.Equal(4, results.Count);
            foreach (var result in results)
            {
                Assert.True(result.UsedProductWholeMember, result.Method);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            }

            var targetedSetter = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "PropertyWholeMemberFixture",
                    method => method.Method == "set_Value"));
            Assert.True(targetedSetter.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, targetedSetter.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_UsesProductWholeEventForCustomAccessors()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class EventWholeMemberFixture
            {
                private EventHandler? _changed;
                private static EventHandler? _staticChanged;

                public event EventHandler? Changed
                {
                    add => _changed += value;
                    remove => _changed -= value;
                }

                public static event EventHandler? StaticChanged
                {
                    add => _staticChanged += value;
                    remove => _staticChanged -= value;
                }

                public event EventHandler? FieldLike;
            }

            public struct StructEventWholeMemberFixture
            {
                private EventHandler? _changed;

                public event EventHandler? Changed
                {
                    add => _changed += value;
                    remove => _changed -= value;
                }
            }

            public interface IEventContract
            {
                event EventHandler? Changed;
            }

            public sealed class ExplicitEventFixture : IEventContract
            {
                event EventHandler? IEventContract.Changed
                {
                    add { }
                    remove { }
                }
            }

            public class BaseEventFixture
            {
                private EventHandler? _changed;

                public virtual event EventHandler? Changed
                {
                    add => _changed += value;
                    remove => _changed -= value;
                }
            }

            public sealed class OverrideEventFixture : BaseEventFixture
            {
                private EventHandler? _changed;

                public override event EventHandler? Changed
                {
                    add => _changed += value;
                    remove => _changed -= value;
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "EventWholeMemberFixture"));
            var customEvent = reader.GetEventDefinition(Assert.Single(
                type.GetEvents(),
                handle => reader.GetString(reader.GetEventDefinition(handle).Name) == "Changed"));
            var fieldLikeEvent = reader.GetEventDefinition(Assert.Single(
                type.GetEvents(),
                handle => reader.GetString(reader.GetEventDefinition(handle).Name) == "FieldLike"));
            var explicitType = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "ExplicitEventFixture"));
            var explicitEvent = reader.GetEventDefinition(Assert.Single(explicitType.GetEvents()));
            var overrideType = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "OverrideEventFixture"));
            var overrideEvent = reader.GetEventDefinition(Assert.Single(overrideType.GetEvents()));

            using var source = MetadataSource.Open(assemblyPath);
            var wholeMember = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                customEvent.GetAccessors().Adder,
                targeted: true,
                isPrimaryConstructor: false);
            Assert.NotNull(wholeMember);
            Assert.Contains("public event EventHandler Changed", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains("add =>", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains("Delegate.Combine(_changed, value)", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains("remove =>", wholeMember.Value.Text, StringComparison.Ordinal);
            Assert.Contains("Delegate.Remove(_changed, value)", wholeMember.Value.Text, StringComparison.Ordinal);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                fieldLikeEvent.GetAccessors().Adder,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                explicitEvent.GetAccessors().Adder,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                overrideEvent.GetAccessors().Adder,
                targeted: true,
                isPrimaryConstructor: false));

            var results = FidelityCheck.Evaluate(assemblyPath)
                .Where(result => result.Type == "EventWholeMemberFixture"
                    && result.Method is "add_Changed" or "remove_Changed"
                        or "add_StaticChanged" or "remove_StaticChanged")
                .ToList();

            Assert.Equal(4, results.Count);
            foreach (var result in results)
            {
                Assert.True(result.UsedProductWholeMember, result.Method);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            }

            var structResults = FidelityCheck.Evaluate(assemblyPath)
                .Where(result => result.Type == "StructEventWholeMemberFixture"
                    && result.Method is "add_Changed" or "remove_Changed")
                .ToList();
            Assert.Equal(2, structResults.Count);
            foreach (var result in structResults)
            {
                Assert.True(result.UsedProductWholeMember, result.Method);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            }

            var targetedRemover = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "EventWholeMemberFixture",
                    method => method.Method == "remove_Changed"));
            Assert.True(targetedRemover.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, targetedRemover.Status);

            var overrideResults = FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "OverrideEventFixture",
                    method => method.Method is "add_Changed" or "remove_Changed")
                .ToList();
            Assert.Equal(2, overrideResults.Count);
            foreach (var result in overrideResults)
            {
                Assert.False(result.UsedProductWholeMember, result.Method);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void PropertyWholeMember_DeclinesExplicitImplementationsAndNonAutoStructs()
    {
        var assemblyPath = CompileFixture("""
            public interface IValue
            {
                int Value { get; }
            }

            public sealed class ExplicitValue : IValue
            {
                int IValue.Value => 42;
            }

            public readonly struct ComputedValue
            {
                private readonly int _value;

                public ComputedValue(int value) => _value = value;

                public int Value => _value + 1;
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var explicitType = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "ExplicitValue"));
            var explicitAccessor = Assert.Single(
                explicitType.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "IValue.get_Value");
            var structType = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "ComputedValue"));
            var structAccessor = Assert.Single(
                structType.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "get_Value");

            using var source = MetadataSource.Open(assemblyPath);
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                explicitAccessor,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                structAccessor,
                targeted: true,
                isPrimaryConstructor: false));

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "ComputedValue",
                    method => method.Method == "get_Value"));
            Assert.False(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetApiIndex_PreservesDeclaringExtensionMethodEntry(
        bool extensionDeclaredFirst)
    {
        const string widget = """
            // Both source orderings are intentional: the extended-type projection
            // and declaring method share one MethodDef token in either order.
            public sealed class Widget
            {
                public int Value;
            }
            """;

        const string extensions = """
            public static class WidgetExtensions
            {
                public static int Twice(this Widget value) => value.Value * 2;
            }
            """;

        var assemblyPath = CompileFixture(
            extensionDeclaredFirst
                ? $"{extensions}{Environment.NewLine}{widget}"
                : $"{widget}{Environment.NewLine}{extensions}");
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "WidgetExtensions"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "Twice");
            using var source = MetadataSource.Open(assemblyPath);

            var rendered = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false);

            Assert.NotNull(rendered);
            Assert.Contains("Twice", rendered.Value.Text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void StructFalseAutoProperty_RemainsOnLegacyFallback()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            var assemblyName = new AssemblyName("FalseStructAutoProperty");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = module.DefineType(
                "FalseAutoStruct",
                TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.SequentialLayout,
                typeof(ValueType));
            var compilerGenerated = new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
                    .GetConstructor(Type.EmptyTypes)!,
                []);
            var backingField = typeBuilder.DefineField(
                "<Value>k__BackingField",
                typeof(int),
                FieldAttributes.Private);
            backingField.SetCustomAttribute(compilerGenerated);
            var getter = typeBuilder.DefineMethod(
                "get_Value",
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                typeof(int),
                Type.EmptyTypes);
            getter.SetCustomAttribute(compilerGenerated);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_S, (sbyte)42);
            il.Emit(OpCodes.Ret);
            typeBuilder
                .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
                .SetGetMethod(getter);
            typeBuilder.CreateType();
            assemblyBuilder.Save(assemblyPath);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "FalseAutoStruct"));
            var accessor = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "get_Value");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                accessor,
                targeted: true,
                isPrimaryConstructor: false));

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "FalseAutoStruct",
                    method => method.Method == "get_Value"));
            Assert.False(result.UsedProductWholeMember);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_ReportsProductWholeMemberWhenConstructorRecompileFails()
    {
        var assemblyPath = CompileFixture("""
            using System.ComponentModel;

            internal sealed class DerivedDescriptionAttribute : DescriptionAttribute
            {
                public DerivedDescriptionAttribute(string text) : base(text)
                {
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    type => type == "DerivedDescriptionAttribute",
                    method => method.Method == ".ctor"));

            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.Contains("CS1729", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ConstructorShellAccessibility_PreservesBodySyntaxDiagnostics()
    {
        const string member = """
                private Fixture()
                {
                    Consume(,);
                }
            """;

        Assert.True(
            FidelityCheck.TryForcePublicConstructorAccessibility(
                member,
                out string normalized));
        Assert.Contains("public Fixture()", normalized, StringComparison.Ordinal);
        Assert.Contains("Consume(,);", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_EscapesProductWholeMemberNamespaces()
    {
        var assemblyPath = CompileFixture("""
            namespace Tags.@event
            {
                public sealed class Payload
                {
                }
            }

            namespace ConstructorHost
            {
                public sealed class KeywordNamespaceConstructor
                {
                    private readonly Tags.@event.Payload _value;

                    public KeywordNamespaceConstructor(Tags.@event.Payload value)
                    {
                        _value = value;
                    }
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    type => type == "ConstructorHost.KeywordNamespaceConstructor",
                    method => method.Method == ".ctor"));

            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsStructAutoProperties()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct StructAutoPropertyPairFixture
            {
                public StructAutoPropertyPairFixture(double left, double right)
                {
                    Left = left;
                    Right = right;
                }

                public double Left { get; }
                public double Right { get; }

                public double Sum() => this.Left + this.Right;
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            var ctor = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == ".ctor");
            var getter = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == "get_Left");
            var sum = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == "Sum");

            Assert.True(ctor.Status == FidelityCheck.CompileBackStatus.Exact, ctor.Detail);
            Assert.True(getter.Status == FidelityCheck.CompileBackStatus.Exact, getter.Detail);
            Assert.True(getter.UsedProductWholeMember);
            Assert.True(sum.Status == FidelityCheck.CompileBackStatus.Exact, sum.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsStructObjectToStringDispatch()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct StructWithToString
            {
                public override string ToString() => "value";
            }

            public static class StructWithToStringExtensions
            {
                public static string Humanize(this StructWithToString value, string? format)
                {
                    if (!string.IsNullOrWhiteSpace(format))
                        return format;
                    return value.ToString();
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "StructWithToStringExtensions" && result.Method == "Humanize");

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsExtensionMethodForwarding()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionForwardingFixture;

            public readonly struct TinyDate
            {
                public override string ToString() => "tiny";
            }

            public static class TinyDateExtensions
            {
                public static string Humanize(this TinyDate input, int style)
                    => input.ToString() + style.ToString();

                public static string Humanize(this TinyDate? input, int style)
                {
                    if (input.HasValue)
                    {
                        return input.Value.Humanize(style);
                    }
                    return "never";
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "ExtensionForwardingFixture.TinyDateExtensions"
                          && result.Method == "Humanize"
                          && result.Signature.Contains("Nullable", StringComparison.Ordinal));

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_UsesInheritedAndGenericReceiverCompatibility()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverFixture;

            public interface IBag<T> { }
            public class BaseBag : IBag<int> { }
            public class DerivedBag : BaseBag { }
            public class Other { }

            public static class RelevantExtensions
            {
                public static void Add(this BaseBag receiver, int value) { }
                public static Awaiter GetAwaiter(this IBag<int> receiver) => new();
            }

            public static class UnrelatedExtensions
            {
                public static void Add(this Other receiver, int value) { }
                public static Awaiter GetAwaiter(this Other receiver) => new();
            }

            public static class SameNameNonExtensions
            {
                public static void Add(Other receiver, int value) { }
                public static Awaiter GetAwaiter(Other receiver) => new();
            }

            public struct Awaiter
            {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public void GetResult() { }
            }
            """);
        try
        {
            var add = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverFixture.DerivedBag",
                ["ExtensionReceiverFixture.DerivedBag"],
                compatibleReceiverTypesComplete: false);
            var getAwaiter = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "GetAwaiter",
                "ExtensionReceiverFixture.DerivedBag",
                ["ExtensionReceiverFixture.DerivedBag"],
                compatibleReceiverTypesComplete: false);

            Assert.Equal(["ExtensionReceiverFixture.RelevantExtensions"], add.Roots);
            Assert.False(add.UsedFallback, add.FallbackReason);
            Assert.Equal(["ExtensionReceiverFixture.RelevantExtensions"], getAwaiter.Roots);
            Assert.False(getAwaiter.UsedFallback, getAwaiter.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_FallsBackWithExplicitProvenanceWhenReceiverIsUnknown()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverFallbackFixture;

            public class First { }
            public class Second { }

            public static class FirstExtensions
            {
                public static void Add(this First receiver, int value) { }
            }

            public static class SecondExtensions
            {
                public static void Add(this Second receiver, int value) { }
            }
            """);
        try
        {
                var metadataOnlySelection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                    "External.MissingReceiver");
                var selection = FidelityCheck.SelectExtensionRootsForTest(
                    assemblyPath,
                    "Add",
                "External.MissingReceiver",
                ["External.MissingReceiver"],
                compatibleReceiverTypesComplete: false);

            Assert.Equal(
                [
                    "ExtensionReceiverFallbackFixture.FirstExtensions",
                    "ExtensionReceiverFallbackFixture.SecondExtensions",
                ],
                selection.Roots);
            Assert.Equal(selection.Roots, metadataOnlySelection.Roots);
            Assert.True(metadataOnlySelection.UsedFallback);
            Assert.Equal(
                "receiver metadata unavailable for External.MissingReceiver",
                metadataOnlySelection.FallbackReason);
            Assert.True(selection.UsedFallback);
            Assert.Equal(
                "receiver hierarchy incomplete for External.MissingReceiver",
                selection.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_IncludesUnknownRootsAlongsideCompatibleGenericRoot()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverMixedFixture;

            public static class GenericExtensions
            {
                public static void Add<T>(this T receiver, string value) { }
            }

            public static class ArrayExtensions
            {
                public static void Add(this System.Array receiver, int value) { }
            }
            """);
        try
        {
            var selection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "System.Int32[]",
                ["System.Int32[]"],
                compatibleReceiverTypesComplete: false);

            Assert.Equal(
                [
                    "ExtensionReceiverMixedFixture.ArrayExtensions",
                    "ExtensionReceiverMixedFixture.GenericExtensions",
                ],
                selection.Roots);
            Assert.True(selection.UsedFallback);
            Assert.Equal(
                "receiver hierarchy incomplete for System.Int32[]",
                selection.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_IncludesObjectExtensionForInterfaceReceiver()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverInterfaceFixture;

            public interface IReceiver { }

            public static class ObjectExtensions
            {
                public static void Add(this object receiver, int value) { }
            }
            """);
        try
        {
            var metadataSelection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverInterfaceFixture.IReceiver");
            var semanticSelection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverInterfaceFixture.IReceiver",
                [
                    "ExtensionReceiverInterfaceFixture.IReceiver",
                    "System.Object",
                ],
                compatibleReceiverTypesComplete: true);

            Assert.Equal(
                ["ExtensionReceiverInterfaceFixture.ObjectExtensions"],
                metadataSelection.Roots);
            Assert.False(metadataSelection.UsedFallback, metadataSelection.FallbackReason);
            Assert.Equal(metadataSelection.Roots, semanticSelection.Roots);
            Assert.False(semanticSelection.UsedFallback, semanticSelection.FallbackReason);
            Assert.Equal(metadataSelection.FallbackReason, semanticSelection.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_UsesArityAwareReceiverIndexForGenericReceiver()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            namespace ExtensionReceiverArityFixture;

            public class Result { }
            public class Result<T> : IEnumerable<T>
            {
                public IEnumerator<T> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class EnumerableExtensions
            {
                public static void Add(this IEnumerable<int> receiver, int value) { }
            }
            """);
        try
        {
            var selection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverArityFixture.Result<int>",
                ["ExtensionReceiverArityFixture.Result<int>"],
                compatibleReceiverTypesComplete: false);

            Assert.Equal(
                ["ExtensionReceiverArityFixture.EnumerableExtensions"],
                selection.Roots);
            Assert.False(selection.UsedFallback, selection.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_DistinguishesReceiverTypesByGenericArity()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverArityFixture;

            public class NonGenericBase { }
            public class GenericBase { }
            public class Result : NonGenericBase { }
            public class Result<T> : GenericBase { }

            public static class NonGenericExtensions
            {
                public static void Add(this NonGenericBase receiver, string value) { }
            }

            public static class GenericExtensions
            {
                public static void Add(this GenericBase receiver, int value) { }
            }
            """);
        try
        {
            var nonGeneric = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverArityFixture.Result");
            var generic = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                "Add",
                "ExtensionReceiverArityFixture.Result<int>");

            Assert.Equal(
                ["ExtensionReceiverArityFixture.NonGenericExtensions"],
                nonGeneric.Roots);
            Assert.False(nonGeneric.UsedFallback, nonGeneric.FallbackReason);
            Assert.Equal(
                ["ExtensionReceiverArityFixture.GenericExtensions"],
                generic.Roots);
            Assert.False(generic.UsedFallback, generic.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ExtensionRootSelection_UsesArityAwareRoslynReceiverEvidence()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionReceiverArityEvidenceFixture;

            public interface IReceiver<T> { }
            public class Result<T> : IReceiver<T> { }

            public static class GenericReceiverExtensions
            {
                public static void Add<T>(this IReceiver<T> receiver, int value) { }
            }
            """);
        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                """
                class Consumer
                {
                    void Use(
                        ExtensionReceiverArityEvidenceFixture.Result<int> value)
                        => value.Add(1);
                }
                """,
                cancellationToken: TestContext.Current.CancellationToken);
            var compilation = CSharpCompilation.Create(
                "extension-receiver-arity-evidence",
                [tree],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(assemblyPath),
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var diagnostic = Assert.Single(
                compilation.GetDiagnostics(TestContext.Current.CancellationToken),
                candidate => candidate.Id == "CS1061");
            var reference = Assert.IsType<ClosureDiagnosticReference>(
                ClosureDiagnosticEvidence.Extract(
                    diagnostic,
                    compilation.GetSemanticModel(tree)));

            Assert.Equal(
                "ExtensionReceiverArityEvidenceFixture.Result`1",
                reference.ContainingType);
            Assert.Contains(
                "ExtensionReceiverArityEvidenceFixture.IReceiver`1",
                reference.CompatibleReceiverTypes!);

            var selection = FidelityCheck.SelectExtensionRootsForTest(
                assemblyPath,
                reference.Name,
                reference.ContainingType,
                reference.CompatibleReceiverTypes,
                reference.CompatibleReceiverTypesComplete);

            Assert.Equal(
                ["ExtensionReceiverArityEvidenceFixture.GenericReceiverExtensions"],
                selection.Roots);
            Assert.False(selection.UsedFallback, selection.FallbackReason);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunMethodDelta_UsesCorpusMetadataForPlatformOutParameters()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class TargetedDictionaryOutFixture
            {
                public bool Lookup(Dictionary<string, int> dictionary, string key)
                {
                    int value = default;
                    return dictionary.TryGetValue(key, out value);
                }
            }
            """);
        var originalOut = Console.Out;
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "TargetedDictionaryOutFixture" && result.Method == "Lookup");
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);

            var deltaPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "delta.json");
            File.WriteAllText(deltaPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                generatedUtc = DateTimeOffset.UtcNow,
                baselineGeneratedUtc = DateTimeOffset.UtcNow,
                currentGeneratedUtc = DateTimeOffset.UtcNow,
                baselineHasMethodDetails = true,
                currentHasMethodDetails = true,
                changedMethods = new[]
                {
                    new
                    {
                        method = "fixture!TargetedDictionaryOutFixture::Lookup#0",
                        assembly = "fixture",
                        assemblyPath = Path.GetFileName(assemblyPath),
                        type = "TargetedDictionaryOutFixture",
                        methodName = "Lookup",
                        overload = 0,
                        signature = result.Signature,
                        baseline = (object?)null,
                        current = new
                        {
                            assembly = "fixture",
                            assemblyPath = Path.GetFileName(assemblyPath),
                            type = "TargetedDictionaryOutFixture",
                            method = "Lookup",
                            overload = 0,
                            signature = result.Signature,
                            fidelity = "Full",
                            fullyRaised = true,
                            residual = (string?)null,
                            passBug = (string?)null,
                            validity = "not-sampled",
                            fidelityCheck = "not-sampled",
                        },
                        deltas = new[] { "triage" },
                    },
                },
            }));

            using var writer = new StringWriter();
            Console.SetOut(writer);
            int exitCode = await FidelityCheck.RunMethodDelta([assemblyPath], deltaPath, maxExamples: 5);
            Console.SetOut(originalOut);
            var output = writer.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains(
                "Changed-method engine: product-artifact RTS (raised; compile-back-floor=false)",
                output);
            Assert.Contains($"exact (contract v{FidelityCheck.CurrentContractVersion}): 1", output);
            Assert.DoesNotContain("CS1620", output);

            using var loweredWriter = new StringWriter();
            Console.SetOut(loweredWriter);
            exitCode = await FidelityCheck.RunMethodDelta(
                [assemblyPath],
                deltaPath,
                maxExamples: 5,
                lowered: true);
            Console.SetOut(originalOut);

            Assert.Equal(0, exitCode);
            Assert.Contains(
                "Changed-method engine: legacy whole-module (lowered)",
                loweredWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task EvaluateChangedMethodTargets_RejectsStaleSignatureWithoutDroppingRows()
    {
        var assemblyPath = CompileFixture("""
            public class ChangedMethodIdentityFixture
            {
                public int Pick(int value) => value + 1;
                public string Pick(string value) => value + "!";
            }
            """);
        try
        {
            var candidates = FidelityCheck.Evaluate(assemblyPath)
                .Where(result => result.Type == "ChangedMethodIdentityFixture"
                    && result.Method == "Pick")
                .OrderBy(result => result.Overload)
                .ToArray();
            Assert.Equal(2, candidates.Length);

            var targets = new[]
            {
                new FidelityCheck.CompileBackTarget(
                    assemblyPath,
                    candidates[0].Type,
                    candidates[0].Method,
                    candidates[0].Overload,
                    candidates[1].Signature),
                new FidelityCheck.CompileBackTarget(
                    assemblyPath,
                    candidates[1].Type,
                    candidates[1].Method,
                    candidates[1].Overload,
                    candidates[1].Signature),
            };

            var results = await FidelityCheck.EvaluateChangedMethodTargetsForTesting(
                [assemblyPath],
                targets);

            Assert.Equal(2, results.Count);
            Assert.Equal(targets[0].Signature, results[0].Signature);
            Assert.Equal(FidelityCheck.CompileBackStatus.ContextFail, results[0].Status);
            Assert.Equal("target-method-not-found", results[0].Detail);
            Assert.Equal(targets[1].Signature, results[1].Signature);
            Assert.NotEqual("target-method-not-found", results[1].Detail);
            Assert.All(
                results,
                result => Assert.Equal(
                    FidelityCheck.CaptureMode.ProductArtifact,
                    result.Capture));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task EvaluateChangedMethodTargets_PreservesProductNotFull()
    {
        string assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var current = Assert.Single(
            FidelityCheck.Evaluate(
                assemblyPath,
                type => type == typeof(CfgSampleClass).FullName,
                method => method.Method == nameof(CfgSampleClass.CapturedParamReadInOuterBody)));
        Assert.Equal(FidelityCheck.CompileBackStatus.NotFull, current.Status);

        var result = Assert.Single(
            await FidelityCheck.EvaluateChangedMethodTargetsForTesting(
                [assemblyPath],
                [
                    new FidelityCheck.CompileBackTarget(
                        assemblyPath,
                        current.Type,
                        current.Method,
                        current.Overload,
                        current.Signature),
                ]));

        Assert.Equal(FidelityCheck.CompileBackStatus.NotFull, result.Status);
        Assert.Equal(FidelityCheck.CaptureMode.ProductArtifact, result.Capture);
        Assert.Equal(
            "product-artifact RTS; compile-back-floor=false",
            result.CaptureDetail);
    }

    [Fact]
    public void Evaluate_BindsNamespacesReferencedByTargetBodies()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Concurrent;
            using System.Collections.Frozen;
            using System.Collections.Generic;
            using System.Text.RegularExpressions;

            public class FrameworkNamespaceFixture
            {
                public ConcurrentDictionary<string, int> CreateConcurrent()
                    => new ConcurrentDictionary<string, int>();

                public FrozenDictionary<string, int> Freeze(Dictionary<string, int> source)
                    => source.ToFrozenDictionary();

                public Match MatchS(string input)
                    => Regex.Match(input, "s");
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "CreateConcurrent");
            AssertCheckable(results, "Freeze");
            AssertCheckable(results, "MatchS");

            Environment.SetEnvironmentVariable("CB_NOGROUP", "1");
            var perMethodResults = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(perMethodResults, "CreateConcurrent");
            AssertCheckable(perMethodResults, "Freeze");
            AssertCheckable(perMethodResults, "MatchS");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CB_NOGROUP", null);
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "FrameworkNamespaceFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsAbstractOverloadsForForwardingCalls()
    {
        var assemblyPath = CompileFixture("""
            public abstract class AbstractForwardingFixture
            {
                public enum Gender
                {
                    Neutral,
                }

                public string Convert(long value)
                    => Convert(value, Gender.Neutral, true);

                public string ConvertToOrdinal(int value, bool words)
                    => ConvertToOrdinal(value);

                public abstract string Convert(long value, Gender gender, bool words);
                public abstract string ConvertToOrdinal(int value);
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "Convert");
            AssertCheckable(results, "ConvertToOrdinal");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "AbstractForwardingFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsExceptionBaseClause()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public class CustomException : Exception
            {
                public CustomException(string message)
                    : base(message)
                {
                }
            }

            public static class BaseAndInterfaceFixture
            {
                public static void ThrowCustom()
                    => throw new CustomException("bad");
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "ThrowCustom");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "BaseAndInterfaceFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsSatisfiedInterfaceBaseClause()
    {
        var assemblyPath = CompileFixture("""
            public interface IResource
            {
                string Name { get; }
            }

            public class Resource : IResource
            {
                public virtual string Name => "resource";
            }

            public sealed class ConnectionStringResource : Resource
            {
            }

            public interface IResourceBuilder<T>
                where T : IResource
            {
            }

            public static class ResourceBuilderFactory
            {
                public static IResourceBuilder<ConnectionStringResource> Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "ResourceBuilderFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsProtobufSelfMessageInterfaceClause()
    {
        var assemblyPath = CompileFixture("""
            namespace Google.Protobuf.Reflection
            {
                public class MessageDescriptor { }
            }

            namespace Google.Protobuf
            {
                public interface IMessage
                {
                    Google.Protobuf.Reflection.MessageDescriptor Descriptor { get; }
                }

                public interface IMessage<T> : IMessage
                    where T : IMessage<T>
                {
                }

                public class MessageParser<T>
                    where T : IMessage<T>
                {
                }
            }

            namespace Fixture
            {
                public sealed class Request : Google.Protobuf.IMessage<Request>
                {
                    public static Google.Protobuf.Reflection.MessageDescriptor Descriptor => throw null;
                    public Request Clone() => throw null;
                    public void WriteTo(object output) { }
                    public int CalculateSize() => 0;
                    public void MergeFrom(Request other) { }
                    public void MergeFrom(object input) { }
                    Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => Descriptor;
                }

                public static class ParserFactory
                {
                    public static Google.Protobuf.MessageParser<Request> Create()
                        => throw null;
                }
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "Fixture.ParserFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsGenericBaseAndStaticMetadataClause()
    {
        var assemblyPath = CompileFixture("""
            namespace Aspire.Hosting.Dcp.Model
            {
                public interface IKubernetesStaticMetadata
                {
                    string ObjectKind { get; }
                }

                public class CustomResource { }
                public class CustomResource<TSpec, TStatus> : CustomResource { }
                public sealed class ServiceSpec { }
                public sealed class ServiceStatus { }

                public sealed class Service : CustomResource<ServiceSpec, ServiceStatus>, IKubernetesStaticMetadata
                {
                    public static string ObjectKind => "Service";
                    string IKubernetesStaticMetadata.ObjectKind => ObjectKind;
                }
            }

            namespace Aspire.Hosting.Dcp
            {
                public class RenderedModelResource<T>
                    where T : Aspire.Hosting.Dcp.Model.CustomResource, Aspire.Hosting.Dcp.Model.IKubernetesStaticMetadata
                {
                }
            }

            public static class DcpFactory
            {
                public static Aspire.Hosting.Dcp.RenderedModelResource<Aspire.Hosting.Dcp.Model.Service> Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "DcpFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsNestedGenericBaseClause()
    {
        var assemblyPath = CompileFixture("""
            public class Outer
            {
                public class Base<T>
                {
                }

                public sealed class Derived : Base<int>
                {
                }
            }

            public static class NestedGenericBaseFactory
            {
                public static Outer.Derived Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "NestedGenericBaseFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsResourceCollectionEnumerableClause()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            using System.Linq;

            namespace Aspire.Hosting.ApplicationModel
            {
                public interface IResource
                {
                    string Name { get; }
                }

                public interface IResourceCollection : IList<IResource>
                {
                    bool TryGetByName(string name, out IResource resource);
                }
            }

            public static class ResourceCollectionQueries
            {
                public static Aspire.Hosting.ApplicationModel.IResource Find(
                    Aspire.Hosting.ApplicationModel.IResourceCollection resources,
                    string name)
                    => resources.SingleOrDefault(resource => resource.Name == name);
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "ResourceCollectionQueries", "Find");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsResourceAnnotationCollectionSurface()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            using System.Linq;

            namespace Aspire.Hosting.ApplicationModel
            {
                public interface IResourceAnnotation { }

                public sealed class HttpAnnotation : IResourceAnnotation { }

                public class ResourceAnnotationCollection : System.Collections.ObjectModel.Collection<IResourceAnnotation>
                {
                }
            }

            public static class AnnotationQueries
            {
                public static bool HasHttp(Aspire.Hosting.ApplicationModel.ResourceAnnotationCollection annotations)
                {
                    annotations.Add(new Aspire.Hosting.ApplicationModel.HttpAnnotation());
                    foreach (var annotation in annotations)
                        if (annotation is Aspire.Hosting.ApplicationModel.HttpAnnotation)
                            return annotations.OfType<Aspire.Hosting.ApplicationModel.HttpAnnotation>().Any();
                    return false;
                }
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "AnnotationQueries", "HasHttp");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectSharedFrameworkDirectory_PrefersExactThenNearestSameMajorMinor()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-frameworks-").FullName;
        try
        {
            string exact = Path.Combine(root, "11.0.1");
            string sameBandLower = Path.Combine(root, "11.0.0");
            string sameBandHigher = Path.Combine(root, "11.0.3");
            string preview = Path.Combine(root, "12.0.0-preview.6.1");
            string otherBand = Path.Combine(root, "11.1.9");
            Directory.CreateDirectory(exact);
            Directory.CreateDirectory(sameBandLower);
            Directory.CreateDirectory(sameBandHigher);
            Directory.CreateDirectory(preview);
            Directory.CreateDirectory(otherBand);

            Assert.Equal(exact, AssemblyDependencyResolver.SelectSharedFrameworkDirectory(root, "11.0.1"));

            Directory.Delete(exact);
            Assert.Equal(sameBandHigher, AssemblyDependencyResolver.SelectSharedFrameworkDirectory(root, "11.0.2"));
            Assert.Equal(preview, AssemblyDependencyResolver.SelectSharedFrameworkDirectory(root, "12.0.0-preview.6.2"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string type, string method)
    {
        var result = Assert.Single(
            results,
            result => result.Type == type && result.Method == method);
        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            result.Detail);
    }

    static string CompileFixture(
        string source,
        bool allowUnsafe = false,
        string assemblyName = "fixture")
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{assemblyName}.dll");
        var references = RoslynTestReferences.TrustedPlatform.AsEnumerable();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable));

        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    static string CreateUnrepresentableIdentityFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "UnrepresentableIdentity.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("UnrepresentableIdentity"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("UnrepresentableIdentity");
        TypeBuilder badSignatureType = module.DefineType(
            "bad-namespace.BadType",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        DefineConstantMethod(badSignatureType, "Hidden", typeof(int));

        TypeBuilder goodType = module.DefineType(
            "GoodType",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        DefineConstantMethod(goodType, "Good", typeof(int));
        DefineConstantMethod(goodType, "event", typeof(int));
        DefineConstantMethod(goodType, "bad-name", typeof(int));
        MethodBuilder badSignature = goodType.DefineMethod(
            "BadSignature",
            MethodAttributes.Public | MethodAttributes.Static,
            badSignatureType,
            Type.EmptyTypes);
        ILGenerator badSignatureBody = badSignature.GetILGenerator();
        badSignatureBody.Emit(OpCodes.Ldnull);
        badSignatureBody.Emit(OpCodes.Ret);

        MethodBuilder badConstraint = goodType.DefineMethod(
            "BadConstraint",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        badConstraint.DefineGenericParameters("T")[0]
            .SetBaseTypeConstraint(badSignatureType);
        ILGenerator badConstraintBody = badConstraint.GetILGenerator();
        badConstraintBody.Emit(OpCodes.Ldc_I4_1);
        badConstraintBody.Emit(OpCodes.Ret);

        TypeBuilder operatorType = module.DefineType(
            "OperatorType",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Sealed);
        MethodBuilder validOperator = operatorType.DefineMethod(
            "op_Subtraction",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            typeof(int),
            [operatorType, typeof(int)]);
        ILGenerator validOperatorBody = validOperator.GetILGenerator();
        validOperatorBody.Emit(OpCodes.Ldc_I4_1);
        validOperatorBody.Emit(OpCodes.Ret);

        MethodBuilder malformedOperator = operatorType.DefineMethod(
            "op_Addition",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator malformedOperatorBody = malformedOperator.GetILGenerator();
        malformedOperatorBody.Emit(OpCodes.Ldc_I4_1);
        malformedOperatorBody.Emit(OpCodes.Ret);

        TypeBuilder genericOperatorType = module.DefineType(
            "GenericOperatorType`1",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Sealed);
        GenericTypeParameterBuilder genericParameter =
            genericOperatorType.DefineGenericParameters("T")[0];
        Type openDeclaringType = genericOperatorType.MakeGenericType(
            genericParameter);
        Type wrongConstructedType = genericOperatorType.MakeGenericType(
            typeof(int));
        MethodBuilder validGenericOperator = genericOperatorType.DefineMethod(
            "op_Division",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            typeof(int),
            [openDeclaringType, typeof(int)]);
        ILGenerator validGenericOperatorBody =
            validGenericOperator.GetILGenerator();
        validGenericOperatorBody.Emit(OpCodes.Ldc_I4_1);
        validGenericOperatorBody.Emit(OpCodes.Ret);
        MethodBuilder malformedGenericOperator =
            genericOperatorType.DefineMethod(
                "op_Multiply",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                typeof(int),
                [wrongConstructedType, typeof(int)]);
        ILGenerator malformedGenericOperatorBody =
            malformedGenericOperator.GetILGenerator();
        malformedGenericOperatorBody.Emit(OpCodes.Ldc_I4_1);
        malformedGenericOperatorBody.Emit(OpCodes.Ret);
        MethodBuilder ordinaryOperatorName =
            operatorType.DefineMethod(
                "op_UnaryNegation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                [operatorType]);
        ILGenerator ordinaryOperatorNameBody =
            ordinaryOperatorName.GetILGenerator();
        ordinaryOperatorNameBody.Emit(OpCodes.Ldc_I4_1);
        ordinaryOperatorNameBody.Emit(OpCodes.Ret);

        badSignatureType.CreateType();
        goodType.CreateType();
        operatorType.CreateType();
        genericOperatorType.CreateType();
        assembly.Save(path);
        return path;
    }

    static string CreateInvalidConstraintSignatureFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "InvalidConstraintSignature.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("InvalidConstraintSignature.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InvalidConstraintSignature"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("InvalidConstraintSignatureFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeReferenceHandle modifier = metadata.AddTypeReference(
            resolutionScope: default,
            @namespace: metadata.GetOrAddString("bad-namespace"),
            name: metadata.GetOrAddString("BadModifier"));
        TypeReferenceHandle disposable = metadata.AddTypeReference(
            resolutionScope: default,
            @namespace: metadata.GetOrAddString("System"),
            name: metadata.GetOrAddString("IDisposable"));
        TypeReferenceHandle @object = metadata.AddTypeReference(
            resolutionScope: default,
            @namespace: metadata.GetOrAddString("System"),
            name: metadata.GetOrAddString("Object"));
        TypeReferenceHandle valueType = metadata.AddTypeReference(
            resolutionScope: default,
            @namespace: metadata.GetOrAddString("System"),
            name: metadata.GetOrAddString("ValueType"));
        AssemblyReferenceHandle fakeCore = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Fake.Core"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    (byte[])
                    [
                        0xb0, 0x3f, 0x5f, 0x7f,
                        0x11, 0xd5, 0x0a, 0x3a,
                    ]),
                flags: default,
                hashValue: default);
        TypeReferenceHandle fakeObject = metadata.AddTypeReference(
            resolutionScope: fakeCore,
            @namespace: metadata.GetOrAddString("System"),
            name: metadata.GetOrAddString("Object"));
        TypeReferenceHandle fakeUnmanagedModifier =
            metadata.AddTypeReference(
                resolutionScope: fakeCore,
                @namespace: metadata.GetOrAddString(
                    "System.Runtime.InteropServices"),
                name: metadata.GetOrAddString("UnmanagedType"));
        TypeReferenceHandle coreValueType = metadata.AddTypeReference(
            resolutionScope: systemRuntime,
            @namespace: metadata.GetOrAddString("System"),
            name: metadata.GetOrAddString("ValueType"));
        TypeReferenceHandle isUnmanagedAttribute =
            metadata.AddTypeReference(
                resolutionScope: systemRuntime,
                @namespace: metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                name: metadata.GetOrAddString(
                    "IsUnmanagedAttribute"));
        MemberReferenceHandle isUnmanagedConstructor =
            metadata.AddMemberReference(
                isUnmanagedAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    (byte[])[0x20, 0x00, 0x01]));
        TypeReferenceHandle enumerable = metadata.AddTypeReference(
            resolutionScope: fakeCore,
            @namespace: metadata.GetOrAddString(
                "System.Collections.Generic"),
            name: metadata.GetOrAddString("IEnumerable`1"));

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x10);
        methodSignature.WriteCompressedInteger(1);
        methodSignature.WriteCompressedInteger(0);
        methodSignature.WriteByte(0x08);

        var methodBodies = new BlobBuilder();
        var methodBodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int AddBody()
        {
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(
                instructions,
                new ControlFlowBuilder());
            encoder.OpCode(ILOpCode.Ldc_i4_1);
            encoder.OpCode(ILOpCode.Ret);
            return methodBodyEncoder.AddMethodBody(encoder, maxStack: 1);
        }

        MethodDefinitionHandle badIndexMethod = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("BadGenericIndex"),
            metadata.GetOrAddBlob(methodSignature),
            AddBody(),
            MetadataTokens.ParameterHandle(1));
        GenericParameterHandle badIndexParameter = metadata.AddGenericParameter(
            badIndexMethod,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        var badIndexConstraint = new BlobBuilder();
        badIndexConstraint.WriteByte(0x1E);
        badIndexConstraint.WriteCompressedInteger(1);
        metadata.AddGenericParameterConstraint(
            badIndexParameter,
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(badIndexConstraint)));

        void AddModifiedConstraintMethod(
            string name,
            TypeReferenceHandle underlyingType)
        {
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(methodSignature),
                AddBody(),
                MetadataTokens.ParameterHandle(1));
            GenericParameterHandle parameter = metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("U"),
                index: 0);
            var constraint = new BlobBuilder();
            constraint.WriteByte(0x1F);
            constraint.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(modifier) << 2) | 1);
            constraint.WriteByte(0x12);
            constraint.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(underlyingType) << 2) | 1);
            metadata.AddGenericParameterConstraint(
                parameter,
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(constraint)));
        }

        AddModifiedConstraintMethod("ModifiedConstraint", disposable);
        AddModifiedConstraintMethod("ModifiedObjectConstraint", @object);
        AddModifiedConstraintMethod("ModifiedValueTypeConstraint", valueType);

        MethodDefinitionHandle fakeUnmanagedMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("FakeUnmanagedConstraint"),
                metadata.GetOrAddBlob(methodSignature),
                AddBody(),
                MetadataTokens.ParameterHandle(1));
        GenericParameterHandle fakeUnmanagedParameter =
            metadata.AddGenericParameter(
                fakeUnmanagedMethod,
                GenericParameterAttributes.NotNullableValueTypeConstraint
                    | GenericParameterAttributes.DefaultConstructorConstraint,
                metadata.GetOrAddString("U"),
                index: 0);
        metadata.AddCustomAttribute(
            fakeUnmanagedParameter,
            isUnmanagedConstructor,
            metadata.GetOrAddBlob(
                (byte[])[0x01, 0x00, 0x00, 0x00]));
        var fakeUnmanagedConstraint = new BlobBuilder();
        fakeUnmanagedConstraint.WriteByte(0x1F);
        fakeUnmanagedConstraint.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(fakeUnmanagedModifier) << 2)
                | 1);
        fakeUnmanagedConstraint.WriteByte(0x11);
        fakeUnmanagedConstraint.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(coreValueType) << 2)
                | 1);
        metadata.AddGenericParameterConstraint(
            fakeUnmanagedParameter,
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(fakeUnmanagedConstraint)));

        MethodDefinitionHandle flaglessValueTypeMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("FlaglessValueTypeConstraint"),
                metadata.GetOrAddBlob(methodSignature),
                AddBody(),
                MetadataTokens.ParameterHandle(1));
        GenericParameterHandle flaglessValueTypeParameter =
            metadata.AddGenericParameter(
                flaglessValueTypeMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("U"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            flaglessValueTypeParameter,
            coreValueType);

        MethodDefinitionHandle unmodifiedUnmanagedMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("UnmodifiedUnmanagedConstraint"),
                metadata.GetOrAddBlob(methodSignature),
                AddBody(),
                MetadataTokens.ParameterHandle(1));
        GenericParameterHandle unmodifiedUnmanagedParameter =
            metadata.AddGenericParameter(
                unmodifiedUnmanagedMethod,
                GenericParameterAttributes.NotNullableValueTypeConstraint
                    | GenericParameterAttributes.DefaultConstructorConstraint,
                metadata.GetOrAddString("U"),
                index: 0);
        metadata.AddCustomAttribute(
            unmodifiedUnmanagedParameter,
            isUnmanagedConstructor,
            metadata.GetOrAddBlob(
                (byte[])[0x01, 0x00, 0x00, 0x00]));
        metadata.AddGenericParameterConstraint(
            unmodifiedUnmanagedParameter,
            coreValueType);

        void AddConstraintMethod(
            string name,
            EntityHandle constraintType)
        {
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(methodSignature),
                AddBody(),
                MetadataTokens.ParameterHandle(1));
            GenericParameterHandle parameter = metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("V"),
                index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                constraintType);
        }

        var arityMismatchConstraint = new BlobBuilder();
        arityMismatchConstraint.WriteByte(0x15);
        arityMismatchConstraint.WriteByte(0x12);
        arityMismatchConstraint.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(enumerable) << 2) | 1);
        arityMismatchConstraint.WriteCompressedInteger(2);
        arityMismatchConstraint.WriteByte(0x08);
        arityMismatchConstraint.WriteByte(0x08);
        AddConstraintMethod(
            "ArityMismatchConstraint",
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(arityMismatchConstraint)));
        AddConstraintMethod("FakeObjectConstraint", fakeObject);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateCustomModifiedSignatureFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "CustomModifiedSignatures.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("CustomModifiedSignatures"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "CustomModifiedSignatures");
        TypeBuilder modifierType = module.DefineType(
            "SignatureModifier",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        TypeBuilder fixtureType = module.DefineType(
            "CustomModifiedSignatureFixture",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);

        DefineConstantMethod(fixtureType, "Good", typeof(int));

        MethodBuilder modifiedReturn = fixtureType.DefineMethod(
            "ModifiedReturn",
            MethodAttributes.Public | MethodAttributes.Static);
        modifiedReturn.SetSignature(
            typeof(int),
            [modifierType],
            null,
            Type.EmptyTypes,
            null,
            null);
        ILGenerator modifiedReturnBody = modifiedReturn.GetILGenerator();
        modifiedReturnBody.Emit(OpCodes.Ldc_I4_1);
        modifiedReturnBody.Emit(OpCodes.Ret);

        MethodBuilder optionalModifiedReturn = fixtureType.DefineMethod(
            "OptionalModifiedReturn",
            MethodAttributes.Public | MethodAttributes.Static);
        optionalModifiedReturn.SetSignature(
            typeof(int),
            null,
            [modifierType],
            Type.EmptyTypes,
            null,
            null);
        ILGenerator optionalModifiedReturnBody =
            optionalModifiedReturn.GetILGenerator();
        optionalModifiedReturnBody.Emit(OpCodes.Ldc_I4_1);
        optionalModifiedReturnBody.Emit(OpCodes.Ret);

        MethodBuilder alternateReadonlyReturn = fixtureType.DefineMethod(
            "AlternateReadonlyReturn",
            MethodAttributes.Public | MethodAttributes.Static);
        alternateReadonlyReturn.SetSignature(
            typeof(int).MakeByRefType(),
            [typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)],
            null,
            Type.EmptyTypes,
            null,
            null);
        ILGenerator alternateReadonlyReturnBody =
            alternateReadonlyReturn.GetILGenerator();
        alternateReadonlyReturnBody.Emit(OpCodes.Ldnull);
        alternateReadonlyReturnBody.Emit(OpCodes.Throw);

        MethodBuilder attributeOnlyReadonlyReturn = fixtureType.DefineMethod(
            "AttributeOnlyReadonlyReturn",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int).MakeByRefType(),
            Type.EmptyTypes);
        attributeOnlyReadonlyReturn.DefineParameter(
                0,
                ParameterAttributes.None,
                null)
            .SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)
                        .GetConstructor(Type.EmptyTypes)!,
                    []));
        ILGenerator attributeOnlyReadonlyReturnBody =
            attributeOnlyReadonlyReturn.GetILGenerator();
        attributeOnlyReadonlyReturnBody.Emit(OpCodes.Ldnull);
        attributeOnlyReadonlyReturnBody.Emit(OpCodes.Throw);

        MethodBuilder modifiedParameter = fixtureType.DefineMethod(
            "ModifiedParameter",
            MethodAttributes.Public | MethodAttributes.Static);
        modifiedParameter.SetSignature(
            typeof(int),
            null,
            null,
            [typeof(int)],
            [[modifierType]],
            null);
        ILGenerator modifiedParameterBody =
            modifiedParameter.GetILGenerator();
        modifiedParameterBody.Emit(OpCodes.Ldc_I4_1);
        modifiedParameterBody.Emit(OpCodes.Ret);

        MethodBuilder modifiedInParameter = fixtureType.DefineMethod(
            "ModifiedInParameter",
            MethodAttributes.Public | MethodAttributes.Static);
        modifiedInParameter.SetSignature(
            typeof(int),
            null,
            null,
            [typeof(int).MakeByRefType()],
            [[typeof(System.Runtime.InteropServices.InAttribute)]],
            null);
        modifiedInParameter.DefineParameter(
            1,
            ParameterAttributes.In,
            "value");
        ILGenerator modifiedInParameterBody =
            modifiedInParameter.GetILGenerator();
        modifiedInParameterBody.Emit(OpCodes.Ldc_I4_1);
        modifiedInParameterBody.Emit(OpCodes.Ret);

        modifierType.CreateType();
        fixtureType.CreateType();
        assembly.Save(path);
        return path;
    }

    static string CreateMalformedPropertyFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "MalformedProperties.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("MalformedProperties"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "MalformedProperties");
        TypeBuilder fixtureType = module.DefineType(
            "MalformedPropertyFixture",
            TypeAttributes.Public | TypeAttributes.Class);
        FieldBuilder intField = fixtureType.DefineField(
            "_intValue",
            typeof(int),
            FieldAttributes.Private | FieldAttributes.Static);
        FieldBuilder longField = fixtureType.DefineField(
            "_longValue",
            typeof(long),
            FieldAttributes.Private | FieldAttributes.Static);
        fixtureType.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(DefaultMemberAttribute)
                    .GetConstructor([typeof(string)])!,
                ["Item"]));

        DefineConstantMethod(fixtureType, "Good", typeof(int));
        MethodBuilder validIndexerGetter = fixtureType.DefineMethod(
            "get_Item",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            [typeof(int)]);
        validIndexerGetter.DefineParameter(
            1,
            ParameterAttributes.None,
            "index");
        ILGenerator validIndexerGetterBody =
            validIndexerGetter.GetILGenerator();
        validIndexerGetterBody.Emit(OpCodes.Ldarg_1);
        validIndexerGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder validIndexer = fixtureType.DefineProperty(
            "Item",
            PropertyAttributes.None,
            CallingConventions.HasThis,
            typeof(int),
            [typeof(int)]);
        validIndexer.SetGetMethod(validIndexerGetter);

        MethodBuilder malformedIndexerGetter = fixtureType.DefineMethod(
            "get_BadIndexer",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator malformedIndexerGetterBody =
            malformedIndexerGetter.GetILGenerator();
        malformedIndexerGetterBody.Emit(OpCodes.Ldc_I4_1);
        malformedIndexerGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder malformedIndexer = fixtureType.DefineProperty(
            "this[]",
            PropertyAttributes.None,
            CallingConventions.HasThis,
            typeof(int),
            Type.EmptyTypes);
        malformedIndexer.SetGetMethod(malformedIndexerGetter);

        MethodBuilder ordinaryGetter = fixtureType.DefineMethod(
            "get_Ordinary",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            [typeof(int)]);
        ordinaryGetter.DefineParameter(
            1,
            ParameterAttributes.None,
            "index");
        ILGenerator ordinaryGetterBody = ordinaryGetter.GetILGenerator();
        ordinaryGetterBody.Emit(OpCodes.Ldarg_1);
        ordinaryGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder ordinary = fixtureType.DefineProperty(
            "Ordinary",
            PropertyAttributes.None,
            CallingConventions.HasThis,
            typeof(int),
            [typeof(int)]);
        ordinary.SetGetMethod(ordinaryGetter);

        MethodBuilder varargGetter = fixtureType.DefineMethod(
            "get_Vararg",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            CallingConventions.HasThis | CallingConventions.VarArgs,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator varargGetterBody = varargGetter.GetILGenerator();
        varargGetterBody.Emit(OpCodes.Ldc_I4_1);
        varargGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder varargProperty = fixtureType.DefineProperty(
            "Vararg",
            PropertyAttributes.None,
            CallingConventions.HasThis,
            typeof(int),
            Type.EmptyTypes);
        varargProperty.SetGetMethod(varargGetter);

        const MethodAttributes staticAccessorAttributes =
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
        MethodBuilder modifiedRefGetter = fixtureType.DefineMethod(
            "get_ModifiedRef",
            staticAccessorAttributes,
            CallingConventions.Standard,
            typeof(int).MakeByRefType(),
            [typeof(ObsoleteAttribute)],
            null,
            Type.EmptyTypes,
            null,
            null);
        ILGenerator modifiedRefGetterBody =
            modifiedRefGetter.GetILGenerator();
        modifiedRefGetterBody.Emit(OpCodes.Ldsflda, intField);
        modifiedRefGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder modifiedRefProperty = fixtureType.DefineProperty(
            "ModifiedRef",
            PropertyAttributes.None,
            CallingConventions.Standard,
            typeof(int).MakeByRefType(),
            null,
            null,
            Type.EmptyTypes,
            null,
            null);
        modifiedRefProperty.SetGetMethod(modifiedRefGetter);

        MethodBuilder mismatchedRefGetter = fixtureType.DefineMethod(
            "get_MismatchedRef",
            staticAccessorAttributes,
            CallingConventions.Standard,
            typeof(long).MakeByRefType(),
            null,
            null,
            Type.EmptyTypes,
            null,
            null);
        ILGenerator mismatchedRefGetterBody =
            mismatchedRefGetter.GetILGenerator();
        mismatchedRefGetterBody.Emit(OpCodes.Ldsflda, longField);
        mismatchedRefGetterBody.Emit(OpCodes.Ret);
        PropertyBuilder mismatchedRefProperty = fixtureType.DefineProperty(
            "MismatchedRef",
            PropertyAttributes.None,
            CallingConventions.Standard,
            typeof(int).MakeByRefType(),
            null,
            null,
            Type.EmptyTypes,
            null,
            null);
        mismatchedRefProperty.SetGetMethod(mismatchedRefGetter);

        const MethodAttributes eventAccessorAttributes =
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
        MethodBuilder goodAdder = fixtureType.DefineMethod(
            "add_GoodEvent",
            eventAccessorAttributes,
            typeof(void),
            [typeof(Action)]);
        goodAdder.GetILGenerator().Emit(OpCodes.Ret);
        MethodBuilder goodRemover = fixtureType.DefineMethod(
            "remove_GoodEvent",
            eventAccessorAttributes,
            typeof(void),
            [typeof(Action)]);
        goodRemover.GetILGenerator().Emit(OpCodes.Ret);
        EventBuilder goodEvent = fixtureType.DefineEvent(
            "GoodEvent",
            EventAttributes.None,
            typeof(Action));
        goodEvent.SetAddOnMethod(goodAdder);
        goodEvent.SetRemoveOnMethod(goodRemover);

        MethodBuilder varargAdder = fixtureType.DefineMethod(
            "add_VarargEvent",
            eventAccessorAttributes,
            CallingConventions.HasThis | CallingConventions.VarArgs,
            typeof(void),
            [typeof(Action)]);
        varargAdder.GetILGenerator().Emit(OpCodes.Ret);
        MethodBuilder varargRemover = fixtureType.DefineMethod(
            "remove_VarargEvent",
            eventAccessorAttributes,
            typeof(void),
            [typeof(Action)]);
        varargRemover.GetILGenerator().Emit(OpCodes.Ret);
        EventBuilder varargEvent = fixtureType.DefineEvent(
            "VarargEvent",
            EventAttributes.None,
            typeof(Action));
        varargEvent.SetAddOnMethod(varargAdder);
        varargEvent.SetRemoveOnMethod(varargRemover);

        MethodBuilder badSignatureAdder = fixtureType.DefineMethod(
            "add_BadEventSignature",
            eventAccessorAttributes,
            typeof(int),
            [typeof(Action)]);
        ILGenerator badSignatureAdderBody =
            badSignatureAdder.GetILGenerator();
        badSignatureAdderBody.Emit(OpCodes.Ldc_I4_1);
        badSignatureAdderBody.Emit(OpCodes.Ret);
        MethodBuilder badSignatureRemover = fixtureType.DefineMethod(
            "remove_BadEventSignature",
            eventAccessorAttributes,
            typeof(void),
            [typeof(string)]);
        badSignatureRemover.GetILGenerator().Emit(OpCodes.Ret);
        EventBuilder badSignatureEvent = fixtureType.DefineEvent(
            "BadEventSignature",
            EventAttributes.None,
            typeof(Action));
        badSignatureEvent.SetAddOnMethod(badSignatureAdder);
        badSignatureEvent.SetRemoveOnMethod(badSignatureRemover);

        MethodBuilder voidGetter = fixtureType.DefineMethod(
            "get_Void",
            eventAccessorAttributes,
            typeof(void),
            Type.EmptyTypes);
        voidGetter.GetILGenerator().Emit(OpCodes.Ret);
        PropertyBuilder voidProperty = fixtureType.DefineProperty(
            "Void",
            PropertyAttributes.None,
            CallingConventions.HasThis,
            typeof(void),
            Type.EmptyTypes);
        voidProperty.SetGetMethod(voidGetter);

        fixtureType.CreateType();

        TypeBuilder malformedIndexerType = module.DefineType(
            "MalformedIndexerShapeFixture",
            TypeAttributes.Public | TypeAttributes.Class);
        malformedIndexerType.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(DefaultMemberAttribute)
                    .GetConstructor([typeof(string)])!,
                ["BadItem"]));
        MethodBuilder zeroParameterGetter = malformedIndexerType.DefineMethod(
            "get_BadItem",
            eventAccessorAttributes,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator zeroParameterBody =
            zeroParameterGetter.GetILGenerator();
        zeroParameterBody.Emit(OpCodes.Ldc_I4_1);
        zeroParameterBody.Emit(OpCodes.Ret);
        PropertyBuilder zeroParameterIndexer =
            malformedIndexerType.DefineProperty(
                "BadItem",
                PropertyAttributes.None,
                CallingConventions.HasThis,
                typeof(int),
                Type.EmptyTypes);
        zeroParameterIndexer.SetGetMethod(zeroParameterGetter);

        MethodBuilder byRefParameterGetter =
            malformedIndexerType.DefineMethod(
                "get_BadItem",
                eventAccessorAttributes,
                typeof(int),
                [typeof(int).MakeByRefType()]);
        byRefParameterGetter.DefineParameter(
            1,
            ParameterAttributes.None,
            "index");
        ILGenerator byRefParameterBody =
            byRefParameterGetter.GetILGenerator();
        byRefParameterBody.Emit(OpCodes.Ldc_I4_1);
        byRefParameterBody.Emit(OpCodes.Ret);
        PropertyBuilder byRefParameterIndexer =
            malformedIndexerType.DefineProperty(
                "BadItem",
                PropertyAttributes.None,
                CallingConventions.HasThis,
                typeof(int),
                [typeof(int).MakeByRefType()]);
        byRefParameterIndexer.SetGetMethod(byRefParameterGetter);
        malformedIndexerType.CreateType();

        assembly.Save(path);
        return path;
    }

    static string CreateAccessorNameFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "AccessorNames.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("AccessorNames"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("AccessorNames");
        var interfaceProperty = DefinePropertyInterface(module);
        var interfaceEvent = DefineEventInterface(module);
        TypeBuilder fixtureType = module.DefineType(
            "AccessorNameFixture",
            TypeAttributes.Public | TypeAttributes.Class);
        fixtureType.AddInterfaceImplementation(interfaceProperty.Type);
        fixtureType.AddInterfaceImplementation(interfaceEvent.Type);

        const MethodAttributes ordinaryAccessorAttributes =
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
        DefineProperty(
            fixtureType,
            "Value",
            "get_Value",
            "set_Value",
            ordinaryAccessorAttributes);
        DefineEvent(
            fixtureType,
            "Changed",
            "add_Changed",
            "remove_Changed",
            ordinaryAccessorAttributes);
        DefineProperty(
            fixtureType,
            "BadGetter",
            "get_WrongGetter",
            "set_BadGetter",
            ordinaryAccessorAttributes);
        DefineProperty(
            fixtureType,
            "BadSetter",
            "get_BadSetter",
            "set_WrongSetter",
            ordinaryAccessorAttributes);
        DefineEvent(
            fixtureType,
            "BadAdder",
            "add_WrongAdder",
            "remove_BadAdder",
            ordinaryAccessorAttributes);
        DefineEvent(
            fixtureType,
            "BadRemover",
            "add_BadRemover",
            "remove_WrongRemover",
            ordinaryAccessorAttributes);

        const MethodAttributes explicitAccessorAttributes =
            MethodAttributes.Private
                | MethodAttributes.Final
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
        (MethodBuilder getter, MethodBuilder setter) = DefineProperty(
            fixtureType,
            "INameProperty.Value",
            "INameProperty.get_Value",
            "INameProperty.set_Value",
            explicitAccessorAttributes);
        fixtureType.DefineMethodOverride(
            getter,
            interfaceProperty.Getter);
        fixtureType.DefineMethodOverride(
            setter,
            interfaceProperty.Setter);
        (MethodBuilder adder, MethodBuilder remover) = DefineEvent(
            fixtureType,
            "INameEvent.Changed",
            "INameEvent.add_Changed",
            "INameEvent.remove_Changed",
            explicitAccessorAttributes);
        fixtureType.DefineMethodOverride(
            adder,
            interfaceEvent.Adder);
        fixtureType.DefineMethodOverride(
            remover,
            interfaceEvent.Remover);

        fixtureType.CreateType();
        assembly.Save(path);
        return path;

        static (
            Type Type,
            MethodBuilder Getter,
            MethodBuilder Setter) DefinePropertyInterface(
                ModuleBuilder module)
        {
            TypeBuilder type = module.DefineType(
                "INameProperty",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            const MethodAttributes attributes =
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig;
            MethodBuilder getter = type.DefineMethod(
                "get_Value",
                attributes,
                typeof(int),
                Type.EmptyTypes);
            MethodBuilder setter = type.DefineMethod(
                "set_Value",
                attributes,
                typeof(void),
                [typeof(int)]);
            PropertyBuilder property = type.DefineProperty(
                "Value",
                PropertyAttributes.None,
                CallingConventions.HasThis,
                typeof(int),
                Type.EmptyTypes);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
            return (type.CreateType()!, getter, setter);
        }

        static (
            Type Type,
            MethodBuilder Adder,
            MethodBuilder Remover) DefineEventInterface(
                ModuleBuilder module)
        {
            TypeBuilder type = module.DefineType(
                "INameEvent",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            const MethodAttributes attributes =
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig;
            MethodBuilder adder = type.DefineMethod(
                "add_Changed",
                attributes,
                typeof(void),
                [typeof(Action)]);
            MethodBuilder remover = type.DefineMethod(
                "remove_Changed",
                attributes,
                typeof(void),
                [typeof(Action)]);
            EventBuilder @event = type.DefineEvent(
                "Changed",
                EventAttributes.None,
                typeof(Action));
            @event.SetAddOnMethod(adder);
            @event.SetRemoveOnMethod(remover);
            return (type.CreateType()!, adder, remover);
        }

        static (MethodBuilder Getter, MethodBuilder Setter) DefineProperty(
            TypeBuilder type,
            string propertyName,
            string getterName,
            string setterName,
            MethodAttributes attributes)
        {
            MethodBuilder getter = type.DefineMethod(
                getterName,
                attributes,
                typeof(int),
                Type.EmptyTypes);
            ILGenerator getterBody = getter.GetILGenerator();
            getterBody.Emit(OpCodes.Ldc_I4_1);
            getterBody.Emit(OpCodes.Ret);
            MethodBuilder setter = type.DefineMethod(
                setterName,
                attributes,
                typeof(void),
                [typeof(int)]);
            setter.GetILGenerator().Emit(OpCodes.Ret);
            PropertyBuilder property = type.DefineProperty(
                propertyName,
                PropertyAttributes.None,
                CallingConventions.HasThis,
                typeof(int),
                Type.EmptyTypes);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
            return (getter, setter);
        }

        static (MethodBuilder Adder, MethodBuilder Remover) DefineEvent(
            TypeBuilder type,
            string eventName,
            string adderName,
            string removerName,
            MethodAttributes attributes)
        {
            MethodBuilder adder = type.DefineMethod(
                adderName,
                attributes,
                typeof(void),
                [typeof(Action)]);
            adder.GetILGenerator().Emit(OpCodes.Ret);
            MethodBuilder remover = type.DefineMethod(
                removerName,
                attributes,
                typeof(void),
                [typeof(Action)]);
            remover.GetILGenerator().Emit(OpCodes.Ret);
            EventBuilder @event = type.DefineEvent(
                eventName,
                EventAttributes.None,
                typeof(Action));
            @event.SetAddOnMethod(adder);
            @event.SetRemoveOnMethod(remover);
            return (adder, remover);
        }
    }

    static string CreatePrivateScopeMemberFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "PrivateScopeMembers.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("PrivateScopeMembers"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "PrivateScopeMembers");
        TypeBuilder fixtureType = module.DefineType(
            "PrivateScopeMemberFixture",
            TypeAttributes.Public | TypeAttributes.Class);

        DefineConstantMethod(fixtureType, "Good", typeof(int));
        MethodBuilder hidden = fixtureType.DefineMethod(
            "Hidden",
            MethodAttributes.PrivateScope | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator hiddenBody = hidden.GetILGenerator();
        hiddenBody.Emit(OpCodes.Ldc_I4_1);
        hiddenBody.Emit(OpCodes.Ret);

        ConstructorBuilder constructor = fixtureType.DefineConstructor(
            MethodAttributes.PrivateScope,
            CallingConventions.Standard,
            Type.EmptyTypes);
        ILGenerator constructorBody = constructor.GetILGenerator();
        constructorBody.Emit(OpCodes.Ldarg_0);
        constructorBody.Emit(
            OpCodes.Call,
            typeof(object).GetConstructor(Type.EmptyTypes)!);
        constructorBody.Emit(OpCodes.Ret);

        fixtureType.CreateType();
        assembly.Save(path);
        return path;
    }

    static string CreateExplicitFinalizerFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ExplicitFinalizers.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                "ExplicitFinalizers.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ExplicitFinalizers"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle coreLib =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x7c, 0xec, 0x85, 0xd7,
                        0xbe, 0xa7, 0x79, 0x8e,
                    }),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectRef = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        BlobHandle instanceVoidSignature = metadata.GetOrAddBlob(
            new byte[] { 0x20, 0x00, 0x01 });
        BlobHandle instanceIntSignature = metadata.GetOrAddBlob(
            new byte[] { 0x20, 0x00, 0x08 });
        MemberReferenceHandle objectFinalize =
            metadata.AddMemberReference(
                objectRef,
                metadata.GetOrAddString("Finalize"),
                instanceVoidSignature);
        MemberReferenceHandle mismatchedObjectFinalize =
            metadata.AddMemberReference(
                objectRef,
                metadata.GetOrAddString("Finalize"),
                instanceIntSignature);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle validType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("ValidFinalizerFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle malformedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("MalformedFinalizerFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("ImplicitFinalizerFixture"),
            objectRef,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("FinalizerNeighborFixture"),
            objectRef,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(4));
        TypeDefinitionHandle unauthenticatedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("UnauthenticatedFinalizerFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(5));

        var methodBodies = new BlobBuilder();
        var methodBodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int AddVoidBody()
        {
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(
                instructions,
                new ControlFlowBuilder());
            encoder.OpCode(ILOpCode.Ret);
            return methodBodyEncoder.AddMethodBody(
                encoder,
                maxStack: 0);
        }

        int AddIntBody()
        {
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(
                instructions,
                new ControlFlowBuilder());
            encoder.LoadConstantI4(1);
            encoder.OpCode(ILOpCode.Ret);
            return methodBodyEncoder.AddMethodBody(
                encoder,
                maxStack: 1);
        }

        const MethodAttributes finalizerAttributes =
            MethodAttributes.Family
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig;
        MethodDefinitionHandle validFinalizer =
            metadata.AddMethodDefinition(
                finalizerAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Finalize"),
                instanceVoidSignature,
                AddVoidBody(),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle malformedFinalizer =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Finalize"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x01 }),
                AddVoidBody(),
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            finalizerAttributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Finalize"),
            instanceVoidSignature,
            AddVoidBody(),
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Good"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x08 }),
            AddIntBody(),
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            finalizerAttributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Finalize"),
            instanceVoidSignature,
            AddVoidBody(),
            MetadataTokens.ParameterHandle(1));
        MemberReferenceHandle unauthenticatedBody =
            metadata.AddMemberReference(
                unauthenticatedType,
                metadata.GetOrAddString("Finalize"),
                instanceVoidSignature);
        metadata.AddMethodImplementation(
            validType,
            validFinalizer,
            objectFinalize);
        metadata.AddMethodImplementation(
            malformedType,
            malformedFinalizer,
            objectFinalize);
        metadata.AddMethodImplementation(
            unauthenticatedType,
            unauthenticatedBody,
            mismatchedObjectFinalize);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateUnrepresentableMethodDeclarationFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "UnrepresentableMethodDeclarations.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                "UnrepresentableMethodDeclarations.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("UnrepresentableMethodDeclarations"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("MalformedConstructorFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("StaticConstructorFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(7));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("StaticAbstractMethodFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(8));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("MethodDeclarationNeighborFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(9));

        var methodBodies = new BlobBuilder();
        var methodBodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int AddBody()
        {
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(instructions);
            encoder.OpCode(ILOpCode.Ret);
            return methodBodyEncoder.AddMethodBody(
                encoder,
                maxStack: 0);
        }

        metadata.AddMethodDefinition(
            MethodAttributes.Private
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".cctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x01, 0x01, 0x08]),
            AddBody(),
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".cctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.CheckAccessOnOverride
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x01]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x00, 0x08]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Good"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x00, 0x08]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateMethodHeaderFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "MethodHeaders.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("MethodHeaders.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MethodHeaders"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("MethodHeaderFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(instructions);
        encoder.LoadConstantI4(1);
        encoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 1);

        MethodDefinitionHandle AddMethod(
            string name,
            MethodAttributes attributes,
            byte[] signature) =>
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));

        const MethodAttributes publicStatic =
            MethodAttributes.Public | MethodAttributes.Static;
        AddMethod("Good", publicStatic, [0x00, 0x00, 0x08]);
        AddMethod("Vararg", publicStatic, [0x05, 0x00, 0x08]);
        AddMethod("InstanceHeaderOnStatic", publicStatic, [0x20, 0x00, 0x08]);
        AddMethod(
            "StaticHeaderOnInstance",
            MethodAttributes.Public,
            [0x00, 0x00, 0x08]);
        AddMethod("ExplicitThis", publicStatic, [0x60, 0x00, 0x08]);
        AddMethod("ReservedHeader", publicStatic, [0x80, 0x00, 0x08]);
        AddMethod(
            "GenericHeaderWithoutRow",
            publicStatic,
            [0x10, 0x01, 0x00, 0x08]);
        MethodDefinitionHandle genericRowWithoutHeader = AddMethod(
            "GenericRowWithoutHeader",
            publicStatic,
            [0x00, 0x00, 0x08]);
        metadata.AddGenericParameter(
            genericRowWithoutHeader,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateForeignOperatorOperandFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ForeignOperatorOperand.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ForeignOperatorOperand.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ForeignOperatorOperand"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle foreignAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Foreign"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle foreignWidget = metadata.AddTypeReference(
            foreignAssembly,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Widget"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle widget = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Widget"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(instructions);
        encoder.LoadConstantI4(1);
        encoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 1);

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Good"),
            metadata.GetOrAddBlob(
                (byte[])[0x00, 0x00, 0x08]),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        var operatorSignature = new BlobBuilder();
        operatorSignature.WriteByte(0x00);
        operatorSignature.WriteCompressedInteger(1);
        operatorSignature.WriteByte(0x08);
        operatorSignature.WriteByte(0x12);
        operatorSignature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(foreignWidget) << 2) | 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("op_UnaryPlus"),
            metadata.GetOrAddBlob(operatorSignature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        var contradictoryOperatorSignature = new BlobBuilder();
        contradictoryOperatorSignature.WriteByte(0x00);
        contradictoryOperatorSignature.WriteCompressedInteger(1);
        contradictoryOperatorSignature.WriteByte(0x08);
        contradictoryOperatorSignature.WriteByte(0x12);
        contradictoryOperatorSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(widget) << 2);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.Virtual
                | MethodAttributes.Abstract
                | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("op_UnaryNegation"),
            metadata.GetOrAddBlob(contradictoryOperatorSignature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateMethodSemanticsFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "MethodSemantics.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("MethodSemantics"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("MethodSemantics");
        TypeBuilder fixtureType = module.DefineType(
            "MethodSemanticsFixture",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);

        DefineConstantMethod(fixtureType, "get_Ordinary", typeof(int));
        MethodBuilder classified = fixtureType.DefineMethod(
            "get_Classified",
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator classifiedBody = classified.GetILGenerator();
        classifiedBody.Emit(OpCodes.Ldc_I4_1);
        classifiedBody.Emit(OpCodes.Ret);
        PropertyBuilder property = fixtureType.DefineProperty(
            "Classified",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        property.AddOtherMethod(classified);

        fixtureType.CreateType();
        assembly.Save(path);
        return path;
    }

    static string CreateExplicitInterfaceAccessibilityFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "ExplicitInterfaceAccessibility.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                "ExplicitInterfaceAccessibility.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ExplicitInterfaceAccessibility"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle coreLib =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x7c, 0xec, 0x85, 0xd7,
                        0xbe, 0xa7, 0x79, 0x8e,
                    }),
                flags: default,
                hashValue: default);
        TypeReferenceHandle objectRef = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        BlobHandle instanceVoidSignature = metadata.GetOrAddBlob(
            new byte[] { 0x20, 0x00, 0x01 });

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle contractType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IContract"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle validType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("ValidExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(2));
        TypeDefinitionHandle malformedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("MalformedExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddInterfaceImplementation(validType, contractType);
        metadata.AddInterfaceImplementation(malformedType, contractType);

        MethodDefinitionHandle declaration =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Run"),
                instanceVoidSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 0);
        const MethodAttributes explicitImplementationAttributes =
            MethodAttributes.Final
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig;
        MethodDefinitionHandle validBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle malformedBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            validType,
            validBody,
            declaration);
        metadata.AddMethodImplementation(
            malformedType,
            malformedBody,
            declaration);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static string CreateUnavailableCanonicalSignatureFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "UnavailableCanonicalSignature.dll");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("UnavailableCanonicalSignature.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("UnavailableCanonicalSignature"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("UnavailableCanonicalSignatureFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x14);
        signature.WriteByte(0x08);
        signature.WriteCompressedInteger(2);
        signature.WriteCompressedInteger(1);
        signature.WriteCompressedInteger(3);
        signature.WriteCompressedInteger(0);

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var instructionEncoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        instructionEncoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(instructionEncoder, maxStack: 0);
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            parameter);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    static void DefineConstantMethod(
        TypeBuilder type,
        string name,
        Type returnType)
    {
        MethodBuilder method = type.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            Type.EmptyTypes);
        ILGenerator body = method.GetILGenerator();
        body.Emit(OpCodes.Ldc_I4_1);
        body.Emit(OpCodes.Ret);
    }

    static FidelityCheck.CompileBackResult NativeResult(
        FidelityCheck.CompileBackTarget target,
        FidelityCheck.CompileBackStatus status = FidelityCheck.CompileBackStatus.Exact,
        string? detail = null)
        => new(
            target.Type,
            target.Method,
            target.Overload,
            target.Signature,
            status,
            "ldarg.0 ret",
            "ldarg.0 ret",
            detail,
            FidelityCheck.CaptureMode.ProductArtifact,
            "product-artifact RTS; compile-back-floor=false");

    static string TargetIdentity(FidelityCheck.CompileBackTarget target)
        => $"{target.AssemblyPath}!{target.Type}::{target.Method}#{target.Overload}{target.Signature}";

    static HarnessRun RunHarness(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AuthoredCorpusRatchetTests.FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add(HarnessBinary());
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Remove("CB_TYPE");
        startInfo.Environment.Remove("CB_CLUSTER");
        startInfo.Environment.Remove("CB_DUMP");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start DecompilerHarness.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new HarnessRun(
            process.ExitCode,
            output + error);
    }

    static string DotnetHost()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string path = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(path))
                return path;
        }

        return "dotnet";
    }

    static string HarnessBinary()
    {
        string path = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools",
            "DecompilerHarness",
            "bin",
            "Release",
            "net11.0",
            "decompiler-harness.dll");
        Assert.True(File.Exists(path), $"The harness binary is missing: {path}.");
        return path;
    }

    readonly record struct HarnessRun(int ExitCode, string Output);

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("fidelity-generated-filter-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
