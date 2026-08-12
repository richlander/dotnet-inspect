using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler.Tests;

public class AnnotatedSourceComparisonTests
{
    [Fact]
    public void CompareReportsKindChangeInsideCaseAndRendersBothViews()
    {
        const string beforeText = """
            void M(int value)
            {
                switch (value)
                {
                    case 0:
                        return;
                }
            }
            """;
        const string afterText = """
            void M(int value)
            {
                switch (value)
                {
                    case 0:
                        break;
                }
            }
            """;
        var before = Document(
            beforeText,
            [Node(0, "ReturnStatement", beforeText, "return;")],
            [CaseRegion(beforeText, "case 0:", "return;")]);
        var after = Document(
            afterText,
            [Node(0, "BreakStatement", afterText, "break;")],
            [CaseRegion(afterText, "case 0:", "break;")]);

        var result = AnnotatedSourceComparer.Compare(before, after);

        var change = Assert.Single(result.Changes);
        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("ReturnStatement", change.Before!.Kind);
        Assert.Equal("BreakStatement", change.After!.Kind);
        Assert.Equal("Case", change.Before.RegionPath);
        Assert.Empty(change.Evidence);

        string markdown = AnnotatedSourceComparisonRenderer.RenderMarkdown(result);
        Assert.Contains("return;", markdown);
        Assert.Contains("break;", markdown);
        Assert.Contains("ReturnStatement -> BreakStatement [Case]", markdown);
        Assert.Contains("| Changed | ReturnStatement | BreakStatement | Case |", markdown);
        Assert.DoesNotContain("IL fidelity", markdown);
    }

    [Fact]
    public void RendererStacksMultipleChangesOnOneLine()
    {
        const string beforeText = "void M() { return; new C(); }";
        const string afterText = "void M() { break; C.Create(); }";
        var before = Document(
            beforeText,
            [
                Node(0, "ReturnStatement", beforeText, "return;"),
                Node(1, "ObjectCreationExpression", beforeText, "new C()"),
            ]);
        var after = Document(
            afterText,
            [
                Node(0, "BreakStatement", afterText, "break;"),
                Node(1, "InvocationExpression", afterText, "C.Create()"),
            ]);

        var result = AnnotatedSourceComparer.Compare(before, after);
        string rendered = AnnotatedSourceComparisonRenderer.RenderBefore(result);

        Assert.Equal(2, result.Changes.Length);
        Assert.Contains("1.", rendered);
        Assert.Contains("2.", rendered);
        Assert.Contains("ReturnStatement -> BreakStatement", rendered);
        Assert.Contains("ObjectCreationExpression -> InvocationExpression", rendered);
    }

    [Fact]
    public void RendererProjectsEveryPieceOfMultiSpanNode()
    {
        const string beforeText = """
            void M()
            {
                if (ready)
                    return;
            }
            """;
        const string afterText = """
            void M()
            {
                while (ready)
                    break;
            }
            """;
        var before = Document(beforeText,
            [Node(0, "IfStatement", beforeText, "if", "return;")]);
        var after = Document(afterText,
            [Node(0, "WhileStatement", afterText, "while", "break;")]);

        var result = AnnotatedSourceComparer.Compare(before, after);
        string rendered = AnnotatedSourceComparisonRenderer.RenderBefore(result);

        Assert.Equal(2, rendered.Split('\n').Count(line => line.Contains('^')));
        Assert.Equal(2, Assert.Single(result.Changes).Before!.Spans.Length);
    }

    [Fact]
    public void TextMapUsesAbsoluteUtf16Coordinates()
    {
        const string text = "a😀b\r\nreturn;";
        var map = new AnnotatedSourceTextMap(text);

        var emoji = Assert.Single(map.Project(new AnnotatedSourceSpan(1, 2)));
        var statement = Assert.Single(map.Project(
            new AnnotatedSourceSpan(text.IndexOf("return;", StringComparison.Ordinal), "return;".Length)));

        Assert.Equal(new AnnotatedSourceLineSpan(0, 1, 2), emoji);
        Assert.Equal(new AnnotatedSourceLineSpan(1, 0, 7), statement);
    }

    [Fact]
    public void CompareReportsMovedUnknownFutureKind()
    {
        const string beforeText = "alpha\nbeta";
        const string afterText = "beta\nalpha";
        var before = Document(
            beforeText,
            [
                Node(0, "FutureShape", beforeText, "alpha"),
                Node(1, "InvocationExpression", beforeText, "beta"),
            ]);
        var after = Document(
            afterText,
            [
                Node(0, "InvocationExpression", afterText, "beta"),
                Node(1, "FutureShape", afterText, "alpha"),
            ]);

        var result = AnnotatedSourceComparer.Compare(before, after);

        var move = Assert.Single(result.Changes);
        Assert.Equal(AnnotatedSourceChangeKind.Moved, move.Kind);
        Assert.Equal("FutureShape", move.Before!.Kind);
        Assert.Contains("FutureShape", AnnotatedSourceComparisonRenderer.RenderRichDiff(result));
    }

    [Fact]
    public void IdenticalDocumentsHaveNoStructuralChanges()
    {
        const string text = "void M() { return; }";
        var document = Document(
            text,
            [Node(0, "ReturnStatement", text, "return;")]);

        var result = AnnotatedSourceComparer.Compare(document, document);

        Assert.Empty(result.Changes);
        Assert.Equal("No structural changes.", AnnotatedSourceComparisonRenderer.RenderRichDiff(result));
    }

    [Fact]
    public void CompareReportsAddedAndRemovedNodes()
    {
        const string text = "return;";
        var empty = Document(text, []);
        var populated = Document(text, [Node(0, "ReturnStatement", text, text)]);

        var added = AnnotatedSourceComparer.Compare(empty, populated);
        var removed = AnnotatedSourceComparer.Compare(populated, empty);

        Assert.Equal(AnnotatedSourceChangeKind.Added, Assert.Single(added.Changes).Kind);
        Assert.Equal(AnnotatedSourceChangeKind.Removed, Assert.Single(removed.Changes).Kind);
    }

    [Fact]
    public void CompareReportsChangedTextForTheSameKind()
    {
        const string beforeText = "return first;";
        const string afterText = "return second;";
        var before = Document(
            beforeText,
            [Node(0, "ReturnStatement", beforeText, beforeText)]);
        var after = Document(
            afterText,
            [Node(0, "ReturnStatement", afterText, afterText)]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("return first;", change.Before!.SelectedText);
        Assert.Equal("return second;", change.After!.SelectedText);
    }

    [Fact]
    public void CompareUsesRegionContextToDisambiguateDuplicateNodes()
    {
        const string beforeText = "if (ready) { return; } else { return; }";
        const string afterText = "if (ready) { } else { return; }";
        int first = beforeText.IndexOf("return;", StringComparison.Ordinal);
        int second = beforeText.LastIndexOf("return;", StringComparison.Ordinal);
        int surviving = afterText.IndexOf("return;", StringComparison.Ordinal);
        var before = Document(
            beforeText,
            [
                NodeAt(0, "ReturnStatement", first, "return;".Length),
                NodeAt(1, "ReturnStatement", second, "return;".Length),
            ],
            [
                Region(PrintedRegionRole.Body, first, "return;".Length),
                Region(PrintedRegionRole.Else, second, "return;".Length),
            ]);
        var after = Document(
            afterText,
            [NodeAt(0, "ReturnStatement", surviving, "return;".Length)],
            [Region(PrintedRegionRole.Else, surviving, "return;".Length)]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Removed, change.Kind);
        Assert.Equal(first, change.Before!.Spans[0].Start);
        Assert.Equal("Body", change.Before.RegionPath);
    }

    [Fact]
    public void CompareReportsRegionOnlyChange()
    {
        const string text = "return;";
        var before = Document(
            text,
            [NodeAt(0, "ReturnStatement", 0, text.Length)],
            [Region(PrintedRegionRole.Body, 0, text.Length)]);
        var after = Document(
            text,
            [NodeAt(0, "ReturnStatement", 0, text.Length)],
            [Region(PrintedRegionRole.Else, 0, text.Length)]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("Body", change.Before!.RegionPath);
        Assert.Equal("Else", change.After!.RegionPath);
    }

    [Fact]
    public void CompareUsesRegionTextToDisambiguateRepeatedRegionRoles()
    {
        const string beforeText = "case 0:\n    return;\ncase 1:\n    return;";
        const string afterText = "case 1:\n    return;";
        int first = beforeText.IndexOf("return;", StringComparison.Ordinal);
        int second = beforeText.LastIndexOf("return;", StringComparison.Ordinal);
        int surviving = afterText.IndexOf("return;", StringComparison.Ordinal);
        var before = Document(
            beforeText,
            [
                NodeAt(0, "ReturnStatement", first, "return;".Length),
                NodeAt(1, "ReturnStatement", second, "return;".Length),
            ],
            [
                Region(
                    PrintedRegionRole.Case,
                    beforeText.IndexOf("case 0:", StringComparison.Ordinal),
                    first + "return;".Length),
                Region(
                    PrintedRegionRole.Case,
                    beforeText.IndexOf("case 1:", StringComparison.Ordinal),
                    second + "return;".Length
                        - beforeText.IndexOf("case 1:", StringComparison.Ordinal)),
            ]);
        var after = Document(
            afterText,
            [NodeAt(0, "ReturnStatement", surviving, "return;".Length)],
            [Region(PrintedRegionRole.Case, 0, afterText.Length)]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Removed, change.Kind);
        Assert.Equal(first, change.Before!.Spans[0].Start);
    }

    [Fact]
    public void RegionDisambiguationDoesNotChangeUneditedDuplicateNodes()
    {
        const string beforeText = "case 0:\n    Log();\n    return;\ncase 1:\n    return;";
        const string afterText = "case 0:\n    return;\ncase 1:\n    return;";
        int log = beforeText.IndexOf("Log();", StringComparison.Ordinal);
        int firstBefore = beforeText.IndexOf("return;", StringComparison.Ordinal);
        int secondBefore = beforeText.LastIndexOf("return;", StringComparison.Ordinal);
        int firstAfter = afterText.IndexOf("return;", StringComparison.Ordinal);
        int secondAfter = afterText.LastIndexOf("return;", StringComparison.Ordinal);
        var before = Document(
            beforeText,
            [
                NodeAt(0, "ExpressionStatement", log, "Log();".Length),
                NodeAt(1, "ReturnStatement", firstBefore, "return;".Length),
                NodeAt(2, "ReturnStatement", secondBefore, "return;".Length),
            ],
            [
                Region(PrintedRegionRole.Case, 0, firstBefore + "return;".Length),
                Region(
                    PrintedRegionRole.Case,
                    beforeText.IndexOf("case 1:", StringComparison.Ordinal),
                    beforeText.Length - beforeText.IndexOf("case 1:", StringComparison.Ordinal)),
            ]);
        var after = Document(
            afterText,
            [
                NodeAt(0, "ReturnStatement", firstAfter, "return;".Length),
                NodeAt(1, "ReturnStatement", secondAfter, "return;".Length),
            ],
            [
                Region(PrintedRegionRole.Case, 0, firstAfter + "return;".Length),
                Region(
                    PrintedRegionRole.Case,
                    afterText.IndexOf("case 1:", StringComparison.Ordinal),
                    afterText.Length - afterText.IndexOf("case 1:", StringComparison.Ordinal)),
            ]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Removed, change.Kind);
        Assert.Equal("ExpressionStatement", change.Before!.Kind);
    }

    [Fact]
    public void CompareReportsContextChangeBetweenRepeatedRoleRegions()
    {
        const string beforeText = "case 0:\n    return;\ncase 1:\n";
        const string afterText = "case 0:\ncase 1:\n    return;";
        int beforeReturn = beforeText.IndexOf("return;", StringComparison.Ordinal);
        int afterReturn = afterText.IndexOf("return;", StringComparison.Ordinal);
        int beforeCase1 = beforeText.IndexOf("case 1:", StringComparison.Ordinal);
        int afterCase1 = afterText.IndexOf("case 1:", StringComparison.Ordinal);
        var before = Document(
            beforeText,
            [NodeAt(0, "ReturnStatement", beforeReturn, "return;".Length)],
            [
                Region(PrintedRegionRole.Case, 0, beforeReturn + "return;".Length),
                Region(PrintedRegionRole.Case, beforeCase1, "case 1:\n".Length),
            ]);
        var after = Document(
            afterText,
            [NodeAt(0, "ReturnStatement", afterReturn, "return;".Length)],
            [
                Region(PrintedRegionRole.Case, 0, "case 0:\n".Length),
                Region(
                    PrintedRegionRole.Case,
                    afterCase1,
                    afterText.Length - afterCase1),
            ]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("Case", change.Before!.RegionPath);
        Assert.Equal("Case", change.After!.RegionPath);
    }

    [Fact]
    public void RepeatedRoleDeletionStaysCorrectWhenSiblingRegionChanges()
    {
        const string beforeText = """
            case 0:
                return;
            case 1:
                return;
            case 2:
                return;
            """;
        const string afterText = """
            case 0:
                Log();
                return;
            case 2:
                return;
            """;
        var before = Cases(beforeText, includeLog: false, 0, 1, 2);
        var after = Cases(afterText, includeLog: true, 0, 2);
        int deleted = NthIndexOf(beforeText, "return;", 1);

        var changes = AnnotatedSourceComparer.Compare(before, after).Changes;

        Assert.Equal(2, changes.Length);
        var added = Assert.Single(changes, change => change.Kind == AnnotatedSourceChangeKind.Added);
        Assert.Equal("ExpressionStatement", added.After!.Kind);
        var removed = Assert.Single(changes, change => change.Kind == AnnotatedSourceChangeKind.Removed);
        Assert.Equal(deleted, removed.Before!.Spans[0].Start);
    }

    [Fact]
    public void IdenticalRepeatedRegionsStayPairedWhenOneSiblingChanges()
    {
        const string beforeText = """
            if (a)
            {
                return;
            }
            if (b)
            {
                return;
            }
            """;
        const string afterText = """
            if (a)
            {
                Log();
                return;
            }
            if (b)
            {
                return;
            }
            """;
        int beforeFirst = NthIndexOf(beforeText, "return;", 0);
        int beforeSecond = NthIndexOf(beforeText, "return;", 1);
        int afterLog = afterText.IndexOf("Log();", StringComparison.Ordinal);
        int afterFirst = NthIndexOf(afterText, "return;", 0);
        int afterSecond = NthIndexOf(afterText, "return;", 1);
        var before = Document(
            beforeText,
            [
                NodeAt(0, "ReturnStatement", beforeFirst, "return;".Length),
                NodeAt(1, "ReturnStatement", beforeSecond, "return;".Length),
            ],
            [
                BracedBody(beforeText, beforeFirst),
                BracedBody(beforeText, beforeSecond),
            ]);
        var after = Document(
            afterText,
            [
                NodeAt(0, "ExpressionStatement", afterLog, "Log();".Length),
                NodeAt(1, "ReturnStatement", afterFirst, "return;".Length),
                NodeAt(2, "ReturnStatement", afterSecond, "return;".Length),
            ],
            [
                BracedBody(afterText, afterFirst),
                BracedBody(afterText, afterSecond),
            ]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Added, change.Kind);
        Assert.Equal("ExpressionStatement", change.After!.Kind);
    }

    [Fact]
    public void InsertedRepeatedRegionDoesNotDisplaceSurvivingNodes()
    {
        const string beforeText = """
            {
                return;
            }
            {
                Log();
            }
            """;
        const string afterText = """
            {
                New();
            }
            {
                // note
                return;
            }
            {
                Log();
            }
            """;
        int beforeReturn = beforeText.IndexOf("return;", StringComparison.Ordinal);
        int beforeLog = beforeText.IndexOf("Log();", StringComparison.Ordinal);
        int afterNew = afterText.IndexOf("New();", StringComparison.Ordinal);
        int afterReturn = afterText.IndexOf("return;", StringComparison.Ordinal);
        int afterLog = afterText.IndexOf("Log();", StringComparison.Ordinal);
        var before = Document(
            beforeText,
            [
                NodeAt(0, "ReturnStatement", beforeReturn, "return;".Length),
                NodeAt(1, "ExpressionStatement", beforeLog, "Log();".Length),
            ],
            [
                BracedBody(beforeText, beforeReturn),
                BracedBody(beforeText, beforeLog),
            ]);
        var after = Document(
            afterText,
            [
                NodeAt(0, "ExpressionStatement", afterNew, "New();".Length),
                NodeAt(1, "ReturnStatement", afterReturn, "return;".Length),
                NodeAt(2, "ExpressionStatement", afterLog, "Log();".Length),
            ],
            [
                BracedBody(afterText, afterNew),
                BracedBody(afterText, afterReturn),
                BracedBody(afterText, afterLog),
            ]);

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Added, change.Kind);
        Assert.Equal("New();", change.After!.SelectedText);
    }

    [Fact]
    public void InsertedNestedConstructDoesNotChangeSurvivingBodyNodes()
    {
        const string beforeText = """
            if (c0)
            {
                A();
            }
            if (c1)
            {
                B();
            }
            """;
        const string afterText = """
            if (c0)
            {
                New();
            }
            if (c1)
            {
                // note
                A();
            }
            if (c2)
            {
                B();
            }
            """;
        var before = NestedBodies(beforeText, "A();", "B();");
        var after = NestedBodies(afterText, "New();", "A();", "B();");

        var change = Assert.Single(AnnotatedSourceComparer.Compare(before, after).Changes);

        Assert.Equal(AnnotatedSourceChangeKind.Added, change.Kind);
        Assert.Equal("New();", change.After!.SelectedText);
    }

    [Fact]
    public void CompareProjectsInterleavedDocumentsToCSharp()
    {
        var before = InterleavedDocument("return;", "IL_0000: ret", "ReturnStatement");
        var after = InterleavedDocument("break;", "IL_0000: br", "BreakStatement");

        var result = AnnotatedSourceComparer.Compare(before, after);

        var change = Assert.Single(result.Changes);
        Assert.Equal(AnnotatedSourceChangeKind.Changed, change.Kind);
        Assert.Equal("return;", result.Before.Text);
        Assert.Equal("break;", result.After.Text);
        Assert.All(result.Before.Nodes, node => Assert.Equal(SourceLineKind.CSharp, node.Medium));
        Assert.Single(result.Before.Facts);
        Assert.Equal(new AnnotatedSourceTarget(0, 0), Assert.Single(result.Before.Targets));
    }

    [Fact]
    public void CompareRejectsAnIlNodeThatDoesNotOwnItsWholeLine()
    {
        const string text = "return;\nIL_0000: ret and unclassified text";
        var document = new AnnotatedSourceDocument(
            text,
            [
                NodeAt(0, "ReturnStatement", 0, "return;".Length),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan("return;\n".Length, "IL_0000: ret".Length)],
                    IlOffset: 0),
            ],
            [],
            [],
            []);

        var exception = Assert.Throws<ArgumentException>(
            () => AnnotatedSourceComparer.Compare(document, document));

        Assert.Contains("unclassified", exception.Message);
    }

    static AnnotatedSourceDocument Document(
        string text,
        IReadOnlyList<AnnotatedSourceNode> nodes,
        IReadOnlyList<AnnotatedSourceRegion>? regions = null)
        => new(text, nodes, regions ?? [], [], []);

    static AnnotatedSourceNode Node(
        int id,
        string kind,
        string text,
        params string[] selections)
        => new(
            id,
            kind,
            SourceLineKind.CSharp,
            [.. selections.Select(selection =>
            {
                int start = text.IndexOf(selection, StringComparison.Ordinal);
                Assert.True(start >= 0, $"Selection '{selection}' was not found.");
                return new AnnotatedSourceSpan(start, selection.Length);
            })]);

    static AnnotatedSourceNode NodeAt(int id, string kind, int start, int length)
        => new(
            id,
            kind,
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(start, length)]);

    static AnnotatedSourceRegion Region(
        PrintedRegionRole role,
        int start,
        int length)
        => new(role, [new AnnotatedSourceSpan(start, length)]);

    static AnnotatedSourceDocument InterleavedDocument(
        string csharp,
        string il,
        string kind)
    {
        string text = $"{csharp}\n{il}";
        var fact = new AnnotatedSourceFact(
            0,
            "test.fact",
            "Test",
            AnnotationConditionality.Always,
            Detail: null,
            SourceOffset: 0,
            AnnotatedSourceFactOrigin.Body);
        var ilOnlyFact = fact with { Id = 1, Descriptor = "test.il-only" };
        return new AnnotatedSourceDocument(
            text,
            [
                NodeAt(0, kind, 0, csharp.Length),
                new AnnotatedSourceNode(
                    1,
                    AnnotatedSourceNode.InstructionKind,
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(csharp.Length + 1, il.Length)],
                    IlOffset: 0),
            ],
            [],
            [fact, ilOnlyFact],
            [
                new AnnotatedSourceTarget(0, 0),
                new AnnotatedSourceTarget(0, 1),
                new AnnotatedSourceTarget(1, 1),
            ]);
    }

    static AnnotatedSourceDocument Cases(
        string text,
        bool includeLog,
        params int[] labels)
    {
        var nodes = new List<AnnotatedSourceNode>();
        if (includeLog)
        {
            int log = text.IndexOf("Log();", StringComparison.Ordinal);
            nodes.Add(NodeAt(nodes.Count, "ExpressionStatement", log, "Log();".Length));
        }
        for (int index = 0; index < labels.Length; index++)
        {
            int statement = NthIndexOf(text, "return;", index);
            nodes.Add(NodeAt(nodes.Count, "ReturnStatement", statement, "return;".Length));
        }

        var regions = new List<AnnotatedSourceRegion>();
        for (int index = 0; index < labels.Length; index++)
        {
            int start = text.IndexOf($"case {labels[index]}:", StringComparison.Ordinal);
            int end = index + 1 < labels.Length
                ? text.IndexOf($"case {labels[index + 1]}:", StringComparison.Ordinal)
                : text.Length;
            regions.Add(Region(PrintedRegionRole.Case, start, end - start));
        }
        return Document(text, nodes, regions);
    }

    static AnnotatedSourceRegion BracedBody(string text, int statement)
    {
        int start = text.LastIndexOf('{', statement);
        int end = text.IndexOf('}', statement);
        Assert.True(start >= 0 && end > start);
        return Region(PrintedRegionRole.Body, start, end - start + 1);
    }

    static AnnotatedSourceDocument NestedBodies(
        string text,
        params string[] statements)
    {
        var nodes = new List<AnnotatedSourceNode>();
        var regions = new List<AnnotatedSourceRegion>();
        foreach (string statementText in statements)
        {
            int statement = text.IndexOf(statementText, StringComparison.Ordinal);
            nodes.Add(NodeAt(
                nodes.Count,
                "ExpressionStatement",
                statement,
                statementText.Length));

            var body = BracedBody(text, statement);
            int constructStart = text.LastIndexOf("if (", statement, StringComparison.Ordinal);
            int constructEnd = body.Spans[0].Start + body.Spans[0].Length;
            regions.Add(Region(
                PrintedRegionRole.Construct,
                constructStart,
                constructEnd - constructStart));
            regions.Add(body);
        }
        return Document(text, nodes, regions);
    }

    static int NthIndexOf(string text, string value, int occurrence)
    {
        int start = 0;
        for (int index = 0; index <= occurrence; index++)
        {
            start = text.IndexOf(value, start, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Occurrence {occurrence} of '{value}' was not found.");
            if (index < occurrence)
                start += value.Length;
        }
        return start;
    }

    static AnnotatedSourceRegion CaseRegion(
        string text,
        string startText,
        string endText)
    {
        int start = text.IndexOf(startText, StringComparison.Ordinal);
        int end = text.IndexOf(endText, StringComparison.Ordinal) + endText.Length;
        return new AnnotatedSourceRegion(
            PrintedRegionRole.Case,
            [new AnnotatedSourceSpan(start, end - start)]);
    }
}
