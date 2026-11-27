using System.Runtime.Versioning;

using BrowserTypeMetadata =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task QueryTypeProjection_FindsCrossAssemblyEnumerableWhereCandidates()
    {
        const string runtimePackageId =
            "Browser.TypeExtensions.SystemRuntime";
        const string linqPackageId =
            "Browser.TypeExtensions.SystemLinq";
        const string receiverType =
            "System.Collections.Generic.IEnumerable`1";
        string runtimeReferencePath = Net11SystemRuntimeReferencePath();
        _ = await Coordinate(
            runtimePackageId,
            Package(
                File.ReadAllBytes(runtimeReferencePath),
                "ref/net11.0/System.Runtime.dll"));
        _ = await Coordinate(
            linqPackageId,
            Package(
                File.ReadAllBytes(typeof(Enumerable).Assembly.Location),
                "ref/net11.0/System.Linq.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            runtimePackageId,
            "System.Runtime.dll",
            receiverType,
            $$"""
            [
              {
                "package": "{{runtimePackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "{{linqPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        var extensionCandidates = Assert.IsType<
            DotnetInspect.Web.Interop.Metadata
                .BrowserTypeExtensionCandidatesMetadata>(
                    metadata.ExtensionCandidates);
        Assert.Equal("System.Runtime", extensionCandidates.ReceiverAssembly);
        Assert.Equal(receiverType, extensionCandidates.ReceiverType);
        var linqParticipant = Assert.Single(
            extensionCandidates.Participants,
            participant => participant.PackageId == linqPackageId);
        Assert.Equal("complete", linqParticipant.Outcome);
        Assert.Equal("counted", linqParticipant.CountOutcome);
        Assert.True(linqParticipant.Count > 0);
        Assert.Contains(
            linqParticipant.Rows,
            row => row.DeclaringType == "System.Linq.Enumerable"
                && row.Signature.StartsWith(
                    "M:System.Linq.Enumerable.Where<",
                    StringComparison.Ordinal));
    }

    static string Net11SystemRuntimeReferencePath()
    {
        string dotnetRoot = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment
                .GetRuntimeDirectory(),
            "..",
            "..",
            ".."));
        string referencePackRoot = Path.Combine(
            dotnetRoot,
            "packs",
            "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(referencePackRoot))
        {
            throw new DirectoryNotFoundException(
                $"The .NET 11 reference pack was not found under "
                    + $"'{referencePackRoot}'.");
        }

        string? referenceAssembly = Directory.GetDirectories(referencePackRoot)
            .Select(directory => (
                Directory: directory,
                Version: Version.TryParse(
                    Path.GetFileName(directory),
                    out Version? version)
                    ? version
                    : null))
            .Where(item => item.Version?.Major == 11)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(
                item.Directory,
                "ref",
                "net11.0",
                "System.Runtime.dll"))
            .FirstOrDefault(File.Exists);
        return referenceAssembly
            ?? throw new FileNotFoundException(
                "The .NET 11 System.Runtime reference assembly was not found "
                    + $"under '{referencePackRoot}'.");
    }
}
