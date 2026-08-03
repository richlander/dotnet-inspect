using System.Collections.Concurrent;
using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Validated user lookup pattern for platform type discovery. It deliberately
/// remains distinct from an exact <see cref="MetadataTypeDefinitionName"/>.
/// </summary>
public sealed class PlatformTypeLookupPattern
{
    PlatformTypeLookupPattern(string normalized) => Normalized = normalized;

    internal string Normalized { get; }

    public static PlatformTypeLookupPatternResult Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new PlatformTypeLookupPatternResult.Rejected(
                new PlatformTypeLookupFailure(
                    PlatformTypeLookupFailureKind.InvalidPattern,
                    "A platform type lookup pattern is required."));
        }

        return new PlatformTypeLookupPatternResult.Valid(
            new PlatformTypeLookupPattern(TypeMatcher.Normalize(value.Trim())));
    }

    internal bool Matches(MetadataTypeDefinitionName name) =>
        TypeMatcher.Matches(name.ToMetadataFullName(), Normalized);

    internal bool IsExact(MetadataTypeDefinitionName name)
    {
        string candidate = NormalizeLookup(name.ToMetadataFullName());
        string pattern = NormalizeLookup(Normalized);
        return candidate.Equals(pattern, StringComparison.OrdinalIgnoreCase)
            || TypeMatcher.GetSimpleName(candidate).Equals(
                pattern,
                StringComparison.OrdinalIgnoreCase);
    }

    internal int GenericArity => TypeMatcher.GetPatternArity(Normalized);

    static string NormalizeLookup(string value) =>
        TypeMatcher.Normalize(value).Replace('+', '.');
}

/// <summary>The result of validating a platform type lookup pattern.</summary>
public abstract class PlatformTypeLookupPatternResult
{
    private protected PlatformTypeLookupPatternResult()
    {
    }

    public sealed class Valid : PlatformTypeLookupPatternResult
    {
        internal Valid(PlatformTypeLookupPattern pattern) => Pattern = pattern;

        public PlatformTypeLookupPattern Pattern { get; }
    }

    public sealed class Rejected : PlatformTypeLookupPatternResult
    {
        internal Rejected(PlatformTypeLookupFailure failure) => Failure = failure;

        public PlatformTypeLookupFailure Failure { get; }
    }
}

public enum PlatformTypeDeclarationKind
{
    Definition,
    Forwarder,
}

/// <summary>
/// One deterministic platform-catalog match. The descriptor, structured type
/// name, and declaration kind remain separate evidence.
/// </summary>
public sealed record PlatformTypeLookupCandidate(
    ResolvedAssemblyReference Assembly,
    MetadataTypeDefinitionName Type,
    PlatformTypeDeclarationKind DeclarationKind);

public enum PlatformTypeLookupFailureKind
{
    InvalidPattern,
    CatalogUnavailable,
    InvalidAssembly,
}

public sealed record PlatformTypeLookupFailure(
    PlatformTypeLookupFailureKind Kind,
    string Detail);

/// <summary>
/// Closed platform source-selection outcome. Only <see cref="Resolved"/>
/// supplies a descriptor; callers must handle ambiguity and rejection.
/// </summary>
public abstract class PlatformTypeLookupOutcome
{
    private protected PlatformTypeLookupOutcome()
    {
    }

    public sealed class Resolved : PlatformTypeLookupOutcome
    {
        internal Resolved(PlatformTypeLookupCandidate candidate) =>
            Candidate = candidate;

        public PlatformTypeLookupCandidate Candidate { get; }
    }

    public sealed class Missing : PlatformTypeLookupOutcome
    {
        internal Missing()
        {
        }
    }

    public sealed class Ambiguous : PlatformTypeLookupOutcome
    {
        internal Ambiguous(
            ImmutableArray<PlatformTypeLookupCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<PlatformTypeLookupCandidate> Candidates { get; }
    }

    public sealed class Rejected : PlatformTypeLookupOutcome
    {
        internal Rejected(PlatformTypeLookupFailure failure) => Failure = failure;

        public PlatformTypeLookupFailure Failure { get; }
    }
}

/// <summary>
/// Trusted reference-pack index. Metadata produces each declaration inventory;
/// this service owns platform discovery, caching, and source-selection policy.
/// </summary>
internal sealed class PlatformTypeCatalog
{
    static readonly ConcurrentDictionary<
        string,
        Lazy<PlatformTypeCatalogResult>> Cache =
            new(StringComparer.Ordinal);

    readonly ImmutableArray<PlatformTypeLookupCandidate> _entries;

    PlatformTypeCatalog(
        ImmutableArray<PlatformTypeLookupCandidate> entries) =>
        _entries = entries;

    internal static PlatformTypeLookupOutcome Lookup(
        string typeName,
        string referencePath,
        string framework,
        string frameworkVersion)
    {
        PlatformTypeLookupPatternResult patternResult =
            PlatformTypeLookupPattern.Create(typeName);
        if (patternResult is PlatformTypeLookupPatternResult.Rejected invalid)
            return new PlatformTypeLookupOutcome.Rejected(invalid.Failure);

        string fullReferencePath = Path.GetFullPath(referencePath);
        PlatformTypeCatalogResult catalogResult = Cache.GetOrAdd(
            fullReferencePath,
            path => new Lazy<PlatformTypeCatalogResult>(
                () => Build(path, framework, frameworkVersion),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (catalogResult is PlatformTypeCatalogResult.Rejected rejected)
            return new PlatformTypeLookupOutcome.Rejected(rejected.Failure);

        var catalog =
            ((PlatformTypeCatalogResult.Ready)catalogResult).Catalog;
        return catalog.Select(
            ((PlatformTypeLookupPatternResult.Valid)patternResult).Pattern);
    }

    PlatformTypeLookupOutcome Select(PlatformTypeLookupPattern pattern)
    {
        ImmutableArray<PlatformTypeLookupCandidate> matches =
        [
            .. _entries.Where(candidate => pattern.Matches(candidate.Type)),
        ];
        if (matches.IsEmpty)
            return new PlatformTypeLookupOutcome.Missing();

        ImmutableArray<PlatformTypeLookupCandidate> definitions =
        [
            .. matches.Where(candidate =>
                candidate.DeclarationKind
                    == PlatformTypeDeclarationKind.Definition),
        ];
        ImmutableArray<PlatformTypeLookupCandidate> selected =
            definitions.IsEmpty ? matches : definitions;
        if (pattern.GenericArity >= 0)
        {
            ImmutableArray<PlatformTypeLookupCandidate> sameArity =
            [
                .. selected.Where(candidate =>
                    TypeMatcher.GetGenericArity(
                        candidate.Type.ToMetadataFullName())
                    == pattern.GenericArity),
            ];
            if (!sameArity.IsEmpty)
                selected = sameArity;
        }

        ImmutableArray<PlatformTypeLookupCandidate> exact =
        [
            .. selected.Where(candidate => pattern.IsExact(candidate.Type)),
        ];
        if (!exact.IsEmpty)
            selected = exact;

        return selected.Length == 1
            ? new PlatformTypeLookupOutcome.Resolved(selected[0])
            : new PlatformTypeLookupOutcome.Ambiguous(selected);
    }

    static PlatformTypeCatalogResult Build(
        string referencePath,
        string framework,
        string frameworkVersion)
    {
        if (!Directory.Exists(referencePath))
        {
            return Rejected(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                "The platform reference catalog is unavailable.");
        }

        try
        {
            var entries =
                ImmutableArray.CreateBuilder<PlatformTypeLookupCandidate>();
            foreach (string path in Directory
                .EnumerateFiles(referencePath, "*.dll")
                .OrderBy(static path => path, StringComparer.Ordinal))
            {
                ResolvedAssemblyReference assembly =
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Platform(
                            framework,
                            frameworkVersion,
                            "PlatformTypeCatalog"));
                if (AssemblyTypeDeclarationInventoryReader.Read(assembly)
                    is not AssemblyTypeDeclarationInventoryOutcome.Read read)
                {
                    return Rejected(
                        PlatformTypeLookupFailureKind.InvalidAssembly,
                        "A platform reference assembly could not be inventoried.");
                }

                foreach (MetadataTypeDefinitionName definition
                    in read.Inventory.Definitions)
                {
                    entries.Add(new PlatformTypeLookupCandidate(
                        assembly,
                        definition,
                        PlatformTypeDeclarationKind.Definition));
                }

                foreach (MetadataTypeDefinitionName forwarder
                    in read.Inventory.Forwarders)
                {
                    entries.Add(new PlatformTypeLookupCandidate(
                        assembly,
                        forwarder,
                        PlatformTypeDeclarationKind.Forwarder));
                }
            }

            return new PlatformTypeCatalogResult.Ready(
                new PlatformTypeCatalog(
                    entries
                        .OrderBy(
                            static candidate => candidate.Assembly.Identity.Name,
                            StringComparer.Ordinal)
                        .ThenBy(
                            static candidate =>
                                candidate.Type.ToMetadataFullName(),
                            StringComparer.Ordinal)
                        .ThenBy(
                            static candidate => candidate.DeclarationKind)
                        .ThenBy(
                            static candidate => candidate.Assembly.Path,
                            StringComparer.Ordinal)
                        .ToImmutableArray()));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            return Rejected(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                "The platform reference catalog could not be built.");
        }
    }

    static PlatformTypeCatalogResult.Rejected Rejected(
        PlatformTypeLookupFailureKind kind,
        string detail) =>
        new(new PlatformTypeLookupFailure(kind, detail));

    abstract class PlatformTypeCatalogResult
    {
        private protected PlatformTypeCatalogResult()
        {
        }

        internal sealed class Ready : PlatformTypeCatalogResult
        {
            internal Ready(PlatformTypeCatalog catalog) => Catalog = catalog;

            internal PlatformTypeCatalog Catalog { get; }
        }

        internal sealed class Rejected : PlatformTypeCatalogResult
        {
            internal Rejected(PlatformTypeLookupFailure failure) =>
                Failure = failure;

            internal PlatformTypeLookupFailure Failure { get; }
        }
    }
}
