namespace ILInspector.JsExportSurface;

/// <summary>
/// The authenticated effective default-ignore condition for records reached
/// through one or more serializer contexts.
/// </summary>
public enum JsonWireContextDefaultIgnoreCondition
{
    /// <summary>Context defaults do not conditionally omit member values.</summary>
    Never,

    /// <summary>Null member values are omitted while serializing.</summary>
    WhenWritingNull,

    /// <summary>The context evidence is unsupported or conflicting.</summary>
    Unsupported,
}
