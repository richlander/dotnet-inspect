namespace DotnetInspector.Queries;

/// <summary>The default target-framework policy carried by one Workspace.</summary>
public sealed record WorkspaceTargetFrameworkPolicy
{
    public const string ProductDefaultFramework = "net11.0";

    public static WorkspaceTargetFrameworkPolicy ProductDefault { get; } =
        new(
            ProductDefaultFramework,
            WorkspaceTargetFrameworkPolicySource.ProductDefault);

    public WorkspaceTargetFrameworkPolicy(string defaultFramework)
        : this(
            defaultFramework,
            WorkspaceTargetFrameworkPolicySource.Configured)
    {
    }

    WorkspaceTargetFrameworkPolicy(
        string defaultFramework,
        WorkspaceTargetFrameworkPolicySource source)
    {
        ArgumentNullException.ThrowIfNull(defaultFramework);
        if (!string.Equals(
                defaultFramework,
                defaultFramework.Trim(),
                StringComparison.Ordinal)
            || !NuGetTargetFrameworkIdentity.TryNormalize(
                defaultFramework,
                out string canonical))
        {
            throw new ArgumentException(
                "The Workspace default target framework is invalid.",
                nameof(defaultFramework));
        }

        DefaultFramework = canonical;
        Source = source;
    }

    public string DefaultFramework { get; }

    public WorkspaceTargetFrameworkPolicySource Source { get; }

    public override string ToString() => DefaultFramework;
}

/// <summary>The origin of one Workspace default target-framework policy.</summary>
public enum WorkspaceTargetFrameworkPolicySource
{
    ProductDefault,
    Configured,
}
