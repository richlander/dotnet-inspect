using System.Text.Json;
using ILInspector.Analysis;
using Xunit;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/platform-caller-unsafe-contracts.md that read only the
// committed projection.
public class PlatformCallerUnsafeContractsTests
{
    internal static TypeRef PlatformType(string assembly, string ns, string name, bool trusted = true)
        => TypeRef.Definition(assembly, ns, name, trustedFrameworkAssembly: trusted);

    // Unsafe.As<T>(object), a projected member.
    internal static MemberRef UnsafeAsObject(TypeRef declaring)
        => new(
            declaring,
            "As",
            [TypeRef.CoreLib("System", "Object")],
            TypeRef.MethodGenericParameter(0),
            MemberKind.Method)
        {
            OpenParameterTypes = [TypeRef.CoreLib("System", "Object")],
            OpenReturnType = TypeRef.MethodGenericParameter(0),
            GenericArity = 1,
        };

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("netstandard")]
    [InlineData("System.Memory")]
    public void PlatformContractMatchesAcrossForwarding(string assembly)
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;
        Assert.True(platform.Contains(UnsafeAsObject(
            PlatformType(assembly, "System.Runtime.CompilerServices", "Unsafe"))));

        // MemoryMarshal.GetReference<T>(Span<T>)
        TypeRef span = PlatformType(assembly, "System", "Span`1");
        TypeRef openSpan = TypeRef.GenericInstance(span, [TypeRef.MethodGenericParameter(0)]);
        Assert.True(platform.Contains(new MemberRef(
            PlatformType(assembly, "System.Runtime.InteropServices", "MemoryMarshal"),
            "GetReference",
            [openSpan],
            TypeRef.ByRef(TypeRef.MethodGenericParameter(0)),
            MemberKind.Method)
        {
            OpenParameterTypes = [openSpan],
            GenericArity = 1,
        }));
    }

    [Fact]
    public void PlatformContractMatchesGenericDefinitions()
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;

        // A MethodSpec instantiation keeps its open definition signature.
        MemberRef instantiated = UnsafeAsObject(
            PlatformType("System.Runtime", "System.Runtime.CompilerServices", "Unsafe")) with
        {
            TypeArguments = [TypeRef.CoreLib("System", "String")],
            ReturnType = TypeRef.CoreLib("System", "String"),
        };
        Assert.True(platform.Contains(instantiated));

        // new ReadOnlySpan<byte>(void*, int) on a constructed declaring type.
        TypeRef readOnlySpan = PlatformType("System.Runtime", "System", "ReadOnlySpan`1");
        TypeRef voidPointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Void"));
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        Assert.True(platform.Contains(new MemberRef(
            TypeRef.GenericInstance(readOnlySpan, [TypeRef.CoreLib("System", "Byte")]),
            ".ctor",
            [voidPointer, int32],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Constructor)
        {
            OpenParameterTypes = [voidPointer, int32],
            HasThis = true,
        }));
    }

    [Fact]
    public void NonPlatformLookalikeDoesNotMatch()
    {
        Assert.False(PlatformCallerUnsafeContracts.Embedded.Contains(UnsafeAsObject(
            PlatformType("System.Runtime", "System.Runtime.CompilerServices", "Unsafe", trusted: false))));
    }

    [Fact]
    public void EmbeddedLoad_ReadsTheVerifiedProjection()
    {
        PlatformCallerUnsafeContracts embedded = PlatformCallerUnsafeContracts.Embedded;
        PlatformCallerUnsafeContracts verified =
            PlatformCallerUnsafeContracts.Parse(ReadEmbeddedProjection());

        Assert.Equal(verified.Source, embedded.Source);
        Assert.Equal(verified.Digest, embedded.Digest);
        Assert.True(verified.Identifiers.SetEquals(embedded.Identifiers));
    }

    [Fact]
    public void ProjectionMatchesItsHeader()
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;
        Assert.Equal("Microsoft.NETCore.App.Ref", platform.Source.PackId);
        Assert.True(platform.Count > 0);

        string text = ReadEmbeddedProjection();
        Assert.Equal(platform.Count, PlatformCallerUnsafeContracts.Parse(text).Count);
        string tampered = text.Replace(
            "M:System.Runtime.CompilerServices.Unsafe.As``1(System.Object)",
            "M:System.Runtime.CompilerServices.Unsafe.As``1(System.String)",
            StringComparison.Ordinal);
        Assert.NotEqual(text, tampered);
        Assert.Throws<InvalidDataException>(() => PlatformCallerUnsafeContracts.Parse(tampered));
    }

    [Fact]
    public void ProjectionTracksPinnedSdk()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "global.json")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("global.json not found.");
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        string sdk = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;

        // SDK feature band X.Y.Z00 carries runtime X.Y.Z with Z = patch % 100.
        int dash = sdk.IndexOf('-');
        string core = dash < 0 ? sdk : sdk[..dash];
        string[] parts = core.Split('.');
        string runtime = $"{parts[0]}.{parts[1]}.{int.Parse(parts[2]) % 100}" + (dash < 0 ? "" : sdk[dash..]);

        Assert.Equal(runtime, PlatformCallerUnsafeContracts.Embedded.Source.PackVersion);
    }

    static string ReadEmbeddedProjection()
    {
        using Stream stream = typeof(PlatformCallerUnsafeContracts).Assembly
            .GetManifestResourceStream(PlatformCallerUnsafeContracts.ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
