using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotnetInspect.Cli.Tests;

public partial class HttpClientFactoryTests
{
    private const int AbandonedBodyLength = 900_000;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProductionPackageTransportReusesOnlyFullyConsumedResponses(
        bool gallery,
        bool consume)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/payload");
        using var serverCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int connections = 0;
        var workers = new List<Task>();
        byte[] body = new byte[AbandonedBodyLength];
        Task server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    TcpClient connection = await listener.AcceptTcpClientAsync(serverCancellation.Token);
                    Interlocked.Increment(ref connections);
                    workers.Add(Task.Run(async () =>
                    {
                        using (connection)
                        {
                            await using NetworkStream stream = connection.GetStream();
                            try
                            {
                                while (await ReadRequestLineAsync(stream, serverCancellation.Token) is not null)
                                {
                                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                                        "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                                        + $"Content-Length: {body.Length}\r\n\r\n"), serverCancellation.Token);
                                    // Send the body immediately: abandonment must work even when
                                    // reads complete from buffered data, without a pending read.
                                    await stream.WriteAsync(body, serverCancellation.Token);
                                }
                            }
                            catch (Exception exception) when (
                                exception is IOException or SocketException or OperationCanceledException)
                            {
                            }
                        }
                    }, CancellationToken.None));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
        try
        {
            using HttpMessageHandler transport = gallery
                ? DotnetInspector.Networking.HttpClientFactory.CreateCredentialFreeGalleryHandler(endpoint.AbsoluteUri)
                : DotnetInspector.Networking.HttpClientFactory.CreateCredentialFreePackageSourceHandler(endpoint.AbsoluteUri);
            using var client = new HttpClient(transport);
            using (HttpResponseMessage first = await client.GetAsync(
                endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (consume)
                    await first.Content.CopyToAsync(Stream.Null, cancellationToken);
                else
                    await Task.Delay(100, cancellationToken);
            }
            using HttpResponseMessage second = await client.GetAsync(
                endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await second.Content.CopyToAsync(Stream.Null, cancellationToken);
            Assert.Equal(consume ? 1 : 2, Volatile.Read(ref connections));
        }
        finally
        {
            await serverCancellation.CancelAsync();
            listener.Stop();
            await server;
            await Task.WhenAll(workers);
        }
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
