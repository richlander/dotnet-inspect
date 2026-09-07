using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DotnetInspector.Core;
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
    [InlineData(RetryFailureMode.NonRetryableSocket, "https://private.example/v3/index.json?access_token=sup3rs3cret", "error HostNotFound", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "//user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, " //user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "\t//user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "\r//user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "\n//user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "\\/user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, " \\/user:sup3rs3cret@private.example/v3/index.json", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "https://private.example/F/auth/auth/sup3rs3cret/api/v3/index.json", "(not retryable)", false)]
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

    [Fact]
    public async Task FailureLoggingRedactsTheEffectiveUrlWithoutReparsingIt()
    {
        const string Secret = "sup3rs3cret";
        var messages = new List<string>();
        using var client = new HttpClient(new FailureHandler(RetryFailureMode.NonRetryableStatus));

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            $"https://user:{Secret}@\u202E/F/feed/auth/{Secret}/api?access_token={Secret}",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        string branchLog = Assert.Single(messages, message =>
            message.Contains("(not retryable)", StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, branchLog, StringComparison.Ordinal);
        Assert.Contains(
            "https:///F/feed/auth/REDACTED/api?REDACTED",
            branchLog,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureLoggingAndTelemetryRemoveFragmentsFromEffectiveUrls()
    {
        const string Secret = "sup3rs3cret";
        using var failureScope = FeedFailureTelemetry.Scope();
        var messages = new List<string>();
        using var client = new HttpClient(new FailureHandler(RetryFailureMode.NonRetryableStatus));

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            $"https://private.example/v3/index.json#opaque-{Secret}",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        string branchLog = Assert.Single(messages, message =>
            message.Contains("(not retryable)", StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, branchLog, StringComparison.Ordinal);
        Assert.DoesNotContain('#', branchLog);

        var failure = Assert.Single(FeedFailureTelemetry.Current!.Failures);
        Assert.Equal("https://private.example/v3/index.json", failure.Url.ToString());
    }

    [Theory]
    [InlineData(RetryFailureMode.NonRetryableStatus, "F\\feed\\auth\\sup3rs3cret\\api", "https://base.example/root/F/feed/auth/sup3rs3cret/api", "https://base.example/root/F/feed/auth/REDACTED/api", "(not retryable)", false)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "F\\auth\\auth\\sup3rs3cret\\api", "https://base.example/root/F/auth/auth/sup3rs3cret/api", "https://base.example/root/F/auth/REDACTED/REDACTED/api", "(not retryable)", false)]
    [InlineData(RetryFailureMode.RetryableStatus, "auth/./sup3rs3cret/api", "https://base.example/root/auth/sup3rs3cret/api", "https://base.example/root/auth/REDACTED/api", "503 (retryable)", true)]
    [InlineData(RetryFailureMode.RetryableSocket, "auth/x/../sup3rs3cret/api", "https://base.example/root/auth/sup3rs3cret/api", "https://base.example/root/auth/REDACTED/api", "Socket error", true)]
    [InlineData(RetryFailureMode.Timeout, "auth\\.\\sup3rs3cret\\api", "https://base.example/root/auth/sup3rs3cret/api", "https://base.example/root/auth/REDACTED/api", "request timeout", true)]
    [InlineData(RetryFailureMode.NonRetryableStatus, "auth\\x\\..\\sup3rs3cret\\api", "https://base.example/root/auth/sup3rs3cret/api", "https://base.example/root/auth/REDACTED/api", "(not retryable)", false)]
    public async Task FailureLogsUseTheEffectiveUrlForRelativeRequests(
        RetryFailureMode mode,
        string url,
        string expectedTransportUrl,
        string expectedDisplayUrl,
        string branchMessage,
        bool exhaustsRetries)
    {
        var messages = new List<string>();
        using var handler = new FailureHandler(mode);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://base.example/root/")
        };

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            url,
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        Assert.Equal(expectedTransportUrl, handler.RequestUri?.ToString());
        string branchLog = Assert.Single(messages, message =>
            message.Contains(branchMessage, StringComparison.Ordinal));
        Assert.DoesNotContain("sup3rs3cret", branchLog, StringComparison.Ordinal);
        Assert.Contains(expectedDisplayUrl, branchLog, StringComparison.Ordinal);
        var exhaustedLogs = messages
            .Where(message => message.Contains("Max retries", StringComparison.Ordinal))
            .ToArray();
        if (exhaustsRetries)
        {
            string exhaustedLog = Assert.Single(exhaustedLogs);
            Assert.DoesNotContain("sup3rs3cret", exhaustedLog, StringComparison.Ordinal);
            Assert.Contains(expectedDisplayUrl, exhaustedLog, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(exhaustedLogs);
        }
    }

    [Fact]
    public async Task UnicodeWhitespaceDoesNotCreateNetworkPathAuthority()
    {
        var messages = new List<string>();
        using var handler = new CaptureStatusHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://base.example/root/")
        };

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "\u00A0//user@private.example/path",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        Assert.Equal("base.example", handler.RequestUri?.Host);
        string branchLog = Assert.Single(messages, message =>
            message.Contains("(not retryable)", StringComparison.Ordinal));
        Assert.Contains("user@private.example", branchLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExceptionMessagesCannotInjectUrlsIntoLogs()
    {
        var messages = new List<string>();
        using var client = new HttpClient(new FailureHandler(RetryFailureMode.Unsupported));

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://private.example/v3/index.json",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(content);
        Assert.Equal("HTTP GET unsupported URL (not retryable)", Assert.Single(messages));
        Assert.DoesNotContain("sup3rs3cret", Assert.Single(messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachRetryUsesItsOwnEffectiveUrlAndRequest()
    {
        var messages = new List<string>();
        using var handler = new RetryThenUnauthorizedHandler();
        using var client = new HttpClient(handler);
        var auth = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "credential");

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://original.example/v3/index.json",
            retryCount: 1,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken,
            auth: auth);

        Assert.Null(content);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotSame(handler.Requests[0], handler.Requests[1]);
        Assert.All(handler.AuthorizationParameters, value => Assert.Equal("credential", value));

        string firstLog = Assert.Single(messages, message =>
            message.Contains("503 (retryable)", StringComparison.Ordinal));
        Assert.Contains(
            "https://first.example/F/feed/auth/REDACTED/api",
            firstLog,
            StringComparison.Ordinal);
        string secondLog = Assert.Single(messages, message =>
            message.Contains("401 (not retryable)", StringComparison.Ordinal));
        Assert.Contains(
            "https://second.example/F/feed/auth/REDACTED/api",
            secondLog,
            StringComparison.Ordinal);
        Assert.DoesNotContain(messages, message =>
            message.Contains("sup3rs3cret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessfulResponseKeepsItsRequestUsable()
    {
        using var client = new HttpClient(new SuccessHandler());
        using var response = await HttpRetryHelper.GetWithRetryAsync(
            client,
            "https://private.example/v3/index.json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.NotNull(response.RequestMessage);
        response.RequestMessage.RequestUri = new Uri("https://after.example/");
        Assert.Equal("after.example", response.RequestMessage.RequestUri.Host);
    }

    [Fact]
    public async Task ContentReadRequestsUseTheVirtualHttpClientSendAsync()
    {
        using var client = new OverridingHttpClient();

        using var getResponse = await HttpRetryHelper.GetWithRetryAsync(
            client,
            "https://private.example/get",
            cancellationToken: TestContext.Current.CancellationToken);
        using var headResponse = await HttpRetryHelper.HeadWithRetryAsync(
            client,
            "https://private.example/head",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(getResponse);
        Assert.NotNull(headResponse);
        Assert.Equal(2, client.SendCount);
    }

    [Fact]
    public async Task AsynchronousVirtualFailureKeepsTheOriginalRelativeRequestUrl()
    {
        const string Secret = "sup3rs3cret";
        using var failureScope = FeedFailureTelemetry.Scope();
        var messages = new List<string>();
        using var client = new MutatingFaultHttpClient(Secret);

        var response = await HttpRetryHelper.GetWithRetryAsync(
            client,
            "relative/original",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(response);
        string errorLog = Assert.Single(messages, message =>
            message.Contains("(not retryable)", StringComparison.Ordinal));
        Assert.Contains("relative/original", errorLog, StringComparison.Ordinal);
        Assert.DoesNotContain("redirect.example", errorLog, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, errorLog, StringComparison.Ordinal);
        var failure = Assert.Single(FeedFailureTelemetry.Current!.Failures);
        Assert.Equal("relative/original", failure.Url.ToString());
    }

    [Fact]
    public async Task FailedResponseDisposesADistinctFinalRequest()
    {
        using var handler = new DistinctFailureRequestHandler();
        using var client = new HttpClient(handler);

        var response = await HttpRetryHelper.GetWithRetryAsync(
            client,
            "https://private.example/v3/index.json",
            retryCount: 0,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(response);
        Assert.NotNull(handler.FinalRequest);
        Assert.Throws<ObjectDisposedException>(() =>
            handler.FinalRequest.RequestUri = new Uri("https://after.example/"));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RedirectFailureUsesTheOriginalResolvedRequestUrl()
    {
        const string Secret = "sup3rs3cret";
        using var failureScope = FeedFailureTelemetry.Scope();
        using var redirectListener = new TcpListener(IPAddress.Loopback, 0);
        redirectListener.Start();
        int redirectPort = ((IPEndPoint)redirectListener.LocalEndpoint).Port;

        int closedPort;
        using (var closedListener = new TcpListener(IPAddress.Loopback, 0))
        {
            closedListener.Start();
            closedPort = ((IPEndPoint)closedListener.LocalEndpoint).Port;
        }

        string originalUrl = $"http://127.0.0.1:{redirectPort}/original";
        string redirectUrl = $"http://127.0.0.1:{closedPort}/auth/{Secret}";
        Task serverTask = ServeRedirectAsync(
            redirectListener,
            redirectUrl,
            TestContext.Current.CancellationToken);
        var messages = new List<string>();
        using var client = new HttpClient();

        var content = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            originalUrl,
            retryCount: 0,
            log: messages.Add,
            cancellationToken: TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Null(content);
        string errorLog = Assert.Single(messages, message =>
            message.Contains("(not retryable)", StringComparison.Ordinal));
        Assert.Contains(originalUrl, errorLog, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, errorLog, StringComparison.Ordinal);
        var failure = Assert.Single(FeedFailureTelemetry.Current!.Failures);
        Assert.Equal(originalUrl, failure.Url.ToString());
    }

    private static async Task ServeRedirectAsync(
        TcpListener listener,
        string redirectUrl,
        CancellationToken cancellationToken)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = connection.GetStream();
        using var reader = new StreamReader(
            stream,
            System.Text.Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        byte[] response = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 302 Found\r\nLocation: {redirectUrl}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
    }

    private static void AssertRedactedUrl(string message)
    {
        Assert.DoesNotContain("sup3rs3cret", message, StringComparison.Ordinal);
        Assert.Contains("private.example", message, StringComparison.Ordinal);
    }

    public enum RetryFailureMode
    {
        NonRetryableStatus,
        NonRetryableSocket,
        RetryableStatus,
        RetryableSocket,
        Timeout,
        Unsupported,
    }

    [Fact]
    public async Task GetWithRetryResultAsync_NonRangeRequestBuffersWithinRetryOperation()
    {
        using var client = new HttpClient(new ThrowingContentHandler());

        HttpRetryHelper.HttpRetryResult result =
            await HttpRetryHelper.GetWithRetryResultAsync(
                client,
                "https://feed.example/index.json",
                retryCount: 0,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetWithRetryResultAsync_RangeRequestReturnsAfterHeaders()
    {
        using var client = new HttpClient(new ThrowingContentHandler());

        HttpRetryHelper.HttpRetryResult result =
            await HttpRetryHelper.GetWithRetryResultAsync(
                client,
                "https://feed.example/package.nupkg",
                retryCount: 0,
                cancellationToken: TestContext.Current.CancellationToken,
                range: new RangeHeaderValue(0, 0));

        using HttpResponseMessage? response = result.Response;
        Assert.NotNull(response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadToFile_BoundsActualBytesWhenLengthIsMissingOrUnderReported(
        bool underReportLength)
    {
        string destination = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-bounded-download-{Guid.NewGuid():N}");
        using var client = new HttpClient(
            new DownloadHandler(
                new byte[17],
                underReportLength ? 1 : null));

        try
        {
            Assert.Equal(
                HttpRetryHelper.DownloadToFileResult.RejectedPayload,
                await HttpRetryHelper.DownloadToFileWithRetryAsync(
                    client,
                    "https://feed.example/package.nupkg",
                    destination,
                    retryCount: 0,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    maxDownloadedBytes: 16));

            Assert.False(File.Exists(destination));
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    [Fact]
    public async Task DownloadToFile_AcceptsExactlyTheByteLimit()
    {
        string destination = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-bounded-download-{Guid.NewGuid():N}");
        using var client = new HttpClient(
            new DownloadHandler(new byte[16], contentLength: null));

        try
        {
            Assert.Equal(
                HttpRetryHelper.DownloadToFileResult.Succeeded,
                await HttpRetryHelper.DownloadToFileWithRetryAsync(
                    client,
                    "https://feed.example/package.nupkg",
                    destination,
                    retryCount: 0,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    maxDownloadedBytes: 16));
            Assert.Equal(16, new FileInfo(destination).Length);
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    [Fact]
    public async Task DownloadToFile_RejectsAdvertisedOversizeAsTypedResult()
    {
        string destination = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-oversize-download-{Guid.NewGuid():N}");
        using var client = new HttpClient(
            new DownloadHandler(new byte[1], contentLength: 600_000_000));

        try
        {
            Assert.Equal(
                HttpRetryHelper.DownloadToFileResult.RejectedPayload,
                await HttpRetryHelper.DownloadToFileWithRetryAsync(
                    client,
                    "https://feed.example/package.nupkg",
                    destination,
                    retryCount: 0,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    maxDownloadedBytes: 16));
            Assert.False(File.Exists(destination));
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task HeaderFirstBodyRead_TimesOutAndRetriesAStalledBody()
    {
        var handler = new StallingBodyHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(25),
        };

        HttpRetryHelper.HttpBodyFetchResult result =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                client,
                "https://example.test/source.cs",
                static _ => true,
                retryCount: 1,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpRetryHelper.HttpBodyFetchStatus.Unavailable, result.Status);
        Assert.Null(result.Bytes);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task HeaderFirstBodyRead_CapsAChunkedBodyByDecodedBytes()
    {
        using var client = new HttpClient(new ByteHandler("123456789"u8.ToArray()));

        HttpRetryHelper.HttpBodyFetchResult result =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                client,
                "https://example.test/source.cs",
                static _ => true,
                maxDownloadSize: 8,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpRetryHelper.HttpBodyFetchStatus.TooLarge, result.Status);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task GetStringWithRetryAsync_CapsBodyAndReturnsNullWhenOversize()
    {
        byte[] body = Encoding.UTF8.GetBytes(new string('x', 64));
        using var client = new HttpClient(new ByteHandler(body));
        var messages = new List<string>();
        using var telemetry = FeedFailureTelemetry.Scope();

        string? text = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://example.test/index.json",
            log: messages.Add,
            maxDownloadSize: 32,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(text);
        Assert.Contains(
            messages,
            message => message.Contains("byte cap", StringComparison.Ordinal));
        // Oversize must count as a feed failure (not silent Absent).
        Assert.NotEmpty(FeedFailureTelemetry.Current!.Failures);
    }

    [Fact]
    public async Task GetStringWithRetryAsync_ReturnsUtf8BodyWhenUnderCap()
    {
        using var client = new HttpClient(new ByteHandler("{\"ok\":true}"u8.ToArray()));

        string? text = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://example.test/index.json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("{\"ok\":true}", text);
    }

    [Fact]
    public async Task GetStringWithRetryAsync_TimesOutAStalledBodyAfterHeaders()
    {
        var handler = new StallingBodyHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var messages = new List<string>();
        using var overall = new CancellationTokenSource();

        Task<string?> fetch = HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://example.test/index.json",
            retryCount: 0,
            log: messages.Add,
            cancellationToken: overall.Token);
        await handler.BodyReadStarted.WaitAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        overall.CancelAfter(TimeSpan.FromSeconds(30));

        string? text = await fetch;

        Assert.Null(text);
        Assert.False(overall.IsCancellationRequested);
        Assert.Contains(
            messages,
            message => message.Contains("body timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStringWithRetryAsync_MidBodyIo_ReturnsNullAndRecordsFailure()
    {
        using var telemetry = FeedFailureTelemetry.Scope();
        using var client = new HttpClient(new RetryingBodyHandler("unused"u8.ToArray()));
        var messages = new List<string>();

        // retryCount 0: headers succeed once; body stream throws IOException.
        string? text = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://example.test/index.json",
            retryCount: 0,
            log: messages.Add,
            trafficKind: NetworkTrafficKind.PackageVersionList,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(text);
        Assert.Contains(
            messages,
            message => message.Contains("body failed", StringComparison.Ordinal));
        FeedFailure failure = Assert.Single(FeedFailureTelemetry.Current!.Failures);
        Assert.Equal(NetworkTrafficKind.PackageVersionList, failure.Phase);
    }

    [Fact]
    public async Task GetStringWithRetryAsync_BodyFailure_UsesCallerTrafficKindNotOuterScope()
    {
        using var telemetry = FeedFailureTelemetry.Scope();
        using var client = new HttpClient(new RetryingBodyHandler("unused"u8.ToArray()));
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch))
        {
            string? text = await HttpRetryHelper.GetStringWithRetryAsync(
                client,
                "https://example.test/index.json",
                retryCount: 0,
                trafficKind: NetworkTrafficKind.PackageVersionList,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(text);
        }

        FeedFailure failure = Assert.Single(FeedFailureTelemetry.Current!.Failures);
        Assert.Equal(NetworkTrafficKind.PackageVersionList, failure.Phase);
        Assert.NotEqual(NetworkTrafficKind.PackageSearch, failure.Phase);
    }

    [Fact]
    public async Task HeaderFirstBodyRead_RetriesAMidBodyIoFailure()
    {
        var handler = new RetryingBodyHandler("complete"u8.ToArray());
        using var client = new HttpClient(handler);

        HttpRetryHelper.HttpBodyFetchResult result =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                client,
                "https://example.test/source.cs",
                static _ => true,
                retryCount: 1,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpRetryHelper.HttpBodyFetchStatus.Success, result.Status);
        Assert.Equal("complete"u8.ToArray(), result.Bytes);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task HeaderFirstBodyRead_RequiresBrowserStreamingResponse()
    {
        var handler = new BrowserStreamingOptionHandler();
        using var client = new HttpClient(handler);

        HttpRetryHelper.HttpBodyFetchResult result =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                client,
                "https://example.test/source.cs",
                static _ => true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpRetryHelper.HttpBodyFetchStatus.Success, result.Status);
        Assert.True(handler.StreamingRequested);
    }

    [Fact]
    public async Task StringBodyRead_RequiresBrowserStreamingResponse()
    {
        var handler = new BrowserStreamingOptionHandler();
        using var client = new HttpClient(handler);

        string? result = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://example.test/index.json",
            cancellationToken: TestContext.Current.CancellationToken,
            configureRequest: static request =>
                request.Options.Set(
                    new HttpRequestOptionsKey<bool>(
                        "WebAssemblyEnableStreamingResponse"),
                    false));

        Assert.Equal("source", result);
        Assert.True(handler.StreamingRequested);
    }

    [Fact]
    public async Task StreamedResponse_RequiresBrowserStreamingResponse()
    {
        var handler = new BrowserStreamingOptionHandler();
        using var client = new HttpClient(handler);

        using HttpResponseMessage? response =
            await HttpRetryHelper.GetStreamedWithRetryAsync(
                client,
                "https://example.test/package.nupkg",
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.True(handler.StreamingRequested);
    }

    [Fact]
    public async Task RangeResponse_RequiresBrowserStreamingResponse()
    {
        var handler = new BrowserStreamingOptionHandler();
        using var client = new HttpClient(handler);

        HttpRetryHelper.HttpRetryResult result =
            await HttpRetryHelper.GetWithRetryResultAsync(
                client,
                "https://example.test/package.nupkg",
                cancellationToken: TestContext.Current.CancellationToken,
                range: new RangeHeaderValue(0, 0));
        using HttpResponseMessage? response = result.Response;

        Assert.NotNull(response);
        Assert.True(handler.StreamingRequested);
    }

    [Theory]
    [InlineData(RetryFailureMode.NonRetryableStatus)]
    [InlineData(RetryFailureMode.NonRetryableSocket)]
    [InlineData(RetryFailureMode.RetryableStatus)]
    [InlineData(RetryFailureMode.RetryableSocket)]
    [InlineData(RetryFailureMode.Timeout)]
    [InlineData(RetryFailureMode.Unsupported)]
    public async Task HeaderFirstBodyRead_FailureLogsCarryNoUrlOrExceptionText(
        RetryFailureMode mode)
    {
        const string Secret = "sup3rs3cret";
        var messages = new List<string>();
        using var client = new HttpClient(new FailureHandler(mode));

        HttpRetryHelper.HttpBodyFetchResult result =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                client,
                $"https://user:{Secret}@private.example/F/auth/{Secret}/source.cs?sig={Secret}#{Secret}",
                static _ => true,
                retryCount: 0,
                log: messages.Add,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpRetryHelper.HttpBodyFetchStatus.Unavailable, result.Status);
        Assert.NotEmpty(messages);
        Assert.DoesNotContain(messages, message =>
            message.Contains(Secret, StringComparison.Ordinal)
            || message.Contains("private.example", StringComparison.Ordinal));
    }

    private sealed class FailureHandler(RetryFailureMode mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return mode switch
            {
                RetryFailureMode.NonRetryableStatus =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                RetryFailureMode.NonRetryableSocket =>
                    Task.FromException<HttpResponseMessage>(new HttpRequestException(
                        "Failed https://user:sup3rs3cret@private.example/v3/index.json",
                        new SocketException((int)SocketError.HostNotFound))),
                RetryFailureMode.RetryableStatus =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                RetryFailureMode.RetryableSocket =>
                    Task.FromException<HttpResponseMessage>(new HttpRequestException(
                        "Connection reset at https://user:sup3rs3cret@private.example/",
                        new SocketException((int)SocketError.ConnectionReset))),
                RetryFailureMode.Timeout =>
                    Task.FromException<HttpResponseMessage>(new TaskCanceledException()),
                RetryFailureMode.Unsupported =>
                    Task.FromException<HttpResponseMessage>(new NotSupportedException(
                        "Unsupported https://user:sup3rs3cret@private.example/")),
                _ => throw new InvalidOperationException($"Unexpected failure mode: {mode}"),
            };
        }

        public Uri? RequestUri { get; private set; }
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

    private sealed class OverridingHttpClient()
        : HttpClient(new UnexpectedTransportHandler())
    {
        public int SendCount { get; private set; }

        public override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
                RequestMessage = request,
            });
        }
    }

    private sealed class UnexpectedTransportHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The virtual HttpClient override was bypassed.");
    }

    private sealed class MutatingFaultHttpClient(string secret)
        : HttpClient(new UnexpectedTransportHandler())
    {
        public override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            request.RequestUri = new Uri($"https://redirect.example/auth/{secret}");
            throw new HttpRequestException(
                "Host not found",
                new SocketException((int)SocketError.HostNotFound));
        }
    }

    private sealed class DistinctFailureRequestHandler : HttpMessageHandler
    {
        public HttpRequestMessage? FinalRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            FinalRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = FinalRequest,
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                FinalRequest?.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class CaptureStatusHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
            });
        }
    }

    private sealed class RetryThenUnauthorizedHandler : HttpMessageHandler
    {
        private int _attempt;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            _attempt++;
            request.RequestUri = new Uri(_attempt == 1
                ? "https://first.example/F/feed/auth/sup3rs3cret/api"
                : "https://second.example/F/feed/auth/sup3rs3cret/api");
            return Task.FromResult(new HttpResponseMessage(
                _attempt == 1
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
            });
        }
    }

    private sealed class ThrowingContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent(),
            });
    }

    private sealed class DownloadHandler(
        byte[] content,
        long? contentLength)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseContent = new UnknownLengthContent(content);
            responseContent.Headers.ContentLength = contentLength;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = responseContent,
                });
        }
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => stream.WriteAsync(content).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(
                new MemoryStream(content, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => Task.FromException(new HttpRequestException("Broken response body."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    sealed class BrowserStreamingOptionHandler : HttpMessageHandler
    {
        static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
            new("WebAssemblyEnableStreamingResponse");

        public bool StreamingRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            StreamingRequested = request.Options.TryGetValue(
                BrowserStreamingResponse,
                out bool enabled)
                && enabled;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("source"u8.ToArray()),
                RequestMessage = request,
            });
        }
    }

    sealed class StallingBodyHandler : HttpMessageHandler
    {
        int _requestCount;
        readonly TaskCompletionSource _bodyReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => Volatile.Read(ref _requestCount);
        public Task BodyReadStarted => _bodyReadStarted.Task;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream(_bodyReadStarted)),
                RequestMessage = request,
            });
        }
    }

    sealed class ByteHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableStream(body)),
                RequestMessage = request,
            });
    }

    sealed class RetryingBodyHandler(byte[] successfulBody) : HttpMessageHandler
    {
        int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    attempt == 1
                        ? new FailingBodyStream()
                        : new NonSeekableStream(successfulBody)),
                RequestMessage = request,
            });
        }
    }

    sealed class StallingStream(TaskCompletionSource bodyReadStarted) : Stream
    {
        bool _sentByte;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_sentByte)
            {
                _sentByte = true;
                buffer.Span[0] = (byte)'x';
                return 1;
            }

            bodyReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    sealed class FailingBodyStream : Stream
    {
        bool _sentByte;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_sentByte)
                throw new IOException("mid-body failure");

            _sentByte = true;
            buffer.Span[0] = (byte)'x';
            return ValueTask.FromResult(1);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    sealed class NonSeekableStream(byte[] body) : Stream
    {
        int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, body.Length - _position);
            if (count == 0)
                return ValueTask.FromResult(0);

            body.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
