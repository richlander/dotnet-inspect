using System.Net;
using System.Net.Sockets;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for HttpRetryHelper retry logic and status code classification.
/// </summary>
public class HttpRetryHelperTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]       // 408
    [InlineData(HttpStatusCode.InternalServerError)]  // 500
    [InlineData(HttpStatusCode.BadGateway)]           // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]   // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]       // 504
    public void IsRetryableStatus_RetryableStatusCodes_ReturnsTrue(HttpStatusCode status)
    {
        Assert.True(HttpRetryHelper.IsRetryableStatus(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]                   // 200
    [InlineData(HttpStatusCode.Created)]              // 201
    [InlineData(HttpStatusCode.NotFound)]             // 404
    [InlineData(HttpStatusCode.BadRequest)]           // 400
    [InlineData(HttpStatusCode.Unauthorized)]         // 401
    [InlineData(HttpStatusCode.Forbidden)]            // 403
    [InlineData(HttpStatusCode.NotImplemented)]       // 501
    public void IsRetryableStatus_NonRetryableStatusCodes_ReturnsFalse(HttpStatusCode status)
    {
        Assert.False(HttpRetryHelper.IsRetryableStatus(status));
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.Shutdown)]
    [InlineData(SocketError.TimedOut)]
    [InlineData(SocketError.TryAgain)]
    public void IsRetryableSocketError_RetryableErrors_ReturnsTrue(SocketError error)
    {
        Assert.True(HttpRetryHelper.IsRetryableSocketError(error));
    }

    [Theory]
    [InlineData(SocketError.Success)]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.AddressNotAvailable)]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.NetworkUnreachable)]
    public void IsRetryableSocketError_NonRetryableErrors_ReturnsFalse(SocketError error)
    {
        Assert.False(HttpRetryHelper.IsRetryableSocketError(error));
    }

    [Fact]
    public void GetRetryDelay_ReturnsIncreasingDelays()
    {
        var delay1 = HttpRetryHelper.GetRetryDelay(1);
        var delay2 = HttpRetryHelper.GetRetryDelay(2);
        var delay3 = HttpRetryHelper.GetRetryDelay(3);

        // Delays should increase exponentially (base delay doubles each time)
        // Delay 1: 2^1 * 100 + jitter (0-200) = 200-400ms
        // Delay 2: 2^2 * 100 + jitter (0-200) = 400-600ms
        // Delay 3: 2^3 * 100 + jitter (0-200) = 800-1000ms

        Assert.True(delay1.TotalMilliseconds >= 200 && delay1.TotalMilliseconds < 400,
            $"Delay1 should be 200-400ms, was {delay1.TotalMilliseconds}ms");
        Assert.True(delay2.TotalMilliseconds >= 400 && delay2.TotalMilliseconds < 600,
            $"Delay2 should be 400-600ms, was {delay2.TotalMilliseconds}ms");
        Assert.True(delay3.TotalMilliseconds >= 800 && delay3.TotalMilliseconds < 1000,
            $"Delay3 should be 800-1000ms, was {delay3.TotalMilliseconds}ms");
    }

    [Fact]
    public void GetRetryDelay_IncludesJitter()
    {
        // Run multiple times and verify we get different values (jitter is random)
        HashSet<double> delays = [];
        for (int i = 0; i < 20; i++)
        {
            delays.Add(HttpRetryHelper.GetRetryDelay(1).TotalMilliseconds);
        }

        // With random jitter of 0-200ms, we should see variation
        // (statistically, 20 samples should show at least some variation)
        Assert.True(delays.Count > 1, "Jitter should produce varying delays");
    }

    [Fact]
    public void GetSocketError_WithSocketException_ExtractsError()
    {
        var socketException = new SocketException((int)SocketError.ConnectionReset);
        var httpException = new HttpRequestException("Connection reset", socketException);

        var (isRetryable, error) = HttpRetryHelper.GetSocketError(httpException);

        Assert.True(isRetryable);
        Assert.Equal(SocketError.ConnectionReset, error);
    }

    [Fact]
    public void GetSocketError_WithNestedSocketException_ExtractsError()
    {
        var socketException = new SocketException((int)SocketError.TimedOut);
        var innerException = new Exception("Wrapper", socketException);
        var httpException = new HttpRequestException("Request failed", innerException);

        var (isRetryable, error) = HttpRetryHelper.GetSocketError(httpException);

        Assert.True(isRetryable);
        Assert.Equal(SocketError.TimedOut, error);
    }

    [Fact]
    public void GetSocketError_WithNoSocketException_ReturnsNotRetryable()
    {
        var httpException = new HttpRequestException("Generic error");

        var (isRetryable, error) = HttpRetryHelper.GetSocketError(httpException);

        Assert.False(isRetryable);
        Assert.Equal(SocketError.Success, error);
    }

    [Fact]
    public void GetSocketError_WithNonRetryableSocketError_ReturnsNotRetryable()
    {
        var socketException = new SocketException((int)SocketError.HostNotFound);
        var httpException = new HttpRequestException("Host not found", socketException);

        var (isRetryable, error) = HttpRetryHelper.GetSocketError(httpException);

        Assert.False(isRetryable);
        Assert.Equal(SocketError.HostNotFound, error);
    }

    [Fact]
    public void DefaultRetryCount_IsThree()
    {
        Assert.Equal(3, HttpRetryHelper.DefaultRetryCount);
    }

    [Theory]
    [InlineData(RetryFailureMode.NonRetryableStatus, "https://user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.RetryableStatus, "https://private.example/v3/index.json?access_token=sup3rs3cret", "503 (retryable)", true)]
    [InlineData(RetryFailureMode.RetryableSocket, "https://private.example/F/feed/auth/sup3rs3cret/api/v3/index.json", "Socket error", true)]
    [InlineData(RetryFailureMode.Timeout, "https://private.example/v3/index.json?sig=sup3rs3cret", "request timeout", true)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "//user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    public async Task FailureLogsRedactTheUrlOnEveryBranch(
        RetryFailureMode mode,
        string url,
        string branchMessage,
        bool exhaustsRetries)
    {
        var messages = new List<string>();
        using var client = new HttpClient(new FailureHandler(mode))
        {
            BaseAddress = new Uri("https://base.example/")
        };

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            url,
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        Assert.NotEmpty(messages);
        string branchLog = Assert.Single(messages, message =>
            message.Contains(branchMessage, StringComparison.Ordinal));
        AssertRedactedUrl(branchLog);
        var exhaustedLogs = messages
            .Where(message => message.Contains("Max retries", StringComparison.Ordinal))
            .ToArray();
        if (exhaustsRetries)
        {
            AssertRedactedUrl(Assert.Single(exhaustedLogs));
        }
        else
        {
            Assert.Empty(exhaustedLogs);
        }

        Assert.DoesNotContain(messages, message =>
            message.Contains("sup3rs3cret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessWithoutLoggingDoesNotRequireDisplayUrlParsing()
    {
        using var client = new HttpClient(new SuccessHandler())
        {
            BaseAddress = new Uri("https://base.example/")
        };

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "/\\/\u202E",
            log: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", content);
    }

    private static void AssertRedactedUrl(string message)
    {
        Assert.DoesNotContain("sup3rs3cret", message, StringComparison.Ordinal);
        Assert.Contains("private.example", message, StringComparison.Ordinal);
    }

    public enum RetryFailureMode
    {
        NonRetryableStatus,
        RetryableStatus,
        RetryableSocket,
        Timeout,
    }

    private sealed class FailureHandler(RetryFailureMode mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            mode switch
            {
                RetryFailureMode.NonRetryableStatus =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                RetryFailureMode.RetryableStatus =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                RetryFailureMode.RetryableSocket =>
                    Task.FromException<HttpResponseMessage>(new HttpRequestException(
                        "Connection reset",
                        new SocketException((int)SocketError.ConnectionReset))),
                RetryFailureMode.Timeout =>
                    Task.FromException<HttpResponseMessage>(new TaskCanceledException()),
                _ => throw new InvalidOperationException($"Unexpected failure mode: {mode}"),
            };
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
                RequestMessage = request,
            });
    }
}
