using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspaceComponentCommand
{
    internal static int List(
        string packetInput,
        CancellationToken cancellationToken)
    {
        if (!TryDecode(packetInput, cancellationToken, out WorkspaceSharePacket? packet))
            return 1;

        WorkspaceComponentDocument document =
            WorkspaceComponentCatalog.Describe(packet!);
        var context = new WorkspaceCommandJsonContext(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            });
        Console.WriteLine(
            JsonSerializer.Serialize(
                document,
                context.WorkspaceComponentDocument));
        return 0;
    }

    internal static int AddPackage(
        string packetInput,
        string package,
        string? framework,
        string? runtimeIdentifier,
        WorkspaceContextComponentPath? context,
        WorkspaceShareFormat shareFormat,
        CancellationToken cancellationToken)
    {
        if (!TryDecode(packetInput, cancellationToken, out WorkspaceSharePacket? packet))
            return 1;

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(package);
        WorkspacePackageComponentEditResult result =
            WorkspacePackageComponentEditor.Add(
                packet!,
                packageId,
                version,
                framework,
                runtimeIdentifier,
                context);
        return Write(result, shareFormat);
    }

    internal static int RemovePackage(
        string packetInput,
        WorkspacePackageComponentPath component,
        WorkspaceShareFormat shareFormat,
        CancellationToken cancellationToken)
    {
        if (!TryDecode(packetInput, cancellationToken, out WorkspaceSharePacket? packet))
            return 1;

        return Write(
            WorkspacePackageComponentEditor.Remove(packet!, component),
            shareFormat);
    }

    private static int Write(
        WorkspacePackageComponentEditResult result,
        WorkspaceShareFormat shareFormat)
    {
        if (result.Failure is { } failure)
        {
            CommandError.Write(
                $"Workspace Package edit failed ({failure.Kind}).",
                [failure.Detail]);
            return 1;
        }

        return WorkspaceShareOutput.Write(result.Packet!, shareFormat);
    }

    private static bool TryDecode(
        string packetInput,
        CancellationToken cancellationToken,
        out WorkspaceSharePacket? packet)
    {
        try
        {
            packet = WorkspaceSharePacketCodec.Decode(
                WorkspacePacketRestoration.GetPacketInput(
                    packetInput,
                    "--packet"),
                cancellationToken);
            return true;
        }
        catch (Exception error) when (error is WorkspaceSharePacketException
            or InvalidDataException or ArgumentException)
        {
            CommandError.Write(
                "The Workspace packet input is invalid.",
                [error.Message]);
            packet = null;
            return false;
        }
    }
}
