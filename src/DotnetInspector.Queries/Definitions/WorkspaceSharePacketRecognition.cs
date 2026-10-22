namespace DotnetInspector.Queries.Definitions;

/// <summary>Bounded packet-intent recognition, without host activation.</summary>
public abstract record WorkspaceSharePacketRecognition
{
    private WorkspaceSharePacketRecognition()
    {
    }

    /// <summary>No format marker was established within the codec's limits.</summary>
    public sealed record Unrecognized : WorkspaceSharePacketRecognition;

    /// <summary>The input is a fully validated canonical packet.</summary>
    public sealed record Recognized(WorkspaceSharePacket Packet)
        : WorkspaceSharePacketRecognition;

    /// <summary>Packet intent was observed, but validation failed.</summary>
    public sealed record Invalid(
        WorkspaceSharePacketFailureKind Kind,
        string Message) : WorkspaceSharePacketRecognition;
}
