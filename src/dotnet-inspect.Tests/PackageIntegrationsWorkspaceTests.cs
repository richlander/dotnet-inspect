using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class PackageIntegrationsWorkspaceTests
{
    [Fact]
    public async Task Create_PartitionsTfmsAndRetainsParticipantGeneration()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-integrations-workspace-").FullName;
        try
        {
            string net8 = Path.Combine(directory, "lib", "net8.0");
            string net9 = Path.Combine(directory, "lib", "net9.0");
            Directory.CreateDirectory(net8);
            Directory.CreateDirectory(net9);
            string first = Path.Combine(net8, "First.dll");
            string second = Path.Combine(net8, "Second.dll");
            string third = Path.Combine(net9, "Third.dll");
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                first);
            File.Copy(typeof(PdbContext).Assembly.Location, second);
            File.Copy(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.Location,
                third);

            using var workspace = PackageIntegrationsWorkspace.Create(
                [
                    new(first, "net8.0"),
                    new(second, "net8.0"),
                    new(third, "net9.0"),
                ],
                "Test.Package",
                "1.0.0");

            Assert.Equal(2, workspace.ContextGroupCount);
            File.Copy(second, first, overwrite: true);

            var observed = await workspace.UseAssemblyAsync(
                first,
                (retained, integrations) =>
                {
                    Assert.NotNull(retained);
                    AssemblyIntegrationsEntry.Available available =
                        Assert.IsType<
                            AssemblyIntegrationsEntry.Available>(
                            integrations);
                    Assert.Same(
                        available.Subject.Registration,
                        retained.Registration);
                    var provenance =
                        Assert.IsType<
                            AssemblyResolutionProvenance.PackageAsset>(
                            available.Subject.Provenance);
                    Assert.Equal("net8.0", provenance.Tfm);

                    using PdbContext context =
                        PdbContext.Open(retained);
                    return Task.FromResult(
                        context.ExtractAssemblyInfo().AssemblyName);
                });

            Assert.Equal(
                typeof(PackageIntegrationsWorkspaceTests)
                    .Assembly.GetName().Name,
                observed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAssemblyIntegrationsResult_PreventsLegacyRescan()
    {
        string path =
            typeof(PackageIntegrationsWorkspaceTests).Assembly.Location;
        using var workspace = PackageIntegrationsWorkspace.Create(
            [new(path, "net11.0")],
            "Test.Package",
            "1.0.0");
        AssemblyIntegrationsEntry entry =
            await workspace.UseAssemblyAsync(
                path,
                static (_, integrations) =>
                    Task.FromResult(integrations!));
        var model = new LibraryInspection();
        var logger = new VerboseLogger(enabled: false);

        LibraryMetadataService.ApplyAssemblyIntegrationsResult(
            path,
            model,
            logger,
            entry);
        FindingInspection<EcosystemIntegrationSignalInfo>? ecosystem =
            model.EcosystemIntegrationInspection;
        FindingInspection<OpenTelemetrySignalInfo>? openTelemetry =
            model.OpenTelemetryInspection;

        using var session = AssemblyInspectionSession.Open(path);
        LibraryMetadataService.ScanIntegrations(
            session,
            path,
            model,
            logger);

        Assert.Same(ecosystem, model.EcosystemIntegrationInspection);
        Assert.Same(openTelemetry, model.OpenTelemetryInspection);
    }
}
