namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyCoordinateMatchQueryTests
{
    [Fact]
    public void Execute_SelectsOneCompatibleNuGetCoordinate()
    {
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            [
                Candidate("old", "Example.Package", "1.0.0"),
                Candidate("selected", "example.package", "2.1.0"),
                Candidate("new", "Example.Package", "3.0.0"),
            ],
            "EXAMPLE.PACKAGE",
            "2.*");

        Assert.Equal(PackageDependencyCoordinateMatchStatus.Unique, result.Status);
        Assert.Equal("selected", result.CandidateKey);
    }

    [Fact]
    public void Execute_RejectsPlatformRuntimeWithTheSamePackageIdentity()
    {
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            [
                Candidate(
                    "platform",
                    "Microsoft.NETCore.App",
                    "10.0.10",
                    PackageDependencyCoordinateKind.PlatformRuntime),
            ],
            "Microsoft.NETCore.App",
            "1.0.5");

        Assert.Equal(PackageDependencyCoordinateMatchStatus.NoMatch, result.Status);
        Assert.Null(result.CandidateKey);
    }

    [Fact]
    public void Execute_SelectsTheNuGetPackageBesideASameIdentityPlatformRuntime()
    {
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            [
                Candidate(
                    "platform",
                    "Microsoft.NETCore.App",
                    "10.0.10",
                    PackageDependencyCoordinateKind.PlatformRuntime),
                Candidate("package", "Microsoft.NETCore.App", "2.2.8"),
            ],
            "Microsoft.NETCore.App",
            "1.0.5");

        Assert.Equal(PackageDependencyCoordinateMatchStatus.Unique, result.Status);
        Assert.Equal("package", result.CandidateKey);
    }

    [Fact]
    public void Execute_PreservesAmbiguityAcrossLoadedFrameworkCoordinates()
    {
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            [
                Candidate("net8", "Example.Package", "2.0.0", framework: "net8.0"),
                Candidate("net9", "Example.Package", "2.0.0", framework: "net9.0"),
            ],
            "Example.Package",
            "2.*");

        Assert.Equal(PackageDependencyCoordinateMatchStatus.Ambiguous, result.Status);
        Assert.Null(result.CandidateKey);
    }

    [Fact]
    public void Execute_DoesNotValidateANonPackageVersionAsNuGet()
    {
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            [
                Candidate(
                    "platform",
                    "Example.Package",
                    "runtime-version",
                    PackageDependencyCoordinateKind.PlatformRuntime),
            ],
            "Example.Package",
            "1.0.0");

        Assert.Equal(PackageDependencyCoordinateMatchStatus.NoMatch, result.Status);
    }

    private static PackageDependencyCoordinateCandidate Candidate(
        string key,
        string packageId,
        string version,
        PackageDependencyCoordinateKind kind = PackageDependencyCoordinateKind.NuGetPackage,
        string framework = "net10.0") =>
        new(key, kind, packageId, version, framework);
}
