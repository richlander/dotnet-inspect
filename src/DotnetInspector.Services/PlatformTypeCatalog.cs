using System.Collections.Concurrent;
using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Validated user lookup pattern for platform type discovery. It deliberately
/// remains distinct from an exact <see cref="MetadataTypeDefinitionName"/>.
/// </summary>
public sealed class PlatformTypeLookupPattern
{
    PlatformTypeLookupPattern(string normalized, bool hasExplicitGenericNotation)
    {
        NormalizedLookup = NormalizeLookup(normalized);
        HasExplicitGenericNotation = hasExplicitGenericNotation;
    }

    internal string NormalizedLookup { get; }
    internal bool HasExplicitGenericNotation { get; }

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
            new PlatformTypeLookupPattern(
                FqnParser.NormalizeTypeName(value.Trim()),
                TypeMatcher.HasExplicitGenericNotation(value)));
    }

    internal bool Matches(MetadataTypeDefinitionName name) =>
        TypeMatcher.MatchesNormalized(
            NormalizeMetadataLookup(name),
            NormalizedLookup);

    internal bool IsExact(MetadataTypeDefinitionName name)
    {
        string candidate = NormalizeMetadataLookup(name);
        return candidate.Equals(
                NormalizedLookup,
                StringComparison.OrdinalIgnoreCase)
            || (candidate.Length > NormalizedLookup.Length
                && candidate[
                    candidate.Length
                    - NormalizedLookup.Length
                    - 1] == '.'
                && candidate.EndsWith(
                    NormalizedLookup,
                    StringComparison.OrdinalIgnoreCase));
    }

    static string NormalizeLookup(string value) =>
        FqnParser.NormalizeTypeName(value).Replace('+', '.');

    static string NormalizeMetadataLookup(
        MetadataTypeDefinitionName name) =>
        name.ToMetadataFullName().Replace('+', '.');
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
    NoMetadata,
    UnsupportedMetadataFormat,
    MalformedMetadataRoot,
}

public sealed record PlatformTypeLookupFailure(
    PlatformTypeLookupFailureKind Kind,
    string Detail)
{
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

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
        Lazy<PlatformTypeCatalogResult> cachedCatalog = Cache.GetOrAdd(
            fullReferencePath,
            path => new Lazy<PlatformTypeCatalogResult>(
                () => Build(path, framework, frameworkVersion),
                LazyThreadSafetyMode.ExecutionAndPublication));
        PlatformTypeCatalogResult catalogResult = cachedCatalog.Value;
        if (catalogResult is PlatformTypeCatalogResult.Rejected rejected)
        {
            if (rejected.Failure.Kind
                == PlatformTypeLookupFailureKind.CatalogUnavailable)
            {
                // Remove only the failed Lazy observed by this caller; a
                // concurrent retry may already have installed a replacement.
                ((ICollection<KeyValuePair<
                    string,
                    Lazy<PlatformTypeCatalogResult>>>)Cache).Remove(
                        new(fullReferencePath, cachedCatalog));
            }

            return new PlatformTypeLookupOutcome.Rejected(rejected.Failure);
        }

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
        ImmutableArray<PlatformTypeLookupCandidate> exact =
        [
            .. selected.Where(candidate => pattern.IsExact(candidate.Type)),
        ];
        if (!exact.IsEmpty)
            selected = exact;
        else if (pattern.HasExplicitGenericNotation)
            return new PlatformTypeLookupOutcome.Missing();

        if (selected.Length > 1
            && selected
                .Select(candidate =>
                    candidate.Type.ToMetadataFullName())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1)
        {
            var resolvedTypeName =
                selected[0].Type.ToMetadataFullName();
            var assemblyPrefixMatches = selected
                .Where(candidate =>
                    resolvedTypeName.StartsWith(
                        candidate.Assembly.Identity.Name + ".",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate =>
                    candidate.Assembly.Identity.Name.Length)
                .ToArray();
            if (assemblyPrefixMatches.Length > 0
                && (assemblyPrefixMatches.Length == 1
                    || assemblyPrefixMatches[0]
                            .Assembly.Identity.Name.Length
                        > assemblyPrefixMatches[1]
                            .Assembly.Identity.Name.Length))
            {
                return new PlatformTypeLookupOutcome.Resolved(
                    assemblyPrefixMatches[0]);
            }
        }

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
            PlatformTypeLookupFailure? retainedFailure = null;
            string[] assemblyPaths =
            [
                .. Directory
                .EnumerateFiles(referencePath, "*.dll")
                .OrderBy(static path => path, StringComparer.Ordinal),
            ];
            if (assemblyPaths.Length == 0)
            {
                return Rejected(
                    PlatformTypeLookupFailureKind.CatalogUnavailable,
                    "The platform reference catalog contains no assemblies.");
            }

            foreach (string path in assemblyPaths)
            {
                ResolvedAssemblyReference? assembly;
                try
                {
                    assembly =
                        ResolvedAssemblyReference.CreateFromPathIfManaged(
                            path,
                            AssemblyResolutionProvenance.Platform(
                                framework,
                                frameworkVersion,
                                "PlatformTypeCatalog"));
                    if (assembly is null)
                    {
                        RetainFailure(
                            ref retainedFailure,
                            new PlatformTypeLookupFailure(
                                PlatformTypeLookupFailureKind.NoMetadata,
                                "A platform reference assembly contains no managed metadata."));
                        continue;
                    }
                }
                catch (UnsupportedMetadataFormatException ex)
                {
                    RetainFailure(
                        ref retainedFailure,
                        new PlatformTypeLookupFailure(
                            PlatformTypeLookupFailureKind
                                .UnsupportedMetadataFormat,
                            ex.Message));
                    continue;
                }
                catch (MalformedMetadataRootException ex)
                {
                    RetainFailure(
                        ref retainedFailure,
                        new PlatformTypeLookupFailure(
                            PlatformTypeLookupFailureKind
                                .MalformedMetadataRoot,
                            ex.Message)
                        {
                            MetadataRootReason = ex.Reason,
                        });
                    continue;
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or OverflowException)
                {
                    RetainFailure(
                        ref retainedFailure,
                        new PlatformTypeLookupFailure(
                            PlatformTypeLookupFailureKind.InvalidAssembly,
                            "A platform reference assembly contains invalid metadata."));
                    continue;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException)
                {
                    RetainFailure(
                        ref retainedFailure,
                        new PlatformTypeLookupFailure(
                            PlatformTypeLookupFailureKind.CatalogUnavailable,
                            "A platform reference assembly could not be read."));
                    continue;
                }

                AssemblyTypeDeclarationInventoryOutcome inventory =
                    AssemblyTypeDeclarationInventoryReader.Read(assembly);
                if (inventory
                    is not AssemblyTypeDeclarationInventoryOutcome.Read read)
                {
                    var rejected =
                        (AssemblyTypeDeclarationInventoryOutcome.Rejected)
                            inventory;
                    RetainFailure(
                        ref retainedFailure,
                        PlatformFailure(rejected.Failure));
                    continue;
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

            if (retainedFailure is not null)
                return new PlatformTypeCatalogResult.Rejected(
                    retainedFailure);

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
                or UnauthorizedAccessException)
        {
            return Rejected(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                "The platform reference catalog could not be built.");
        }
    }

    internal static bool ShouldReplaceFailure(
        PlatformTypeLookupFailure? retained,
        PlatformTypeLookupFailure candidate) =>
        retained is null
        || FailurePrecedence(candidate)
            > FailurePrecedence(retained);

    static void RetainFailure(
        ref PlatformTypeLookupFailure? retained,
        PlatformTypeLookupFailure candidate)
    {
        if (ShouldReplaceFailure(retained, candidate))
            retained = candidate;
    }

    static int FailurePrecedence(
        PlatformTypeLookupFailure failure) =>
        failure.Kind switch
        {
            PlatformTypeLookupFailureKind.InvalidPattern => 3,
            PlatformTypeLookupFailureKind
                .UnsupportedMetadataFormat
                or PlatformTypeLookupFailureKind
                    .MalformedMetadataRoot => 2,
            PlatformTypeLookupFailureKind.NoMetadata
                or PlatformTypeLookupFailureKind.InvalidAssembly => 1,
            _ => 0,
        };

    static PlatformTypeLookupFailure PlatformFailure(
        CandidateOpenFailure failure) =>
        failure.Kind switch
        {
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                new PlatformTypeLookupFailure(
                    PlatformTypeLookupFailureKind
                        .UnsupportedMetadataFormat,
                    failure.Detail),
            CandidateOpenFailureKind.InvalidImage
                when failure.MetadataRootReason is not null =>
                    new PlatformTypeLookupFailure(
                        PlatformTypeLookupFailureKind
                            .MalformedMetadataRoot,
                        failure.Detail)
                    {
                        MetadataRootReason =
                            failure.MetadataRootReason,
                    },
            CandidateOpenFailureKind.InvalidImage =>
                new PlatformTypeLookupFailure(
                    PlatformTypeLookupFailureKind.InvalidAssembly,
                    failure.Detail),
            _ => new PlatformTypeLookupFailure(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                failure.Detail),
        };

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
