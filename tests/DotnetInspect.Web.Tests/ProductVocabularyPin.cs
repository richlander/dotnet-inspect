namespace DotnetInspect.ProductVocabularyTesting;

/// <summary>
/// The one pinned product vocabulary snapshot identity. Inspect Web's suite
/// compiles this file and the CLI suite links it, and each asserts its own
/// host's composed snapshot against this value. Changing it for one host's list
/// fails the other host's suite until that host composes the same snapshot.
/// </summary>
/// <remarks>
/// The file lives in the Inspect Web suite on purpose. CI selects jobs by
/// changed path, and a change under <c>tests/DotnetInspect.Web.Tests</c> runs
/// both the Inspect Web lane and the CLI lane, so moving this pin always runs
/// both suites.
/// </remarks>
internal static class ProductVocabularyPin
{
    public const int FormatVersion = 1;

    public const string SnapshotIdentity =
        "sha256:f0527bd80f85c7683fcf3797da980ef38b3266116f375cc44323796775d884df";

    /// <summary>
    /// The SHA-256 of the Resource Explanation Document JSON each host
    /// produces for one vocabulary explanation request: the CLI through
    /// <c>explain --json</c> and Inspect Web through its catalog-facade export.
    /// Equal snapshots must yield equal Content under the shared host limits.
    /// </summary>
    public static readonly ExplanationContentPin[] ExplanationContent =
    [
        new(
            "vocabularies",
            1,
            "sha256:7cedbe8a3c10f419b0a4f718272a519a44cb38c9f41323be611845d905960a43"),
        new(
            "vocabularies/csharp.style-choices",
            1,
            "sha256:300c497ee8976bbfc98a482ad990abab66f26ebda1d11eabc221cf5930bb46e9"),
    ];
}

internal sealed record ExplanationContentPin(
    string Path,
    int Depth,
    string Digest);
