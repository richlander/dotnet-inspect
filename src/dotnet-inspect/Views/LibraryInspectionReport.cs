using DotnetInspector.Models;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class LibraryInspectionReport
{
    public string Title { get; set; } = "";

    [MarkoutSection(Name = "Libraries")]
    public List<LibraryInspectionView> Assemblies { get; set; } = [];
}
