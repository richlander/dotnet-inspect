using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using Inspector.Findings;

namespace DotnetInspect.Cli.Inspectors;

internal sealed record SearchAssemblySource(
    string Library,
    string Source,
    string? SourceVersion,
    string DiagnosticSubject,
    FindingSubject FindingSubject)
{
    internal static SearchAssemblySource FromAssemblySet(
        AssemblySetEntry assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string fullPath = Path.GetFullPath(assembly.Path);
        return new(
            Path.GetFileNameWithoutExtension(assembly.Path),
            assembly.Source,
            assembly.Version,
            assembly.Path,
            new FindingSubject(fullPath, Path.GetFileName(assembly.Path)));
    }

    internal static SearchAssemblySource FromPackage(
        PackageRootIdentity package,
        PackageCompileAsset asset)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(asset);
        string root = $"{package.PackageId}@{package.PackageVersion}";
        string display = $"{root}/{asset.Path}";
        return new(
            Path.GetFileNameWithoutExtension(asset.AssemblyName),
            package.PackageId,
            package.PackageVersion,
            display,
            new FindingSubject($"{root}/{asset.Id}", display));
    }
}
