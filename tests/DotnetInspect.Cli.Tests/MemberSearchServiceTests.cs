using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Tests;

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
            FindSearchResult<MemberFindResult> search =
                await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = SearchTargetMemberName,
                    BinPaths = [directory],
                    IncludeAll = true,
                    Members = true,
                },
                [SearchTargetMemberName],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

            Assert.False(search.HasFailures);
            var result = Assert.Single(
                search.Rows,
                r => r.Member == SearchTargetMemberName
                    && r.Library == "CopiedMemberAssembly");
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
            FindSearchResult<MemberFindResult> search =
                await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = "FindMembersAsync_*",
                    BinPaths = [directory],
                    IncludeAll = true,
                    Members = true,
                },
                ["FindMembersAsync_*"],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

            Assert.False(search.HasFailures);
            Assert.NotEmpty(search.Rows);
            Assert.All(
                search.Rows,
                r => Assert.Equal(MatchKind.Glob, r.Match));
            Assert.Contains(
                search.Rows,
                r => r.Member == SearchTargetMemberName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FindMembersAsync_InvalidAssemblyWarnsWithoutVerbose()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            path,
            "not a managed assembly",
            TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        try
        {
            var capture = await ConsoleCapture.RunAsync(async () =>
            {
                FindSearchResult<MemberFindResult> search =
                    await MemberSearchService.FindMembersAsync(
                        new FindOptions
                        {
                            Pattern = ".NoSuchMember",
                            Assemblies = [path],
                            Members = true,
                        },
                        ["NoSuchMember"],
                        new VerboseLogger(enabled: false),
                        httpClient,
                        TestContext.Current.CancellationToken);
                Assert.Empty(search.Rows);
                Assert.True(search.HasFailures);
                return 0;
            });

            Assert.Contains($"Could not read {path}", capture.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FindMembersAsync_WithLimitDoesNotResolveLaterSources()
    {
        using var httpClient = new HttpClient();
        FindSearchResult<MemberFindResult>? search = null;
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"missing-member-search-{Guid.NewGuid():N}");

        var capture = await ConsoleCapture.RunAsync(async () =>
        {
            search = await MemberSearchService.FindMembersAsync(
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
                httpClient,
                TestContext.Current.CancellationToken);
            return 0;
        });

        Assert.NotNull(search);
        Assert.NotEmpty(search.Rows);
        Assert.False(search.HasFailures);
        Assert.DoesNotContain("Directory not found", capture.Error);
    }

    [Fact]
    public async Task FindMembersAsync_TypeFilterPrecedesTrustedLimit()
    {
        using var httpClient = new HttpClient();
        FindSearchResult<MemberFindResult> search =
            await MemberSearchService.FindMembersAsync(
                new FindOptions
                {
                    Pattern = "*",
                    Assemblies =
                    [
                        typeof(MemberSearchServiceTests).Assembly.Location,
                    ],
                    IncludeAll = true,
                    Members = true,
                    TypeFilter =
                        typeof(MemberSearchServiceTests).FullName,
                    Limit = 1,
                },
                ["*"],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        MemberFindResult result = Assert.Single(search.Rows);
        Assert.Equal(
            typeof(MemberSearchServiceTests).FullName,
            result.DeclaringType);
        Assert.False(search.HasFailures);
    }
}
