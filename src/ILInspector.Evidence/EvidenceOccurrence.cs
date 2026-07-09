namespace ILInspector.Evidence;

/// <summary>
/// A single domain-free item within an ordered stream, presented to the
/// <see cref="CorrespondenceEngine"/> for cross-version matching. Domain producers
/// project their nodes (IL operations, IR nodes, API members) into this shape.
/// </summary>
/// <param name="ContentKey">
/// The canonical content fingerprint. Two occurrences are candidate "same thing"
/// matches iff their content keys are equal, so the producing layer must fold all
/// incidental encoding (short/long form, register renumbering) into this key.
/// </param>
/// <param name="ScopeKey">
/// An optional structural scope tag (enclosing loop/try/region). A corroborating
/// signal for move detection and the raw material for scope-transition folds; null
/// when unknown or not modeled.
/// </param>
/// <param name="Payload">
/// Opaque domain detail carried onto the produced <see cref="EvidenceRow"/> for
/// display. Never inspected by the matcher.
/// </param>
public sealed record EvidenceOccurrence(
    string ContentKey,
    string? ScopeKey = null,
    object? Payload = null);
