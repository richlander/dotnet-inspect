using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

public partial class ReturnToSenderPrototypeTests
{
    [Fact]
    public async Task CompileBackTargets_SynthesizesParameterlessConstructorForNestedDerivedType()
    {
        // Issue #2527 guard (Gemini review of #2732): nested types are emitted from
        // their enclosing requirement and are absent from the top-level requirement
        // map, so the synthetic-parameterless-base-constructor scan must also walk
        // nested types. Here `Outer.Nested : Base` is emitted even though it is never
        // consumed; `Base` (reconstructed with only a parameterized constructor) must
        // still receive a synthetic parameterless constructor so Nested's implicit
        // `: base()` binds natively, without relying on the compile-back floor.
        var assemblyPath = CompileFixture("""
            using System;

            public class Base
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }

            public class Outer
            {
                public Outer()
                {
                }

                public class Nested : Base
                {
                    public Nested() : base(1)
                    {
                    }
                }
            }

            public static class Use
            {
                public static void Run()
                {
                    Console.WriteLine(new Base(1));
                    Console.WriteLine(new Outer());
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Use", "Run", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_DoesNotReconstructGenericBaseClass()
    {
        // Issue #2527 guard (Gemini review of #2732): a closed generic base
        // instantiation (`Derived : Base<int>`) is a TypeSpecification, which the
        // flat shell cannot carry and cannot own a synthetic constructor for. It must
        // be dropped rather than emitted, so the derived stub does not fail on an
        // implicit `: base()` with no parameterless target.
        var assemblyPath = CompileFixture("""
            using System;

            public class Base<T>
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }

            public class Derived : Base<int>
            {
                public Derived() : base(1)
                {
                }
            }

            public static class Use
            {
                public static void Run()
                {
                    Console.WriteLine(new Base<int>(1));
                    Console.WriteLine(new Derived());
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Use", "Run", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.DoesNotContain(": Base", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_RoundTripsMinimalClassProperty()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Equal("Class1", result.Plan.TargetMethod.Type);
            Assert.Equal("get_Method1", result.Plan.TargetMethod.Method);
            Assert.Contains("public class Class1", result.Source);
            Assert.Contains("public string Method1", result.Source);
            Assert.Contains("return \"Hello World\";", result.Source);
            Assert.NotNull(result.FidelityDiff);
            Assert.True(result.FidelityDiff.IsExact);

            var compileBack = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                row => row.Type == "Class1" && row.Method == "get_Method1");
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, compileBack.Status);
            Assert.NotNull(compileBack.FidelityDiff);
            Assert.True(compileBack.FidelityDiff.IsExact);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_RoundTripsInheritedExplicitInterfaceProperty()
    {
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitPropertyFixture : IDerived
            {
                int IBase.Value => 42;

                void IBase.Touch()
                {
                }
            }

            public interface IDerived : IBase
            {
            }

            public interface IBase
            {
                int Value { get; }

                void Touch();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int IBase.Value", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IBase_Value", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsExplicitInterfaceMethod()
    {
        // #3112: a class method whose metadata name is an explicit-interface spelling
        // (`IBase.Touch`) must reconstruct as an explicit-interface implementation with the
        // interface declaring the member, not a plain `IBase_Touch` method (which recompiles
        // under the wrong name and fails the fidelity lookup as ContextFail/method-not-found).
        var assemblyPath = CompileFixture("""
            using System;
            public sealed class ExplicitMethodFixture : IBase
            {
                void IBase.Touch()
                {
                    Console.WriteLine("touched");
                }
            }

            public interface IBase
            {
                void Touch();
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitMethodFixture",
                    "IBase.Touch",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("void IBase.Touch()", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IBase_Touch", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsNamespacedExplicitInterfaceMethodWithParameters()
    {
        // The corpus family (#3112) is dominated by namespaced interfaces (System.IConvertible,
        // System.Collections.IEnumerable, ...) with real parameters and return values. Reconstruct
        // the qualified interface spelling and round-trip the body exactly.
        var assemblyPath = CompileFixture("""
            namespace Sample
            {
                public sealed class ExplicitComputeFixture : IComputer
                {
                    int IComputer.Compute(int left, int right)
                    {
                        return left + right;
                    }
                }

                public interface IComputer
                {
                    int Compute(int left, int right);
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Sample.ExplicitComputeFixture",
                    "Sample.IComputer.Compute",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int Sample.IComputer.Compute(int left, int right)", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IComputer_Compute", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsExternalSingleMemberExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Seq : System.Collections.IEnumerable
            {
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    throw null;
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Seq",
                    "System.Collections.IEnumerable.GetEnumerator",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.Source.Contains("class Seq : System.Collections.IEnumerable", StringComparison.Ordinal)
                || result.Source.Contains("class Seq : IEnumerable", StringComparison.Ordinal),
                result.Source);
            Assert.True(
                result.Source.Contains(
                    "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal)
                || result.Source.Contains(
                    "IEnumerator System.Collections.IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal)
                || result.Source.Contains(
                    "IEnumerator IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal),
                result.Source);
            Assert.DoesNotContain("System_Collections_IEnumerable_GetEnumerator", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsForwardedExternalExplicitInterfaceMethod()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var baseFacadePath = CompileFixture(
            "namespace RtsForwardBase { public interface IBase { } }",
            directory: fixtureDir,
            assemblyName: "RtsForwardBaseFacade");
        var facadePath = CompileFixture(
            """
            namespace RtsForward
            {
                public interface IProbe : RtsForwardBase.IBase
                {
                    void Target();
                }
            }
            """,
            directory: fixtureDir,
            assemblyName: "RtsForwardFacade",
            additionalReferences: [MetadataReference.CreateFromFile(baseFacadePath)]);
        var assemblyPath = CompileFixture(
            """
            public sealed class ForwardedImpl : RtsForward.IProbe
            {
                void RtsForward.IProbe.Target() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences:
            [
                MetadataReference.CreateFromFile(facadePath),
                MetadataReference.CreateFromFile(baseFacadePath),
            ]);
        var targetPath = CompileFixture(
            """
            namespace RtsForward
            {
                public interface IProbe : RtsForwardBase.IBase
                {
                    void Target();
                }
            }
            """,
            directory: fixtureDir,
            assemblyName: "RtsForwardTarget",
            additionalReferences: [MetadataReference.CreateFromFile(baseFacadePath)]);
        var baseTargetPath = CompileFixture(
            "namespace RtsForwardBase { public interface IBase { } }",
            directory: fixtureDir,
            assemblyName: "RtsForwardBaseTarget");
        CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(RtsForwardBase.IBase))]
            """,
            directory: fixtureDir,
            assemblyName: "RtsForwardBaseFacade",
            additionalReferences: [MetadataReference.CreateFromFile(baseTargetPath)]);
        CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(RtsForward.IProbe))]
            """,
            directory: fixtureDir,
            assemblyName: "RtsForwardFacade",
            additionalReferences: [MetadataReference.CreateFromFile(targetPath)]);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ForwardedImpl",
                    "RtsForward.IProbe.Target",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains(
                "RtsForward.IProbe.Target()",
                result.Source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RtsForward_IProbe_Target",
                result.Source,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_AcceptsByteIdenticalDirectSignedInterfaceSibling()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.Serialization.IJsonOnDeserialized)
            .Assembly.Location;
        string assemblyPath = CompileFixture(
            """
            public sealed class ExactCopyImpl :
                System.Text.Json.Serialization.IJsonOnDeserialized
            {
                void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture");
        File.Copy(
            platformPath,
            Path.Combine(fixtureDir, "System.Text.Json.dll"));
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExactCopyImpl",
                    "System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized",
                    0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(
                "IJsonOnDeserialized.OnDeserialized()",
                result.Source,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CreateCompilationClosure_RejectsDirectSignedInterfaceSpoof()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.Serialization.IJsonOnDeserialized)
            .Assembly.Location;
        string assemblyPath = CompileFixture(
            """
            public sealed class SpoofedImpl :
                System.Text.Json.Serialization.IJsonOnDeserialized
            {
                void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture");
        File.WriteAllBytes(
            Path.Combine(fixtureDir, "System.Text.Json.dll"),
            BuildConfusableInterfaceAssemblyImage(
                platformPath,
                "System.Text.Json.Serialization",
                "IJsonOnDeserialized",
                "OnDeserialized"));
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(assemblyPath));
            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferencePlatformAgreementMismatch),
                error.Message);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackPropertyGetters_SharesOneCompilationClosure()
    {
        string assemblyPath = CompileFixture(
            """
            public sealed class Fixture
            {
                public int First => 1;
                public int Second => 2;
            }
            """);
        try
        {
            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(assemblyPath);
            IReadOnlyList<ReturnToSender.Result> results =
                await ReturnToSender.CompileBackPropertyGetters(
                    assemblyPath,
                    maxTargets: 2,
                    closure);

            Assert.Equal(2, results.Count);
            Assert.All(
                results,
                result => Assert.Same(
                    closure,
                    result.FinalRequest!.CompilationClosure));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_ReleasesOwnedCompilationClosure()
    {
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public int Value => 1; }");
        try
        {
            ReturnToSender.Result result =
                await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.NotNull(result.FinalRequest);
            Assert.Null(result.FinalRequest.CompilationClosure);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompilationClosure_DisposeRevokesScopedUse()
    {
        string assemblyPath = CompileFixture("public sealed class Fixture { }");
        try
        {
            ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(assemblyPath);

            closure.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => closure.Use(static _ => true));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CreateCompilationClosure_RejectsDuplicateFullIdentityCandidates()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string dependencyPath = CompileFixture(
            "public sealed class Duplicate { }",
            directory: fixtureDir,
            assemblyName: "RtsDuplicateReference");
        File.Copy(
            dependencyPath,
            Path.Combine(fixtureDir, "RtsDuplicateReference.Copy.dll"));
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { }",
            directory: fixtureDir,
            assemblyName: "fixture");
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(assemblyPath));

            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferenceSelectionAmbiguous),
                error.Message);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CreateCompilationClosure_FreezesResolverAndRoslynToSameDependencyImage()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string dependencyPath = CompileFixture(
            "public interface IBefore { void M(); }",
            directory: fixtureDir,
            assemblyName: "RtsSnapshotDependency");
        var dependency = ResolvedAssemblyReference.CreateFromPath(
            dependencyPath,
            AssemblyResolutionProvenance.Local("test"));
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { }",
            directory: fixtureDir,
            assemblyName: "fixture");
        try
        {
            using var closure = ReturnToSender.CreateCompilationClosure(assemblyPath);

            CompileFixture(
                "public interface IAfter { void M(); }",
                directory: fixtureDir,
                assemblyName: "RtsSnapshotDependency");

            closure.Use(context =>
            {
                Assert.False(
                    context.CompilerReferences.Any(reference =>
                        AssemblyReferenceIdentity
                            .FromAssemblyDefinition(
                                Assert.Single(
                                    Assert.IsType<AssemblyMetadata>(
                                        reference.GetMetadata()).GetModules())
                                    .GetMetadataReader())
                        == context.Source.Identity));
                ResolvedAssemblyReference frozen = Assert.IsType<ResolvedAssemblyReference>(
                    context.Resolve(
                        dependency.Identity,
                        AssemblyResolutionScope.Any));
                using Stream frozenStream = frozen.OpenRead();
                using var frozenPe = new PEReader(frozenStream);
                Assert.True(
                    ContainsType(
                        frozenPe.GetMetadataReader(),
                        "IBefore"));
                Assert.False(
                    ContainsType(
                        frozenPe.GetMetadataReader(),
                        "IAfter"));

                PortableExecutableReference roslynReference =
                    Assert.Single(
                        context.CompilerReferences,
                        reference =>
                        {
                            var metadata =
                                Assert.IsType<AssemblyMetadata>(
                                    reference.GetMetadata());
                            var module = Assert.Single(metadata.GetModules());
                            var reader = module.GetMetadataReader();
                            return AssemblyReferenceIdentity
                                .FromAssemblyDefinition(reader)
                                == dependency.Identity;
                        });
                Assert.Equal(
                    Path.GetFullPath(dependencyPath),
                    roslynReference.FilePath);
                var roslynMetadata =
                    Assert.IsType<AssemblyMetadata>(
                        roslynReference.GetMetadata());
                var roslynReader =
                    Assert.Single(roslynMetadata.GetModules())
                        .GetMetadataReader();
                Assert.True(ContainsType(roslynReader, "IBefore"));
                Assert.False(ContainsType(roslynReader, "IAfter"));
                return true;
            });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CreateCompilationClosure_AcceptsUnreferencedByteIdenticalPlatformSibling()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public int Value => 1; }",
            directory: fixtureDir,
            assemblyName: "fixture");
        File.Copy(
            platformPath,
            Path.Combine(fixtureDir, "System.Text.Json.dll"));
        try
        {
            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(assemblyPath);
            AssemblyReferenceIdentity platformIdentity = Identity(platformPath);

            Assert.True(closure.Use(context =>
                context.CompilerReferences.Count(reference =>
                {
                    var metadata =
                        Assert.IsType<AssemblyMetadata>(reference.GetMetadata());
                    MetadataReader reader =
                        Assert.Single(metadata.GetModules()).GetMetadataReader();
                    return AssemblyReferenceIdentity
                        .FromAssemblyDefinition(reader)
                        .IsEquivalentTo(platformIdentity);
                }) == 1));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CreateCompilationClosure_ExcludesDistinctSourceModuleAcquisitions()
    {
        string originalPath = typeof(RoundTripCompilationEngine).Assembly.Location;
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);
        string copiedPath = Path.Combine(fixtureDir, Path.GetFileName(originalPath));
        File.Copy(originalPath, copiedPath);
        try
        {
            IReadOnlyList<ReturnToSender.Result> original =
                await ReturnToSender.CompileBackPropertyGetters(originalPath, maxTargets: 40);
            IReadOnlyList<ReturnToSender.Result> copied =
                await ReturnToSender.CompileBackPropertyGetters(copiedPath, maxTargets: 40);

            Assert.Equal(original.Count, copied.Count);
            Assert.Equal(
                original.Select(result => result.Status),
                copied.Select(result => result.Status));
            Assert.Equal(
                original.Sum(result => Math.Max(0, result.Plan.Types.Count - 1)),
                copied.Sum(result => Math.Max(0, result.Plan.Types.Count - 1)));
        }
        finally
        {
            DeleteFixture(copiedPath);
        }
    }

    [Fact]
    public void ResolveExternalTypeDefinition_AcceptsByteIdenticalPlatformSibling()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        string facadePath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(System.Text.Json.JsonSerializer))]
            public sealed class RtsPlatformFacadeMarker { }
            """,
            directory: fixtureDir,
            assemblyName: "RtsPlatformFacade");
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public RtsPlatformFacadeMarker Value => null; }",
            directory: fixtureDir,
            assemblyName: "fixture",
            [MetadataReference.CreateFromFile(facadePath)]);
        File.Copy(
            platformPath,
            Path.Combine(fixtureDir, "System.Text.Json.dll"));
        try
        {
            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(assemblyPath);
            AssemblyReferenceIdentity facadeIdentity =
                Identity(facadePath);
            Assert.NotNull(closure.Use(context =>
                CompileBackSourceComposer.ResolveExternalTypeDefinition(
                    Assert.IsType<ResolvedAssemblyReference>(
                        context.Resolve(facadeIdentity, AssemblyResolutionScope.Any)),
                    "System.Text.Json.JsonSerializer",
                    context)));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ResolveExternalTypeDefinition_RollsOlderPlatformFacadeIntoCompilationClosure()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        AssemblyReferenceIdentity platformIdentity = Identity(platformPath);
        Assert.NotNull(platformIdentity.Version);
        Assert.True(platformIdentity.Version.Major > 0);
        AssemblyReferenceIdentity priorPlatformIdentity = platformIdentity with
        {
            Version = new Version(platformIdentity.Version.Major - 1, 0, 0, 0),
        };
        string facadePath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(System.Text.Json.JsonSerializer))]
            public sealed class RtsPlatformFacadeMarker { }
            """,
            directory: fixtureDir,
            assemblyName: "RtsPlatformFacade");
        RewriteAssemblyReferenceVersion(
            facadePath,
            priorPlatformIdentity.Name,
            priorPlatformIdentity.Version);
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public RtsPlatformFacadeMarker Value => null; }",
            directory: fixtureDir,
            assemblyName: "fixture",
            [MetadataReference.CreateFromFile(facadePath)]);
        try
        {
            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(assemblyPath);

            var resolved = closure.Use(context =>
                CompileBackSourceComposer.ResolveExternalTypeDefinition(
                    Assert.IsType<ResolvedAssemblyReference>(
                        context.Resolve(Identity(facadePath), AssemblyResolutionScope.Any)),
                    "System.Text.Json.JsonSerializer",
                    context));

            Assert.NotNull(resolved);
            Assert.Equal(platformIdentity.Name, resolved.Value.Assembly.Identity.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ResolveExternalTypeDefinition_DeclinesWhenSiblingSpoofsDurableAddress()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        string facadePath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(System.Text.Json.JsonSerializer))]
            public sealed class RtsPlatformFacadeMarker { }
            """,
            directory: fixtureDir,
            assemblyName: "RtsPlatformFacade");
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public RtsPlatformFacadeMarker Value => null; }",
            directory: fixtureDir,
            assemblyName: "fixture",
            [MetadataReference.CreateFromFile(facadePath)]);
        File.WriteAllBytes(
            Path.Combine(fixtureDir, "System.Text.Json.dll"),
            BuildSpoofedDefinitionAddressAssemblyImage(
                platformPath,
                "System.Text.Json",
                "JsonSerializer"));
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(assemblyPath));
            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferencePlatformAgreementMismatch),
                error.Message);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ResolveExternalTypeDefinition_DeclinesWhenPlatformSelectionDiffersFromCompilationClosure()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        string facadePath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(System.Text.Json.JsonSerializer))]
            public sealed class RtsPlatformFacadeMarker { }
            """,
            directory: fixtureDir,
            assemblyName: "RtsPlatformFacade");
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public RtsPlatformFacadeMarker Value => null; }",
            directory: fixtureDir,
            assemblyName: "fixture",
            [MetadataReference.CreateFromFile(facadePath)]);
        File.WriteAllBytes(
            Path.Combine(fixtureDir, "System.Text.Json.dll"),
            BuildConfusableAssemblyImage(platformPath));
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(assemblyPath));
            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferencePlatformAgreementMismatch),
                error.Message);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CreateCompilationClosure_RejectsVersionSkewedPlatformSibling()
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        string facadePath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(System.Text.Json.JsonSerializer))]
            public sealed class RtsPlatformFacadeMarker { }
            """,
            directory: fixtureDir,
            assemblyName: "RtsPlatformFacade");
        string assemblyPath = CompileFixture(
            "public sealed class Fixture { public RtsPlatformFacadeMarker Value => null; }",
            directory: fixtureDir,
            assemblyName: "fixture",
            [MetadataReference.CreateFromFile(facadePath)]);
        using (var stream = File.OpenRead(platformPath))
        using (var pe = new PEReader(stream))
        {
            Version platformVersion = pe.GetMetadataReader().GetAssemblyDefinition().Version;
            File.WriteAllBytes(
                Path.Combine(fixtureDir, "System.Text.Json.dll"),
                BuildConfusableAssemblyImage(
                    platformPath,
                    new Version(platformVersion.Major - 1, 0, 0, 0)));
        }
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(assemblyPath));
            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable),
                error.Message);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    // Close-negative for the shadow-decline (#3112 review): a compiler-authored sibling whose
    // simple name matches a *non-leading* segment of the interface spelling must NOT trigger a
    // decline. Only the FIRST segment (`System`) can be shadowed into a compile error, because
    // the explicit-member qualifier is always emitted fully qualified
    // (`System.Collections.IEnumerable.GetEnumerator`) and the collision-aware using-collapser
    // only shortens the base-list entry to the bare `IEnumerable` when nothing collides:
    //  - `N.Collections` (middle segment): collapser shortens to `class Seq : IEnumerable`; the
    //    middle `Collections` never leads, so it compiles.
    //  - `N.IEnumerable` (final type name): collapser detects the collision and KEEPS the base
    //    list fully qualified (`class Seq : System.Collections.IEnumerable`, leading `System`),
    //    so it still compiles.
    // Under RoundTripScope.All the sibling is reconstructed alongside the real explicit impl and
    // the whole shape must round-trip Exact, not fall back to the sanitized
    // `System_Collections_IEnumerable_GetEnumerator` floor.
    [Theory]
    [InlineData("Collections")]
    [InlineData("IEnumerable")]
    public async Task CompileBackTargets_ExternalExplicitInterfaceKeepsExactWhenClosureSiblingMatchesNonLeadingSegment(string siblingName)
    {
        var assemblyPath = CompileFixture($$"""
            namespace N;
            public sealed class {{siblingName}} { }
            public sealed class Seq : System.Collections.IEnumerable
            {
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    throw null;
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "N.Seq",
                    "System.Collections.IEnumerable.GetEnumerator",
                    0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains(
                "System.Collections.IEnumerable.GetEnumerator()",
                result.Source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "System_Collections_IEnumerable_GetEnumerator",
                result.Source,
                StringComparison.Ordinal);
            Assert.Contains($"class {siblingName}", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
