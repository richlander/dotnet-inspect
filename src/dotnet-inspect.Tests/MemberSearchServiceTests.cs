using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberSearchServiceTests
{
    // A distinctively named public member of this test assembly, used as a self-referential search
    // target so the tests need no network access or external fixtures.
    public const string SearchTargetMemberName = nameof(FindMembersAsync_BinPathUsesAssemblySetDirectorySource);

    [Fact]
    public async Task FindMembersAsync_BinPathUsesAssemblySetDirectorySource()
    {
        var directory = Directory.CreateTempSubdirectory("member-search-bin-test").FullName;
        var copiedAssembly = Path.Combine(directory, "CopiedMemberAssembly.dll");
        File.Copy(typeof(MemberSearchServiceTests).Assembly.Location, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            var results = await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = SearchTargetMemberName,
                    BinPaths = [directory],
                    IncludeAll = true,
                    Members = true,
                },
                [SearchTargetMemberName],
                new VerboseLogger(enabled: false),
                httpClient);

            var result = Assert.Single(results, r => r.Member == SearchTargetMemberName && r.Library == "CopiedMemberAssembly");
            Assert.Equal("method", result.Kind);
            Assert.Equal(typeof(MemberSearchServiceTests).FullName, result.DeclaringType);
            Assert.Equal(MatchKind.Exact, result.Match);
            Assert.Equal(Path.GetFileName(directory), result.Source);
            Assert.Null(result.SourceVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FindMembersAsync_GlobPatternMarksMatchAsGlob()
    {
        var directory = Directory.CreateTempSubdirectory("member-search-glob-test").FullName;
        var copiedAssembly = Path.Combine(directory, "CopiedGlobAssembly.dll");
        File.Copy(typeof(MemberSearchServiceTests).Assembly.Location, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            var results = await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = "FindMembersAsync_*",
                    BinPaths = [directory],
                    IncludeAll = true,
                    Members = true,
                },
                ["FindMembersAsync_*"],
                new VerboseLogger(enabled: false),
                httpClient);

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal(MatchKind.Glob, r.Match));
            Assert.Contains(results, r => r.Member == SearchTargetMemberName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FindMembersAsync_WithLimitDoesNotResolveLaterSources()
    {
        using var httpClient = new HttpClient();
        List<MemberFindResult>? results = null;
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"missing-member-search-{Guid.NewGuid():N}");

        var capture = await ConsoleCapture.RunAsync(async () =>
        {
            results = await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = SearchTargetMemberName,
                    Assemblies = [typeof(MemberSearchServiceTests).Assembly.Location],
                    BinPaths = [missingDirectory],
                    IncludeAll = true,
                    Members = true,
                    Limit = 1,
                },
                [SearchTargetMemberName],
                new VerboseLogger(enabled: false),
                httpClient);
            return 0;
        });

        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.DoesNotContain("Directory not found", capture.Error);
    }
}
