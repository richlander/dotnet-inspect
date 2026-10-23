using DotnetInspector.Packages;
using DotnetInspector.Ecosystems;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Findings;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

internal sealed record SearchAssemblySource(
    string Library,
    string Source,
    string? SourceVersion,
    string DiagnosticSubject,
    FindingSubject FindingSubject,
    ExactLibrarySourceCoordinate? ExactSource = null)
{
    internal static SearchAssemblySource FromAssemblySet(
        AssemblySetEntry assembly,
        AssemblyReferenceIdentity? identity = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string fullPath = Path.GetFullPath(assembly.Path);
        return new(
            Path.GetFileNameWithoutExtension(assembly.Path),
            assembly.Source,
            assembly.Version,
            assembly.Path,
            new FindingSubject(
                fullPath,
                Path.GetFileName(assembly.Path)),
            assembly.SourceKind is AssemblySetSourceKind.Package
                && assembly.Version is not null
                && identity is not null
                    ? new ExactLibrarySourceCoordinate.Package(
                        PackageSourceCoordinate.Create(
                            assembly.Source,
                            assembly.Version),
                        new ManagedMetadataIdentity.Assembly(identity))
                    : null);
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
            asset.AssemblySimpleName,
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

    internal static SearchAssemblySource FromIndexed(
        EcosystemFindIndexSourceCoordinate source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string packageId = source.Package.PackageId;
        string version = source.Package.Version!;
        string library = source.Library;
        string display = $"{packageId}@{version}/{library}";
        return new(
            library,
            packageId,
            version,
            display,
            new FindingSubject($"indexed:{display}", display));
    }
}
