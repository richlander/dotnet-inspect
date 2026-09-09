using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace DotnetInspector.Tests;

/// <summary>
/// End-to-end cover for the configured request timeout, against a stub feed that answers the
/// service index and then never answers the search query.
/// </summary>
/// <remarks>
/// <para>
/// This is the only cover for the wiring itself. The unit tests prove that
/// <c>HttpClientFactory</c> applies its configured default, and the option tests prove that the
/// CLI parses and strips the flag, but neither would notice if the parsed value were dropped on the
/// way into <c>Initialize</c>. Here the resolved value reaches the NuGet request deadline and
/// comes back out in the timeout message, so the assertion reads the number the caller asked for.
/// </para>
/// <para>
/// The stub has to answer the service index rather than refuse the connection. A dead index
/// fails earlier, in the branch that reports no searchable endpoint, and never reaches the
/// search query where the timeout applies.
/// </para>
/// <para>
/// Nothing here asserts on elapsed time. The stub never answers, so the request always outlasts
/// the timeout regardless of how loaded the machine is.
/// </para>
/// </remarks>
[Collection("Console")]
public sealed class HttpTimeoutEndToEndTests : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly int _port;

    public HttpTimeoutEndToEndTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(() => ServeAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener.Stop();
        _shutdown.Dispose();
    }

    /// <summary>
    /// The flag reaches the client, and the message names the number that was asked for.
    /// </summary>
    /// <remarks>
    /// Both spellings run here rather than only in the option tests. Those assert that the
    /// process succeeds, which a mutation dropping the <c>=</c> branch would survive: the token
    /// then reaches the parser, where it is a registered root option and parses cleanly while
    /// changing nothing. Only asserting the value took effect distinguishes the two.
    /// </remarks>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("--http-timeout", "3")]
    [InlineData("--http-timeout=3", null)]
    [InlineData("--http-timeout:3", null)]
    public void HttpTimeout_FlagGovernsTheSearchRequest(string flag, string? value)
    {
        string[] args = value is null ? [flag] : [flag, value];

        string error = RunSearch(args, environmentValue: null);

        Assert.Contains(3, TimeoutSeconds(error));
    }

    /// <summary>
    /// The variable still works for callers that never pass a flag.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void HttpTimeout_EnvironmentVariableGovernsTheSearchRequest()
    {
        string error = RunSearch([], environmentValue: "4");

        Assert.Contains(4, TimeoutSeconds(error));
    }

    /// <summary>
    /// A value above the default extends the request instead of being clamped back to 30 seconds.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void HttpTimeout_AboveDefaultExtendsTheSearchRequest()
    {
        string error = RunSearch(
            ["--http-timeout", "31"],
            environmentValue: null,
            stallServiceIndex: true);

        Assert.Contains(31, TimeoutSeconds(error));
        Assert.DoesNotContain(30, TimeoutSeconds(error));
    }

    /// <summary>
    /// The flag outranks the variable, so a stale export in a shell profile cannot quietly
    /// override what the operator typed.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void HttpTimeout_FlagOutranksTheEnvironmentVariable()
    {
        IReadOnlyList<int> seconds = TimeoutSeconds(RunSearch(["--http-timeout", "2"], environmentValue: "9"));

        Assert.Contains(2, seconds);
        Assert.DoesNotContain(9, seconds);
    }

    [Theory]
    [InlineData("explicit: net_http_request_timedout, 7")]
    [InlineData("explicit: The request was canceled due to the configured HttpClient.Timeout of 7 seconds elapsing.")]
    [InlineData("explicit: NuGet request did not complete within 00:00:07.")]
    public void TimeoutSeconds_RecognizesOwnedAndRuntimeMessageShapes(string error)
    {
        Assert.Contains(7, TimeoutSeconds(error));
    }

    /// <summary>
    /// Reads the numbers out of the timed-out clause of the error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Numbers rather than substrings, because the default is 30 and a substring check for "3"
    /// passes against it. A mutation that dropped the parsed flag on the way into the factory
    /// went undetected until this compared integers.
    /// </para>
    /// <para>
    /// The clause is isolated first so the port in the stub feed's URL cannot supply a digit.
    /// NuGetFetch emits the owned request-deadline message. Older paths may still expose the
    /// NativeAOT resource key or CoreCLR property name, so all three stable markers can locate
    /// the clause. Every number in the clause is returned rather than one at a fixed offset
    /// because the runtime wording is not this repository's to pin.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<int> TimeoutSeconds(string error)
    {
        int start = error.IndexOf("timedout", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            start = error.IndexOf("HttpClient.Timeout", StringComparison.Ordinal);
        if (start < 0)
            start = error.IndexOf(
                "NuGet request did not complete within",
                StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a timeout in the error, got: {error}");

        int end = error.IndexOf('\n', start);
        string clause = end < 0 ? error[start..] : error[start..end];

        var numbers = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(clause, @"\d+"))
        {
            if (int.TryParse(match.Value, out int value))
                numbers.Add(value);
        }

        Assert.NotEmpty(numbers);
        return numbers;
    }

    private string RunSearch(
        string[] leadingArgs,
        string? environmentValue,
        bool stallServiceIndex = false)
    {
        string executable = Path.Combine(
            Path.GetDirectoryName(ProductAssemblyPath())!,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in leadingArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.ArgumentList.Add("package");
        psi.ArgumentList.Add("search");
        psi.ArgumentList.Add("dotnet-inspect-timeout-probe");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(
            $"http://127.0.0.1:{_port}/"
            + (stallServiceIndex ? "slow-index.json" : "index.json"));

        // Explicitly cleared, not merely unset: an ambient value on the developer's machine
        // would otherwise decide the result of the cases that pass none.
        psi.Environment[HttpTimeoutConfiguration.EnvironmentVariable] = environmentValue ?? "";

        // Same reasoning, different variable. An ambient DOTNET_INSPECT_OFFLINE=1 makes every
        // request throw before it can reach the stub feed, so these cases fail somewhere
        // that has nothing to do with timeouts.
        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(10));
            throw new TimeoutException($"{executable} did not exit.");
        }

        Task.WaitAll([stdout, stderr], 10_000);
        return stderr.Result;
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => RespondAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task RespondAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer, cancellationToken);
                string request = Encoding.ASCII.GetString(buffer, 0, read);
                string path = request.Split(' ').Skip(1).FirstOrDefault() ?? string.Empty;

                if (path.StartsWith("/slow-index.json", StringComparison.Ordinal))
                {
                    // Send service-index headers and a partial body, then stall. The
                    // above-default test proves discovery does not restore the old
                    // 30-second package-helper body clamp.
                    byte[] indexHead = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        + "Content-Length: 1024\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(indexHead, cancellationToken);
                    await stream.WriteAsync(
                        Encoding.UTF8.GetBytes("{"),
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                if (!path.StartsWith("/index.json", StringComparison.Ordinal))
                {
                    // The search query. Send headers and a partial body, then stall so this
                    // reaches the body phase that historically clamped values above 30 seconds.
                    byte[] searchHead = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        + "Content-Length: 1024\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(searchHead, cancellationToken);
                    await stream.WriteAsync(
                        Encoding.UTF8.GetBytes("{"),
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                string body =
                    $$"""
                    {"version":"3.0.0","resources":[{"@id":"http://127.0.0.1:{{_port}}/query","@type":"SearchQueryService"}]}
                    """;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                byte[] head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                    + $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(head, cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (IOException)
        {
            // The client gave up first, which is the point of the stalling branch.
        }
    }

    private static string ProductAssemblyPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        if (File.Exists(path))
        {
            return path;
        }

        var located = Assembly.Load("dotnet-inspect").Location;
        return string.IsNullOrEmpty(located)
            ? throw new FileNotFoundException("Could not locate the dotnet-inspect product assembly.")
            : located;
    }
}
