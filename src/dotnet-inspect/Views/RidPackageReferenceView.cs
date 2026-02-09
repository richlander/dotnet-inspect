using DotnetInspector.Models;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public class RidPackageReferenceView
{
    private readonly RidPackageReference _data;

    public RidPackageReferenceView(RidPackageReference data)
    {
        _data = data;
    }

    [MarkoutPropertyName("RID")]
    public string RuntimeIdentifier => _data.RuntimeIdentifier;

    [MarkoutPropertyName("Package")]
    public string PackageId => _data.PackageId;

    [MarkoutPropertyName("Available")]
    public string AvailableDisplay => _data.AvailableDisplay;
}
