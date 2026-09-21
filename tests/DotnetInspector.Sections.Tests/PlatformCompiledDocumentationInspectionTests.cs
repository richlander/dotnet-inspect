using System.Text.Json;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Installed;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using DotnetInspector.Queries;
using DotnetInspector.Sections.Installed;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class PlatformCompiledDocumentationInspectionTests
{
    [Fact]
    public void EnvelopeJsonPreservesOrderedSelectionAndAuthoritativeAbsence()
    {
        const string documentationId =
            "M:System.Collections.Generic.List`1.Add(`0)";
        var assembly = new CompiledDocumentationAssemblyIdentity(
            "System.Collections",
            "11.0.0.0",
            null,
            "b03f5f7f11d50a3a");
        var subject = new CompiledDocumentationSubject(
            assembly,
            documentationId);
        var absent = new CompiledDocumentationOutcome.Absent(
            subject,
            [
                new(
                    new(
                        CompiledDocumentationSourceKind.Platform,
                        "Microsoft.NETCore.App.Ref",
                        0),
                    CompiledDocumentationSourceEvidenceKind.Absent),
            ],
            SourcesTruncated: false);
        var envelope = new InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>(
                new PlatformCompiledDocumentationInspectionOutcome.Completed(
                    new(
                        new(
                            PlatformFamily.DotNetRuntime,
                            "net11.0",
                            "11.0.7147",
                            assembly,
                            [documentationId],
                            PlatformCompiledDocumentationSubjectSelection
                                .RequireAll),
                        [absent])),
                new InspectionShare.NonProjectable(
                    "platform-compiled-documentation/share",
                    "No share."));

        string json = JsonSerializer.Serialize(
            envelope,
            PlatformCompiledDocumentationInspectionJsonContext.Default
                .PlatformCompiledDocumentationInspectionEnvelope);
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>? roundTrip =
                JsonSerializer.Deserialize(
                    json,
                    PlatformCompiledDocumentationInspectionJsonContext.Default
                        .PlatformCompiledDocumentationInspectionEnvelope);

        Assert.NotNull(roundTrip);
        var completed = Assert.IsType<
            PlatformCompiledDocumentationInspectionOutcome.Completed>(
                roundTrip.Content);
        Assert.Equal(
            [documentationId],
            completed.Document.Selection.DocumentationIds);
        var restored = Assert.IsType<
            CompiledDocumentationOutcome.Absent>(
                Assert.Single(completed.Document.Outcomes));
        Assert.Equal(documentationId, restored.Subject.DocumentationId);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            Assert.Single(restored.Sources).Kind);
    }

    [Fact]
    public void RequestRejectsDuplicateDocumentationSubjects()
    {
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.7147"));
        var assembly = new AssemblyReferenceIdentity(
            "System.Collections",
            new Version(11, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");

        Assert.Throws<ArgumentException>(
            () => new PlatformCompiledDocumentationInspectionRequest(
                target,
                assembly,
                ["T:System.String", "T:System.String"],
                PlatformCompiledDocumentationSubjectSelection.RequireAll));
    }

    [Fact]
    public void EnvelopeJsonPreservesInstalledSourceFailureIdentity()
    {
        var selection = new PlatformCompiledDocumentationSelection(
            PlatformFamily.DotNetRuntime,
            "net11.0",
            "11.0.7147",
            new(
                "System.Collections",
                "11.0.0.0",
                null,
                "b03f5f7f11d50a3a"),
            ["T:System.String"],
            PlatformCompiledDocumentationSubjectSelection.RequireAll);
        var envelope = new InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>(
                new PlatformCompiledDocumentationInspectionOutcome
                    .NotAvailable(
                        new(
                            selection,
                            PlatformCompiledDocumentationFailureStage
                                .SourceRealization,
                            new(
                                TextPolicy.Field,
                                "The installed reference layout is invalid."),
                            new(
                                PlatformCompiledDocumentationSource.Installed,
                                "InvalidLayout"),
                            Settlement: null)),
                new InspectionShare.NonProjectable(
                    "platform-compiled-documentation/share",
                    "No share."));

        string json = JsonSerializer.Serialize(
            envelope,
            PlatformCompiledDocumentationInspectionJsonContext.Default
                .PlatformCompiledDocumentationInspectionEnvelope);
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>? roundTrip =
                JsonSerializer.Deserialize(
                    json,
                    PlatformCompiledDocumentationInspectionJsonContext.Default
                        .PlatformCompiledDocumentationInspectionEnvelope);

        Assert.NotNull(roundTrip);
        var unavailable = Assert.IsType<
            PlatformCompiledDocumentationInspectionOutcome.NotAvailable>(
                roundTrip.Content);
        PlatformCompiledDocumentationSourceDiagnostic diagnostic =
            Assert.IsType<
                PlatformCompiledDocumentationSourceDiagnostic>(
                    unavailable.Failure.SourceDiagnostic);
        Assert.Equal(
            PlatformCompiledDocumentationSource.Installed,
            diagnostic.Source);
        Assert.Equal("InvalidLayout", diagnostic.Code);
    }

    [Fact]
    public async Task InstalledExecutionPreservesSourceFailureIdentity()
    {
        string missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-hive-{Guid.NewGuid():N}");
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var request = new PlatformCompiledDocumentationInspectionRequest(
            target,
            new(
                "System.Runtime",
                new Version(11, 0, 0, 0),
                null,
                "b03f5f7f11d50a3a"),
            ["T:System.String"],
            PlatformCompiledDocumentationSubjectSelection.RequireAll);
        InstalledDotnetHiveIdentity hive =
            InstalledDotnetHiveIdentity.Create(
                "sections-installed-documentation-test");
        var adapter = new InstalledPlatformHouseAdapter(
            new InstalledReferencePackSource(hive, missingRoot),
            new InstalledImplementationPlatformSource(hive, missingRoot),
            "sections-installed-documentation-test");
        var work = new PlatformHouseWorkBudget(
            maxSourceOperations: 1,
            maxTargetCandidates: 0,
            maxAssemblies: 1,
            maxXmlDocuments: 1,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 520L * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromSeconds(30));

        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome> envelope =
                await InstalledPlatformCompiledDocumentationInspection
                    .ExecuteAsync(
                        request,
                        adapter,
                        work,
                        cancellationToken:
                            TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<
            PlatformCompiledDocumentationInspectionOutcome.NotAvailable>(
                envelope.Content);
        Assert.Equal(
            PlatformCompiledDocumentationFailureStage.SourceRealization,
            unavailable.Failure.Stage);
        PlatformCompiledDocumentationSourceDiagnostic diagnostic =
            Assert.IsType<
                PlatformCompiledDocumentationSourceDiagnostic>(
                    unavailable.Failure.SourceDiagnostic);
        Assert.Equal(
            PlatformCompiledDocumentationSource.Installed,
            diagnostic.Source);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidLayout.ToString(),
            diagnostic.Code);
        Assert.Single(envelope.Diagnostics);
    }
}
