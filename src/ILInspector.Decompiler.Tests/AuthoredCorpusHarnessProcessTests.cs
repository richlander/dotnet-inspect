using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Research;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Black-box gates on the harness <em>binary</em> for the authored-corpus gate flags
/// (#3245).
///
/// <para>These exist because of a defect that five separate review rounds found five
/// separate instances of: <c>Program.cs</c> owns an entry point, so it cannot be linked
/// into this project, so every decision it makes is unreachable by an ordinary test. The
/// response each time was to move one more term of the rule into
/// <see cref="AuthoredCorpusExitContract"/> and — for what could not be moved — to pin
/// the call site by reading its source text. Both responses were wrong.</para>
///
/// <para>Moving terms only relocates the seam. Review round nine deleted a single
/// argument from the <c>JudgeGateFlags</c> call, so the harness passed <c>false</c> where
/// it should have passed <c>verifyAuthoredCorpus</c>; the contract function was still
/// perfectly tested, the whole suite stayed green, and
/// <c>--verify-authored-corpus &lt;missing&gt; --help</c> printed usage and exited 0 —
/// a gate reporting success having measured nothing, which is #3245 itself.</para>
///
/// <para>The source-text pin was worse than useless, being wrong in both directions at
/// once: a behavior-preserving comment inside the argument list <em>failed</em> it, while
/// a commented-out decoy call above the real one <em>satisfied</em> it with the live call
/// passing a single gate. A test that fails on a no-op edit and passes on a real
/// regression is not a weak gate, it is a misleading one, and it was deleted rather than
/// patched.</para>
///
/// <para>So these tests run the binary and read what it says. There is no seam left to
/// strand a rule behind: whatever <c>Program.cs</c> parses, forwards, orders, or decides
/// is inside the assertion. They check the <em>message</em> and not only the exit code,
/// because an earlier round shipped a regression that exited 1 for the wrong reason and
/// an exit-code-only check read it as correct.</para>
/// </summary>
[Trait("Area", "Corpus")]
public partial class AuthoredCorpusHarnessProcessTests
{
    [Fact]
    public void HarnessBinary_CarriesKnownBuildSourceState()
    {
        Assembly assembly = Assembly.LoadFile(HarnessBinary());
        string state = AuthoredCorpusHistoryStore.ReadSourceStateAtBuild(
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>());

        Assert.Contains(state, new[] { "clean", "dirty" });
    }

    [Fact]
    public void Harness_RendersStructuralReviewFromProductDiffDocument()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, StructuralReviewJson("break;", "BreakStatement", 6));

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains(
                "raise: Return case body; changed to break;",
                run.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "raise: Break case body; changed from return;",
                run.Output,
                StringComparison.Ordinal);
            Assert.Contains("| Change | Structure | Region | Detail | Fidelity |", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Before spans", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("After spans", run.Output, StringComparison.Ordinal);
            Assert.Contains("Return -&gt; Break", run.Output, StringComparison.Ordinal);
            Assert.Contains("return; -&gt; break;", run.Output, StringComparison.Ordinal);
            Assert.Contains("OpcodeDiff -&gt; Exact", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RendersExplicitNoChangeStructuralReview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, StructuralReviewJson("return;", "ReturnStatement", 7));

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("## Before", run.Output, StringComparison.Ordinal);
            Assert.Contains("## After", run.Output, StringComparison.Ordinal);
            Assert.Contains("No structural changes.", run.Output, StringComparison.Ordinal);
            Assert.Contains("Fidelity: ` OpcodeDiff -> Exact; terminal IL_0072: ret `", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("raise:", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_IssuesStructuralReviewFromTwoProductDocuments()
    {
        string beforePath = Path.Combine(Path.GetTempPath(), $"structural-before-{Guid.NewGuid():N}.json");
        string afterPath = Path.Combine(Path.GetTempPath(), $"structural-after-{Guid.NewGuid():N}.json");
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var reader = source.Reader;
        var method = reader.MethodDefinitions.Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(CfgSampleClass.CallsKeywordInstanceMethod));
        int token = MetadataTokens.GetToken(method);

        AnnotatedSourceDocument Project(PrinterOptions? options)
            => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.CallsKeywordInstanceMethod),
                AnnotatedStage: AnnotationStage.Raised,
                MethodToken: token,
                PrinterOptions: options,
                SourceDocument: true)).SourceDocument!;

        var before = Project(options: null);
        var after = Project(new PrinterOptions { QualifyMethodAccess = true });
        File.WriteAllText(
            beforePath,
            JsonSerializer.Serialize(
                before,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));
        File.WriteAllText(
            afterPath,
            JsonSerializer.Serialize(
                after,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));

        try
        {
            var run = RunHarness("--structural-review", beforePath, afterPath);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("@event(value)", run.Output, StringComparison.Ordinal);
            Assert.Contains("this.@event(value)", run.Output, StringComparison.Ordinal);
            Assert.Contains("Changed", run.Output, StringComparison.Ordinal);
            Assert.Contains("| Change | Structure | Region |", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("| Change | Structure | Region | Fidelity |", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("IL_", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(beforePath);
            File.Delete(afterPath);
        }
    }

    [Fact]
    public void Harness_BoundsStructuralReviewGapsWithoutDiscardingJsonEvidence()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, LargeGapStructuralReviewJson(20));

        try
        {
            var markdown = RunHarness("--structural-review", path);

            Assert.Equal(0, markdown.ExitCode);
            Assert.Contains(
                "Structural review status: **Partial** - 40 unsupported or ambiguous nodes were excluded.",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Supported rows do not establish changes represented only by the gaps below.",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.True(
                markdown.Output.IndexOf("Structural review status:", StringComparison.Ordinal) <
                markdown.Output.IndexOf("## Before", StringComparison.Ordinal));
            Assert.Contains("| Change | Structure | Region |", markdown.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("| Change | Structure | Region | Fidelity |", markdown.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Before spans", markdown.Output, StringComparison.Ordinal);
            Assert.Contains(
                "| Before | Ambiguous | 20 | 1, 2, 3, 4, 5 (+15 more) |",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "| After | Ambiguous | 20 | 1, 2, 3, 4, 5 (+15 more) |",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain("IL_0020", markdown.Output, StringComparison.Ordinal);

            var json = RunHarness("--structural-review", path, "--json");

            Assert.Equal(0, json.ExitCode);
            var replayed = AnnotatedSourceJson.DeserializeStructuralDiff(json.Output);
            Assert.Equal(
                20,
                replayed.Correspondence.UnmatchedBefore.Count(
                    static node => node.Reason == CSharpUnmatchedNodeReason.Ambiguous));
            Assert.Equal(
                20,
                replayed.Correspondence.UnmatchedAfter.Count(
                    static node => node.Reason == CSharpUnmatchedNodeReason.Ambiguous));
            Assert.All(
                replayed.Correspondence.UnmatchedBefore,
                static node => Assert.Equal([0x20, 0x21, 0x22], node.Evidence!.IlOffsets));
            Assert.All(
                replayed.Correspondence.UnmatchedAfter,
                static node => Assert.Equal([0x20, 0x21, 0x22], node.Evidence!.IlOffsets));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_EmitsAndReplaysProductStructuralDiffDocument()
    {
        string beforePath = Path.Combine(Path.GetTempPath(), $"structural-before-{Guid.NewGuid():N}.json");
        string afterPath = Path.Combine(Path.GetTempPath(), $"structural-after-{Guid.NewGuid():N}.json");
        string diffPath = Path.Combine(Path.GetTempPath(), $"structural-diff-{Guid.NewGuid():N}.json");
        var source = new AnnotatedSourceDocumentSource(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            "Fixture.M");
        var before = StructuralDocument("return;", "ReturnStatement", 7, source);
        var after = StructuralDocument("break;", "BreakStatement", 6, source);
        File.WriteAllText(
            beforePath,
            JsonSerializer.Serialize(
                before,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));
        File.WriteAllText(
            afterPath,
            JsonSerializer.Serialize(
                after,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));

        try
        {
            var emitted = RunHarness(
                "--structural-review",
                beforePath,
                afterPath,
                "--json");

            Assert.Equal(0, emitted.ExitCode);
            var document = AnnotatedSourceJson.DeserializeStructuralDiff(emitted.Output);
            Assert.Single(document.Correspondence.Matches);
            Assert.Single(document.ToComparison().Rows);

            File.WriteAllText(diffPath, emitted.Output);
            var replayed = RunHarness("--structural-review", diffPath);

            Assert.Equal(0, replayed.ExitCode);
            Assert.Contains("Return -&gt; Break", replayed.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(beforePath);
            File.Delete(afterPath);
            File.Delete(diffPath);
        }
    }

    [Fact]
    public void Harness_ReportsPureProductDocumentReorderAsMovement()
    {
        string beforePath = Path.Combine(Path.GetTempPath(), $"structural-before-{Guid.NewGuid():N}.json");
        string afterPath = Path.Combine(Path.GetTempPath(), $"structural-after-{Guid.NewGuid():N}.json");
        var source = new AnnotatedSourceDocumentSource(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            "Fixture.M");
        static AnnotatedSourceNode Node(
            int id,
            int start,
            int ilOffset) => new(
                id,
                "ExpressionStatement",
                SourceLineKind.CSharp,
                [new(start, 6)],
                Provenance: new AnnotatedSourceNodeProvenance([ilOffset]));
        var before = new AnnotatedSourceDocument(
            "one();\ntwo();\n",
            [Node(0, 0, 0x10), Node(1, 7, 0x20)],
            [],
            [],
            [],
            source);
        var after = new AnnotatedSourceDocument(
            "two();\none();\n",
            [Node(0, 0, 0x20), Node(1, 7, 0x10)],
            [],
            [],
            [],
            source);
        File.WriteAllText(
            beforePath,
            JsonSerializer.Serialize(
                before,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));
        File.WriteAllText(
            afterPath,
            JsonSerializer.Serialize(
                after,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument));

        try
        {
            var run = RunHarness("--structural-review", beforePath, afterPath);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("| Moved |", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("No structural changes.", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(beforePath);
            File.Delete(afterPath);
        }
    }

    [Fact]
    public void Harness_AcceptsProductDocumentFactWithoutDetail()
    {
        string beforePath = Path.Combine(Path.GetTempPath(), $"structural-before-{Guid.NewGuid():N}.json");
        string afterPath = Path.Combine(Path.GetTempPath(), $"structural-after-{Guid.NewGuid():N}.json");
        var source = new AnnotatedSourceDocumentSource(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            "Fixture.M");
        var document = new AnnotatedSourceDocument(
            "return;",
            [new AnnotatedSourceNode(
                0,
                "ReturnStatement",
                SourceLineKind.CSharp,
                [new(0, 7)],
                Provenance: new AnnotatedSourceNodeProvenance([0x10]))],
            [],
            [new AnnotatedSourceFact(
                0,
                "test",
                "Semantics",
                AnnotationConditionality.Always,
                Detail: null,
                SourceOffset: 0x10,
                AnnotatedSourceFactOrigin.Body)],
            [new AnnotatedSourceTarget(0, 0)],
            source);
        string json = JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument);
        File.WriteAllText(beforePath, json);
        File.WriteAllText(afterPath, json);

        try
        {
            var run = RunHarness("--structural-review", beforePath, afterPath);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("No structural changes.", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(beforePath);
            File.Delete(afterPath);
        }
    }

    [Fact]
    public void Harness_RejectsMissingRequiredStructuralDiffField()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace("\"provenance\": \"IlOriginSet\",", "", StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("missing required properties", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("provenance", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_ContainsUntrustedStructuralMarkdownCells()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson(
            "break;",
            "Break | ![remote](https://example.test/image)",
            6,
            note: "retained | ![remote](https://example.test/image)");
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(0, run.ExitCode);
            string richDiff = run.Output[run.Output.IndexOf("## Structural diff", StringComparison.Ordinal)..];
            Assert.Contains("&#124;", richDiff, StringComparison.Ordinal);
            Assert.Contains("&#33;&#91;remote&#93;", richDiff, StringComparison.Ordinal);
            Assert.DoesNotContain(" | ![remote]", richDiff, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_DispatchesStructuralReviewBeforeFixtureInventory()
    {
        var run = RunHarness(
            "--fixture-source-inventory",
            "--structural-review",
            "/does-not-exist/structural-review.json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--structural-review is an exclusive mode", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture source inventory", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_RejectsHelpCombinedWithStructuralReview()
    {
        var run = RunHarness(
            "--structural-review",
            "/does-not-exist/structural-review.json",
            "--help");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--structural-review is an exclusive mode", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Usage:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_ContainsStructuralTerminalControls()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson(
            "break;",
            "Break\u001B[31m",
            6,
            subject: "M\u001B[31m",
            note: "retained\u001B[31m");
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(0, run.ExitCode);
            Assert.DoesNotContain("\u001B", run.Output, StringComparison.Ordinal);
            Assert.Contains("\\u001B", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsUnknownStructuralFidelityOutcome()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace("\"before\": \"OpcodeDiff\"", "\"before\": 999", StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("unknown IL body-diff outcome", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsCompositeStructuralFidelityOutcome()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace(
                "\"before\": \"OpcodeDiff\"",
                "\"before\": \"Exact, OperandDiff\"",
                StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("unknown IL body-diff outcome", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsCompositeStructuralRegionRole()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace(
                "\"role\": \"Case\"",
                "\"role\": \"Header, Body\"",
                StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("unknown PrintedRegionRole value", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsMalformedStructuralPropertyNameWithoutCrashing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6);
        int rootEnd = json.LastIndexOf('}');
        json = $"{json[..rootEnd]},\"\\uD800\":0{json[rootEnd..]}";
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("structural diff JSON", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidOperationException", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"start\": 0", "\"discarded_start\": 0", "start")]
    [InlineData("\"length\": 7", "\"discarded_length\": 7", "length")]
    public void Harness_RejectsMissingStructuralSpanField(
        string oldValue,
        string newValue,
        string expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace(oldValue, newValue, StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("missing required properties", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expected, run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsMissingStructuralFactField()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace(
                "\"facts\": []",
                """
                "facts": [
                  {
                    "id": 0,
                    "descriptor": "test",
                    "category": "Semantics",
                    "conditionality": "Always",
                    "detail": null,
                    "source_offset": -1
                  }
                ]
                """,
                StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("missing required properties", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("origin", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsMissingStructuralTargetField()
    {
        string path = Path.Combine(Path.GetTempPath(), $"structural-review-{Guid.NewGuid():N}.json");
        string json = StructuralReviewJson("break;", "BreakStatement", 6)
            .Replace(
                "\"facts\": []",
                """
                "facts": [
                  {
                    "id": 0,
                    "descriptor": "test",
                    "category": "Semantics",
                    "conditionality": "Always",
                    "detail": null,
                    "source_offset": -1,
                    "origin": "Body"
                  }
                ]
                """,
                StringComparison.Ordinal)
            .Replace("\"targets\": []", "\"targets\": [ { \"fact_id\": 0 } ]", StringComparison.Ordinal);
        File.WriteAllText(path, json);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("missing required properties", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("node_id", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Harness_RejectsLegacyCallerAuthoredComparisonInput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"legacy-structural-review-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              "subject": "M",
              "before": {},
              "after": {},
              "before_node_ids": [],
              "after_node_ids": [],
              "correspondences": []
            }
            """);

        try
        {
            var run = RunHarness("--structural-review", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("missing required properties", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("schema_version", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Every mode that dispatches before a gate must refuse rather than run instead of
    /// it. One representative earlier mode per gate is enough here: which modes precede
    /// which is `AuthoredCorpusRatchetTests`' business, while this asserts the binary
    /// actually applies the rule to both gates.
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_RefusesAGateThatAnEarlierModeWouldReplace(string gate)
    {
        var run = RunHarness(gate, "/does-not-exist.jsonl", "--history-card");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            $"--history-card runs instead of {gate}",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same, with the flags in the other order. Preemption is decided by dispatch
    /// order and not by the order the caller typed, so both spellings must refuse — and a
    /// reviewer's exploit used the mode-first spelling.
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    [InlineData("--append-authored-corpus-history")]
    [InlineData("--verify-authored-corpus-history")]
    public void Harness_RefusesRegardlessOfTheOrderTheFlagsWereTyped(string gate)
    {
        var run = RunHarness("--history-card", gate, "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"runs instead of {gate}", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking for usage while asking for a gate must refuse, not print usage and exit 0.
    ///
    /// <para>This is the exact combination whose rule was found stranded twice: once
    /// inline in <c>Program.cs</c>, and once as an argument the harness stopped
    /// forwarding. Both times the contract function was fully tested and both times the
    /// binary answered 0.</para>
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    [InlineData("--append-authored-corpus-history")]
    [InlineData("--verify-authored-corpus-history")]
    public void Harness_RefusesHelpWhenAGateWasAlsoRequested(string gate)
    {
        var run = RunHarness(gate, "/does-not-exist.jsonl", "--help");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--help does not run a gate", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The modifiers refuse on their own, rather than being silently ignored by a run
    /// that then reports whatever it happened to do.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--integrity-only" }, "--integrity-only applies to")]
    [InlineData(new[] { "--ratchet-baseline", "/does-not-exist.jsonl" }, "--ratchet-baseline applies to")]
    [InlineData(new[] { "--source-oracle-manifest", "/does-not-exist.json" }, "--source-oracle-manifest applies to")]
    [InlineData(
        new[] { "--baseline-source-oracle-report", "/does-not-exist.json" },
        "--baseline-source-oracle-report applies to")]
    public void Harness_RefusesAModifierWithoutItsGate(string[] flags, string expected)
    {
        var run = RunHarness(flags);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(expected, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Demanding a quality verdict and declining to judge quality are contradictory, and
    /// honouring either silently would make the exit code mean something the caller did
    /// not ask for.
    /// </summary>
    [Fact]
    public void Harness_RefusesTheContradictoryModifierPair()
    {
        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl",
            "--integrity-only",
            "--ratchet-baseline", "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("are contradictory", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_RefusesIntegrityOnlyWithSourceOracleManifest()
    {
        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl",
            "--integrity-only",
            "--source-oracle-manifest", "/does-not-exist.json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("are contradictory", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusals above must not fire on a gate nobody requested.
    ///
    /// <para>Round seven shipped exactly that: the preemption search ran unconditionally,
    /// so every mode in the harness refused for preempting a gate that had not been
    /// asked for. It exited 1 — the value an exit-code-only check expects from a refusal
    /// test — for entirely the wrong reason, and the scheduled lane could never have
    /// run.</para>
    /// </summary>
    [Fact]
    public void Harness_RunsAModeThatPreemptsNoRequestedGate()
    {
        var run = RunHarness("--history-card");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Usage on its own still prints usage and succeeds; the refusals above are about a
    /// gate being requested, not about <c>--help</c> being unwelcome.
    /// </summary>
    [Fact]
    public void Harness_PrintsUsageWhenNoGateWasRequested()
    {
        var run = RunHarness("--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("usage: decompiler-harness", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void Harness_DispatchesTheTypedHistoryVerifier()
    {
        var run = RunHarness(
            "--verify-authored-corpus-history",
            "--history-path",
            "/does-not-exist/history.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("EVIL history verification failed:", run.Output, StringComparison.Ordinal);
        Assert.Contains("history.jsonl", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void Harness_DispatchesTheTypedHistoryAppender()
    {
        var run = RunHarness(
            "--append-authored-corpus-history",
            "/does-not-exist/run.json",
            "--history-path",
            "/does-not-exist/history.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Could not append EVIL history:", run.Output, StringComparison.Ordinal);
        Assert.Contains("history.jsonl", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scheduled lane's own flag combination reaches the benchmark.
    ///
    /// <para>Named after the workflow rather than described abstractly, because the
    /// failure this guards against is a refusal that looks correct in isolation. The
    /// corpus path is deliberately absent: reaching "corpus file not found" proves the
    /// flags were accepted and dispatch got as far as the gate, which is the part that
    /// regressed. Pointing it at a real corpus would test the benchmark instead, which
    /// `AuthoredCorpusRatchetTests` already does in-process.</para>
    ///
    /// <para>The lane carried `--integrity-only` until the trend store crossed its
    /// identity bootstrap (#3353); it now ratchets against the committed history, so
    /// this pins the combination the workflow actually passes. The `--integrity-only`
    /// combination is still supported and is covered separately below — a test named
    /// for the scheduled lane that exercises flags the lane no longer passes would
    /// report coverage it does not have.</para>
    /// </summary>
    [Fact]
    public void Harness_LetsTheScheduledRatchetLaneRun()
    {
        string history = AuthoredCorpusHistoryCardTests.TrackedHistoryPath();

        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl",
            "--ratchet-baseline", history,
            "--json");

        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("does not run a gate", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("applies to", run.Output, StringComparison.Ordinal);
        Assert.Contains("Corpus file not found", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--integrity-only` remains a supported contract, so its dispatch stays covered
    /// even though the scheduled lane no longer selects it.
    /// </summary>
    [Fact]
    public void Harness_LetsTheIntegrityOnlyCombinationRun()
    {
        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl", "--integrity-only", "--json");

        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("does not run a gate", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("applies to", run.Output, StringComparison.Ordinal);
        Assert.Contains("Corpus file not found", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every mode that dispatches before a gate must refuse rather than run instead of
    /// it — checked against the binary, once per mode, for both protected gates.
    ///
    /// <para>This was one representative mode per gate until review round ten, on the
    /// reasoning that which modes precede which is <c>AuthoredCorpusRatchetTests</c>'
    /// business. That reasoning was wrong, and a reviewer showed why: the dispatch-order
    /// pin over there compares mode <em>names</em>, while the harness pairs each name
    /// with a <c>Selected</c> expression. Replacing one such expression with
    /// <c>false</c> left the whole suite green, and
    /// <c>--fixture-source-inventory --benchmark-authored-corpus &lt;corpus&gt;</c>
    /// printed an inventory and exited 0 — the requested gate silently discarded, which
    /// is #3245 exactly. One mode was covered here, so one mode was safe; the other
    /// sixteen were not.</para>
    ///
    /// <para>The corpus path is absent on purpose. A refusal must win over a missing
    /// file, so "runs instead of" rather than "corpus file not found" is what proves the
    /// preemption fired.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(PreemptionCases))]
    public void Harness_RefusesEveryPreemptingModeAgainstEveryProtectedGate(
        string gate,
        string mode,
        string[] invocation)
    {
        var run = RunHarness([gate, "/does-not-exist.jsonl", .. invocation]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            $"{mode} runs instead of {gate}",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every mode that precedes a gate, paired with each gate it precedes.
    ///
    /// <para>Derived from the dispatch order rather than hand-listed, so a mode added to
    /// the harness without a case here fails
    /// <see cref="Preemption_CoversEveryModeThatPrecedesAGate"/> rather than silently
    /// going ungated.</para>
    /// </summary>
    public static TheoryData<string, string, string[]> PreemptionCases()
    {
        var cases = new TheoryData<string, string, string[]>();
        string[] order = AuthoredCorpusRatchetTests.DispatchOrderFlags;

        foreach (string gate in AuthoredCorpusExitContract.ProtectedGates)
        {
            int gateIndex = Array.IndexOf(order, gate);
            Assert.True(gateIndex >= 0, $"{gate} is not in the dispatch order.");

            foreach ((string mode, string[] invocation) in PreemptingModeInvocations)
            {
                // Only a mode that dispatches *earlier* preempts this gate. The two gates
                // are themselves in the order, so pairing them the other way round would
                // assert a refusal naming the wrong flag.
                if (Array.IndexOf(order, mode) < gateIndex)
                    cases.Add(gate, mode, invocation);
            }
        }

        return cases;
    }

    /// <summary>
    /// The theory above must exercise every mode the harness dispatches, not a subset
    /// that happens to be listed here.
    /// </summary>
    [Fact]
    public void Preemption_CoversEveryModeThatPrecedesAGate()
    {
        Assert.Equal(
            AuthoredCorpusRatchetTests.DispatchOrderFlags,
            PreemptingModeInvocations.Select(mode => mode.Mode).Distinct().ToArray());
    }

    /// <summary>
    /// How to invoke each dispatch mode, in dispatch order, paired with the flag name the
    /// refusal will report.
    ///
    /// <para>The two are separate because a mode is not always selected by the flag it is
    /// named after. Most are bare flags. The ones carrying a value must be spelled with
    /// it, or the flag would swallow the gate that follows it as its own argument and the
    /// test would pass having never requested a gate.</para>
    ///
    /// <para><c>--fidelity-check</c> is selected by a two-term disjunction, so it appears
    /// twice — and the second case must <em>omit</em> <c>--fidelity-check</c>, or the
    /// first term alone would satisfy it and the second would never be the reason the
    /// mode was selected. Review round eleven found exactly that: both cases passed the
    /// flag, so deleting the second term left the suite green. This list pairs the
    /// isolating invocation with the flag the harness reports for it, which is why the
    /// two columns exist at all.</para>
    ///
    /// <para>Round twelve found the same shape in five more modes. A mode is selected by
    /// every flag that sets its variable, whether the extra term sits in the selector
    /// expression (<c>--assertion-scan</c>, <c>--validity-check</c>) or in the parser
    /// (<c>--fuzz-signatures</c>, <c>--return-address</c>, <c>--not-my-type</c>), and an
    /// alternate flag with no case here is an alternate way to discard a gate that no
    /// test would notice. Every flag that selects a preempting mode gets an isolating
    /// case, which is why several modes appear more than once.</para>
    /// </summary>
    static readonly (string Mode, string[] Invocation)[] PreemptingModeInvocations =
    [
        ("--structural-review", ["--structural-review", UnusedOutputPath]),
        ("--fixture-source-inventory", ["--fixture-source-inventory"]),
        ("--history-card", ["--history-card"]),
        ("--generated-fixtures", ["--generated-fixtures"]),
        ("--fuzz-signatures", ["--fuzz-signatures"]),
        ("--fuzz-signatures", ["--fuzz-unguarded"]),
        ("--return-to-sender-catalog", ["--return-to-sender-catalog"]),
        ("--emit-inverse-ledger", ["--emit-inverse-ledger", UnusedOutputPath]),
        ("--assertion-scan", ["--assertion-scan"]),
        ("--assertion-scan", ["--emit-assertion-violations", UnusedOutputPath]),
        ("--assertion-scan", ["--diff-assertion-violations", UnusedOutputPath]),
        ("--validity-check", ["--validity-check"]),
        ("--validity-check", ["--emit-validity-defects", UnusedOutputPath]),
        ("--validity-check", ["--diff-validity-defects", UnusedOutputPath]),
        ("--validity-predicate-scan", ["--validity-predicate-scan"]),
        ("--fidelity-check", ["--fidelity-check"]),
        ("--fidelity-check", ["--fidelity-method-delta", "SomeType.SomeMethod"]),
        ("--return-to-sender", ["--return-to-sender"]),
        ("--return-address", ["--return-address"]),
        ("--return-address", ["--emit-return-address-snapshot", UnusedOutputPath]),
        ("--return-address", ["--diff-return-address-baseline", UnusedOutputPath]),
        ("--not-my-type", ["--not-my-type"]),
        ("--not-my-type", ["--emit-not-my-type-snapshot", UnusedOutputPath]),
        ("--not-my-type", ["--diff-not-my-type-baseline", UnusedOutputPath]),
        ("--enumerate-real-methods", ["--enumerate-real-methods"]),
        ("--harvest-authored-corpus", ["--harvest-authored-corpus", UnusedOutputPath]),
        ("--harvest-evil-corpus", ["--harvest-evil-corpus", UnusedOutputPath]),
        ("--source-oracle-candidates",
            ["--source-oracle-candidates", "--baseline-source-oracle-report", UnusedOutputPath]),
        ("--benchmark-authored-corpus", ["--benchmark-authored-corpus", "/does-not-exist.jsonl"]),
        ("--verify-authored-corpus", ["--verify-authored-corpus", "/does-not-exist.jsonl"]),
        ("--append-authored-corpus-history", ["--append-authored-corpus-history", "/does-not-exist.json"]),
        ("--verify-authored-corpus-history", ["--verify-authored-corpus-history"]),
    ];

    /// <summary>
    /// A path for flags that demand an output file. Every case using it must refuse
    /// before dispatch, so nothing is ever written here.
    /// </summary>
    const string UnusedOutputPath = "/does-not-exist/unused-output.json";

    static string StructuralReviewJson(
        string afterText,
        string afterKind,
        int afterLength,
        string subject = "M",
        string note = "terminal IL_0072: ret")
    {
        var source = new AnnotatedSourceDocumentSource(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            subject);
        var document = CSharpStructuralDiffDocument.Create(
            StructuralDocument("return;", "ReturnStatement", 7, source),
            StructuralDocument(afterText, afterKind, afterLength, source),
            new CSharpStructuralFidelityEvidence(
                IlBodyDiffOutcome.OpcodeDiff,
                IlBodyDiffOutcome.Exact,
                note));
        return AnnotatedSourceJson.SerializeStructuralDiff(document);
    }

    static string LargeGapStructuralReviewJson(int gapCount)
    {
        string beforeText = "return;\n" +
            string.Join('\n', Enumerable.Range(1, gapCount).Select(static index => $"Before{index}();"));
        string afterText = "break;\n" +
            string.Join('\n', Enumerable.Range(1, gapCount).Select(static index => $"After{index}();"));
        var source = new AnnotatedSourceDocumentSource(
            "Fixture",
            new Guid("11111111-2222-3333-4444-555555555555"),
            0x06000001,
            new string('A', 64),
            "M");

        AnnotatedSourceDocument Document(
            string text,
            string matchedKind,
            int matchedLength)
        {
            var nodes = new List<AnnotatedSourceNode>
            {
                new(
                    0,
                    matchedKind,
                    SourceLineKind.CSharp,
                    [new(0, matchedLength)],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10]))
            };

            int lineStart = matchedLength + 1;
            for (int index = 1; index <= gapCount; index++)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                    lineEnd = text.Length;

                nodes.Add(
                    new AnnotatedSourceNode(
                        index,
                        "ExpressionStatement",
                        SourceLineKind.CSharp,
                        [new(lineStart, lineEnd - lineStart)],
                        Provenance: new AnnotatedSourceNodeProvenance([0x20, 0x21, 0x22])));
                lineStart = lineEnd + 1;
            }

            return new AnnotatedSourceDocument(
                text,
                nodes,
                [new AnnotatedSourceRegion(PrintedRegionRole.Case, [new(0, text.Length)])],
                [],
                [],
                source);
        }

        return AnnotatedSourceJson.SerializeStructuralDiff(
            CSharpStructuralDiffDocument.Create(
                Document(beforeText, "ReturnStatement", "return;".Length),
                Document(afterText, "BreakStatement", "break;".Length)));
    }

    static AnnotatedSourceDocument StructuralDocument(
        string text,
        string kind,
        int length,
        AnnotatedSourceDocumentSource source)
        => new(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    kind,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, length)],
                    Provenance: new AnnotatedSourceNodeProvenance([0x10]))
            ],
            [
                new AnnotatedSourceRegion(
                    PrintedRegionRole.Case,
                    [new AnnotatedSourceSpan(0, length)])
            ],
            [],
            [],
            source);

    /// <summary>
    /// A requested <c>--ratchet-baseline</c> must reach the benchmark.
    ///
    /// <para>Dropping it at the call site left the suite green in review round ten, and
    /// the harness then ran the benchmark against no baseline at all while the caller
    /// believed it had ratcheted — a gate reporting success having compared nothing,
    /// which is #3245 restated.</para>
    ///
    /// <para>A missing baseline beside a real corpus file is what makes the forwarding
    /// observable: the benchmark reads the baseline before it reads the corpus, so
    /// "ratchet baseline not found" can only be reached if the path arrived. Were the
    /// argument dropped, the run would proceed to the corpus and complain about that
    /// instead.</para>
    /// </summary>
    [Fact]
    public void Harness_ForwardsTheRatchetBaselineToTheBenchmark()
    {
        string corpus = Path.Combine(Path.GetTempPath(), $"ratchet-forwarding-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(corpus, string.Empty);

        try
        {
            var run = RunHarness(
                "--benchmark-authored-corpus", corpus,
                "--ratchet-baseline", "/does-not-exist/baseline.jsonl");

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                "Ratchet baseline not found: /does-not-exist/baseline.jsonl",
                run.Output,
                StringComparison.Ordinal);
        }

        finally
        {
            File.Delete(corpus);
        }
    }

    [Fact]
    public void Harness_ForwardsTheSourceOracleManifestToTheBenchmark()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string corpus = AuthoredCorpusTestData.WriteCorrelatedCorpus(assembly);

        try
        {
            var run = RunHarness(
                "--benchmark-authored-corpus", corpus,
                "--source-oracle-manifest", "/does-not-exist/source-oracles.json");

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                "Source-oracle manifest not found: /does-not-exist/source-oracles.json",
                run.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corpus);
        }
    }

    /// <summary>
    /// A requested <c>--integrity-only</c> must reach the benchmark, and an unrequested
    /// one must not be invented.
    ///
    /// <para>This is the scheduled lane's whole contract. Dropping the argument at the
    /// call site left the suite green in review round ten; the lane would then have
    /// selected the historical <c>invalid == 0</c> contract, which its corpus cannot
    /// satisfy, and gone red every week for a reason no one could read off the exit
    /// code — the failure #3245 is about, reintroduced by the fix for it.</para>
    ///
    /// <para>Both runs are asserted because the flag only changes which contract is
    /// <em>reported</em>: the exit codes here are identical, so an exit-code check would
    /// see nothing. The corpus is a single real harvested row, and the claim does not
    /// depend on how that row decompiles — corrupting its token, method, type, or
    /// checksum still reaches the verdict and still names the contract applied, so
    /// ordinary drift in <c>ILInspector.CSharp</c> cannot rot this into a false green.
    /// This asserts the plumbing, not the quality.</para>
    ///
    /// <para>One drift does end the run early: renaming the assembly the row was
    /// harvested from, after which nothing in the pool matches it and the harness
    /// measures nothing. That is asserted separately rather than left to surface as an
    /// unexplained absence of <c>[integrity-only]</c>, because the negative assertion
    /// below would otherwise be satisfied by a run that never got started — this PR's
    /// characteristic defect, in its own test.</para>
    /// </summary>
    [Fact]
    public void Harness_ForwardsIntegrityOnlyToTheBenchmark()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string corpus = AuthoredCorpusTestData.WriteCorrelatedCorpus(assembly);

        try
        {
            var requested = RunHarness(assembly, "--benchmark-authored-corpus", corpus, "--integrity-only");
            AssertTheBenchmarkActuallyRan(requested);
            Assert.Contains("[integrity-only]", requested.Output, StringComparison.Ordinal);

            var notRequested = RunHarness(assembly, "--benchmark-authored-corpus", corpus);
            AssertTheBenchmarkActuallyRan(notRequested);
            Assert.DoesNotContain("[integrity-only]", notRequested.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corpus);
        }
    }

    /// <summary>
    /// The run reached a verdict, so an assertion about which contract it reported is
    /// about the contract and not about the run having died first.
    ///
    /// <para>The exit code is checked because this is the only place a protected gate
    /// runs to completion, which makes it the only test that can catch
    /// <see cref="AuthoredCorpusExitContract.GateExitedWithoutRunning"/> firing on a
    /// healthy run. That check reads a flag the gate sets at its own dispatch site;
    /// deleting the flag turns every real gate run into a failure, and until this
    /// assertion existed the suite stayed green while it did. A guard against a silent
    /// success is worth little if it can start rejecting the successes that are real.</para>
    /// </summary>
    static void AssertTheBenchmarkActuallyRan(HarnessRun run, int expectedExitCode = 0)
    {
        AssertTheBenchmarkDidNotDieEarly(run);
        Assert.Contains("targets evaluated", run.Output, StringComparison.Ordinal);
        Assert.Equal(expectedExitCode, run.ExitCode);
    }

    /// <summary>
    /// The shared half of the check above, for the JSON path, which reports a document
    /// rather than a card and so has no "targets evaluated" line to look for.
    /// </summary>
    static void AssertTheBenchmarkDidNotDieEarly(HarnessRun run)
    {
        Assert.DoesNotContain("measured nothing", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The verify gate must dispatch to the drift check.
    ///
    /// <para>Inverting its dispatch condition left the suite green in review round ten:
    /// the flag was accepted, every refusal rule applied to it correctly, and then
    /// nothing ran it. Reaching the drift check's own complaint about an unparseable
    /// corpus is what proves it was entered.</para>
    /// </summary>
    [Fact]
    public void Harness_DispatchesTheStandaloneVerifyGate()
    {
        string corpus = Path.Combine(Path.GetTempPath(), $"verify-dispatch-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(corpus, string.Empty);

        try
        {
            var run = RunHarness("--verify-authored-corpus", corpus);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                $"Corpus is empty or unparseable: {corpus}",
                run.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corpus);
        }
    }

    /// <summary>
    /// A mode that returns before a requested gate makes the harness fail rather than
    /// report a success it did not measure.
    ///
    /// <para>This is the round-twelve finding, and it is the only test that reaches the
    /// check. Every list-based defense in this area — the declared modes, the dispatch
    /// order, the refusal — was defeated at least once by something absent from the list
    /// doing the preempting, most recently by a mode hand-edited into the harness's
    /// <c>if</c>-cascade and left out of both lists. The check under test names nothing
    /// and asks only whether the requested gate ran, so this test injects the defect
    /// rather than one particular route to it.</para>
    ///
    /// <para>The injection is real harness code
    /// (<see cref="AuthoredCorpusExitContract.SimulatePreemptionVariable"/>) because no ordinary invocation
    /// reaches the check: it is live only when the harness is broken. Without this test
    /// the wiring in <c>Main</c> could be deleted and nothing would go red.</para>
    /// </summary>
    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_FailsWhenARequestedGateNeverRan(string gate)
    {
        var run = RunHarness(
            (AuthoredCorpusExitContract.SimulatePreemptionVariable, "1"),
            gate,
            "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same preempting mode is not an error when no gate was requested.
    ///
    /// <para>Without this, the test above would pass just as well against a harness that
    /// failed on the environment variable alone, and it would be asserting the injection
    /// rather than the property. A mode returning 0 is the harness working normally; only
    /// a mode returning 0 <em>while a gate was asked for</em> is the defect.</para>
    /// </summary>
    [Fact]
    [Trait("Area", "Corpus")]
    public void Harness_SucceedsWhenAModeReturnsFirstAndNoGateWasRequested()
    {
        var run = RunHarness((AuthoredCorpusExitContract.SimulatePreemptionVariable, "1"), "--history-card");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every way the benchmark can decline to measure exits non-zero.
    ///
    /// <para>This is the gate for an assumption
    /// <see cref="AuthoredCorpusExitContract.GateExitedWithoutRunning"/> depends on and
    /// cannot check for itself. That check reads a flag set at the gate's dispatch site,
    /// so it learns that the gate was <em>entered</em>, not that it measured anything. The
    /// distinction is harmless only while every path that enters the benchmark and
    /// measures nothing exits non-zero, because the check deliberately ignores non-zero
    /// exits. A future early <c>return 0</c> would be a gate reporting success without
    /// measuring — #3245 again — and nothing else here would notice.</para>
    ///
    /// <para>Each case names the message that identifies the path, so a run that failed
    /// somewhere else cannot be mistaken for the path under test.</para>
    /// </summary>
    [Fact]
    [Trait("Area", "Corpus")]
    public void Benchmark_ExitsNonZeroOnEveryPathThatMeasuresNothing()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"empty-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(empty, string.Empty);
        string realCorpus = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools", "DecompilerHarness", "corpus", "one-row-authored-corpus.jsonl");

        try
        {
            (string Message, string[] Invocation)[] cases =
            [
                ("Corpus file not found",
                    ["--benchmark-authored-corpus", "/does-not-exist.jsonl"]),
                ("Ratchet baseline not found",
                    ["--benchmark-authored-corpus", empty, "--ratchet-baseline", "/does-not-exist.json"]),
                ("Corpus is empty or unparseable",
                    ["--benchmark-authored-corpus", empty]),
                ("the run measured nothing",
                    [typeof(Xunit.FactAttribute).Assembly.Location, "--benchmark-authored-corpus", realCorpus]),
            ];

            foreach ((string message, string[] invocation) in cases)
            {
                var run = RunHarness(invocation);

                Assert.Contains(message, run.Output, StringComparison.Ordinal);
                Assert.NotEqual(0, run.ExitCode);
            }
        }
        finally
        {
            File.Delete(empty);
        }
    }

    /// <summary>
    /// A corpus row that cannot be read fails the drift gate rather than being dropped.
    ///
    /// <para>Review round thirteen demonstrated the opposite: <c>ReadCorpus</c> logged
    /// malformed rows and discarded them uncounted, so one valid row beside one line of
    /// invalid JSON reported <c>corpusRows: 1, verified: 1, honest: true</c> and exited 0.
    /// A fail-closed gate reported success over a corpus it had silently shortened, which
    /// is #3245 in the gate nobody was looking at.</para>
    ///
    /// <para>Both reporters are checked. They each computed the exit code themselves, in
    /// duplicate, so fixing one and not the other was available and would have looked
    /// fixed. Report-only is checked too: dropping rows misreports how much was read
    /// whether or not the caller asked for a fail-closed run.</para>
    /// </summary>
    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("rows unreadable    : 1", new[] { "--fail-on-drift" })]
    [InlineData("\"malformedRows\": 1", new[] { "--fail-on-drift", "--json" })]
    [InlineData("rows unreadable    : 1", new string[0])]
    public void Drift_FailsWhenACorpusRowCouldNotBeRead(string reported, string[] modifiers)
    {
        string repositoryRoot = AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string mixed = Path.Combine(Path.GetTempPath(), $"mixed-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(
            mixed,
            File.ReadAllText(Path.Combine(
                repositoryRoot, "tools", "DecompilerHarness", "corpus", "one-row-authored-corpus.jsonl"))
            + "{not-json}" + Environment.NewLine);

        try
        {
            var run = RunHarness([
                typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location,
                "--verify-authored-corpus",
                mixed,
                .. modifiers,
            ]);

            // Both markers are printed by the reporters, which only run after the corpus
            // has been read and every matched row evaluated. Reaching one is what
            // distinguishes this from a run that fell over before reading anything.
            Assert.Contains(reported, run.Output, StringComparison.Ordinal);
            Assert.Equal(1, run.ExitCode);
        }
        finally
        {
            File.Delete(mixed);
        }
    }

    /// <summary>
    /// A value-taking flag does not eat a protected gate as its value.
    ///
    /// <para>Review ran <c>--emit-inverse-ledger --benchmark-authored-corpus &lt;corpus&gt;</c>.
    /// The ledger flag took the benchmark flag as its output path, so the benchmark was
    /// never parsed, never requested, and never run; the harness wrote a file literally
    /// named <c>--benchmark-authored-corpus</c> and exited 0. That is #3245 reached
    /// through the parser instead of through dispatch, and the escape check in
    /// <c>Main</c> is blind to it, because a gate that was never parsed was never
    /// requested.</para>
    ///
    /// <para>The flags below are coverage, not the guarantee. The rule lives in one
    /// function and is the next token's own shape, so it holds for value-taking flags
    /// this list does not name -- which matters, because there are twenty-odd of them and
    /// a list of that length is how the earlier attempts to enumerate this parser went
    /// wrong.</para>
    /// </summary>
    [Theory]
    [InlineData("--emit-inverse-ledger")]
    [InlineData("--emit-corpus-baseline")]
    [InlineData("--ratchet-baseline")]
    [InlineData("--history-path")]
    [InlineData("--history-window")]
    [InlineData("--package-version")]
    public void AValueTakingFlagDoesNotSwallowTheGateThatFollows(string valueTakingFlag)
    {
        string repositoryRoot = AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string corpus = Path.Combine(
            repositoryRoot, "tools", "DecompilerHarness", "corpus", "one-row-authored-corpus.jsonl");
        string stray = Path.Combine(Environment.CurrentDirectory, "--benchmark-authored-corpus");

        var run = RunHarness([
            typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location,
            valueTakingFlag,
            "--benchmark-authored-corpus",
            corpus,
        ]);

        // The refusal, not merely a non-zero exit: the run must fail because the gate was
        // about to be eaten, rather than for any of the other reasons a harness run ends
        // badly.
        Assert.Contains("which is another flag", run.Output, StringComparison.Ordinal);
        Assert.NotEqual(0, run.ExitCode);
        Assert.False(File.Exists(stray), $"the gate token was consumed as a path and written to '{stray}'");
    }

    /// <summary>
    /// Neither corpus gate counts one method twice.
    ///
    /// <para>A metadata token is unique within an assembly, so a repeated identity is one
    /// method presented as two measurements. Review duplicated the sole fixture row and
    /// both gates reported two evaluated targets and exited 0. A padded denominator
    /// distorts the ratchet exactly as a shortened one does, and it is the more useful
    /// substitution: swapping a regressed row for a copy of a healthy one holds every
    /// count still. Nothing else pins corpus content -- <c>PoolDigest</c> covers the
    /// assembly pool, not the rows.</para>
    /// </summary>
    [Fact]
    public void NeitherCorpusGateCountsOneMethodTwice()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string row = AuthoredCorpusTestData.CorrelatedRow(assembly);
        string duplicated = Path.Combine(Path.GetTempPath(), $"duplicated-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(duplicated, row + Environment.NewLine + row + Environment.NewLine);

        try
        {
            var benchmark = RunHarness([
                assembly,
                "--benchmark-authored-corpus",
                duplicated,
            ]);
            var drift = RunHarness([
                assembly,
                "--verify-authored-corpus",
                duplicated,
                "--fail-on-drift",
            ]);

            Assert.Contains("counted twice", benchmark.Output, StringComparison.Ordinal);
            Assert.Contains("counted twice", drift.Output, StringComparison.Ordinal);
            Assert.Equal(1, benchmark.ExitCode);
            Assert.Equal(1, drift.ExitCode);
        }
        finally
        {
            File.Delete(duplicated);
        }
    }

    /// <summary>
    /// Both corpus gates reject the same damaged row, because both read the corpus
    /// through one reader.
    ///
    /// <para>They did not. The drift gate had its own reader that counted malformed JSON
    /// but skipped a blank line uncounted and never applied the declared schema, so a
    /// corpus with one row erased to whitespace was rejected by the benchmark and
    /// <em>verified</em> by drift at exit 0 over a silently shortened denominator. The
    /// round-thirteen fix that made drift count unreadable rows closed only the case that
    /// review had demonstrated, and left its twin open.</para>
    ///
    /// <para>This asserts the property rather than the plumbing: whatever a corpus row is
    /// damaged into, neither gate reports success over it. Reintroducing a second reader
    /// fails here as soon as the two disagree, which is the only way the first divergence
    /// could have been caught.</para>
    /// </summary>
    [Theory]
    [InlineData("blank", "   ")]
    [InlineData("not JSON at all", "{not-json}")]
    [InlineData("JSON that supplies no required field", "{}")]
    [InlineData("a row missing one required field", "{\"assembly\":\"a.dll\"}")]
    public void BothCorpusGatesRejectTheSameDamagedRow(string description, string damagedRow)
    {
        Assert.NotNull(description);
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string damaged = Path.Combine(Path.GetTempPath(), $"damaged-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(
            damaged,
            AuthoredCorpusTestData.CorrelatedRow(assembly) + Environment.NewLine
            + damagedRow + Environment.NewLine);

        try
        {
            var drift = RunHarness([
                assembly,
                "--verify-authored-corpus",
                damaged,
                "--fail-on-drift",
            ]);
            var benchmark = RunHarness([
                assembly,
                "--benchmark-authored-corpus",
                damaged,
            ]);

            // Asserted before the exit codes so that a run which died on startup, and so
            // would satisfy any expectation of failure, reads as the harness fault it is
            // rather than as this gate holding.
            Assert.Contains("rows unreadable", drift.Output, StringComparison.Ordinal);
            Assert.Contains("malformed rows", benchmark.Output, StringComparison.Ordinal);

            Assert.Equal(1, drift.ExitCode);
            Assert.Equal(1, benchmark.ExitCode);
        }
        finally
        {
            File.Delete(damaged);
        }
    }

    /// <summary>
    /// The benchmark's JSON path reports the same exit code as its text path.
    ///
    /// <para>Review round thirteen found that nothing asserted it. Both paths end in the
    /// same <c>ExitCode</c> call, but the only process run using <c>--json</c> stopped at
    /// "corpus file not found" and never reached the JSON writer, and the in-process JSON
    /// tests discard the return value. Replacing that final return with <c>return 0</c>
    /// left every JSON assertion intact and the suite green — while making the scheduled
    /// lane, which runs with <c>--json</c>, pass runs with failed integrity.</para>
    /// </summary>
    [Fact]
    [Trait("Area", "Corpus")]
    public void Benchmark_ReportsFailedIntegrityThroughTheJsonPath()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string mixed = Path.Combine(Path.GetTempPath(), $"mixed-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(
            mixed,
            AuthoredCorpusTestData.CorrelatedRow(assembly) + Environment.NewLine
            + "{not-json}" + Environment.NewLine);

        try
        {
            var run = RunHarness(
                assembly,
                "--benchmark-authored-corpus",
                mixed,
                "--json");

            AssertTheBenchmarkDidNotDieEarly(run);
            Assert.Contains("\"malformedRows\": 1", run.Output, StringComparison.Ordinal);
            Assert.Contains("\"inputsComplete\": false", run.Output, StringComparison.Ordinal);
            Assert.Equal(1, run.ExitCode);
        }
        finally
        {
            File.Delete(mixed);
        }
    }

    sealed record HarnessRun(int ExitCode, string Output);

    /// <summary>
    /// The candidate ledger refuses without its baseline rather than ranking against an
    /// empty feature set, which would report every enrolled feature as new coverage.
    /// </summary>
    [Fact]
    public void Harness_RefusesTheCandidateLedgerWithoutItsBaseline()
    {
        var run = RunHarness("--source-oracle-candidates");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "--source-oracle-candidates requires --baseline-source-oracle-report",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The baseline path the caller passed is the one the ledger reads.
    ///
    /// <para>Non-vacuous because the refusal names the exact path: a mode that dispatched
    /// but forwarded a different value, or forwarded nothing, cannot produce this
    /// message. The path is deliberately absent — reaching "not found" proves the flags
    /// were accepted, dispatch reached the ledger, and the value survived the trip.</para>
    /// </summary>
    [Fact]
    public void Harness_ForwardsTheCandidateLedgerBaselinePath()
    {
        const string baseline = "/does-not-exist/candidate-baseline.json";

        var run = RunHarness("--source-oracle-candidates", "--baseline-source-oracle-report", baseline);

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("applies to", run.Output, StringComparison.Ordinal);
        Assert.Contains(
            $"Baseline source-oracle report not found: {baseline}",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The ledger verifies the forwarded baseline rather than trusting it: a source-oracle
    /// manifest supplied in place of a benchmark report is refused, which also proves the
    /// verification runs inside the dispatched mode and not only in-process.
    /// </summary>
    [Fact]
    public void Harness_VerifiesTheForwardedCandidateLedgerBaseline()
    {
        string path = Path.Combine(Path.GetTempPath(), $"candidate-baseline-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              "version": 1,
              "printerComparisonVersion": 1,
              "syntaxInventoryVersion": 1,
              "files": []
            }
            """);

        try
        {
            var run = RunHarness("--source-oracle-candidates", "--baseline-source-oracle-report", path);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                "is not a current benchmark report",
                run.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Harness_SourceCorrespondenceCensusPopulatesPdbSource()
    {
        string repositoryRoot = AuthoredCorpusRatchetTests.FindRepositoryRoot();

        var run = RunHarness(
            "--source-correspondence-census",
            "--json",
            "--cap",
            "1",
            "--repo",
            repositoryRoot,
            HarnessBinary());

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\"source_acquisition\": {", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"not_attempted\": 0", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"complete\": 1", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"source_acquisition\": \"complete\"", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "assembly is not registered in FixtureCatalog",
            run.Output,
            StringComparison.Ordinal);
    }

    static HarnessRun RunHarness(params string[] arguments)
        => RunHarness(environment: null, arguments);

    static HarnessRun RunHarness(
        (string Name, string Value)? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // The harness resolves the committed run-history store relative to the
            // repository root, so run it from there rather than from the test host's
            // output directory.
            WorkingDirectory = AuthoredCorpusRatchetTests.FindRepositoryRoot(),
        };

        if (environment is { } variable)
            startInfo.Environment[variable.Name] = variable.Value;

        startInfo.ArgumentList.Add(HarnessBinary());
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;

        // Read both streams before waiting: the harness writes its refusals to stderr and
        // its reports to stdout, and a full pipe buffer on either would deadlock.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        Assert.True(
            process.WaitForExit(milliseconds: 120_000),
            "The harness did not exit within two minutes.");

        string output = standardOutput.GetAwaiter().GetResult()
            + standardError.GetAwaiter().GetResult();

        // A crash otherwise surfaces as an exit code nobody expected, and the reader has
        // to go find the process output to learn why. Every assertion below this point is
        // about what the harness decided, so a harness that never decided anything should
        // say so in its own words.
        if (output.Contains("Unhandled exception", StringComparison.Ordinal))
            Assert.Fail($"The harness crashed instead of deciding:{Environment.NewLine}{output}");

        return new HarnessRun(process.ExitCode, output);
    }

    static string DotnetHost()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string path = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(path))
                return path;
        }

        return "dotnet";
    }

    /// <summary>
    /// The built harness under test.
    ///
    /// <para>A missing binary fails rather than skips. These tests are the only ones that
    /// can see <c>Program.cs</c>, so quietly skipping them would restore precisely the
    /// blind spot they exist to remove — and "skipped" reading as "fine" is the shape of
    /// defect this whole PR is about. The test project takes a
    /// <c>ReferenceOutputAssembly="false"</c> project reference on the harness so the
    /// binary is built, and rebuilt, whenever these tests are.</para>
    /// </summary>
    static string HarnessBinary()
    {
        string path = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools", "DecompilerHarness", "bin", "Release", "net11.0", "decompiler-harness.dll");

        Assert.True(
            File.Exists(path),
            $"The harness binary is missing: {path}. Build it with "
                + "`dotnet build tools/DecompilerHarness -c Release`.");

        return path;
    }
}
