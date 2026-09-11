using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NuGetFetch.PluginFixture;

internal static class Program
{
    private const int OutputClosedExitCode = 42;
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static async Task<int> Main(string[] args)
    {
        if (args is ["-Plugin"])
        {
            return await RunSupervisorAsync();
        }

        if (args is ["--worker", var configurationPath])
        {
            return await RunWorkerAsync(configurationPath);
        }

        return 2;
    }

    private static async Task<int> RunSupervisorAsync()
    {
        string assemblyPath = typeof(Program).Assembly.Location;
        string configurationPath = Path.ChangeExtension(assemblyPath, ".json");
        PluginFixtureConfiguration configuration =
            await ReadConfigurationAsync(configurationPath);
        string hostPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not determine the plugin fixture host path.");
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add(configurationPath);

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start the plugin fixture worker.");
        // The worker can end the output stream while this launched process stays alive.
        CloseStandardOutput();
        await worker.WaitForExitAsync();
        if (worker.ExitCode != OutputClosedExitCode)
        {
            return worker.ExitCode;
        }

        using var recordStream = new FileStream(
            configuration.RecordPath,
            FileMode.Append,
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
        await DrainInputAsync(input, record);
        return 0;
    }

    private static async Task<int> RunWorkerAsync(string configurationPath)
    {
        PluginFixtureConfiguration configuration =
            await ReadConfigurationAsync(configurationPath);

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
        using var output = new StreamWriter(
            Console.OpenStandardOutput(),
            Utf8NoBom)
        {
            AutoFlush = true,
        };

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
                        return OutputClosedExitCode;
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
                        return OutputClosedExitCode;
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

    private static async Task<PluginFixtureConfiguration> ReadConfigurationAsync(
        string configurationPath) =>
        JsonSerializer.Deserialize<PluginFixtureConfiguration>(
            await File.ReadAllTextAsync(configurationPath))
        ?? throw new InvalidOperationException(
            $"Could not read plugin fixture configuration '{configurationPath}'.");

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
                return true;
            case PluginAfterSetLogLevelBehavior.WaitForCloseMarkerThenCloseOutput:
                await WaitForMarkerAsync(configuration.RecordPath + ".close");
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

    private static void CloseStandardOutput()
    {
        int error;
        if (OperatingSystem.IsWindows())
        {
            error = CloseHandle(GetStdHandle(-11))
                ? 0
                : Marshal.GetLastPInvokeError();
        }
        else if (OperatingSystem.IsMacOS())
        {
            error = CloseMatchingUnixDescriptors(isMacOS: true);
        }
        else
        {
            error = CloseMatchingUnixDescriptors(isMacOS: false);
        }

        if (error != 0)
        {
            throw new IOException(
                $"Could not close the plugin fixture output pipe (error {error}).");
        }
    }

    private static int CloseMatchingUnixDescriptors(bool isMacOS)
    {
        const int maxDescriptor = 1024;
        if (SystemNativeFStat(1, out FileStatus outputStatus) != 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        List<int> matches = [];
        for (int descriptor = 1; descriptor < maxDescriptor; descriptor++)
        {
            if (SystemNativeFStat(descriptor, out FileStatus candidateStatus) == 0
                && outputStatus.Device == candidateStatus.Device
                && outputStatus.Inode == candidateStatus.Inode)
            {
                matches.Add(descriptor);
            }
        }

        foreach (int descriptor in matches)
        {
            int result = isMacOS
                ? CloseMacOS(descriptor)
                : CloseUnix(descriptor);
            if (result != 0)
            {
                return Marshal.GetLastPInvokeError();
            }
        }

        return 0;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStatus
    {
        internal int Flags;
        internal int Mode;
        internal uint UserId;
        internal uint GroupId;
        internal long Size;
        internal long AccessTime;
        internal long AccessTimeNanoseconds;
        internal long ModificationTime;
        internal long ModificationTimeNanoseconds;
        internal long ChangeTime;
        internal long ChangeTimeNanoseconds;
        internal long BirthTime;
        internal long BirthTimeNanoseconds;
        internal long Device;
        internal long RawDevice;
        internal long Inode;
        internal uint UserFlags;
        internal int HardLinkCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int standardHandle);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseUnix(int fileDescriptor);

    [DllImport("libSystem.B.dylib", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseMacOS(int fileDescriptor);

    [DllImport(
        "libSystem.Native",
        EntryPoint = "SystemNative_FStat",
        SetLastError = true)]
    private static extern int SystemNativeFStat(
        nint fileDescriptor,
        out FileStatus status);
}
