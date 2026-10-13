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
        "sha256:1c48720af80cda1e0c37e0759f6b3c987c35b6358db7a44a1eadd1671e47ec73";

    /// <summary>
    /// The SHA-256 of the Resource Explanation Document JSON each host
    /// produces for one vocabulary explanation request: the CLI through
    /// <c>explain .contract --json</c> and Inspect Web through explicit contract selection.
    /// Equal snapshots must yield equal Content under the shared host limits.
    /// </summary>
    public static readonly ExplanationContentPin[] ExplanationContent =
    [
        new(
            "vocabularies",
            1,
            "sha256:edf9e7452c84cc3afa291beb10c7d9cec28f3d4c5c4cf5e353800cb010153f07"),
        new(
            "vocabularies/csharp.style-choices",
            1,
            "sha256:300c497ee8976bbfc98a482ad990abab66f26ebda1d11eabc221cf5930bb46e9"),
    ];

    /// <summary>Actual CLI projections pinned independently by both host suites.</summary>
    public static readonly ExplanationContentPin[] CompactContent =
    [
        new("vocabularies/csharp.style-choices", 1,
            "sha256:1bdcda51e030a5d1cf430751feaa72082b3da996f134ecd34491f9d58b88aa95"),
        new("vocabularies/csharp.style-choices", 0,
            "sha256:b728f429ac8d2166fbe17f9b3beba032a6b4813674662b1153cef8fe53f3c50e", "data"),
        new("vocabularies/csharp.style-choices", 0,
            "sha256:ffc3639e94ee0dd9972af847103e79b12abc58e0f228337cd45ac8eba3ba76da", "hal"),
    ];
}

internal sealed record ExplanationContentPin(
    string Path,
    int Depth,
    string Digest,
    string? Selection = null);
