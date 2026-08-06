using InertText;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// A dependency row. Every field comes out of the package's own nuspec, so
/// each is contained here at the row rather than at the several places that
/// build these rows (issue #3319).
/// </summary>
public class FlatDependency
{
    private readonly InertString _targetFramework;
    private readonly InertString _id;
    private readonly InertString _version;

    public FlatDependency(
        InertString targetFramework,
        InertString id,
        InertString version)
    {
        _targetFramework = targetFramework;
        _id = id;
        _version = version;
    }

    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework => _targetFramework.ToString();

    [MarkoutPropertyName("Package")]
    public string Id => _id.ToString();

    public string Version => _version.ToString();
}
