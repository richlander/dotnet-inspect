namespace DotnetInspector.Queries;

/// <summary>
/// The canonical target framework against which a Workspace selects compatible
/// package assets and dependency groups.
/// </summary>
public sealed record WorkspaceRootTargetFramework
{
    public const string DefaultValue = "net11.0";

    public static WorkspaceRootTargetFramework Default { get; } =
        new(DefaultValue);

    public WorkspaceRootTargetFramework(string targetFramework)
    {
        if (!NuGetTargetFrameworkIdentity.TryNormalize(
                targetFramework,
                out string canonical))
        {
            throw new ArgumentException(
                "The Workspace root target framework is invalid.",
                nameof(targetFramework));
        }

        TargetFramework = canonical;
    }

    public string TargetFramework { get; }

    public override string ToString() => TargetFramework;
}
