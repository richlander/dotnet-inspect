namespace NuGetFetch.PluginFixture;

internal sealed record PluginFixtureConfiguration
{
    public string RecordPath { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string Claims { get; init; } = "Authentication";
    public string ResponseCode { get; init; } = "Success";
    public string? AuthenticationType { get; init; }
    public string? PreambleMessage { get; init; }
    public string InboundHandshakePayload { get; init; } =
        """{"ProtocolVersion":"2.0.0","MinimumProtocolVersion":"1.0.0"}""";
    public string OutboundHandshakePayload { get; init; } =
        """{"ResponseCode":"Success","ProtocolVersion":"2.0.0"}""";
    public string? InboundLogRequestId { get; init; }
    public string? InboundLogPayload { get; init; }
    public bool WriteGarbageAndWait { get; init; }
    public PluginCredentialBehavior CredentialBehavior { get; init; }
    public PluginAfterSetLogLevelBehavior AfterSetLogLevelBehavior { get; init; }
}

internal enum PluginCredentialBehavior
{
    Respond,
    CloseOutput,
    Exit,
    ExitOnFirstRequest,
}

internal enum PluginAfterSetLogLevelBehavior
{
    Continue,
    EmitLog,
    CloseOutput,
    WaitForCloseMarkerThenCloseOutput,
    Stall,
    WaitForLogMarkerThenEmitLogAndStall,
    RespondBeforeCredentialLineCompletes,
}
