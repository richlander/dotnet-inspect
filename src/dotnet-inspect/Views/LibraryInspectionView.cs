using DotnetInspector.Models;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFields = false)]
public class LibraryInspectionView
{
    private readonly LibraryInspection _data;

    public LibraryInspectionView(LibraryInspection data)
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
    [MarkoutBoolFormat("Yes", "No")]
    public bool HasEmbeddedPdb => _data.HasEmbeddedPdb;

    [MarkoutPropertyName("Reproducible Flag")]
    [MarkoutBoolFormat("Yes", "No")]
    public bool HasReproducibleFlag => _data.HasReproducibleFlag;

    [MarkoutIgnore]
    public bool? HasNormalizedPaths => _data.HasNormalizedPaths;

    [MarkoutPropertyName("SourceLink")]
    [MarkoutBoolFormat("Yes", "No")]
    public bool HasSourceLink => _data.HasSourceLink;

    [MarkoutIgnore]
    public string? SourceLinkUnavailableReason => _data.SourceLinkUnavailableReason;

    [MarkoutPropertyName("SourceLink Status")]
    public string SourceLinkStatus => _data.HasSourceLink
        ? "Yes"
        : _data.SourceLinkUnavailableReason != null
            ? $"No ({_data.SourceLinkUnavailableReason})"
            : "No";

    [MarkoutBoolFormat("Yes", "No")]
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

    [MarkoutIgnoreInTable]
    public List<MarkoutField> Summary => GetCompactFields();

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

    [MarkoutSection(Name = "Source Link Audit")]
    public List<MarkoutField> SourceLinkAuditSection => GetSourceLinkAuditFields();

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
        }).OrderBy(e => e.ExtendedType).ThenBy(e => e.Name).ToList();

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
    public bool HasCustomAttributes => _data.CustomAttributes is { Count: > 0 };

    [MarkoutSection(Name = "Custom Attributes", ShowWhenProperty = nameof(HasCustomAttributes))]
    public List<CustomAttributeRow>? CustomAttributesSection =>
        _data.CustomAttributes?.Select(a => new CustomAttributeRow(a.Name, a.Target, a.Value ?? "")).ToList();

    [MarkoutIgnore]
    public bool HasTypeForwarders => _data.TypeForwarders is { Count: > 0 };

    [MarkoutSection(Name = "Type Forwarders", ShowWhenProperty = nameof(HasTypeForwarders))]
    public List<TypeForwarderRow>? TypeForwardersSection =>
        _data.TypeForwarders?.Select(f => new TypeForwarderRow(f.TypeName, f.TargetAssembly)).ToList();

    [MarkoutIgnore]
    public bool HasNonNormalizedPaths => _data.NonNormalizedPaths is { Count: > 0 };

    [MarkoutSection(Name = "Non-normalized Paths", ShowWhenProperty = nameof(HasNonNormalizedPaths))]
    public List<string>? NonNormalizedPathsSection => _data.NonNormalizedPaths;

    /// <summary>
    /// Resolves the display version using priority: PlatformVersion, InformationalVersion (prefix), AssemblyVersion, FileVersion.
    /// </summary>
    private string ResolveVersion()
    {
        if (!string.IsNullOrEmpty(_data.PlatformVersion))
            return _data.PlatformVersion;

        if (_data.AssemblyInfo is { } info)
        {
            if (!string.IsNullOrEmpty(info.InformationalVersion))
            {
                var ver = info.InformationalVersion;
                // Strip +commit suffix (e.g., "13.0.4+4e13299..." → "13.0.4")
                var plusIndex = ver.IndexOf('+');
                if (plusIndex > 0)
                    ver = ver[..plusIndex];
                // Strip -prerelease suffix for version part count check
                var dashIndex = ver.IndexOf('-');
                var versionPart = dashIndex > 0 ? ver[..dashIndex] : ver;
                if (versionPart.Split('.').All(p => int.TryParse(p, out _)))
                    return dashIndex > 0 ? ver : versionPart;
            }

            if (!string.IsNullOrEmpty(info.AssemblyVersion))
                return info.AssemblyVersion;

            if (!string.IsNullOrEmpty(info.FileVersion))
                return info.FileVersion;
        }

        return "";
    }

    private List<MarkoutField> GetCompactFields()
    {
        List<MarkoutField> fields = [];
        if (_data.AssemblyInfo is not { } info) return fields;

        if (!string.IsNullOrEmpty(info.AssemblyName))
            fields.Add(new("Name", info.AssemblyName));
        fields.Add(new("Version", ResolveVersion()));
        if (!string.IsNullOrEmpty(info.TargetFramework))
            fields.Add(new("TFM", info.TargetFramework));
        if (!string.IsNullOrEmpty(info.Architecture))
            fields.Add(new("Arch", info.Architecture));
        if (_data.FileSize > 0)
            fields.Add(new("Size", FormatFileSize(_data.FileSize)));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", _data.Source));
        if (_data.LastModified.HasValue)
            fields.Add(new("Modified", _data.LastModified.Value.ToString("yyyy-MM-dd")));

        return fields;
    }

    private List<MarkoutField> GetAssemblyInfoFields()
    {
        List<MarkoutField> fields = [];
        if (_data.AssemblyInfo is not { } info) return fields;

        if (!string.IsNullOrEmpty(info.AssemblyName))
            fields.Add(new("Name", info.AssemblyName));
        fields.Add(new("Version", ResolveVersion()));
        if (!string.IsNullOrEmpty(info.InformationalVersion))
            fields.Add(new("Informational Version", info.InformationalVersion));
        if (!string.IsNullOrEmpty(info.AssemblyVersion))
            fields.Add(new("Assembly Version", info.AssemblyVersion));
        if (!string.IsNullOrEmpty(info.TargetFramework))
            fields.Add(new("Target Framework", info.TargetFramework));
        if (!string.IsNullOrEmpty(info.Architecture))
            fields.Add(new("Architecture", info.Architecture));
        if (!string.IsNullOrEmpty(info.CompilationType))
            fields.Add(new("Compilation", info.CompilationType));
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
        fields.Add(new("Deterministic", _data.IsDeterministic ? "Yes" : "No"));
        fields.Add(new("Reproducible", _data.HasReproducibleFlag ? "Yes" : "No"));
        if (_data.FileSize > 0)
            fields.Add(new("File Size", FormatFileSize(_data.FileSize)));
        if (info.TypeDefinitionCount > 0)
            fields.Add(new("Types", info.TypeDefinitionCount.ToString("N0")));
        if (info.MethodDefinitionCount > 0)
            fields.Add(new("Methods", info.MethodDefinitionCount.ToString("N0")));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", _data.Source));
        if (_data.LastModified.HasValue)
            fields.Add(new("Modified", _data.LastModified.Value.ToString("yyyy-MM-dd")));

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

    private List<MarkoutField> GetSourceLinkAuditFields()
    {
        List<MarkoutField> fields = [];
        if (_data.TotalSourceFiles <= 0) return fields;

        int accessible = _data.AccessibleSourceFiles + _data.EmbeddedSourceFiles;
        string status = _data.AllSourcesAccessible == true ? "Yes" : "No";
        fields.Add(new("Status", $"{status} {accessible}/{_data.TotalSourceFiles} files accessible"));

        if (_data.EmbeddedSourceFiles > 0)
            fields.Add(new("Embedded", $"{_data.EmbeddedSourceFiles} files"));

        if (_data.MissingSourceFiles is { Count: > 0 })
        {
            foreach (var file in _data.MissingSourceFiles.Take(10))
                fields.Add(new("Missing", $"`{file}`"));
        }

        return fields;
    }

    private static string FormatSize(int bytes) => bytes switch
    {
        0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static List<TreeNode> BuildFlatTransitiveTree(List<AssemblyReferenceNode> nodes)
    {
        List<TreeNode> result = [];
        foreach (var node in nodes)
        {
            var badge = node.ResolvedFrom switch
            {
                "local" => "local",
                "platform" => "platform",
                _ => "?"
            };
            var suffix = node.IsCyclic ? " (circular)" : "";
            result.Add(new TreeNode($"{node.Name} {node.Version}{suffix}", badge));
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

[MarkoutSerializable]
public record CustomAttributeRow(
    string Name,
    string Target,
    string Value);

[MarkoutSerializable]
public record TypeForwarderRow(
    [property: MarkoutPropertyName("Type")] string TypeName,
    [property: MarkoutPropertyName("Target Assembly")] string TargetAssembly);
