namespace ILInspector.JsExportSurface;

/// <summary>
/// Whether one JSON member participates in a wire direction.
/// </summary>
public enum JsonWireMemberPresence
{
    /// <summary>The member does not participate in this direction.</summary>
    Absent,

    /// <summary>The member participates unconditionally in this direction.</summary>
    Present,

    /// <summary>The member's value determines whether its key is present.</summary>
    Conditional,

    /// <summary>The member's authentic metadata cannot be honored.</summary>
    Unsupported,
}
