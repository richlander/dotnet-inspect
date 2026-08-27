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

[Collection(ConsoleMutatorCollection.Name)]
public class FidelityCheckGeneratedFilterTests
{
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
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesEffectiveUsingAndChildNamespaceCollisions()
    {
        var assemblyPath = CompileFixture("""
            namespace Contracts
            {
                public interface ICloneable { object Clone(); }

                public sealed class SameNamespace : ICloneable
                {
                    object ICloneable.Clone() => new SameNamespace();
                }
            }

            namespace App
            {
                public sealed class SkeletonUsingCollision : Contracts.ICloneable
                {
                    object Contracts.ICloneable.Clone() => new SkeletonUsingCollision();
                }
            }

            namespace N
            {
                public interface I { void M(); }
            }

            namespace T
            {
                public sealed class ChildNamespaceCollision : N.I
                {
                    void N.I.M() { }
                }
            }

            namespace T.I
            {
                public sealed class X { }
            }

            namespace Models
            {
                public sealed class Spanner { }
                public sealed class T { }
                public sealed class Timer { }
                public sealed class Widget { }

                public class Outer
                {
                    public sealed class Inner { }
                }
            }

            namespace SignatureCollision
            {
                public interface I { void M(Models.Timer value); }
                public interface IBaseNested { void M(Models.Widget value); }
                public interface IBody { object M(); }
                public interface IGeneric { void M<T>(Models.T value); }
                public interface INested { void M(Models.Widget value); }
                public interface IQualifiedMethodGeneric
                {
                    Models.Outer.Inner Create<Outer>();
                }
                public interface IQualifiedNested { object Create(); }
                public interface IQualifiedTypeGeneric
                {
                    Models.Outer.Inner Create();
                }
                public interface ITypeGeneric { void M(Models.T value); }

                public class BaseWithNested
                {
                    public sealed class Widget { }
                }

                public sealed class BaseNested : BaseWithNested, IBaseNested
                {
                    void IBaseNested.M(Models.Widget value) { }
                }

                public sealed class Body : IBody
                {
                    public sealed class Spanner { }
                    object IBody.M() => new Models.Spanner();
                }

                public sealed class C : I
                {
                    void I.M(Models.Timer value) { }
                }

                public sealed class Generic : IGeneric
                {
                    void IGeneric.M<T>(Models.T value) { }
                }

                public sealed class Nested : INested
                {
                    public sealed class Widget { }
                    void INested.M(Models.Widget value) { }
                }

                public sealed class QualifiedNested : IQualifiedNested
                {
                    public class Outer
                    {
                        public sealed class Inner { }
                    }

                    object IQualifiedNested.Create()
                        => new Models.Outer.Inner();
                }

                public sealed class QualifiedMethodGeneric
                    : IQualifiedMethodGeneric
                {
                    Models.Outer.Inner IQualifiedMethodGeneric.Create<Outer>()
                        => new Models.Outer.Inner();
                }

                public sealed class QualifiedTypeGeneric<Outer>
                    : IQualifiedTypeGeneric
                {
                    Models.Outer.Inner IQualifiedTypeGeneric.Create()
                        => new Models.Outer.Inner();
                }

                public sealed class TypeGeneric<T> : ITypeGeneric
                {
                    void ITypeGeneric.M(Models.T value) { }
                }
            }

            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, admitted) in new[]
            {
                ("SameNamespace", "Contracts.ICloneable.Clone", true),
                ("SkeletonUsingCollision", "Contracts.ICloneable.Clone", false),
                ("ChildNamespaceCollision", "N.I.M", false),
                ("BaseNested", "SignatureCollision.IBaseNested.M", false),
                ("Body", "SignatureCollision.IBody.M", false),
                ("C", "SignatureCollision.I.M", false),
                ("Generic", "SignatureCollision.IGeneric.M", false),
                ("Nested", "SignatureCollision.INested.M", false),
                ("QualifiedMethodGeneric", "SignatureCollision.IQualifiedMethodGeneric.Create", false),
                ("QualifiedNested", "SignatureCollision.IQualifiedNested.Create", false),
                ("QualifiedTypeGeneric`1", "SignatureCollision.IQualifiedTypeGeneric.Create", false),
                ("TypeGeneric`1", "SignatureCollision.ITypeGeneric.M", false)
            })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == methodName);
                foreach (bool targeted in new[] { true, false })
                {
                    var rendered = FidelityCheck.TryRenderTargetMember(
                        pe,
                        source,
                        method,
                        targeted,
                        isPrimaryConstructor: false);
                    if (admitted)
                        Assert.True(
                            rendered.HasValue,
                            $"{typeName}: targeted={targeted}");
                    else
                        Assert.Null(rendered);
                }
            }

            var targetedResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "Contracts.SameNamespace"
                    or "App.SkeletonUsingCollision"
                    or "T.ChildNamespaceCollision"
                    or "SignatureCollision.BaseNested"
                    or "SignatureCollision.Body"
                    or "SignatureCollision.C"
                    or "SignatureCollision.Generic"
                    or "SignatureCollision.Nested"
                    or "SignatureCollision.QualifiedMethodGeneric"
                    or "SignatureCollision.QualifiedNested"
                    or "SignatureCollision.QualifiedTypeGeneric`1"
                    or "SignatureCollision.TypeGeneric`1",
                candidate => candidate.Method.EndsWith(
                    "Clone",
                    StringComparison.Ordinal)
                    || candidate.Method.EndsWith(".M", StringComparison.Ordinal)
                    || candidate.Method.EndsWith(
                        ".Create",
                        StringComparison.Ordinal));
            var batchResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "Contracts.SameNamespace"
                    or "App.SkeletonUsingCollision"
                    or "T.ChildNamespaceCollision"
                    or "SignatureCollision.BaseNested"
                    or "SignatureCollision.Body"
                    or "SignatureCollision.C"
                    or "SignatureCollision.Generic"
                    or "SignatureCollision.Nested"
                    or "SignatureCollision.QualifiedMethodGeneric"
                    or "SignatureCollision.QualifiedNested"
                    or "SignatureCollision.QualifiedTypeGeneric`1"
                    or "SignatureCollision.TypeGeneric`1");
            foreach (var results in new[] { targetedResults, batchResults })
            {
                var control = Assert.Single(
                    results,
                    result => result.Type == "Contracts.SameNamespace"
                        && result.Method == "Contracts.ICloneable.Clone");
                Assert.True(control.UsedProductWholeMember);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, control.Status);
                foreach (string typeName in new[]
                {
                    "App.SkeletonUsingCollision",
                    "T.ChildNamespaceCollision",
                    "SignatureCollision.BaseNested",
                    "SignatureCollision.Body",
                    "SignatureCollision.C",
                    "SignatureCollision.Generic",
                    "SignatureCollision.Nested",
                    "SignatureCollision.QualifiedMethodGeneric",
                    "SignatureCollision.QualifiedNested",
                    "SignatureCollision.QualifiedTypeGeneric`1",
                    "SignatureCollision.TypeGeneric`1"
                })
                {
                    Assert.False(Assert.Single(
                        results,
                        result => result.Type == typeName
                            && (result.Method.EndsWith(
                                    "Clone",
                                    StringComparison.Ordinal)
                                || result.Method.EndsWith(
                                    ".M",
                                    StringComparison.Ordinal)
                                || result.Method.EndsWith(
                                    ".Create",
                                    StringComparison.Ordinal)))
                        .UsedProductWholeMember);
                }
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesQualifiedRootMultiplicityCollisions()
    {
        var assemblyPath = CompileFixture("""
            namespace Models
            {
                public class Safe
                {
                    public sealed class Inner { }
                }

                public class Timer
                {
                    public sealed class Inner { }
                }

                public class Widget
                {
                    public sealed class Inner { }
                }
            }

            namespace App
            {
                public class Widget
                {
                    public sealed class Inner { }
                }

                public interface IControl { object Create(); }
                public interface ISibling { object Create(); }
                public interface IUsing { Models.Timer.Inner Create(); }

                public sealed class Control : IControl
                {
                    object IControl.Create() => new Models.Safe.Inner();
                }

                public sealed class Sibling : ISibling
                {
                    object ISibling.Create() => new Models.Widget.Inner();
                }

                public sealed class Using : IUsing
                {
                    Models.Timer.Inner IUsing.Create()
                        => new Models.Timer.Inner();
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, admitted) in new[]
            {
                ("Control", "App.IControl.Create", true),
                ("Sibling", "App.ISibling.Create", false),
                ("Using", "App.IUsing.Create", false),
            })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == methodName);
                foreach (bool targeted in new[] { true, false })
                {
                    var rendered = FidelityCheck.TryRenderTargetMember(
                        pe,
                        source,
                        method,
                        targeted,
                        isPrimaryConstructor: false);
                    if (admitted)
                        Assert.True(rendered.HasValue, $"{typeName}: targeted={targeted}");
                    else
                        Assert.Null(rendered);
                }
            }

            var targetedResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "App.Control" or "App.Sibling" or "App.Using",
                candidate => candidate.Method.EndsWith(
                    ".Create",
                    StringComparison.Ordinal));
            var batchResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "App.Control" or "App.Sibling" or "App.Using");
            foreach (var results in new[] { targetedResults, batchResults })
            {
                var control = Assert.Single(
                    results,
                    result => result.Type == "App.Control"
                        && result.Method == "App.IControl.Create");
                Assert.True(control.UsedProductWholeMember);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, control.Status);
                foreach (string typeName in new[] { "App.Sibling", "App.Using" })
                {
                    Assert.False(Assert.Single(
                        results,
                        result => result.Type == typeName
                            && result.Method.EndsWith(
                                ".Create",
                                StringComparison.Ordinal))
                        .UsedProductWholeMember);
                }
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesReferencedLexicalAndFriendCollisions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string dependencyPath = Path.Combine(directory, "Neighbour.dll");
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            CompileAssembly(
                dependencyPath,
                """
                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("cb")]

                namespace App.IR8Contract
                {
                    public sealed class X { }
                }

                namespace System
                {
                    internal sealed class IR8Friend { }
                }

                namespace A.B
                {
                    public sealed class IFoo { }
                }
                """);
            CompileAssembly(
                assemblyPath,
                """
                namespace N
                {
                    public interface IR8Contract { void M(); }
                }

                namespace App
                {
                    public sealed class ReferencedChildNamespace : N.IR8Contract
                    {
                        void N.IR8Contract.M() { }
                    }
                }

                namespace Contracts
                {
                    public interface IR8Friend { void M(); }
                }

                namespace Other
                {
                    public sealed class FriendVisibleType : Contracts.IR8Friend
                    {
                        void Contracts.IR8Friend.M() { }
                    }
                }

                namespace A
                {
                    public interface IFoo { void M(); }

                    public sealed class Control : IFoo
                    {
                        void IFoo.M() { }
                    }
                }

                namespace A.B
                {
                    public sealed class NearerReferencedType : A.IFoo
                    {
                        void A.IFoo.M() { }
                    }
                }

                namespace System
                {
                    public interface IEnumerator { void M(); }
                }

                namespace System.Collections
                {
                    public sealed class FrameworkNearerType : System.IEnumerator
                    {
                        void System.IEnumerator.M() { }
                    }
                }
                """,
                [MetadataReference.CreateFromFile(dependencyPath)]);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, admitted) in new[]
            {
                ("Control", "A.IFoo.M", true),
                ("ReferencedChildNamespace", "N.IR8Contract.M", false),
                ("FriendVisibleType", "Contracts.IR8Friend.M", false),
                ("NearerReferencedType", "A.IFoo.M", false),
                ("FrameworkNearerType", "System.IEnumerator.M", false)
            })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == methodName);
                foreach (bool targeted in new[] { true, false })
                {
                    var rendered = FidelityCheck.TryRenderTargetMember(
                        pe,
                        source,
                        method,
                        targeted,
                        isPrimaryConstructor: false);
                    if (admitted)
                        Assert.NotNull(rendered);
                    else
                        Assert.Null(rendered);
                }
            }

            var targetedResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "A.Control"
                    or "App.ReferencedChildNamespace"
                    or "Other.FriendVisibleType"
                    or "A.B.NearerReferencedType"
                    or "System.Collections.FrameworkNearerType",
                candidate => candidate.Method.EndsWith(".M", StringComparison.Ordinal));
            var batchResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName is "A.Control"
                    or "App.ReferencedChildNamespace"
                    or "Other.FriendVisibleType"
                    or "A.B.NearerReferencedType"
                    or "System.Collections.FrameworkNearerType");
            foreach (var results in new[] { targetedResults, batchResults })
            {
                var control = Assert.Single(
                    results,
                    result => result.Type == "A.Control"
                        && result.Method == "A.IFoo.M");
                Assert.True(control.UsedProductWholeMember);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, control.Status);
                foreach (string typeName in new[]
                {
                    "App.ReferencedChildNamespace",
                    "Other.FriendVisibleType",
                    "A.B.NearerReferencedType",
                    "System.Collections.FrameworkNearerType"
                })
                {
                    Assert.False(Assert.Single(
                        results,
                        result => result.Type == typeName
                            && result.Method.EndsWith(".M", StringComparison.Ordinal))
                        .UsedProductWholeMember);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesReferencesWithLinkedMetadataModules()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string modulePath = Path.Combine(directory, "Part.netmodule");
        string dependencyPath = Path.Combine(directory, "Neighbour.dll");
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            CompileAssembly(
                modulePath,
                """
                namespace App
                {
                    internal sealed class ILinked { }
                }
                """,
                outputKind: OutputKind.NetModule);
            CompileAssembly(
                dependencyPath,
                """
                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("cb")]
                public sealed class ManifestType { }
                """,
                [MetadataReference.CreateFromFile(
                    modulePath,
                    MetadataReferenceProperties.Module)]);
            CompileAssembly(
                assemblyPath,
                """
                namespace Contracts
                {
                    public interface ILinked { void M(); }
                }

                namespace App
                {
                    public sealed class C : Contracts.ILinked
                    {
                        void Contracts.ILinked.M() { }
                    }
                }
                """,
                [MetadataReference.CreateFromFile(dependencyPath)]);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "C"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "Contracts.ILinked.M");
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: false,
                isPrimaryConstructor: false));

            foreach (var results in new[]
            {
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "App.C",
                    candidate => candidate.Method == "Contracts.ILinked.M"),
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "App.C")
            })
            {
                Assert.False(Assert.Single(
                    results,
                    result => result.Method == "Contracts.ILinked.M")
                    .UsedProductWholeMember);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesSyntaxInvalidMetadataName()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            var assemblyName = new AssemblyName("InvalidExplicitMethodName");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var interfaceType = module.DefineType(
                "I",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            var declaration = interfaceType.DefineMethod(
                "bad-name",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            var type = module.DefineType(
                "C",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [interfaceType]);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            var body = type.DefineMethod(
                "I.bad-name",
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            body.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(body, declaration);
            interfaceType.CreateType();
            type.CreateType();
            assemblyBuilder.Save(assemblyPath);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeDef = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "C"));
            var method = Assert.Single(
                typeDef.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "I.bad-name");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: false,
                isPrimaryConstructor: false));

            foreach (var results in new[]
            {
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "C",
                    candidate => candidate.Method == "I.bad-name"),
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "C")
            })
            {
                Assert.False(Assert.Single(
                    results,
                    result => result.Method == "I.bad-name")
                    .UsedProductWholeMember);
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesDuplicateMetadataIdentifiers()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            var assemblyName = new AssemblyName("DuplicateExplicitMethodIdentifiers");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

            var parameterInterface = module.DefineType(
                "IParameters",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            var parameterDeclaration = parameterInterface.DefineMethod(
                "M",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                [typeof(int), typeof(int)]);
            parameterDeclaration.DefineParameter(1, ParameterAttributes.None, "x");
            parameterDeclaration.DefineParameter(2, ParameterAttributes.None, "x");
            var parameterType = module.DefineType(
                "DuplicateParameters",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [parameterInterface]);
            parameterType.DefineDefaultConstructor(MethodAttributes.Public);
            var parameterBody = parameterType.DefineMethod(
                "IParameters.M",
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                [typeof(int), typeof(int)]);
            parameterBody.DefineParameter(1, ParameterAttributes.None, "x");
            parameterBody.DefineParameter(2, ParameterAttributes.None, "x");
            parameterBody.GetILGenerator().Emit(OpCodes.Ret);
            parameterType.DefineMethodOverride(parameterBody, parameterDeclaration);

            var genericInterface = module.DefineType(
                "IGenericParameters",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            var genericDeclaration = genericInterface.DefineMethod(
                "M",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            genericDeclaration.DefineGenericParameters("T", "T");
            var genericType = module.DefineType(
                "DuplicateGenericParameters",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [genericInterface]);
            genericType.DefineDefaultConstructor(MethodAttributes.Public);
            var genericBody = genericType.DefineMethod(
                "IGenericParameters.M",
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            genericBody.DefineGenericParameters("T", "T");
            genericBody.GetILGenerator().Emit(OpCodes.Ret);
            genericType.DefineMethodOverride(genericBody, genericDeclaration);

            var parameterGenericInterface = module.DefineType(
                "IParameterGeneric",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract);
            var parameterGenericDeclaration = parameterGenericInterface.DefineMethod(
                "M",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot);
            var declarationTypeParameter =
                parameterGenericDeclaration.DefineGenericParameters("T")[0];
            parameterGenericDeclaration.SetReturnType(typeof(void));
            parameterGenericDeclaration.SetParameters(declarationTypeParameter);
            parameterGenericDeclaration.DefineParameter(
                1,
                ParameterAttributes.None,
                "T");
            var parameterGenericType = module.DefineType(
                "ParameterGenericCollision",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [parameterGenericInterface]);
            parameterGenericType.DefineDefaultConstructor(MethodAttributes.Public);
            var parameterGenericBody = parameterGenericType.DefineMethod(
                "IParameterGeneric.M",
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot);
            var bodyTypeParameter =
                parameterGenericBody.DefineGenericParameters("T")[0];
            parameterGenericBody.SetReturnType(typeof(void));
            parameterGenericBody.SetParameters(bodyTypeParameter);
            parameterGenericBody.DefineParameter(
                1,
                ParameterAttributes.None,
                "T");
            parameterGenericBody.GetILGenerator().Emit(OpCodes.Ret);
            parameterGenericType.DefineMethodOverride(
                parameterGenericBody,
                parameterGenericDeclaration);

            parameterInterface.CreateType();
            genericInterface.CreateType();
            parameterGenericInterface.CreateType();
            parameterType.CreateType();
            genericType.CreateType();
            parameterGenericType.CreateType();
            assemblyBuilder.Save(assemblyPath);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName) in new[]
            {
                ("DuplicateParameters", "IParameters.M"),
                ("DuplicateGenericParameters", "IGenericParameters.M"),
                ("ParameterGenericCollision", "IParameterGeneric.M")
            })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == methodName);
                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: false,
                    isPrimaryConstructor: false));
            }

            foreach (var results in new[]
            {
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName.StartsWith(
                        "Duplicate",
                        StringComparison.Ordinal),
                    candidate => candidate.Method.EndsWith(".M", StringComparison.Ordinal)),
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName.StartsWith(
                        "Duplicate",
                        StringComparison.Ordinal))
            })
            {
                Assert.All(
                    results.Where(result => result.Method.EndsWith(
                        ".M",
                        StringComparison.Ordinal)),
                    result => Assert.False(result.UsedProductWholeMember));
            }
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
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
    public void Evaluate_UsesProductWholeMemberForSingleMethodExplicitInterface()
    {
        var assemblyPath = CompileFixture("""
            public interface IExplicitMethod
            {
                int Compute(int value);
            }

            public sealed class ExplicitMethodFixture : IExplicitMethod
            {
                int IExplicitMethod.Compute(int value) => value + 1;
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "ExplicitMethodFixture"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "IExplicitMethod.Compute");

            using var source = MetadataSource.Open(assemblyPath);
            var wholeMember = FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false);

            Assert.NotNull(wholeMember);
            var declaration = Assert.IsType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseMemberDeclaration(
                    wholeMember.Value.Text));
            Assert.NotNull(declaration.ExplicitInterfaceSpecifier);

            var targetedResult = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "ExplicitMethodFixture",
                    candidate => candidate.Method == "IExplicitMethod.Compute"));
            Assert.True(targetedResult.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, targetedResult.Status);

            var batchResult = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "ExplicitMethodFixture"),
                candidate => candidate.Method == "IExplicitMethod.Compute");
            Assert.True(batchResult.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, batchResult.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeclinesProductWholeMemberForVarargExplicitInterface()
    {
        var assemblyPath = CompileFixture("""
            namespace Contracts
            {
                public interface IVararg
                {
                    void Invoke(__arglist);
                }
            }

            public sealed class VarargFixture : Contracts.IVararg
            {
                void Contracts.IVararg.Invoke(__arglist) { }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "VarargFixture"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "Contracts.IVararg.Invoke");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: false,
                isPrimaryConstructor: false));

            var targetedResult = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "VarargFixture",
                    candidate => candidate.Method == "Contracts.IVararg.Invoke"));
            Assert.False(targetedResult.UsedProductWholeMember);

            var batchResult = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "VarargFixture"),
                candidate => candidate.Method == "Contracts.IVararg.Invoke");
            Assert.False(batchResult.UsedProductWholeMember);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_UsesProductWholeMemberForGenericExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            public interface IGenericMethod
            {
                T Echo<T>(T value) where T : class;
            }

            public sealed class GenericMethodFixture : IGenericMethod
            {
                T IGenericMethod.Echo<T>(T value) => value;
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "GenericMethodFixture",
                    candidate => candidate.Method == "IGenericMethod.Echo"));
            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_UsesProductWholeMemberWhenOnlyInterfaceParameterIsOptional()
    {
        var assemblyPath = CompileFixture("""
            public interface IOptional
            {
                int Compute(int value = 42);
            }

            public sealed class OptionalFixture : IOptional
            {
                int IOptional.Compute(int value) => value + 1;
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "OptionalFixture",
                    candidate => candidate.Method == "IOptional.Compute"));
            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeclinesQualifiedExplicitInterfaceWhenTypeShadowsNamespace()
    {
        var assemblyPath = CompileFixture("""
            namespace N
            {
                public sealed class N { }
                public interface I { void M(); }
                public sealed class C : I { void I.M() { } }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "C"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "N.I.M");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "N.C",
                    candidate => candidate.Method == "N.I.M"));
            Assert.False(result.UsedProductWholeMember);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesMismatchedMethodImplBodyName()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            var assemblyName = new AssemblyName("MismatchedExplicitMethod");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var iType = module.DefineType(
                "I",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            var iMethod = iType.DefineMethod(
                "M",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            var jType = module.DefineType(
                "J",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            jType.DefineMethod(
                "M",
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            var type = module.DefineType(
                "C",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [iType]);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            var body = type.DefineMethod(
                "J.M",
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                typeof(void),
                Type.EmptyTypes);
            body.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(body, iMethod);
            iType.CreateType();
            jType.CreateType();
            type.CreateType();
            assemblyBuilder.Save(assemblyPath);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeDef = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "C"));
            var method = Assert.Single(
                typeDef.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "J.M");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesAmbiguousAndIncompatibleMethodImplRows()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            var assemblyName = new AssemblyName("IncompatibleExplicitMethods");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

            var firstInterface = DefineInterface(module, "A", "B.C", typeof(void), Type.EmptyTypes);
            var secondInterface = DefineInterface(module, "A.B", "C", typeof(void), Type.EmptyTypes);
            var ambiguousType = module.DefineType(
                "Ambiguous",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [firstInterface.Type, secondInterface.Type]);
            ambiguousType.DefineDefaultConstructor(MethodAttributes.Public);
            var ambiguousBody = DefineBody(
                ambiguousType,
                "A.B.C",
                typeof(void),
                Type.EmptyTypes);
            ambiguousType.DefineMethodOverride(ambiguousBody, firstInterface.Method);
            ambiguousType.DefineMethodOverride(ambiguousBody, secondInterface.Method);

            var mismatchInterface = DefineInterface(
                module,
                "IMismatch",
                "M",
                typeof(int),
                Type.EmptyTypes);
            var mismatchType = module.DefineType(
                "Mismatch",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [mismatchInterface.Type]);
            mismatchType.DefineDefaultConstructor(MethodAttributes.Public);
            var mismatchBody = DefineBody(
                mismatchType,
                "IMismatch.M",
                typeof(void),
                Type.EmptyTypes);
            mismatchType.DefineMethodOverride(mismatchBody, mismatchInterface.Method);

            var paramsInterface = DefineInterface(
                module,
                "IBodyParams",
                "M",
                typeof(void),
                [typeof(int[])]);
            var paramsType = module.DefineType(
                "BodyParams",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [paramsInterface.Type]);
            paramsType.DefineDefaultConstructor(MethodAttributes.Public);
            var paramsBody = DefineBody(
                paramsType,
                "IBodyParams.M",
                typeof(void),
                [typeof(int[])]);
            paramsBody.DefineParameter(1, ParameterAttributes.None, "values")
                .SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!,
                    []));
            paramsType.DefineMethodOverride(paramsBody, paramsInterface.Method);

            var refInterface = DefineInterface(
                module,
                "IRefKind",
                "M",
                typeof(void),
                [typeof(int).MakeByRefType()]);
            refInterface.Method.DefineParameter(
                1,
                ParameterAttributes.None,
                "value");
            var outType = module.DefineType(
                "OutBody",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                [refInterface.Type]);
            outType.DefineDefaultConstructor(MethodAttributes.Public);
            var outBody = DefineBody(
                outType,
                "IRefKind.M",
                typeof(void),
                [typeof(int).MakeByRefType()]);
            outBody.DefineParameter(
                1,
                ParameterAttributes.Out,
                "value");
            outType.DefineMethodOverride(outBody, refInterface.Method);

            firstInterface.Type.CreateType();
            secondInterface.Type.CreateType();
            mismatchInterface.Type.CreateType();
            paramsInterface.Type.CreateType();
            refInterface.Type.CreateType();
            ambiguousType.CreateType();
            mismatchType.CreateType();
            paramsType.CreateType();
            outType.CreateType();
            assemblyBuilder.Save(assemblyPath);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            foreach ((string Type, string Method) expected in new[]
                {
                    ("Ambiguous", "A.B.C"),
                    ("Mismatch", "IMismatch.M"),
                    ("BodyParams", "IBodyParams.M"),
                    ("OutBody", "IRefKind.M"),
                })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == expected.Type));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == expected.Method);

                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static (TypeBuilder Type, MethodBuilder Method) DefineInterface(
            ModuleBuilder module,
            string typeName,
            string methodName,
            Type returnType,
            Type[] parameterTypes)
        {
            var type = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            var method = type.DefineMethod(
                methodName,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                returnType,
                parameterTypes);
            return (type, method);
        }

        static MethodBuilder DefineBody(
            TypeBuilder type,
            string name,
            Type returnType,
            Type[] parameterTypes)
        {
            var method = type.DefineMethod(
                name,
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot,
                returnType,
                parameterTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            return method;
        }
    }

    [Fact]
    public void ExplicitInterfaceCollisionWalk_RejectsCyclicBaseGraph()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("CyclicBases.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CyclicBases"),
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
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("A"),
            MetadataTokens.TypeDefinitionHandle(3),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("B"),
            MetadataTokens.TypeDefinitionHandle(2),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            MetadataTokens.TypeDefinitionHandle(2),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata, suppressValidation: true)
            .Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(
            System.Collections.Immutable.ImmutableArray.Create(image.ToArray()));

        Assert.True(FidelityCheck.HasNestedOrBaseInterfaceIdentifierCollision(
            provider.GetMetadataReader(),
            target,
            "N"));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesNamespacedNestedInterfaceIdentifierCollisions()
    {
        var assemblyPath = CompileFixture("""
            namespace Contracts
            {
                public interface IControl { int Run(int value); }
                public interface IHandler { int Run(int value); }
                public interface IWalker { int Run(int value); }
            }

            namespace Consumers
            {
                public sealed class Control : Contracts.IControl
                {
                    int Contracts.IControl.Run(int value) => value + 1;
                }

                public sealed class NestedShadow : Contracts.IHandler
                {
                    public sealed class IHandler { }
                    int Contracts.IHandler.Run(int value) => value + 1;
                }

                public class BaseWithNested
                {
                    public sealed class IWalker { }
                }

                public sealed class BaseNestedShadow
                    : BaseWithNested, Contracts.IWalker
                {
                    int Contracts.IWalker.Run(int value) => value + 1;
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);

            foreach (var (typeName, methodName, admitted) in new[]
            {
                ("Control", "Contracts.IControl.Run", true),
                ("NestedShadow", "Contracts.IHandler.Run", false),
                ("BaseNestedShadow", "Contracts.IWalker.Run", false)
            })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                        == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        == methodName);

                var rendered = FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false);
                if (admitted)
                    Assert.True(
                        rendered.HasValue,
                        $"{typeName}: targeted=true");
                else
                    Assert.Null(rendered);
            }

            var targetedResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName.StartsWith("Consumers.", StringComparison.Ordinal),
                candidate => candidate.Method.EndsWith(".Run", StringComparison.Ordinal));
            var batchResults = FidelityCheck.Evaluate(
                assemblyPath,
                typeName => typeName.StartsWith("Consumers.", StringComparison.Ordinal));
            foreach (var results in new[] { targetedResults, batchResults })
            {
                var control = Assert.Single(
                    results,
                    result => result.Type == "Consumers.Control"
                        && result.Method == "Contracts.IControl.Run");
                Assert.True(control.UsedProductWholeMember);
                Assert.Equal(FidelityCheck.CompileBackStatus.Exact, control.Status);
                foreach (string typeName in new[]
                {
                    "Consumers.NestedShadow",
                    "Consumers.BaseNestedShadow"
                })
                {
                    Assert.False(Assert.Single(
                        results,
                        result => result.Type == typeName
                            && result.Method.EndsWith(".Run", StringComparison.Ordinal))
                        .UsedProductWholeMember);
                }
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesNestedAndImportedInterfaceIdentifierCollisions()
    {
        var assemblyPath = CompileFixture("""
            using NI = N.I;

            namespace N
            {
                public interface I
                {
                    void M(T.I first, X.N second);
                }
            }

            namespace T
            {
                public sealed class I { }

                public sealed class NestedCollision : NI
                {
                    public sealed class N { }
                    void NI.M(I first, X.N second) { }
                }

                public sealed class ImportedCollision : NI
                {
                    void NI.M(I first, X.N second) { }
                }
            }

            namespace X
            {
                public sealed class N { }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);

            foreach (string typeName in new[] { "NestedCollision", "ImportedCollision" })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "N.I.M");

                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesAdditionalExplicitInterfaceBindingHazards()
    {
        var assemblyPath = CompileFixture("""
            using NI = N.I;
            using BI = B.I;
            using FII = FileInfo.I;

            public interface IGlobal
            {
                void M();
            }

            public sealed class GenericShadow<IGlobal> : global::IGlobal
            {
                void global::IGlobal.M() { }
            }

            public sealed class NestedShadow : global::IGlobal
            {
                public sealed class IGlobal { }
                void global::IGlobal.M() { }
            }

            public interface ITuple
            {
                (int A, string B) Get();
            }

            public sealed class TupleFixture : ITuple
            {
                (int A, string B) ITuple.Get() => (1, "x");
            }

            public interface IStreamConstraint
            {
                T Create<T>() where T : System.IO.Stream;
            }

            public sealed class StreamConstraintFixture : IStreamConstraint
            {
                T IStreamConstraint.Create<T>() => throw null;
            }

            namespace N
            {
                public interface I
                {
                    void M(A.I value);
                }
            }

            namespace Q
            {
                public interface I
                {
                    void M<Q>();
                }

                public sealed class MethodGenericShadow : I
                {
                    void I.M<Q>() { }
                }
            }

            namespace B
            {
                public interface I
                {
                    int T(int value);
                }
            }

            namespace FileInfo
            {
                public interface I
                {
                    void M(T.I value);
                }
            }

            namespace T
            {
                public sealed class I { }
            }

            namespace A
            {
                public sealed class I { }
                public sealed class N { }
            }

            namespace A.B
            {
                public interface I
                {
                    void Other();
                }

                public sealed class ParentNamespaceShadow : NI
                {
                    void NI.M(A.I value) { }
                }

                public sealed class NestedNamespaceShadow : BI
                {
                    int BI.T(int value) => value + 1;
                }
            }

            namespace System.IO.Blah
            {
                public sealed class ReferencedParentTypeShadow : FII
                {
                    void FII.M(T.I value) { }
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);

            foreach (string typeName in new[]
                {
                    "GenericShadow`1",
                    "NestedShadow",
                    "TupleFixture",
                    "StreamConstraintFixture",
                    "MethodGenericShadow",
                    "ParentNamespaceShadow",
                    "NestedNamespaceShadow",
                    "ReferencedParentTypeShadow",
                })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                        != ".ctor");

                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeduplicatesProductAndLegacyInterfaceClauses()
    {
        var assemblyPath = CompileFixture("""
            public interface IResource
            {
                void M();
            }

            public sealed class DuplicateInterfaceFixture : IResource
            {
                public void M() { }
                void IResource.M() { }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "DuplicateInterfaceFixture",
                    candidate => candidate.Method == "IResource.M"));
            Assert.True(result.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesUnsupportedInterfaceParameterModifiers()
    {
        var assemblyPath = CompileFixture("""
            public interface IReadonlyRef
            {
                void M(ref readonly int value);
            }

            public sealed class ReadonlyRefFixture : IReadonlyRef
            {
                void IReadonlyRef.M(ref readonly int value) { }
            }

            public interface IParams
            {
                void M(params int[] values);
            }

            public sealed class ParamsFixture : IParams
            {
                void IParams.M(params int[] values) { }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);

            foreach (string typeName in new[] { "ReadonlyRefFixture", "ParamsFixture" })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name).EndsWith(
                        ".M",
                        StringComparison.Ordinal));

                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeclinesUnspellableExplicitInterfaceSignature()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string libraryPath = Path.Combine(directory, "HiddenContract.dll");
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            CompileAssembly(
                libraryPath,
                """
                using System.Runtime.CompilerServices;

                [assembly: InternalsVisibleTo("fixture")]

                namespace HiddenContract;

                internal sealed class Secret
                {
                    internal int Value;
                }
                """);
            CompileAssembly(
                assemblyPath,
                """
                internal interface IHidden
                {
                    int Use(HiddenContract.Secret value);
                }

                public sealed class HiddenImplementation : IHidden
                {
                    int IHidden.Use(HiddenContract.Secret value)
                        => value.Value;
                }
                """,
                [MetadataReference.CreateFromFile(libraryPath)]);

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "HiddenImplementation",
                    candidate => candidate.Method == "IHidden.Use"));
            Assert.False(result.UsedProductWholeMember);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeclinesUnavailableExplicitInterfaceSignatureType()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string libraryPath = Path.Combine(directory, "MissingContract.dll");
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            CompileAssembly(
                libraryPath,
                """
                namespace MissingContract;

                public sealed class External { }
                """);
            CompileAssembly(
                assemblyPath,
                """
                public interface I
                {
                    MissingContract.External Echo(
                        MissingContract.External value);
                }

                public sealed class MissingImplementation : I
                {
                    MissingContract.External I.Echo(
                        MissingContract.External value) => value;
                }
                """,
                [MetadataReference.CreateFromFile(libraryPath)]);

            var control = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "MissingImplementation",
                    candidate => candidate.Method == "I.Echo"));
            Assert.True(control.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, control.Status);

            CompileAssembly(
                libraryPath,
                """
                namespace MissingContract;

                public sealed class Other { }
                """);
            AssertDeclined();

            File.Delete(libraryPath);
            AssertDeclined();

            void AssertDeclined()
            {
                var targetedResults = FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "MissingImplementation",
                    candidate => candidate.Method == "I.Echo");
                var batchResults = FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "MissingImplementation");
                foreach (var results in new[] { targetedResults, batchResults })
                {
                    Assert.False(Assert.Single(
                        results,
                        result => result.Method == "I.Echo")
                        .UsedProductWholeMember);
                }
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesImportedExternalTypeCollision()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string libraryPath = Path.Combine(directory, "ExternalTypes.dll");
        string assemblyPath = Path.Combine(directory, "fixture.dll");
        try
        {
            CompileAssembly(
                libraryPath,
                """
                namespace X;

                public sealed class N { }
                """);
            CompileAssembly(
                assemblyPath,
                """
                using NI = N.I;

                namespace N
                {
                    public interface I
                    {
                        void M(T.I first, X.N second);
                    }
                }

                namespace T
                {
                    public sealed class I { }

                    public sealed class ImportedExternalCollision : NI
                    {
                        void NI.M(I first, X.N second) { }
                    }
                }
                """,
                [MetadataReference.CreateFromFile(libraryPath)]);

            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "ImportedExternalCollision"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "N.I.M");
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_DeclinesProductWholeMemberForMultiMethodExplicitInterface()
    {
        var assemblyPath = CompileFixture("""
            public interface IMultiMethod
            {
                int Compute(int value);
                void Reset();
            }

            public sealed class MultiMethodFixture : IMultiMethod
            {
                int IMultiMethod.Compute(int value) => value + 1;
                void IMultiMethod.Reset() { }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "MultiMethodFixture"));
            var method = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "IMultiMethod.Compute");

            using var source = MetadataSource.Open(assemblyPath);
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                method,
                targeted: true,
                isPrimaryConstructor: false));

            var result = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "MultiMethodFixture",
                    candidate => candidate.Method == "IMultiMethod.Compute"));
            Assert.False(result.UsedProductWholeMember);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TryRenderTargetMember_DeclinesAdjacentExplicitInterfaceShapes()
    {
        var assemblyPath = CompileFixture("""
            public interface IGeneric<T>
            {
                T Echo(T value);
            }

            public sealed class GenericExplicit : IGeneric<int>
            {
                int IGeneric<int>.Echo(int value) => value;
            }

            public interface IStatic
            {
                static abstract int Parse(string value);
            }

            public sealed class StaticExplicit : IStatic
            {
                static int IStatic.Parse(string value) => value.Length;
            }

            public static class Outer
            {
                public interface INested
                {
                    void Ping();
                }
            }

            public sealed class NestedExplicit : Outer.INested
            {
                void Outer.INested.Ping() { }
            }

            public sealed class ExternalExplicit : System.IDisposable
            {
                void System.IDisposable.Dispose() { }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);

            foreach (string typeName in new[]
                {
                    "GenericExplicit",
                    "StaticExplicit",
                    "NestedExplicit",
                    "ExternalExplicit",
                })
            {
                var type = reader.GetTypeDefinition(Assert.Single(
                    reader.TypeDefinitions,
                    handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName));
                var method = Assert.Single(
                    type.GetMethods(),
                    handle => reader.GetString(reader.GetMethodDefinition(handle).Name) != ".ctor");

                Assert.Null(FidelityCheck.TryRenderTargetMember(
                    pe,
                    source,
                    method,
                    targeted: true,
                    isPrimaryConstructor: false));
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
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
    [Trait("Speed", "Slow")]
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
    public void RunMethodDelta_UsesCorpusMetadataForPlatformOutParameters()
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
            int exitCode = FidelityCheck.RunMethodDelta([assemblyPath], deltaPath, maxExamples: 5);
            Console.SetOut(originalOut);
            var output = writer.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains($"exact (contract v{FidelityCheck.CurrentContractVersion}): 1", output);
            Assert.DoesNotContain("CS1620", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteFixture(assemblyPath);
        }
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

    static string CompileFixture(string source, bool allowUnsafe = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.dll");
        CompileAssembly(path, source, additionalReferences: null, allowUnsafe);
        return path;
    }

    static void CompileAssembly(
        string path,
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null,
        bool allowUnsafe = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var references = RoslynTestReferences.TrustedPlatform.AsEnumerable();
        if (additionalReferences is not null)
            references = references.Concat(additionalReferences);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                outputKind,
                allowUnsafe: allowUnsafe,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("fidelity-generated-filter-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
