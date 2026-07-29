using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Integration tests for the Search API.
/// </summary>
[Collection("NuGet Integration")]
public class SearchServiceIntegrationTests : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly SearchService _search;

    public SearchServiceIntegrationTests()
    {
        _search = new SearchService(_http);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task SearchAsync_ReturnsResults()
    {
        var results = await _search.SearchAsync("Newtonsoft.Json", take: 3, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(results);
        Assert.Equal("Newtonsoft.Json", results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsResults()
    {
        var results = await _search.SearchAsync("", take: 5, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task SearchByPrefixAsync_FiltersByPrefix()
    {
        var results = await _search.SearchByPrefixAsync("Humanizer", take: 10, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(results);
        Assert.All(results, r =>
            Assert.StartsWith("Humanizer", r.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_ResultHasMetadata()
    {
        var results = await _search.SearchAsync("Newtonsoft.Json", take: 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.NotNull(first.Id);
        Assert.NotNull(first.Version);
        Assert.NotNull(first.Description);
        Assert.True(first.TotalDownloads > 0);
    }
}
