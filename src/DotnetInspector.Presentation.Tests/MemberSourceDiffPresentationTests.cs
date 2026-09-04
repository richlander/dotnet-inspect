using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Presentation.Tests;

public class MemberSourceDiffPresentationTests
{
    [Fact]
    public void CompleteEndpoints_ProjectOneCanonicalAnalysisAndMappedDiff()
    {
        const string before =
            """
              [Source] extension(
                  int value)
              {
            #if TRACE
                  string text = @"first
            continuation";
            #endif
                  Moved();
                  if (value > 0)
                  {
                      Changed(value + 1);
                  }
              }
            """;
        const string after =
            """
                [Metadata]
                extension(
                    int value)
                {
                    if (value > 0)
                    {
                        Changed(
                            value + 2);
                    }
                    Moved();
                }
            """;

        MemberSourceDiffPresentation presentation =
            Assert.IsType<MemberSourceDiffPresentationResult.Available>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(before, after, "extension")))
            .Presentation;

        Assert.StartsWith(
            "  extension(\n      int value)",
            presentation.BeforeText,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "  extension(\n      int value)",
            presentation.AfterText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[Source]", presentation.BeforeText);
        Assert.DoesNotContain("[Metadata]", presentation.AfterText);
        Assert.Contains("\n#if TRACE\n", presentation.BeforeText);
        Assert.Contains("\ncontinuation\";\n", presentation.BeforeText);
        Assert.All(
            presentation.AfterText.Split('\n').Where(line => line.Length > 0),
            line => Assert.StartsWith("  ", line, StringComparison.Ordinal));
        Assert.Equal(
            presentation.BeforeText.Split('\n'),
            presentation.Analysis.Before);
        Assert.Equal(
            presentation.AfterText.Split('\n'),
            presentation.Analysis.After);
        Assert.Equal(
            MemberSourceDiffPresentationAdapter.BeforeLabel,
            presentation.Diff.Before.Label);
        Assert.Equal(
            MemberSourceDiffPresentationAdapter.AfterLabel,
            presentation.Diff.After.Label);
        Assert.Equal(
            Markout.TextDiffLineTerminator.Absent,
            presentation.Diff.Before.FinalLineTerminator);
        Assert.Equal(
            Markout.TextDiffLineTerminator.Absent,
            presentation.Diff.After.FinalLineTerminator);
        Assert.True(presentation.Statistics.HasDifferences);
        Assert.Equal(before, presentation.Pdb.Inspection.Text);
        Assert.DoesNotContain("class extension", presentation.BeforeText);
        Assert.DoesNotContain("class extension", presentation.AfterText);
    }

    [Theory]
    [InlineData("", "public void M() { }")]
    [InlineData("\t", "\tpublic void M() { }")]
    [InlineData(" \t", " \tpublic void M() { }")]
    public void PdbPlacementPrefix_ReplacesExactlyOneDecompilerTypeBodyPrefix(
        string prefix,
        string expected)
    {
        MemberSourceDiffPresentation presentation =
            Assert.IsType<MemberSourceDiffPresentationResult.Available>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        $"{prefix}public void M() {{ }}",
                        "    public void M() { }",
                        "C")))
            .Presentation;

        Assert.Equal(expected, presentation.BeforeText);
        Assert.Equal(expected, presentation.AfterText);
    }

    [Fact]
    public void UnequalChangedAndMovedPopulations_AreCountedFromRelations()
    {
        MemberSourceDiffStatistics changed =
            Statistics(
                "before one\nbefore two\n",
                "after one\nafter two\nafter three\n");
        MemberSourceDiffStatistics moved =
            Statistics(
                "A\nB\nC\nmoved-one\nmoved-two\nD\nE",
                "moved-one\nmoved-two\nA\nB\nC\nD\nE");
        MemberSourceDiffStatistics overlap =
            Statistics(
                "A\nB\nC\nmoved-one\nmoved-two",
                "moved-one\nmoved-two\nA\nB\nC\n");

        Assert.Equal(2, changed.ChangedBefore);
        Assert.Equal(3, changed.ChangedAfter);
        Assert.Equal(2, moved.MovedBefore);
        Assert.Equal(2, moved.MovedAfter);
        Assert.Equal(1, overlap.ChangedBefore);
        Assert.Equal(1, overlap.ChangedAfter);
        Assert.Equal(2, overlap.MovedBefore);
        Assert.Equal(2, overlap.MovedAfter);
    }

    [Fact]
    public void IdenticalCanonicalEndpoints_RemainACompleteZeroCountResult()
    {
        MemberSourceDiffPresentation presentation =
            Assert.IsType<MemberSourceDiffPresentationResult.Available>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "public void M() { }",
                        "    public void M() { }",
                        "C")))
            .Presentation;

        Assert.False(presentation.Statistics.HasDifferences);
        Assert.Equal(default, presentation.Statistics);
        Assert.NotEmpty(presentation.Analysis.Relations);
    }

    [Fact]
    public void CanonicalGenericArityAndKeywordEscaping_DeriveWrapperFromExactType()
    {
        MemberSourceDiffPresentation presentation =
            Assert.IsType<MemberSourceDiffPresentationResult.Available>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "public @class() { }",
                        "    public @class() { }",
                        "class`1")))
            .Presentation;

        Assert.Equal("public @class() { }", presentation.BeforeText);
        Assert.Equal("public @class() { }", presentation.AfterText);
    }

    [Fact]
    public void InadmissibleDeclaringTypeName_FailsVisibly()
    {
        var failure =
            Assert.IsType<MemberSourceDiffPresentationResult.Failed>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "public void M() { }",
                        "    public void M() { }",
                        "bad-name")));

        Assert.Equal(
            MemberSourceDiffProjectionFailureKind.DeclaringTypeNameNotRepresentable,
            failure.Failure.Kind);
    }

    [Fact]
    public void CompleteDecompilerLineWithoutTypeBodyPrefix_FailsVisibly()
    {
        var failure =
            Assert.IsType<MemberSourceDiffPresentationResult.Failed>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "public void M() { }",
                        "    public void M()\n{\n    return;\n}",
                        "C")));

        Assert.Equal(
            MemberSourceDiffProjectionFailureKind.InconsistentDecompilerIndentation,
            failure.Failure.Kind);
    }

    [Fact]
    public void MissingAndAmbiguousMemberBoundaries_FailVisibly()
    {
        var missing =
            Assert.IsType<MemberSourceDiffPresentationResult.Failed>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "not a member",
                        "    public void M() { }",
                        "C")));
        var ambiguous =
            Assert.IsType<MemberSourceDiffPresentationResult.Failed>(
                MemberSourceDiffPresentationAdapter.Create(
                    Comparison(
                        "int first, second;",
                        "    int first, second;",
                        "C")));

        Assert.Equal(
            MemberSourceDiffProjectionFailureKind.MissingMemberBoundary,
            missing.Failure.Kind);
        Assert.Equal(
            MemberSourceDiffProjectionFailureKind.AmbiguousMemberBoundary,
            ambiguous.Failure.Kind);
    }

    [Fact]
    public void IncompleteEndpointEvidence_DoesNotProduceADiff()
    {
        AssemblyMemberSourceComparisonEntry.Available comparison =
            Comparison(
                "public void M() { }",
                "    public void M() { }",
                "C");
        var incomplete = comparison with
        {
            Decompiled = new AssemblyMemberDecompiledSourceAttempt.Unavailable(
                MemberBodyProductionStatus.Failed,
                "failure")
        };

        Assert.IsType<MemberSourceDiffPresentationResult.Unavailable>(
            MemberSourceDiffPresentationAdapter.Create(incomplete));
    }

    static AssemblyMemberSourceComparisonEntry.Available Comparison(
        string before,
        string after,
        string typeName)
    {
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Example", [typeName]))
            .Name;
        var request = new AssemblyMemberSourceRequest(
            type,
            new MemberAnchor(
                "M()",
                "Example.C.M()",
                "fingerprint",
                $"Example.{typeName}",
                "M"),
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)));
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(MemberSourceDiffPresentationTests).Assembly.Location,
                AssemblyResolutionProvenance.Local("presentation tests"));
        var subject = new AssemblyContextSubject(assembly);
        var inspection = new PdbMemberSourceInspection(
            new FindingInspection<string>(
                new FindingInspection<string>.Complete(
                    ImmutableArray<Finding<string>>.Empty)),
            before,
            Mapping: null,
            Document: null,
            SourceChecksumVerification.Exact)
        {
            Outcome = PdbMemberSourceOutcome.Complete
        };
        return new AssemblyMemberSourceComparisonEntry.Available(
            subject,
            request,
            new AssemblyMemberPdbSourceAttempt.Available(
                inspection,
                new AssemblyPdbSourceProvenance(
                    "https://example.test/repo",
                    "revision")),
            new AssemblyMemberDecompiledSourceAttempt.Available(
                new MemberRenderResult(
                    MemberBodyProductionStatus.Complete,
                    after,
                    [])));
    }

    static MemberSourceDiffStatistics Statistics(
        string before,
        string after)
        => MemberSourceDiffStatistics.Create(
            ILInspector.Text.TextFindings.CreateAnalysisDiff(
                before,
                after,
                new FindingSubject(
                    "member.source.diff",
                    "Statistics test")));
}
