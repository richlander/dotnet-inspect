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
        "sha256:79f5a1cddcbabc41e23e85a96ecefbe582416382ec00fb79794f444e794c308e";
}
