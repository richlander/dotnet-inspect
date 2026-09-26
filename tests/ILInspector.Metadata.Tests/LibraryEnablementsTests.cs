using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates for docs/design/library-enablements.md over pinned
/// Microsoft.NETCore.App 11.0.0-rc.1.26425.128 assets and fixtures.
/// </summary>
public sealed class LibraryEnablementsTests
{
    [Theory]
    [InlineData("System.Net.Sockets.dll", LibraryEnablementState.Enabled)]
    [InlineData("System.Net.Http.dll", LibraryEnablementState.Enabled)]
    [InlineData("System.Text.Json.dll", LibraryEnablementState.NotEnabled)]
    public void RuntimePack_RuntimeAsyncFollowsAsyncMethodDefinitions(
        string fileName,
        LibraryEnablementState expected)
    {
        LibraryEnablements enablements = Read(Pinned("runtime", fileName));

        Assert.Equal(expected, State(enablements, LibraryEnablementKind.RuntimeAsync));
        Assert.Equal(LibraryEnablementState.Enabled, State(enablements, LibraryEnablementKind.AotCompatible));
    }

    [Fact]
    public void RuntimePack_UnmarkedImplementationIsNotMemorySafetyV2()
    {
        LibraryEnablements enablements = Read(Pinned("runtime", "System.Security.Cryptography.dll"));

        Assert.Equal(LibraryEnablementState.NotEnabled, State(enablements, LibraryEnablementKind.MemorySafetyV2));
    }

    [Theory]
    [InlineData("System.Net.Sockets.dll")]
    [InlineData("System.Security.Cryptography.dll")]
    public void ReferencePack_ReportsEveryEnablementUnavailable(string fileName)
    {
        // Both images carry IsAotCompatible, and System.Security.Cryptography
        // carries MemorySafetyRules(2); a reference surface still cannot say
        // how the implementation was built.
        LibraryEnablements enablements = Read(Pinned("ref", fileName));

        Assert.Equal(3, enablements.Items.Length);
        Assert.All(
            enablements.Items,
            item =>
            {
                Assert.Equal(LibraryEnablementState.Unavailable, item.State);
                Assert.Equal(LibraryEnablementUnavailableReason.ReferenceAssembly, item.Reason);
            });
        Assert.Empty(enablements.Enabled());
    }

    [Fact]
    public void UpdatedMemorySafetyFixture_IsMemorySafetyV2Enabled()
    {
        LibraryEnablements enablements = Read(FixtureCatalog.MetadataMemorySafety.AssemblyPath());

        Assert.Equal(LibraryEnablementState.Enabled, State(enablements, LibraryEnablementKind.MemorySafetyV2));
    }

    [Fact]
    public void CompilerGeneratedInternalAsyncMethod_EnablesRuntimeAsync()
    {
        string path = FixtureCatalog.MetadataEnablements.AssemblyPath();
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);

        // The public-surface classification cannot see the only async method.
        Assert.False(session.PresenceFlags().HasRuntimeAsync);
        Assert.Equal(
            LibraryEnablementState.Enabled,
            State(session.Enablements(), LibraryEnablementKind.RuntimeAsync));
    }

    [Theory]
    [InlineData(new string[0], LibraryEnablementState.NotEnabled, null)]
    [InlineData(new[] { "True" }, LibraryEnablementState.Enabled, null)]
    [InlineData(new[] { " true " }, LibraryEnablementState.Enabled, null)]
    [InlineData(new[] { "False" }, LibraryEnablementState.NotEnabled, null)]
    [InlineData(new[] { "True", "True" }, LibraryEnablementState.Enabled, null)]
    [InlineData(new[] { "True", "False" }, LibraryEnablementState.Unavailable, LibraryEnablementUnavailableReason.ConflictingValues)]
    [InlineData(new[] { "yes" }, LibraryEnablementState.Unavailable, LibraryEnablementUnavailableReason.UnrecognizedValue)]
    [InlineData(new[] { "True", "False", "yes" }, LibraryEnablementState.Unavailable, LibraryEnablementUnavailableReason.UnrecognizedValue)]
    public void AotCompatibleMarker_DecidesStateAndReason(
        string[] values,
        LibraryEnablementState state,
        LibraryEnablementUnavailableReason? reason)
    {
        string source = string.Concat(
            values.Select(value =>
                $"[assembly: System.Reflection.AssemblyMetadata(\"IsAotCompatible\", \"{value}\")]\n"));
        LibraryEnablement aot = Single(Read(Compile(source)), LibraryEnablementKind.AotCompatible);

        Assert.Equal(state, aot.State);
        Assert.Equal(reason, aot.Reason);
    }

    [Fact]
    public void UndecodableAssemblyMetadataBlob_MakesAotUnavailable()
    {
        LibraryEnablement aot = Single(
            Read(BuildImageWithAssemblyMetadataBlob([0x01, 0x00, 0x05, 0x41])),
            LibraryEnablementKind.AotCompatible);

        Assert.Equal(LibraryEnablementState.Unavailable, aot.State);
        Assert.Equal(LibraryEnablementUnavailableReason.UndecodableMetadata, aot.Reason);
    }

    private static string Pinned(string pack, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", pack, fileName);

    private static LibraryEnablements Read(string path)
    {
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);
        return session.Enablements();
    }

    private static LibraryEnablements Read(byte[] image)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(new MemoryStream(image, writable: false));
        return session.Enablements();
    }

    private static LibraryEnablement Single(LibraryEnablements enablements, LibraryEnablementKind kind) =>
        Assert.Single(enablements.Items, item => item.Kind == kind);

    private static LibraryEnablementState State(LibraryEnablements enablements, LibraryEnablementKind kind) =>
        Single(enablements, kind).State;

    private static byte[] Compile(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "EnablementMatrix",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result =
            compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    /// <summary>
    /// Builds a minimal assembly whose one AssemblyMetadataAttribute row has
    /// the given (possibly malformed) value blob. C# cannot emit a malformed
    /// blob, so this input is constructed directly.
    /// </summary>
    private static byte[] BuildImageWithAssemblyMetadataBlob(byte[] valueBlob)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Malformed.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("Malformed"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Reflection"),
            metadata.GetOrAddString("AssemblyMetadataAttribute"));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().String();
                    parameters.AddParameter().Type().String();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));
        metadata.AddCustomAttribute(assembly, constructor, metadata.GetOrAddBlob(valueBlob));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
                new MetadataRootBuilder(metadata),
                new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}
