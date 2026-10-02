using System.IO.Compression;
using System.Text;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Packages.Tests;

public sealed class PackageManifestFactsProjectionTests
{
    [Fact]
    public async Task MicrosoftAzureSignalR_PreservesNonEmptyAndEmptyGroups()
    {
        string archivePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkReferences",
            "microsoft.azure.signalr.1.33.1.nupkg");
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry manifest = Assert.Single(
            archive.Entries,
            entry =>
                !entry.FullName.Contains('/')
                && entry.FullName.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));
        await using Stream manifestStream = manifest.Open();
        byte[] bytes = await BoundedContentReader.ReadAllBytesAsync(
            manifestStream,
            PackageManifestFactsProjection.MaxManifestBytes,
            manifest.Length,
            TestContext.Current.CancellationToken);

        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.Execute(
                bytes,
                PackageSourceCoordinate.Create(
                    "Microsoft.Azure.SignalR",
                    "1.33.1")));
        PackageManifestFrameworkReferenceFacts frameworkReferences =
            AvailableFrameworkReferences(facts);

        PackageManifestFrameworkReferenceGroup net8 = Assert.Single(
            frameworkReferences.Groups,
            group => group.CanonicalTargetFramework == "net8.0");
        PackageFrameworkReferenceIdentity aspnet = Assert.Single(
            net8.References);
        Assert.Equal("Microsoft.AspNetCore.App", aspnet.Name);
        Assert.Equal(
            "Microsoft.AspNetCore.App",
            Assert.Single(net8.Occurrences).SourceName);

        PackageManifestFrameworkReferenceGroup netstandard = Assert.Single(
            frameworkReferences.Groups,
            group =>
                group.CanonicalTargetFramework == "netstandard2.0");
        Assert.Empty(netstandard.References);
        Assert.Empty(netstandard.Occurrences);
    }

    [Fact]
    public void NoFrameworkReferences_ProducesCompleteEmptySection()
    {
        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.ExecuteSelfAttested(
                Manifest()));

        Assert.Empty(AvailableFrameworkReferences(facts).Groups);
    }

    [Theory]
    [InlineData(
        """
        <group>
          <frameworkReference name="Microsoft.AspNetCore.App" />
        </group>
        """,
        PackageManifestFrameworkReferenceFailureReason.InvalidTargetFramework)]
    [InlineData(
        """
        <group targetFramework="net8.0">
          <frameworkReference />
        </group>
        """,
        PackageManifestFrameworkReferenceFailureReason.InvalidReferenceName)]
    public void InvalidFrameworkSection_PreservesIndependentManifestFacts(
        string frameworkGroup,
        PackageManifestFrameworkReferenceFailureReason expectedReason)
    {
        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.ExecuteSelfAttested(
                Manifest(frameworkGroup)));

        Assert.Equal("Example Authors", facts.Authors);
        Assert.Single(facts.DependencyGroups);
        PackageManifestFrameworkReferenceFactsResult.Failed failed =
            Assert.IsType<
                PackageManifestFrameworkReferenceFactsResult.Failed>(
                    facts.FrameworkReferences);
        Assert.Equal(expectedReason, failed.Failure.Reason);
    }

    [Fact]
    public void CaseOnlyDuplicates_PreserveOccurrencesAndOneSemanticIdentity()
    {
        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.ExecuteSelfAttested(
                Manifest(
                    """
                    <group targetFramework=".NETCoreApp,Version=v8.0">
                      <frameworkReference name="Microsoft.AspNetCore.App" />
                      <frameworkReference name="microsoft.aspnetcore.app" />
                    </group>
                    """)));
        PackageManifestFrameworkReferenceGroup group = Assert.Single(
            AvailableFrameworkReferences(facts).Groups);

        Assert.Equal(".NETCoreApp,Version=v8.0", group.SourceTargetFramework);
        Assert.Equal("net8.0", group.CanonicalTargetFramework);
        PackageFrameworkReferenceIdentity identity = Assert.Single(
            group.References);
        Assert.Equal("Microsoft.AspNetCore.App", identity.Name);
        Assert.Equal(
            ["Microsoft.AspNetCore.App", "microsoft.aspnetcore.app"],
            group.Occurrences.Select(occurrence => occurrence.SourceName));
        Assert.All(
            group.Occurrences,
            occurrence => Assert.Same(identity, occurrence.Identity));
    }

    [Fact]
    public void FrameworkReferenceGroupLimit_FailsOnlyTheSection()
    {
        var groups = new StringBuilder();
        for (int i = 0;
            i <= PackageManifestFactsProjection
                .MaxFrameworkReferenceGroups;
            i++)
        {
            groups.Append(
                $"""<group targetFramework="net{i}.0" />""");
        }

        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.ExecuteSelfAttested(
                Manifest(groups.ToString())));

        AssertSectionLimitFailure(facts);
        Assert.Equal("Example Authors", facts.Authors);
    }

    [Fact]
    public void FrameworkReferenceCountLimit_FailsOnlyTheSection()
    {
        var references = new StringBuilder();
        for (int i = 0;
            i <= PackageManifestFactsProjection.MaxFrameworkReferences;
            i++)
        {
            references.Append(
                $"""<frameworkReference name="Framework.{i}" />""");
        }

        PackageManifestFacts facts = Available(
            PackageManifestFactsProjection.ExecuteSelfAttested(
                Manifest(
                    $"""
                     <group targetFramework="net8.0">
                       {references}
                     </group>
                     """)));

        AssertSectionLimitFailure(facts);
        Assert.Equal("Example Authors", facts.Authors);
    }

    [Theory]
    [InlineData(
        PackageManifestFrameworkReferenceFailureReason.InvalidTargetFramework,
        "The package manifest contains an invalid framework-reference target.")]
    [InlineData(
        PackageManifestFrameworkReferenceFailureReason.InvalidReferenceName,
        "The package manifest contains an invalid framework-reference name.")]
    [InlineData(
        PackageManifestFrameworkReferenceFailureReason
            .ConfiguredLimitExceeded,
        "The package manifest framework-reference section exceeds a configured resource limit.")]
    public void FrameworkReferenceFailureMessage_IsStableForEveryReason(
        PackageManifestFrameworkReferenceFailureReason reason,
        string expectedMessage)
    {
        var failure = new PackageManifestFrameworkReferenceFailure(reason);

        Assert.Equal(expectedMessage, failure.Message);
    }

    [Fact]
    public void FrameworkReferenceFailureMessage_IsSafeForUnknownFutureReason()
    {
        var failure = new PackageManifestFrameworkReferenceFailure(
            (PackageManifestFrameworkReferenceFailureReason)int.MaxValue);

        Assert.Equal(
            "The package manifest framework-reference section could not be projected.",
            failure.Message);
    }

    private static byte[] Manifest(string? frameworkGroups = null) =>
        Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example Authors</authors>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Example.Dependency" version="1.0.0" />
                  </group>
                </dependencies>
                {{(frameworkGroups is null
                    ? ""
                    : $"<frameworkReferences>{frameworkGroups}</frameworkReferences>")}}
              </metadata>
            </package>
            """);

    private static PackageManifestFacts Available(
        PackageManifestFactsResult result) =>
        Assert.IsType<PackageManifestFactsResult.Available>(result).Value;

    private static PackageManifestFrameworkReferenceFacts
        AvailableFrameworkReferences(PackageManifestFacts facts) =>
        Assert.IsType<
            PackageManifestFrameworkReferenceFactsResult.Available>(
                facts.FrameworkReferences).Value;

    private static void AssertSectionLimitFailure(
        PackageManifestFacts facts)
    {
        PackageManifestFrameworkReferenceFactsResult.Failed failed =
            Assert.IsType<
                PackageManifestFrameworkReferenceFactsResult.Failed>(
                    facts.FrameworkReferences);
        Assert.Equal(
            PackageManifestFrameworkReferenceFailureReason
                .ConfiguredLimitExceeded,
            failed.Failure.Reason);
    }
}
