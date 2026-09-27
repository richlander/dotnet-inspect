using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Gates for docs/design/library-info-composition.md over real pinned assets.
/// </summary>
[Collection("Console")]
public sealed class LibraryInfoCompositionTests
{
    [Fact]
    public async Task RuntimeSockets_RendersDocumentFactsAndEnabledWithoutModified()
    {
        string info = await LibraryInfoAsync(Asset("runtime", "System.Net.Sockets.dll"));

        Assert.Contains("| Enabled | AOT · Runtime Async |", info, StringComparison.Ordinal);
        Assert.Contains("| Compilation | ReadyToRun |", info, StringComparison.Ordinal);
        Assert.Contains("| Target Framework | .NETCoreApp,Version=v11.0 |", info, StringComparison.Ordinal);
        Assert.Contains("| Company | Microsoft Corporation |", info, StringComparison.Ordinal);
        Assert.Contains("| Signed | Yes |", info, StringComparison.Ordinal);
        Assert.Contains("| Reproducible | Yes |", info, StringComparison.Ordinal);
        Assert.Contains("| Version | 11.0.0-rc.1.26425.128 |", info, StringComparison.Ordinal);
        Assert.DoesNotContain("Modified", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceSockets_RendersNoEnabledRow()
    {
        string info = await LibraryInfoAsync(Asset("ref", "System.Net.Sockets.dll"));

        Assert.Contains("| Name | System.Net.Sockets |", info, StringComparison.Ordinal);
        Assert.DoesNotContain("Enabled", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlOnlyUnsafe_KeepsItsCurrentImageRows()
    {
        string info = await LibraryInfoAsync(Asset("unsafe", "System.Runtime.CompilerServices.Unsafe.dll"));

        Assert.Contains("| Compilation | CoreCLR |", info, StringComparison.Ordinal);
        Assert.Contains("| Architecture | AnyCPU |", info, StringComparison.Ordinal);
        Assert.Contains("| Signed | Yes |", info, StringComparison.Ordinal);
        Assert.Contains("| Reproducible | No |", info, StringComparison.Ordinal);
        Assert.Contains("| File Size | 17.6 KB |", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AntlrWithoutInformationalVersion_KeepsAssemblyVersion()
    {
        string info = await LibraryInfoAsync(Asset("antlr", "Antlr3.Runtime.dll"));

        Assert.Contains("| Version | 3.5.0.2 |", info, StringComparison.Ordinal);
        Assert.DoesNotContain("Informational Version", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeImage_KeepsItsLegacyRows()
    {
        string info = await LibraryInfoAsync(Asset("native", "capstone.dll"));

        Assert.Contains("| Compilation | Native |", info, StringComparison.Ordinal);
        Assert.Contains("| Architecture | x64 |", info, StringComparison.Ordinal);
        Assert.DoesNotContain("Library Document", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompactSummary_MatchesLibraryInfoWithoutModified()
    {
        (int exit, string output, string error) =
            await RunAsync("library", Asset("runtime", "System.Net.Sockets.dll"), "-v:q");

        Assert.True(exit == 0, error);
        Assert.Contains("Version: 11.0.0-rc.1.26425.128", output, StringComparison.Ordinal);
        Assert.Contains("TFM: .NETCoreApp,Version=v11.0", output, StringComparison.Ordinal);
        Assert.Contains("Size: 601.3 KB", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Modified", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryInfoRowJson_CarriesTheSameValues()
    {
        // The structured form of Library Info rows is the field projection.
        (int exit, string output, string error) = await RunAsync(
            "library",
            Asset("runtime", "System.Net.Sockets.dll"),
            "-S",
            "Library Info",
            "--fields",
            "Enabled,Reproducible,Compilation,Modified",
            "--jsonl");

        Assert.True(exit == 0, error);
        Assert.Contains("{\"field\":\"Enabled\",\"value\":\"AOT · Runtime Async\"}", output, StringComparison.Ordinal);
        Assert.Contains("{\"field\":\"Reproducible\",\"value\":\"Yes\"}", output, StringComparison.Ordinal);
        Assert.Contains("{\"field\":\"Compilation\",\"value\":\"ReadyToRun\"}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Modified\",\"value\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndecodableCompany_RendersUnavailableWithItsReason()
    {
        string path = Path.Combine(Path.GetTempPath(), $"undecodable-company-{Guid.NewGuid():N}.dll");
        await File.WriteAllBytesAsync(path, BuildUndecodableCompanyImage(), TestContext.Current.CancellationToken);
        try
        {
            string info = await LibraryInfoAsync(path);

            Assert.Contains("| Company | unavailable (undecodable-metadata) |", info, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("/cache/pkg/1.0.0/lib/net8.0/Pkg.dll", "Pkg", false, AssemblyContextLibraryRole.Implementation)]
    [InlineData("/cache/pkg/1.0.0/runtimes/linux-x64/lib/net8.0/Pkg.dll", "Pkg", false, AssemblyContextLibraryRole.Implementation)]
    [InlineData("/cache/pkg/1.0.0/ref/net8.0/Pkg.dll", "Pkg", false, AssemblyContextLibraryRole.ApiOnly)]
    [InlineData("/dotnet/shared/Microsoft.NETCore.App/11.0.0/System.Net.Sockets.dll", null, true, AssemblyContextLibraryRole.Implementation)]
    [InlineData("/dotnet/packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/System.Net.Sockets.dll", null, true, AssemblyContextLibraryRole.ApiOnly)]
    [InlineData("/work/Direct.dll", null, false, AssemblyContextLibraryRole.ApiOnly)]
    public void HostSelection_ChoosesTheAdapterRole(
        string path,
        string? packageName,
        bool isPlatformAssembly,
        AssemblyContextLibraryRole expected)
    {
        Assert.Equal(expected, LibraryMetadataService.LibraryInfoRole(path, packageName, isPlatformAssembly));
    }

    private static string Asset(string folder, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "RealAssets", "LibraryInfo", folder, fileName);

    private static async Task<string> LibraryInfoAsync(string path)
    {
        (int exit, string output, string error) = await RunAsync("library", path, "-S", "Library Info");
        Assert.True(exit == 0, error);
        return output;
    }

    private static Task<(int Exit, string Output, string Error)> RunAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });

    /// <summary>
    /// A minimal assembly whose AssemblyCompanyAttribute string claims more
    /// bytes than its blob holds. C# cannot emit this shape.
    /// </summary>
    private static byte[] BuildUndecodableCompanyImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Undecodable.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("Undecodable"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);
        TypeReferenceHandle company = metadata.AddTypeReference(
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default),
            metadata.GetOrAddString("System.Reflection"),
            metadata.GetOrAddString("AssemblyCompanyAttribute"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().String());
        metadata.AddCustomAttribute(
            assembly,
            metadata.AddMemberReference(company, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(signature)),
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x05, 0x41 }));
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
