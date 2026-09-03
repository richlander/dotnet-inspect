using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>How subject identifiers are interpreted within one comparison document.</summary>
public enum SubjectCoordinateBasis
{
    /// <summary>Every subject identifier is complete in the producer's outer comparison context.</summary>
    OuterContext,

    /// <summary>Every subject identifier is complete relative to its corresponding root endpoint.</summary>
    RootRelative,
}

/// <summary>The exceptional coordinate change described by a change description.</summary>
public enum ComparisonExceptionalChangeKind
{
    Rename,
    Move,
    RenameAndMove,
}

/// <summary>
/// One closed composition-level change for a comparison root or subject.
/// Payload-internal changes remain owned by the payload.
/// </summary>
public abstract record ComparisonSubjectChange
{
    private ComparisonSubjectChange()
    {
    }

    // Records synthesize a protected copy constructor. This inaccessible abstract member prevents
    // external records from using that constructor to extend the closed change hierarchy.
    private protected abstract void EnsureKnownChange();

    /// <summary>No composition-level existence or coordinate change.</summary>
    public sealed record Diff : ComparisonSubjectChange
    {
        private protected override void EnsureKnownChange()
        {
        }
    }

    /// <summary>The subject exists only on the After endpoint.</summary>
    public sealed record Addition : ComparisonSubjectChange
    {
        private protected override void EnsureKnownChange()
        {
        }
    }

    /// <summary>The subject exists only on the Before endpoint.</summary>
    public sealed record Deletion : ComparisonSubjectChange
    {
        private protected override void EnsureKnownChange()
        {
        }
    }

    /// <summary>The local subject identity changed within one containing coordinate.</summary>
    public sealed record Rename : ComparisonSubjectChange
    {
        public Rename(string ChangeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ChangeId);
            this.ChangeId = ChangeId;
        }

        public string ChangeId { get; }

        private protected override void EnsureKnownChange()
        {
        }
    }

    /// <summary>The containing coordinate changed while local subject identity was retained.</summary>
    public sealed record Move : ComparisonSubjectChange
    {
        public Move(string ChangeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ChangeId);
            this.ChangeId = ChangeId;
        }

        public string ChangeId { get; }

        private protected override void EnsureKnownChange()
        {
        }
    }

    /// <summary>Both local subject identity and containing coordinate changed.</summary>
    public sealed record RenameAndMove : ComparisonSubjectChange
    {
        public RenameAndMove(string ChangeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ChangeId);
            this.ChangeId = ChangeId;
        }

        public string ChangeId { get; }

        private protected override void EnsureKnownChange()
        {
        }
    }
}

/// <summary>A root comparison that is explicitly present or structurally not applicable.</summary>
public abstract record ComparisonRootComparison<T>
    where T : notnull
{
    private ComparisonRootComparison()
    {
    }

    // Records synthesize a protected copy constructor. This inaccessible abstract member prevents
    // external records from using that constructor to extend the closed presence hierarchy.
    private protected abstract void EnsureKnownComparison();

    /// <summary>The root has one comparison payload.</summary>
    public sealed record Present : ComparisonRootComparison<T>
    {
        public Present(T Comparison)
        {
            if (Comparison is null)
                throw new ArgumentNullException(nameof(Comparison));
            this.Comparison = Comparison;
        }

        public T Comparison { get; }

        private protected override void EnsureKnownComparison()
        {
        }

        public bool Equals(Present? other)
            => other is not null
                && EqualityComparer<T>.Default.Equals(Comparison, other.Comparison);

        public override int GetHashCode()
            => EqualityComparer<T>.Default.GetHashCode(Comparison);
    }

    /// <summary>The payload type has no meaningful root-wide item space.</summary>
    public sealed record NotApplicable : ComparisonRootComparison<T>
    {
        private protected override void EnsureKnownComparison()
        {
        }
    }
}

/// <summary>One complete portable endpoint for an exceptional subject change.</summary>
public sealed record ComparisonSubjectEndpoint
{
    public ComparisonSubjectEndpoint(string Identifier, string Display)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Identifier = Identifier;
        this.Display = Display;
    }

    public string Identifier { get; }
    public string Display { get; }
}

/// <summary>One producer-issued refinement of an exceptional coordinate change.</summary>
public sealed record ComparisonTransformationDescriptor
{
    public ComparisonTransformationDescriptor(string Identifier, string Display)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Identifier = Identifier;
        this.Display = Display;
    }

    public string Identifier { get; }
    public string Display { get; }
}

/// <summary>The complete endpoint detail referenced by one exceptional root or subject.</summary>
public sealed record ComparisonChangeDescription
{
    public ComparisonChangeDescription(
        string Id,
        ComparisonExceptionalChangeKind Kind,
        ComparisonSubjectEndpoint Before,
        ComparisonSubjectEndpoint After,
        ImmutableArray<ComparisonTransformationDescriptor> Transformations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Kind = Validate(Kind);
        this.Before = Before ?? throw new ArgumentNullException(nameof(Before));
        this.After = After ?? throw new ArgumentNullException(nameof(After));
        if (StringComparer.Ordinal.Equals(Before.Identifier, After.Identifier))
        {
            throw new ArgumentException(
                "Exceptional Before and After identifiers must differ.",
                nameof(After));
        }
        if (Transformations.IsDefault)
        {
            throw new ArgumentException(
                "Transformations must be initialized.",
                nameof(Transformations));
        }

        var transformationIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Transformations.Length; i++)
        {
            ComparisonTransformationDescriptor transformation =
                Transformations[i]
                ?? throw new ArgumentException(
                    $"Transformation at index {i} must not be null.",
                    nameof(Transformations));
            if (!transformationIds.Add(transformation.Identifier))
            {
                throw new ArgumentException(
                    $"Transformation identifier '{transformation.Identifier}' occurs more than once.",
                    nameof(Transformations));
            }
        }

        this.Id = Id;
        this.Transformations = Transformations;
    }

    public string Id { get; }
    public ComparisonExceptionalChangeKind Kind { get; }
    public ComparisonSubjectEndpoint Before { get; }
    public ComparisonSubjectEndpoint After { get; }
    public ImmutableArray<ComparisonTransformationDescriptor> Transformations { get; }

    public bool Equals(ComparisonChangeDescription? other)
        => other is not null
            && StringComparer.Ordinal.Equals(Id, other.Id)
            && Kind == other.Kind
            && Before == other.Before
            && After == other.After
            && FindingValueEquality.SequenceEqual(Transformations, other.Transformations);

    public override int GetHashCode()
        => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Id),
            Kind,
            Before,
            After,
            FindingValueEquality.SequenceHashCode(Transformations));

    static ComparisonExceptionalChangeKind Validate(
        ComparisonExceptionalChangeKind kind)
        => kind switch
        {
            ComparisonExceptionalChangeKind.Rename => kind,
            ComparisonExceptionalChangeKind.Move => kind,
            ComparisonExceptionalChangeKind.RenameAndMove => kind,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}

/// <summary>One ordered portable subject and its opaque comparison payload.</summary>
public sealed record ComparisonSubject<T>
    where T : notnull
{
    public ComparisonSubject(
        string Identifier,
        string Display,
        ComparisonSubjectChange Change,
        T Comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Change = Change ?? throw new ArgumentNullException(nameof(Change));
        if (Comparison is null)
            throw new ArgumentNullException(nameof(Comparison));

        this.Identifier = Identifier;
        this.Display = Display;
        this.Comparison = Comparison;
    }

    public string Identifier { get; }
    public string Display { get; }
    public ComparisonSubjectChange Change { get; }
    public T Comparison { get; }

    public bool Equals(ComparisonSubject<T>? other)
        => other is not null
            && StringComparer.Ordinal.Equals(Identifier, other.Identifier)
            && StringComparer.Ordinal.Equals(Display, other.Display)
            && Change == other.Change
            && EqualityComparer<T>.Default.Equals(Comparison, other.Comparison);

    public override int GetHashCode()
        => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Identifier),
            StringComparer.Ordinal.GetHashCode(Display),
            Change,
            EqualityComparer<T>.Default.GetHashCode(Comparison));
}

/// <summary>
/// A complete immutable composition of one portable root, ordered portable subjects,
/// opaque comparison payloads, and referenced exceptional coordinate changes.
/// </summary>
public sealed record ComparisonDocument<T>
    where T : notnull
{
    public const int CurrentSchemaVersion = 1;

    public ComparisonDocument(
        int SchemaVersion,
        SubjectCoordinateBasis SubjectCoordinateBasis,
        string Identifier,
        string Display,
        ComparisonSubjectChange Change,
        ComparisonRootComparison<T> Comparison,
        ImmutableArray<ComparisonSubject<T>> Subjects,
        ImmutableArray<ComparisonChangeDescription> ChangeDescriptions)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SchemaVersion),
                "Comparison document schema version is unsupported.");
        }
        this.SubjectCoordinateBasis = Validate(SubjectCoordinateBasis);
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Change = Change ?? throw new ArgumentNullException(nameof(Change));
        this.Comparison = Comparison ?? throw new ArgumentNullException(nameof(Comparison));
        this.Subjects = ValidateSubjects(Subjects);
        this.ChangeDescriptions = ValidateAndCanonicalizeDescriptions(ChangeDescriptions);

        ValidateTopology(
            SubjectCoordinateBasis,
            Identifier,
            Display,
            Change,
            this.Subjects,
            this.ChangeDescriptions);

        this.SchemaVersion = SchemaVersion;
        this.Identifier = Identifier;
        this.Display = Display;
    }

    public int SchemaVersion { get; }
    public SubjectCoordinateBasis SubjectCoordinateBasis { get; }
    public string Identifier { get; }
    public string Display { get; }
    public ComparisonSubjectChange Change { get; }
    public ComparisonRootComparison<T> Comparison { get; }
    public ImmutableArray<ComparisonSubject<T>> Subjects { get; }
    public ImmutableArray<ComparisonChangeDescription> ChangeDescriptions { get; }

    public bool Equals(ComparisonDocument<T>? other)
        => other is not null
            && SchemaVersion == other.SchemaVersion
            && SubjectCoordinateBasis == other.SubjectCoordinateBasis
            && StringComparer.Ordinal.Equals(Identifier, other.Identifier)
            && StringComparer.Ordinal.Equals(Display, other.Display)
            && Change == other.Change
            && Comparison == other.Comparison
            && FindingValueEquality.SequenceEqual(Subjects, other.Subjects)
            && FindingValueEquality.SequenceEqual(
                ChangeDescriptions,
                other.ChangeDescriptions);

    public override int GetHashCode()
        => HashCode.Combine(
            SchemaVersion,
            SubjectCoordinateBasis,
            StringComparer.Ordinal.GetHashCode(Identifier),
            StringComparer.Ordinal.GetHashCode(Display),
            Change,
            Comparison,
            FindingValueEquality.SequenceHashCode(Subjects),
            FindingValueEquality.SequenceHashCode(ChangeDescriptions));

    static SubjectCoordinateBasis Validate(SubjectCoordinateBasis basis)
        => basis switch
        {
            SubjectCoordinateBasis.OuterContext => basis,
            SubjectCoordinateBasis.RootRelative => basis,
            _ => throw new ArgumentOutOfRangeException(nameof(basis)),
        };

    static ImmutableArray<ComparisonSubject<T>> ValidateSubjects(
        ImmutableArray<ComparisonSubject<T>> subjects)
    {
        if (subjects.IsDefault)
            throw new ArgumentException("Subjects must be initialized.", nameof(Subjects));
        for (int i = 0; i < subjects.Length; i++)
        {
            if (subjects[i] is null)
            {
                throw new ArgumentException(
                    $"Subject at index {i} must not be null.",
                    nameof(Subjects));
            }
        }
        return subjects;
    }

    static ImmutableArray<ComparisonChangeDescription> ValidateAndCanonicalizeDescriptions(
        ImmutableArray<ComparisonChangeDescription> descriptions)
    {
        if (descriptions.IsDefault)
        {
            throw new ArgumentException(
                "Change descriptions must be initialized.",
                nameof(ChangeDescriptions));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < descriptions.Length; i++)
        {
            ComparisonChangeDescription description =
                descriptions[i]
                ?? throw new ArgumentException(
                    $"Change description at index {i} must not be null.",
                    nameof(ChangeDescriptions));
            if (!ids.Add(description.Id))
            {
                throw new ArgumentException(
                    $"Change description id '{description.Id}' occurs more than once.",
                    nameof(ChangeDescriptions));
            }
        }

        return [.. descriptions.OrderBy(description => description.Id, StringComparer.Ordinal)];
    }

    static void ValidateTopology(
        SubjectCoordinateBasis basis,
        string rootIdentifier,
        string rootDisplay,
        ComparisonSubjectChange rootChange,
        ImmutableArray<ComparisonSubject<T>> subjects,
        ImmutableArray<ComparisonChangeDescription> descriptions)
    {
        var descriptionsById =
            descriptions.ToDictionary(description => description.Id, StringComparer.Ordinal);
        var referenceCounts = descriptions.ToDictionary(
            description => description.Id,
            _ => 0,
            StringComparer.Ordinal);

        ValidateReference(
            rootIdentifier,
            rootDisplay,
            rootChange,
            descriptionsById,
            referenceCounts,
            owner: "Root");

        if (basis == SubjectCoordinateBasis.RootRelative)
            ValidateRootRelativeEndpointAvailability(rootChange, subjects);

        bool hasEndpointTopology =
            rootChange is not ComparisonSubjectChange.Diff
            || subjects.Any(subject => subject.Change is not ComparisonSubjectChange.Diff);
        var beforeIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var afterIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < subjects.Length; i++)
        {
            ComparisonSubject<T> subject = subjects[i];
            ValidateReference(
                subject.Identifier,
                subject.Display,
                subject.Change,
                descriptionsById,
                referenceCounts,
                owner: $"Subject {i}");

            switch (subject.Change)
            {
                case ComparisonSubjectChange.Diff:
                    AddUnique(afterIdentifiers, subject.Identifier, "After");
                    if (hasEndpointTopology)
                        AddUnique(beforeIdentifiers, subject.Identifier, "Before");
                    break;
                case ComparisonSubjectChange.Addition:
                    AddUnique(afterIdentifiers, subject.Identifier, "After");
                    break;
                case ComparisonSubjectChange.Deletion:
                    AddUnique(beforeIdentifiers, subject.Identifier, "Before");
                    break;
                case ComparisonSubjectChange.Rename rename:
                    AddExceptionalSubject(rename.ChangeId);
                    break;
                case ComparisonSubjectChange.Move move:
                    AddExceptionalSubject(move.ChangeId);
                    break;
                case ComparisonSubjectChange.RenameAndMove renameAndMove:
                    AddExceptionalSubject(renameAndMove.ChangeId);
                    break;
                default:
                    throw new ArgumentException(
                        $"Subject {i} has an unknown change type.",
                        nameof(Subjects));
            }

            void AddExceptionalSubject(string changeId)
            {
                AddUnique(afterIdentifiers, subject.Identifier, "After");
                AddUnique(
                    beforeIdentifiers,
                    descriptionsById[changeId].Before.Identifier,
                    "Before");
            }
        }

        foreach ((string id, int count) in referenceCounts)
        {
            if (count != 1)
            {
                throw new ArgumentException(
                    $"Change description '{id}' must be referenced exactly once; found {count} references.",
                    nameof(ChangeDescriptions));
            }
        }
    }

    static void ValidateReference(
        string identifier,
        string display,
        ComparisonSubjectChange change,
        Dictionary<string, ComparisonChangeDescription> descriptions,
        Dictionary<string, int> referenceCounts,
        string owner)
    {
        (string ChangeId, ComparisonExceptionalChangeKind Kind)? exceptional =
            Exceptional(change);
        if (exceptional is not { } value)
            return;

        if (!descriptions.TryGetValue(value.ChangeId, out var description))
        {
            throw new ArgumentException(
                $"{owner} references unknown change description '{value.ChangeId}'.",
                nameof(ChangeDescriptions));
        }
        if (description.Kind != value.Kind)
        {
            throw new ArgumentException(
                $"{owner} change kind does not match description '{value.ChangeId}'.",
                nameof(ChangeDescriptions));
        }
        if (!StringComparer.Ordinal.Equals(identifier, description.After.Identifier)
            || !StringComparer.Ordinal.Equals(display, description.After.Display))
        {
            throw new ArgumentException(
                $"{owner} primary identifier and display must equal description '{value.ChangeId}' After endpoint.",
                nameof(ChangeDescriptions));
        }

        referenceCounts[value.ChangeId]++;
    }

    static void ValidateRootRelativeEndpointAvailability(
        ComparisonSubjectChange rootChange,
        ImmutableArray<ComparisonSubject<T>> subjects)
    {
        (bool RootBefore, bool RootAfter) = RequiredSides(rootChange);
        for (int i = 0; i < subjects.Length; i++)
        {
            (bool SubjectBefore, bool SubjectAfter) = RequiredSides(subjects[i].Change);
            if ((SubjectBefore && !RootBefore) || (SubjectAfter && !RootAfter))
            {
                throw new ArgumentException(
                    $"Root-relative subject {i} requires an endpoint side absent from the root.",
                    nameof(Subjects));
            }
        }
    }

    static (bool Before, bool After) RequiredSides(ComparisonSubjectChange change)
        => change switch
        {
            ComparisonSubjectChange.Diff => (true, true),
            ComparisonSubjectChange.Addition => (false, true),
            ComparisonSubjectChange.Deletion => (true, false),
            ComparisonSubjectChange.Rename => (true, true),
            ComparisonSubjectChange.Move => (true, true),
            ComparisonSubjectChange.RenameAndMove => (true, true),
            _ => throw new ArgumentException(
                "Unknown comparison subject change type.",
                nameof(change)),
        };

    static (string ChangeId, ComparisonExceptionalChangeKind Kind)? Exceptional(
        ComparisonSubjectChange change)
        => change switch
        {
            ComparisonSubjectChange.Diff => null,
            ComparisonSubjectChange.Addition => null,
            ComparisonSubjectChange.Deletion => null,
            ComparisonSubjectChange.Rename rename
                => (rename.ChangeId, ComparisonExceptionalChangeKind.Rename),
            ComparisonSubjectChange.Move move
                => (move.ChangeId, ComparisonExceptionalChangeKind.Move),
            ComparisonSubjectChange.RenameAndMove renameAndMove
                => (renameAndMove.ChangeId, ComparisonExceptionalChangeKind.RenameAndMove),
            _ => throw new ArgumentException(
                "Unknown comparison subject change type.",
                nameof(change)),
        };

    static void AddUnique(
        HashSet<string> identifiers,
        string identifier,
        string endpoint)
    {
        if (!identifiers.Add(identifier))
        {
            throw new ArgumentException(
                $"{endpoint} subject identifier '{identifier}' occurs more than once.",
                nameof(Subjects));
        }
    }
}
