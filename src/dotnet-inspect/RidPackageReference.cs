using Markout;

namespace DotnetInspector;

public class RidPackageReference
{
    [MarkoutPropertyName("RID")]
    public string RuntimeIdentifier { get; set; } = "";

    [MarkoutPropertyName("Package")]
    public string PackageId { get; set; } = "";

    /// <summary>
    /// Whether the RID-specific package exists (verified via local file or NuGet).
    /// Null means not checked.
    /// </summary>
    [MarkoutIgnore]
    public bool? Exists { get; set; }

    [MarkoutPropertyName("Available")]
    public string AvailableDisplay => Exists switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };
}
