namespace ILInspector.CSharp;

/// <summary>
/// How a decompiled member body touches a backing field, so ReturnToSender can decide
/// which auto-property/field member requirements to synthesize without inspecting
/// Decompiler IR itself.
/// </summary>
public enum BackingFieldAccess
{
    /// <summary>A field load (<c>ldfld</c>/<c>ldflda</c>) — the body reads the field.</summary>
    Read,

    /// <summary>An instance field store whose receiver is <c>this</c> (argument 0).</summary>
    InstanceWrite,

    /// <summary>A static field store.</summary>
    StaticWrite,
}

/// <summary>
/// A neutral, IR-free reference to a backing field a decompiled member body reads or
/// writes. ReturnToSender uses these to reconstruct the auto-properties/fields the body
/// depends on without reading Decompiler IR, so the IR-to-fact extraction lives in the
/// product (ILInspector.Decompiler, which owns the IR) rather than in the harness. All
/// fields are plain strings and an access enum; no Decompiler type crosses this boundary.
/// </summary>
/// <param name="DeclaringNamespace">
/// Namespace of the field's declaring type (the definition, with any generic instance
/// unwrapped), so the harness can match it against the target type's identity.
/// </param>
/// <param name="DeclaringName">
/// Metadata name of the field's declaring type definition, matching the harness's
/// self-type name form.
/// </param>
/// <param name="FieldName">The stored/loaded field's metadata name.</param>
/// <param name="BackingPropertyName">
/// The property name when the field backs an auto-property, or null when there is no
/// such proof.
/// </param>
/// <param name="Access">Whether the reference is a read, an instance write, or a static write.</param>
public sealed record BackingFieldReference(
    string DeclaringNamespace,
    string DeclaringName,
    string FieldName,
    string? BackingPropertyName,
    BackingFieldAccess Access);
