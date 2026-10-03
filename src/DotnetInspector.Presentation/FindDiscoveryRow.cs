using InertText;

namespace DotnetInspector.Presentation;

/// <summary>
/// Shared display projection for a settled Type or Member discovery result.
/// Coordinates are presentation addresses, not declaration identities.
/// </summary>
public sealed record FindDiscoveryRow(
    InertString Coordinate,
    InertString Kind,
    InertString Source,
    InertString Library,
    InertString Pattern,
    InertString Declaration,
    InertString Signature,
    InertString Match,
    InertString Ecosystem);
