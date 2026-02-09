using System.Text.Json.Serialization;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector;

// Summary helper for AssemblyInfo in table display

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFields = false)]
public class AssemblyAudit
{
    [MarkoutIgnore]
    [JsonIgnore]
    public string? Tfm { get; set; }

    [MarkoutPropertyName("File")]
    public string FileName { get; set; } = "";

    [MarkoutPropertyName("Type")]
    public string FileType { get; set; } = "";

    /// <summary>
    /// PDB format: "Portable PDB", "Windows PDB", or null if none.
    /// </summary>
    [MarkoutPropertyName("PDB Format")]
    public string? PdbFormat { get; set; }

    /// <summary>
    /// Where the PDB is located: "Embedded", "Standalone", or null if unknown.
    /// </summary>
    [MarkoutPropertyName("PDB Location")]
    public string? PdbLocation { get; set; }

    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath { get; set; }

    [MarkoutPropertyName("Embedded PDB")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasEmbeddedPdb { get; set; }

    [MarkoutPropertyName("Reproducible Flag")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasReproducibleFlag { get; set; }

    [MarkoutIgnore]
    public bool? HasNormalizedPaths { get; set; }

    [MarkoutPropertyName("SourceLink")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasSourceLink { get; set; }

    /// <summary>
    /// Explanation for why SourceLink is unavailable (e.g., "Distro build (ReadyToRun)").
    /// Only set when HasSourceLink is false and we can determine the reason.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("source_link_unavailable_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkUnavailableReason { get; set; }

    /// <summary>
    /// Returns SourceLink status with explanation if unavailable.
    /// </summary>
    [MarkoutPropertyName("SourceLink Status")]
    [JsonIgnore]
    public string SourceLinkStatus => HasSourceLink
        ? "✓"
        : SourceLinkUnavailableReason != null
            ? $"✗ ({SourceLinkUnavailableReason})"
            : "✗";

    [MarkoutBoolFormat("✓", "✗")]
    public bool IsDeterministic { get; set; }

    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Indicates that a Windows PDB was detected (not supported by this tool).
    /// </summary>
    [MarkoutIgnore]
    public bool WindowsPdbDetected { get; set; }

    /// <summary>
    /// The server the PDB was retrieved from (e.g., "nuget.org", "msdl.microsoft.com"), or null if local/embedded.
    /// </summary>
    [MarkoutPropertyName("Symbol Server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SymbolServer { get; set; }

    /// <summary>
    /// Inferred builder of the assembly based on symbol availability and SourceLink.
    /// </summary>
    [MarkoutPropertyName("Builder")]
    [JsonPropertyName("builder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builder { get; set; }

    /// <summary>
    /// Publisher identity from NuGet package author signature (CN).
    /// </summary>
    [MarkoutPropertyName("Publisher")]
    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    /// <summary>
    /// Whether the package publisher signature was cryptographically verified.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("publisher_verified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublisherVerified { get; set; }

    /// <summary>
    /// Whether the package repository signature was cryptographically verified.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("repository_verified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RepositoryVerified { get; set; }

    /// <summary>
    /// Status message when signature verification was skipped or failed.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("signature_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [MarkoutIgnore]
    public string? SourceLinkJson { get; set; }

    [MarkoutIgnore]
    public List<string>? NonNormalizedPaths { get; set; }

    // Strict audit: source verification results
    /// <summary>
    /// Total number of source documents in the PDB.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("total_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalSourceFiles { get; set; }

    /// <summary>
    /// Number of source files accessible via SourceLink (HTTP 200).
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("accessible_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AccessibleSourceFiles { get; set; }

    /// <summary>
    /// Number of source files embedded in the PDB.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("embedded_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EmbeddedSourceFiles { get; set; }

    /// <summary>
    /// Source files that are neither accessible via SourceLink nor embedded.
    /// Only populated in strict audit mode.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("missing_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingSourceFiles { get; set; }

    /// <summary>
    /// Whether all source files are accessible (via SourceLink or embedded).
    /// Only set in strict audit mode.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("all_sources_accessible")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllSourcesAccessible { get; set; }

    /// <summary>
    /// Human-readable source coverage summary for display.
    /// </summary>
    [MarkoutPropertyName("Source Coverage")]
    [JsonIgnore]
    public string? SourceCoverageSummary => TotalSourceFiles > 0
        ? $"{AccessibleSourceFiles + EmbeddedSourceFiles}/{TotalSourceFiles} files"
        : null;

    // Assembly metadata
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    // Public API surface
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    // Computed summary properties for MDF table display
    [MarkoutPropertyName("Assembly")]
    [JsonIgnore]
    public string? AssemblySummary => AssemblyInfo switch
    {
        null => null,
        var info => string.Join(", ", new[]
        {
            info.Architecture,
            info.TargetFramework,
            info.CompilationType,
            info.IsSigned ? "Signed" : null
        }.Where(s => !string.IsNullOrEmpty(s)))
    };

    [MarkoutPropertyName("API")]
    [JsonIgnore]
    public string? ApiSummary => ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };

    // ===== Field Collection Sections for Source-Gen Serializer =====

    [MarkoutSection(Name = "Assembly Info")]
    [JsonIgnore]
    public List<MarkoutField> AssemblyInfoSection => GetAssemblyInfoFields();

    [MarkoutSection(Name = "Symbols")]
    [JsonIgnore]
    public List<MarkoutField> SymbolsSection => GetSymbolsFields();

    [MarkoutSection(Name = "Source Coverage")]
    [JsonIgnore]
    public List<MarkoutField> SourceCoverageSection => GetSourceCoverageFields();

    [MarkoutIgnore]
    [JsonIgnore]
    public bool UseDependenciesView { get; set; }

    [MarkoutSection(Name = "Assembly References")]
    [JsonIgnore]
    public List<ReferenceRow>? AssemblyReferencesSection =>
        AssemblyInfo?.TransitiveReferences is { Count: > 0 } ? null :
        AssemblyInfo?.References?.OrderBy(r => r.Name)
            .Select(r => new ReferenceRow(r.Name, r.Version, r.PublicKeyToken ?? "-"))
            .ToList() is { Count: > 0 } list ? list : null;

    [MarkoutSection(Name = "Assembly References (Transitive)")]
    [JsonIgnore]
    public List<TreeNode>? TransitiveReferencesSection =>
        UseDependenciesView || AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildFlatTransitiveTree(AssemblyInfo.TransitiveReferences);

    [MarkoutSection(Name = "Dependencies")]
    [JsonIgnore]
    public List<TreeNode>? DependenciesSection =>
        !UseDependenciesView || AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildNestedDependencyTree(AssemblyInfo.TransitiveReferences);

    [MarkoutSection(Name = "Non-normalized Paths")]
    [JsonIgnore]
    public List<string>? NonNormalizedPathsSection =>
        NonNormalizedPaths is { Count: > 0 } ? NonNormalizedPaths : null;

    [MarkoutSection(Name = "Missing Sources")]
    [JsonIgnore]
    public List<string>? MissingSourcesSection => GetMissingSourcesDisplay();

    private List<MarkoutField> GetAssemblyInfoFields()
    {
        var fields = new List<MarkoutField>();
        if (AssemblyInfo is not { } info) return fields;

        if (!string.IsNullOrEmpty(info.AssemblyName))
            fields.Add(new("Name", info.AssemblyName));
        if (!string.IsNullOrEmpty(info.AssemblyVersion))
            fields.Add(new("Version", info.AssemblyVersion));
        if (!string.IsNullOrEmpty(info.TargetFramework))
            fields.Add(new("Target Framework", info.TargetFramework));
        if (!string.IsNullOrEmpty(info.Architecture))
            fields.Add(new("Architecture", info.Architecture));
        if (!string.IsNullOrEmpty(info.CompilationType))
            fields.Add(new("Compilation", info.CompilationType));
        if (!string.IsNullOrEmpty(info.InformationalVersion))
            fields.Add(new("Informational Version", info.InformationalVersion));
        if (!string.IsNullOrEmpty(info.Product))
            fields.Add(new("Product", info.Product));
        if (!string.IsNullOrEmpty(info.Company))
            fields.Add(new("Company", info.Company));
        if (!string.IsNullOrEmpty(info.Copyright))
            fields.Add(new("Copyright", info.Copyright));
        if (info.IsSigned)
            fields.Add(new("Signed", "Yes"));
        if (!string.IsNullOrEmpty(info.PublicKeyToken))
            fields.Add(new("Public Key Token", info.PublicKeyToken));
        fields.Add(new("Deterministic", IsDeterministic ? "✓" : "✗"));
        fields.Add(new("Reproducible", HasReproducibleFlag ? "✓" : "✗"));

        return fields;
    }

    private List<MarkoutField> GetSymbolsFields()
    {
        var fields = new List<MarkoutField>
        {
            new("PDB Format", PdbFormat ?? "Unknown"),
            new("PDB Location", PdbLocation ?? "Unknown")
        };

        if (!string.IsNullOrEmpty(SymbolServer))
            fields.Add(new("Symbol Server", SymbolServer));
        if (!string.IsNullOrEmpty(PdbPath))
            fields.Add(new("PDB Path", PdbPath));
        if (PdbLocation == null && !string.IsNullOrEmpty(PdbPath))
            fields.Add(new("Note", "Path is from the CodeView record; actual PDB location is unknown"));

        fields.Add(new("SourceLink", SourceLinkStatus));

        if (!string.IsNullOrEmpty(Builder))
            fields.Add(new("Builder", Builder));
        if (!string.IsNullOrEmpty(Publisher))
        {
            var publisherStatus = PublisherVerified ? "(Verified)" : "";
            fields.Add(new("Publisher", $"{Publisher} {publisherStatus}".Trim()));
        }
        else if (!string.IsNullOrEmpty(SignatureStatus))
        {
            fields.Add(new("Publisher", SignatureStatus));
        }
        if (RepositoryVerified)
            fields.Add(new("Repository", "nuget.org (Verified)"));
        if (!string.IsNullOrEmpty(RepositoryUrl))
            fields.Add(new("Repository URL", RepositoryUrl));

        if (WindowsPdbDetected)
        {
            fields.Add(new("Warning", "Windows PDB format is not supported by this tool"));
            fields.Add(new("Recommendation", "Consider asking the package maintainer to publish Portable PDBs"));
        }

        return fields;
    }

    private List<MarkoutField> GetSourceCoverageFields()
    {
        var fields = new List<MarkoutField>();
        if (TotalSourceFiles <= 0) return fields;

        int accessible = AccessibleSourceFiles + EmbeddedSourceFiles;
        string status = AllSourcesAccessible == true ? "✓" : "✗";
        fields.Add(new("Status", $"{status} {accessible}/{TotalSourceFiles} files accessible"));

        if (EmbeddedSourceFiles > 0)
            fields.Add(new("Embedded", $"{EmbeddedSourceFiles} files"));

        return fields;
    }

    private List<string>? GetMissingSourcesDisplay()
    {
        if (MissingSourceFiles is not { Count: > 0 }) return null;
        var display = MissingSourceFiles.Take(10).Select(f => $"`{f}`").ToList();
        if (MissingSourceFiles.Count > 10)
            display.Add($"... and {MissingSourceFiles.Count - 10} more");
        return display;
    }

    private static List<TreeNode> BuildFlatTransitiveTree(List<AssemblyReferenceNode> nodes)
    {
        var result = new List<TreeNode>();
        foreach (var node in nodes)
        {
            var icon = node.ResolvedFrom switch
            {
                "local" => "📁",
                "platform" => "🚢",
                _ => "❓"
            };
            var suffix = node.IsCyclic ? " (circular)" : "";
            result.Add(new TreeNode($"{node.Name} {node.Version}{suffix}", icon));
        }
        return result;
    }

    private static List<TreeNode> BuildNestedDependencyTree(List<AssemblyReferenceNode> nodes)
    {
        var result = new List<TreeNode>();
        int i = 0;
        BuildNestedNodes(nodes, ref i, 0, result);
        return result;
    }

    private static void BuildNestedNodes(List<AssemblyReferenceNode> nodes, ref int index, int currentDepth, List<TreeNode> target)
    {
        while (index < nodes.Count && nodes[index].Depth == currentDepth)
        {
            var node = nodes[index];
            var label = $"{node.Name} {node.Version}";
            index++;

            var children = new List<TreeNode>();
            if (index < nodes.Count && nodes[index].Depth > currentDepth)
            {
                BuildNestedNodes(nodes, ref index, currentDepth + 1, children);
            }

            target.Add(children.Count > 0 ? new TreeNode(label, children) : new TreeNode(label));
        }
    }
}

[MarkoutSerializable]
public record ReferenceRow(
    string Name,
    string Version,
    [property: MarkoutPropertyName("Public Key Token")] string PublicKeyToken);

/// <summary>
/// Standalone view model for assembly dependencies (--dependencies).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class AssemblyDependenciesView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    [MarkoutIgnore]
    public string? AssemblyName { get; set; }

    [MarkoutIgnore]
    public string? Version { get; set; }

    [MarkoutIgnore]
    public string? Tfm { get; set; }

    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Identity => GetIdentityFields();

    [MarkoutIgnoreInTable]
    public List<TreeNode> Dependencies { get; set; } = [];

    private List<MarkoutField> GetIdentityFields()
    {
        var fields = new List<MarkoutField>();
        if (!string.IsNullOrEmpty(AssemblyName))
            fields.Add(new("Assembly", AssemblyName));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        return fields;
    }

    public static AssemblyDependenciesView FromAudit(AssemblyAudit audit)
    {
        var info = audit.AssemblyInfo;
        var treeNodes = audit.DependenciesSection ?? [];

        return new AssemblyDependenciesView
        {
            Title = audit.FileName,
            AssemblyName = info?.AssemblyName,
            Version = info?.AssemblyVersion,
            Tfm = audit.Tfm ?? info?.TargetFramework,
            Dependencies = treeNodes
        };
    }
}

[MarkoutContext(typeof(AssemblyDependenciesView))]
public partial class AssemblyDependenciesContext : MarkoutSerializerContext
{
}
