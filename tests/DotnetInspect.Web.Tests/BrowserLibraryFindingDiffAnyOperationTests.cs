using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryFindingDiffAnyOperationTests
{
    const string Framework = "net11.0";
    const string TargetVersion = "1.0.0";
    const string CurrentVersion = "2.0.0";
    const string AssetPath = "lib/net11.0/ILInspector.Metadata.FindingDiffAny.dll";
    const string AssetId = "compile:" + AssetPath;
    const string ReferencePath = "ref/net11.0/ILInspector.Metadata.FindingDiffAny.dll";
    const string ReferenceAssetId = "compile:" + ReferencePath;

    [Fact]
    public async Task Export_ReportsApiAndBodyStatesByNavigationIdentity()
    {
        string packageId = await RegisterFixturePairAsync();

        BrowserLibraryFindingDiffAnyResult result = await Query(Request(packageId));

        Assert.True(
            result.Kind == BrowserLibraryFindingDiffAnyResultKind.Succeeded,
            $"{result.Kind}: {result.Error}\n{result.Diagnostic}");
        BrowserLibraryFindingDiffAnyValue value = Assert.IsType<BrowserLibraryFindingDiffAnyValue>(result.Value);
        Assert.EndsWith(AssetPath, value.CurrentAssetId, StringComparison.Ordinal);
        Assert.True(value.ComparedTypeCount > value.Types.Length);
        Assert.Equal(
            (BrowserFindingDiffAnyState.Unchanged, BrowserFindingDiffAnyState.Changed),
            State(value, "FindingDiffAnyFixture.BodyOnly"));
        Assert.Equal(
            (BrowserFindingDiffAnyState.Changed, BrowserFindingDiffAnyState.Unchanged),
            State(value, "FindingDiffAnyFixture.PublicMemberAdded"));
        // Nested Types use the escaped navigation identity, not the dotted name.
        BrowserFindingDiffAnyType inner = Assert.Single(
            value.Types,
            type => type.Identifier == "FindingDiffAnyFixture.Outer+Inner");
        Assert.Equal("FindingDiffAnyFixture.Outer.Inner", inner.FullName);
        // A Type with no difference on either axis is omitted.
        Assert.DoesNotContain(value.Types, type => type.Identifier == "FindingDiffAnyFixture.Unchanged");
    }

    [Fact]
    public async Task Export_YieldsBetweenStepsAndReachesTheSameResult()
    {
        string packageId = await RegisterFixturePairAsync();
        BrowserLibraryFindingDiffAnyResult whole = await Query(Request(packageId));

        // A zero budget makes every row and Type its own step.
        TimeSpan budget = MetadataExports.LibraryFindingDiffAnyStepBudget;
        MetadataExports.LibraryFindingDiffAnyStepBudget = TimeSpan.Zero;
        BrowserLibraryFindingDiffAnyResult stepped;
        try
        {
            stepped = await Query(Request(packageId));
        }
        finally
        {
            MetadataExports.LibraryFindingDiffAnyStepBudget = budget;
        }

        Assert.Equal(BrowserLibraryFindingDiffAnyResultKind.Succeeded, stepped.Kind);
        Assert.Equal(whole.Value!.ComparedTypeCount, stepped.Value!.ComparedTypeCount);
        Assert.Equal(whole.Value.Types, stepped.Value.Types);
    }

    [Fact]
    public async Task Export_ApiAxisReportsOnlyApiChanges()
    {
        string packageId = await RegisterFixturePairAsync();

        BrowserLibraryFindingDiffAnyResult result = await Query(
            Request(packageId) with { Axes = BrowserFindingDiffAnyAxes.Api });

        Assert.True(
            result.Kind == BrowserLibraryFindingDiffAnyResultKind.Succeeded,
            $"{result.Kind}: {result.Error}\n{result.Diagnostic}");
        BrowserLibraryFindingDiffAnyValue value = Assert.IsType<BrowserLibraryFindingDiffAnyValue>(result.Value);
        Assert.All(value.Types, type => Assert.Equal(BrowserFindingDiffAnyState.NotCompared, type.Body));
        Assert.Equal(
            (BrowserFindingDiffAnyState.Changed, BrowserFindingDiffAnyState.NotCompared),
            State(value, "FindingDiffAnyFixture.PublicMemberAdded"));
        // A body-only change is not an API change, so the Type is absent.
        Assert.DoesNotContain(value.Types, type => type.Identifier == "FindingDiffAnyFixture.BodyOnly");
    }

    [Fact]
    public async Task Export_ComparesImplementationsBehindReferenceAssemblies()
    {
        // Both versions share one reference assembly, so only their
        // implementations differ; comparing references would find nothing.
        string packageId = "Fast.Diff.Ref." + Guid.NewGuid().ToString("N");
        byte[] reference = File.ReadAllBytes(FixtureCatalog.MetadataFindingDiffAnyV1.AssemblyPath());
        await Register(packageId, TargetVersion,
            (ReferencePath, reference),
            (AssetPath, File.ReadAllBytes(FixtureCatalog.MetadataFindingDiffAnyV1.AssemblyPath())));
        await Register(packageId, CurrentVersion,
            (ReferencePath, reference),
            (AssetPath, File.ReadAllBytes(FixtureCatalog.MetadataFindingDiffAnyV2.AssemblyPath())));

        BrowserLibraryFindingDiffAnyResult result = await Query(
            Request(packageId) with { CompileAssetId = ReferenceAssetId });

        Assert.True(
            result.Kind == BrowserLibraryFindingDiffAnyResultKind.Succeeded,
            $"{result.Kind}: {result.Error}\n{result.Diagnostic}");
        BrowserLibraryFindingDiffAnyValue value = Assert.IsType<BrowserLibraryFindingDiffAnyValue>(result.Value);
        Assert.Equal(
            (BrowserFindingDiffAnyState.Unchanged, BrowserFindingDiffAnyState.Changed),
            State(value, "FindingDiffAnyFixture.BodyOnly"));
    }

    [Fact]
    public async Task Export_FailsVisiblyWithoutAnImplementationAssembly()
    {
        string packageId = "Fast.Diff.RefOnly." + Guid.NewGuid().ToString("N");
        await Register(packageId, TargetVersion,
            (ReferencePath, File.ReadAllBytes(FixtureCatalog.MetadataFindingDiffAnyV1.AssemblyPath())));
        await Register(packageId, CurrentVersion,
            (ReferencePath, File.ReadAllBytes(FixtureCatalog.MetadataFindingDiffAnyV2.AssemblyPath())));

        BrowserLibraryFindingDiffAnyResult result = await Query(
            Request(packageId) with { CompileAssetId = ReferenceAssetId });

        Assert.Equal(BrowserLibraryFindingDiffAnyResultKind.Failed, result.Kind);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Export_RejectsAnUnsupportedSchemaAsAFailedResult()
    {
        BrowserLibraryFindingDiffAnyResult result = await Query(
            Request("Unregistered.Package") with { SchemaVersion = 0 });

        Assert.Equal(BrowserLibraryFindingDiffAnyResultKind.Failed, result.Kind);
        Assert.Contains("schema version", result.Error, StringComparison.Ordinal);
    }

    static (BrowserFindingDiffAnyState Api, BrowserFindingDiffAnyState Body) State(
        BrowserLibraryFindingDiffAnyValue value,
        string identifier)
    {
        BrowserFindingDiffAnyType type = Assert.Single(value.Types, type => type.Identifier == identifier);
        return (type.Api, type.Body);
    }

    static BrowserLibraryFindingDiffAnyRequest Request(string packageId) => new(
        BrowserLibraryFindingDiffAnySchema.Version,
        packageId,
        CurrentVersion,
        TargetVersion,
        Framework,
        AssetId,
        BrowserFindingDiffAnyAxes.ApiAndBody);

    static async Task<BrowserLibraryFindingDiffAnyResult> Query(BrowserLibraryFindingDiffAnyRequest request)
    {
        string resultJson = await MetadataExports.QueryLibraryFindingDiffAny(
            Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserMetadataJsonContext.Default.BrowserLibraryFindingDiffAnyRequest));
        return JsonSerializer.Deserialize(
                resultJson,
                BrowserMetadataJsonContext.Default.BrowserLibraryFindingDiffAnyResult)
            ?? throw new InvalidOperationException("The export returned no result.");
    }

    static async Task<string> RegisterFixturePairAsync()
    {
        string packageId = "Fast.Diff." + Guid.NewGuid().ToString("N");
        await Register(packageId, TargetVersion, FixtureCatalog.MetadataFindingDiffAnyV1.AssemblyPath());
        await Register(packageId, CurrentVersion, FixtureCatalog.MetadataFindingDiffAnyV2.AssemblyPath());
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
                        + "<authors>Tests</authors><description>Finding Diff Any fixture</description>"
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
