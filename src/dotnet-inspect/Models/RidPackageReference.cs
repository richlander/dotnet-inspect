using System.Text.Json.Serialization;

namespace DotnetInspector.Models;

public class RidPackageReference
{
    public string RuntimeIdentifier { get; set; } = "";

    public string PackageId { get; set; } = "";

    /// <summary>
    /// Whether the RID-specific package exists (verified via local file or NuGet).
    /// Null means not checked.
    /// </summary>
    [JsonIgnore]
    public bool? Exists { get; set; }

    [JsonIgnore]
    public string AvailableDisplay => Exists switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };
}
