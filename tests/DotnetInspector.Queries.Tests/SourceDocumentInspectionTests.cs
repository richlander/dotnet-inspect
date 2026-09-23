using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // PR-fast: exact Source line coordinates are independent of acquisition.
    [Fact]
    public async Task SourceDocumentLinesReconstructExactDecodedText()
    {
        TestAssembly assembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(FixtureCatalog.SourceDiffV1));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);
        InspectionEnvelope<AssemblyTypeSourceEntry> template =
            await TypeSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest("Counter"),
                host.Context,
                TestContext.Current.CancellationToken);

        const string mixed =
            "A\r\n😀B\rC\nD\u0085E\u2028F\u2029G";
        SourceDocument document =
            AvailableDocument(
                SourceDocumentInspection.Project(
                    WithTypeText(template, mixed)));

        Assert.Equal(mixed, Reconstruct(document));
        Assert.Collection(
            document.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "A",
                SourceDocumentLineTerminator.CarriageReturnLineFeed),
            line => AssertLine(
                line,
                2,
                3,
                "😀B",
                SourceDocumentLineTerminator.CarriageReturn),
            line => AssertLine(
                line,
                3,
                7,
                "C",
                SourceDocumentLineTerminator.LineFeed),
            line => AssertLine(
                line,
                4,
                9,
                "D",
                SourceDocumentLineTerminator.NextLine),
            line => AssertLine(
                line,
                5,
                11,
                "E",
                SourceDocumentLineTerminator.LineSeparator),
            line => AssertLine(
                line,
                6,
                13,
                "F",
                SourceDocumentLineTerminator.ParagraphSeparator),
            line => AssertLine(
                line,
                7,
                15,
                "G",
                SourceDocumentLineTerminator.None));

        SourceDocument empty =
            AvailableDocument(
                SourceDocumentInspection.Project(
                    WithTypeText(template, "")));
        Assert.Collection(
            empty.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "",
                SourceDocumentLineTerminator.None));
        Assert.Equal("", Reconstruct(empty));

        SourceDocument finalEmpty =
            AvailableDocument(
                SourceDocumentInspection.Project(
                    WithTypeText(template, "A\n")));
        Assert.Collection(
            finalEmpty.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "A",
                SourceDocumentLineTerminator.LineFeed),
            line => AssertLine(
                line,
                2,
                2,
                "",
                SourceDocumentLineTerminator.None));
        Assert.Equal("A\n", Reconstruct(finalEmpty));
    }

    // PR-fast: completed type/member Source envelopes keep provider evidence,
    // while unsuccessful envelopes retain their exact typed content.
    [Fact]
    public async Task
        SourceDocumentProjectionPreservesProviderAndFailureEvidence()
    {
        TestAssembly typeAssembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        InspectionEnvelope<AssemblyTypeSourceEntry> pdbType;
        using (var host = QueryHost.WithPdb(
            typeAssembly.PdbPath,
            SourcePairBytes(FixtureCatalog.SourceDiffV1)))
        {
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [typeAssembly.Participant]);
            pdbType = await TypeSourceInspection.ExecuteAsync(
                group,
                typeAssembly.Participant,
                typeAssembly.TypeRequest("Counter"),
                host.Context,
                TestContext.Current.CancellationToken);
        }

        var additionalDocuments =
            new List<PdbTypeSourceAdditionalDocument>
            {
                new("Additional.cs", "https://example.test/Additional.cs"),
            };
        var typeAvailable =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                pdbType.Content);
        var typePdb =
            Assert.IsType<AssemblyTypeSource.Pdb>(
                typeAvailable.Source);
        var partialInspection = typePdb.Inspection with
        {
            IsPartial = true,
            AdditionalDocuments = additionalDocuments,
        };
        var partialEntry = typeAvailable with
        {
            Source = new AssemblyTypeSource.Pdb(
                typePdb.Text,
                partialInspection,
                typePdb.Provenance),
        };
        var partialEnvelope =
            new InspectionEnvelope<AssemblyTypeSourceEntry>(
                partialEntry,
                pdbType.Share,
                pdbType.Diagnostics);
        InspectionEnvelope<SourceDocument> projectedType =
            Assert.IsType<
                    SourceDocumentProjection<AssemblyTypeSourceEntry>
                        .Available>(
                    SourceDocumentInspection.Project(partialEnvelope))
                .Inspection;
        SourceDocument typeDocument = projectedType.Content;

        Assert.Equal(SourceDocumentProvider.Pdb, typeDocument.Provider);
        Assert.Equal("csharp", typeDocument.Language);
        Assert.Equal(
            typeAvailable.Subject.Identity,
            typeDocument.Identity.Assembly);
        Assert.Same(
            typeAvailable.Subject.Provenance,
            typeDocument.Identity.Resolution);
        Assert.Equal(
            typeAvailable.Request,
            Assert.IsType<SourceDocumentRequest.Type>(
                    typeDocument.Request)
                .Value);
        Assert.NotEmpty(typeDocument.Binding.Value);
        Assert.Empty(typeof(SourceDocumentBinding).GetConstructors());
        Assert.NotNull(typeDocument.Authored);
        Assert.Null(typeDocument.AuthoredAttempt);
        SourceDocumentTypeMappingEvidence typeMapping =
            Assert.IsType<SourceDocumentTypeMappingEvidence>(
                typeDocument.TypeMapping);
        Assert.True(typeMapping.IsPartial);
        Assert.Single(typeMapping.AdditionalDocuments);
        Assert.Same(partialEnvelope.Share, projectedType.Share);
        Assert.Equal(partialEnvelope.Diagnostics, projectedType.Diagnostics);
        additionalDocuments.Clear();
        Assert.Single(typeMapping.AdditionalDocuments);

        TestAssembly fallbackAssembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        InspectionEnvelope<AssemblyTypeSourceEntry> fallbackType;
        using (var host = QueryHost.WithPdb(
            fallbackAssembly.PdbPath,
            "not checksum verified source"u8.ToArray()))
        {
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [fallbackAssembly.Participant]);
            fallbackType = await TypeSourceInspection.ExecuteAsync(
                group,
                fallbackAssembly.Participant,
                fallbackAssembly.TypeRequest("Counter"),
                host.Context,
                TestContext.Current.CancellationToken);
        }

        InspectionEnvelope<SourceDocument> projectedFallback =
            Assert.IsType<
                    SourceDocumentProjection<AssemblyTypeSourceEntry>
                        .Available>(
                    SourceDocumentInspection.Project(fallbackType))
                .Inspection;
        Assert.Equal(
            SourceDocumentProvider.Decompiled,
            projectedFallback.Content.Provider);
        Assert.Null(projectedFallback.Content.Authored);
        var authoredAttempt =
            Assert.IsType<
                SourceDocumentAuthoredAttemptEvidence.Type>(
                projectedFallback.Content.AuthoredAttempt);
        Assert.Equal(
            PdbTypeSourceOutcome.ChecksumMismatch,
            authoredAttempt.Outcome);
        Assert.Equal(
            fallbackType.Share,
            projectedFallback.Share);
        Assert.Equal(
            fallbackType.Diagnostics,
            projectedFallback.Diagnostics);

        string memberPath =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string memberPdbPath =
            Path.ChangeExtension(memberPath, ".pdb");
        TestAssembly memberAssembly =
            TestAssembly.CreatePackage(
                File.ReadAllBytes(memberPath),
                memberPdbPath);
        using var memberHost = QueryHost.WithPdb(
            memberPdbPath,
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "LibraryAdapter",
                    "MemberTextSlicer.cs")),
            maxDecompilerBodyProjections: 0);
        InspectionEnvelope<AssemblyMemberSourceEntry> pdbMember;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [memberAssembly.Participant]);
            pdbMember = await MemberSourceInspection.ExecuteAsync(
                group,
                memberAssembly.Participant,
                memberAssembly.MemberRequest(
                    "ExtractMemberText",
                    "MemberTextSlicer"),
                memberHost.Context,
                TestContext.Current.CancellationToken);
        }

        InspectionEnvelope<SourceDocument> projectedMember =
            Assert.IsType<
                    SourceDocumentProjection<AssemblyMemberSourceEntry>
                        .Available>(
                    SourceDocumentInspection.Project(pdbMember))
                .Inspection;
        Assert.Equal(
            SourceDocumentProvider.Pdb,
            projectedMember.Content.Provider);
        Assert.IsType<SourceDocumentRequest.Member>(
            projectedMember.Content.Request);
        Assert.Null(projectedMember.Content.TypeMapping);
        Assert.Equal(
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                    pdbMember.Content)
                .Source.Text,
            Reconstruct(projectedMember.Content));

        TestAssembly fallbackMemberAssembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var fallbackMemberHost = QueryHost.WithPdb(
            fallbackMemberAssembly.PdbPath,
            "not checksum verified member source"u8.ToArray());
        InspectionEnvelope<AssemblyMemberSourceEntry> fallbackMember;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [fallbackMemberAssembly.Participant]);
            fallbackMember = await MemberSourceInspection.ExecuteAsync(
                group,
                fallbackMemberAssembly.Participant,
                fallbackMemberAssembly.MemberRequest(
                    "Value",
                    "Counter"),
                fallbackMemberHost.Context,
                TestContext.Current.CancellationToken);
        }

        InspectionEnvelope<SourceDocument> projectedFallbackMember =
            Assert.IsType<
                    SourceDocumentProjection<AssemblyMemberSourceEntry>
                        .Available>(
                    SourceDocumentInspection.Project(fallbackMember))
                .Inspection;
        Assert.Equal(
            SourceDocumentProvider.Decompiled,
            projectedFallbackMember.Content.Provider);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumMismatch,
            Assert.IsType<
                    SourceDocumentAuthoredAttemptEvidence.Member>(
                    projectedFallbackMember.Content.AuthoredAttempt)
                .Outcome);

        var rejectedContent =
            new AssemblyTypeSourceEntry.Rejected(
                typeAvailable.Subject,
                typeAvailable.Request,
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The candidate is not a managed image."));
        var rejectedEnvelope =
            new InspectionEnvelope<AssemblyTypeSourceEntry>(
                rejectedContent,
                pdbType.Share,
                pdbType.Diagnostics);
        var retainedRejected =
            Assert.IsType<
                SourceDocumentProjection<AssemblyTypeSourceEntry>
                    .Retained>(
                SourceDocumentInspection.Project(rejectedEnvelope));
        Assert.Same(rejectedEnvelope, retainedRejected.Inspection);

        var unavailableContent =
            new AssemblyTypeSourceEntry.Unavailable(
                typeAvailable.Subject,
                typeAvailable.Request,
                new AssemblySourceFailure(
                    AssemblySourceFailureKind.PdbAndDecompiledUnavailable,
                    "No complete Source content was available."),
                partialInspection with
                {
                    Outcome = PdbTypeSourceOutcome.SourceLimitExceeded,
                });
        var unavailableEnvelope =
            new InspectionEnvelope<AssemblyTypeSourceEntry>(
                unavailableContent,
                pdbType.Share,
                pdbType.Diagnostics);
        var retainedUnavailable =
            Assert.IsType<
                SourceDocumentProjection<AssemblyTypeSourceEntry>
                    .Retained>(
                SourceDocumentInspection.Project(unavailableEnvelope));
        Assert.Same(unavailableEnvelope, retainedUnavailable.Inspection);

        var incompleteAvailable =
            partialEntry with
            {
                Source = new AssemblyTypeSource.Pdb(
                    typePdb.Text,
                    partialInspection with { Text = null },
                    typePdb.Provenance),
            };
        var incompleteAvailableEnvelope =
            new InspectionEnvelope<AssemblyTypeSourceEntry>(
                incompleteAvailable,
                pdbType.Share,
                pdbType.Diagnostics);
        InvalidOperationException incompleteError =
            Assert.Throws<InvalidOperationException>(
                () => SourceDocumentInspection.Project(
                    incompleteAvailableEnvelope));
        Assert.Contains(
            "complete decoded document",
            incompleteError.Message);
    }

    private static InspectionEnvelope<AssemblyTypeSourceEntry> WithTypeText(
        InspectionEnvelope<AssemblyTypeSourceEntry> template,
        string text)
    {
        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                template.Content);
        var pdb =
            Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var content = available with
        {
            Source = new AssemblyTypeSource.Pdb(
                text,
                pdb.Inspection with { Text = text },
                pdb.Provenance),
        };
        return new InspectionEnvelope<AssemblyTypeSourceEntry>(
            content,
            template.Share,
            template.Diagnostics);
    }

    private static SourceDocument AvailableDocument(
        SourceDocumentProjection<AssemblyTypeSourceEntry> projection) =>
        Assert.IsType<
                SourceDocumentProjection<AssemblyTypeSourceEntry>
                    .Available>(projection)
            .Inspection.Content;

    private static string Reconstruct(SourceDocument document) =>
        string.Concat(
            document.Lines.Select(
                static line => line.Content + line.TerminatorText));

    private static void AssertLine(
        SourceDocumentLine line,
        int number,
        int start,
        string content,
        SourceDocumentLineTerminator terminator)
    {
        Assert.Equal(number, line.Number);
        Assert.Equal(start, line.Start);
        Assert.Equal(content, line.Content);
        Assert.Equal(terminator, line.Terminator);
    }
}
