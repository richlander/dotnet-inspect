using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for HttpClientFactory shared instance behavior.
/// </summary>
public class HttpClientFactoryTests
{
    [Fact]
    public void Shared_ReturnsSameInstance()
    {
        var client1 = HttpClientFactory.Shared;
        var client2 = HttpClientFactory.Shared;

        Assert.Same(client1, client2);
    }

    [Fact]
    public void Shared_IsNotNull()
    {
        var client = HttpClientFactory.Shared;

        Assert.NotNull(client);
    }

    [Fact]
    public void Shared_HasUserAgentHeader()
    {
        var client = HttpClientFactory.Shared;

        Assert.True(client.DefaultRequestHeaders.Contains("User-Agent"));
    }

    [Fact]
    public void CreateNew_ReturnsDifferentInstances()
    {
        var client1 = HttpClientFactory.CreateNew();
        var client2 = HttpClientFactory.CreateNew();

        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void CreateNew_RespectsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(5);
        var client = HttpClientFactory.CreateNew(timeout);

        Assert.Equal(timeout, client.Timeout);
    }
}
