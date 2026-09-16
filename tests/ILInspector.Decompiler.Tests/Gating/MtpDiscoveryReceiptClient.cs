using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests.Gating;

internal sealed record DiscoveryReceiptExpansion(
    string? Path,
    IReadOnlyList<string> Args,
    string? Error);

internal static class MtpDiscoveryReceiptClient
{
    internal const string ReceiptArgument = "--gate-discovery-receipt";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    internal static DiscoveryReceiptExpansion RemoveReceiptArgument(
        IReadOnlyList<string> args)
    {
        int argumentIndex = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (!string.Equals(
                args[i],
                ReceiptArgument,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (argumentIndex >= 0)
            {
                return new(
                    null,
                    [],
                    $"error: {ReceiptArgument} may be specified only once.");
            }

            argumentIndex = i;
        }

        if (argumentIndex < 0)
        {
            return new(null, args, null);
        }

        if (argumentIndex + 1 >= args.Count
            || string.IsNullOrWhiteSpace(args[argumentIndex + 1]))
        {
            return new(
                null,
                [],
                $"error: {ReceiptArgument} requires an output path.");
        }

        var remaining = new List<string>(args.Count - 2);
        for (int i = 0; i < args.Count; i++)
        {
            if (i != argumentIndex && i != argumentIndex + 1)
            {
                remaining.Add(args[i]);
            }
        }

        return new(args[argumentIndex + 1], remaining, null);
    }

    internal static async Task<int> CaptureAsync(
        string receiptPath,
        IReadOnlyList<string> testArguments)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        string temporaryPath =
            $"{Path.GetFullPath(receiptPath)}.{Guid.NewGuid():N}.tmp";
        MtpGateReceiptWriter.Initialize(temporaryPath);

        try
        {
            int count = await CaptureCoreAsync(
                temporaryPath,
                testArguments,
                timeout.Token);
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "MTP discovered zero tests for the requested gate.");
            }

            File.Move(temporaryPath, receiptPath, overwrite: true);
            Console.Out.WriteLine(
                $"MTP discovery receipt: {count} test case(s) -> {receiptPath}");
            return 0;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<int> CaptureCoreAsync(
        string receiptPath,
        IReadOnlyList<string> testArguments,
        CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using Process process = StartServer(port, testArguments);
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            using TcpClient client =
                await listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            var protocol = new JsonRpcProtocol(stream);

            await protocol.WriteInitializeAsync(
                Environment.ProcessId,
                cancellationToken);
            await AwaitResponseAsync(
                protocol,
                expectedId: 1,
                cancellationToken);

            Guid runId = Guid.NewGuid();
            await protocol.WriteDiscoverAsync(runId, cancellationToken);

            bool responseReceived = false;
            bool completionReceived = false;
            int discoveredCount = 0;
            while (!responseReceived || !completionReceived)
            {
                using JsonDocument message =
                    await protocol.ReadAsync(cancellationToken);
                JsonElement root = message.RootElement;
                if (IsResponse(root, expectedId: 2))
                {
                    ThrowIfError(root, "MTP discovery");
                    responseReceived = true;
                }

                if (!IsMethod(root, "testing/testUpdates/tests")
                    || !root.TryGetProperty("params", out JsonElement parameters))
                {
                    continue;
                }

                string? messageRunId = JsonString(parameters, "runId");
                if (!Guid.TryParse(messageRunId, out Guid actualRunId)
                    || actualRunId != runId)
                {
                    throw new InvalidDataException(
                        "MTP returned a discovery update for an unexpected run.");
                }

                if (!parameters.TryGetProperty(
                    "changes",
                    out JsonElement changes)
                    || changes.ValueKind == JsonValueKind.Null)
                {
                    completionReceived = true;
                    continue;
                }

                if (changes.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        "MTP returned a malformed discovery update.");
                }

                foreach (JsonElement change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("node", out JsonElement node))
                    {
                        throw new InvalidDataException(
                            "MTP returned a discovery change without a test node.");
                    }

                    string? nodeType = JsonString(node, "node-type");
                    if (nodeType != "action")
                    {
                        continue;
                    }

                    string uid = RequiredJsonString(node, "uid");
                    string displayName = RequiredJsonString(
                        node,
                        "display-name");
                    string className = RequiredJsonString(
                        node,
                        "location.type");
                    string methodSignature = RequiredJsonString(
                        node,
                        "location.method");
                    int parameterList = methodSignature.IndexOf('(');
                    string methodName = parameterList < 0
                        ? methodSignature
                        : methodSignature[..parameterList];
                    string state = RequiredJsonString(
                        node,
                        "execution-state");
                    if (state != "discovered")
                    {
                        throw new InvalidDataException(
                            $"MTP discovery returned unexpected state '{state}'.");
                    }

                    MtpGateReceiptWriter.Write(
                        receiptPath,
                        uid,
                        displayName,
                        className,
                        methodName,
                        methodSignature,
                        state);
                    discoveredCount++;
                }
            }

            await protocol.WriteExitAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string output = await outputTask;
            string error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"MTP discovery server exited {process.ExitCode}."
                    + FormatProcessOutput(output, error));
            }

            return discoveredCount;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static Process StartServer(
        int port,
        IReadOnlyList<string> testArguments)
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The current process path is unavailable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        }

        foreach (string argument in testArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--server");
        startInfo.ArgumentList.Add("jsonrpc");
        startInfo.ArgumentList.Add("--client-port");
        startInfo.ArgumentList.Add(port.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.Environment.Remove(
            MtpGateReceiptConsumer.ReceiptPathEnvironmentVariable);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the MTP discovery server.");
    }

    private static async Task AwaitResponseAsync(
        JsonRpcProtocol protocol,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using JsonDocument message =
                await protocol.ReadAsync(cancellationToken);
            JsonElement root = message.RootElement;
            if (IsResponse(root, expectedId))
            {
                ThrowIfError(root, "MTP initialization");
                return;
            }
        }
    }

    private static bool IsResponse(JsonElement root, int expectedId) =>
        root.TryGetProperty("id", out JsonElement id)
        && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt32(out int actualId)
        && actualId == expectedId;

    private static bool IsMethod(JsonElement root, string expected) =>
        root.TryGetProperty("method", out JsonElement method)
        && method.ValueKind == JsonValueKind.String
        && method.GetString() == expected;

    private static void ThrowIfError(JsonElement root, string operation)
    {
        if (!root.TryGetProperty("error", out JsonElement error))
        {
            return;
        }

        string detail = JsonString(error, "message")
            ?? error.GetRawText();
        throw new InvalidOperationException($"{operation} failed: {detail}");
    }

    private static string RequiredJsonString(
        JsonElement element,
        string propertyName) =>
        JsonString(element, propertyName)
        ?? throw new InvalidDataException(
            $"MTP test node omitted required '{propertyName}'.");

    private static string? JsonString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string FormatProcessOutput(string output, string error)
    {
        string combined = string.Join(
            Environment.NewLine,
            new[] { output.Trim(), error.Trim() }
                .Where(text => text.Length > 0));
        return combined.Length == 0
            ? string.Empty
            : Environment.NewLine + combined;
    }

    private sealed class JsonRpcProtocol(NetworkStream stream)
    {
        private static readonly byte[] ContentTypeHeader =
            "Content-Type: application/testingplatform\r\n\r\n"u8.ToArray();

        public Task WriteInitializeAsync(
            int processId,
            CancellationToken cancellationToken) =>
            WriteAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteNumber("id", 1);
                    writer.WriteString("method", "initialize");
                    writer.WriteStartObject("params");
                    writer.WriteNumber("processId", processId);
                    writer.WriteStartObject("clientInfo");
                    writer.WriteString("name", "dotnet-inspect-decompiler-gate");
                    writer.WriteString("version", "1.0.0");
                    writer.WriteEndObject();
                    writer.WriteStartObject("capabilities");
                    writer.WriteStartObject("testing");
                    writer.WriteBoolean("debuggerProvider", false);
                    writer.WriteBoolean("batchLoggingSupport", false);
                    writer.WriteBoolean("attachmentsSupport", false);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                },
                cancellationToken);

        public Task WriteDiscoverAsync(
            Guid runId,
            CancellationToken cancellationToken) =>
            WriteAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteNumber("id", 2);
                    writer.WriteString("method", "testing/discoverTests");
                    writer.WriteStartObject("params");
                    writer.WriteString("runId", runId);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                },
                cancellationToken);

        public Task WriteExitAsync(CancellationToken cancellationToken) =>
            WriteAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteString("method", "exit");
                    writer.WriteStartObject("params");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                },
                cancellationToken);

        public async Task<JsonDocument> ReadAsync(
            CancellationToken cancellationToken)
        {
            int contentLength = -1;
            while (true)
            {
                string line = await ReadLineAsync(cancellationToken);
                if (line.Length == 0)
                {
                    break;
                }

                const string Header = "Content-Length:";
                if (line.StartsWith(
                    Header,
                    StringComparison.OrdinalIgnoreCase)
                    && !int.TryParse(
                        line.AsSpan(Header.Length).Trim(),
                        out contentLength))
                {
                    throw new InvalidDataException(
                        "MTP returned an invalid Content-Length header.");
                }
            }

            if (contentLength < 0)
            {
                throw new InvalidDataException(
                    "MTP response omitted the Content-Length header.");
            }

            byte[] content = new byte[contentLength];
            await stream.ReadExactlyAsync(content, cancellationToken);
            return JsonDocument.Parse(content);
        }

        private async Task WriteAsync(
            Action<Utf8JsonWriter> write,
            CancellationToken cancellationToken)
        {
            using var content = new MemoryStream();
            using (var writer = new Utf8JsonWriter(content))
            {
                write(writer);
            }

            byte[] lengthHeader = Encoding.ASCII.GetBytes(
                $"Content-Length: {content.Length}\r\n");
            await stream.WriteAsync(lengthHeader, cancellationToken);
            await stream.WriteAsync(ContentTypeHeader, cancellationToken);
            content.Position = 0;
            await content.CopyToAsync(stream, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private async Task<string> ReadLineAsync(
            CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            byte[] one = new byte[1];
            while (true)
            {
                int read = await stream.ReadAsync(one, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "MTP closed the JSON-RPC connection unexpectedly.");
                }

                if (one[0] == (byte)'\n')
                {
                    if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                    {
                        bytes.RemoveAt(bytes.Count - 1);
                    }

                    return Encoding.ASCII.GetString(bytes.ToArray());
                }

                bytes.Add(one[0]);
            }
        }
    }
}
