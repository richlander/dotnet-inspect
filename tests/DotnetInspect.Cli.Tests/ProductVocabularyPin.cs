namespace DotnetInspect.ProductVocabularyTesting;

/// <summary>
/// The one pinned product vocabulary snapshot identity. The CLI suite compiles
/// this file and Inspect Web's suite links it, and each asserts its own host's
/// composed snapshot against this value. Changing it for one host's list fails
/// the other host's suite until that host composes the same snapshot.
/// </summary>
internal static class ProductVocabularyPin
{
    public const int FormatVersion = 1;

    public const string SnapshotIdentity =
        "sha256:79f5a1cddcbabc41e23e85a96ecefbe582416382ec00fb79794f444e794c308e";
}
