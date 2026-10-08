using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspect.Cli.CommandLine;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LibraryApiDiffTypeSelectionCommandTests
{
    [Fact]
    public async Task TypeFilter_ComparesSelectedTypesDespiteUnselectedMalformedType()
    {
        // A Type-filtered diff extracts only the selected Types, so a failure
        // in another Type no longer withholds the comparison.
        using var directory =
            new TemporaryTestDirectory("dotnet-inspect-diff-selection-");
        string before = Path.Combine(directory.FullName, "Before.dll");
        string after = Path.Combine(directory.FullName, "After.dll");
        File.WriteAllBytes(before, Image(addedMethod: false));
        File.WriteAllBytes(after, Image(addedMethod: true));

        var complete = await Run(before, after);
        var selected = await Run(before, after, "-t", "Ns.Good");

        Assert.NotEqual(0, complete.ExitCode);
        Assert.Contains("not compared", complete.Output + complete.Error);
        Assert.Equal(0, selected.ExitCode);
        Assert.Contains("Good", selected.Output);
        Assert.Contains("Added", selected.Output);
        Assert.DoesNotContain("not compared", selected.Output + selected.Error);
    }

    static Task<(int ExitCode, string Output, string Error)> Run(
        string before,
        string after,
        params string[] options)
    {
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{before}..{after}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }

    /// <summary>
    /// Builds an image with a well-formed <c>Ns.Good</c> and an <c>Ns.Bad</c>
    /// whose public method signature blob cannot be decoded.
    /// </summary>
    static byte[] Image(bool addedMethod)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Selection.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Selection"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle goodSignature = metadata.GetOrAddBlob(voidSignature);
        BlobHandle badSignature = metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x7F });
        const MethodAttributes Abstract = MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.Abstract | MethodAttributes.Virtual;

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle first = metadata.AddMethodDefinition(
            Abstract, MethodImplAttributes.IL, metadata.GetOrAddString("Kept"), goodSignature, -1, default);
        if (addedMethod)
        {
            metadata.AddMethodDefinition(
                Abstract, MethodImplAttributes.IL, metadata.GetOrAddString("Added"), goodSignature, -1, default);
        }
        MethodDefinitionHandle bad = metadata.AddMethodDefinition(
            Abstract, MethodImplAttributes.IL, metadata.GetOrAddString("Broken"), badSignature, -1, default);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Good"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            first);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Bad"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            bad);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}
