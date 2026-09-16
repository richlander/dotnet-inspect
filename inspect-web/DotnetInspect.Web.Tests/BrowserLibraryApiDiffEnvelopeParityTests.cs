using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Tests;

[Collection("Library API diff operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryApiDiffEnvelopeParityTests
{
    const string PackageId = "System.Text.Json";
    const string TargetVersion = "9.0.0";
    const string CurrentVersion = "10.0.0";
    const string Framework = "net8.0";
    const string AssemblyFileName = "System.Text.Json.dll";

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RealPackageDiffPreservesSharedEnvelopeAcrossBrowserExport()
    {
        await using RealAssetFixture fixture = await RealAssetFixture.Open();
        InspectionEnvelope<LibraryApiDiffOutcome> terminal =
            fixture.ExecuteShared();
        Assert.IsType<LibraryApiDiffOutcome.Available>(terminal.Content);

        JsonElement expectedContent = JsonSerializer.SerializeToElement(
            terminal.Content,
            LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);
        Assert.Contains(
            "AllowDuplicateProperties",
            expectedContent.GetRawText(),
            StringComparison.Ordinal);

        string wire = await MetadataExports.QueryLibraryApiDiff(
            Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(
                fixture.Request,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest));
        BrowserLibraryApiDiffResult result =
            JsonSerializer.Deserialize(
                wire,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)
            ?? throw new InvalidOperationException(
                "The Browser Library API diff export returned null.");

        Assert.NotEqual(BrowserLibraryApiDiffResultKind.Failed, result.Kind);
        Assert.True(
            result.Kind == BrowserLibraryApiDiffResultKind.Succeeded,
            $"Browser result was {result.Kind}: {result.Error}\n"
                + $"{result.Diagnostic}\n{wire}");
        InspectionEnvelope<JsonElement> delivered =
            Assert.IsType<InspectionEnvelope<JsonElement>>(result.Inspection);

        Assert.True(
            JsonElement.DeepEquals(expectedContent, delivered.Content),
            "Browser Content differed from the shared service terminal.");
        Assert.Equal(terminal.Share, delivered.Share);
        Assert.Equal(terminal.Diagnostics.Length, delivered.Diagnostics.Length);
        for (int index = 0; index < terminal.Diagnostics.Length; index++)
        {
            InspectionDiagnostic expected = terminal.Diagnostics[index];
            InspectionDiagnostic actual = delivered.Diagnostics[index];
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.Severity, actual.Severity);
            Assert.Equal(expected.Summary.ToString(), actual.Summary.ToString());
            Assert.Equal(
                expected.Correspondence?.ToString(),
                actual.Correspondence?.ToString());
        }
    }

    sealed class RealAssetFixture(
        BrowserInspectionScope targetScope,
        BrowserInspectionScope currentScope,
        BrowserWorkspaceParticipant targetParticipant,
        BrowserWorkspaceParticipant currentParticipant,
        string compileAssetId) : IAsyncDisposable
    {
        internal BrowserLibraryApiDiffRequest Request { get; } =
            new(
                1,
                PackageId,
                CurrentVersion,
                TargetVersion,
                Framework,
                compileAssetId);

        internal static async Task<RealAssetFixture> Open()
        {
            BrowserInspectionScope? targetScope = null;
            BrowserInspectionScope? currentScope = null;
            try
            {
                await using (BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        PackageId,
                        TargetVersion,
                        Framework,
                        TestContext.Current.CancellationToken))
                {
                    targetScope = lease.Scope;
                }
                await using (BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        PackageId,
                        CurrentVersion,
                        Framework,
                        TestContext.Current.CancellationToken))
                {
                    currentScope = lease.Scope;
                }

                BrowserPackageCoordinate targetCoordinate =
                    targetScope.Coordinates[0];
                BrowserPackageCoordinate currentCoordinate =
                    currentScope.Coordinates[0];
                PackageCompileAsset targetAsset =
                    targetCoordinate.CompileAsset(AssemblyFileName);
                PackageCompileAsset currentAsset =
                    currentCoordinate.CompileAsset(AssemblyFileName);
                Assert.Equal(targetAsset.Id, currentAsset.Id);

                return new(
                    targetScope,
                    currentScope,
                    targetScope.SurfaceParticipant(
                        targetCoordinate,
                        targetAsset),
                    currentScope.SurfaceParticipant(
                        currentCoordinate,
                        currentAsset),
                    targetAsset.Id);
            }
            catch
            {
                if (currentScope is not null)
                {
                    await BrowserPackageWorkspace.RemoveScopeAsync(
                        currentScope);
                }
                if (targetScope is not null)
                {
                    await BrowserPackageWorkspace.RemoveScopeAsync(
                        targetScope);
                }
                throw;
            }
        }

        internal InspectionEnvelope<LibraryApiDiffOutcome> ExecuteShared() =>
            targetScope.UseSurfaceParticipant(
                targetParticipant,
                (targetGroup, target) =>
                    currentScope.UseSurfaceParticipant(
                        currentParticipant,
                        (currentGroup, current) =>
                            LibraryApiDiffInspection.Execute(
                                targetGroup,
                                target,
                                currentGroup,
                                current,
                                ApiSurfaceScope.Public,
                                BrowserApiSurfacePolicy.Limits)));

        public async ValueTask DisposeAsync()
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(currentScope);
            if (!ReferenceEquals(targetScope, currentScope))
            {
                await BrowserPackageWorkspace.RemoveScopeAsync(targetScope);
            }
        }
    }
}
