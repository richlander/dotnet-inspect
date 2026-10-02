using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageLicenseInventoryQueryTests
{
    [Theory]
    [InlineData(null, null, PackageLicenseIdentityKind.None, "none")]
    [InlineData(
        "expression",
        "Apache-2.0",
        PackageLicenseIdentityKind.Expression,
        "Apache-2.0")]
    [InlineData(
        "file",
        "legal/OSMFEULA.TXT",
        PackageLicenseIdentityKind.RecognizedFile,
        "OSMF")]
    [InlineData(
        "file",
        "LICENSE.txt",
        PackageLicenseIdentityKind.Unknown,
        "unknown")]
    [InlineData(
        "url",
        "https://example.test/license",
        PackageLicenseIdentityKind.Unknown,
        "unknown")]
    public void IdentityUsesOnlyTheNuspecDeclaration(
        string? kind,
        string? value,
        PackageLicenseIdentityKind expectedKind,
        string expectedValue)
    {
        PackageLicenseDeclaration? declaration = kind switch
        {
            "expression" => new(
                PackageLicenseDeclarationKind.Expression,
                value!),
            "file" => new(
                PackageLicenseDeclarationKind.File,
                value!),
            "url" => new(
                PackageLicenseDeclarationKind.Url,
                value!),
            null => null,
            _ => throw new InvalidOperationException(),
        };

        PackageLicenseIdentity identity =
            PackageLicenseIdentityQuery.Execute(declaration);

        Assert.Equal(expectedKind, identity.Kind);
        Assert.Equal(expectedValue, identity.Value);
    }

    [Fact]
    public async Task InventoryProducesOneSortedRowPerCoordinate()
    {
        PackageSourceCoordinate apache =
            PackageSourceCoordinate.Create("Example.Apache", "2.0.0");
        PackageSourceCoordinate none =
            PackageSourceCoordinate.Create("Example.None", "1.0.0");
        PackageSourceCoordinate unavailable =
            PackageSourceCoordinate.Create("Example.Unavailable", "3.0.0");
        var source = new FakeManifestSource(
            new Dictionary<PackageSourceCoordinate, string>
            {
                [apache] = Nuspec(
                    apache,
                    "<license type=\"expression\">Apache-2.0</license>"),
                [none] = Nuspec(none, license: null),
            },
            unavailable);

        PackageLicenseInventoryResult result =
            await PackageLicenseInventoryQuery.ExecuteAsync(
            [
                unavailable,
                none,
                apache,
                apache,
            ],
            source,
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageLicenseInventoryCompletion.Partial, result.Completion);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(apache, item.Coordinate);
                Assert.Equal("Apache-2.0", item.License?.Value);
                Assert.Null(item.Failure);
            },
            item =>
            {
                Assert.Equal(none, item.Coordinate);
                Assert.Equal("none", item.License?.Value);
                Assert.Null(item.Failure);
            },
            item =>
            {
                Assert.Equal(unavailable, item.Coordinate);
                Assert.Null(item.License);
                Assert.Equal(
                    PackageLicenseInventoryFailureReason
                        .ManifestAcquisitionFailed,
                    item.Failure?.Reason);
            });
    }

    private static string Nuspec(
        PackageSourceCoordinate coordinate,
        string? license) =>
        $"""
        <?xml version="1.0"?>
        <package>
          <metadata>
            <id>{coordinate.PackageId}</id>
            <version>{coordinate.Version}</version>
            <authors>Tests</authors>
            <description>License inventory fixture.</description>
            {license}
          </metadata>
        </package>
        """;

    private sealed class FakeManifestSource(
        IReadOnlyDictionary<PackageSourceCoordinate, string> manifests,
        PackageSourceCoordinate unavailable) : IPackageLicenseManifestSource
    {
        public Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                coordinate == unavailable
                    ? (PackageLicenseManifestResult)
                        new PackageLicenseManifestResult.Unavailable(
                            PackageLicenseManifestFailureReason
                                .AcquisitionFailed)
                    : new PackageLicenseManifestResult.Acquired(
                        Encoding.UTF8.GetBytes(manifests[coordinate])));
        }
    }
}
