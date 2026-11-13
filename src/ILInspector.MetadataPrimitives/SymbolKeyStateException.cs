namespace ILInspector.Metadata;

/// <summary>
/// A Symbol Key builder, staged key, or slot was used outside its lifetime or
/// out of order: a stale staged key or slot after a freeze or reset, a key
/// opened while one is open, or a part written with none open. It is the
/// producer's error, never the inspected image's, so a consumer that
/// classifies image faults must not classify this.
/// </summary>
public sealed class SymbolKeyStateException(string message) : InvalidOperationException(message);
