using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class RidPackageReference
{
    [MarkOutPropertyName("RID")]
    public string RuntimeIdentifier { get; set; } = "";

    [MarkOutPropertyName("Package")]
    public string PackageId { get; set; } = "";

    /// <summary>
    /// Whether the RID-specific package exists (verified via local file or NuGet).
    /// Null means not checked.
    /// </summary>
    [MarkOutIgnore]
    public bool? Exists { get; set; }

    [MarkOutPropertyName("Available")]
    public string AvailableDisplay => Exists switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };
}
