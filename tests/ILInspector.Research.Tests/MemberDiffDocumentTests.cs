using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>The Member diff document's own invariants (docs/design/member-diff.md#document).</summary>
public class MemberDiffDocumentTests
{
    static readonly MemberDiffSubject Subject = new("Example.Widget", "Run~0123456789");

    static readonly ImmutableArray<MemberDiffEnclosure> Enclosure =
    [
        new(MemberDiffEnclosureKind.Namespace, "Example", "namespace Example;", null, []),
        new(MemberDiffEnclosureKind.Type, "Widget", "public class Widget", 4, []),
    ];

    [Fact]
    public void Lines_FollowUnifiedDiffOrder()
    {
        MemberDiffDocument document = Create(
            MemberDiffStatus.Changed,
            Side("[A]", "public void Run(int count);", "// end"),
            Side("[A]", "public void Run(long count);", "// end"));

        Assert.Equal(
            [
                (MemberDiffLineKind.Context, 0, 0, "[A]"),
                (MemberDiffLineKind.Removed, 1, null, "public void Run(int count);"),
                (MemberDiffLineKind.Added, null, 1, "public void Run(long count);"),
                (MemberDiffLineKind.Context, 2, 2, "// end"),
            ],
            document.Lines.Select(static line => (line.Kind, line.BaselineLine, line.CurrentLine, line.Text)));
        Assert.True(document.HasDifference);
        Assert.NotNull(document.Comparison);
    }

    [Fact]
    public void RelatedMemberThatPrintsTheSame_IsChangedWithEveryLineContext()
    {
        var note = new MemberDiffChangeNote(
            MemberDiffSideKind.Current,
            0,
            ChangeKind.MemberHiddenChanged,
            ChangeClassification.NonBreaking,
            ApiChangeCategory.Status,
            "Member 'Run' became hidden",
            null,
            null);

        MemberDiffDocument document = Create(
            MemberDiffStatus.Changed,
            Side("public void Run(int count);"),
            Side("public void Run(int count);"),
            changeNotes: [note]);

        Assert.Equal(MemberDiffStatus.Changed, document.Status);
        Assert.All(document.Lines, static line => Assert.Equal(MemberDiffLineKind.Context, line.Kind));
        Assert.Equal(note, Assert.Single(document.ChangeNotes));
    }

    [Fact]
    public void OneSidedDiff_ComparesAgainstNothing()
    {
        MemberDiffDocument document = Create(
            MemberDiffStatus.Added,
            baseline: null,
            Side("[A]", "public void Run(long count);"),
            baselineEnclosure: []);

        Assert.Equal(
            [MemberDiffLineKind.Added, MemberDiffLineKind.Added],
            document.Lines.Select(static line => line.Kind));
    }

    [Fact]
    public void Unchanged_RejectsADifference()
    {
        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Unchanged,
            Side("public void Run(int count);"),
            Side("public void Run(long count);")));
    }

    [Theory]
    [InlineData(MemberDiffStatus.Added, true, true)]
    [InlineData(MemberDiffStatus.Removed, false, false)]
    [InlineData(MemberDiffStatus.Changed, true, false)]
    [InlineData(MemberDiffStatus.Unchanged, false, true)]
    public void Status_RequiresItsSideTopology(MemberDiffStatus status, bool baseline, bool current)
    {
        Assert.Throws<ArgumentException>(() => Create(
            status,
            baseline ? Side("a;") : null,
            current ? Side("a;") : null,
            baselineEnclosure: baseline ? Enclosure : [],
            currentEnclosure: current ? Enclosure : [],
            printed: current ? [MemberDiffSideKind.Current] : [MemberDiffSideKind.Baseline]));
    }

    [Fact]
    public void Unavailable_HoldsNoLinesAndIsNotCreatedFromSides()
    {
        MemberDiffDocument document = MemberDiffDocument.Unavailable(
            MemberDiffContentKind.PublicApi,
            Subject,
            "The baseline version's API surface was not projected completely.");

        Assert.Equal(MemberDiffStatus.Unavailable, document.Status);
        Assert.Null(document.Baseline);
        Assert.Null(document.Current);
        Assert.Empty(document.Lines);
        Assert.Empty(document.PrintedEnclosure);
        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Unavailable,
            Side("a;"),
            Side("a;")));
    }

    [Fact]
    public void OneSidedDiff_RejectsPartCarets()
    {
        var caret = new MemberDiffCaret(
            MemberDiffSideKind.Current,
            0,
            0,
            6,
            MemberDiffCaretKind.Added,
            MemberDiffPartKind.Modifier,
            "public",
            0,
            null);

        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Added,
            baseline: null,
            Side("public void Run();"),
            baselineEnclosure: [],
            carets: [caret]));
    }

    [Fact]
    public void Caret_LiesWithinItsLine()
    {
        var caret = new MemberDiffCaret(
            MemberDiffSideKind.Current,
            0,
            20,
            10,
            MemberDiffCaretKind.Changed,
            MemberDiffPartKind.Parameter,
            "0",
            0,
            "int");

        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Changed,
            Side("public void Run(int count);"),
            Side("public void Run(long count);"),
            carets: [caret]));
    }

    [Fact]
    public void Caret_SideFollowsItsKind()
    {
        Assert.Throws<ArgumentException>(() => new MemberDiffCaret(
            MemberDiffSideKind.Baseline, 0, 0, 1, MemberDiffCaretKind.Added, MemberDiffPartKind.Modifier, "static", 0, null));
        Assert.Throws<ArgumentException>(() => new MemberDiffCaret(
            MemberDiffSideKind.Current, 0, 0, 1, MemberDiffCaretKind.Removed, MemberDiffPartKind.Modifier, "static", 0, null));
        Assert.Throws<ArgumentException>(() => new MemberDiffCaret(
            MemberDiffSideKind.Current, 0, 0, 1, MemberDiffCaretKind.Changed, MemberDiffPartKind.Parameter, "0", 0, null));
    }

    [Fact]
    public void PrintedEnclosure_IsTheCurrentVersionsWhenItExists()
    {
        // The Member was removed, but its Type remains: the current Type encloses it.
        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Removed,
            Side("public void Run();"),
            current: null,
            printed: [MemberDiffSideKind.Baseline]));

        MemberDiffDocument removed = Create(
            MemberDiffStatus.Removed,
            Side("public void Run();"),
            current: null);
        Assert.Equal([MemberDiffSideKind.Current], removed.PrintedEnclosure);

        MemberDiffDocument acrossTypes = Create(
            MemberDiffStatus.Changed,
            Side("public void Run();"),
            Side("public void Run();"),
            printed: [MemberDiffSideKind.Baseline, MemberDiffSideKind.Current]);
        Assert.Equal([MemberDiffSideKind.Baseline, MemberDiffSideKind.Current], acrossTypes.PrintedEnclosure);

        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Changed,
            Side("public void Run();"),
            Side("public void Run();"),
            printed: [MemberDiffSideKind.Current, MemberDiffSideKind.Baseline]));
    }

    [Fact]
    public void Enclosure_EndsWithTheDeclaringTypeAndCountsOnlyThere()
    {
        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Changed,
            Side("a;"),
            Side("a;"),
            currentEnclosure: [Enclosure[0]]));
        Assert.Throws<ArgumentException>(() => Create(
            MemberDiffStatus.Changed,
            Side("a;"),
            Side("a;"),
            currentEnclosure:
            [
                new(MemberDiffEnclosureKind.Type, "Outer", null, 3, []),
                new(MemberDiffEnclosureKind.Type, "Inner", "public class Inner", 1, []),
            ]));
        Assert.Throws<ArgumentException>(() => new MemberDiffEnclosure(
            MemberDiffEnclosureKind.Namespace, "Example", null, null, []));
    }

    static MemberDiffSide Side(params string[] lines)
        => new([.. lines], PartsUnidentifiedReason: null);

    static MemberDiffDocument Create(
        MemberDiffStatus status,
        MemberDiffSide? baseline,
        MemberDiffSide? current,
        ImmutableArray<MemberDiffEnclosure>? baselineEnclosure = null,
        ImmutableArray<MemberDiffEnclosure>? currentEnclosure = null,
        ImmutableArray<MemberDiffSideKind>? printed = null,
        ImmutableArray<MemberDiffCaret>? carets = null,
        ImmutableArray<MemberDiffChangeNote>? changeNotes = null)
        => MemberDiffDocument.Create(
            MemberDiffContentKind.PublicApi,
            Subject,
            status,
            baseline,
            current,
            baselineEnclosure ?? Enclosure,
            currentEnclosure ?? Enclosure,
            printed ?? [MemberDiffSideKind.Current],
            carets ?? [],
            changeNotes ?? [],
            []);
}
