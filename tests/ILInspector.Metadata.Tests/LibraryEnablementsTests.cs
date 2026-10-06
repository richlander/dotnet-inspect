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
    [InlineData("System.Net.Sockets.dll", Expected.Enabled)]
    [InlineData("System.Net.Http.dll", Expected.Enabled)]
    [InlineData("System.Text.Json.dll", Expected.NotEnabled)]
    public void RuntimePack_RuntimeAsyncFollowsAsyncMethodDefinitions(
        string fileName,
        Expected expected)
    {
        LibraryEnablementFacts enablements = Read(Pinned("runtime", fileName));

        Assert.Equal(expected, State(enablements, LibraryEnablementId.RuntimeAsync));
        Assert.Equal(Expected.Enabled, State(enablements, LibraryEnablementId.AotCompatible));
    }

    [Fact]
    public void RuntimePack_UnmarkedImplementationIsNotMemorySafetyV2()
    {
        LibraryEnablementFacts enablements = Read(Pinned("runtime", "System.Security.Cryptography.dll"));

        Assert.Equal(Expected.NotEnabled, State(enablements, LibraryEnablementId.MemorySafetyV2));
    }

    [Theory]
    [InlineData("System.Net.Sockets.dll")]
    [InlineData("System.Security.Cryptography.dll")]
    public void ReferencePack_ReportsEveryEnablementUnavailable(string fileName)
    {
        // Both images carry IsAotCompatible, and System.Security.Cryptography
        // carries MemorySafetyRules(2); a reference surface still cannot say
        // how the implementation was built.
        LibraryEnablementFacts enablements = Read(Pinned("ref", fileName));

        Assert.Equal(3, enablements.Items.Length);
        Assert.All(
            enablements.Items,
            item =>
            {
                Assert.Equal(Expected.Unavailable, Classify(item));
                Assert.Equal(LibraryEnablementUnavailableReason.ReferenceAssembly, ReasonOf(item));
            });
        Assert.Empty(enablements.Enabled());
    }

    [Fact]
    public void UpdatedMemorySafetyFixture_IsMemorySafetyV2Enabled()
    {
        LibraryEnablementFacts enablements = Read(FixtureCatalog.MetadataMemorySafety.AssemblyPath());

        Assert.Equal(Expected.Enabled, State(enablements, LibraryEnablementId.MemorySafetyV2));
    }

    [Theory]
    [InlineData(new int[] { 1 }, LibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules)]
    [InlineData(new int[] { 3 }, LibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules)]
    [InlineData(new int[] { 2, 1 }, LibraryEnablementUnavailableReason.ConflictingMemorySafetyRules)]
    [InlineData(null, LibraryEnablementUnavailableReason.MalformedMemorySafetyRules)]
    public void NonUpdatedMemorySafetyMarkers_AreUnavailableWithTheOwnersState(
        int[]? versions,
        LibraryEnablementUnavailableReason reason)
    {
        int?[] markers = versions is null ? [null] : [.. versions.Select(static v => (int?)v)];
        LibraryEnablement memorySafety = Single(
            Read(MemorySafetyMetadataIndexTests.BuildSyntheticImage(markers)),
            LibraryEnablementId.MemorySafetyV2);

        Assert.Equal(Expected.Unavailable, Classify(memorySafety));
        Assert.Equal(reason, ReasonOf(memorySafety));
    }

    [Fact]
    public void CompilerGeneratedInternalAsyncMethod_EnablesRuntimeAsync()
    {
        string path = FixtureCatalog.MetadataEnablements.AssemblyPath();
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);

        // The public-surface classification cannot see the only async method.
        Assert.False(session.PresenceFlags().HasRuntimeAsync);
        Assert.Equal(
            Expected.Enabled,
            State(session.Enablements(), LibraryEnablementId.RuntimeAsync));
    }

    [Theory]
    [InlineData(new string[0], Expected.NotEnabled, null)]
    [InlineData(new[] { "True" }, Expected.Enabled, null)]
    [InlineData(new[] { " true " }, Expected.Enabled, null)]
    [InlineData(new[] { "False" }, Expected.NotEnabled, null)]
    [InlineData(new[] { "True", "True" }, Expected.Enabled, null)]
    [InlineData(new[] { "True", "False" }, Expected.Unavailable, LibraryEnablementUnavailableReason.ConflictingValues)]
    [InlineData(new[] { "yes" }, Expected.Unavailable, LibraryEnablementUnavailableReason.UnrecognizedValue)]
    [InlineData(new[] { "True", "False", "yes" }, Expected.Unavailable, LibraryEnablementUnavailableReason.UnrecognizedValue)]
    public void AotCompatibleMarker_DecidesStateAndReason(
        string[] values,
        Expected state,
        LibraryEnablementUnavailableReason? reason)
    {
        string source = string.Concat(
            values.Select(value =>
                $"[assembly: System.Reflection.AssemblyMetadata(\"IsAotCompatible\", \"{value}\")]\n"));
        LibraryEnablement aot = Single(Read(Compile(source)), LibraryEnablementId.AotCompatible);

        Assert.Equal(state, Classify(aot));
        Assert.Equal(reason, ReasonOf(aot));
    }

    [Fact]
    public void UndecodableAssemblyMetadataBlob_MakesAotUnavailable()
    {
        LibraryEnablement aot = Single(
            Read(BuildImageWithAssemblyMetadataBlob([0x01, 0x00, 0x05, 0x41])),
            LibraryEnablementId.AotCompatible);

        Assert.Equal(Expected.Unavailable, Classify(aot));
        Assert.Equal(LibraryEnablementUnavailableReason.UndecodableMetadata, ReasonOf(aot));
    }

    [Fact]
    public void UnnameableAssemblyAttribute_MakesEveryEnablementUndecodable()
    {
        // The unnameable attribute might be ReferenceAssemblyAttribute, so the
        // reference rule cannot be decided.
        LibraryEnablementFacts enablements = Read(
            BuildImageWithAssemblyMetadataBlob(StringPairBlob("IsAotCompatible", "True"), unnameableConstructor: true));

        Assert.Equal(3, enablements.Items.Length);
        Assert.All(
            enablements.Items,
            item => Assert.Equal(
                LibraryEnablementUnavailableReason.UndecodableMetadata,
                Assert.IsType<LibraryEnablement.Unavailable>(item).Reason));
    }

    private static byte[] StringPairBlob(string key, string value)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(0x0001);
        blob.WriteSerializedString(key);
        blob.WriteSerializedString(value);
        blob.WriteUInt16(0);
        return blob.ToArray();
    }

    private static string Pinned(string pack, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", pack, fileName);

    private static LibraryEnablementFacts Read(string path)
    {
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);
        return session.Enablements();
    }

    private static LibraryEnablementFacts Read(byte[] image)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(new MemoryStream(image, writable: false));
        return session.Enablements();
    }

    private static LibraryEnablement Single(LibraryEnablementFacts enablements, LibraryEnablementId id) =>
        Assert.Single(enablements.Items, item => item.Id == id);

    private static Expected State(LibraryEnablementFacts enablements, LibraryEnablementId id) =>
        Classify(Single(enablements, id));

    public enum Expected
    {
        Enabled,
        NotEnabled,
        Unavailable,
    }

    private static Expected Classify(LibraryEnablement item) => item switch
    {
        LibraryEnablement.Enabled => Expected.Enabled,
        LibraryEnablement.NotEnabled => Expected.NotEnabled,
        LibraryEnablement.Unavailable => Expected.Unavailable,
        _ => throw new InvalidOperationException("Unknown enablement case."),
    };

    private static LibraryEnablementUnavailableReason? ReasonOf(LibraryEnablement item) =>
        (item as LibraryEnablement.Unavailable)?.Reason;

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
    private static byte[] BuildImageWithAssemblyMetadataBlob(byte[] valueBlob, bool unnameableConstructor = false)
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
        if (unnameableConstructor)
        {
            // A MemberRef constructor whose TypeRef parent is out of range.
            metadata.AddCustomAttribute(
                assembly,
                metadata.AddMemberReference(
                    MetadataTokens.TypeReferenceHandle(5000),
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(signature)),
                metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }
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
