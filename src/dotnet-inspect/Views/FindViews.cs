using InertText;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindResultView
{
    public FindResultView(InertString titleText, InertString? descriptionText = null)
    {
        TitleText = titleText;
        DescriptionText = descriptionText;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString? DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description => DescriptionText?.ToString();
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Results")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutIgnoreColumnWhen(nameof(MatchIsUniform), "Match")]
    [MarkoutIgnoreColumnWhen(nameof(SimIsUniform), "Sim")]
    public List<FindRow>? Results { get; set; }

    public static bool PatternIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;

    public static bool MatchIsUniform(List<FindRow>? rows)
        => rows?.Select(r => r.Match).Distinct().Count() <= 1;

    public static bool SimIsUniform(List<FindRow>? rows)
        => rows?.All(r => r.Similarity is "1.00" or "-") ?? true;
}

[MarkoutSerializable]
public record FindRow(
    [property: MarkoutIgnore] InertString PatternText,
    [property: MarkoutIgnore] InertString TypeText,
    [property: MarkoutIgnore] InertString NamespaceText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString SourceText,
    [property: MarkoutIgnore] InertString MatchText,
    [property: MarkoutIgnore] InertString SimilarityText)
{
    public string Pattern => PatternText.ToString();
    public string Type => TypeText.ToString();
    public string Namespace => NamespaceText.ToString();
    public string Kind => KindText.ToString();
    public string Library => LibraryText.ToString();
    public string Source => SourceText.ToString();
    public string Match => MatchText.ToString();
    [MarkoutPropertyName("Sim")]
    public string Similarity => SimilarityText.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class FindMembersResultView
{
    public FindMembersResultView(InertString titleText, InertString? descriptionText = null)
    {
        TitleText = titleText;
        DescriptionText = descriptionText;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString? DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description => DescriptionText?.ToString();
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Members")]
    [MarkoutIgnoreColumnWhen(nameof(PatternIsUniform), "Pattern")]
    [MarkoutIgnoreColumnWhen(nameof(SignatureIsEmpty), "Signature")]
    public List<FindMemberRow>? Results { get; set; }

    public static bool PatternIsUniform(List<FindMemberRow>? rows)
        => rows?.Select(r => r.Pattern).Distinct().Count() <= 1;

    public static bool SignatureIsEmpty(List<FindMemberRow>? rows)
        => rows?.All(r => string.IsNullOrEmpty(r.Signature)) ?? true;
}

[MarkoutSerializable]
public record FindMemberRow(
    [property: MarkoutIgnore] InertString PatternText,
    [property: MarkoutIgnore] InertString MemberText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString TypeText,
    [property: MarkoutIgnore] InertString SignatureText,
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString SourceText)
{
    public string Pattern => PatternText.ToString();
    public string Member => MemberText.ToString();
    public string Kind => KindText.ToString();
    public string Type => TypeText.ToString();
    public string Signature => SignatureText.ToString();
    public string Library => LibraryText.ToString();
    public string Source => SourceText.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class PackageProfileView
{
    public PackageProfileView(
        InertString title,
        InertString prefix,
        InertString? description = null)
    {
        TitleText = title;
        PrefixText = prefix;
        DescriptionText = description;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString PrefixText { get; }
    [MarkoutIgnore] public InertString? DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] [MarkoutSkipNull]
    public string? Description => DescriptionText?.ToString();
    [MarkoutIgnore]
    public string Prefix => PrefixText.ToString();
    [MarkoutIgnore]
    public int Packages { get; init; }
    [MarkoutIgnore]
    public int Failures { get; init; }
    [MarkoutIgnore]
    public bool Truncated { get; init; }

    [MarkoutSection(Name = "Packages")]
    public List<PackageProfileRow>? Results { get; init; }
}

[MarkoutSerializable]
public sealed class PackageProfileRow
{
    public PackageProfileRow(
        string package,
        string dependency,
        string version,
        string owners,
        string targetFramework,
        string dependencyVersion,
        string authors,
        string verified,
        string downloads,
        string source,
        string status,
        string error)
        : this(
            Contain(package),
            Contain(dependency),
            Contain(version),
            Contain(owners),
            Contain(targetFramework),
            Contain(dependencyVersion),
            Contain(authors),
            Contain(verified),
            Contain(downloads),
            Contain(source),
            Contain(status),
            Contain(error))
    {
    }

    internal PackageProfileRow(
        InertString package,
        InertString dependency,
        InertString version,
        InertString owners,
        InertString targetFramework,
        InertString dependencyVersion,
        InertString authors,
        InertString verified,
        InertString downloads,
        InertString source,
        InertString status,
        InertString error)
    {
        PackageText = package;
        DependencyText = dependency;
        VersionText = version;
        OwnersText = owners;
        TargetFrameworkText = targetFramework;
        DependencyVersionText = dependencyVersion;
        AuthorsText = authors;
        VerifiedText = verified;
        DownloadsText = downloads;
        SourceText = source;
        StatusText = status;
        ErrorText = error;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString DependencyText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString OwnersText { get; }
    [MarkoutIgnore] public InertString TargetFrameworkText { get; }
    [MarkoutIgnore] public InertString DependencyVersionText { get; }
    [MarkoutIgnore] public InertString AuthorsText { get; }
    [MarkoutIgnore] public InertString VerifiedText { get; }
    [MarkoutIgnore] public InertString DownloadsText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public InertString StatusText { get; }
    [MarkoutIgnore] public InertString ErrorText { get; }

    public string Package => PackageText.ToString();
    public string Dependency => DependencyText.ToString();
    public string Version => VersionText.ToString();
    public string Owners => OwnersText.ToString();
    [MarkoutPropertyName("TFM")]
    public string TargetFramework => TargetFrameworkText.ToString();
    [MarkoutPropertyName("Dependency Version")]
    public string DependencyVersion => DependencyVersionText.ToString();
    public string Authors => AuthorsText.ToString();
    public string Verified => VerifiedText.ToString();
    public string Downloads => DownloadsText.ToString();
    public string Source => SourceText.ToString();
    public string Status => StatusText.ToString();
    public string Error => ErrorText.ToString();

    private static InertString Contain(string value) =>
        new(TextPolicy.Field, value);
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(FindResultView))]
[MarkoutContext(typeof(FindRow))]
[MarkoutContext(typeof(FindMembersResultView))]
[MarkoutContext(typeof(FindMemberRow))]
[MarkoutContext(typeof(PackageProfileView))]
[MarkoutContext(typeof(PackageProfileRow))]
[MarkoutContext(typeof(ImplementsResultView))]
[MarkoutContext(typeof(ImplementerRow))]
[MarkoutContext(typeof(ExtensionsResultView))]
[MarkoutContext(typeof(ExtensionCountRow))]
[MarkoutContext(typeof(ExtensionRow))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchResultView))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchBlockerRow))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchBlockCorrespondenceRow))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchDiscoveryView))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchDiscoveryBlockerRow))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchDiscoveryCandidateRow))]
[MarkoutContext(typeof(DotnetInspector.Views.MatchDiscoveryCandidateTableView))]
public partial class SearchViewContext : MarkoutSerializerContext
{
}
