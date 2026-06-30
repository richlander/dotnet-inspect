using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class TypeSourceComposerUnionTests
{
    [Fact]
    public void LoweredUnionDeclaration_RendersUnionHeaderAndHidesBasicPattern()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;

                public interface IUnion
                {
                    object? Value { get; }
                }
            }

            namespace Other
            {
                public sealed class Bird { }
            }

            namespace UnionFixtures
            {
                using System;
                using System.Collections.Generic;

                public sealed class Cat { }
                public sealed class Dog { }

                [System.Runtime.CompilerServices.Union]
                public readonly struct Pet : System.Runtime.CompilerServices.IUnion, IDisposable
                {
                    public Pet(Cat value) => Value = value;
                    public Pet(Dog value) => Value = value;
                    public Pet(List<Other.Bird> value) => Value = value;

                    public object? Value { get; }

                    public void Dispose() { }
                    public string Describe() => Value?.ToString() ?? "";
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.Pet");

        Assert.Contains("using Other;", source);
        Assert.Contains("public readonly union Pet(Cat, Dog, List<Bird>) : IDisposable", source);
        Assert.DoesNotContain("[Union", source);
        Assert.DoesNotContain("public struct Pet", source);
        Assert.DoesNotContain("IUnion", source);
        Assert.DoesNotContain("public Pet(Cat value)", source);
        Assert.DoesNotContain("public Pet(Dog value)", source);
        Assert.DoesNotContain("public Pet(List<Bird> value)", source);
        Assert.DoesNotContain("public object", source);
        Assert.Contains("Dispose", source);
        Assert.Contains("Describe", source);
    }

    [Fact]
    public void UnionAttributeWithoutIUnion_StaysLowered()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }

                [System.Runtime.CompilerServices.Union]
                public struct NotUnion
                {
                    public NotUnion(Cat value) => Value = value;
                    public object? Value { get; }
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.NotUnion");

        Assert.Contains("[Union]", source);
        Assert.Contains("public struct NotUnion", source);
        Assert.DoesNotContain("public union NotUnion", source);
    }

    static string ComposeType(string path, string fullName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(surface.Types, t => t.FullName == fullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);
        return source!;
    }

    static TempAssembly Compile(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-union-{Guid.NewGuid():N}.dll");
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return new TempAssembly(path);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            references.Add(MetadataReference.CreateFromFile(path));
        }
        return references.ToImmutable();
    }

    sealed class TempAssembly(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch { }
        }
    }
}
