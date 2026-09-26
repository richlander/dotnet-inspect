using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Text;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // PR-fast: exact Source line coordinates are independent of acquisition.
    [Fact]
    public async Task SourceViewLinesReconstructExactDecodedText()
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
        SourceView view =
            AvailableDocument(
                SourceViewInspection.Project(
                    WithTypeText(template, mixed)));

        Assert.Equal(mixed, Reconstruct(view));
        Assert.Collection(
            view.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "A",
                DecodedTextLineTerminator.CarriageReturnLineFeed),
            line => AssertLine(
                line,
                2,
                3,
                "😀B",
                DecodedTextLineTerminator.CarriageReturn),
            line => AssertLine(
                line,
                3,
                7,
                "C",
                DecodedTextLineTerminator.LineFeed),
            line => AssertLine(
                line,
                4,
                9,
                "D",
                DecodedTextLineTerminator.NextLine),
            line => AssertLine(
                line,
                5,
                11,
                "E",
                DecodedTextLineTerminator.LineSeparator),
            line => AssertLine(
                line,
                6,
                13,
                "F",
                DecodedTextLineTerminator.ParagraphSeparator),
            line => AssertLine(
                line,
                7,
                15,
                "G",
                DecodedTextLineTerminator.None));

        SourceView empty =
            AvailableDocument(
                SourceViewInspection.Project(
                    WithTypeText(template, "")));
        Assert.Collection(
            empty.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "",
                DecodedTextLineTerminator.None));
        Assert.Equal("", Reconstruct(empty));

        SourceView finalEmpty =
            AvailableDocument(
                SourceViewInspection.Project(
                    WithTypeText(template, "A\n")));
        Assert.Collection(
            finalEmpty.Lines,
            line => AssertLine(
                line,
                1,
                0,
                "A",
                DecodedTextLineTerminator.LineFeed),
            line => AssertLine(
                line,
                2,
                2,
                "",
                DecodedTextLineTerminator.None));
        Assert.Equal("A\n", Reconstruct(finalEmpty));
    }

    // PR-fast: successful Source target views retain their distinct artifact
    // association, provider evidence, and exact typed failure behavior.
    [Fact]
    public async Task
        SourceViewProjectionDistinguishesArtifactsAndPreservesEvidence()
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
        InspectionEnvelope<SourceView> projectedType =
            Assert.IsType<
                    SourceViewProjection<AssemblyTypeSourceEntry>
                        .Available>(
                    SourceViewInspection.Project(partialEnvelope))
                .Inspection;
        SourceView typeView = projectedType.Content;

        Assert.Equal(SourceViewProvider.Pdb, typeView.Provider);
        Assert.Equal(
            SourceViewKind.AuthoredWholeDocument,
            typeView.Kind);
        Assert.Equal(
            SourceViewLanguage.CSharp,
            typeView.Language);
        Assert.Equal(
            typeAvailable.Subject.Identity,
            typeView.Identity.Assembly);
        Assert.Same(
            typeAvailable.Subject.Provenance,
            typeView.Identity.Resolution);
        Assert.Equal(
            typeAvailable.Request,
            Assert.IsType<SourceViewRequest.Type>(
                    typeView.Request)
                .Value);
        Assert.NotEmpty(typeView.Binding.Value);
        Assert.Empty(typeof(SourceViewBinding).GetConstructors());
        var typeOrigin =
            Assert.IsType<SourceViewOrigin.AuthoredWholeDocument>(
                typeView.Origin);
        Assert.Same(typePdb.Provenance, typeOrigin.Artifact!.Provenance);
        Assert.Equal(
            partialInspection.Document,
            typeOrigin.Artifact.Document);
        Assert.Equal(
            partialInspection.ChecksumVerification,
            typeOrigin.Artifact.ChecksumVerification);
        Assert.Null(typeView.AuthoredAttempt);
        SourceViewTypeMappingEvidence typeMapping =
            Assert.IsType<SourceViewTypeMappingEvidence>(
                typeView.TypeMapping);
        Assert.True(typeMapping.IsPartial);
        Assert.Single(typeMapping.AdditionalDocuments);
        Assert.Same(partialEnvelope.Share, projectedType.Share);
        Assert.Equal(partialEnvelope.Diagnostics, projectedType.Diagnostics);
        additionalDocuments.Clear();
        Assert.Single(typeMapping.AdditionalDocuments);

        string visualBasicAssemblyPath =
            FixtureCatalog.SourceLinkVisualBasic.AssemblyPath();
        TestAssembly visualBasicAssembly =
            TestAssembly.Create(
                File.ReadAllBytes(visualBasicAssemblyPath));
        using var visualBasicHost = QueryHost.WithSource(
            File.ReadAllBytes(
                Path.Combine(
                    FindRepositoryRoot(),
                    "fixtures",
                    "sourcelink",
                    "DotnetInspector.SourceLinkVisualBasicFixtures",
                    "BodylessSourceFixture.vb")));
        InspectionEnvelope<AssemblyTypeSourceEntry> visualBasicType;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [visualBasicAssembly.Participant]);
            visualBasicType = await TypeSourceInspection.ExecuteAsync(
                group,
                visualBasicAssembly.Participant,
                visualBasicAssembly.TypeRequest(
                    "BodylessSourceFixture"),
                visualBasicHost.Context,
                TestContext.Current.CancellationToken);
        }

        SourceView visualBasicView =
            Assert.IsType<
                    SourceViewProjection<AssemblyTypeSourceEntry>
                        .Available>(
                    SourceViewInspection.Project(visualBasicType))
                .Inspection.Content;
        Assert.Equal(
            SourceViewProvider.Pdb,
            visualBasicView.Provider);
        Assert.Equal(
            SourceViewKind.AuthoredWholeDocument,
            visualBasicView.Kind);
        Assert.Equal(
            SourceViewLanguage.VisualBasic,
            visualBasicView.Language);
        Assert.Contains(
            "Public Interface BodylessSourceFixture",
            Reconstruct(visualBasicView),
            StringComparison.Ordinal);

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

        InspectionEnvelope<SourceView> projectedFallback =
            Assert.IsType<
                    SourceViewProjection<AssemblyTypeSourceEntry>
                        .Available>(
                    SourceViewInspection.Project(fallbackType))
                .Inspection;
        Assert.Equal(
            SourceViewProvider.Decompiled,
            projectedFallback.Content.Provider);
        Assert.Equal(
            SourceViewKind.DecompiledType,
            projectedFallback.Content.Kind);
        Assert.Equal(
            SourceViewLanguage.CSharp,
            projectedFallback.Content.Language);
        Assert.Null(projectedFallback.Content.Origin.Artifact);
        var authoredAttempt =
            Assert.IsType<
                SourceViewAuthoredAttemptEvidence.Type>(
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
        byte[] memberDocumentBytes =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "LibraryAdapter",
                    "MemberTextSlicer.cs"));
        using var memberHost = QueryHost.WithPdb(
            memberPdbPath,
            memberDocumentBytes,
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

        InspectionEnvelope<SourceView> projectedMember =
            Assert.IsType<
                    SourceViewProjection<AssemblyMemberSourceEntry>
                        .Available>(
                    SourceViewInspection.Project(pdbMember))
                .Inspection;
        Assert.Equal(
            SourceViewProvider.Pdb,
            projectedMember.Content.Provider);
        Assert.Equal(
            SourceViewKind.AuthoredDeclarationExcerpt,
            projectedMember.Content.Kind);
        Assert.IsType<SourceViewRequest.Member>(
            projectedMember.Content.Request);
        Assert.Null(projectedMember.Content.TypeMapping);
        var memberOrigin =
            Assert.IsType<
                SourceViewOrigin.AuthoredDeclarationExcerpt>(
                projectedMember.Content.Origin);
        var memberAvailable =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                pdbMember.Content);
        var memberPdb =
            Assert.IsType<AssemblyMemberSource.Pdb>(
                memberAvailable.Source);
        Assert.Same(
            memberPdb.Inspection.Mapping,
            memberOrigin.Mapping);
        Assert.Equal(
            memberPdb.Inspection.Document,
            memberOrigin.Artifact!.Document);
        Assert.NotEqual(
            SourceLinkService.DecodeSourceText(memberDocumentBytes),
            Reconstruct(projectedMember.Content));
        Assert.Equal(
            memberAvailable.Source.Text,
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

        InspectionEnvelope<SourceView> projectedFallbackMember =
            Assert.IsType<
                    SourceViewProjection<AssemblyMemberSourceEntry>
                        .Available>(
                    SourceViewInspection.Project(fallbackMember))
                .Inspection;
        Assert.Equal(
            SourceViewProvider.Decompiled,
            projectedFallbackMember.Content.Provider);
        Assert.Equal(
            SourceViewKind.DecompiledMember,
            projectedFallbackMember.Content.Kind);
        Assert.Null(projectedFallbackMember.Content.Origin.Artifact);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumMismatch,
            Assert.IsType<
                    SourceViewAuthoredAttemptEvidence.Member>(
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
                SourceViewProjection<AssemblyTypeSourceEntry>
                    .Retained>(
                SourceViewInspection.Project(rejectedEnvelope));
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
                SourceViewProjection<AssemblyTypeSourceEntry>
                    .Retained>(
                SourceViewInspection.Project(unavailableEnvelope));
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
                () => SourceViewInspection.Project(
                    incompleteAvailableEnvelope));
        Assert.Contains(
            "complete decoded view",
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

    private static SourceView AvailableDocument(
        SourceViewProjection<AssemblyTypeSourceEntry> projection) =>
        Assert.IsType<
                SourceViewProjection<AssemblyTypeSourceEntry>
                    .Available>(projection)
            .Inspection.Content;

    private static string Reconstruct(SourceView view) =>
        string.Concat(
            view.Lines.Select(
                static line => line.Content + line.TerminatorText));

    private static void AssertLine(
        SourceViewLine line,
        int number,
        int start,
        string content,
        DecodedTextLineTerminator terminator)
    {
        Assert.Equal(number, line.Number);
        Assert.Equal(start, line.Start);
        Assert.Equal(content, line.Content);
        Assert.Equal(terminator, line.Terminator);
    }
}
