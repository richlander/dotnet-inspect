using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

internal static class WorkspaceRegistrationTestData
{
    internal static ExactLibrarySourceCoordinate RealPackageSystemTextJson()
    {
        using var stream = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "BindingComposition",
            "package", "System.Text.Json.dll"));
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(
                "System.Text.Json", "11.0.0-preview.7.26381.103"),
            new(AssemblyReferenceIdentity.FromAssemblyDefinition(reader)));
    }
}
