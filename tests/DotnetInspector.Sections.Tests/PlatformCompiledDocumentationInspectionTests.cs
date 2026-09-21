using System.Text.Json;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.Metadata;

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
}
