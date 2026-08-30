using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NuGetFetch.PluginFixture;

internal static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static async Task<int> Main(string[] args)
    {
        if (args is not ["-Plugin"])
        {
            return 2;
        }

        string assemblyPath = typeof(Program).Assembly.Location;
        string configurationPath = Path.ChangeExtension(assemblyPath, ".json");
        PluginFixtureConfiguration configuration =
            JsonSerializer.Deserialize<PluginFixtureConfiguration>(
                await File.ReadAllTextAsync(configurationPath))
            ?? throw new InvalidOperationException(
                $"Could not read plugin fixture configuration '{configurationPath}'.");

        using var recordStream = new FileStream(
            configuration.RecordPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite);
        using var record = new StreamWriter(recordStream, Utf8NoBom)
        {
            AutoFlush = true,
        };
        using var input = new StreamReader(
            Console.OpenStandardInput(),
            Utf8NoBom,
            detectEncodingFromByteOrderMarks: false);
        StreamWriter? output = new(OpenOwnedStandardOutput(), Utf8NoBom)
        {
            AutoFlush = true,
        };

        try
        {
            if (configuration.WriteGarbageAndWait)
            {
                await output.WriteLineAsync("not json at all");
                await Task.Delay(TimeSpan.FromSeconds(2));
                return 0;
            }

            if (configuration.PreambleMessage is not null)
            {
                await output.WriteLineAsync(configuration.PreambleMessage);
            }

            await EmitMessageAsync(
                output,
                "fake-handshake",
                "Request",
                "Handshake",
                configuration.InboundHandshakePayload);

            while (await input.ReadLineAsync() is { } line)
            {
                await record.WriteLineAsync(line);

                using JsonDocument message = JsonDocument.Parse(line);
                JsonElement root = message.RootElement;
                if (root.GetProperty("Type").GetString() != "Request")
                {
                    continue;
                }

                string requestId = root.GetProperty("RequestId").GetString()!;
                string method = root.GetProperty("Method").GetString()!;
                switch (method)
                {
                    case "Handshake":
                        await EmitMessageAsync(
                            output,
                            requestId,
                            "Response",
                            method,
                            configuration.OutboundHandshakePayload);
                        break;
                    case "MonitorNuGetProcessExit":
                    case "Initialize":
                    case "SetLogLevel":
                        await EmitSuccessAsync(output, requestId, method);
                        if (method == "SetLogLevel"
                            && await ApplyAfterSetLogLevelBehaviorAsync(
                                configuration,
                                input,
                                output))
                        {
                            output = null;
                            await DrainInputAsync(input, record);
                            return 0;
                        }
                        break;
                    case "GetOperationClaims":
                        await EmitObjectAsync(
                            output,
                            requestId,
                            method,
                            new { Claims = new[] { configuration.Claims } });
                        break;
                    case "GetAuthenticationCredentials":
                        CredentialActionResult result =
                            await ApplyCredentialBehaviorAsync(
                                configuration,
                                output,
                                requestId);
                        if (result == CredentialActionResult.OutputClosed)
                        {
                            output = null;
                            await DrainInputAsync(input, record);
                            return 0;
                        }
                        if (result == CredentialActionResult.Exit)
                        {
                            return 0;
                        }
                        break;
                    case "Close":
                        return 0;
                }
            }

            return 0;
        }
        finally
        {
            output?.Dispose();
        }
    }

    private static async Task<bool> ApplyAfterSetLogLevelBehaviorAsync(
        PluginFixtureConfiguration configuration,
        StreamReader input,
        StreamWriter output)
    {
        switch (configuration.AfterSetLogLevelBehavior)
        {
            case PluginAfterSetLogLevelBehavior.Continue:
                return false;
            case PluginAfterSetLogLevelBehavior.EmitLog:
                await EmitLogAsync(configuration, output);
                return false;
            case PluginAfterSetLogLevelBehavior.CloseOutput:
                await CloseOutputAsync(output);
                return true;
            case PluginAfterSetLogLevelBehavior.WaitForCloseMarkerThenCloseOutput:
                await WaitForMarkerAsync(configuration.RecordPath + ".close");
                await CloseOutputAsync(output);
                return true;
            case PluginAfterSetLogLevelBehavior.Stall:
                await Task.Delay(TimeSpan.FromSeconds(30));
                return false;
            case PluginAfterSetLogLevelBehavior.WaitForLogMarkerThenEmitLogAndStall:
                await WaitForMarkerAsync(configuration.RecordPath + ".log");
                await EmitLogAsync(configuration, output);
                await Task.Delay(TimeSpan.FromSeconds(30));
                return false;
            case PluginAfterSetLogLevelBehavior.RespondBeforeCredentialLineCompletes:
                char[] prefixBuffer = new char[256];
                int count = await input.ReadBlockAsync(prefixBuffer);
                string prefix = new(prefixBuffer, 0, count);
                string requestId = ReadPartialStringProperty(prefix, "RequestId");
                await EmitCredentialResponseAsync(configuration, output, requestId);
                await Task.Delay(TimeSpan.FromSeconds(30));
                return false;
            default:
                throw new InvalidOperationException(
                    $"Unknown post-initialization behavior " +
                    $"'{configuration.AfterSetLogLevelBehavior}'.");
        }
    }

    private static async Task<CredentialActionResult> ApplyCredentialBehaviorAsync(
        PluginFixtureConfiguration configuration,
        StreamWriter output,
        string requestId)
    {
        switch (configuration.CredentialBehavior)
        {
            case PluginCredentialBehavior.Respond:
                await EmitCredentialResponseAsync(configuration, output, requestId);
                return CredentialActionResult.Continue;
            case PluginCredentialBehavior.CloseOutput:
                await CloseOutputAsync(output);
                return CredentialActionResult.OutputClosed;
            case PluginCredentialBehavior.Exit:
                return CredentialActionResult.Exit;
            case PluginCredentialBehavior.ExitOnFirstRequest:
                string startsPath = configuration.RecordPath + ".starts";
                int starts = File.Exists(startsPath)
                    ? int.Parse(await File.ReadAllTextAsync(startsPath))
                    : 0;
                starts++;
                await File.WriteAllTextAsync(startsPath, starts.ToString());
                if (starts == 1)
                {
                    return CredentialActionResult.Exit;
                }

                await EmitCredentialResponseAsync(configuration, output, requestId);
                return CredentialActionResult.Continue;
            default:
                throw new InvalidOperationException(
                    $"Unknown credential behavior '{configuration.CredentialBehavior}'.");
        }
    }

    private static async Task EmitCredentialResponseAsync(
        PluginFixtureConfiguration configuration,
        StreamWriter output,
        string requestId)
    {
        if (configuration.ResponseCode != "Success")
        {
            await EmitObjectAsync(
                output,
                requestId,
                "GetAuthenticationCredentials",
                new { configuration.ResponseCode });
            return;
        }

        string[]? authenticationTypes = configuration.AuthenticationType is null
            ? null
            : [configuration.AuthenticationType];
        await EmitObjectAsync(
            output,
            requestId,
            "GetAuthenticationCredentials",
            new
            {
                configuration.Username,
                configuration.Password,
                AuthenticationTypes = authenticationTypes,
                ResponseCode = "Success",
            });
    }

    private static Task EmitSuccessAsync(
        StreamWriter output,
        string requestId,
        string method) =>
        EmitObjectAsync(output, requestId, method, new { ResponseCode = "Success" });

    private static Task EmitObjectAsync<T>(
        StreamWriter output,
        string requestId,
        string method,
        T payload) =>
        output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            RequestId = requestId,
            Type = "Response",
            Method = method,
            Payload = payload,
        }));

    private static async Task EmitMessageAsync(
        StreamWriter output,
        string requestId,
        string type,
        string method,
        string payload)
    {
        using JsonDocument payloadDocument = JsonDocument.Parse(payload);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("RequestId", requestId);
            writer.WriteString("Type", type);
            writer.WriteString("Method", method);
            writer.WritePropertyName("Payload");
            payloadDocument.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }

        await output.WriteLineAsync(Utf8NoBom.GetString(buffer.ToArray()));
    }

    private static Task EmitLogAsync(
        PluginFixtureConfiguration configuration,
        StreamWriter output) =>
        EmitMessageAsync(
            output,
            configuration.InboundLogRequestId
                ?? throw new InvalidOperationException("Inbound log request ID was not set."),
            "Request",
            "Log",
            configuration.InboundLogPayload
                ?? throw new InvalidOperationException("Inbound log payload was not set."));

    private static async Task WaitForMarkerAsync(string path)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(10);
        }
    }

    private static async Task CloseOutputAsync(StreamWriter output)
    {
        await output.FlushAsync();
        output.Dispose();
    }

    private static FileStream OpenOwnedStandardOutput()
    {
        nint handle = OperatingSystem.IsWindows()
            ? GetStdHandle(-11)
            : 1;
        return new FileStream(
            new SafeFileHandle(handle, ownsHandle: true),
            FileAccess.Write);
    }

    private static async Task DrainInputAsync(
        StreamReader input,
        StreamWriter record)
    {
        while (await input.ReadLineAsync() is { } line)
        {
            await record.WriteLineAsync(line);
        }
    }

    private static string ReadPartialStringProperty(string json, string property)
    {
        string prefix = $"\"{property}\":\"";
        int start = json.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"The partial request did not contain '{property}'.");
        }

        start += prefix.Length;
        int end = json.IndexOf('"', start);
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"The partial request did not contain a complete '{property}'.");
        }

        return json[start..end];
    }

    private enum CredentialActionResult
    {
        Continue,
        OutputClosed,
        Exit,
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int standardHandle);
}
