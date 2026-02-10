using DotnetInspector.Models;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFields = false)]
public class AssemblyAuditView
{
    private readonly AssemblyAudit _data;

    public AssemblyAuditView(AssemblyAudit data)
    {
        _data = data;
    }

    [MarkoutIgnore]
    public string? Tfm => _data.Tfm;

    [MarkoutPropertyName("File")]
    public string FileName => _data.FileName;

    [MarkoutPropertyName("Type")]
    public string FileType => _data.FileType;

    [MarkoutPropertyName("PDB Format")]
    public string? PdbFormat => _data.PdbFormat;

    [MarkoutPropertyName("PDB Location")]
    public string? PdbLocation => _data.PdbLocation;

    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath => _data.PdbPath;

    [MarkoutPropertyName("Embedded PDB")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasEmbeddedPdb => _data.HasEmbeddedPdb;

    [MarkoutPropertyName("Reproducible Flag")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasReproducibleFlag => _data.HasReproducibleFlag;

    [MarkoutIgnore]
    public bool? HasNormalizedPaths => _data.HasNormalizedPaths;

    [MarkoutPropertyName("SourceLink")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasSourceLink => _data.HasSourceLink;

    [MarkoutIgnore]
    public string? SourceLinkUnavailableReason => _data.SourceLinkUnavailableReason;

    [MarkoutPropertyName("SourceLink Status")]
    public string SourceLinkStatus => _data.HasSourceLink
        ? "✓"
        : _data.SourceLinkUnavailableReason != null
            ? $"✗ ({_data.SourceLinkUnavailableReason})"
            : "✗";

    [MarkoutBoolFormat("✓", "✗")]
    public bool IsDeterministic => _data.IsDeterministic;

    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl => _data.RepositoryUrl;

    [MarkoutIgnore]
    public bool WindowsPdbDetected => _data.WindowsPdbDetected;

    [MarkoutPropertyName("Symbol Server")]
    public string? SymbolServer => _data.SymbolServer;

    [MarkoutPropertyName("Builder")]
    public string? Builder => _data.Builder;

    [MarkoutPropertyName("Publisher")]
    public string? Publisher => _data.Publisher;

    [MarkoutIgnore]
    public bool PublisherVerified => _data.PublisherVerified;

    [MarkoutIgnore]
    public bool RepositoryVerified => _data.RepositoryVerified;

    [MarkoutIgnore]
    public string? SignatureStatus => _data.SignatureStatus;

    [MarkoutIgnore]
    public string? SourceLinkJson => _data.SourceLinkJson;

    [MarkoutIgnore]
    public List<string>? NonNormalizedPaths => _data.NonNormalizedPaths;

    [MarkoutIgnore]
    public int TotalSourceFiles => _data.TotalSourceFiles;

    [MarkoutIgnore]
    public int AccessibleSourceFiles => _data.AccessibleSourceFiles;

    [MarkoutIgnore]
    public int EmbeddedSourceFiles => _data.EmbeddedSourceFiles;

    [MarkoutIgnore]
    public List<string>? MissingSourceFiles => _data.MissingSourceFiles;

    [MarkoutIgnore]
    public bool? AllSourcesAccessible => _data.AllSourcesAccessible;

    [MarkoutIgnore]
    public AssemblyInfo? AssemblyInfo => _data.AssemblyInfo;

    [MarkoutIgnore]
    public ApiSurface? ApiSurface => _data.ApiSurface;

    [MarkoutPropertyName("Library")]
    public string? AssemblySummary => _data.AssemblyInfo switch
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
    public string? ApiSummary => _data.ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };

    // ===== Field Collection Sections =====

    [MarkoutSection(Name = "Library Info")]
    public List<MarkoutField> AssemblyInfoSection => GetAssemblyInfoFields();

    [MarkoutSection(Name = "Symbols")]
    public List<MarkoutField> SymbolsSection => GetSymbolsFields();

    [MarkoutSection(Name = "Source Coverage")]
    public List<MarkoutField> SourceCoverageSection => GetSourceCoverageFields();

    [MarkoutIgnore]
    public bool UseDependenciesView => _data.UseDependenciesView;

    [MarkoutSection(Name = "Library References")]
    public List<ReferenceRow>? AssemblyReferencesSection =>
        _data.AssemblyInfo?.TransitiveReferences is { Count: > 0 } ? null :
        _data.AssemblyInfo?.References?.OrderBy(r => r.Name)
            .Select(r => new ReferenceRow(r.Name, r.Version, r.PublicKeyToken ?? "-"))
            .ToList() is { Count: > 0 } list ? list : null;

    [MarkoutSection(Name = "Library References (Transitive)")]
    public List<TreeNode>? TransitiveReferencesSection =>
        _data.UseDependenciesView || _data.AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildFlatTransitiveTree(_data.AssemblyInfo.TransitiveReferences);

    [MarkoutSection(Name = "Dependencies")]
    public List<TreeNode>? DependenciesSection =>
        !_data.UseDependenciesView || _data.AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildNestedDependencyTree(_data.AssemblyInfo.TransitiveReferences);

    [MarkoutIgnore]
    public bool HasExtensionMethods => _data.ExtensionMethods is { Count: > 0 };

    [MarkoutSection(Name = "Extension Methods", ShowWhenProperty = nameof(HasExtensionMethods))]
    public List<ExtensionMethodRow>? ExtensionMethodsSection =>
        _data.ExtensionMethods?.Select(e =>
        {
            var name = e.Overloads > 1 ? $"{e.MethodName} ({e.Overloads} overloads)" : e.MethodName;
            return new ExtensionMethodRow(name, e.Kind, e.ExtendedType, e.ExtensionClass);
        }).ToList();

    [MarkoutIgnore]
    public bool HasUnsafeMethods => _data.UnsafeMethods is { Count: > 0 };

    [MarkoutSection(Name = "Unsafe Methods", ShowWhenProperty = nameof(HasUnsafeMethods))]
    public List<ClassifiedMethodRow>? UnsafeMethodsSection =>
        _data.UnsafeMethods?.Select(m => new ClassifiedMethodRow(m.MethodName, m.DeclaringType, m.Signature)).ToList();

    [MarkoutIgnore]
    public bool HasPInvokeMethods => _data.PInvokeMethods is { Count: > 0 };

    [MarkoutSection(Name = "P/Invoke Methods", ShowWhenProperty = nameof(HasPInvokeMethods))]
    public List<PInvokeMethodRow>? PInvokeMethodsSection =>
        _data.PInvokeMethods?.Select(m => new PInvokeMethodRow(m.MethodName, m.DeclaringType, m.ModuleName ?? "", m.Signature)).ToList();

    [MarkoutIgnore]
    public bool HasResources => _data.Resources is { Count: > 0 };

    [MarkoutSection(Name = "Resources", ShowWhenProperty = nameof(HasResources))]
    public List<ResourceRow>? ResourcesSection =>
        _data.Resources?.Select(r => new ResourceRow(r.Name, r.Visibility, FormatSize(r.Size))).ToList();

    [MarkoutIgnore]
    public bool HasNonNormalizedPaths => _data.NonNormalizedPaths is { Count: > 0 };

    [MarkoutSection(Name = "Non-normalized Paths", ShowWhenProperty = nameof(HasNonNormalizedPaths))]
    public List<string>? NonNormalizedPathsSection => _data.NonNormalizedPaths;

    [MarkoutIgnore]
    public bool HasMissingSources => _data.MissingSourceFiles is { Count: > 0 };

    [MarkoutSection(Name = "Missing Sources", ShowWhenProperty = nameof(HasMissingSources))]
    [MarkoutMaxItems(10)]
    public List<string>? MissingSourcesSection =>
        _data.MissingSourceFiles?.Select(f => $"`{f}`").ToList();

    private List<MarkoutField> GetAssemblyInfoFields()
    {
        List<MarkoutField> fields = [];
        if (_data.AssemblyInfo is not { } info) return fields;

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
        fields.Add(new("Deterministic", _data.IsDeterministic ? "✓" : "✗"));
        fields.Add(new("Reproducible", _data.HasReproducibleFlag ? "✓" : "✗"));

        return fields;
    }

    private List<MarkoutField> GetSymbolsFields()
    {
        List<MarkoutField> fields =
        [
            new("PDB Format", _data.PdbFormat ?? "Unknown"),
            new("PDB Location", _data.PdbLocation ?? "Unknown")
        ];

        if (!string.IsNullOrEmpty(_data.SymbolServer))
            fields.Add(new("Symbol Server", _data.SymbolServer));
        if (!string.IsNullOrEmpty(_data.PdbPath))
            fields.Add(new("PDB Path", _data.PdbPath));
        if (_data.PdbLocation == null && !string.IsNullOrEmpty(_data.PdbPath))
            fields.Add(new("Note", "Path is from the CodeView record; actual PDB location is unknown"));

        fields.Add(new("SourceLink", SourceLinkStatus));

        if (!string.IsNullOrEmpty(_data.Builder))
            fields.Add(new("Builder", _data.Builder));
        if (!string.IsNullOrEmpty(_data.Publisher))
        {
            var publisherStatus = _data.PublisherVerified ? "(Verified)" : "";
            fields.Add(new("Publisher", $"{_data.Publisher} {publisherStatus}".Trim()));
        }
        else if (!string.IsNullOrEmpty(_data.SignatureStatus))
        {
            fields.Add(new("Publisher", _data.SignatureStatus));
        }
        if (_data.RepositoryVerified)
            fields.Add(new("Repository", "nuget.org (Verified)"));
        if (!string.IsNullOrEmpty(_data.RepositoryUrl))
            fields.Add(new("Repository URL", _data.RepositoryUrl));

        if (_data.WindowsPdbDetected)
        {
            fields.Add(new("Warning", "Windows PDB format is not supported by this tool"));
            fields.Add(new("Recommendation", "Consider asking the package maintainer to publish Portable PDBs"));
        }

        return fields;
    }

    private List<MarkoutField> GetSourceCoverageFields()
    {
        List<MarkoutField> fields = [];
        if (_data.TotalSourceFiles <= 0) return fields;

        int accessible = _data.AccessibleSourceFiles + _data.EmbeddedSourceFiles;
        string status = _data.AllSourcesAccessible == true ? "✓" : "✗";
        fields.Add(new("Status", $"{status} {accessible}/{_data.TotalSourceFiles} files accessible"));

        if (_data.EmbeddedSourceFiles > 0)
            fields.Add(new("Embedded", $"{_data.EmbeddedSourceFiles} files"));

        return fields;
    }

    private static string FormatSize(int bytes) => bytes switch
    {
        0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static List<TreeNode> BuildFlatTransitiveTree(List<AssemblyReferenceNode> nodes)
    {
        List<TreeNode> result = [];
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
        List<TreeNode> result = [];
        int i = 0;
        BuildNestedNodes(nodes, ref i, 0, result);
        return result;
    }

    private static void BuildNestedNodes(List<AssemblyReferenceNode> nodes, ref int index, int currentDepth, List<TreeNode> target)
    {
        while (index < nodes.Count && nodes[index].Depth == currentDepth)
        {
            var node = nodes[index];
            var label = !string.IsNullOrEmpty(node.Company)
                ? $"{node.Name} {node.Version} [{node.Company}]"
                : $"{node.Name} {node.Version}";
            index++;

            List<TreeNode> children = [];
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

[MarkoutSerializable]
public record ExtensionMethodRow(
    string Name,
    string Kind,
    [property: MarkoutPropertyName("Extended Type")] string ExtendedType,
    string Class);

[MarkoutSerializable]
public record ClassifiedMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Signature);

[MarkoutSerializable]
public record PInvokeMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Module,
    string Signature);

[MarkoutSerializable]
public record ResourceRow(
    string Name,
    string Visibility,
    string Size);
