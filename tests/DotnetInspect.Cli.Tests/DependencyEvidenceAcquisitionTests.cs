using System.Collections.Immutable;
using System.Text;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed class DependencyEvidenceAcquisitionTests
{
    [Fact]
    public async Task AuthorizedSourcesAdmitALaterValidManifest()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [
                    ("missing", MissingSource()),
                    ("invalid", InvalidManifestSource(coordinate)),
                    ("valid", ValidManifestSource(coordinate)),
                ]);

        Assert.Empty(failures);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(
                    new PackageDependencyEvidenceRequest(roots, failures)));

        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
            root.SourceKind);
        Assert.Equal("contoso.fallback", root.PackageId);
        Assert.Equal(
            "contoso.dependency",
            Assert.Single(projection.Dependencies).PackageId);
    }

    [Fact]
    public async Task TypedManifestFailureWinsWhenNoSourceSucceeds()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [
                    ("invalid", InvalidManifestSource(coordinate)),
                    ("missing", MissingSource()),
                ]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Package failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Package>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
            failure.AcquisitionForm);
        Assert.Equal(coordinate, failure.Coordinate);
        Assert.Equal(
            PackageManifestFailureReason.IdentityMismatch,
            failure.Failure.Reason);
    }

    [Fact]
    public async Task AuthoritativeAbsenceFromEverySourceReportsNotFound()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [("first", MissingSource()), ("second", MissingSource())]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.NotFound,
            failure.Reason);
    }

    [Fact]
    public async Task UnavailableSourcePreventsAnAuthoritativeAbsenceClaim()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        using IPackageSourceClient missing = MissingSource();

        await DependencyEvidenceAcquisition.AcquireSourceManifestAsync(
            coordinate,
            [
                new PackageSource(
                    "unavailable",
                    "https://unavailable.invalid/v3/index.json"),
                new PackageSource(
                    "missing",
                    "https://missing.invalid/v3/index.json"),
            ],
            source => source.Name == "missing" ? missing : null,
            targetFramework: null,
            new InertString(TextPolicy.Field, "Contoso.Fallback@1.0.0"),
            operationContext: null,
            roots,
            failures,
            TestContext.Current.CancellationToken);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.AcquisitionFailed,
            failure.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InconclusiveSourcePreventsAnAuthoritativeAbsenceClaim(
        bool throwsTransport)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        IPackageSourceClient inconclusive = throwsTransport
            ? UnreachableSource()
            : FailingSource(PackageSourceFailureKind.Transport);
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [("missing", MissingSource()), ("inconclusive", inconclusive)]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.AcquisitionFailed,
            failure.Reason);
    }

    private static async Task<(
        ImmutableArray<PackageDependencyEvidenceInput> Roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure> Failures)>
        AcquireFromSourcesAsync(
            PackageSourceCoordinate coordinate,
            IReadOnlyList<(string Name, IPackageSourceClient Client)> sources)
    {
        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        Dictionary<string, IPackageSourceClient> clients = sources.ToDictionary(
            static source => source.Name,
            static source => source.Client,
            StringComparer.Ordinal);

        await DependencyEvidenceAcquisition.AcquireSourceManifestAsync(
            coordinate,
            [
                .. sources.Select(static source => new PackageSource(
                    source.Name,
                    $"https://{source.Name}.invalid/v3/index.json")),
            ],
            source => clients[source.Name],
            targetFramework: null,
            new InertString(
                TextPolicy.Field,
                $"{coordinate.PackageId}@{coordinate.Version}"),
            operationContext: null,
            roots,
            failures,
            TestContext.Current.CancellationToken);
        return (roots.ToImmutable(), failures.ToImmutable());
    }

    private static IPackageSourceClient MissingSource() =>
        new FakePackageSource(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));

    private static IPackageSourceClient FailingSource(
        PackageSourceFailureKind kind) =>
        new FakePackageSource(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))
        {
            MissingManifestFailure = kind,
        };

    private static IPackageSourceClient UnreachableSource() =>
        new FakePackageSource(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))
        {
            ManifestTransportFailure = true,
        };

    private static IPackageSourceClient InvalidManifestSource(
        PackageSourceCoordinate coordinate) =>
        new FakePackageSource(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{coordinate.PackageId}@{coordinate.Version}"] =
                    Manifest("Contoso.Different", "9.9.9", ""),
            });

    private static IPackageSourceClient ValidManifestSource(
        PackageSourceCoordinate coordinate) =>
        new FakePackageSource(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{coordinate.PackageId}@{coordinate.Version}"] = Manifest(
                    "Contoso.Fallback",
                    "1.0.0",
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Contoso.Dependency" version="[1.0.0]" />
                    </group>
                    """),
            });

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies) =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Dependency Evidence Tests</authors>
                <description>Dependency evidence test.</description>
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new UnusedPackageSource(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class UnusedPackageSource(PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakePackageSource(
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceFailureKind MissingManifestFailure { get; init; } =
            PackageSourceFailureKind.NotFound;

        public bool ManifestTransportFailure { get; init; }

        public PackageSourceResultIdentity Source => _results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            if (ManifestTransportFailure)
                throw new HttpRequestException("The fake source is unreachable.");

            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            return Task.FromResult(
                manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        MissingManifestFailure));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
