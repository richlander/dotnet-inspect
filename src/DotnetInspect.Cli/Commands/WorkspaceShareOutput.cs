using DotnetInspector.Sections;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspaceShareOutput
{
    internal const string UrlPrefix = WorkspaceShareUrl.Prefix;

    private static readonly AsyncLocal<DeferredSideOutput?> DeferredOutput = new();

    internal static IDisposable DeferSideOutput() => new DeferredSideOutput();

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
                string text = format == WorkspaceShareFormat.Url
                    ? available.FullUrl
                    : available.Packet;
                if (DeferredOutput.Value is { } deferred)
                    deferred.Text = text;
                else
                    CommandError.WriteLine(text);
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

    internal static int WriteScalar(
        InspectionShare share,
        WorkspaceShareFormat format)
    {
        switch (share)
        {
            case InspectionShare.Available available:
                Console.WriteLine(
                    format == WorkspaceShareFormat.Url
                        ? available.FullUrl
                        : available.Packet);
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

    private sealed class DeferredSideOutput : IDisposable
    {
        private readonly DeferredSideOutput? _previous = DeferredOutput.Value;

        internal DeferredSideOutput() => DeferredOutput.Value = this;

        internal string? Text { get; set; }

        public void Dispose()
        {
            DeferredOutput.Value = _previous;
            if (Text is not null)
                CommandError.WriteLine(Text);
        }
    }
}
