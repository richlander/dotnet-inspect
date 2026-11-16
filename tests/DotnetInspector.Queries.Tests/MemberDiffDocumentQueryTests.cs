using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.Research;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// The Public API Member diff document over a real API comparison
/// (docs/design/member-diff.md#acceptance-scenarios).
/// </summary>
public sealed class MemberDiffDocumentQueryTests
{
    const string Before = """
        using System;
        using System.ComponentModel;
        namespace Example.Shapes { public class Point { } }
        namespace Example
        {
            public class Widget
            {
                public void Run(int count) { }
                public string Name => "";
                public virtual void Reset() { }
                public int Size { get; }
                [Category("old")]
                [Description("text")]
                public void Tagged() { }
                public Shapes.Point Origin() => null!;
                public void Optional(int value = 1) { }
                public void Retire() { }
                public class Inner { public void Ping() { } }
            }
            public delegate void Callback(int input);
            public sealed class Marker : Attribute { }
            [return: Marker]
            public delegate bool Predicate();
            public class Mover { public void Shift(int x) { } }
            public class Legacy { public void Old() { } }
        }
        """;

    const string After = """
        using System;
        using System.ComponentModel;
        namespace Example.Shapes { public class Point { } }
        namespace Example.Drawing { public class Point { } }
        namespace Example
        {
            public class Widget
            {
                public void Run(long count) { }
                public string Name => "";
                public void Reset() { }
                public int Size { get; set; }
                [Description("text")]
                [Category("new")]
                public void Tagged() { }
                public Drawing.Point Origin() => null!;
                public void Optional(int value = 2) { }
                [Obsolete("Use Reset.")]
                public void Retire() { }
                public class Inner { public void Ping() { } }
            }
            public delegate void Callback(int input);
            public sealed class Marker : Attribute { }
            [return: Marker]
            public delegate bool Predicate();
            public class Mover { }
            public static class MoverExtensions { public static void Shift(this Mover mover, int x) { } }
            public sealed class Gauge
            {
                public double Read(TimeSpan window) => 0;
                public void Reset() { }
                public int Level => 0;
            }
        }
        """;

    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Fact]
    public async Task ChangedReturnType_CaretsTheChangedTokenAndNotesTheClassification()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Origin");

        Assert.Equal(MemberDiffStatus.Changed, document.Status);
        Assert.Equal(
            [
                (MemberDiffLineKind.Removed, "public Example.Shapes.Point Origin();"),
                (MemberDiffLineKind.Added, "public Example.Drawing.Point Origin();"),
            ],
            document.Lines.Select(static line => (line.Kind, line.Text)));
        MemberDiffCaret caret = Assert.Single(document.Carets);
        Assert.Equal(
            (MemberDiffSideKind.Current, 0, "Drawing", MemberDiffCaretKind.Changed, MemberDiffPartKind.ReturnType, "Shapes"),
            (caret.Side, caret.Line, Slice(document.Current!, caret), caret.Kind, caret.Part, caret.Counterpart));
        MemberDiffChangeNote note = Assert.Single(document.ChangeNotes);
        Assert.Equal(
            (MemberDiffSideKind.Current, 0, ChangeKind.MemberSignatureChanged, ChangeClassification.Breaking),
            (note.Side, note.Line, note.Kind, note.Classification));
        Assert.Equal([MemberDiffSideKind.Current], document.PrintedEnclosure);
        Assert.Equal(
            [
                (MemberDiffEnclosureKind.Namespace, "namespace Example;"),
                (MemberDiffEnclosureKind.Type, "public class Widget"),
            ],
            document.CurrentEnclosure.Select(static value => (value.Kind, value.Text)));
        Assert.Equal(document.BaselineEnclosure[^1].OtherMembers, document.CurrentEnclosure[^1].OtherMembers);
    }

    [Fact]
    public async Task SameSimpleNameAddedInCurrent_SpellsBothSidesFromBothVersions()
    {
        // Alone, each version would print `Point Origin()`, and the change would vanish.
        MemberDiffDocument document = await Single("Example.Widget", "Origin");

        Assert.Equal("public Example.Shapes.Point Origin();", Assert.Single(document.Baseline!.Lines));
        Assert.Equal("public Example.Drawing.Point Origin();", Assert.Single(document.Current!.Lines));
    }

    [Fact]
    public async Task ChangedParameterDefault_CaretsTheValueWithinTheParameter()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Optional");

        MemberDiffCaret caret = Assert.Single(document.Carets);
        Assert.Equal(
            (MemberDiffPartKind.Parameter, "0", "2", "1"),
            (caret.Part, caret.Key, Slice(document.Current!, caret), caret.Counterpart));
    }

    [Fact]
    public async Task ChangedParameterType_IsTheComparisonsRemovalAndAddition()
    {
        // Member correspondence is not this design's: a new parameter type is a new overload.
        MemberDiffDocument[] documents = await All("Example.Widget", "Run");

        Assert.Equal(
            [
                (MemberDiffStatus.Removed, MemberDiffLineKind.Removed, "public void Run(int count);", MemberDiffSideKind.Baseline),
                (MemberDiffStatus.Added, MemberDiffLineKind.Added, "public void Run(long count);", MemberDiffSideKind.Current),
            ],
            documents.Select(static document => (
                document.Status,
                Assert.Single(document.Lines).Kind,
                document.Lines[0].Text,
                Assert.Single(document.ChangeNotes).Side)));
        Assert.All(documents, static document => Assert.Empty(document.Carets));
        Assert.All(documents, static document => Assert.Equal([MemberDiffSideKind.Current], document.PrintedEnclosure));
    }

    [Fact]
    public async Task MemberOfAnAddedType_IsOneSidedWithTheTypeNotedAdded()
    {
        MemberDiffDocument document = await Single("Example.Gauge", "Read");

        Assert.Equal(MemberDiffStatus.Added, document.Status);
        Assert.Null(document.Baseline);
        Assert.Empty(document.Carets);
        Assert.Empty(document.BaselineEnclosure);
        Assert.Equal(
            [(MemberDiffLineKind.Added, "public double Read(TimeSpan window);")],
            document.Lines.Select(static line => (line.Kind, line.Text)));
        MemberDiffEnclosure type = document.CurrentEnclosure[^1];
        Assert.Equal("public sealed class Gauge", type.Text);
        // The constructor, Reset, and Level.
        Assert.Equal(3, type.OtherMembers);
        MemberDiffStatusNote note = Assert.Single(type.Notes);
        Assert.Equal((MemberDiffStatusNoteKind.TypeAdded, "2.0.0"), (note.Kind, note.Version));
    }

    [Fact]
    public async Task UnchangedMember_IsContextNotedUnchangedSince()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Name");

        Assert.Equal(MemberDiffStatus.Unchanged, document.Status);
        Assert.False(document.HasDifference);
        Assert.Equal(
            [(MemberDiffLineKind.Context, "public string Name { get; }")],
            document.Lines.Select(static line => (line.Kind, line.Text)));
        MemberDiffLineStatusNote note = Assert.Single(document.StatusNotes);
        Assert.Equal(
            (MemberDiffSideKind.Current, 0, MemberDiffStatusNoteKind.UnchangedSince, "1.0.0"),
            (note.Side, note.Line, note.Note.Kind, note.Note.Version));
        Assert.Empty(document.Carets);
        Assert.Empty(document.ChangeNotes);
    }

    [Fact]
    public async Task ReorderedAttributes_PairByTypeAndCaretOnlyTheChangedArgument()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Tagged");

        MemberDiffCaret caret = Assert.Single(document.Carets);
        Assert.Equal(
            (MemberDiffCaretKind.Changed, MemberDiffPartKind.Attribute, "System.ComponentModel.CategoryAttribute", "\"new\"", "\"old\""),
            (caret.Kind, caret.Part, caret.Key, Slice(document.Current!, caret), caret.Counterpart));
        Assert.Contains(
            document.Lines,
            static line => line.Kind == MemberDiffLineKind.Context && line.Text.Contains("Description", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddedObsolete_IsAnAttributeCaretWithoutAClassificationNote()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Retire");

        Assert.Equal(MemberDiffStatus.Changed, document.Status);
        MemberDiffCaret caret = Assert.Single(document.Carets);
        Assert.Equal(
            (MemberDiffCaretKind.Added, "System.ObsoleteAttribute", "[Obsolete(\"Use Reset.\")]"),
            (caret.Kind, caret.Key, Slice(document.Current!, caret)));
    }

    [Fact]
    public async Task RemovedModifierAndAddedAccessor_EachCaretTheirPart()
    {
        MemberDiffDocument reset = await Single("Example.Widget", "Reset");
        MemberDiffCaret removed = Assert.Single(reset.Carets);
        Assert.Equal(
            (MemberDiffSideKind.Baseline, MemberDiffCaretKind.Removed, MemberDiffPartKind.Modifier, "virtual"),
            (removed.Side, removed.Kind, removed.Part, Slice(reset.Baseline!, removed)));

        MemberDiffDocument size = await Single("Example.Widget", "Size");
        MemberDiffCaret added = Assert.Single(size.Carets);
        Assert.Equal(
            (MemberDiffSideKind.Current, MemberDiffCaretKind.Added, MemberDiffPartKind.Accessor, "set;"),
            (added.Side, added.Kind, added.Part, Slice(size.Current!, added)));
    }

    [Fact]
    public async Task ContainerOutsideTheSurface_IsRecordedByNameOnly()
    {
        AssemblyContextApiComparisonResult complete = await CompareAsync(typeSelection: null, GenerousLimits);
        ApiType inner = complete.After.Surface!.Types.Single(
            static type => type.DefinitionName?.ToEscapedFullName() == "Example.Widget+Inner");

        MemberDiffDocument document = await Single(
            "Example.Widget+Inner",
            "Ping",
            ApiTypeSelection.ForDefinition(inner.DefinitionName!));

        Assert.Equal(
            [
                (MemberDiffEnclosureKind.Namespace, "Example", "namespace Example;"),
                (MemberDiffEnclosureKind.Type, "Widget", null),
                (MemberDiffEnclosureKind.Type, "Inner", "public class Inner"),
            ],
            document.CurrentEnclosure.Select(static value => (value.Kind, value.Name, value.Text)));
    }

    [Fact]
    public async Task DelegateMember_IsEnclosedByTheDelegateSignature()
    {
        MemberDiffDocument document = await Single("Example.Callback", "Invoke");

        Assert.Equal("public delegate void Callback(int input);", document.CurrentEnclosure[^1].Text);
        Assert.Equal(document.BaselineEnclosure[^1].Text, document.CurrentEnclosure[^1].Text);
    }

    [Fact]
    public async Task ReturnAttributedDelegate_IsEnclosedByOneDeclarationLine()
    {
        // A listing prints the return attribute above the declaration, as it
        // prints any Type's attributes; the enclosure line is the declaration.
        MemberDiffDocument document = await Single("Example.Predicate", "Invoke");

        Assert.Equal("public delegate bool Predicate();", document.CurrentEnclosure[^1].Text);
    }

    [Fact]
    public async Task MemberMovedAcrossTypes_PrintsBothEnclosuresBaselineFirst()
    {
        MemberDiffDocument document = await Single("Example.Mover", "Shift");

        Assert.Equal(MemberDiffStatus.Changed, document.Status);
        Assert.Equal([MemberDiffSideKind.Baseline, MemberDiffSideKind.Current], document.PrintedEnclosure);
        Assert.Equal("public class Mover", document.BaselineEnclosure[^1].Text);
        Assert.Equal("public static class MoverExtensions", document.CurrentEnclosure[^1].Text);
        // Mover lost Shift, so its remaining members are all other members.
        Assert.Equal(1, document.BaselineEnclosure[^1].OtherMembers);
    }

    [Fact]
    public async Task MemberOfARemovedType_PrintsTheBaselineTypeNotedRemoved()
    {
        MemberDiffDocument document = await Single("Example.Legacy", "Old");

        Assert.Equal(MemberDiffStatus.Removed, document.Status);
        Assert.Empty(document.CurrentEnclosure);
        Assert.Equal([MemberDiffSideKind.Baseline], document.PrintedEnclosure);
        MemberDiffStatusNote note = Assert.Single(document.BaselineEnclosure[^1].Notes);
        Assert.Equal((MemberDiffStatusNoteKind.TypeRemoved, "2.0.0"), (note.Kind, note.Version));
    }

    [Fact]
    public async Task IncompleteComparison_MakesEveryTargetUnavailable()
    {
        // An incomplete comparison has no surfaces to resolve targets from; a target is an identity.
        MemberDiffTarget target = Targets(await CompareAsync(typeSelection: null, GenerousLimits), "Example.Widget", "Origin")[0];
        AssemblyContextApiComparisonResult truncated = await CompareAsync(
            typeSelection: null,
            new ApiSurfaceProjectionLimits(64, 1, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue));

        var result = Assert.IsType<MemberDiffDocumentResult.Unavailable>(
            MemberDiffDocumentQuery.Execute(new MemberDiffRequest(truncated, "1.0.0", "2.0.0", [target])));
        MemberDiffDocument document = Assert.Single(result.Documents);
        Assert.Equal(MemberDiffStatus.Unavailable, document.Status);
        Assert.Equal(result.Reason, document.UnavailableReason);
        Assert.Null(document.Baseline);
        Assert.Null(document.Current);
        Assert.Empty(document.Lines);
    }

    [Fact]
    public async Task UnknownTarget_IsNotFound()
    {
        AssemblyContextApiComparisonResult comparison = await CompareAsync(typeSelection: null, GenerousLimits);
        MemberDiffTarget target = Targets(comparison, "Example.Widget", "Origin")[0];

        Assert.IsType<MemberDiffDocumentResult.NotFound>(
            MemberDiffDocumentQuery.Execute(new MemberDiffRequest(
                comparison,
                "1.0.0",
                "2.0.0",
                [target with { Anchor = target.Anchor with { StableSelector = "Missing~0000000000", CanonicalSignature = "Missing()" } }])));
    }

    [Fact]
    public async Task Json_RecordsLineKindsEnclosureAndAnnotations()
    {
        MemberDiffDocument document = await Single("Example.Widget", "Origin");

        string json = MemberDiffJson.Serialize(document);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("PublicApi", root.GetProperty("content").GetString());
        Assert.Equal("Changed", root.GetProperty("status").GetString());
        Assert.Equal(
            ["Removed", "Added"],
            root.GetProperty("lines").EnumerateArray().Select(static line => line.GetProperty("kind").GetString()));
        Assert.Equal(2, root.GetProperty("baseline_enclosure").GetArrayLength());
        Assert.Equal(2, root.GetProperty("current_enclosure").GetArrayLength());
        Assert.Equal(["Current"], root.GetProperty("printed_enclosure").EnumerateArray().Select(static side => side.GetString()));
        Assert.Equal("Shapes", root.GetProperty("carets")[0].GetProperty("counterpart").GetString());
        Assert.Equal("Breaking", root.GetProperty("change_notes")[0].GetProperty("classification").GetString());
        Assert.False(root.TryGetProperty("comparison", out _));
        Assert.Contains("signature changed from 'Example.Shapes.Point Origin()'", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int count", "long count", "long", "int")]
    [InlineData("System.ComponentModel.Category(\"old\")", "System.ComponentModel.Category(\"new\")", "\"new\"", "\"old\"")]
    [InlineData("int value = 1", "int value = 2", "2", "1")]
    [InlineData("Dictionary<string, int> map", "Dictionary<string, long> map", "long", "int")]
    [InlineData("int count", "int  count", "", "")]
    [InlineData("int count", "int count = 0", "= 0", "")]
    [InlineData("in int count", "int count", "", "in")]
    public void DifferingSpans_TrimSharedTokens(string baseline, string current, string currentSpan, string baselineSpan)
    {
        var (currentRange, baselineRange) = MemberDiffAnnotations.DifferingSpans(baseline, current);

        Assert.Equal(currentSpan, current.Substring(currentRange.Start, currentRange.Length));
        Assert.Equal(baselineSpan, baseline.Substring(baselineRange.Start, baselineRange.Length));
    }

    static string Slice(MemberDiffSide side, MemberDiffCaret caret)
        => side.Lines[caret.Line].Substring(caret.Start, caret.Length);

    static async Task<MemberDiffDocument> Single(
        string declaringType,
        string memberName,
        ApiTypeSelection? typeSelection = null)
        => Assert.Single(await All(declaringType, memberName, typeSelection));

    static async Task<MemberDiffDocument[]> All(
        string declaringType,
        string memberName,
        ApiTypeSelection? typeSelection = null)
    {
        AssemblyContextApiComparisonResult comparison = await CompareAsync(typeSelection, GenerousLimits);
        var result = Assert.IsType<MemberDiffDocumentResult.Published>(
            MemberDiffDocumentQuery.Execute(new MemberDiffRequest(
                comparison,
                "1.0.0",
                "2.0.0",
                Targets(comparison, declaringType, memberName))));
        return [.. result.Documents];
    }

    static ImmutableArray<MemberDiffTarget> Targets(
        AssemblyContextApiComparisonResult comparison,
        string declaringType,
        string memberName)
        => [
            .. new[] { comparison.Before.Surface!, comparison.After.Surface! }
                .SelectMany(static surface => surface.Types)
                .Where(type => type.DefinitionName?.ToEscapedFullName() == declaringType)
                .SelectMany(type => type.Members
                    .Where(member => member.Name == memberName)
                    .Select(member => new MemberDiffTarget(
                        type.DefinitionName!,
                        ApiMemberIdentity.GetMemberAnchor(type, member))))
                .Distinct(),
        ];

    static async Task<AssemblyContextApiComparisonResult> CompareAsync(
        ApiTypeSelection? typeSelection,
        ApiSurfaceProjectionLimits limits)
    {
        string directory = Directory.CreateTempSubdirectory("member-diff-").FullName;
        try
        {
            string before = Emit(directory, "Before", Before, "1.0.0");
            string after = Emit(directory, "After", After, "2.0.0");
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup beforeGroup =
                workspace.CreateAssemblyContextGroup([Participant(before, "Before")]);
            using AssemblyContextGroup afterGroup =
                workspace.CreateAssemblyContextGroup([Participant(after, "After")]);
            return AssemblyContextApiComparisonQuery.Execute(
                beforeGroup,
                Assert.Single(beforeGroup.Participants),
                afterGroup,
                Assert.Single(afterGroup.Participants),
                ApiSurfaceScope.Public,
                limits,
                typeSelection);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string Emit(string directory, string name, string source, string version)
    {
        string[] platform =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
        // Assembly attributes follow the using directives.
        int namespaceStart = source.IndexOf("namespace ", StringComparison.Ordinal);
        source = source.Insert(namespaceStart, $"[assembly: System.Reflection.AssemblyVersion(\"{version}\")]\n");
        // Both versions are one logical Library.
        CSharpCompilation compilation = CSharpCompilation.Create(
            "MemberDiffLibrary",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable));
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, name)).FullName, "MemberDiffLibrary.dll");
        using (FileStream stream = File.Create(path))
        {
            var result = compilation.Emit(stream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }
        return path;
    }

    static AssemblyContextParticipant Participant(string path, string label)
        => new(
            ResolvedAssemblyReference.CreateFromPath(path, AssemblyResolutionProvenance.Local(label)),
            NoResolverAssemblyBindingPolicy.Instance);
}
