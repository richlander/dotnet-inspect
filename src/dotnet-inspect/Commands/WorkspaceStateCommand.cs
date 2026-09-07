using System.Text;
using DotnetInspector.Output;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Commands;

/// <summary>Converts canonical workspace share packets and their JSON shape.</summary>
public static class WorkspaceStateCommand
{
    private static readonly UTF8Encoding s_utf8Strict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<int> DecodeAsync(
        string? packet,
        string? file,
        CancellationToken cancellationToken,
        Stream? standardInput = null)
    {
        try
        {
            string encoded = await ReadInputAsync(
                packet,
                file,
                WorkspaceSharePacketCodec.MaxEncodedLength,
                trimTerminalLineEndings: true,
                standardInput,
                cancellationToken);
            WorkspaceSharePacket decoded = WorkspaceSharePacketCodec.Decode(
                encoded,
                cancellationToken);
            Console.WriteLine(WorkspaceSharePacketCodec.SerializeJson(decoded));
            return 0;
        }
        catch (Exception ex) when (IsExpectedInputFailure(ex))
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    public static async Task<int> EncodeAsync(
        string? json,
        string? file,
        CancellationToken cancellationToken,
        Stream? standardInput = null,
        bool url = false)
    {
        try
        {
            string input = await ReadInputAsync(
                json,
                file,
                WorkspaceSharePacketCodec.MaxDecodedUtf8Length,
                trimTerminalLineEndings: true,
                standardInput,
                cancellationToken);
            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
                input,
                cancellationToken);
            string encoded = WorkspaceSharePacketCodec.Encode(packet);
            Console.WriteLine(url
                ? $"https://dotnet-inspect.net/?w={encoded}"
                : encoded);
            return 0;
        }
        catch (Exception ex) when (IsExpectedInputFailure(ex))
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    private static async Task<string> ReadInputAsync(
        string? inline,
        string? file,
        int maximumLength,
        bool trimTerminalLineEndings,
        Stream? standardInput,
        CancellationToken cancellationToken)
    {
        if (inline is not null && inline != "-")
            return inline;

        int readLimit = maximumLength + (trimTerminalLineEndings ? 2 : 0);
        string input;
        if (file is not null)
        {
            string path = NormalizeFilePath(file);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                s_utf8Strict,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            input = await ReadBoundedAsync(
                reader,
                readLimit,
                maximumLength,
                cancellationToken);
        }
        else
        {
            Stream stream = standardInput ?? Console.OpenStandardInput();
            using var reader = new StreamReader(
                stream,
                s_utf8Strict,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: standardInput is not null);
            input = await ReadBoundedAsync(
                reader,
                readLimit,
                maximumLength,
                cancellationToken);
        }

        string payload = trimTerminalLineEndings
            ? RemoveTerminalLineEnding(input)
            : input;
        if (payload.Length > maximumLength)
        {
            throw new InvalidDataException(
                $"Workspace share input exceeds the {maximumLength}-character read limit.");
        }

        return payload;
    }

    private static string RemoveTerminalLineEnding(string input)
    {
        if (input.Length == 0 || input[^1] != '\n')
            return input;

        int length = input.Length - 1;
        if (length > 0 && input[length - 1] == '\r')
            length--;
        return input[..length];
    }

    private static string NormalizeFilePath(string path)
    {
        if (path.Length == 0)
            throw new InvalidDataException("Workspace-state file path cannot be empty.");

        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException ex)
        {
            throw InvalidFilePath(ex);
        }
        catch (NotSupportedException ex)
        {
            throw InvalidFilePath(ex);
        }
    }

    private static InvalidDataException InvalidFilePath(Exception innerException) =>
        new($"Workspace-state file path is invalid: {innerException.Message}", innerException);

    private static async Task<string> ReadBoundedAsync(
        TextReader reader,
        int readLimit,
        int declaredLimit,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[readLimit + 1];
        int written = 0;
        while (written < buffer.Length)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(written, buffer.Length - written),
                cancellationToken);
            if (read == 0)
                return new string(buffer, 0, written);
            written += read;
        }

        throw new InvalidDataException(
            $"Workspace share input exceeds the {declaredLimit}-character read limit.");
    }

    private static bool IsExpectedInputFailure(Exception exception) =>
        exception is WorkspaceSharePacketException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or DecoderFallbackException;
}
