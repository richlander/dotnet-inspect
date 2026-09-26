using System.Net;
using System.Net.Sockets;
using System.Text;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed partial class PackageSourceClientTests
{
    private const int AbandonedBodyLength = 900_000;

    /// <summary>
    /// An abandoned package response ends its transfer. The body is under the
    /// HTTP handler's 1 MiB response drain limit, so disposing the response
    /// unread lets the handler read the whole body to reuse the connection;
    /// abandoning it cancels a pending read instead, which closes the
    /// connection after at most what was already buffered. The source writes
    /// slowly into a small send buffer, so what it manages to send bounds
    /// what the client received.
    /// </summary>
    [Fact]
    public async Task AbandonedPayloadEndsItsTransferInsteadOfDraining()
    {
        long abandoned = await ServePayloadAsync(
            static payload => payload.AbandonAsync());
        long disposed = await ServePayloadAsync(
            static payload => payload.Content.DisposeAsync());

        Assert.Equal(AbandonedBodyLength, disposed);
        Assert.True(
            abandoned <= 128 * 1024,
            $"the abandoned response sent {abandoned} of {AbandonedBodyLength} body bytes");
    }

    private static async Task<long> ServePayloadAsync(
        Func<PackageSourcePayload, ValueTask> release)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string baseAddress = $"http://127.0.0.1:{port}/flat/";
        long sent = 0;
        var bodyDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serverCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task server = Task.Run(async () =>
        {
            while (!serverCancellation.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await listener.AcceptTcpClientAsync(serverCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    using (connection)
                    {
                        connection.SendBufferSize = 8192;
                        await using NetworkStream stream = connection.GetStream();
                        string? requestLine = await ReadRequestLineAsync(stream, serverCancellation.Token);
                        if (requestLine is null)
                            return;
                        if (requestLine.Contains("/index.json", StringComparison.Ordinal))
                        {
                            byte[] index = Encoding.UTF8.GetBytes($$"""
                                {"version":"3.0.0","resources":[
                                  {"@id":"{{baseAddress}}","@type":"PackageBaseAddress/3.0.0"}
                                ]}
                                """);
                            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                                + $"Content-Length: {index.Length}\r\nConnection: close\r\n\r\n"),
                                serverCancellation.Token);
                            await stream.WriteAsync(index, serverCancellation.Token);
                            return;
                        }

                        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                            + $"Content-Length: {AbandonedBodyLength}\r\n\r\n"),
                            serverCancellation.Token);
                        var chunk = new byte[4096];
                        int remaining = AbandonedBodyLength;
                        try
                        {
                            while (remaining > 0)
                            {
                                int count = Math.Min(chunk.Length, remaining);
                                await stream.WriteAsync(chunk.AsMemory(0, count), serverCancellation.Token);
                                Interlocked.Add(ref sent, count);
                                remaining -= count;
                                await Task.Delay(1, serverCancellation.Token);
                            }
                        }
                        catch (Exception exception) when (
                            exception is IOException or SocketException or OperationCanceledException)
                        {
                        }
                        finally
                        {
                            bodyDone.TrySetResult();
                        }
                    }
                }, CancellationToken.None);
            }
        }, CancellationToken.None);

        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(
            new PackageSource("loopback", $"http://127.0.0.1:{port}/index.json"));
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync("contoso", "1.0.0", cancellationToken));
        Assert.Equal(AbandonedBodyLength, payload.AdvertisedLength);
        await release(payload);

        await bodyDone.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await serverCancellation.CancelAsync();
        listener.Stop();
        await server;
        return Interlocked.Read(ref sent);
    }

    private static async Task<string?> ReadRequestLineAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var request = new byte[8192];
        int length = 0;
        while (length < request.Length)
        {
            int read = await stream.ReadAsync(request.AsMemory(length), cancellationToken);
            if (read == 0)
                return null;
            length += read;
            if (request.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }
        return Encoding.ASCII.GetString(request, 0, length).Split("\r\n")[0];
    }
}
