using System.Globalization;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using ILInspector.CSharp;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName),
    TitleContextProperty = nameof(TitleVersion),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public class InspectionResultView
{
    private readonly InspectionResult _data;
    private readonly bool _includeTitleVersion;

    public InspectionResultView(InspectionResult data, bool includeTitleVersion = true)
    {
        _data = data;
        _includeTitleVersion = includeTitleVersion;
    }

    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutPropertyName("Package")]
    public string PackageName => PackageViewText.Contain(_data.PackageName);

    /// <inheritdoc cref="PackageViewText"/>
    public string Version => PackageViewText.Contain(_data.Version);

    /// <inheritdoc cref="PackageViewText"/>
    public string? TitleVersion => _includeTitleVersion ? PackageViewText.Contain(_data.Version) : null;

    /// <inheritdoc cref="PackageViewText.ContainProse"/>
    public string? Description => PackageViewText.ContainProse(_data.Description);

    // ===== Field Collections for Serializer =====

    [MarkoutSection(Name = PackageSections.Summary, Headless = true)]
    public List<MarkoutField> Summary => GetCompactFields();

    [MarkoutIgnoreInTable]
    public List<DependencyGroup>? DependencyGroups => _data.DependencyGroups;

    [MarkoutSection(Name = PackageSections.Dependencies)]
    public List<FlatDependency>? FlatDependencies => _data.DependencyGroups is { } groups
        ? TfmSelector.OrderByTfmPriorityDescending(groups, g => g.TargetFramework)
            .ThenBy(g => g.TargetFramework)
            .SelectMany(g => g.Dependencies
                .OrderBy(d => d.Id)
                .Select(d => new FlatDependency
                {
                    TargetFramework = g.TargetFramework,
                    Id = d.Id,
                    Version = d.Version
                }))
            .ToList()
        : null;

    [MarkoutSection(Name = PackageSections.Files)]
    public List<PackageFileRow>? Files => _data.Files?
        .Select(ToFileRow)
        .ToList();

    [MarkoutSection(Name = PackageSections.PackageReadme)]
    public List<PackageFileRow>? PackageReadme => PackageReadmeFiles()
        ?.Select(ToFileRow)
        .ToList();

    [MarkoutSection(Name = PackageSections.LibraryFiles)]
    public List<PackageFileRow>? LibraryFiles => LibraryPackageFiles()
        ?.Select(ToFileRow)
        .ToList();

    [MarkoutSection(Name = PackageSections.MarkdownFiles)]
    public List<PackageFileRow>? MarkdownFiles => _data.PackageFiles?
        .Where(IsMarkdownFile)
        .Select(ToFileRow)
        .ToList();

    [MarkoutSection(Name = PackageSections.SourceFiles, EmptyText = "No SourceLink source files found for this package.")]
    public List<PackageSourceFileRow>? SourceFiles => _data.SourceFiles?
        .Select(row => new PackageSourceFileRow(row.Library, row.Type, row.Url))
        .ToList();

    [MarkoutSection(Name = PackageSections.Manifest)]
    public List<ManifestRow>? Manifest => !HasManifest ? null : GetManifestRows();

    [MarkoutIgnore]
    public bool HasManifest => !string.IsNullOrWhiteSpace(_data.PackageName)
        || !string.IsNullOrWhiteSpace(_data.Version)
        || !string.IsNullOrWhiteSpace(_data.ToolFormat)
        || _data.ToolCommands is { Count: > 0 }
        || _data.RuntimeIdentifierPackages is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.PackageInfo, FieldOrder = MarkoutFieldOrder.Alphabetical)]
    public List<MarkoutField> Metadata => GetMetadataFields();

    [MarkoutSection(Name = PackageSections.RuntimeDependencies)]
    public List<PackageDependency>? RuntimeDependencies => _data.RuntimeDependencies;

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.Signals, ShowWhenProperty = nameof(HasAuditSignals))]
    public List<AuditSignalRow>? SignalsSection =>
        _data.AuditSignals?.Select(s => new AuditSignalRow(s.Area, s.Signal, s.Value, s.Evidence)).ToList();

    [MarkoutSection(Name = PackageSections.Signature)]
    public SigningSection? SigningSectionData => _data.SignatureResult is { } sig
        ? new SigningSection
        {
            AuthorVerified = sig.AuthorVerified ? "Yes" : sig.IsUnsigned ? "No" : null,
            Publisher = !string.IsNullOrEmpty(sig.Publisher)
                ? $"{sig.Publisher}{(sig.AuthorVerified ? " (Verified)" : "")}"
                : null,
            Repository = sig.Repository,
            RepositoryVerified = sig.RepositoryVerified ? "Yes" : null,
            Signed = _data.Signed == true ? "Yes" : sig.IsUnsigned ? "No" : "Unknown",
            Status = sig.StatusMessage,
        }
        : null;

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutPropertyName("Published")]
    public DateTimeOffset? Published => _data.Published;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Downloads")]
    public long? TotalDownloads => _data.TotalDownloads;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Version Downloads")]
    public long? VersionDownloads => _data.VersionDownloads;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutPropertyName("Version Count")]
    public int? VersionCount => _data.VersionCount;

    [MarkoutSection(Name = PackageSections.TargetFrameworks)]
    public List<TargetFrameworkRow>? TargetFrameworkRows => _data.TargetFrameworks is { } tfms
        ? TfmSelector.OrderByTfmPriorityDescending(tfms, tfm => tfm)
            .Select(tfm => new TargetFrameworkRow(tfm))
            .ToList()
        : null;

    [MarkoutSection(Name = PackageSections.Vulnerabilities)]
    [MarkoutIgnoreInTable]
    public List<PackageVulnerability>? Vulnerabilities => _data.Vulnerabilities;

    /// <inheritdoc cref="PackageViewText"/>
    public string? Authors => PackageViewText.Contain(_data.Authors);
    /// <inheritdoc cref="PackageViewText"/>
    public string? License => PackageViewText.Contain(_data.License);
    /// <inheritdoc cref="PackageViewText"/>
    public string? LicenseUrl => PackageViewText.Contain(_data.LicenseUrl);
    /// <inheritdoc cref="PackageViewText"/>
    public string? Repository => PackageViewText.Contain(_data.Repository);
    /// <inheritdoc cref="PackageViewText"/>
    public string? RepositoryType => PackageViewText.Contain(_data.RepositoryType);
    /// <inheritdoc cref="PackageViewText"/>
    public string? RepositoryCommit => PackageViewText.Contain(_data.RepositoryCommit);

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutPropertyName("Built")]
    public DateTimeOffset? BuiltDate => _data.BuiltDate;

    public bool? IsVerified => _data.IsVerified;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Owners")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? Owners => _data.Owners?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutIgnoreInTable]
    public PackageDeprecation? Deprecation => _data.Deprecation;

    [MarkoutPropertyName("Vulnerabilities")]
    public string? VulnerabilitiesDisplay => Vulnerabilities is { Count: > 0 }
        ? $"{Vulnerabilities.Count} known ({string.Join(", ", Vulnerabilities.Select(v => v.Severity).Distinct())})"
        : null;

    [MarkoutSkipDefault]
    public bool HasReadme => _data.HasReadme;

    [MarkoutPropertyName("Readme")]
    public string? ReadmeFile => _data.HasReadme
        ? _data.PackageReadmeFile ?? _data.ReadmeFile ?? "README.md"
        : _data.ReadmeFile;

    [MarkoutSkipDefault]
    public bool IsToolPackage => _data.IsToolPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Package Types")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? PackageTypes => _data.PackageTypes?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutPropertyName("Package Type")]
    public string PackageType => _data.ToolFormat?.Contains("Version=\"2\"") == true
        ? "Tool v2"
        : _data.IsToolPackage ? "Tool" : "Library";

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Content")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? ContentDirectories => _data.ContentDirectories?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Target Frameworks")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? TargetFrameworks => _data.TargetFrameworks?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutPropertyName("TFM Count")]
    public int TargetFrameworkCount => _data.TargetFrameworks?.Count ?? 0;

    [MarkoutPropertyName("Highest TFM")]
    /// <inheritdoc cref="PackageViewText"/>
    public string? HighestTfm => _data.TargetFrameworks is { Count: > 0 }
        ? PackageViewText.Contain(TfmSelector.SelectHighestTfm(_data.TargetFrameworks))
        : null;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Supported RIDs")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? SupportedRids => _data.SupportedRids?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutPropertyName("Runtime Identifiers")]
    public int SupportedRidCount => _data.SupportedRids?.Count ?? 0;

    [MarkoutPropertyName("Libraries")]
    public int AssemblyCount => _data.AssemblyCount;

    [MarkoutPropertyName("Framework Dependent")]
    [MarkoutSkipDefault]
    public bool IsFrameworkDependent => _data.IsFrameworkDependent;

    [MarkoutPropertyName("RID-Specific Assets")]
    [MarkoutSkipDefault]
    public bool HasRidSpecificAssets => _data.HasRidSpecificAssets;

    [MarkoutPropertyName("Native Dependencies")]
    [MarkoutSkipDefault]
    public bool HasNativeDependencies => _data.HasNativeDependencies;

    [MarkoutPropertyName("Tool Format")]
    /// <inheritdoc cref="PackageViewText"/>
    public string? ToolFormat => PackageViewText.Contain(_data.ToolFormat);

    [MarkoutPropertyName("RID Pointer Package")]
    [MarkoutSkipDefault]
    public bool IsRidSpecificPointerPackage => _data.IsRidSpecificPointerPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Tool Commands")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? ToolCommands => _data.ToolCommands?.Select(value => PackageViewText.Contain(value)).ToList()!;

    [MarkoutPropertyName("Runtime Target RID")]
    /// <inheritdoc cref="PackageViewText"/>
    public string? RuntimeTargetRid => PackageViewText.Contain(_data.RuntimeTargetRid);

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Native Files")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? NativeFiles => _data.NativeFiles?.Select(value => PackageViewText.Contain(value)).ToList()!;

    private List<PackageFile>? LibraryPackageFiles()
    {
        if (_data.PackageFiles is { Count: > 0 } files)
        {
            var rows = files
                .Where(IsLibraryFile)
                .ToList();
            return rows.Count > 0 ? rows : null;
        }

        return _data.LibraryFiles?
            .Select(path => new PackageFile(path, 0))
            .ToList();
    }

    private List<PackageFile>? PackageReadmeFiles()
    {
        if (_data.PackageFiles is not { Count: > 0 } files || string.IsNullOrWhiteSpace(_data.PackageReadmeFile))
            return null;

        var readme = files
            .Where(file => string.Equals(file.Path, _data.PackageReadmeFile, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToList();
        return readme.Count > 0 ? readme : null;
    }

    private static PackageFileRow ToFileRow(PackageFile file)
        => new(file.Path, file.Size);

    private static bool IsLibraryFile(PackageFile file)
        => file.Path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkdownFile(PackageFile file)
        => file.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private List<MarkoutField> GetCompactFields()
    {
        List<MarkoutField> fields = [];

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));

        if (!string.IsNullOrEmpty(HighestTfm))
            fields.Add(new("Highest TFM", HighestTfm));
        if (TargetFrameworkCount > 0)
            fields.Add(new("TFM Count", TargetFrameworkCount.ToString()));
        if (_data.BuiltDate.HasValue)
            fields.Add(new("Built", _data.BuiltDate.Value.ToString("yyyy-MM-dd")));
        else if (_data.Published.HasValue)
            fields.Add(new("Published", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", PackageViewText.Contain(_data.Source)));
        if (_data.Deprecation != null)
            fields.Add(new("Deprecated", "Yes"));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(new("Vulnerabilities", _data.Vulnerabilities.Count.ToString()));

        return fields;
    }

    private List<MarkoutField> GetMetadataFields()
    {
        List<MarkoutField> fields = [];

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));
        if (_data.PackageSize.HasValue)
            fields.Add(new("Size", new ByteSizeFormatter().Format(_data.PackageSize.Value)));
        if (!string.IsNullOrEmpty(HighestTfm))
            fields.Add(new("Highest TFM", HighestTfm));
        if (TargetFrameworkCount > 0)
            fields.Add(new("TFM Count", TargetFrameworkCount.ToString()));
        if (_data.BuiltDate.HasValue)
            fields.Add(new("Built", _data.BuiltDate.Value.ToString("yyyy-MM-dd")));
        if (_data.Published.HasValue)
            fields.Add(new("Published", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", PackageViewText.Contain(_data.Source)));

        if (_data.Deprecation?.Summary != null)
            fields.Add(new("Deprecated Note", PackageViewText.Contain(_data.Deprecation.Summary)));

        if (!string.IsNullOrWhiteSpace(_data.Authors))
            fields.Add(new("Authors", PackageViewText.Contain(_data.Authors)));
        if (_data.Owners is { Count: > 0 })
            fields.Add(new("Owners", PackageViewText.Contain(string.Join(", ", _data.Owners))));
        if (!string.IsNullOrWhiteSpace(_data.License))
            fields.Add(new("License", PackageViewText.Contain(_data.License)));
        if (!string.IsNullOrWhiteSpace(_data.LicenseUrl))
            fields.Add(new("License URL", PackageViewText.Contain(_data.LicenseUrl)));
        if (!string.IsNullOrWhiteSpace(_data.Repository))
            fields.Add(new("Repository", PackageViewText.Contain(_data.Repository)));
        if (!string.IsNullOrWhiteSpace(_data.RepositoryType))
            fields.Add(new("Repository Type", PackageViewText.Contain(_data.RepositoryType)));
        if (!string.IsNullOrWhiteSpace(_data.RepositoryCommit))
            fields.Add(new("Repository Commit", PackageViewText.Contain(_data.RepositoryCommit)));

        if (_data.IsVerified == true)
            fields.Add(new("Verified", "Yes"));

        if (_data.Signed.HasValue)
            fields.Add(new("Signed", PackageViewText.Contain(_data.Signed.Value ? "Yes" : "No")));

        if (_data.ContentDirectories is { Count: > 0 })
            fields.Add(new("Content", PackageViewText.Contain(string.Join(", ", _data.ContentDirectories))));
        if (SupportedRidCount > 0)
            fields.Add(new("Runtime Identifiers", SupportedRidCount.ToString()));
        if (_data.AssemblyCount > 1)
            fields.Add(new("Libraries", _data.AssemblyCount.ToString()));
        if (_data.HasReadme)
            fields.Add(new("Readme", PackageViewText.Contain(_data.PackageReadmeFile ?? _data.ReadmeFile ?? "README.md")));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(new("Vulnerabilities", _data.Vulnerabilities.Count.ToString()));

        if (_data.ToolCommands is { Count: > 0 })
            fields.Add(new("Tool Commands", PackageViewText.Contain(string.Join(", ", _data.ToolCommands))));

        if (_data.IsFrameworkDependent)
            fields.Add(new("Framework Dependent", "Yes"));
        if (_data.IsRidSpecificPointerPackage)
            fields.Add(new("RID-Specific Pointer", "Yes"));
        if (!string.IsNullOrWhiteSpace(_data.RuntimeTargetRid))
            fields.Add(new("Runtime Target RID", PackageViewText.Contain(_data.RuntimeTargetRid)));

        return fields;
    }

    private List<ManifestRow> GetManifestRows()
    {
        List<ManifestRow> rows = [];

        if (!string.IsNullOrWhiteSpace(_data.ManifestVersion))
            rows.Add(new("Info", "Manifest Version", _data.ManifestVersion, "n/a"));
        if (!string.IsNullOrWhiteSpace(_data.PackageName))
            rows.Add(new("Info", "Package", _data.PackageName, "n/a"));
        if (!string.IsNullOrWhiteSpace(_data.Version))
            rows.Add(new("Info", "Version", _data.Version, "n/a"));
        rows.Add(new("Info", "Type", PackageType, "n/a"));
        if (_data.ToolCommands is { Count: > 0 })
            rows.Add(new("Info", "Commands", string.Join(", ", _data.ToolCommands), "n/a"));
        if (_data.RuntimeIdentifierPackages is { Count: > 0 })
        {
            rows.AddRange(_data.RuntimeIdentifierPackages.Select(r =>
                new ManifestRow("RID Package", r.RuntimeIdentifier, r.PackageId, r.AvailableDisplay)));
        }

        return rows;
    }

}

public class SigningSection
{
    [MarkoutPropertyName("Author Verified")]
    public string? AuthorVerified { get; init; }
    public string? Publisher { get; init; }
    public string? Repository { get; init; }
    [MarkoutPropertyName("Repository Verified")]
    public string? RepositoryVerified { get; init; }
    public string Signed { get; init; } = "Unknown";
    public string? Status { get; init; }
}

/// <inheritdoc cref="PackageViewText"/>
[MarkoutSerializable]
public record ManifestRow(
    string Kind,
    string Name,
    string Value,
    string? Available)
{
    public string Kind { get; init; } = PackageViewText.Contain(Kind);
    public string Name { get; init; } = PackageViewText.Contain(Name);
    public string Value { get; init; } = PackageViewText.Contain(Value);
    public string? Available { get; init; } = PackageViewText.Contain(Available);
}

/// <inheritdoc cref="PackageViewText"/>
[MarkoutSerializable]
public record TargetFrameworkRow(string Tfm)
{
    [MarkoutPropertyName("TFM")]
    public string Tfm { get; init; } = PackageViewText.Contain(Tfm);
}

/// <summary>
/// Containment for text rendered by the package views.
/// </summary>
/// <remarks>
/// A .nupkg is untrusted input: its ZIP entry names and nuspec text are chosen
/// by whoever built it. Text carrying a line terminator, ANSI escape, or bidi
/// override breaks out of its Markdown table cell and injects text that reads
/// as genuine tool output (issue #3319). These rows are presentation-only --
/// path filtering runs against <c>PackageFile</c>, the model, not against these
/// rows -- so containment here cannot affect matching.
/// </remarks>
internal static class PackageViewText
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Contain(string? value)
        => value is null ? null : CSharpIdentifier.ContainRenderedText(value);

    /// <summary>
    /// Containment for a free-form prose block, as distinct from a table cell.
    /// </summary>
    /// <remarks>
    /// A nuspec description is legitimately several paragraphs, and it renders
    /// as a standalone block rather than inside a cell, so folding its line
    /// endings the way <see cref="Contain"/> does would silently merge and drop
    /// paragraphs of real package documentation. This escapes every other
    /// rendering hazard -- bidi overrides, ANSI escapes, C0/C1 controls, and the
    /// U+2028/U+2029 separators -- and deliberately leaves CR and LF alone.
    ///
    /// The boundary that leaves is explicit and unverified by any gate: a
    /// hostile description can still introduce a line break, and because the
    /// block is rendered as Markdown it could already introduce a heading or a
    /// table before this change. Constraining a package's own description to
    /// inert text is a separate decision from #3319, which is about untrusted
    /// text escaping a structure it was placed inside.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? ContainProse(string? value)
    {
        if (value is null || !value.Any(NeedsEscape))
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (NeedsEscape(ch))
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}");
            else
                builder.Append(ch);
        }

        return builder.ToString();

        // U+2028 and U+2029 are categories Zl/Zp, so char.IsControl is false for
        // them and IsRenderingHazard does not claim them; they are named here
        // because a terminal still breaks the line on them, which would split
        // the block just as CR/LF do without being visible in the source text.
        static bool NeedsEscape(char ch)
            => ch is '\u2028' or '\u2029'
                || (ch is not '\n' and not '\r' and not '\t' && CSharpIdentifier.IsRenderingHazard(ch));
    }
}

[MarkoutSerializable]
public record PackageFileRow(
    string Path,
    long Size)
{
    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutPropertyName("Path")]
    public string Path { get; init; } = PackageViewText.Contain(Path);

    [MarkoutPropertyName("Size")]
    public long Size { get; init; } = Size;
}

[MarkoutSerializable]
public record PackageSourceFileRow(
    string Library,
    string Type,
    string? Url)
{
    /// <inheritdoc cref="PackageViewText"/>
    public string Library { get; init; } = PackageViewText.Contain(Library);

    /// <inheritdoc cref="PackageViewText"/>
    public string Type { get; init; } = PackageViewText.Contain(Type);

    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutSkipNull]
    public string? Url { get; init; } = PackageViewText.Contain(Url);
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(InspectionResultView))]
[MarkoutContext(typeof(LibraryInspectionView))]
[MarkoutContext(typeof(LibraryInspectionReport))]
[MarkoutContext(typeof(ReferenceRow))]
[MarkoutContext(typeof(ExtensionMethodRow))]
[MarkoutContext(typeof(ClassifiedMethodRow))]
[MarkoutContext(typeof(PInvokeMethodRow))]
[MarkoutContext(typeof(ResourceRow))]
[MarkoutContext(typeof(ResourceTriageRow))]
[MarkoutContext(typeof(PerformanceRow))]
[MarkoutContext(typeof(PerformanceGroupRow))]
[MarkoutContext(typeof(PerformanceGroupView))]
[MarkoutContext(typeof(CustomAttributeRow))]
[MarkoutContext(typeof(TypeForwarderRow))]
[MarkoutContext(typeof(AuditSignalRow))]
[MarkoutContext(typeof(InspectionFailureRow))]
[MarkoutContext(typeof(SwitchRow))]
[MarkoutContext(typeof(IntegrationRow))]
[MarkoutContext(typeof(IntegrationOpportunityRow))]
[MarkoutContext(typeof(IntegrationSignalRow))]
[MarkoutContext(typeof(IntegrationApiSignalRow))]
[MarkoutContext(typeof(DependencyGroup))]
[MarkoutContext(typeof(PackageDependency))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(TargetFrameworkRow))]
[MarkoutContext(typeof(PackageFileRow))]
[MarkoutContext(typeof(PackageSourceFileRow))]
[MarkoutContext(typeof(ILOffsetSection))]
[MarkoutContext(typeof(ILOffsetMemberContextSection))]
[MarkoutContext(typeof(ILOffsetInstructionContextSection))]
[MarkoutContext(typeof(ILOffsetExceptionContextRow))]
[MarkoutContext(typeof(ILOffsetCallsiteContextSection))]
[MarkoutContext(typeof(ILOffsetReturnAddressContextSection))]
[MarkoutContext(typeof(ManifestRow))]
[MarkoutContext(typeof(RidPackageReferenceView))]
[MarkoutContext(typeof(EmptyDepsView))]
public partial class InspectionContext : MarkoutSerializerContext
{
}
