using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryFastDiffOperationTests
{
    const string Framework = "net11.0";
    const string TargetVersion = "1.0.0";
    const string CurrentVersion = "2.0.0";
    const string AssetPath = "lib/net11.0/ILInspector.Metadata.FastDiff.dll";
    const string AssetId = "compile:" + AssetPath;
    const string ReferencePath = "ref/net11.0/ILInspector.Metadata.FastDiff.dll";
    const string ReferenceAssetId = "compile:" + ReferencePath;

    [Fact]
    public async Task Export_ReportsApiAndBodyStatesByNavigationIdentity()
    {
        string packageId = await RegisterFixturePairAsync();

        BrowserLibraryFastDiffResult result = await Query(Request(packageId));

        Assert.True(
            result.Kind == BrowserLibraryFastDiffResultKind.Succeeded,
            $"{result.Kind}: {result.Error}\n{result.Diagnostic}");
        BrowserLibraryFastDiffValue value = Assert.IsType<BrowserLibraryFastDiffValue>(result.Value);
        Assert.EndsWith(AssetPath, value.CurrentAssetId, StringComparison.Ordinal);
        Assert.True(value.ComparedTypeCount > value.Types.Length);
        Assert.Equal(
            (BrowserFastDiffState.Unchanged, BrowserFastDiffState.Changed),
            State(value, "FastDiffFixture.BodyOnly"));
        Assert.Equal(
            (BrowserFastDiffState.Changed, BrowserFastDiffState.Unchanged),
            State(value, "FastDiffFixture.PublicMemberAdded"));
        // Nested Types use the escaped navigation identity, not the dotted name.
        BrowserFastDiffType inner = Assert.Single(
            value.Types,
            type => type.Identifier == "FastDiffFixture.Outer+Inner");
        Assert.Equal("FastDiffFixture.Outer.Inner", inner.FullName);
        // A Type with no difference on either axis is omitted.
        Assert.DoesNotContain(value.Types, type => type.Identifier == "FastDiffFixture.Unchanged");
    }

    [Fact]
    public async Task Export_ComparesImplementationsBehindReferenceAssemblies()
    {
        // Both versions share one reference assembly, so only their
        // implementations differ; comparing references would find nothing.
        string packageId = "Fast.Diff.Ref." + Guid.NewGuid().ToString("N");
        byte[] reference = File.ReadAllBytes(FixtureCatalog.MetadataFastDiffV1.AssemblyPath());
        await Register(packageId, TargetVersion,
            (ReferencePath, reference),
            (AssetPath, File.ReadAllBytes(FixtureCatalog.MetadataFastDiffV1.AssemblyPath())));
        await Register(packageId, CurrentVersion,
            (ReferencePath, reference),
            (AssetPath, File.ReadAllBytes(FixtureCatalog.MetadataFastDiffV2.AssemblyPath())));

        BrowserLibraryFastDiffResult result = await Query(
            Request(packageId) with { CompileAssetId = ReferenceAssetId });

        Assert.True(
            result.Kind == BrowserLibraryFastDiffResultKind.Succeeded,
            $"{result.Kind}: {result.Error}\n{result.Diagnostic}");
        BrowserLibraryFastDiffValue value = Assert.IsType<BrowserLibraryFastDiffValue>(result.Value);
        Assert.Equal(
            (BrowserFastDiffState.Unchanged, BrowserFastDiffState.Changed),
            State(value, "FastDiffFixture.BodyOnly"));
    }

    [Fact]
    public async Task Export_FailsVisiblyWithoutAnImplementationAssembly()
    {
        string packageId = "Fast.Diff.RefOnly." + Guid.NewGuid().ToString("N");
        await Register(packageId, TargetVersion,
            (ReferencePath, File.ReadAllBytes(FixtureCatalog.MetadataFastDiffV1.AssemblyPath())));
        await Register(packageId, CurrentVersion,
            (ReferencePath, File.ReadAllBytes(FixtureCatalog.MetadataFastDiffV2.AssemblyPath())));

        BrowserLibraryFastDiffResult result = await Query(
            Request(packageId) with { CompileAssetId = ReferenceAssetId });

        Assert.Equal(BrowserLibraryFastDiffResultKind.Failed, result.Kind);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Export_RejectsAnUnsupportedSchemaAsAFailedResult()
    {
        BrowserLibraryFastDiffResult result = await Query(
            Request("Unregistered.Package") with { SchemaVersion = 0 });

        Assert.Equal(BrowserLibraryFastDiffResultKind.Failed, result.Kind);
        Assert.Contains("schema version", result.Error, StringComparison.Ordinal);
    }

    static (BrowserFastDiffState Api, BrowserFastDiffState Body) State(
        BrowserLibraryFastDiffValue value,
        string identifier)
    {
        BrowserFastDiffType type = Assert.Single(value.Types, type => type.Identifier == identifier);
        return (type.Api, type.Body);
    }

    static BrowserLibraryFastDiffRequest Request(string packageId) => new(
        BrowserLibraryFastDiffSchema.Version,
        packageId,
        CurrentVersion,
        TargetVersion,
        Framework,
        AssetId);

    static async Task<BrowserLibraryFastDiffResult> Query(BrowserLibraryFastDiffRequest request)
    {
        string resultJson = await MetadataExports.QueryLibraryFastDiff(
            Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserMetadataJsonContext.Default.BrowserLibraryFastDiffRequest));
        return JsonSerializer.Deserialize(
                resultJson,
                BrowserMetadataJsonContext.Default.BrowserLibraryFastDiffResult)
            ?? throw new InvalidOperationException("The export returned no result.");
    }

    static async Task<string> RegisterFixturePairAsync()
    {
        string packageId = "Fast.Diff." + Guid.NewGuid().ToString("N");
        await Register(packageId, TargetVersion, FixtureCatalog.MetadataFastDiffV1.AssemblyPath());
        await Register(packageId, CurrentVersion, FixtureCatalog.MetadataFastDiffV2.AssemblyPath());
        return packageId;
    }

    static ValueTask Register(string packageId, string version, string assemblyPath)
        => Register(packageId, version, (AssetPath, File.ReadAllBytes(assemblyPath)));

    static ValueTask Register(
        string packageId,
        string version,
        params (string Path, byte[] Bytes)[] entries)
        => BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                Archive(packageId, version, entries),
                fromCache: false));

    static byte[] Archive(string packageId, string version, (string Path, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream manifest = archive.CreateEntry($"{packageId}.nuspec").Open())
            {
                manifest.Write(Encoding.UTF8.GetBytes(
                    $"<package><metadata><id>{packageId}</id><version>{version}</version>"
                        + "<authors>Tests</authors><description>Fast Diff fixture</description>"
                        + "</metadata></package>"));
            }
            foreach ((string path, byte[] bytes) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }
        return buffer.ToArray();
    }
}
