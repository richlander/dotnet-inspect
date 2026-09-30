using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
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

    internal static SearchAssemblySource FromPlatformPopulation(
        PlatformPopulationMember member,
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(assembly);
        string source = member.Target.Family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => throw new InvalidOperationException(
                $"Unsupported Platform family '{member.Target.Family}'."),
        };
        string version = member.Target.Version.Value;
        string display =
            $"{source}@{version}/{assembly.Identity.Name}";
        return new(
            assembly.Identity.Name,
            source,
            version,
            display,
            new FindingSubject($"platform:{display}", display));
    }
}
