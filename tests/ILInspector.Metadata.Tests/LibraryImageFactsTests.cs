using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates for the Image and Description observations behind
/// docs/design/library-inspection-document.md#library-facts.
/// </summary>
public sealed class LibraryImageFactsTests
{
    [Fact]
    public void RuntimePackSockets_ReportsItsImageAndDescription()
    {
        AssemblyLibraryFactsObservation facts = Read(Pinned("runtime", "System.Net.Sockets.dll"));

        Assert.Equal(LibraryCompilationForm.ReadyToRun, facts.Compilation);
        Assert.True(facts.StrongNameSigned);
        Assert.Equal(LibraryReproducibility.Reproducible, facts.Reproducibility);
        Assert.Equal(Present(".NETCoreApp,Version=v11.0"), facts.TargetFramework);
        Assert.Equal(Present("Microsoft Corporation"), facts.Company);
        Assert.Equal(Present("Microsoft® .NET"), facts.Product);
        // OS-specific ReadyToRun images carry machine values the vocabulary does not name.
        Assert.Null(facts.Architecture);
    }

    [Fact]
    public void ReferencePackSockets_IsIL()
    {
        Assert.Equal(
            LibraryCompilationForm.IL,
            Read(Pinned("ref", "System.Net.Sockets.dll")).Compilation);
    }

    [Fact]
    public void MissingAttributes_AreAbsentNotEmpty()
    {
        AssemblyLibraryFactsObservation facts = Read(Compile(
            "[assembly: System.Reflection.AssemblyCompany(\"Contoso\")]"));

        Assert.Equal(Present("Contoso"), facts.Company);
        Assert.Null(facts.Product);
        Assert.Null(facts.Copyright);
        Assert.Null(facts.InformationalVersion);
        Assert.Equal(LibraryArchitecture.AnyCpu, facts.Architecture);
        Assert.False(facts.StrongNameSigned);
        Assert.Equal(LibraryCompilationForm.IL, facts.Compilation);
    }

    [Theory]
    [InlineData(Platform.AnyCpu, LibraryArchitecture.AnyCpu)]
    [InlineData(Platform.AnyCpu32BitPreferred, LibraryArchitecture.AnyCpuPrefers32Bit)]
    [InlineData(Platform.X86, LibraryArchitecture.X86)]
    [InlineData(Platform.X64, LibraryArchitecture.X64)]
    [InlineData(Platform.Arm64, LibraryArchitecture.Arm64)]
    public void CompilerPlatform_MapsToArchitecture(Platform platform, LibraryArchitecture expected)
    {
        // 32-bit-preferred requires an executable; Roslyn sets both
        // Requires32Bit and Prefers32Bit for it.
        AssemblyLibraryFactsObservation facts = Read(Compile(
            "static class Program { static void Main() { } }",
            OutputKind.ConsoleApplication,
            platform));

        Assert.Equal(expected, facts.Architecture);
    }

    [Fact]
    public void UndecodableAndConflictingText_AreUnavailableWithoutAffectingOtherFacts()
    {
        AssemblyLibraryFactsObservation facts = Read(BuildImage(
            ("System.Reflection", "AssemblyCompanyAttribute", [0x01, 0x00, 0x05, 0x41]),
            ("System.Reflection", "AssemblyProductAttribute", StringBlob("One")),
            ("System.Reflection", "AssemblyProductAttribute", StringBlob("Two")),
            ("System.Reflection", "AssemblyCopyrightAttribute", StringBlob("Same")),
            ("System.Reflection", "AssemblyCopyrightAttribute", StringBlob("Same"))));

        Assert.Equal(new AssemblyAttributeText(AssemblyAttributeTextState.Undecodable, null), facts.Company);
        Assert.Equal(new AssemblyAttributeText(AssemblyAttributeTextState.Conflicting, null), facts.Product);
        Assert.Equal(Present("Same"), facts.Copyright);
        Assert.Null(facts.TargetFramework);
        Assert.Equal(LibraryCompilationForm.IL, facts.Compilation);
    }

    private static AssemblyAttributeText Present(string value) =>
        new(AssemblyAttributeTextState.Present, value);

    private static string Pinned(string pack, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", pack, fileName);

    private static AssemblyLibraryFactsObservation Read(string path)
    {
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);
        return session.LibraryFacts();
    }

    private static AssemblyLibraryFactsObservation Read(byte[] image)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(new MemoryStream(image, writable: false));
        return session.LibraryFacts();
    }

    private static byte[] Compile(
        string source,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary,
        Platform platform = Platform.AnyCpu)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "FactsMatrix",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(kind, platform: platform));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result =
            compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    private static byte[] StringBlob(string value)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(0x0001);
        blob.WriteSerializedString(value);
        blob.WriteUInt16(0);
        return blob.ToArray();
    }

    /// <summary>
    /// Builds a minimal assembly whose assembly-level string attributes carry
    /// the given (possibly malformed or repeated) value blobs. C# cannot emit
    /// these shapes, so the input is constructed directly.
    /// </summary>
    private static byte[] BuildImage(
        params (string Namespace, string Name, byte[] Blob)[] attributes)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Facts.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("Facts"),
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
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        var constructors = new Dictionary<string, MemberReferenceHandle>(StringComparer.Ordinal);
        foreach ((string @namespace, string name, byte[] value) in attributes)
        {
            if (!constructors.TryGetValue(name, out MemberReferenceHandle constructor))
            {
                TypeReferenceHandle type = metadata.AddTypeReference(
                    runtime,
                    metadata.GetOrAddString(@namespace),
                    metadata.GetOrAddString(name));
                constructor = metadata.AddMemberReference(
                    type,
                    metadata.GetOrAddString(".ctor"),
                    signatureHandle);
                constructors[name] = constructor;
            }

            metadata.AddCustomAttribute(assembly, constructor, metadata.GetOrAddBlob(value));
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
