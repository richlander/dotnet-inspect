using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using DotnetInspector.Packages;
using DotnetInspector.PortableQueries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The acquisition envelope required by one package-query term.</summary>
public enum PackageQueryAcquisitionTier
{
    Nuspec,
    PackageContent,
    SearchMetadata,
}

/// <summary>A bounded package-query request over one package input.</summary>
public sealed record PackageQueryRequest(
    string Input,
    IReadOnlyCollection<PortableQueryTerm>? Terms = null,
    int MaximumCandidates = PackageQuery.DefaultMaximumCandidates,
    int? MaximumMatches = PackageQuery.DefaultMaximumMatches,
    bool IncludePrerelease = false,
    RowSelectionIntent<string>? RowSelection = null);

public enum PackageQueryDependencyTargetKind
{
    All,
    TargetFramework,
}

/// <summary>The dependency-group scope applied by one Package Query plan.</summary>
public sealed record PackageQueryDependencyTarget
{
    private PackageQueryDependencyTarget(
        PackageQueryDependencyTargetKind kind,
        string? requestedTargetFramework)
    {
        Kind = kind;
        RequestedTargetFramework = requestedTargetFramework;
    }

    public PackageQueryDependencyTargetKind Kind { get; }

    public string? RequestedTargetFramework { get; }

    public static PackageQueryDependencyTarget All { get; } =
        new(PackageQueryDependencyTargetKind.All, null);

    internal static PackageQueryDependencyTarget ForTargetFramework(
        string canonicalFramework) =>
        new(
            PackageQueryDependencyTargetKind.TargetFramework,
            canonicalFramework);
}

/// <summary>Why a package-query request could not become an executable plan.</summary>
public enum PackageQueryRequestFailureReason
{
    InvalidCandidateLimit,
    InvalidMatchLimit,
    TooManyTerms,
    InvalidPackageInput,
    UnknownVocabulary,
    UnknownTerm,
    TermOperatorNotAdmitted,
    InvalidTermValue,
    DuplicateTerm,
    IncompatibleTerms,
    DependencyTargetRequiresDependencyPredicate,
    RequiredPopulationMissing,
    RequiredPrereleaseMissing,
    RequiredCandidateBoundMissing,
    UnknownBound,
    StageNotAdmitted,
    OrderNotSupported,
}

/// <summary>
/// A typed, content-safe package-query planning failure. Returned term keys are
/// always product-issued.
/// </summary>
public sealed record PackageQueryRequestFailure
{
    internal PackageQueryRequestFailure(
        PackageQueryRequestFailureReason reason,
        IEnumerable<string>? termKeys = null,
        int? value = null,
        PortableQueryFailure? portableFailure = null)
    {
        Reason = reason;
        TermKeys = termKeys is null ? [] : [.. termKeys];
        Value = value;
        PortableFailure = portableFailure;
    }

    public PackageQueryRequestFailureReason Reason { get; }
    public ImmutableArray<string> TermKeys { get; }
    public int? Value { get; }
    public PortableQueryFailure? PortableFailure { get; }

    public string Message => Reason switch
    {
        PackageQueryRequestFailureReason.InvalidPackageInput =>
            "Enter a package ID or a literal package-ID prefix followed by one '*'.",
        PackageQueryRequestFailureReason.InvalidCandidateLimit =>
            $"The package-query candidate limit must be between 1 and {PackageQuery.MaximumCandidates}; "
            + $"package-content terms admit at most {PackageQuery.MaximumPackageContentCandidates} candidates.",
        PackageQueryRequestFailureReason.InvalidMatchLimit =>
            $"The package-query match limit must be between 1 and {PackageQuery.MaximumCandidates}.",
        PackageQueryRequestFailureReason.TooManyTerms =>
            $"Package Query admits at most {PackageQuery.MaximumInspectionTerms} inspection terms "
            + $"so its complete intent remains within {PortableQueryPayloadCodec.MaxTerms} portable terms.",
        PackageQueryRequestFailureReason.UnknownVocabulary =>
            "The Package Query vocabulary is not available in this build.",
        PackageQueryRequestFailureReason.UnknownTerm =>
            $"Package Query does not define term '{PortableFailure?.Offender}'.",
        PackageQueryRequestFailureReason.TermOperatorNotAdmitted =>
            "A package-query term uses an operator that its key does not admit.",
        PackageQueryRequestFailureReason.InvalidTermValue =>
            "A package-query term value is invalid.",
        PackageQueryRequestFailureReason.DuplicateTerm =>
            "Two package-query terms resolve to the same predicate.",
        PackageQueryRequestFailureReason.IncompatibleTerms =>
            "The selected package-query terms cannot be combined.",
        PackageQueryRequestFailureReason.DependencyTargetRequiresDependencyPredicate =>
            "dependency-target requires a depends or dependencies term.",
        PackageQueryRequestFailureReason.RequiredPopulationMissing =>
            "Package Query requires exactly one package or prefix population term.",
        PackageQueryRequestFailureReason.RequiredPrereleaseMissing =>
            "Package Query requires an explicit stable or include-prerelease term.",
        PackageQueryRequestFailureReason.RequiredCandidateBoundMissing =>
            "Package Query requires an explicit candidate bound.",
        PackageQueryRequestFailureReason.UnknownBound =>
            $"Package Query does not define bound '{PortableFailure?.Offender}'.",
        PackageQueryRequestFailureReason.StageNotAdmitted =>
            "Package Query supports Head, Tail, and Window selection, but not Top.",
        PackageQueryRequestFailureReason.OrderNotSupported =>
            "Package Query does not define an ordering namespace.",
        _ => "The package-query request is invalid.",
    };
}

/// <summary>The result of validating and lowering one package-query request.</summary>
public abstract record PackageQueryPlanResult
{
    private PackageQueryPlanResult()
    {
    }

    public sealed record Accepted(PackageQueryPlan Plan) : PackageQueryPlanResult;
    public sealed record Rejected(PackageQueryRequestFailure Failure) : PackageQueryPlanResult;
}

/// <summary>
/// One validated package-query plan. Construction is product-owned so
/// execution cannot receive unknown or incompatible predicates.
/// </summary>
public sealed class PackageQueryPlan
{
    internal PackageQueryPlan(
        PortableQueryIntent intent,
        InertString prefix,
        ImmutableArray<BoundPackageQueryTerm> terms,
        PackageQueryDependencyTarget dependencyTarget,
        int maximumCandidates,
        int? maximumMatches,
        bool includePrerelease,
        RowSelectionIntent<string> rowSelection,
        SourceSelector packageInput)
    {
        Intent = intent;
        Prefix = prefix;
        BoundTerms = terms;
        Terms = [.. terms.Select(term => term.Term)];
        DependencyTarget = dependencyTarget;
        MaximumCandidates = maximumCandidates;
        MaximumMatches = maximumMatches;
        IncludePrerelease = includePrerelease;
        RowSelection = rowSelection;
        PackageInput = packageInput;
    }

    public PortableQueryIntent Intent { get; }
    public InertString Prefix { get; }
    public ImmutableArray<PortableQueryTerm> Terms { get; }
    public PackageQueryDependencyTarget DependencyTarget { get; }
    public int MaximumCandidates { get; }
    public int? MaximumMatches { get; }
    public bool IncludePrerelease { get; }
    public RowSelectionIntent<string> RowSelection { get; }
    public SourceSelector PackageInput { get; }
    public bool RequiresPackageContent =>
        BoundTerms.Any(term => term.Predicate.RequiresPackageContent);
    internal bool RequiresSearchMetadata =>
        BoundTerms.Any(term =>
            term.Descriptor.Tier == PackageQueryAcquisitionTier.SearchMetadata);
    public bool RequiresManifest =>
        BoundTerms.Any(term =>
            term.Descriptor.Tier is PackageQueryAcquisitionTier.Nuspec
                or PackageQueryAcquisitionTier.PackageContent);

    internal ImmutableArray<BoundPackageQueryTerm> BoundTerms { get; }
    internal bool HasDependencyPredicate =>
        BoundTerms.Any(term =>
            term.Predicate.Kind is PackageQueryPredicateKind.NoDependencies
                or PackageQueryPredicateKind.Depends);
    internal bool HasExplicitDependencyTarget =>
        BoundTerms.Any(term =>
            term.Predicate.Kind == PackageQueryPredicateKind.DependencyTarget);
    internal bool HasDependencyTerms =>
        HasDependencyPredicate || HasExplicitDependencyTarget;
}

/// <summary>Whether evidence describes the query input or an inspected package.</summary>
public enum PackageQueryEvidenceScope
{
    Package,
    Query,
}

/// <summary>A complete observed item count and bounded inert display previews.</summary>
public sealed record PackageQueryEvidenceSummary(
    int Count,
    ImmutableArray<InertString> Preview);

/// <summary>One semantic answer produced by a matched package-query term.</summary>
public sealed record PackageQueryAnswer(
    string Id,
    InertString ValueText)
{
    public PortableQueryTerm? Term { get; init; }
    public string Value => ValueText.ToString();
}

/// <summary>One named structured fact supporting a package-query answer.</summary>
public sealed record PackageQueryEvidenceProperty(
    string Name,
    InertString ValueText)
{
    public string Value => ValueText.ToString();
}

/// <summary>Structured supporting data for a package-query match.</summary>
public sealed record PackageQueryEvidence(
    string Id)
{
    public PackageQueryEvidenceScope Scope { get; init; }
    public PackageQueryEvidenceSummary? Summary { get; init; }
    public ImmutableArray<PackageQueryEvidenceProperty> Properties { get; init; } = [];
    public long? Number { get; init; }
    public PortableQueryTerm? Term { get; init; }
}

/// <summary>One package that satisfied every selected package-query term.</summary>
public sealed record PackageQueryMatch(
    PackageQueryPackage Package,
    PackageQueryAcquisitionTier Tier,
    ImmutableArray<PackageQueryAnswer> Answers,
    ImmutableArray<PackageQueryEvidence> Evidence)
{
    public PackageQueryMatch(
        PackageProfileMatch Package,
        PackageQueryAcquisitionTier Tier,
        ImmutableArray<PackageQueryAnswer> Answers,
        ImmutableArray<PackageQueryEvidence> Evidence)
        : this(new PackageQueryPackage(Package), Tier, Answers, Evidence)
    {
    }
}

/// <summary>The stage at which one package-query item failed.</summary>
public enum PackageQueryFailureKind
{
    Search,
    SearchContract,
    ManifestAcquisition,
    ManifestContract,
    InvalidManifest,
    PackageContentAcquisition,
    PackageContentEvaluation,
}

/// <summary>One visible package-query failure.</summary>
public sealed record PackageQueryFailure(
    string? PackageId,
    string? Version,
    PackageSourceResultIdentity Source,
    PackageQueryFailureKind Kind,
    string Message,
    PackageManifestFailureReason? ManifestFailureReason = null);

/// <summary>Why one package-query stream stopped.</summary>
public enum PackageQueryCompletionKind
{
    Exhausted,
    MatchLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Failed,
    ExactPackageComplete,
}

/// <summary>Terminal accounting for one package-query stream.</summary>
public sealed record PackageQuerySummary(
    InertString Prefix,
    PackageSourceResultIdentity Source,
    int CandidateLimit,
    int? MatchLimit,
    int Candidates,
    int Matches,
    int Failures,
    PackageQueryCompletionKind Completion)
{
    public int? SourceCandidates { get; init; }
}

/// <summary>
/// The settled semantic content of one completed Package Query operation.
/// </summary>
public sealed record PackageQueryDocument(
    ImmutableArray<PackageQueryMatch> Results,
    ImmutableArray<PackageQueryFailure> Failures,
    PackageQuerySummary Summary)
{
    public bool HasPackages => !Results.IsEmpty;
}

/// <summary>A bounded checkpoint in package-query work.</summary>
public sealed record PackageQueryProgress(
    PackageQueryProgressPhase Phase,
    int Completed,
    int Limit);

/// <summary>The user-meaningful phase represented by package-query progress.</summary>
public enum PackageQueryProgressPhase
{
    Search,
    Manifest,
    PackageContent,
}

/// <summary>One event from a package-query stream.</summary>
public abstract record PackageQueryEvent
{
    private PackageQueryEvent()
    {
    }

    /// <summary>An operation event that may be published before settlement.</summary>
    public abstract record Nonterminal : PackageQueryEvent
    {
        private protected Nonterminal()
        {
        }
    }

    public sealed record Progress(PackageQueryProgress Value)
        : Nonterminal;

    public sealed record Match(PackageQueryMatch Value) : Nonterminal;
    public sealed record Failure(PackageQueryFailure Value) : Nonterminal;
    public sealed record Completed(PackageQuerySummary Value) : PackageQueryEvent;
}

/// <summary>The result of acquiring admitted package content for one query candidate.</summary>
public abstract record PackageQueryContentResult
{
    private PackageQueryContentResult()
    {
    }

    public sealed record Available(IPackageContent Content)
        : PackageQueryContentResult;

    public sealed record Unavailable(string Message)
        : PackageQueryContentResult;
}

/// <summary>
/// Host capability for acquiring one exact candidate's admitted package content.
/// </summary>
public interface IPackageQueryContentProvider
{
    ValueTask<PackageQueryContentResult> GetContentAsync(
        PackageQueryPackage package,
        CancellationToken cancellationToken);
}

internal sealed record BoundPackageQueryTerm(
    PackageQueryTermDescriptor Descriptor,
    PortableQueryTerm Term,
    PackageQueryPredicate Predicate);

internal sealed record PackageQueryTermResult(
    PackageQueryAnswer Answer,
    PackageQueryEvidence Evidence);

internal sealed record PackageQueryDependencySelection(
    PackageQueryDependencyTarget Target,
    ImmutableArray<DeclaredPackageDependencyGroup> Groups,
    PackageDependencyGroupSelectionStatus? SelectionStatus,
    DeclaredPackageDependencyGroup? SelectedGroup);

internal sealed record PackageQueryDependencyMatch(
    DeclaredPackageDependencyGroup Group,
    DeclaredPackageDependency Dependency);

internal sealed record PackageContentFacts(
    PackageQueryEvidenceSummary? SkillDocuments,
    string? ToolSettingsVersion);

/// <summary>
/// Plans and executes product-owned manifest and package-content terms over a
/// bounded package profile without loading inspected assemblies.
/// </summary>
public static partial class PackageQuery
{
    private const int RequiredPortableTermCount = 2;

    public const int DefaultMaximumCandidates = 200;
    public const int DefaultMaximumMatches = 100;
    public const int MaximumCandidates = 1_000;
    public const int MaximumPackageContentCandidates = 20;
    public const int MaximumInspectionTerms =
        PortableQueryPayloadCodec.MaxTerms - RequiredPortableTermCount;
    public const int MaximumToolSettingsBytes = 64 * 1024;
    public const int MaximumEvidencePreviewItems = 3;
    public const int MaximumEvidencePreviewCharacters = 160;

    public const string PrefixEvidenceId = "package.query.scope.prefix";
    public const string ExactPackageEvidenceId = "package.query.scope.exact-package";
    public const string PackageTermKey = "package";
    public const string PrefixTermKey = "prefix";
    public const string PrereleaseTermKey = "prerelease";
    public const string DependenciesTermKey = "dependencies";
    public const string DependencyTargetTermKey = "dependency-target";
    public const string DependencyTargetAllValue = "all";
    public const string DependsTermKey = "depends";
    public const string DownloadsTermKey = "downloads";
    public const string LicenseTermKey = "license";
    public const string ReadmeTermKey = "readme";
    public const string ToolTermKey = "tool";
    public const string ToolFormatTermKey = "tool-format";
    public const string SkillTermKey = "skill";
    public const string ToolReplacementGroupId =
        "package.query.replacement.dotnet-tool";
    public const string ToolDisplayGroupId = "package.query.display.dotnet-tool";

    private static readonly ImmutableArray<string> EqualityOperator =
        [PortableQueryModel.TextOf(PortableQueryOperator.Equal)];

    /// <summary>The complete ordered Package Query vocabulary.</summary>
    public static ImmutableArray<PackageQueryTermDescriptor> Terms { get; } =
    [
        new(
            PackageTermKey,
            "exact package",
            "Selects one exact package ID.",
            10,
            PackageQueryAcquisitionTier.SearchMetadata,
            EqualityOperator,
            "NuGet package ID",
            "Newtonsoft.Json",
            PackageQueryTermRole.Population,
            PackageQueryTermControlKind.Input)
        {
            SelectionGroupId = PackageQueryVocabulary.PopulationFamily,
        },
        new(
            PrefixTermKey,
            "package prefix",
            "Selects package IDs beginning with one literal prefix.",
            20,
            PackageQueryAcquisitionTier.SearchMetadata,
            EqualityOperator,
            "NuGet package ID prefix",
            "Microsoft.Extensions.",
            PackageQueryTermRole.Population,
            PackageQueryTermControlKind.Input)
        {
            SelectionGroupId = PackageQueryVocabulary.PopulationFamily,
        },
        new(
            PrereleaseTermKey,
            "package versions",
            "Selects stable versions or includes prerelease versions.",
            30,
            PackageQueryAcquisitionTier.SearchMetadata,
            EqualityOperator,
            "closed value",
            "include",
            PackageQueryTermRole.Population,
            PackageQueryTermControlKind.Choice)
        {
            SelectionGroupId = PackageQueryVocabulary.PrereleaseFamily,
            Options =
            [
                new("stable", "Stable", "Select stable package versions."),
                new(
                    "include",
                    "Include prerelease",
                    "Include prerelease package versions."),
            ],
        },
        new(
            DependenciesTermKey,
            "dependencies",
            "Matches packages with no dependencies in the selected dependency scope.",
            100,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "closed value",
            "none",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Toggle)
        {
            Options =
            [
                new(
                    "none",
                    "no dependencies",
                    "The selected dependency scope declares no dependencies."),
            ],
        },
        new(
            DependencyTargetTermKey,
            "dependency target",
            "Scopes dependency terms to all manifest groups or one TFM-selected group.",
            150,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "all or NuGet target framework",
            "net8.0",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Input)
        {
            SelectionGroupId =
                PackageQueryVocabulary.DependencyTargetFamily,
        },
        new(
            DependsTermKey,
            "depends on package",
            "Matches a direct dependency in the selected dependency scope.",
            200,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "NuGet package ID",
            "Microsoft.Extensions.DependencyInjection",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Input),
        new(
            LicenseTermKey,
            "license",
            "Matches license presence or a closed license identity derived from nuspec metadata.",
            250,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "closed value",
            "MIT",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Choice)
        {
            Options =
            [
                new("any", "has license", "The nuspec declares a license."),
                new("MIT", "MIT", "The nuspec declares the exact SPDX expression MIT."),
                new(
                    "OSMF",
                    "OSMF",
                    "The nuspec declares an OSMFEULA.* license file."),
            ],
        },
        new(
            DownloadsTermKey,
            "downloads",
            "Matches a closed lifetime-download threshold reported by the package source.",
            300,
            PackageQueryAcquisitionTier.SearchMetadata,
            EqualityOperator,
            "closed value",
            "100k",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Choice)
        {
            SelectionGroupId = PackageQueryVocabulary.DownloadsFamily,
            Options =
            [
                new("10k", "10k+ downloads", "At least ten thousand downloads."),
                new("100k", "100k+ downloads", "At least one hundred thousand downloads."),
                new("1m", "1M+ downloads", "At least one million downloads."),
            ],
        },
        new(
            ReadmeTermKey,
            "embedded README",
            "Matches a package manifest that declares an embedded README.",
            400,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "boolean",
            "true",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Toggle)
        {
            Options =
            [
                new("true", "embedded README", "The manifest declares an embedded README."),
            ],
        },
        new(
            ToolTermKey,
            ".NET Tool",
            "Matches the .NET tool package type from the package manifest.",
            500,
            PackageQueryAcquisitionTier.Nuspec,
            EqualityOperator,
            "boolean",
            "true",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Toggle)
        {
            Options =
            [
                new("true", ".NET Tool", "The manifest declares a .NET tool package type."),
            ],
            ReplacementGroupId = ToolReplacementGroupId,
            DisplayGroupId = ToolDisplayGroupId,
            DisplayGroupLabel = ".NET tool",
        },
        new(
            ToolFormatTermKey,
            ".NET tool format",
            "Downloads the package and matches its .NET tool CLI format.",
            510,
            PackageQueryAcquisitionTier.PackageContent,
            EqualityOperator,
            "closed value",
            "v2",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Choice)
        {
            Options =
            [
                new("v1", "v1", "Portable .NET tool format."),
                new("v2", "v2", "RID-specific .NET tool format."),
            ],
            SelectionGroupId = PackageQueryVocabulary.ToolFormatFamily,
            CombinesWithinSelectionGroup = true,
            ReplacementGroupId = ToolReplacementGroupId,
            DisplayGroupId = ToolDisplayGroupId,
            DisplayGroupLabel = ".NET tool",
        },
        new(
            SkillTermKey,
            "embedded SKILL.md",
            "Downloads the package and matches a skills/SKILL.md or skills/**/SKILL.md file.",
            600,
            PackageQueryAcquisitionTier.PackageContent,
            EqualityOperator,
            "boolean",
            "true",
            PackageQueryTermRole.Inspection,
            PackageQueryTermControlKind.Toggle)
        {
            Options =
            [
                new(
                    "true",
                    "embedded SKILL.md",
                    "The package contains a skill document."),
            ],
        },
    ];

    static readonly IReadOnlyDictionary<string, PackageQueryTermDescriptor>
        TermsByKey = Terms.ToDictionary(
            descriptor => descriptor.Key,
            StringComparer.Ordinal);

    private static readonly PackageQueryVocabulary Vocabulary = new(
    [
        Key(PackageTermKey, BindPackage),
        Key(PrefixTermKey, BindPrefix),
        Key(PrereleaseTermKey, BindPrerelease),
        Key(DependenciesTermKey, BindDependencies),
        Key(DependencyTargetTermKey, BindDependencyTarget),
        Key(DependsTermKey, BindDepends),
        Key(LicenseTermKey, BindLicense),
        Key(DownloadsTermKey, BindDownloads),
        Key(ReadmeTermKey, static (op, value) =>
            BindBoolean(op, value, PackageQueryPredicateKind.Readme)),
        Key(ToolTermKey, static (op, value) =>
            BindBoolean(op, value, PackageQueryPredicateKind.Tool)),
        Key(ToolFormatTermKey, BindToolFormat),
        Key(SkillTermKey, static (op, value) =>
            BindBoolean(op, value, PackageQueryPredicateKind.Skill)),
    ]);

    public static string VocabularyIdentity => Vocabulary.Identity;

    internal static PackageQueryTermDescriptor Descriptor(string key) =>
        TermsByKey[key];

    /// <summary>Resolves one complete Portable Query intent.</summary>
    public static PackageQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        if (intent.Terms.Count > PortableQueryPayloadCodec.MaxTerms)
        {
            return Rejected(
                PackageQueryRequestFailureReason.TooManyTerms,
                value: intent.Terms.Count);
        }

        PortableQueryResolution<PackageQueryPlan> resolution =
            PortableQueryResolver.Resolve(
                Vocabulary.Identity,
                Vocabulary,
                intent,
                cancellationToken);
        if (!resolution.IsResolved)
            return Rejected(resolution.Failure);

        PackageQueryPlan plan = resolution.Plan;
        return plan.HasExplicitDependencyTarget
            && !plan.HasDependencyPredicate
                ? Rejected(
                    PackageQueryRequestFailureReason
                        .DependencyTargetRequiresDependencyPredicate,
                    [DependencyTargetTermKey])
                : new PackageQueryPlanResult.Accepted(plan);
    }

    public static PackageQueryPlanResult Plan(PackageQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PlanInput(
            request.Input,
            request.Terms,
            request.MaximumCandidates,
            request.MaximumMatches,
            request.IncludePrerelease,
            request.RowSelection);
    }

    private static PackageQueryKeyDeclaration Key(
        string key,
        Func<PortableQueryOperator, string,
            PortableQueryBinding<PackageQueryPredicate>> bind) =>
        new(Descriptor(key), bind);

    private static PortableQueryBinding<PackageQueryPredicate> BindPackage(
        PortableQueryOperator @operator,
        string value) =>
        @operator == PortableQueryOperator.Equal
        && DotnetInspector.Packages.PackageExtractor.IsValidPackageId(value)
        && InertString.IsPermitted(TextPolicy.Field, value)
            ? Bound(
                PackageQueryPredicateKind.Package,
                value,
                Normalize(value))
            : PortableQueryBinding<PackageQueryPredicate>.Rejected;

    private static PortableQueryBinding<PackageQueryPredicate> BindPrefix(
        PortableQueryOperator @operator,
        string value) =>
        @operator == PortableQueryOperator.Equal
        && !value.Contains('*')
        && PackageProfileQuery.IsValidPrefix(value)
        && InertString.IsPermitted(TextPolicy.Field, value)
            ? Bound(
                PackageQueryPredicateKind.Prefix,
                value,
                Normalize(value))
            : PortableQueryBinding<PackageQueryPredicate>.Rejected;

    private static PortableQueryBinding<PackageQueryPredicate> BindPrerelease(
        PortableQueryOperator @operator,
        string value)
    {
        if (@operator != PortableQueryOperator.Equal)
            return PortableQueryBinding<PackageQueryPredicate>.Rejected;
        return value.ToLowerInvariant() switch
        {
            "stable" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                "prerelease:stable",
                new(PackageQueryPredicateKind.Prerelease, Flag: false)),
            "include" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                "prerelease:include",
                new(PackageQueryPredicateKind.Prerelease, Flag: true)),
            _ => PortableQueryBinding<PackageQueryPredicate>.Rejected,
        };
    }

    private static PortableQueryBinding<PackageQueryPredicate> BindDependencies(
        PortableQueryOperator @operator,
        string value) =>
        @operator == PortableQueryOperator.Equal
        && value.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? PortableQueryBinding<PackageQueryPredicate>.Bound(
                "dependencies:none",
                new(PackageQueryPredicateKind.NoDependencies))
            : PortableQueryBinding<PackageQueryPredicate>.Rejected;

    private static PortableQueryBinding<PackageQueryPredicate> BindDepends(
        PortableQueryOperator @operator,
        string value) =>
        @operator == PortableQueryOperator.Equal
        && DotnetInspector.Packages.PackageExtractor.IsValidPackageId(value)
        && InertString.IsPermitted(TextPolicy.Field, value)
            ? Bound(
                PackageQueryPredicateKind.Depends,
                value,
                Normalize(value))
            : PortableQueryBinding<PackageQueryPredicate>.Rejected;

    private static PortableQueryBinding<PackageQueryPredicate> BindLicense(
        PortableQueryOperator @operator,
        string value)
    {
        if (@operator != PortableQueryOperator.Equal)
            return PortableQueryBinding<PackageQueryPredicate>.Rejected;
        return value.ToUpperInvariant() switch
        {
            "ANY" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                "license:any",
                new(PackageQueryPredicateKind.License, "any")),
            "MIT" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                "license:MIT",
                new(PackageQueryPredicateKind.License, "MIT")),
            "OSMF" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                "license:OSMF",
                new(PackageQueryPredicateKind.License, "OSMF")),
            _ => PortableQueryBinding<PackageQueryPredicate>.Rejected,
        };
    }

    private static PortableQueryBinding<PackageQueryPredicate>
        BindDependencyTarget(
            PortableQueryOperator @operator,
            string value)
    {
        if (@operator != PortableQueryOperator.Equal)
            return PortableQueryBinding<PackageQueryPredicate>.Rejected;
        if (value.Equals(
                DependencyTargetAllValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return Bound(
                PackageQueryPredicateKind.DependencyTarget,
                DependencyTargetAllValue,
                DependencyTargetAllValue);
        }

        if (value.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return Bound(
                PackageQueryPredicateKind.DependencyTarget,
                "any",
                "any");
        }

        return NuGetTargetFrameworkIdentity.TryNormalize(
            value,
            out string canonical)
                ? Bound(
                    PackageQueryPredicateKind.DependencyTarget,
                    canonical,
                    canonical)
                : PortableQueryBinding<PackageQueryPredicate>.Rejected;
    }

    private static PortableQueryBinding<PackageQueryPredicate> BindDownloads(
        PortableQueryOperator @operator,
        string value)
    {
        if (@operator != PortableQueryOperator.Equal)
            return PortableQueryBinding<PackageQueryPredicate>.Rejected;
        long threshold = value.ToLowerInvariant() switch
        {
            "10k" => 10_000,
            "100k" => 100_000,
            "1m" => 1_000_000,
            _ => 0,
        };
        return threshold == 0
            ? PortableQueryBinding<PackageQueryPredicate>.Rejected
            : PortableQueryBinding<PackageQueryPredicate>.Bound(
                $"downloads:{threshold.ToString(CultureInfo.InvariantCulture)}",
                new(PackageQueryPredicateKind.Downloads, Number: threshold));
    }

    private static PortableQueryBinding<PackageQueryPredicate> BindBoolean(
        PortableQueryOperator @operator,
        string value,
        PackageQueryPredicateKind kind) =>
        @operator == PortableQueryOperator.Equal
        && value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? PortableQueryBinding<PackageQueryPredicate>.Bound(
                $"{Terms.Single(term => term.Key == KeyOf(kind)).Key}:true",
                new(kind, Flag: true))
            : PortableQueryBinding<PackageQueryPredicate>.Rejected;

    private static PortableQueryBinding<PackageQueryPredicate> BindToolFormat(
        PortableQueryOperator @operator,
        string value)
    {
        if (@operator != PortableQueryOperator.Equal)
            return PortableQueryBinding<PackageQueryPredicate>.Rejected;
        return value.ToLowerInvariant() switch
        {
            "v1" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                $"{ToolFormatTermKey}:v1",
                new(PackageQueryPredicateKind.ToolFormat, "1")),
            "v2" => PortableQueryBinding<PackageQueryPredicate>.Bound(
                $"{ToolFormatTermKey}:v2",
                new(PackageQueryPredicateKind.ToolFormat, "2")),
            _ => PortableQueryBinding<PackageQueryPredicate>.Rejected,
        };
    }

    private static PortableQueryBinding<PackageQueryPredicate> Bound(
        PackageQueryPredicateKind kind,
        string value,
        string identity) =>
        PortableQueryBinding<PackageQueryPredicate>.Bound(
            $"{KeyOf(kind)}:{identity}",
            new(kind, value));

    private static string KeyOf(PackageQueryPredicateKind kind) =>
        kind switch
        {
            PackageQueryPredicateKind.Package => PackageTermKey,
            PackageQueryPredicateKind.Prefix => PrefixTermKey,
            PackageQueryPredicateKind.Prerelease => PrereleaseTermKey,
            PackageQueryPredicateKind.NoDependencies => DependenciesTermKey,
            PackageQueryPredicateKind.DependencyTarget =>
                DependencyTargetTermKey,
            PackageQueryPredicateKind.Depends => DependsTermKey,
            PackageQueryPredicateKind.Downloads => DownloadsTermKey,
            PackageQueryPredicateKind.License => LicenseTermKey,
            PackageQueryPredicateKind.Readme => ReadmeTermKey,
            PackageQueryPredicateKind.Tool => ToolTermKey,
            PackageQueryPredicateKind.ToolFormat => ToolFormatTermKey,
            PackageQueryPredicateKind.Skill => SkillTermKey,
            _ => throw new InvalidOperationException(
                "Unknown Package Query predicate kind."),
        };

    private static string Normalize(string value) =>
        value.ToUpperInvariant();

    internal static async ValueTask<ImmutableArray<PackageQueryEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
    {
        var events = ImmutableArray.CreateBuilder<PackageQueryEvent>();
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            cancellationToken).ConfigureAwait(false))
        {
            events.Add(queryEvent);
        }

        return events.ToImmutable();
    }

    internal static async ValueTask<ImmutableArray<PackageQueryEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            CancellationToken cancellationToken = default)
    {
        var events = ImmutableArray.CreateBuilder<PackageQueryEvent>();
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            contentProvider,
            cancellationToken).ConfigureAwait(false))
        {
            events.Add(queryEvent);
        }

        return events.ToImmutable();
    }

    internal static async IAsyncEnumerable<PackageQueryEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            cancellationToken).ConfigureAwait(false))
        {
            yield return queryEvent;
        }
    }

    /// <summary>
    /// Executes a package query with the explicit host capability required to
    /// acquire admitted package content.
    /// </summary>
    internal static async IAsyncEnumerable<PackageQueryEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        IPackageQueryContentProvider? contentProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        bool requiresPackageContent = plan.BoundTerms.Any(term =>
            term.Predicate.RequiresPackageContent);
        if (requiresPackageContent && contentProvider is null)
        {
            throw new InvalidOperationException(
                "Package-content terms require an explicit package-content provider.");
        }

        int candidates = 0;
        int matches = 0;
        int failures = 0;
        int packageContentCompleted = 0;
        int? sourceCandidates = null;
        bool searchOutcomeObserved = false;
        bool sourceSearchFailed = false;
        cancellationToken.ThrowIfCancellationRequested();
        yield return Progress(
            PackageQueryProgressPhase.Search,
            completed: 0,
            limit: 1);
        await foreach (PackageQueryInputEvent inputEvent
            in AcquireInputAsync(source, plan, cancellationToken)
                .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputEvent is PackageQueryInputEvent.Acquired acquired)
            {
                sourceCandidates = acquired.Count;
                searchOutcomeObserved = true;
                yield return Progress(
                    PackageQueryProgressPhase.Search, completed: 1, limit: 1);
                continue;
            }
            bool sourceWideSearchFailure =
                inputEvent is PackageQueryInputEvent.Failure searchFailure
                && IsSourceWideSearchFailure(searchFailure.Value);
            sourceSearchFailed |= sourceWideSearchFailure;
            if (!searchOutcomeObserved)
            {
                searchOutcomeObserved = true;
                if (!sourceWideSearchFailure)
                {
                    yield return Progress(
                        PackageQueryProgressPhase.Search,
                        completed: 1,
                        limit: 1);
                }
            }

            switch (inputEvent)
            {
                case PackageQueryInputEvent.Match match:
                    candidates++;
                    if (match.Value.Manifest is not null)
                    {
                        yield return Progress(
                            PackageQueryProgressPhase.Manifest,
                            candidates,
                            plan.MaximumCandidates);
                    }
                    if (!TryMatchManifest(
                        plan,
                        match.Value,
                        out ImmutableArray<PackageQueryAnswer>.Builder
                            answers,
                        out ImmutableArray<PackageQueryEvidence>.Builder
                            evidence))
                        continue;

                    if (requiresPackageContent)
                    {
                        if (packageContentCompleted == 0)
                        {
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                completed: 0,
                                limit: plan.MaximumCandidates);
                        }
                        PackageQueryContentResult contentResult =
                            await contentProvider!.GetContentAsync(
                                match.Value,
                                cancellationToken).ConfigureAwait(false);
                        if (contentResult
                            is PackageQueryContentResult.Unavailable unavailable)
                        {
                            packageContentCompleted++;
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                packageContentCompleted,
                                plan.MaximumCandidates);
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Source,
                                    PackageQueryFailureKind
                                        .PackageContentAcquisition,
                                    unavailable.Message));
                            continue;
                        }

                        IPackageContent content =
                            ((PackageQueryContentResult.Available)contentResult)
                                .Content;
                        PackageContentFacts? facts = null;
                        try
                        {
                            facts = await ReadPackageContentFactsAsync(
                                content,
                                plan.BoundTerms,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (
                            ex is IOException
                                or InvalidDataException
                                or DecoderFallbackException
                                or NotSupportedException
                                or UnauthorizedAccessException
                                or XmlException)
                        {
                            // Iterator catch clauses cannot yield; null is
                            // projected below as a typed item failure.
                        }
                        if (facts is null)
                        {
                            packageContentCompleted++;
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                packageContentCompleted,
                                plan.MaximumCandidates);
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Source,
                                    PackageQueryFailureKind
                                        .PackageContentEvaluation,
                                    "The package content could not be evaluated."));
                            continue;
                        }

                        packageContentCompleted++;
                        yield return Progress(
                            PackageQueryProgressPhase.PackageContent,
                            packageContentCompleted,
                            plan.MaximumCandidates);
                        if (!TryMatchPackageContent(
                            plan,
                            match.Value,
                            facts,
                            answers,
                            evidence))
                        {
                            continue;
                        }
                    }

                    matches++;
                    yield return new PackageQueryEvent.Match(
                        new PackageQueryMatch(
                            match.Value,
                            requiresPackageContent
                                ? PackageQueryAcquisitionTier.PackageContent
                                : match.Value.Manifest is not null
                                    ? PackageQueryAcquisitionTier.Nuspec
                                    : PackageQueryAcquisitionTier.SearchMetadata,
                            answers.ToImmutable(),
                            evidence.ToImmutable()));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (plan.MaximumMatches is int maximumMatches
                        && matches >= maximumMatches)
                    {
                        yield return Completed(
                            plan,
                            source.Source,
                            candidates,
                            matches,
                            failures,
                            plan.PackageInput is SourceSelector.Package
                                ? PackageQueryCompletionKind.ExactPackageComplete
                                : PackageQueryCompletionKind.MatchLimitReached,
                            sourceCandidates);
                        yield break;
                    }
                    break;

                case PackageQueryInputEvent.Failure failure:
                    failures++;
                    if (!sourceWideSearchFailure)
                    {
                        candidates++;
                        yield return Progress(
                            PackageQueryProgressPhase.Manifest,
                            candidates,
                            plan.MaximumCandidates);
                    }

                    yield return new PackageQueryEvent.Failure(failure.Value);
                    break;

                case PackageQueryInputEvent.Completed completed:
                    yield return Completed(
                        plan,
                        source.Source,
                        completed.Candidates,
                        matches,
                        failures,
                        sourceSearchFailed
                            ? PackageQueryCompletionKind.Failed
                            : completed.Completion,
                        sourceCandidates);
                    yield break;
            }
        }

        throw new InvalidOperationException(
            "The package query input ended without a completion event.");
    }

    static bool IsSourceWideSearchFailure(PackageQueryFailure failure) =>
        failure.Kind == PackageQueryFailureKind.Search
        || failure is
        {
            Kind: PackageQueryFailureKind.SearchContract,
            PackageId: null,
            Version: null,
        };

    static bool TryMatchManifest(
        PackageQueryPlan plan,
        PackageQueryPackage match,
        out ImmutableArray<PackageQueryAnswer>.Builder answers,
        out ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        answers = ImmutableArray.CreateBuilder<PackageQueryAnswer>(
            plan.BoundTerms.Length);
        evidence = ImmutableArray.CreateBuilder<PackageQueryEvidence>(
            plan.BoundTerms.Length + 1);
        AddScopeEvidence(plan, evidence);
        PackageQueryDependencySelection? dependencySelection =
            plan.HasDependencyTerms
                ? SelectDependencies(plan, match)
                : null;
        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (BoundPackageQueryTerm term in plan.BoundTerms)
        {
            string? groupId = term.Descriptor.SelectionGroupId;
            if (groupId is not null && !handledGroups.Add(groupId))
                continue;

            BoundPackageQueryTerm[] alternatives = groupId is null
                ? [term]
                :
                [
                    .. plan.BoundTerms.Where(candidate =>
                        candidate.Descriptor.SelectionGroupId == groupId),
                ];
            BoundPackageQueryTerm[] matched =
            [
                .. alternatives.Where(candidate =>
                    MatchesManifest(
                        candidate,
                        match,
                        dependencySelection)),
            ];
            if (matched.Length == 0)
            {
                evidence.Clear();
                return false;
            }

            foreach (BoundPackageQueryTerm candidate in matched)
            {
                if (candidate.Descriptor.Tier
                    == PackageQueryAcquisitionTier.PackageContent)
                {
                    continue;
                }
                AddTermEvidence(
                    candidate,
                    match,
                    content: null,
                    dependencySelection,
                    answers,
                    evidence);
            }
        }

        return true;
    }

    static bool TryMatchPackageContent(
        PackageQueryPlan plan,
        PackageQueryPackage match,
        PackageContentFacts content,
        ImmutableArray<PackageQueryAnswer>.Builder answers,
        ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (BoundPackageQueryTerm term in plan.BoundTerms)
        {
            if (term.Descriptor.Tier
                    != PackageQueryAcquisitionTier.PackageContent)
            {
                continue;
            }

            string? groupId = term.Descriptor.SelectionGroupId;
            if (groupId is not null && !handledGroups.Add(groupId))
                continue;

            BoundPackageQueryTerm[] alternatives = groupId is null
                ? [term]
                :
                [
                    .. plan.BoundTerms.Where(candidate =>
                        candidate.Descriptor.Tier
                            == PackageQueryAcquisitionTier.PackageContent
                        && candidate.Descriptor.SelectionGroupId == groupId),
                ];
            BoundPackageQueryTerm[] matched =
            [
                .. alternatives.Where(candidate =>
                    MatchesPackageContent(candidate, content)),
            ];
            if (matched.Length == 0)
            {
                return false;
            }

            foreach (BoundPackageQueryTerm candidate in matched)
            {
                AddTermEvidence(
                    candidate,
                    match,
                    content,
                    dependencySelection: null,
                    answers,
                    evidence);
            }
        }

        return true;
    }

    static async ValueTask<PackageContentFacts> ReadPackageContentFactsAsync(
        IPackageContent content,
        ImmutableArray<BoundPackageQueryTerm> terms,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] entries = [.. content.EnumerateEntries()];
        bool needsSkills = terms.Any(term =>
            term.Predicate.Kind == PackageQueryPredicateKind.Skill);
        bool needsToolSettings = terms.Any(term =>
            term.Predicate.Kind == PackageQueryPredicateKind.ToolFormat);
        PackageQueryEvidenceSummary? skills = needsSkills
            ? SummarizeItems(entries.Where(IsSkillDocument), StringComparer.Ordinal)
            : null;
        string? toolVersion = needsToolSettings
            ? await ReadToolSettingsVersionAsync(
                content,
                entries,
                cancellationToken).ConfigureAwait(false)
            : null;
        return new PackageContentFacts(skills, toolVersion);
    }

    static async ValueTask<string?> ReadToolSettingsVersionAsync(
        IPackageContent content,
        IEnumerable<string> entries,
        CancellationToken cancellationToken)
    {
        string[] settingsPaths =
        [
            .. entries
                .Where(IsToolSettings)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        string? packageVersion = null;
        foreach (string path in settingsPaths)
        {
            if (!content.TryOpenEntry(
                path,
                MaximumToolSettingsBytes,
                out Stream? stream))
            {
                throw new IOException(
                    "The selected tool settings entry is unavailable.");
            }

            await using (stream.ConfigureAwait(false))
            {
                byte[] bytes = await BoundedContentReader.ReadAllBytesAsync(
                    stream,
                    MaximumToolSettingsBytes,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                string xml = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(bytes);
                if (xml.Length > 0 && xml[0] == '\uFEFF')
                    xml = xml[1..];
                DotnetToolSettingsData? settings =
                    DotnetToolSettingsParser.ParseContentOrThrow(xml);
                if (settings is null)
                    return null;
                string version = settings.Version ?? "1";
                if (packageVersion is null)
                {
                    packageVersion = version;
                }
                else if (!packageVersion.Equals(
                    version,
                    StringComparison.Ordinal))
                {
                    return null;
                }
            }
        }

        return packageVersion;
    }

    static bool IsSkillDocument(string path)
    {
        string[] segments = path.Split('/');
        return segments.Length >= 2
            && segments[0].Equals(
                "skills",
                StringComparison.OrdinalIgnoreCase)
            && segments[^1].Equals(
                "SKILL.md",
                StringComparison.OrdinalIgnoreCase);
    }

    static bool IsToolSettings(string path)
    {
        string[] segments = path.Split('/');
        return segments.Length is >= 2 and <= 4
            && segments[0].Equals(
                "tools",
                StringComparison.OrdinalIgnoreCase)
            && segments[^1].Equals(
                "DotnetToolSettings.xml",
                StringComparison.OrdinalIgnoreCase);
    }

    static PackageQueryFailure FromProfileFailure(
        PackageProfileFailure failure) =>
        new(
            failure.PackageId,
            failure.Version,
            failure.Source,
            failure.Kind switch
            {
                PackageProfileFailureKind.Search =>
                    PackageQueryFailureKind.Search,
                PackageProfileFailureKind.SearchContract =>
                    PackageQueryFailureKind.SearchContract,
                PackageProfileFailureKind.ManifestAcquisition =>
                    PackageQueryFailureKind.ManifestAcquisition,
                PackageProfileFailureKind.ManifestContract =>
                    PackageQueryFailureKind.ManifestContract,
                PackageProfileFailureKind.InvalidManifest =>
                    PackageQueryFailureKind.InvalidManifest,
                _ => throw new InvalidOperationException(
                    "Unknown package-profile failure kind."),
            },
            failure.Message,
            failure.ManifestFailureReason);

    static bool MatchesManifest(
        BoundPackageQueryTerm term,
        PackageQueryPackage match,
        PackageQueryDependencySelection? dependencySelection) =>
        term.Predicate.Kind switch
        {
            PackageQueryPredicateKind.NoDependencies =>
                HasNoDependencies(
                    dependencySelection
                    ?? throw new InvalidOperationException(
                        "Dependency matching requires one dependency selection.")),
            PackageQueryPredicateKind.DependencyTarget => true,
            PackageQueryPredicateKind.Depends =>
                MatchingDependencies(
                    term,
                    dependencySelection
                    ?? throw new InvalidOperationException(
                        "Dependency matching requires one dependency selection."))
                    .Length > 0,
            PackageQueryPredicateKind.Downloads =>
                match.TotalDownloads >= term.Predicate.Number,
            PackageQueryPredicateKind.License =>
                MatchesLicense(
                    match.RequiredManifest.LicenseDeclaration,
                    term.Predicate.Text),
            PackageQueryPredicateKind.Readme =>
                !string.IsNullOrWhiteSpace(
                    match.RequiredManifest.ReadmeFile),
            PackageQueryPredicateKind.Tool =>
                match.RequiredManifest.IsToolPackage,
            PackageQueryPredicateKind.ToolFormat =>
                match.RequiredManifest.IsToolPackage,
            PackageQueryPredicateKind.Skill => true,
            _ => throw new InvalidOperationException(
                "A structural Package Query term reached manifest evaluation."),
        };

    static PackageQueryDependencySelection SelectDependencies(
        PackageQueryPlan plan,
        PackageQueryPackage match)
    {
        PackageManifestFacts manifest = match.RequiredManifest;
        if (plan.DependencyTarget.Kind
            == PackageQueryDependencyTargetKind.All)
        {
            return new PackageQueryDependencySelection(
                plan.DependencyTarget,
                manifest.DependencyGroups,
                SelectionStatus: null,
                SelectedGroup: null);
        }

        PackageDependencyGroups selection =
            PackageDependencyGroupsQuery.ProjectDependencyGroups(
                manifest,
                plan.DependencyTarget.RequestedTargetFramework,
                allowCompatibleFallbackForRequestedTfm: true);
        return new PackageQueryDependencySelection(
            plan.DependencyTarget,
            selection.Groups,
            selection.SelectionStatus,
            selection.SelectedGroup);
    }

    static bool HasNoDependencies(
        PackageQueryDependencySelection selection) =>
        selection.Target.Kind switch
        {
            PackageQueryDependencyTargetKind.All =>
                selection.Groups.All(group =>
                    group.Dependencies.IsEmpty),
            PackageQueryDependencyTargetKind.TargetFramework =>
                selection.SelectionStatus switch
                {
                    PackageDependencyGroupSelectionStatus.Selected =>
                        selection.SelectedGroup!.Dependencies.IsEmpty,
                    PackageDependencyGroupSelectionStatus.NoDependencyGroups =>
                        true,
                    PackageDependencyGroupSelectionStatus
                        .NoMatchingTargetFramework =>
                        false,
                    _ => throw new InvalidOperationException(
                        "Unknown dependency-group selection status."),
                },
            _ => throw new InvalidOperationException(
                "Unknown Package Query dependency target."),
        };

    static bool MatchesPackageContent(
        BoundPackageQueryTerm term,
        PackageContentFacts content) =>
        term.Predicate.Kind switch
        {
            PackageQueryPredicateKind.ToolFormat =>
                content.ToolSettingsVersion == term.Predicate.Text,
            PackageQueryPredicateKind.Skill =>
                content.SkillDocuments is { Count: > 0 },
            _ => throw new InvalidOperationException(
                "A non-content Package Query term reached content evaluation."),
        };

    static PackageQueryDependencyMatch[] MatchingDependencies(
        BoundPackageQueryTerm term,
        PackageQueryDependencySelection selection) =>
        [
            .. SelectedDependencyGroups(selection)
                .SelectMany(group => group.Dependencies
                    .Where(dependency => string.Equals(
                        dependency.Id,
                        term.Predicate.Text,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(dependency =>
                        new PackageQueryDependencyMatch(
                            group,
                            dependency))),
        ];

    static bool MatchesLicense(
        PackageLicenseDeclaration? declaration,
        string? requested)
    {
        if (declaration is null)
            return false;
        if (requested == "any")
            return true;
        if (requested == "MIT")
        {
            return declaration.Kind == PackageLicenseDeclarationKind.Expression
                && declaration.Value.Equals(
                    "MIT",
                    StringComparison.OrdinalIgnoreCase);
        }
        if (requested == "OSMF")
        {
            if (declaration.Kind != PackageLicenseDeclarationKind.File)
                return false;
            string normalized = declaration.Value.Replace('\\', '/');
            string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
            return fileName.StartsWith(
                "OSMFEULA.",
                StringComparison.OrdinalIgnoreCase);
        }
        throw new InvalidOperationException(
            "Unknown bound Package Query license identity.");
    }

    static IEnumerable<DeclaredPackageDependencyGroup>
        SelectedDependencyGroups(
            PackageQueryDependencySelection selection) =>
        selection.Target.Kind == PackageQueryDependencyTargetKind.All
            ? selection.Groups
            : selection.SelectedGroup is { } selected
                ? [selected]
                : [];

    static string DescribeDependencyMatch(
        PackageQueryDependencyMatch match)
    {
        string group = string.IsNullOrWhiteSpace(
            match.Group.TargetFramework)
                ? "any"
                : match.Group.TargetFramework;
        return $"{group}: {match.Dependency.Id} "
            + match.Dependency.VersionRange;
    }

    static PackageQueryEvidenceSummary SummarizeItems(
        IEnumerable<string> items,
        StringComparer comparer)
    {
        string[] distinct = [.. items.Distinct(comparer).Order(comparer)];
        return new PackageQueryEvidenceSummary(
            distinct.Length,
            [
                .. distinct.Take(MaximumEvidencePreviewItems)
                    .Select(item => new InertString(
                        TextPolicy.Field, item, MaximumEvidencePreviewCharacters)),
            ]);
    }

    static PackageQueryTermResult CreateTermResult(
        BoundPackageQueryTerm term,
        PackageQueryPackage package,
        PackageContentFacts? content,
        PackageQueryDependencySelection? dependencySelection)
    {
        var answer = new PackageQueryAnswer(
            term.Descriptor.Key,
            new InertString(
                TextPolicy.Field,
                term.Predicate.Kind switch
                {
                    PackageQueryPredicateKind.NoDependencies => "none",
                    PackageQueryPredicateKind.DependencyTarget =>
                        term.Predicate.Text
                        ?? throw new InvalidOperationException(
                            "Dependency-target answers require a bound target."),
                    PackageQueryPredicateKind.Depends =>
                        term.Predicate.Text
                        ?? throw new InvalidOperationException(
                            "Dependency answers require a bound package ID."),
                    PackageQueryPredicateKind.Downloads => term.Term.Value,
                    PackageQueryPredicateKind.License =>
                        term.Predicate.Text switch
                        {
                            "any" => "true",
                            { } identity => identity,
                            null => throw new InvalidOperationException(
                                "License answers require a bound identity."),
                        },
                    PackageQueryPredicateKind.Readme => "true",
                    PackageQueryPredicateKind.Tool => "true",
                    PackageQueryPredicateKind.ToolFormat => term.Term.Value,
                    PackageQueryPredicateKind.Skill => "true",
                    _ => throw new InvalidOperationException(
                        "A structural Package Query term reached answer production."),
                }))
        {
            Term = term.Term,
        };
        PackageQueryEvidence evidence = term.Predicate.Kind switch
        {
            PackageQueryPredicateKind.NoDependencies =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Summary = SummarizeItems(
                        SelectedDependencyGroups(
                            dependencySelection
                            ?? throw new InvalidOperationException(
                                "Dependency evidence requires one dependency selection."))
                            .SelectMany(group => group.Dependencies)
                            .Select(dependency => dependency.Id),
                        StringComparer.OrdinalIgnoreCase),
                },
            PackageQueryPredicateKind.DependencyTarget =>
                DependencyTargetEvidence(
                    term.Descriptor.Key,
                    dependencySelection
                    ?? throw new InvalidOperationException(
                        "Dependency-target evidence requires one dependency selection.")),
            PackageQueryPredicateKind.Depends =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Summary = SummarizeItems(
                        MatchingDependencies(
                            term,
                            dependencySelection
                            ?? throw new InvalidOperationException(
                                "Dependency evidence requires one dependency selection."))
                            .Select(DescribeDependencyMatch),
                        StringComparer.Ordinal),
                },
            PackageQueryPredicateKind.Downloads =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Number = package.TotalDownloads
                        ?? throw new InvalidOperationException(
                            "Download evidence requires a source count."),
                },
            PackageQueryPredicateKind.License =>
                LicenseEvidence(
                    term.Descriptor.Key,
                    package.RequiredManifest.LicenseDeclaration
                    ?? throw new InvalidOperationException(
                        "License evidence requires a nuspec declaration.")),
            PackageQueryPredicateKind.Readme =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Properties =
                    [
                        Property(
                            "path",
                            package.RequiredManifest.ReadmeFile
                            ?? throw new InvalidOperationException(
                                "README evidence requires a declared path.")),
                    ],
                },
            PackageQueryPredicateKind.Tool =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Properties = [Property("package-type", "DotnetTool")],
                },
            PackageQueryPredicateKind.ToolFormat =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Properties =
                    [
                        Property(
                            "settings-version",
                            content?.ToolSettingsVersion
                            ?? throw new InvalidOperationException(
                                ".NET tool format evidence requires package-content facts.")),
                    ],
                },
            PackageQueryPredicateKind.Skill =>
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Summary = content?.SkillDocuments
                        ?? throw new InvalidOperationException(
                            "Skill-document evidence requires package-content facts."),
                },
            _ => throw new InvalidOperationException(
                "A structural Package Query term reached evidence production."),
        };
        evidence = evidence with
        {
            Scope = term.Predicate.Kind
                    == PackageQueryPredicateKind.DependencyTarget
                && dependencySelection!.Target.Kind
                    == PackageQueryDependencyTargetKind.All
                    ? PackageQueryEvidenceScope.Query
                    : PackageQueryEvidenceScope.Package,
            Term = term.Term,
        };
        return new PackageQueryTermResult(answer, evidence);
    }

    static PackageQueryEvidence DependencyTargetEvidence(
        string id,
        PackageQueryDependencySelection selection)
    {
        var properties =
            ImmutableArray.CreateBuilder<PackageQueryEvidenceProperty>();
        if (selection.Target.Kind == PackageQueryDependencyTargetKind.All)
        {
            properties.Add(Property("target", "all"));
            return new PackageQueryEvidence(id)
            {
                Properties = properties.ToImmutable(),
            };
        }

        properties.Add(Property(
            "requested-target",
            selection.Target.RequestedTargetFramework
            ?? throw new InvalidOperationException(
                "A target-framework dependency scope requires its requested framework.")));
        properties.Add(Property(
            "selection-status",
            selection.SelectionStatus?.ToString()
            ?? throw new InvalidOperationException(
                "A target-framework dependency scope requires a selection status.")));
        if (selection.SelectedGroup is { } selected)
        {
            properties.Add(Property(
                "selected-group",
                string.IsNullOrWhiteSpace(selected.TargetFramework)
                    ? "any"
                    : selected.TargetFramework));
        }
        return new PackageQueryEvidence(id)
        {
            Properties = properties.ToImmutable(),
        };
    }

    static PackageQueryEvidence LicenseEvidence(
        string id,
        PackageLicenseDeclaration declaration) =>
        new(id)
        {
            Properties =
            [
                Property(
                    "declaration-kind",
                    declaration.Kind.ToString()),
                Property("declaration-value", declaration.Value),
            ],
        };

    static void AddTermEvidence(
        BoundPackageQueryTerm term,
        PackageQueryPackage package,
        PackageContentFacts? content,
        PackageQueryDependencySelection? dependencySelection,
        ImmutableArray<PackageQueryAnswer>.Builder answers,
        ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        PackageQueryTermResult result = CreateTermResult(
            term,
            package,
            content,
            dependencySelection);
        int answerInsertionIndex = 0;
        while (answerInsertionIndex < answers.Count
            && TermsByKey[answers[answerInsertionIndex].Id].Weight
                < term.Descriptor.Weight)
        {
            answerInsertionIndex++;
        }
        answers.Insert(answerInsertionIndex, result.Answer);

        int evidenceInsertionIndex = 1;
        while (evidenceInsertionIndex < evidence.Count
            && TermsByKey.TryGetValue(
                evidence[evidenceInsertionIndex].Id,
                out PackageQueryTermDescriptor? existing)
            && existing.Weight < term.Descriptor.Weight)
        {
            evidenceInsertionIndex++;
        }
        evidence.Insert(evidenceInsertionIndex, result.Evidence);
    }

    static PackageQueryEvent.Progress Progress(
        PackageQueryProgressPhase phase,
        int completed,
        int limit) =>
        new(new PackageQueryProgress(phase, completed, limit));

    static PackageQueryEvent.Completed Completed(
        PackageQueryPlan plan,
        PackageSourceResultIdentity source,
        int candidates,
        int matches,
        int failures,
        PackageQueryCompletionKind completion,
        int? sourceCandidates = null) =>
        new(
            new PackageQuerySummary(
                plan.Prefix,
                source,
                plan.MaximumCandidates,
                plan.MaximumMatches,
                candidates,
                matches,
                failures,
                completion)
            {
                SourceCandidates = sourceCandidates,
            });

    static PackageQueryCompletionKind MapCompletion(
        PackageSearchTruncationReason reason) =>
        reason switch
        {
            PackageSearchTruncationReason.None =>
                PackageQueryCompletionKind.Exhausted,
            PackageSearchTruncationReason.RequestedLimit =>
                PackageQueryCompletionKind.CandidateLimitReached,
            PackageSearchTruncationReason.SourcePageLimit =>
                PackageQueryCompletionKind.SourcePageLimitReached,
            PackageSearchTruncationReason.ClientPageLimit =>
                PackageQueryCompletionKind.ClientPageLimitReached,
            _ => throw new InvalidOperationException(
                "Unknown package search truncation reason."),
        };

    static PackageQueryPlanResult.Rejected Rejected(
        PackageQueryRequestFailureReason reason,
        IEnumerable<string>? termKeys = null,
        int? value = null,
        PortableQueryFailure? portableFailure = null) =>
        new(new PackageQueryRequestFailure(
            reason,
            termKeys,
            value,
            portableFailure));

    static PackageQueryPlanResult.Rejected Rejected(
        PortableQueryFailure failure)
    {
        PackageQueryRequestFailureReason reason = failure.Reason switch
        {
            PortableQueryFailureReason.UnknownVocabulary =>
                PackageQueryRequestFailureReason.UnknownVocabulary,
            PortableQueryFailureReason.UnknownKey =>
                PackageQueryRequestFailureReason.UnknownTerm,
            PortableQueryFailureReason.OperatorNotAdmitted =>
                PackageQueryRequestFailureReason.TermOperatorNotAdmitted,
            PortableQueryFailureReason.ValueRejected =>
                PackageQueryRequestFailureReason.InvalidTermValue,
            PortableQueryFailureReason.DuplicateAfterBinding =>
                PackageQueryRequestFailureReason.DuplicateTerm,
            PortableQueryFailureReason.TermsIncompatible =>
                PackageQueryRequestFailureReason.IncompatibleTerms,
            PortableQueryFailureReason.RequiredTermFamilyMissing
                when failure.Offender == PackageQueryVocabulary.PopulationFamily =>
                PackageQueryRequestFailureReason.RequiredPopulationMissing,
            PortableQueryFailureReason.RequiredTermFamilyMissing
                when failure.Offender == PackageQueryVocabulary.PrereleaseFamily =>
                PackageQueryRequestFailureReason.RequiredPrereleaseMissing,
            PortableQueryFailureReason.UnknownDimension =>
                PackageQueryRequestFailureReason.UnknownBound,
            PortableQueryFailureReason.MaximumOutsideRange
                when failure.Offender
                    == PackageQueryVocabulary.CandidatesDimension =>
                PackageQueryRequestFailureReason.InvalidCandidateLimit,
            PortableQueryFailureReason.MaximumOutsideRange
                when failure.Offender
                    == PackageQueryVocabulary.MatchesDimension =>
                PackageQueryRequestFailureReason.InvalidMatchLimit,
            PortableQueryFailureReason.RequiredDimensionMissing =>
                PackageQueryRequestFailureReason.RequiredCandidateBoundMissing,
            PortableQueryFailureReason.StageNotAdmitted =>
                PackageQueryRequestFailureReason.StageNotAdmitted,
            PortableQueryFailureReason.UnknownOrderReference
                or PortableQueryFailureReason.OrderReferenceNotOrderable
                or PortableQueryFailureReason.OrderNotARanking
                or PortableQueryFailureReason.RankingMissing =>
                PackageQueryRequestFailureReason.OrderNotSupported,
            _ => throw new InvalidOperationException(
                "Unknown Portable Query resolution failure."),
        };
        return Rejected(
            reason,
            termKeys: failure.Location.Part == PortableQueryPart.Terms
                && failure.Offender is not null
                    ? [failure.Offender]
                    : null,
            portableFailure: failure);
    }
}
