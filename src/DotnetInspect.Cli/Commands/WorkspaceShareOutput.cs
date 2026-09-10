using DotnetInspector.Options;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Commands;

internal static class WorkspaceShareOutput
{
    private const string ShareUrlPrefix = "https://dotnet-inspect.net/?w=";

    internal static int Write(
        WorkspaceSharePacket packet,
        WorkspaceShareFormat format)
    {
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        Console.WriteLine(
            format == WorkspaceShareFormat.Url
                ? ShareUrlPrefix + encoded
                : encoded);
        return 0;
    }
}
