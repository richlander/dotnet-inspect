using System.Text;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

public static partial class ResourceExplanationCommand
{
    internal static async Task<int> ExplainComponentAsync(
        string file,
        string? parentPacket,
        int depth,
        OutputFormat format,
        string? outputPath,
        string? projection,
        CancellationToken cancellationToken,
        Stream? standardInput = null)
    {
        try
        {
            // Decode declarations only. This path never restores or realizes a Workspace.
            WorkspaceSharePacket? parent = parentPacket is null ? null
                : WorkspaceSharePacketCodec.Decode(
                    WorkspacePacketRestoration.GetPacketInput(parentPacket, "--parent-packet"),
                    cancellationToken);
            string json = await ReadComponentAsync(file, standardInput, cancellationToken);
            WorkspacePackageComponentExplanationInput input =
                WorkspacePackageComponentExplanation.Parse(json, parent);
            var explanation = WorkspaceComponentExplanationOperation.Explain(
                input, ResourceExplanationRequest.ForHost(depth));
            return WriteExplanation(explanation.Content, format, outputPath,
                contract: projection == ".contract", hal: projection == ".hal");
        }
        catch (Exception error) when (error is WorkspaceComponentExplanationException
            or WorkspaceSharePacketException or InvalidDataException or IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            CommandError.Write(error);
            return 1;
        }
    }

    private static async Task<string> ReadComponentAsync(
        string file, Stream? standardInput, CancellationToken cancellationToken)
    {
        if (file == "-" && standardInput is null && !Console.IsInputRedirected)
            throw new InvalidDataException("--component - requires redirected UTF-8 stdin.");

        Stream? ownedStream = null;
        try
        {
            Stream stream = file == "-"
                ? standardInput ?? (ownedStream = Console.OpenStandardInput())
                : ownedStream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int limit = WorkspacePackageComponentExplanation.MaxUtf8ByteLength;
            byte[] buffer = new byte[limit + 1];
            int written = 0;
            while (written < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(written), cancellationToken);
                if (read == 0)
                    return new UTF8Encoding(false, true).GetString(buffer, 0, written);
                written += read;
            }
            throw new InvalidDataException($"Component input exceeds the {limit}-byte read limit.");
        }
        finally
        {
            if (ownedStream is not null)
                await ownedStream.DisposeAsync();
        }
    }
}
