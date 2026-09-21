using DotnetInspector.Sections;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspaceShareOutput
{
    internal const string UrlPrefix = "https://dotnet-inspect.net/?w=";

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
        InspectionPortableProjection portableProjection,
        WorkspaceShareFormat format)
    {
        switch (portableProjection)
        {
            case InspectionPortableProjection.Available available:
                string text = format == WorkspaceShareFormat.Url
                    ? available.FullUrl
                    : available.Packet;
                if (DeferredOutput.Value is { } deferred)
                    deferred.Text = text;
                else
                    CommandError.WriteLine(text);
                return 0;
            case InspectionPortableProjection.NonProjectable nonProjectable:
                CommandError.Write(
                    $"--share is not projectable{Location(nonProjectable)}: "
                    + Explain(nonProjectable));
                return 1;
            default:
                throw new InvalidOperationException(
                    "Unknown inspection portable projection.");
        }
    }

    internal static int WriteScalar(
        InspectionPortableProjection portableProjection,
        WorkspaceShareFormat format)
    {
        switch (portableProjection)
        {
            case InspectionPortableProjection.Available available:
                Console.WriteLine(
                    format == WorkspaceShareFormat.Url
                        ? available.FullUrl
                        : available.Packet);
                return 0;
            case InspectionPortableProjection.NonProjectable nonProjectable:
                CommandError.Write(
                    $"--share is not projectable{Location(nonProjectable)}: "
                        + Explain(nonProjectable));
                return 1;
            default:
                throw new InvalidOperationException(
                    "Unknown inspection portable projection.");
        }
    }

    private static string Explain(
        InspectionPortableProjection.NonProjectable nonProjectable) =>
        nonProjectable.Explanation
        ?? nonProjectable.Reason switch
        {
            InspectionPortableProjectionFailureReason.NotSupported =>
                "this inspection has no defined portable projection",
            InspectionPortableProjectionFailureReason.Invalid =>
                "the inspection state is invalid for portable projection",
            InspectionPortableProjectionFailureReason.Incomplete =>
                "the inspection lacks complete evidence for portable projection",
            InspectionPortableProjectionFailureReason.Unavailable =>
                "required portable state is unavailable",
            InspectionPortableProjectionFailureReason.Failed =>
                "the portable projection failed",
            _ => throw new InvalidOperationException(
                "Unknown portable projection failure reason."),
        };

    private static string Location(
        InspectionPortableProjection.NonProjectable nonProjectable) =>
        nonProjectable.Location is { } location
            ? $" at {location}"
            : "";

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
