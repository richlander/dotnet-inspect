using System.Collections.Immutable;
using CSharpText;
using DotnetInspector.PlatformHouse;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformQueries;

/// <summary>Reason that user text could not form a Platform type query.</summary>
public enum PlatformTypeCatalogQueryRejectionKind
{
    EmptyPattern,
    PatternTooLong,
}

/// <summary>Typed terminal outcome for one Platform type catalog query.</summary>
public abstract class PlatformTypeCatalogQueryOutcome
{
    private protected PlatformTypeCatalogQueryOutcome(
        PlatformTypeCatalog catalog) =>
        Catalog = catalog;

    /// <summary>The exact completed catalog queried by this operation.</summary>
    public PlatformTypeCatalog Catalog { get; }

    /// <summary>One exact preferred declaration candidate.</summary>
    public sealed class Resolved : PlatformTypeCatalogQueryOutcome
    {
        internal Resolved(
            PlatformTypeCatalog catalog,
            PlatformTypeCatalogEntry candidate)
            : base(catalog) =>
            Candidate = candidate;

        public PlatformTypeCatalogEntry Candidate { get; }
    }

    /// <summary>Multiple equally preferred declaration candidates.</summary>
    public sealed class Ambiguous : PlatformTypeCatalogQueryOutcome
    {
        internal Ambiguous(
            PlatformTypeCatalog catalog,
            ImmutableArray<PlatformTypeCatalogEntry> candidates)
            : base(catalog) =>
            Candidates = candidates;

        public ImmutableArray<PlatformTypeCatalogEntry> Candidates { get; }
    }

    /// <summary>No declaration in the complete catalog matched the query.</summary>
    public sealed class Missing : PlatformTypeCatalogQueryOutcome
    {
        internal Missing(PlatformTypeCatalog catalog)
            : base(catalog)
        {
        }
    }

    /// <summary>The supplied user text could not form a query.</summary>
    public sealed class Rejected : PlatformTypeCatalogQueryOutcome
    {
        internal Rejected(
            PlatformTypeCatalog catalog,
            PlatformTypeCatalogQueryRejectionKind kind)
            : base(catalog) =>
            Kind = kind;

        public PlatformTypeCatalogQueryRejectionKind Kind { get; }
    }
}

/// <summary>
/// Adapts user type text to exact declarations in one completed Platform
/// catalog.
/// </summary>
public static partial class PlatformTypeCatalogQuery
{
    public static PlatformTypeCatalogQueryOutcome Execute(
        PlatformTypeCatalog catalog,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pattern);
        return Execute(
            catalog,
            ResolvePattern(pattern, cancellationToken),
            cancellationToken);
    }

    public static PlatformTypeCatalogQueryOutcome Execute(
        PlatformTypeCatalog catalog,
        PlatformTypeCatalogQueryPlanResult resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resolution);
        return resolution switch
        {
            PlatformTypeCatalogQueryPlanResult.Accepted accepted =>
                Execute(catalog, accepted.Plan, cancellationToken),
            PlatformTypeCatalogQueryPlanResult.Rejected rejected =>
                Publish(
                    new PlatformTypeCatalogQueryOutcome.Rejected(
                        catalog,
                        rejected.Kind),
                    cancellationToken),
            _ => throw new InvalidOperationException(
                "Unknown Platform type catalog query plan result."),
        };
    }

    public static PlatformTypeCatalogQueryOutcome Execute(
        PlatformTypeCatalog catalog,
        PlatformTypeCatalogQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var matches =
            ImmutableArray.CreateBuilder<PlatformTypeCatalogEntry>();

        foreach (PlatformTypeCatalogEntry entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalizedCandidate = Normalize(entry.Name);
            if (TypeMatcher.MatchesNormalized(
                    normalizedCandidate,
                    plan.NormalizedPattern))
            {
                matches.Add(entry);
            }
        }

        ImmutableArray<PlatformTypeCatalogEntry> preferred =
            matches.ToImmutable();
        ImmutableArray<PlatformTypeCatalogEntry> exact =
            PreferExactMatches(
                preferred,
                plan.NormalizedPattern,
                cancellationToken);
        if (!exact.IsDefaultOrEmpty)
        {
            preferred = exact;
        }
        else if (plan.HasExplicitGenericNotation)
        {
            preferred = [];
        }
        preferred = PreferTopLevelDeclarations(
            preferred,
            cancellationToken);
        preferred = PreferDefinitions(
            preferred,
            cancellationToken);

        PlatformTypeCatalogQueryOutcome outcome = preferred.Length switch
        {
            0 => new PlatformTypeCatalogQueryOutcome.Missing(catalog),
            1 => new PlatformTypeCatalogQueryOutcome.Resolved(
                catalog,
                preferred[0]),
            _ => new PlatformTypeCatalogQueryOutcome.Ambiguous(
                catalog,
                preferred),
        };
        return Publish(outcome, cancellationToken);
    }

    private static ImmutableArray<PlatformTypeCatalogEntry>
        PreferTopLevelDeclarations(
            ImmutableArray<PlatformTypeCatalogEntry> candidates,
            CancellationToken cancellationToken)
    {
        var topLevel =
            ImmutableArray.CreateBuilder<PlatformTypeCatalogEntry>();
        foreach (PlatformTypeCatalogEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Name.Segments.Length == 1)
            {
                topLevel.Add(candidate);
            }
        }

        return topLevel.Count == 0
            ? candidates
            : topLevel.ToImmutable();
    }

    private static ImmutableArray<PlatformTypeCatalogEntry> PreferDefinitions(
        ImmutableArray<PlatformTypeCatalogEntry> candidates,
        CancellationToken cancellationToken)
    {
        var definitions =
            ImmutableArray.CreateBuilder<PlatformTypeCatalogEntry>();
        foreach (PlatformTypeCatalogEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Kind
                == AssemblyTypeDeclarationKind.Definition)
            {
                definitions.Add(candidate);
            }
        }

        return definitions.Count == 0
            ? candidates
            : definitions.ToImmutable();
    }

    private static ImmutableArray<PlatformTypeCatalogEntry> PreferExactMatches(
        ImmutableArray<PlatformTypeCatalogEntry> candidates,
        string normalizedPattern,
        CancellationToken cancellationToken)
    {
        var exact =
            ImmutableArray.CreateBuilder<PlatformTypeCatalogEntry>();
        string dottedSuffix = $".{normalizedPattern}";
        foreach (PlatformTypeCatalogEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalizedCandidate = Normalize(candidate.Name);
            if (normalizedCandidate.Equals(
                    normalizedPattern,
                    StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.EndsWith(
                    dottedSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(candidate);
            }
        }

        return exact.ToImmutable();
    }

    private static string Normalize(MetadataTypeDefinitionName name)
    {
        string typeName = string.Join('.', name.Segments);
        return name.Namespace.Length == 0
            ? typeName
            : $"{name.Namespace}.{typeName}";
    }

    private static T Publish<T>(
        T outcome,
        CancellationToken cancellationToken)
        where T : PlatformTypeCatalogQueryOutcome
    {
        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }
}
