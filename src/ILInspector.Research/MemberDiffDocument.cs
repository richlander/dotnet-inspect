using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ILInspector.Metadata;
using Inspector.Findings;
using Inspector.Text;

namespace ILInspector.Research;

/// <summary>The input a Member diff compares (docs/design/member-diff.md#inputs).</summary>
public enum MemberDiffContentKind
{
    /// <summary>The Member's declaration as the C# display policy prints it.</summary>
    PublicApi,
}

/// <summary>What the comparison found for the Member.</summary>
public enum MemberDiffStatus
{
    /// <summary>
    /// The Library API Diff relates the two sides. Its declarations may still
    /// print the same, when the change lies outside the declaration.
    /// </summary>
    Changed,

    /// <summary>Present in the current version only.</summary>
    Added,

    /// <summary>Present in the baseline version only.</summary>
    Removed,

    /// <summary>Paired by exact identity, with no relation.</summary>
    Unchanged,

    /// <summary>The comparison is incomplete; the diff holds no lines.</summary>
    Unavailable,
}

public enum MemberDiffSideKind
{
    Baseline,
    Current,
}

/// <summary>A compared line's kind, as unified diff marks it.</summary>
public enum MemberDiffLineKind
{
    /// <summary>Equal on both sides.</summary>
    Context,

    /// <summary>A difference present in the baseline only.</summary>
    Removed,

    /// <summary>A difference present in the current version only.</summary>
    Added,
}

public enum MemberDiffEnclosureKind
{
    Namespace,
    Type,
}

/// <summary>The role of the declaration part a caret marks.</summary>
public enum MemberDiffPartKind
{
    Attribute,
    ReturnAttributes,
    Modifier,
    ReturnType,
    Name,
    Parameter,
    Accessor,
    Constraint,
}

public enum MemberDiffCaretKind
{
    /// <summary>The part differs; the caret marks the differing tokens and names their counterpart.</summary>
    Changed,

    /// <summary>The part is present in the current version only.</summary>
    Added,

    /// <summary>The part, or its tokens, are present in the baseline only.</summary>
    Removed,
}

/// <summary>A status the diff states. A status note is not a change.</summary>
public enum MemberDiffStatusNoteKind
{
    /// <summary>The Member's declaration is unchanged since the named version.</summary>
    UnchangedSince,

    /// <summary>The Type was added in the named version, and is reported at Type scope.</summary>
    TypeAdded,

    /// <summary>The Type was removed in the named version, and is reported at Type scope.</summary>
    TypeRemoved,
}

/// <summary>The Member a diff concerns.</summary>
public sealed record MemberDiffSubject
{
    public MemberDiffSubject(string DeclaringType, string Member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DeclaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(Member);
        this.DeclaringType = DeclaringType;
        this.Member = Member;
    }

    /// <summary>The declaring Type's escaped full name.</summary>
    public string DeclaringType { get; }

    /// <summary>The Member's stable selector.</summary>
    public string Member { get; }
}

public sealed record MemberDiffStatusNote
{
    public MemberDiffStatusNote(MemberDiffStatusNoteKind Kind, string Version)
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        this.Kind = Kind;
        this.Version = Version;
    }

    public MemberDiffStatusNoteKind Kind { get; }
    public string Version { get; }
}

/// <summary>
/// One declaration that encloses the compared input. Enclosure locates the
/// input and is never compared at this scope.
/// </summary>
public sealed record MemberDiffEnclosure
{
    public MemberDiffEnclosure(
        MemberDiffEnclosureKind Kind,
        string Name,
        string? Text,
        int? OtherMembers,
        ImmutableArray<MemberDiffStatusNote> Notes)
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Kind == MemberDiffEnclosureKind.Namespace && Text is null)
            throw new ArgumentException("A namespace enclosure has its declaration text.", nameof(Text));
        if (Text is { Length: 0 } || Text?.Contains('\n') == true)
            throw new ArgumentException("Enclosure text is one non-empty line.", nameof(Text));
        if (OtherMembers is < 0)
            throw new ArgumentOutOfRangeException(nameof(OtherMembers));
        if (Kind == MemberDiffEnclosureKind.Namespace && OtherMembers is not null)
            throw new ArgumentException("Only a Type counts its other members.", nameof(OtherMembers));
        if (Notes.IsDefault || Notes.Any(static note => note is null))
            throw new ArgumentException("Notes must be initialized and non-null.", nameof(Notes));

        this.Kind = Kind;
        this.Name = Name;
        this.Text = Text;
        this.OtherMembers = OtherMembers;
        this.Notes = Notes;
    }

    public MemberDiffEnclosureKind Kind { get; }

    /// <summary>The namespace or the Type's name.</summary>
    public string Name { get; }

    /// <summary>
    /// The declaration line, or null for a containing Type outside the
    /// compared surface, which is recorded by name only.
    /// </summary>
    public string? Text { get; }

    /// <summary>The innermost Type's members other than the compared one.</summary>
    public int? OtherMembers { get; }

    public ImmutableArray<MemberDiffStatusNote> Notes { get; }
}

/// <summary>One side's compared input.</summary>
public sealed record MemberDiffSide
{
    public MemberDiffSide(
        ImmutableArray<string> Lines,
        string? PartsUnidentifiedReason)
    {
        if (Lines.IsDefaultOrEmpty)
            throw new ArgumentException("A present side has at least one line.", nameof(Lines));
        if (Lines.Any(static line => line is null || line.Contains('\n') || line.Contains('\r')))
            throw new ArgumentException("Each line is one line of text.", nameof(Lines));
        if (PartsUnidentifiedReason is { Length: 0 })
            throw new ArgumentException("An unidentified reason is not empty.", nameof(PartsUnidentifiedReason));

        this.Lines = Lines;
        this.PartsUnidentifiedReason = PartsUnidentifiedReason;
    }

    /// <summary>The declaration, numbered from zero within itself.</summary>
    public ImmutableArray<string> Lines { get; }

    /// <summary>Why the side's parts are unidentified, so no part caret is placed; null when identified.</summary>
    public string? PartsUnidentifiedReason { get; }
}

/// <summary>A compared line in unified-diff order.</summary>
public sealed record MemberDiffLine(
    MemberDiffLineKind Kind,
    int? BaselineLine,
    int? CurrentLine,
    string Text);

/// <summary>A caret on the span of one declaration part, as caret stacking renders it.</summary>
public sealed record MemberDiffCaret
{
    public MemberDiffCaret(
        MemberDiffSideKind Side,
        int Line,
        int Start,
        int Length,
        MemberDiffCaretKind Kind,
        MemberDiffPartKind Part,
        string Key,
        int Occurrence,
        string? Counterpart)
    {
        if (!Enum.IsDefined(Side))
            throw new ArgumentOutOfRangeException(nameof(Side));
        ArgumentOutOfRangeException.ThrowIfNegative(Line);
        ArgumentOutOfRangeException.ThrowIfNegative(Start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Length);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!Enum.IsDefined(Part))
            throw new ArgumentOutOfRangeException(nameof(Part));
        ArgumentException.ThrowIfNullOrEmpty(Key);
        ArgumentOutOfRangeException.ThrowIfNegative(Occurrence);
        if ((Kind == MemberDiffCaretKind.Changed) != (Counterpart is not null))
            throw new ArgumentException("Only a Changed caret names its counterpart.", nameof(Counterpart));
        if (Kind == MemberDiffCaretKind.Added && Side != MemberDiffSideKind.Current)
            throw new ArgumentException("An Added caret is on the current side.", nameof(Side));
        if (Kind == MemberDiffCaretKind.Removed && Side != MemberDiffSideKind.Baseline)
            throw new ArgumentException("A Removed caret is on the baseline side.", nameof(Side));
        if (Kind == MemberDiffCaretKind.Changed && Side != MemberDiffSideKind.Current)
            throw new ArgumentException("A Changed caret is on the current side.", nameof(Side));

        this.Side = Side;
        this.Line = Line;
        this.Start = Start;
        this.Length = Length;
        this.Kind = Kind;
        this.Part = Part;
        this.Key = Key;
        this.Occurrence = Occurrence;
        this.Counterpart = Counterpart;
    }

    public MemberDiffSideKind Side { get; }

    /// <summary>The line within <see cref="Side"/>'s input.</summary>
    public int Line { get; }

    /// <summary>The caret's first column in that line.</summary>
    public int Start { get; }

    public int Length { get; }
    public MemberDiffCaretKind Kind { get; }
    public MemberDiffPartKind Part { get; }

    /// <summary>The part's correspondence key: attribute Type, modifier keyword, parameter position, accessor kind, or constrained type parameter.</summary>
    public string Key { get; }

    /// <summary>The part's position among parts with the same kind and key.</summary>
    public int Occurrence { get; }

    /// <summary>For a Changed caret, the baseline text of the marked tokens.</summary>
    public string? Counterpart { get; }
}

/// <summary>A classified API change of the relation, noted on the declaration.</summary>
public sealed record MemberDiffChangeNote
{
    public MemberDiffChangeNote(
        MemberDiffSideKind Side,
        int Line,
        ChangeKind Kind,
        ChangeClassification Classification,
        ApiChangeCategory Category,
        string Message,
        string? OldValue,
        string? NewValue)
    {
        if (!Enum.IsDefined(Side))
            throw new ArgumentOutOfRangeException(nameof(Side));
        ArgumentOutOfRangeException.ThrowIfNegative(Line);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        this.Side = Side;
        this.Line = Line;
        this.Kind = Kind;
        this.Classification = Classification;
        this.Category = Category;
        this.Message = Message;
        this.OldValue = OldValue;
        this.NewValue = NewValue;
    }

    public MemberDiffSideKind Side { get; }
    public int Line { get; }
    public ChangeKind Kind { get; }
    public ChangeClassification Classification { get; }
    public ApiChangeCategory Category { get; }
    public string Message { get; }
    public string? OldValue { get; }
    public string? NewValue { get; }
}

/// <summary>A status note on a compared line, such as "Unchanged since 1.0.0".</summary>
public sealed record MemberDiffLineStatusNote
{
    public MemberDiffLineStatusNote(
        MemberDiffSideKind Side,
        int Line,
        MemberDiffStatusNote Note)
    {
        if (!Enum.IsDefined(Side))
            throw new ArgumentOutOfRangeException(nameof(Side));
        ArgumentOutOfRangeException.ThrowIfNegative(Line);
        ArgumentNullException.ThrowIfNull(Note);
        this.Side = Side;
        this.Line = Line;
        this.Note = Note;
    }

    public MemberDiffSideKind Side { get; }
    public int Line { get; }
    public MemberDiffStatusNote Note { get; }
}

/// <summary>
/// One Member's diff over one input: the compared lines, the declarations
/// enclosing them on each side, and the annotations on both
/// (docs/design/member-diff.md#document). Hosts render it; none recomputes
/// the comparison or infers a line kind.
/// </summary>
public sealed record MemberDiffDocument
{
    public const int CurrentSchemaVersion = 1;

    MemberDiffDocument(
        MemberDiffContentKind content,
        MemberDiffSubject subject,
        MemberDiffStatus status,
        string? unavailableReason,
        MemberDiffSide? baseline,
        MemberDiffSide? current,
        ImmutableArray<MemberDiffEnclosure> baselineEnclosure,
        ImmutableArray<MemberDiffEnclosure> currentEnclosure,
        ImmutableArray<MemberDiffSideKind> printedEnclosure,
        AnalysisDiff<string>? comparison,
        ImmutableArray<MemberDiffLine> lines,
        ImmutableArray<MemberDiffCaret> carets,
        ImmutableArray<MemberDiffChangeNote> changeNotes,
        ImmutableArray<MemberDiffLineStatusNote> statusNotes)
    {
        Content = content;
        Subject = subject;
        Status = status;
        UnavailableReason = unavailableReason;
        Baseline = baseline;
        Current = current;
        BaselineEnclosure = baselineEnclosure;
        CurrentEnclosure = currentEnclosure;
        PrintedEnclosure = printedEnclosure;
        Comparison = comparison;
        Lines = lines;
        Carets = carets;
        ChangeNotes = changeNotes;
        StatusNotes = statusNotes;
    }

    public int SchemaVersion => CurrentSchemaVersion;
    public MemberDiffContentKind Content { get; }
    public MemberDiffSubject Subject { get; }
    public MemberDiffStatus Status { get; }

    /// <summary>Why the diff is Unavailable; null otherwise.</summary>
    public string? UnavailableReason { get; }

    public MemberDiffSide? Baseline { get; }
    public MemberDiffSide? Current { get; }

    /// <summary>
    /// The declarations enclosing the Member in the baseline version,
    /// outermost first: the namespace, then the containing Types. Empty when
    /// the declaring Type does not exist in that version.
    /// </summary>
    public ImmutableArray<MemberDiffEnclosure> BaselineEnclosure { get; }

    /// <summary>The declarations enclosing the Member in the current version, as <see cref="BaselineEnclosure"/>.</summary>
    public ImmutableArray<MemberDiffEnclosure> CurrentEnclosure { get; }

    /// <summary>
    /// The enclosure every host prints, as the sides whose enclosure it is:
    /// the current side's when its Type exists there, otherwise the
    /// baseline's; both, baseline first, when the two sides belong to
    /// different declaring Types.
    /// </summary>
    public ImmutableArray<MemberDiffSideKind> PrintedEnclosure { get; }

    /// <summary>
    /// The line comparison of the two inputs. Unified diff lowers from it;
    /// <see cref="Lines"/> is the same comparison in unified-diff order.
    /// </summary>
    [JsonIgnore]
    public AnalysisDiff<string>? Comparison { get; }

    /// <summary>Every compared line with its kind, in unified-diff order.</summary>
    public ImmutableArray<MemberDiffLine> Lines { get; }

    public ImmutableArray<MemberDiffCaret> Carets { get; }
    public ImmutableArray<MemberDiffChangeNote> ChangeNotes { get; }
    public ImmutableArray<MemberDiffLineStatusNote> StatusNotes { get; }

    /// <summary>Whether the two inputs differ: any line is not context.</summary>
    [JsonIgnore]
    public bool HasDifference => Lines.Any(static line => line.Kind != MemberDiffLineKind.Context);

    /// <summary>A diff the comparison could not decide. It holds no lines.</summary>
    public static MemberDiffDocument Unavailable(
        MemberDiffContentKind content,
        MemberDiffSubject subject,
        string reason)
    {
        ValidateContent(content);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(
            content,
            subject,
            MemberDiffStatus.Unavailable,
            reason,
            baseline: null,
            current: null,
            baselineEnclosure: [],
            currentEnclosure: [],
            printedEnclosure: [],
            comparison: null,
            lines: [],
            carets: [],
            changeNotes: [],
            statusNotes: []);
    }

    /// <summary>
    /// Compares the two sides' lines and validates the annotations against
    /// them. A side is null when the Member is absent from that version.
    /// </summary>
    public static MemberDiffDocument Create(
        MemberDiffContentKind content,
        MemberDiffSubject subject,
        MemberDiffStatus status,
        MemberDiffSide? baseline,
        MemberDiffSide? current,
        ImmutableArray<MemberDiffEnclosure> baselineEnclosure,
        ImmutableArray<MemberDiffEnclosure> currentEnclosure,
        ImmutableArray<MemberDiffSideKind> printedEnclosure,
        ImmutableArray<MemberDiffCaret> carets,
        ImmutableArray<MemberDiffChangeNote> changeNotes,
        ImmutableArray<MemberDiffLineStatusNote> statusNotes)
    {
        ValidateContent(content);
        ArgumentNullException.ThrowIfNull(subject);
        ValidateTopology(status, baseline, current);
        ValidateEnclosure(baselineEnclosure, baseline, nameof(baselineEnclosure));
        ValidateEnclosure(currentEnclosure, current, nameof(currentEnclosure));
        ValidatePrintedEnclosure(printedEnclosure, baseline, current, baselineEnclosure, currentEnclosure);
        if (carets.IsDefault || carets.Any(static caret => caret is null))
            throw new ArgumentException("Carets must be initialized and non-null.", nameof(carets));
        if (changeNotes.IsDefault || changeNotes.Any(static note => note is null))
            throw new ArgumentException("Change notes must be initialized and non-null.", nameof(changeNotes));
        if (statusNotes.IsDefault || statusNotes.Any(static note => note is null))
            throw new ArgumentException("Status notes must be initialized and non-null.", nameof(statusNotes));

        AnalysisDiff<string> comparison = TextFindings.CreateAnalysisDiff(
            Join(baseline),
            Join(current),
            new FindingSubject(
                $"member-diff:{subject.DeclaringType}:{subject.Member}",
                subject.Member));
        ImmutableArray<MemberDiffLine> lines = UnifiedLines(comparison);

        if (status == MemberDiffStatus.Unchanged
            && lines.Any(static line => line.Kind != MemberDiffLineKind.Context))
        {
            throw new ArgumentException(
                "An Unchanged diff's declarations compare equal.",
                nameof(status));
        }
        if (!carets.IsEmpty && (baseline is null || current is null))
        {
            throw new ArgumentException(
                "A one-sided diff has no part carets; its whole declaration is the difference.",
                nameof(carets));
        }
        foreach (MemberDiffCaret caret in carets)
        {
            string line = LineOf(caret.Side, caret.Line, baseline, current, nameof(carets));
            if (caret.Start + caret.Length > line.Length)
                throw new ArgumentException("A caret lies within its line.", nameof(carets));
        }
        foreach (MemberDiffChangeNote note in changeNotes)
            _ = LineOf(note.Side, note.Line, baseline, current, nameof(changeNotes));
        foreach (MemberDiffLineStatusNote note in statusNotes)
            _ = LineOf(note.Side, note.Line, baseline, current, nameof(statusNotes));

        return new(
            content,
            subject,
            status,
            unavailableReason: null,
            baseline,
            current,
            baselineEnclosure,
            currentEnclosure,
            printedEnclosure,
            comparison,
            lines,
            carets,
            changeNotes,
            statusNotes);
    }

    static void ValidateContent(MemberDiffContentKind content)
    {
        if (!Enum.IsDefined(content))
            throw new ArgumentOutOfRangeException(nameof(content));
    }

    static void ValidateTopology(
        MemberDiffStatus status,
        MemberDiffSide? baseline,
        MemberDiffSide? current)
    {
        bool valid = status switch
        {
            MemberDiffStatus.Changed or MemberDiffStatus.Unchanged => baseline is not null && current is not null,
            MemberDiffStatus.Added => baseline is null && current is not null,
            MemberDiffStatus.Removed => baseline is not null && current is null,
            MemberDiffStatus.Unavailable => throw new ArgumentException(
                "An Unavailable diff is created by Unavailable(...).",
                nameof(status)),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        if (!valid)
        {
            throw new ArgumentException(
                $"A {status} diff does not have that side topology.",
                nameof(status));
        }
    }

    static void ValidateEnclosure(
        ImmutableArray<MemberDiffEnclosure> enclosure,
        MemberDiffSide? side,
        string parameter)
    {
        if (enclosure.IsDefault || enclosure.Any(static value => value is null))
            throw new ArgumentException("Enclosure must be initialized and non-null.", parameter);
        if (side is not null && enclosure.IsEmpty)
            throw new ArgumentException("A present side's declaring Type encloses it.", parameter);
        if (!enclosure.IsEmpty && enclosure[^1].Kind != MemberDiffEnclosureKind.Type)
            throw new ArgumentException("The innermost enclosure is the declaring Type.", parameter);
        if (enclosure.Skip(1).Any(static value => value.Kind == MemberDiffEnclosureKind.Namespace))
            throw new ArgumentException("Only the outermost enclosure can be a namespace.", parameter);
        if (enclosure.SkipLast(1).Any(static value => value.OtherMembers is not null))
            throw new ArgumentException("Only the innermost Type counts its other members.", parameter);
    }

    static void ValidatePrintedEnclosure(
        ImmutableArray<MemberDiffSideKind> printedEnclosure,
        MemberDiffSide? baseline,
        MemberDiffSide? current,
        ImmutableArray<MemberDiffEnclosure> baselineEnclosure,
        ImmutableArray<MemberDiffEnclosure> currentEnclosure)
    {
        bool valid = printedEnclosure switch
        {
            [MemberDiffSideKind.Current] => !currentEnclosure.IsEmpty,
            [MemberDiffSideKind.Baseline] => !baselineEnclosure.IsEmpty && currentEnclosure.IsEmpty,
            [MemberDiffSideKind.Baseline, MemberDiffSideKind.Current] => baseline is not null && current is not null,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "The printed enclosure is the current version's when it exists, the baseline's otherwise, "
                    + "or both, baseline first, across declaring Types.",
                nameof(printedEnclosure));
        }
    }

    static string LineOf(
        MemberDiffSideKind side,
        int line,
        MemberDiffSide? baseline,
        MemberDiffSide? current,
        string parameter)
    {
        MemberDiffSide? value = side == MemberDiffSideKind.Baseline ? baseline : current;
        if (value is null || line >= value.Lines.Length)
            throw new ArgumentException("An annotation lies on a line of a present side.", parameter);
        return value.Lines[line];
    }

    static string Join(MemberDiffSide? side)
        => side is null ? "" : string.Join('\n', side.Lines);

    /// <summary>
    /// Orders the comparison as unified diff does: stable unchanged one-to-one
    /// correspondences are context, and between them the baseline's lines are
    /// removed before the current's are added. This is the anchor rule the
    /// unified-diff lowering uses, so the two agree line for line.
    /// </summary>
    static ImmutableArray<MemberDiffLine> UnifiedLines(AnalysisDiff<string> comparison)
    {
        var anchors = comparison.Relations
            .OfType<AnalysisDiffRelation.Correspondence>()
            .Where(static relation =>
                relation.Content == AnalysisDiffContentKind.Unchanged
                && relation.Placement == AnalysisDiffPlacementKind.Stable
                && relation.BeforeCoordinates.Length == 1
                && relation.AfterCoordinates.Length == 1)
            .Select(static relation => (Before: relation.BeforeCoordinates[0], After: relation.AfterCoordinates[0]))
            .OrderBy(static anchor => anchor.Before)
            .ToArray();

        var lines = ImmutableArray.CreateBuilder<MemberDiffLine>();
        int before = 0;
        int after = 0;
        foreach ((int beforeAnchor, int afterAnchor) in anchors)
        {
            AddGap(beforeAnchor, afterAnchor);
            lines.Add(new MemberDiffLine(
                MemberDiffLineKind.Context,
                beforeAnchor,
                afterAnchor,
                comparison.After[afterAnchor]));
            before = beforeAnchor + 1;
            after = afterAnchor + 1;
        }
        AddGap(comparison.Before.Length, comparison.After.Length);
        return lines.ToImmutable();

        void AddGap(int beforeEnd, int afterEnd)
        {
            for (; before < beforeEnd; before++)
                lines.Add(new MemberDiffLine(MemberDiffLineKind.Removed, before, null, comparison.Before[before]));
            for (; after < afterEnd; after++)
                lines.Add(new MemberDiffLine(MemberDiffLineKind.Added, null, after, comparison.After[after]));
        }
    }
}
