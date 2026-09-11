namespace ILInspector.JsExportSurface;

/// <summary>
/// The wire directions a declared type participates in: values flowing out of
/// exported functions are serialized, values flowing in are deserialized.
/// </summary>
/// <remarks>
/// The distinction exists because <c>[JsonIgnore(Condition = WhenWriting)]</c>
/// and <c>Condition = WhenReading</c> remove a member from one direction only.
/// A type reached in both directions cannot be described by a single
/// declaration when any of its members is direction-sensitive.
/// </remarks>
[Flags]
public enum JsonWireDirection
{
    None = 0,

    /// <summary>The type appears in an exported function's result.</summary>
    Serialize = 1,

    /// <summary>The type appears in an exported function's parameters.</summary>
    Deserialize = 2,

    Both = Serialize | Deserialize,
}
