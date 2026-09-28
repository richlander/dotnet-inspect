using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Which stage of a dependent producer needs which stage of its dependency.
/// </summary>
public enum ProducerDependencyKind
{
    /// <summary>
    /// The dependent's visit of a unit reads the dependency's fact for the same
    /// unit, so the dependency visits each unit first.
    /// </summary>
    VisitNeedsVisit,

    /// <summary>
    /// The dependent's visits read the dependency's completed result, so they
    /// start in a pass after the dependency completes.
    /// </summary>
    VisitNeedsResult,

    /// <summary>
    /// The dependent's completion reads the dependency's completed result.
    /// </summary>
    CompletionNeedsResult,
}

/// <summary>One declared dependency of a producer.</summary>
public sealed record ProducerDependency(
    ProducerDeclaration Producer,
    ProducerDependencyKind Kind);

/// <summary>
/// A producer's static declaration: identity, version, tier, dependencies, and
/// the parameters that distinguish one request from another. A declaration is
/// requested and read by reference; it is never looked up by name or type.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/producer-planning.md</c>. This contract names no
/// unit kind or source; sources such as method definitions derive from it.
/// </remarks>
public abstract class ProducerDeclaration
{
    private protected ProducerDeclaration(
        string identity,
        int version,
        int tier,
        Func<IReadOnlyList<ProducerDependency>>? dependencies,
        string? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentOutOfRangeException.ThrowIfNegative(tier);
        Identity = identity;
        Version = version;
        Tier = tier;
        _dependencies = dependencies;
        Parameters = parameters;
    }

    readonly Func<IReadOnlyList<ProducerDependency>>? _dependencies;
    ImmutableArray<ProducerDependency> _declaredDependencies;

    /// <summary>Internal code identity; never a catalog or discovery name.</summary>
    public string Identity { get; }

    public int Version { get; }

    /// <summary>
    /// Dependency position. A declaration may depend only on its own or a
    /// lower tier.
    /// </summary>
    public int Tier { get; }

    /// <summary>
    /// Declared dependencies, evaluated once when first planned so that static
    /// declarations may refer to each other.
    /// </summary>
    public ImmutableArray<ProducerDependency> Dependencies
    {
        get
        {
            if (_declaredDependencies.IsDefault)
            {
                _declaredDependencies = _dependencies?.Invoke() is { } declared
                    ? [.. declared]
                    : [];
            }

            return _declaredDependencies;
        }
    }

    /// <summary>
    /// Canonical parameter text. Two requested declarations with the same
    /// identity must carry equal parameters.
    /// </summary>
    public string? Parameters { get; }

    public override string ToString() => Identity;
}

/// <summary>A producer declaration that publishes a <typeparamref name="TResult"/>.</summary>
public abstract class ProducerDeclaration<TResult> : ProducerDeclaration
{
    private protected ProducerDeclaration(
        string identity,
        int version,
        int tier,
        Func<IReadOnlyList<ProducerDependency>>? dependencies,
        string? parameters)
        : base(identity, version, tier, dependencies, parameters)
    {
    }
}
