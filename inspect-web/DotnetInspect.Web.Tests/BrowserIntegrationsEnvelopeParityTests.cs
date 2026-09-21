using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Browser package workspace",
    DisableParallelization = true)]
public sealed class BrowserPackageWorkspaceCollection;

[Collection("Browser package workspace")]
[SupportedOSPlatform("browser")]
public sealed class BrowserIntegrationsEnvelopeParityTests
{
    const string PackageId = "Serilog.Sinks.OpenTelemetry";
    const string Version = "4.2.0";
    const string Framework = "net8.0";
    const string AssemblyFileName = "Serilog.Sinks.OpenTelemetry.dll";

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RealPackagePreservesSharedEnvelopesAcrossBrowserExports()
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                PackageId,
                Version,
                Framework,
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        PackageCompileAsset asset =
            coordinate.CompileAsset(AssemblyFileName);
        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, asset.Id);

        InspectionEnvelope<AssemblyIntegrationsEntry> integrationsTerminal =
            scope.UseMetadataParticipant(
                participant,
                AssemblyIntegrationsInspection.Execute);
        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult>
            opportunitiesTerminal =
                scope.UseMetadataParticipant(
                    participant,
                    AssemblyIntegrationOpportunitiesInspection.Execute);

        BrowserPackageIntegrations integrations =
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageIntegrations(
                    PackageId,
                    Version,
                    Framework,
                    asset.Id),
                BrowserAnalysisJsonContext.Default
                    .BrowserPackageIntegrations)
            ?? throw new InvalidOperationException(
                "The Browser package Integrations export returned null.");
        BrowserPackageOpportunities opportunities =
            JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageOpportunities(
                    PackageId,
                    Version,
                    Framework,
                    asset.Id),
                BrowserAnalysisJsonContext.Default
                    .BrowserPackageOpportunities)
            ?? throw new InvalidOperationException(
                "The Browser package Opportunities export returned null.");

        AssertEnvelope(
            integrationsTerminal,
            Assert.IsType<BrowserAnalysisInspectionEnvelope>(
                integrations.Inspection),
            JsonSerializer.SerializeToElement(
                integrationsTerminal.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationsEntry));
        AssertEnvelope(
            opportunitiesTerminal,
            Assert.IsType<BrowserAnalysisInspectionEnvelope>(
                opportunities.Inspection),
            JsonSerializer.SerializeToElement(
                opportunitiesTerminal.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationOpportunitiesInspectionResult));
    }

    static void AssertEnvelope<TContent>(
        InspectionEnvelope<TContent> expected,
        BrowserAnalysisInspectionEnvelope actual,
        JsonElement expectedContent)
    {
        Assert.True(
            JsonElement.DeepEquals(expectedContent, actual.Content),
            "Browser Content differed from the shared service terminal.");
        AssertShare(expected.PortableProjection, actual.PortableProjection);
        Assert.Equal(expected.Diagnostics.Length, actual.Diagnostics.Length);
        for (int index = 0; index < expected.Diagnostics.Length; index++)
        {
            InspectionDiagnostic expectedDiagnostic =
                expected.Diagnostics[index];
            BrowserAnalysisInspectionDiagnostic actualDiagnostic =
                actual.Diagnostics[index];
            Assert.Equal(
                expectedDiagnostic.Code,
                actualDiagnostic.Code);
            Assert.Equal(
                expectedDiagnostic.Severity.ToString(),
                actualDiagnostic.Severity.ToString());
            Assert.Equal(
                expectedDiagnostic.Summary.ToString(),
                actualDiagnostic.Summary.ToString());
            Assert.Equal(
                expectedDiagnostic.Correspondence?.ToString(),
                actualDiagnostic.Correspondence?.ToString());
        }
    }

    static void AssertShare(
        InspectionPortableProjection expected,
        BrowserAnalysisInspectionPortableProjection actual)
    {
        switch (expected)
        {
            case InspectionPortableProjection.Available available:
                Assert.Equal("available", actual.Kind);
                Assert.Equal(available.FullUrl, actual.FullUrl);
                Assert.Equal(available.Packet, actual.Packet);
                Assert.Null(actual.Path);
                Assert.Null(actual.Reason);
                break;
            case InspectionPortableProjection.NonProjectable nonProjectable:
                Assert.Equal("nonProjectable", actual.Kind);
                Assert.Null(actual.FullUrl);
                Assert.Null(actual.Packet);
                Assert.Equal(nonProjectable.Path, actual.Path);
                Assert.Equal(
                    nonProjectable.Reason.ToString(),
                    actual.Reason?.ToString());
                Assert.Equal(
                    nonProjectable.Explanation,
                    actual.Explanation);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown inspection portable projection.");
        }
    }
}
