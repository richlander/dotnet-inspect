using DotnetInspector.Models;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(FileName), TitleContextProperty = nameof(Tfm), AutoFieldsCount = 7, FieldLayout = FieldLayout.Inline)]
public class LibraryInspectionView
{
    private readonly LibraryInspection _data;
    private readonly bool _topFieldsOnly;

    public LibraryInspectionView(LibraryInspection data, bool topFieldsOnly = false)
    {
        _data = data;
        _topFieldsOnly = topFieldsOnly;
    }

    [MarkoutIgnore]
    public string? Tfm => _data.Tfm;

    [MarkoutPropertyName("File")]
    public string FileName => _data.FileName;

    // ===== Top fields (first 7 auto-fields, rendered inline for -v:q compact summary) =====

    [MarkoutSkipNull]
    public string? Name => _topFieldsOnly ? _data.AssemblyInfo?.AssemblyName : null;

    [MarkoutSkipNull]
    public string? Version => _topFieldsOnly ? ResolveVersion() : null;

    [MarkoutPropertyName("TFM")]
    [MarkoutSkipNull]
    public string? TargetFramework => _topFieldsOnly ? _data.AssemblyInfo?.TargetFramework : null;

    [MarkoutPropertyName("Arch")]
    [MarkoutSkipNull]
    public string? Architecture => _topFieldsOnly ? _data.AssemblyInfo?.Architecture : null;

    [MarkoutPropertyName("Size")]
    [MarkoutSkipNull]
    public string? FileSize => _topFieldsOnly ? (_data.FileSize > 0 ? FormatFileSize(_data.FileSize) : null) : null;

    [MarkoutSkipNull]
    public string? Source => _topFieldsOnly ? _data.Source : null;

    [MarkoutSkipNull]
    public string? Modified => _topFieldsOnly ? _data.LastModified?.ToString("yyyy-MM-dd") : null;

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

    [MarkoutIgnore]
    public bool HasAsyncMethods => _data.AsyncMethods is { Count: > 0 };

    [MarkoutSection(Name = "Async Methods", ShowWhenProperty = nameof(HasAsyncMethods))]
    public List<AsyncMethodRow>? AsyncMethodsSection =>
        _data.AsyncMethods?
            .OrderBy(m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(m => new AsyncMethodRow(m.MethodName, m.DeclaringType, m.Kind, m.Signature))
            .ToList();

    [MarkoutIgnore]
    public bool HasCustomAttributes => _data.CustomAttributes is { Count: > 0 };

    [MarkoutSection(Name = "Custom Attributes", ShowWhenProperty = nameof(HasCustomAttributes))]
    public List<CustomAttributeRow>? CustomAttributesSection =>
        _data.CustomAttributes?
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Value, StringComparer.OrdinalIgnoreCase)
            .Select(a => new CustomAttributeRow(a.Name, a.Target, a.Value ?? ""))
            .ToList();

    [MarkoutIgnore]
    public bool UseDependenciesView => _data.UseDependenciesView;

    [MarkoutSection(Name = "Dependencies")]
    public List<TreeNode>? DependenciesSection =>
        !_data.UseDependenciesView || _data.AssemblyInfo?.TransitiveReferences is not { Count: > 0 } ? null :
        BuildNestedDependencyTree(_data.AssemblyInfo.TransitiveReferences);

    [MarkoutIgnore]
    public bool HasExtensionMethods => _data.ExtensionMethods is { Count: > 0 };

    [MarkoutSection(Name = "Extension Methods", ShowWhenProperty = nameof(HasExtensionMethods))]
    public List<ExtensionMethodRow>? ExtensionMethodsSection =>
        _data.ExtensionMethods?
            .OrderBy(e => e.ExtendedType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ExtensionClass, StringComparer.OrdinalIgnoreCase)
            .Select(e =>
            {
                var name = e.Overloads > 1 ? $"{e.MethodName} ({e.Overloads} overloads)" : e.MethodName;
                return new ExtensionMethodRow(name, e.Kind, e.ExtendedType, e.ExtensionClass);
            })
            .ToList();

    [MarkoutSection(Name = "Library Info")]
    public LibraryInfoSection? AssemblyInfoSection => _data.AssemblyInfo is not { } info ? null : new LibraryInfoSection
    {
        Name = info.AssemblyName,
        Version = ResolveVersion(),
        InformationalVersion = info.InformationalVersion,
        AssemblyVersion = info.AssemblyVersion,
        TargetFramework = info.TargetFramework,
        Architecture = info.Architecture,
        Compilation = info.CompilationType,
        Product = info.Product,
        Company = info.Company,
        Copyright = info.Copyright,
        Signed = info.IsSigned ? "Yes" : null,
        PublicKeyToken = info.PublicKeyToken,
        Deterministic = _data.IsDeterministic,
        Reproducible = _data.HasReproducibleFlag,
        FileSize = _data.FileSize > 0 ? FormatFileSize(_data.FileSize) : null,
        Types = info.TypeDefinitionCount > 0 ? info.TypeDefinitionCount.ToString("N0") : null,
        Methods = info.MethodDefinitionCount > 0 ? info.MethodDefinitionCount.ToString("N0") : null,
        AsyncKind = ResolveAsyncKind(),
        Source = _data.Source,
        Modified = _data.LastModified?.ToString("yyyy-MM-dd"),
    };

    [MarkoutSection(Name = "References")]
    public List<ReferenceRow>? AssemblyReferencesSection =>
        _data.AssemblyInfo?.TransitiveReferences is { Count: > 0 } ? null :
        _data.AssemblyInfo?.References?.OrderBy(r => r.Name)
            .Select(r => new ReferenceRow(r.Name, r.Version, r.PublicKeyToken ?? "-"))
            .ToList() is { Count: > 0 } list ? list : null;

    [MarkoutIgnore]
    public bool HasNonNormalizedPaths => _data.NonNormalizedPaths is { Count: > 0 };

    [MarkoutSection(Name = "Non-normalized Paths", ShowWhenProperty = nameof(HasNonNormalizedPaths))]
    public List<string>? NonNormalizedPathsSection =>
        _data.NonNormalizedPaths?.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    [MarkoutIgnore]
    public bool HasPInvokeMethods => _data.PInvokeMethods is { Count: > 0 };

    [MarkoutSection(Name = "P/Invoke Methods", ShowWhenProperty = nameof(HasPInvokeMethods))]
    public List<PInvokeMethodRow>? PInvokeMethodsSection =>
        _data.PInvokeMethods?
            .OrderBy(m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(m => new PInvokeMethodRow(m.MethodName, m.DeclaringType, m.ModuleName ?? "", m.Signature))
            .ToList();

    [MarkoutIgnore]
    public bool HasResources => _data.Resources is { Count: > 0 };

    [MarkoutSection(Name = "Resources", ShowWhenProperty = nameof(HasResources))]
    public List<ResourceRow>? ResourcesSection =>
        _data.Resources?
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new ResourceRow(r.Name, r.Visibility, FormatSize(r.Size)))
            .ToList();

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = "Signals", ShowWhenProperty = nameof(HasAuditSignals))]
    public List<AuditSignalRow>? SignalsSection =>
        _data.AuditSignals?.Select(s => new AuditSignalRow(s.Area, s.Signal, s.Value, s.Evidence)).ToList();

    [MarkoutIgnore]
    public bool HasSourceLinkAudit => _data.AllSourcesAccessible.HasValue || _data.TotalSourceFiles > 0;

    [MarkoutSection(Name = "SourceLink Availability", ShowWhenProperty = nameof(HasSourceLinkAudit))]
    public SourceLinkAuditSection? SourceLinkAuditSection => !HasSourceLinkAudit ? null : new SourceLinkAuditSection
    {
        Status = _data.AllSourcesAccessible == true ? "Complete" : "Partial",
        SourceFiles = $"{_data.AccessibleSourceFiles}/{_data.TotalSourceFiles} available",
        Embedded = _data.EmbeddedSourceFiles,
        Missing = _data.MissingSourceFiles?.Count ?? 0
    };

    [MarkoutIgnore]
    public bool HasSourceIntegrity => _data.SourceIntegrityChecked;

    [MarkoutSection(Name = "SourceLink Integrity", ShowWhenProperty = nameof(HasSourceIntegrity))]
    public SourceIntegritySection? SourceIntegritySection => !HasSourceIntegrity ? null : new SourceIntegritySection
    {
        Status = _data.SourceIntegrityMismatched > 0 ? "Mismatch"
            : _data.SourceIntegrityUnverifiable > 0 ? "Partial" : "Verified",
        Verified = _data.SourceIntegrityVerified,
        Mismatched = _data.SourceIntegrityMismatched,
        MismatchedFiles = _data.SourceIntegrityMismatches is { Count: > 0 } mismatches
            ? string.Join(", ", mismatches.Select(f => $"`{f}`"))
            : null,
        CrlfMismatch = _data.SourceIntegrityLineEndingNormalized > 0
            ? $"{_data.SourceIntegrityLineEndingNormalized} normalized"
            : null,
        Unverifiable = _data.SourceIntegrityUnverifiable
    };

    [MarkoutIgnore]
    public bool HasMissingSourceFiles => _data.MissingSourceFiles is { Count: > 0 };

    [MarkoutSection(Name = "SourceLink Missing Files", ShowWhenProperty = nameof(HasMissingSourceFiles))]
    public List<string>? MissingSourceFilesSection =>
        _data.MissingSourceFiles?
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(f => $"`{f}`")
            .ToList();

    [MarkoutSection(Name = "Symbols")]
    public SymbolsSection? SymbolsSection => new SymbolsSection
    {
        PdbFormat = _data.PdbFormat ?? "Unknown",
        PdbLocation = _data.PdbLocation ?? "Unknown",
        SymbolServer = _data.SymbolServer,
        PdbPath = _data.PdbPath,
        SourceLink = _data.HasSourceLink ? "Yes"
            : _data.SourceLinkUnavailableReason != null ? $"No ({_data.SourceLinkUnavailableReason})" : "No",
        Builder = _data.Builder,
        Publisher = !string.IsNullOrEmpty(_data.Publisher)
            ? $"{_data.Publisher}{(_data.PublisherVerified ? " (Verified)" : "")}"
            : null,
        Repository = _data.RepositoryVerified ? "nuget.org (Verified)" : null,
        Signature = _data.SignatureStatus,
        RepositoryUrl = _data.RepositoryUrl,
        Warning = _data.WindowsPdbDetected ? "Windows PDB format is not supported by this tool" : null,
        Recommendation = _data.WindowsPdbDetected ? "Consider asking the package maintainer to publish Portable PDBs" : null,
    };

    [MarkoutIgnore]
    public bool HasTypeForwarders => _data.TypeForwarders is { Count: > 0 };

    [MarkoutSection(Name = "Type Forwarders", ShowWhenProperty = nameof(HasTypeForwarders))]
    public List<TypeForwarderRow>? TypeForwardersSection =>
        _data.TypeForwarders?
            .OrderBy(f => f.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.TargetAssembly, StringComparer.OrdinalIgnoreCase)
            .Select(f => new TypeForwarderRow(f.TypeName, f.TargetAssembly))
            .ToList();

    [MarkoutIgnore]
    public bool HasUnsafeMethods => _data.UnsafeMethods is { Count: > 0 };

    [MarkoutSection(Name = "Unsafe Methods", ShowWhenProperty = nameof(HasUnsafeMethods))]
    public List<ClassifiedMethodRow>? UnsafeMethodsSection =>
        _data.UnsafeMethods?
            .OrderBy(m => m.DeclaringType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(m => new ClassifiedMethodRow(m.MethodName, m.DeclaringType, m.Signature))
            .ToList();

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
                var plusIndex = ver.IndexOf('+');
                if (plusIndex > 0)
                    ver = ver[..plusIndex];
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

    /// <summary>
    /// Classifies the assembly's public async surface: "Runtime" (impl-flag async only),
    /// "State machine" (compiler state-machine async only), "Mixed" (both), or "None" when the
    /// assembly exposes no public async methods. Scope matches the "Async Methods" section
    /// (public, non-accessor methods).
    /// </summary>
    private string ResolveAsyncKind() => (_data.HasRuntimeAsync, _data.HasStateMachineAsync) switch
    {
        (true, true) => "Mixed",
        (true, false) => AsyncMethodSummary.RuntimeKind,
        (false, true) => AsyncMethodSummary.StateMachineKind,
        _ => "None",
    };

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

            target.Add(children.Count > 0 ? new TreeNode(label) { Children = children } : new TreeNode(label));
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
public record AsyncMethodRow(
    string Name,
    [property: MarkoutPropertyName("Declaring Type")] string DeclaringType,
    string Kind,
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

[MarkoutSerializable]
public record AuditSignalRow(
    string Area,
    string Signal,
    string Value,
    string Evidence);

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class LibraryInfoSection
{
    public string? AssemblyVersion { get; init; }
    public string? AsyncKind { get; init; }
    public string? Architecture { get; init; }
    public string? Company { get; init; }
    public string? Compilation { get; init; }
    public string? Copyright { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Deterministic { get; init; }
    public string? FileSize { get; init; }
    public string? InformationalVersion { get; init; }
    public string? Methods { get; init; }
    public string? Modified { get; init; }
    public string? Name { get; init; }
    public string? Product { get; init; }
    public string? PublicKeyToken { get; init; }
    [MarkoutBoolFormat("Yes", "No")]
    public bool Reproducible { get; init; }
    public string? Signed { get; init; }
    public string? Source { get; init; }
    public string? TargetFramework { get; init; }
    public string? Types { get; init; }
    public string? Version { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SymbolsSection
{
    public string? Builder { get; init; }
    [MarkoutPropertyName("PDB Format")]
    public string PdbFormat { get; init; } = "Unknown";
    [MarkoutPropertyName("PDB Location")]
    public string PdbLocation { get; init; } = "Unknown";
    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath { get; init; }
    public string? Publisher { get; init; }
    public string? Recommendation { get; init; }
    public string? Repository { get; init; }
    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl { get; init; }
    public string? Signature { get; init; }
    public string? SourceLink { get; init; }
    public string? SymbolServer { get; init; }
    public string? Warning { get; init; }
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceLinkAuditSection
{
    public int Embedded { get; init; }
    public int Missing { get; init; }
    public string SourceFiles { get; init; } = "";
    public string Status { get; init; } = "";
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class SourceIntegritySection
{
    public int Mismatched { get; init; }
    [MarkoutPropertyName("Mismatched Files")]
    public string? MismatchedFiles { get; init; }
    [MarkoutPropertyName("CR/LF Mismatch")]
    public string? CrlfMismatch { get; init; }
    public string Status { get; init; } = "";
    public int Unverifiable { get; init; }
    public int Verified { get; init; }
}
