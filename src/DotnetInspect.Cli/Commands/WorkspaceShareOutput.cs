using DotnetInspector.Core;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspaceShareOutput
{
    internal const string UrlPrefix = "https://dotnet-inspect.net/?w=";

    internal static int Write(
        WorkspaceSharePacket packet,
        WorkspaceShareFormat format)
    {
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        Console.WriteLine(
            format == WorkspaceShareFormat.Url
                ? UrlPrefix + encoded
                : encoded);
        return 0;
    }

    internal static int Write(
        InspectionShare share,
        WorkspaceShareFormat format)
    {
        switch (share)
        {
            case InspectionShare.Available available:
                CommandError.WriteLine(
                    format == WorkspaceShareFormat.Url
                        ? available.FullUrl
                        : available.FullUrl.StartsWith(
                            UrlPrefix,
                            StringComparison.Ordinal)
                            ? available.FullUrl[UrlPrefix.Length..]
                            : available.FullUrl);
                return 0;
            case InspectionShare.NonProjectable nonProjectable:
                CommandError.Write(
                    $"--share is not projectable at {nonProjectable.Path}: "
                    + nonProjectable.Reason);
                return 1;
            default:
                throw new InvalidOperationException(
                    "Unknown inspection Share outcome.");
        }
    }
}
