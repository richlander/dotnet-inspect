using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Fact]
    public async Task ExecuteAsync_AssemblyReferencesMatchEveryAdmittedFrameworkGroup()
    {
        string assetRoot = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQueryReferences");
        var archive = FakePackageContent.FromBytes(
            (
                "lib/net10.0/Microsoft.Extensions.Http.dll",
                File.ReadAllBytes(Path.Combine(
                    assetRoot,
                    "net10.0",
                    "Microsoft.Extensions.Http.dll"))),
            (
                "lib/net462/Microsoft.Extensions.Http.dll",
                File.ReadAllBytes(Path.Combine(
                    assetRoot,
                    "net462",
                    "Microsoft.Extensions.Http.dll"))));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] = archive,
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Microsoft.Extensions.*",
                [
                    Term(
                        PackageQuery.ReferencesTermKey,
                        "microsoft.extensions.dependencyinjection.abstractions"),
                ],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(
            match.Evidence,
            item => item.Id == PackageQuery.ReferencesTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(2, summary.Count);
        Assert.Equal(
            [
                "net10.0: lib/net10.0/Microsoft.Extensions.Http.dll -> Microsoft.Extensions.DependencyInjection.Abstractions",
                "net462: lib/net462/Microsoft.Extensions.Http.dll -> Microsoft.Extensions.DependencyInjection.Abstractions",
            ],
            summary.Preview.Select(item => item.ToString()));
        Assert.Equal(PackageQueryAcquisitionTier.PackageContent, match.Tier);
        Assert.Equal(
            [
                "lib/net10.0/Microsoft.Extensions.Http.dll",
                "lib/net462/Microsoft.Extensions.Http.dll",
            ],
            archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferencesMatchLegacyFrameworkGroup()
    {
        const string framework = "portable-win8%2Bwpa81";
        const string fixtureFramework = "portable-win8+wpa81";
        const string assetPath =
            "lib/portable-win8%2Bwpa81/PCLStorage.dll";
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQueryReferences",
            fixtureFramework,
            "PCLStorage.dll");
        var archive = FakePackageContent.FromBytes(
            (assetPath, File.ReadAllBytes(assemblyPath)));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["PCLStorage"] = archive,
            });
        var source = SourceFor(
            Manifest("PCLStorage"),
            "PCLStorage");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "PCLStorage*",
                [Term(PackageQuery.ReferencesTermKey, "Windows")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(
            match.Evidence,
            item => item.Id == PackageQuery.ReferencesTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(1, summary.Count);
        Assert.Equal(
            $"{framework}: {assetPath} -> Windows",
            Assert.Single(summary.Preview).ToString());
        Assert.Equal([assetPath], archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceNearMissDoesNotMatch()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQueryReferences",
            "net10.0",
            "Microsoft.Extensions.Http.dll");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] =
                    FakePackageContent.FromBytes(
                        (
                            "lib/net10.0/Microsoft.Extensions.Http.dll",
                            File.ReadAllBytes(assemblyPath))),
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Microsoft.Extensions.*",
                [Term(PackageQuery.ReferencesTermKey, "System.Collections.Immutable")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(0, summary.Matches);
        Assert.Equal(0, summary.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceTermsAndTogether()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQueryReferences",
            "net10.0",
            "Microsoft.Extensions.Http.dll");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] =
                    FakePackageContent.FromBytes(
                        (
                            "lib/net10.0/Microsoft.Extensions.Http.dll",
                            File.ReadAllBytes(assemblyPath))),
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Microsoft.Extensions.*",
                [
                    Term(
                        PackageQuery.ReferencesTermKey,
                        "Microsoft.Extensions.DependencyInjection.Abstractions"),
                    Term(
                        PackageQuery.ReferencesTermKey,
                        "Contoso.Missing"),
                ],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(content.Requests);
        Assert.Equal(
            1,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Candidates);
    }

    [Fact]
    public async Task ExecuteAsync_CheapMismatchPreventsAssemblyAcquisition()
    {
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] = new FakePackageContent(),
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Microsoft.Extensions.*",
                [
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                    Term(
                        PackageQuery.ReferencesTermKey,
                        "Microsoft.Extensions.DependencyInjection.Abstractions"),
                ],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(content.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedAssemblyReferenceAssetRemainsVisible()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageQueryReferences",
            "net10.0",
            "Microsoft.Extensions.Http.dll");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Microsoft.Extensions.Http"] =
                    FakePackageContent.FromBytes(
                        (
                            "lib/net10.0/Microsoft.Extensions.Http.dll",
                            File.ReadAllBytes(assemblyPath)),
                        (
                            "lib/net8.0/Broken.dll",
                            "not a managed assembly"u8.ToArray())),
            });
        var source = SourceFor(
            Manifest("Microsoft.Extensions.Http"),
            "Microsoft.Extensions.Http");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Microsoft.Extensions.*",
                [
                    Term(
                        PackageQuery.ReferencesTermKey,
                        "Microsoft.Extensions.DependencyInjection.Abstractions"),
                ],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentEvaluation,
            failure.Kind);
        Assert.Equal(
            "The package content could not be evaluated.",
            failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyModuleNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            ManagedAssemblyWithReferences(
                1,
                RequiredIdentityName.Module));

    [Fact]
    public async Task ExecuteAsync_MalformedModuleNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            WithMalformedModuleDefinitionName(
                ManagedAssemblyWithReferences(1)));

    [Fact]
    public async Task ExecuteAsync_EmptyAssemblyNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            ManagedAssemblyWithReferences(
                1,
                RequiredIdentityName.Assembly));

    [Fact]
    public async Task ExecuteAsync_MalformedAssemblyNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            WithMalformedAssemblyDefinitionName(
                ManagedAssemblyWithReferences(1)));

    [Fact]
    public async Task ExecuteAsync_EmptyAssemblyReferenceNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            ManagedAssemblyWithReferences(
                1,
                RequiredIdentityName.AssemblyReference));

    [Fact]
    public async Task ExecuteAsync_MalformedAssemblyReferenceNameRemainsVisible() =>
        await AssertAssemblyReferenceIdentityFailureAsync(
            WithMalformedAssemblyReferenceName(
                ManagedAssemblyWithReferences(1)));

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceAssetLimitRemainsVisible()
    {
        (string Path, byte[] Content)[] entries =
        [
            .. Enumerable.Range(
                    0,
                    PackageQuery.MaximumAssemblyReferenceAssets + 1)
                .Select(index =>
                    (
                        $"lib/net8.0/Assembly{index:D3}.dll",
                        "not opened"u8.ToArray())),
        ];
        var archive = FakePackageContent.FromBytes(entries);
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest("Contoso.Package"),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ReferencesTermKey, "System.Runtime")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Empty(archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceRowLimitRemainsVisible()
    {
        var archive = FakePackageContent.FromBytes(
            (
                "lib/net8.0/Contoso.Package.dll",
                ManagedAssemblyWithReferences(
                    PackageQuery.MaximumAssemblyReferenceRows + 1)));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest("Contoso.Package"),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ReferencesTermKey, "Reference00000")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceEntryLimitRemainsVisible()
    {
        var archive = FakePackageContent.FromBytes(
            (
                "lib/net8.0/Contoso.Package.dll",
                new byte[
                    PackageQuery.MaximumAssemblyReferenceEntryBytes + 1]));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest("Contoso.Package"),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ReferencesTermKey, "System.Runtime")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AssemblyReferenceTotalByteLimitRemainsVisible()
    {
        byte[] image = ManagedAssemblyWithReferences(1);
        int paddedLength =
            PackageQuery.MaximumAssemblyReferenceTotalBytes / 3 + 1;
        var archive = FakePackageContent.FromBytes(
            (
                "lib/net8.0/Contoso.One.dll",
                PaddedImage(image, paddedLength)),
            (
                "lib/net8.0/Contoso.Two.dll",
                PaddedImage(image, paddedLength)),
            (
                "lib/net8.0/Contoso.Three.dll",
                PaddedImage(image, paddedLength)));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest("Contoso.Package"),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ReferencesTermKey, "Reference00000")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(3, archive.EntryRequests.Count);
    }
}
