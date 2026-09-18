namespace DotnetInspector.Queries;

/// <summary>The governing target-framework policy for one traversal.</summary>
public sealed record TraversalTargetFrameworkPolicy
{
    public const string ProductDefaultTargetFramework = "net12.0";

    public static TraversalTargetFrameworkPolicy ProductDefault { get; } =
        new(
            ProductDefaultTargetFramework,
            TraversalTargetFrameworkPolicySource.ProductDefault);

    public TraversalTargetFrameworkPolicy(string targetFramework)
        : this(
            targetFramework,
            TraversalTargetFrameworkPolicySource.Configured)
    {
    }

    TraversalTargetFrameworkPolicy(
        string targetFramework,
        TraversalTargetFrameworkPolicySource source)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);
        if (!string.Equals(
                targetFramework,
                targetFramework.Trim(),
                StringComparison.Ordinal)
            || !NuGetTargetFrameworkIdentity.TryNormalize(
                targetFramework,
                out string canonical))
        {
            throw new ArgumentException(
                "The traversal target framework is invalid.",
                nameof(targetFramework));
        }

        TargetFramework = canonical;
        Source = source;
    }

    public string TargetFramework { get; }

    public TraversalTargetFrameworkPolicySource Source { get; }

    public override string ToString() => TargetFramework;
}

/// <summary>The origin of one traversal target-framework policy.</summary>
public enum TraversalTargetFrameworkPolicySource
{
    ProductDefault,
    Configured,
}
