using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class RidPackageReference
{
    [MdfPropertyName("RID")]
    public string RuntimeIdentifier { get; set; } = "";

    [MdfPropertyName("Package")]
    public string PackageId { get; set; } = "";

    /// <summary>
    /// Whether the RID-specific package exists (verified via local file or NuGet).
    /// Null means not checked.
    /// </summary>
    [MdfIgnore]
    public bool? Exists { get; set; }

    [MdfPropertyName("Available")]
    public string AvailableDisplay => Exists switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };
}
