using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Obsolete status fact excludes Roslyn's compiler-compatibility marker
/// (docs/design/api-status-analysis.md#status-facts). Each image is compiled
/// by Roslyn, so the shapes are the compiler's own emission: on a corlib
/// without <c>CompilerFeatureRequiredAttribute</c> (netstandard2.0, .NET
/// Framework) a ref struct carries the marker without a feature guard.
/// </summary>
public sealed class CompilerCompatibilityObsoleteTests
{
    const string RefStructsMarker =
        "Types with embedded references are not supported in this version of your compiler.";
    const string RequiredMembersMarker =
        "Constructors of types with required members are not supported in this version of your compiler.";

    const string RefStructSource = $$"""
        namespace N
        {
            public ref struct Plain { public int X; }

            [System.Obsolete("Use something else.")]
            public ref struct UserObsolete { public int X; }

            [System.Obsolete("{{RefStructsMarker}}")]
            public ref struct UserMarkerMessageWithoutError { public int X; }

            [System.Obsolete("{{RefStructsMarker}}", true)]
            public class NotRefStruct { }
        }
        """;

    /// <summary>
    /// A corlib with only what the ref-struct source needs, and no
    /// <c>CompilerFeatureRequiredAttribute</c> or <c>IsByRefLikeAttribute</c>,
    /// as on netstandard2.0.
    /// </summary>
    const string MinimalCorlibSource = """
        namespace System
        {
            public class Object { }
            public abstract class ValueType { }
            public abstract class Enum : ValueType { }
            public struct Void { }
            public struct Boolean { }
            public struct Byte { }
            public struct Int16 { }
            public struct Int32 { }
            public struct Int64 { }
            public struct IntPtr { }
            public sealed class String { }
            public abstract class Array { }
            public abstract class Delegate { }
            public abstract class MulticastDelegate : Delegate { }
            public class Type { }
            public struct RuntimeTypeHandle { }
            public struct RuntimeFieldHandle { }
            public struct RuntimeMethodHandle { }
            public abstract class Attribute { }
            public enum AttributeTargets { All = 32767 }
            public sealed class AttributeUsageAttribute : Attribute
            {
                public AttributeUsageAttribute(AttributeTargets validOn) { }
                public bool AllowMultiple { get; set; }
                public bool Inherited { get; set; }
            }
            public sealed class ObsoleteAttribute : Attribute
            {
                public ObsoleteAttribute() { }
                public ObsoleteAttribute(string message) { }
                public ObsoleteAttribute(string message, bool error) { }
            }
        }
        """;

    [Fact]
    public void RefStructMarkerWithoutFeatureGuard_IsNotObsolete()
    {
        byte[] image = CompileAgainstMinimalCorlib(RefStructSource);

        // Roslyn's emission on a target without CompilerFeatureRequired: the
        // marker and [IsByRefLike] (embedded), no feature guard.
        AssertMarkerShape(image, "Plain", RefStructsMarker, expectFeatureGuard: false);

        AssertObsolete(image, "Plain", expected: false);
        AssertObsolete(image, "UserObsolete", expected: true);
        AssertObsolete(image, "UserMarkerMessageWithoutError", expected: true);
        AssertObsolete(image, "NotRefStruct", expected: true);
    }

    [Fact]
    public void RefStructMarkerWithFeatureGuard_IsNotObsolete()
    {
        byte[] image = CompileAgainstRuntime(RefStructSource);

        AssertMarkerShape(image, "Plain", RefStructsMarker, expectFeatureGuard: true);

        AssertObsolete(image, "Plain", expected: false);
        AssertObsolete(image, "UserObsolete", expected: true);
        AssertObsolete(image, "UserMarkerMessageWithoutError", expected: true);
        AssertObsolete(image, "NotRefStruct", expected: true);
    }

    [Fact]
    public void RequiredMembersConstructorMarker_IsNotObsoleteAndAlwaysHasItsFeatureGuard()
    {
        const string source = """
            namespace N
            {
                public class Host
                {
                    public required int A { get; set; }
                    public Host() { }
                    [System.Obsolete("Use the other one.")]
                    public Host(int a) { A = a; }
                }
            }
            """;
        byte[] image = CompileAgainstRuntime(source);

        using var pe = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition host = FindType(reader, "Host");
        MethodDefinition[] constructors = [.. host.GetMethods()
            .Select(reader.GetMethodDefinition)
            .Where(method => reader.GetString(method.Name) == ".ctor")];
        MethodDefinition parameterless = Assert.Single(constructors,
            method => ParameterCount(reader, method) == 0);
        MethodDefinition userObsolete = Assert.Single(constructors,
            method => ParameterCount(reader, method) == 1);

        Assert.Equal(
            [RequiredMembersMarker, "RequiredMembers"],
            MarkerArguments(reader, parameterless.GetCustomAttributes()));
        Assert.False(AttributeReader.TryGetObsoleteAttribute(
            reader, parameterless.GetCustomAttributes(), out _));
        Assert.False(AttributeReader.HasHiddenAttribute(
            reader, parameterless.GetCustomAttributes()));

        Assert.True(AttributeReader.TryGetObsoleteAttribute(
            reader, userObsolete.GetCustomAttributes(), out string? message));
        Assert.Equal("Use the other one.", message);
    }

    [Fact]
    public void RequiredMembersWithoutFeatureGuardType_DoNotCompile()
    {
        // Roslyn requires CompilerFeatureRequiredAttribute for required
        // members, so a required-members constructor marker never lacks its
        // feature guard; only the ref-struct marker does.
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RequiredWithoutGuard",
            [CSharpSyntaxTree.ParseText("""
                namespace System.Runtime.CompilerServices
                {
                    public sealed class RequiredMemberAttribute : System.Attribute { }
                    public sealed class IsExternalInit { }
                }
                namespace System.Diagnostics.CodeAnalysis
                {
                    public sealed class SetsRequiredMembersAttribute : System.Attribute { }
                }
                namespace N { public class Host { public required int A; } }
                """, cancellationToken: TestContext.Current.CancellationToken)],
            [MinimalCorlib()],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Contains(compilation.GetDiagnostics(TestContext.Current.CancellationToken), diagnostic =>
            diagnostic.Id == "CS0656"
            && diagnostic.GetMessage().Contains("CompilerFeatureRequiredAttribute", StringComparison.Ordinal));
    }

    static void AssertObsolete(byte[] image, string typeName, bool expected)
    {
        using (var pe = new PEReader(ImmutableArray.Create(image)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            CustomAttributeHandleCollection attributes = FindType(reader, typeName).GetCustomAttributes();
            Assert.Equal(expected, AttributeReader.TryGetObsoleteAttribute(reader, attributes, out _));
            Assert.Equal(expected, AttributeReader.HasHiddenAttribute(reader, attributes));
            Assert.Equal(expected, AttributeReader.HasHiddenAttribute(reader, attributes, static _ => { }));

            ApiSurface publicSurface = ApiSurfaceExtractor.Extract(pe, ApiSurfaceExtractionScope.Public);
            Assert.Equal(!expected, publicSurface.Types.Any(type => type.Name == typeName));
        }

        using (var pe = new PEReader(ImmutableArray.Create(image)))
        {
            ApiSurface axis = ApiSurfaceExtractor.Extract(pe, ApiSurfaceExtractionScope.ApiAxis);
            ApiType type = Assert.Single(axis.Types, type => type.Name == typeName);
            Assert.Equal(expected, type.IsObsolete);
        }

        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
            session.TypeDeclarations()).Inventory;
        AssemblyTypeDeclaration declaration = Assert.Single(inventory.Declarations,
            declaration => declaration.Name.Segments[^1] == typeName);
        Assert.Equal(expected, Assert.IsType<TypeDeclarationDiscoveryAttributes>(
            declaration.DiscoveryAttributes).IsObsolete);
    }

    static void AssertMarkerShape(byte[] image, string typeName, string marker, bool expectFeatureGuard)
    {
        using var pe = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = pe.GetMetadataReader();
        CustomAttributeHandleCollection attributes = FindType(reader, typeName).GetCustomAttributes();
        string?[] names = [.. attributes.Select(handle =>
            AttributeReader.GetAttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor))];

        Assert.Contains(KnownAttributeNames.IsByRefLikeAttribute, names);
        Assert.Equal(expectFeatureGuard, names.Contains(KnownAttributeNames.CompilerFeatureRequiredAttribute));
        CustomAttribute obsolete = reader.GetCustomAttribute(Assert.Single(attributes, handle =>
            AttributeReader.GetAttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor)
                == "System.ObsoleteAttribute"));
        BlobReader value = reader.GetBlobReader(obsolete.Value);
        Assert.Equal(1, value.ReadUInt16());
        Assert.Equal(marker, value.ReadSerializedString());
        Assert.True(value.ReadBoolean());
    }

    static string?[] MarkerArguments(MetadataReader reader, CustomAttributeHandleCollection attributes) =>
        [.. attributes
            .Select(reader.GetCustomAttribute)
            .Where(attribute => AttributeReader.GetAttributeTypeName(reader, attribute.Constructor)
                is "System.ObsoleteAttribute" or KnownAttributeNames.CompilerFeatureRequiredAttribute)
            .Select(attribute =>
            {
                BlobReader value = reader.GetBlobReader(attribute.Value);
                value.ReadUInt16();
                return value.ReadSerializedString();
            })
            .Order(StringComparer.Ordinal)];

    static int ParameterCount(MetadataReader reader, MethodDefinition method)
    {
        BlobReader signature = reader.GetBlobReader(method.Signature);
        signature.ReadSignatureHeader();
        return signature.ReadCompressedInteger();
    }

    static TypeDefinition FindType(MetadataReader reader, string name) =>
        reader.GetTypeDefinition(Assert.Single(reader.TypeDefinitions, handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == name));

    static byte[] CompileAgainstMinimalCorlib(string source) =>
        Emit("CompatibilityWithoutGuard", source, [MinimalCorlib()]);

    static byte[] CompileAgainstRuntime(string source) =>
        Emit(
            "CompatibilityWithGuard",
            source,
            [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
                .Select(path => MetadataReference.CreateFromFile(path))]);

    static MetadataReference MinimalCorlib() =>
        MetadataReference.CreateFromImage(Emit("mscorlib", MinimalCorlibSource, []));

    static byte[] Emit(string name, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    static ResolvedAssemblyReference Descriptor(byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path: null,
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local("compatibility-fixture"));
    }
}
