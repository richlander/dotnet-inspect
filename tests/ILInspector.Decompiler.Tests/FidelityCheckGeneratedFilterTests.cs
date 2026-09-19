using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public void SelectReturnToSenderTargets_RejectsUnrepresentableReadOnlyMethodsBeforeSampling()
    {
        string assemblyPath = CreateMethodReadOnlyMarkerFixture();
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            var target = Assert.Single(
                selected,
                target => target.Method != ".ctor");
            Assert.Equal("ReadOnlyMethodStructFixture", target.Type);
            Assert.Equal("Good", target.Method);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectReturnToSenderTargets_IncludesSealedOverrides()
    {
        string assemblyPath = CompileFixture("""
            public class Base
            {
                public virtual int Run() => 1;
            }

            public class Derived : Base
            {
                public sealed override int Run() => 2;
            }
            """);
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(
                selected,
                target => target.Type == "Derived"
                    && target.Method == "Run");
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
    public void SelectReturnToSenderTargets_IncludesCompilerRepresentedReadOnlyByRefModifiers()
    {
        string assemblyPath = CompileFixture("""
            public class ReadOnlyByRefFixture
            {
                public static ref readonly int Read(in int value)
                    => ref value;

                public virtual int VirtualIn(in int value)
                    => value;

                public virtual int VirtualReadOnly(ref readonly int value)
                    => value;
            }
            """);
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(selected, target => target.Method == "Read");
            Assert.Contains(selected, target => target.Method == "VirtualIn");
            Assert.Contains(selected, target => target.Method == "VirtualReadOnly");
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
    public void SelectReturnToSenderTargets_IncludesNullableDeclaringStructOperators()
    {
        string assemblyPath = CompileFixture("""
            public readonly struct NullableOperator
            {
                public static int operator +(NullableOperator? value, int other)
                    => other;
            }

            public readonly struct GenericNullableOperator<T>
                where T : struct
            {
                public static int operator +(
                    GenericNullableOperator<T>? value,
                    int other)
                    => other;
            }
            """);
        try
        {
            var selected = FidelityCheck.SelectReturnToSenderTargets(
                [assemblyPath],
                cap: int.MaxValue);

            Assert.Contains(
                selected,
                target => target.Type == "NullableOperator"
                    && target.Method == "op_Addition");
            Assert.Contains(
                selected,
                target => target.Type == "GenericNullableOperator`1"
                    && target.Method == "op_Addition");
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
    public void SelectReturnToSenderTargets_RejectsUnrepresentableExplicitInterfaceImplementationsBeforeSampling()
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
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MissingFinalExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "BaseMethodImplFixture"
                    && target.Method == "BaseContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "WrongQualifierExplicitInterfaceFixture"
                    && target.Method == "IOther.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MissingHideBySigExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "SpecialNameExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type
                        == "RuntimeSpecialNameExplicitInterfaceFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MultipleMethodImplFixture"
                    && target.Method == "IContract.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type.StartsWith(
                        "GenericContextMismatchFixture",
                        StringComparison.Ordinal)
                    && target.Method == "IGenericContract<string>.Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type == "MemberRefMethodImplBodyFixture"
                    && target.Method == "Run");
            Assert.DoesNotContain(
                selected,
                target =>
                    target.Type.StartsWith(
                        "GenericMemberRefMethodImplBodyFixture",
                        StringComparison.Ordinal)
                    && target.Method == "Run");
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
        var fixture = typeof(CSharpText.Tests.PdbScopeFixtures);
        string assemblyPath = fixture.Assembly.Location;
        var current = Assert.Single(
            FidelityCheck.Evaluate(
                assemblyPath,
                type => type == fixture.FullName,
                method => method.Method
                    == nameof(CSharpText.Tests.PdbScopeFixtures.SequentialStackCarry)));
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
        ConstructorInfo fakeReadOnlyConstructor =
            DefineFakeReadOnlyAttribute(module);
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

        ConstructorInfo readOnlyConstructor =
            typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)
                .GetConstructor(Type.EmptyTypes)!;
        ConstructorInfo requiresLocationConstructor =
            typeof(object).Assembly
                .GetType(
                    "System.Runtime.CompilerServices.RequiresLocationAttribute",
                    throwOnError: true)!
                .GetConstructor(Type.EmptyTypes)!;
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

        MethodBuilder readonlyMarkerOnValueReturn =
            fixtureType.DefineMethod(
                "ReadonlyMarkerOnValueReturn",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                Type.EmptyTypes);
        readonlyMarkerOnValueReturn.DefineParameter(
                0,
                ParameterAttributes.None,
                null)
            .SetCustomAttribute(
                new CustomAttributeBuilder(readOnlyConstructor, []));
        ILGenerator readonlyMarkerOnValueReturnBody =
            readonlyMarkerOnValueReturn.GetILGenerator();
        readonlyMarkerOnValueReturnBody.Emit(OpCodes.Ldc_I4_1);
        readonlyMarkerOnValueReturnBody.Emit(OpCodes.Ret);

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

        MethodBuilder additionalReadonlyModifier =
            fixtureType.DefineMethod(
                "AdditionalReadonlyModifier",
                MethodAttributes.Public | MethodAttributes.Static);
        additionalReadonlyModifier.SetSignature(
            typeof(int),
            null,
            null,
            [typeof(int).MakeByRefType()],
            [[
                typeof(System.Runtime.InteropServices.InAttribute),
                modifierType
            ]],
            null);
        additionalReadonlyModifier.DefineParameter(
                1,
                ParameterAttributes.In,
                "value")
            .SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)
                        .GetConstructor(Type.EmptyTypes)!,
                    []));
        ILGenerator additionalReadonlyModifierBody =
            additionalReadonlyModifier.GetILGenerator();
        additionalReadonlyModifierBody.Emit(OpCodes.Ldc_I4_1);
        additionalReadonlyModifierBody.Emit(OpCodes.Ret);

        MethodBuilder duplicateReadonlyMarker =
            fixtureType.DefineMethod(
                "DuplicateReadonlyMarker",
                MethodAttributes.Public | MethodAttributes.Static);
        duplicateReadonlyMarker.SetSignature(
            typeof(int),
            null,
            null,
            [typeof(int).MakeByRefType()],
            [[typeof(System.Runtime.InteropServices.InAttribute)]],
            null);
        ParameterBuilder duplicateParameter =
            duplicateReadonlyMarker.DefineParameter(
                1,
                ParameterAttributes.In,
                "value");
        duplicateParameter.SetCustomAttribute(
            new CustomAttributeBuilder(readOnlyConstructor, []));
        duplicateParameter.SetCustomAttribute(
            new CustomAttributeBuilder(readOnlyConstructor, []));
        ILGenerator duplicateReadonlyMarkerBody =
            duplicateReadonlyMarker.GetILGenerator();
        duplicateReadonlyMarkerBody.Emit(OpCodes.Ldc_I4_1);
        duplicateReadonlyMarkerBody.Emit(OpCodes.Ret);

        MethodBuilder malformedReadonlyMarker =
            fixtureType.DefineMethod(
                "MalformedReadonlyMarker",
                MethodAttributes.Public | MethodAttributes.Static);
        malformedReadonlyMarker.SetSignature(
            typeof(int),
            null,
            null,
            [typeof(int).MakeByRefType()],
            [[typeof(System.Runtime.InteropServices.InAttribute)]],
            null);
        malformedReadonlyMarker.DefineParameter(
                1,
                ParameterAttributes.In,
                "value")
            .SetCustomAttribute(
                readOnlyConstructor,
                [0x01, 0x00]);
        ILGenerator malformedReadonlyMarkerBody =
            malformedReadonlyMarker.GetILGenerator();
        malformedReadonlyMarkerBody.Emit(OpCodes.Ldc_I4_1);
        malformedReadonlyMarkerBody.Emit(OpCodes.Ret);

        DefineMalformedReadonlyReturn(
            fixtureType,
            "DuplicateReadonlyReturnMarker",
            parameter =>
            {
                parameter.SetCustomAttribute(
                    new CustomAttributeBuilder(readOnlyConstructor, []));
                parameter.SetCustomAttribute(
                    new CustomAttributeBuilder(readOnlyConstructor, []));
            });
        DefineMalformedReadonlyReturn(
            fixtureType,
            "MalformedReadonlyReturnMarker",
            parameter => parameter.SetCustomAttribute(
                readOnlyConstructor,
                [0x01, 0x00]));
        DefineMalformedReadonlyReturn(
            fixtureType,
            "ForgedReadonlyReturnMarker",
            parameter => parameter.SetCustomAttribute(
                new CustomAttributeBuilder(fakeReadOnlyConstructor, [])));
        DefineMalformedReadonlyReturn(
            fixtureType,
            "RequiresLocationReadonlyReturnMarker",
            parameter => parameter.SetCustomAttribute(
                new CustomAttributeBuilder(requiresLocationConstructor, [])));

        modifierType.CreateType();
        fixtureType.CreateType();
        assembly.Save(path);
        return path;

        static void DefineMalformedReadonlyReturn(
            TypeBuilder type,
            string name,
            Action<ParameterBuilder> setMarker)
        {
            MethodBuilder method = type.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static);
            method.SetSignature(
                typeof(int).MakeByRefType(),
                [typeof(System.Runtime.InteropServices.InAttribute)],
                null,
                Type.EmptyTypes,
                null,
                null);
            setMarker(method.DefineParameter(
                0,
                ParameterAttributes.None,
                null));
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldnull);
            body.Emit(OpCodes.Throw);
        }
    }

    static string CreateMethodReadOnlyMarkerFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "MethodReadOnlyMarkers.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("MethodReadOnlyMarkers"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "MethodReadOnlyMarkers");
        ConstructorInfo fakeReadOnlyConstructor =
            DefineFakeReadOnlyAttribute(module);
        TypeBuilder classType = module.DefineType(
            "ReadOnlyMethodClassFixture",
            TypeAttributes.Public | TypeAttributes.Class);
        TypeBuilder structType = module.DefineType(
            "ReadOnlyMethodStructFixture",
            TypeAttributes.Public
                | TypeAttributes.Sealed
                | TypeAttributes.SequentialLayout,
            typeof(ValueType));
        ConstructorInfo readOnlyConstructor =
            typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)
                .GetConstructor(Type.EmptyTypes)!;
        var readOnly = new CustomAttributeBuilder(
            readOnlyConstructor,
            []);

        DefineMarkedMethod(
            classType,
            "ClassInstance",
            MethodAttributes.Public,
            readOnly);
        DefineMarkedMethod(
            classType,
            "ClassStatic",
            MethodAttributes.Public | MethodAttributes.Static,
            readOnly);
        DefineMarkedMethod(
            structType,
            "Good",
            MethodAttributes.Public,
            readOnly);
        DefineMarkedMethod(
            structType,
            "Duplicate",
            MethodAttributes.Public,
            new CustomAttributeBuilder(readOnlyConstructor, []),
            new CustomAttributeBuilder(readOnlyConstructor, []));
        DefineRawMarkedMethod(
            structType,
            "Malformed",
            readOnlyConstructor,
            [0x01, 0x00]);
        DefineMarkedMethod(
            structType,
            "Forged",
            MethodAttributes.Public,
            new CustomAttributeBuilder(fakeReadOnlyConstructor, []));

        classType.CreateType();
        structType.CreateType();
        assembly.Save(path);
        return path;

        static void DefineMarkedMethod(
            TypeBuilder type,
            string name,
            MethodAttributes attributes,
            params CustomAttributeBuilder[] markers)
        {
            MethodBuilder method = type.DefineMethod(
                name,
                attributes,
                typeof(int),
                Type.EmptyTypes);
            foreach (CustomAttributeBuilder marker in markers)
                method.SetCustomAttribute(marker);
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Ret);
        }

        static void DefineRawMarkedMethod(
            TypeBuilder type,
            string name,
            ConstructorInfo markerConstructor,
            byte[] markerBlob)
        {
            MethodBuilder method = type.DefineMethod(
                name,
                MethodAttributes.Public,
                typeof(int),
                Type.EmptyTypes);
            method.SetCustomAttribute(markerConstructor, markerBlob);
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Ret);
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
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("FinalMethodFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(9));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("MethodDeclarationNeighborFixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(11));

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
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.Final
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("FinalNewSlot"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x08]),
            AddBody(),
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Final
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("FinalNonVirtual"),
            metadata.GetOrAddBlob(
                (byte[])[0x20, 0x00, 0x08]),
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
        TypeDefinitionHandle otherContractType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IOther"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(2));
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
        TypeDefinitionHandle missingFinalType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "MissingFinalExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(4));
        TypeDefinitionHandle wrongQualifierType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "WrongQualifierExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(5));
        TypeDefinitionHandle missingHideBySigType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "MissingHideBySigExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(6));
        TypeDefinitionHandle multipleMethodImplType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("MultipleMethodImplFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(7));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("BaseContract"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(8));
        TypeDefinitionHandle baseMethodImplType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("BaseMethodImplFixture"),
                baseType,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(9));
        TypeDefinitionHandle genericContractType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IGenericContract`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(10));
        TypeDefinitionHandle genericMismatchType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "GenericContextMismatchFixture`1"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(11));
        TypeDefinitionHandle memberRefBodyType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "MemberRefMethodImplBodyFixture"),
                baseType,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(12));
        TypeDefinitionHandle genericMemberRefBodyType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "GenericMemberRefMethodImplBodyFixture`1"),
                baseType,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(13));
        TypeDefinitionHandle specialNameType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "SpecialNameExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(15));
        TypeDefinitionHandle runtimeSpecialNameType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(
                    "RuntimeSpecialNameExplicitInterfaceFixture"),
                objectRef,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(16));
        metadata.AddGenericParameter(
            genericContractType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TContract"),
            index: 0);
        metadata.AddGenericParameter(
            genericMismatchType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            genericMemberRefBodyType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        var genericContractSpecSignature = new BlobBuilder();
        new BlobEncoder(genericContractSpecSignature)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                genericContractType,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .String();
        TypeSpecificationHandle genericContractString =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    genericContractSpecSignature));
        var genericMemberRefBodySpecSignature = new BlobBuilder();
        new BlobEncoder(genericMemberRefBodySpecSignature)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                genericMemberRefBodyType,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .GenericTypeParameter(0);
        TypeSpecificationHandle genericMemberRefBody =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    genericMemberRefBodySpecSignature));
        metadata.AddInterfaceImplementation(validType, contractType);
        metadata.AddInterfaceImplementation(malformedType, contractType);
        metadata.AddInterfaceImplementation(
            missingFinalType,
            contractType);
        metadata.AddInterfaceImplementation(
            wrongQualifierType,
            contractType);
        metadata.AddInterfaceImplementation(
            wrongQualifierType,
            otherContractType);
        metadata.AddInterfaceImplementation(
            missingHideBySigType,
            contractType);
        metadata.AddInterfaceImplementation(
            multipleMethodImplType,
            contractType);
        metadata.AddInterfaceImplementation(
            genericMismatchType,
            genericContractString);
        metadata.AddInterfaceImplementation(
            specialNameType,
            contractType);
        metadata.AddInterfaceImplementation(
            runtimeSpecialNameType,
            contractType);

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
        metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle specialNameBody =
                metadata.AddMethodDefinition(
                    MethodAttributes.Private
                        | explicitImplementationAttributes
                        | MethodAttributes.SpecialName,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("IContract.Run"),
                    instanceVoidSignature,
                    bodyOffset,
                    MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle runtimeSpecialNameBody =
                metadata.AddMethodDefinition(
                    MethodAttributes.Private
                        | explicitImplementationAttributes
                        | MethodAttributes.RTSpecialName,
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
        MethodDefinitionHandle missingFinalBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle wrongQualifierBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IOther.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle missingHideBySigBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle multipleMethodImplBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("IContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle baseDeclaration =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle baseMethodImplBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("BaseContract.Run"),
                instanceVoidSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        BlobHandle genericInstanceSignature = metadata.GetOrAddBlob(
            new byte[] { 0x20, 0x01, 0x01, 0x13, 0x00 });
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            genericInstanceSignature,
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle genericMismatchBody =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | explicitImplementationAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "IGenericContract<string>.Run"),
                genericInstanceSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            instanceVoidSignature,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            instanceVoidSignature,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        MemberReferenceHandle genericDeclaration =
            metadata.AddMemberReference(
                genericContractString,
                metadata.GetOrAddString("Run"),
                genericInstanceSignature);
        MemberReferenceHandle localBodyReference =
            metadata.AddMemberReference(
                memberRefBodyType,
                metadata.GetOrAddString("Run"),
                instanceVoidSignature);
        MemberReferenceHandle genericLocalBodyReference =
            metadata.AddMemberReference(
                genericMemberRefBody,
                metadata.GetOrAddString("Run"),
                instanceVoidSignature);
        metadata.AddMethodImplementation(
            validType,
            validBody,
            declaration);
        metadata.AddMethodImplementation(
            malformedType,
            malformedBody,
            declaration);
        metadata.AddMethodImplementation(
            missingFinalType,
            missingFinalBody,
            declaration);
        metadata.AddMethodImplementation(
            wrongQualifierType,
            wrongQualifierBody,
            declaration);
        metadata.AddMethodImplementation(
            missingHideBySigType,
            missingHideBySigBody,
            declaration);
        metadata.AddMethodImplementation(
            multipleMethodImplType,
            multipleMethodImplBody,
            declaration);
        metadata.AddMethodImplementation(
            multipleMethodImplType,
            multipleMethodImplBody,
            declaration);
        metadata.AddMethodImplementation(
            baseMethodImplType,
            baseMethodImplBody,
            baseDeclaration);
        metadata.AddMethodImplementation(
            genericMismatchType,
            genericMismatchBody,
            genericDeclaration);
        metadata.AddMethodImplementation(
            memberRefBodyType,
            localBodyReference,
            baseDeclaration);
        metadata.AddMethodImplementation(
            genericMemberRefBodyType,
            genericLocalBodyReference,
            baseDeclaration);
        metadata.AddMethodImplementation(
            specialNameType,
            specialNameBody,
            declaration);
        metadata.AddMethodImplementation(
            runtimeSpecialNameType,
            runtimeSpecialNameBody,
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

    static ConstructorInfo DefineFakeReadOnlyAttribute(ModuleBuilder module)
    {
        TypeBuilder type = module.DefineType(
            "System.Runtime.CompilerServices.IsReadOnlyAttribute",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Sealed,
            typeof(Attribute));
        ConstructorBuilder constructor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        ILGenerator body = constructor.GetILGenerator();
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(
            OpCodes.Call,
            typeof(Attribute).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)!);
        body.Emit(OpCodes.Ret);
        return type.CreateType()!.GetConstructor(Type.EmptyTypes)!;
    }

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("fidelity-generated-filter-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
