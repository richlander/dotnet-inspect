using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// A dependency row. Every field comes out of the package's own nuspec, so
/// each is contained here at the row rather than at the several places that
/// build these rows (issue #3319).
/// </summary>
public class FlatDependency
{
    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework
    {
        get => field;
        set => field = CSharpIdentifier.ContainRenderedText(value);
    } = "";

    [MarkoutPropertyName("Package")]
    public string Id
    {
        get => field;
        set => field = CSharpIdentifier.ContainRenderedText(value);
    } = "";

    public string Version
    {
        get => field;
        set => field = CSharpIdentifier.ContainRenderedText(value);
    } = "";
}
