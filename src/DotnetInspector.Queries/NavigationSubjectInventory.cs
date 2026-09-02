using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed evidence that prevented one complete Type inventory.</summary>
public abstract record NavigationInventoryEvidence
{
    private protected NavigationInventoryEvidence(
        StructuralSubjectIdentity.LibrarySubject library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Library = library;
    }

    public StructuralSubjectIdentity.LibrarySubject Library { get; }

    /// <summary>The Library participant could not be opened.</summary>
    public sealed record ParticipantRejected : NavigationInventoryEvidence
    {
        internal ParticipantRejected(
            StructuralSubjectIdentity.LibrarySubject library,
            AssemblyContextSubject producerSubject,
            CandidateOpenFailure failure)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(producerSubject);
            ArgumentNullException.ThrowIfNull(failure);
            ProducerSubject = producerSubject;
            Failure = failure;
        }

        public AssemblyContextSubject ProducerSubject { get; }
        public CandidateOpenFailure Failure { get; }
    }

    /// <summary>The Library participant failed during inspection.</summary>
    public sealed record ParticipantFailed : NavigationInventoryEvidence
    {
        internal ParticipantFailed(
            StructuralSubjectIdentity.LibrarySubject library,
            AssemblyContextSubject producerSubject,
            Exception error)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(producerSubject);
            ArgumentNullException.ThrowIfNull(error);
            ProducerSubject = producerSubject;
            Error = error;
        }

        public AssemblyContextSubject ProducerSubject { get; }
        public Exception Error { get; }
    }

    /// <summary>A metadata row failed while the Library surface was produced.</summary>
    public sealed record InspectionFailed : NavigationInventoryEvidence
    {
        internal InspectionFailed(
            StructuralSubjectIdentity.LibrarySubject library,
            ApiSurfaceInspectionFailure failure)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public ApiSurfaceInspectionFailure Failure { get; }
    }

    /// <summary>A returned Type row lacked exact definition identity.</summary>
    public sealed record TypeIdentityMissing : NavigationInventoryEvidence
    {
        internal TypeIdentityMissing(
            StructuralSubjectIdentity.LibrarySubject library,
            ApiType producerRow)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(producerRow);
            ProducerRow = producerRow;
        }

        public ApiType ProducerRow { get; }
    }

    /// <summary>A projected Member lacked an exact local declaring Type.</summary>
    public sealed record MemberIdentityMissing : NavigationInventoryEvidence
    {
        internal MemberIdentityMissing(
            StructuralSubjectIdentity.LibrarySubject library,
            ApiType containingType,
            ApiMember producerRow)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(containingType);
            ArgumentNullException.ThrowIfNull(producerRow);
            ContainingType = containingType;
            ProducerRow = producerRow;
        }

        public ApiType ContainingType { get; }
        public ApiMember ProducerRow { get; }
    }

    /// <summary>A projection bound omitted this Library and every later row.</summary>
    public sealed record ProjectionOmitted : NavigationInventoryEvidence
    {
        internal ProjectionOmitted(
            StructuralSubjectIdentity.LibrarySubject library,
            ApiSurfaceProjectionTruncation truncation)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(truncation);
            Truncation = truncation;
        }

        public ApiSurfaceProjectionTruncation Truncation { get; }
    }
}

/// <summary>One exact Member row in producer-issued order.</summary>
public sealed record NavigationMemberInventoryRow
{
    internal NavigationMemberInventoryRow(
        ApiMember producerRow,
        StructuralSubjectIdentity.MemberSubject subject)
    {
        ArgumentNullException.ThrowIfNull(producerRow);
        ArgumentNullException.ThrowIfNull(subject);
        ProducerRow = producerRow;
        Subject = subject;
    }

    public ApiMember ProducerRow { get; }
    public StructuralSubjectIdentity.MemberSubject Subject { get; }
}

/// <summary>One exact Type row and its Members in producer-issued order.</summary>
public sealed record NavigationTypeInventoryRow
{
    internal NavigationTypeInventoryRow(
        ApiType producerRow,
        StructuralSubjectIdentity.TypeSubject subject,
        ApiAccessibilityBucket accessibility,
        ImmutableArray<NavigationMemberInventoryRow> members)
    {
        ArgumentNullException.ThrowIfNull(producerRow);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(accessibility);
        if (members.IsDefault)
        {
            throw new ArgumentException(
                "Member rows must be an initialized immutable array.",
                nameof(members));
        }
        foreach (NavigationMemberInventoryRow? member in members)
        {
            if (member is null
                || member.Subject.DeclaringType != subject)
            {
                throw new ArgumentException(
                    "Every exact Member row must belong to its containing Type.",
                    nameof(members));
            }
        }

        ProducerRow = producerRow;
        Subject = subject;
        Accessibility = accessibility;
        Members = members;
    }

    public ApiType ProducerRow { get; }
    public StructuralSubjectIdentity.TypeSubject Subject { get; }
    public ApiAccessibilityBucket Accessibility { get; }
    public ImmutableArray<NavigationMemberInventoryRow> Members { get; }

    public bool Equals(NavigationTypeInventoryRow? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && ReferenceEquals(ProducerRow, other.ProducerRow)
        && Subject == other.Subject
        && Accessibility == other.Accessibility
        && Members.SequenceEqual(other.Members);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProducerRow);
        hash.Add(Subject);
        hash.Add(Accessibility);
        foreach (NavigationMemberInventoryRow member in Members)
            hash.Add(member);
        return hash.ToHashCode();
    }
}

/// <summary>The classified Type inventory for one scope.</summary>
public abstract record NavigationTypeInventoryOutcome
{
    private protected NavigationTypeInventoryOutcome(
        ImmutableArray<NavigationTypeInventoryRow> rows,
        ImmutableArray<NavigationInventoryEvidence> evidence)
    {
        if (rows.IsDefault)
        {
            throw new ArgumentException(
                "Type rows must be an initialized immutable array.",
                nameof(rows));
        }
        if (evidence.IsDefault)
        {
            throw new ArgumentException(
                "Inventory evidence must be an initialized immutable array.",
                nameof(evidence));
        }
        if (rows.Any(static row => row is null)
            || evidence.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Inventory rows and evidence cannot contain null values.");
        }

        Rows = rows;
        Evidence = evidence;
    }

    public ImmutableArray<NavigationTypeInventoryRow> Rows { get; }
    public ImmutableArray<NavigationInventoryEvidence> Evidence { get; }

    /// <summary>At least one trustworthy exact Type row is available.</summary>
    public sealed record Available : NavigationTypeInventoryOutcome
    {
        internal Available(
            ImmutableArray<NavigationTypeInventoryRow> rows,
            ImmutableArray<NavigationInventoryEvidence> evidence)
            : base(rows, evidence)
        {
            if (rows.IsEmpty)
            {
                throw new ArgumentException(
                    "An available inventory requires at least one Type row.",
                    nameof(rows));
            }
        }

        public bool Equals(Available? other) =>
            ReferenceEquals(this, other)
            || other is not null
            && Rows.SequenceEqual(other.Rows)
            && Evidence.SequenceEqual(other.Evidence);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (NavigationTypeInventoryRow row in Rows)
                hash.Add(row);
            foreach (NavigationInventoryEvidence item in Evidence)
                hash.Add(item);
            return hash.ToHashCode();
        }
    }

    /// <summary>Complete successful production proved the Type inventory empty.</summary>
    public sealed record Unavailable : NavigationTypeInventoryOutcome
    {
        internal Unavailable()
            : base([], [])
        {
        }
    }

    /// <summary>No trustworthy Type row exists and completeness was not established.</summary>
    public sealed record Failed : NavigationTypeInventoryOutcome
    {
        internal Failed(ImmutableArray<NavigationInventoryEvidence> evidence)
            : base([], evidence)
        {
            if (evidence.IsEmpty)
            {
                throw new ArgumentException(
                    "A failed inventory requires typed failure evidence.",
                    nameof(evidence));
            }
        }

        public bool Equals(Failed? other) =>
            ReferenceEquals(this, other)
            || other is not null
            && Evidence.SequenceEqual(other.Evidence);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (NavigationInventoryEvidence item in Evidence)
                hash.Add(item);
            return hash.ToHashCode();
        }
    }
}

/// <summary>One admitted Library and its generation-free Type inventory.</summary>
public sealed record NavigationLibraryInventory
{
    internal NavigationLibraryInventory(
        StructuralSubjectIdentity.LibrarySubject subject,
        bool isPrimary,
        NavigationTypeInventoryOutcome types)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(types);
        if (types.Rows.Any(row => row.Subject.Library != subject)
            || types.Evidence.Any(item => item.Library != subject))
        {
            throw new ArgumentException(
                "Library inventory rows and evidence must retain the exact Library.",
                nameof(types));
        }

        Subject = subject;
        IsPrimary = isPrimary;
        Types = types;
    }

    public StructuralSubjectIdentity.LibrarySubject Subject { get; }
    public bool IsPrimary { get; }
    public NavigationTypeInventoryOutcome Types { get; }
}

/// <summary>
/// Generation-free classified subject inventory over one realized coordinate.
/// </summary>
public sealed record NavigationSubjectInventory
{
    internal NavigationSubjectInventory(
        StructuralSubjectIdentity.RootSubject root,
        ImmutableArray<NavigationLibraryInventory> libraries,
        NavigationTypeInventoryOutcome types,
        ImmutableArray<NavigationInitialLibraryCandidate> initialCandidates)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(types);
        if (libraries.IsDefault || initialCandidates.IsDefault)
        {
            throw new ArgumentException(
                "Inventory collections must be initialized.");
        }
        if (libraries.Length != initialCandidates.Length)
        {
            throw new ArgumentException(
                "Inventory collections must retain every exact Library.");
        }
        for (int index = 0; index < libraries.Length; index++)
        {
            NavigationLibraryInventory? library = libraries[index];
            NavigationInitialLibraryCandidate? candidate =
                initialCandidates[index];
            if (library is null
                || candidate is null
                || library.Subject.Coordinate != root.Coordinate
                || candidate.Subject != library.Subject
                || candidate.IsPrimary != library.IsPrimary
                || !candidate.Types.Select(static item => item.Subject)
                    .SequenceEqual(
                        library.Types.Rows.Select(static row => row.Subject))
                || !candidate.Types.Select(static item => item.Accessibility)
                    .SequenceEqual(
                        library.Types.Rows.Select(
                            static row => row.Accessibility)))
            {
                throw new ArgumentException(
                    "Initial candidates must exactly project each Library inventory.",
                    nameof(initialCandidates));
            }
        }
        if (!types.Rows.SequenceEqual(
                libraries.SelectMany(static library => library.Types.Rows))
            || !types.Evidence.SequenceEqual(
                libraries.SelectMany(
                    static library => library.Types.Evidence)))
        {
            throw new ArgumentException(
                "The aggregate inventory must equal the ordered Library rollup.",
                nameof(types));
        }

        Root = root;
        Libraries = libraries;
        Types = types;
        InitialCandidates = initialCandidates;
    }

    public StructuralSubjectIdentity.RootSubject Root { get; }
    public ImmutableArray<NavigationLibraryInventory> Libraries { get; }
    public NavigationTypeInventoryOutcome Types { get; }
    public ImmutableArray<NavigationInitialLibraryCandidate> InitialCandidates
    {
        get;
    }

    public bool Equals(NavigationSubjectInventory? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Root == other.Root
        && Libraries.SequenceEqual(other.Libraries)
        && Types == other.Types
        && InitialCandidates.SequenceEqual(other.InitialCandidates);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        foreach (NavigationLibraryInventory library in Libraries)
            hash.Add(library);
        hash.Add(Types);
        foreach (NavigationInitialLibraryCandidate candidate
            in InitialCandidates)
        {
            hash.Add(candidate);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Classifies bounded API-surface evidence without committing navigation state.
/// </summary>
public static class NavigationSubjectInventoryClassification
{
    public static NavigationSubjectInventory Classify(
        StructuralSubjectIdentity.RootSubject root,
        ImmutableArray<WorkspaceContextMember> libraries,
        WorkspaceContextMember? primaryLibrary,
        AssemblyContextApiSurfaceResult surface)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(surface);
        if (libraries.IsDefault)
        {
            throw new ArgumentException(
                "Libraries must be an initialized immutable array.",
                nameof(libraries));
        }

        int primaryIndex = ValidateLibraries(root, libraries, primaryLibrary);
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries =
            surface.Assemblies?.Assemblies
            ?? throw new ArgumentException(
                "The API-surface result must retain participant outcomes.",
                nameof(surface));
        ValidateJoin(libraries, entries, surface.Truncation);

        var classified = ImmutableArray.CreateBuilder<NavigationLibraryInventory>(
            libraries.Length);
        for (int index = 0; index < entries.Length; index++)
        {
            StructuralSubjectIdentity.LibrarySubject library =
                StructuralSubjectIdentity.ForLibrary(libraries[index]);
            classified.Add(
                new NavigationLibraryInventory(
                    library,
                    index == primaryIndex,
                    ClassifyEntry(library, entries[index])));
        }

        for (int index = entries.Length; index < libraries.Length; index++)
        {
            StructuralSubjectIdentity.LibrarySubject library =
                StructuralSubjectIdentity.ForLibrary(libraries[index]);
            var omission = new NavigationInventoryEvidence.ProjectionOmitted(
                library,
                surface.Truncation!);
            classified.Add(
                new NavigationLibraryInventory(
                    library,
                    index == primaryIndex,
                    new NavigationTypeInventoryOutcome.Failed([omission])));
        }

        ImmutableArray<NavigationLibraryInventory> libraryInventory =
            classified.ToImmutable();
        var rows = libraryInventory
            .SelectMany(static library => library.Types.Rows)
            .ToImmutableArray();
        var evidence = libraryInventory
            .SelectMany(static library => library.Types.Evidence)
            .ToImmutableArray();
        NavigationTypeInventoryOutcome types =
            Outcome(rows, evidence);
        ImmutableArray<NavigationInitialLibraryCandidate> initialCandidates =
            [
                .. libraryInventory.Select(
                    static library =>
                        new NavigationInitialLibraryCandidate(
                            library.Subject,
                            library.IsPrimary,
                            [
                                .. library.Types.Rows.Select(
                                    static row =>
                                        new NavigationInitialTypeCandidate(
                                            row.Subject,
                                            row.Accessibility)),
                            ])),
            ];
        return new NavigationSubjectInventory(
            root,
            libraryInventory,
            types,
            initialCandidates);
    }

    static int ValidateLibraries(
        StructuralSubjectIdentity.RootSubject root,
        ImmutableArray<WorkspaceContextMember> libraries,
        WorkspaceContextMember? primaryLibrary)
    {
        var registrations = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        StructuralSubjectIdentity.LibrarySubject? primarySubject =
            primaryLibrary is null
                ? null
                : StructuralSubjectIdentity.ForLibrary(primaryLibrary);
        int primaryIndex = -1;
        for (int index = 0; index < libraries.Length; index++)
        {
            WorkspaceContextMember? library = libraries[index];
            if (library is null || library.Realized != root.Coordinate)
            {
                throw new ArgumentException(
                    "Every Library must belong to the Root coordinate.",
                    nameof(libraries));
            }

            AssemblyAcquisitionRegistration registration =
                library.Participant.Assembly.Registration;
            if (!registrations.Add(registration))
            {
                throw new ArgumentException(
                    "Libraries must have distinct exact registrations.",
                    nameof(libraries));
            }
            if (primaryLibrary is not null
                && StructuralSubjectIdentity.ForLibrary(library)
                    == primarySubject)
            {
                primaryIndex = index;
            }
        }

        if (primaryLibrary is not null && primaryIndex < 0)
        {
            throw new ArgumentException(
                "The primary Library must be one of the admitted Libraries.",
                nameof(primaryLibrary));
        }

        return primaryIndex;
    }

    static void ValidateJoin(
        ImmutableArray<WorkspaceContextMember> libraries,
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries,
        ApiSurfaceProjectionTruncation? truncation)
    {
        if (entries.IsDefault || entries.Length > libraries.Length)
        {
            throw new ArgumentException(
                "Participant outcomes must be an initialized Library prefix.",
                nameof(entries));
        }
        for (int index = 0; index < entries.Length; index++)
        {
            AssemblyContextEntry<AssemblyApiSurface>? entry = entries[index];
            if (entry?.Subject is null
                || !ReferenceEquals(
                    entry.Subject.Registration,
                    libraries[index].Participant.Assembly.Registration))
            {
                throw new ArgumentException(
                    "Participant outcomes must exact-join the Library prefix by registration.",
                    nameof(entries));
            }
        }

        int omitted = libraries.Length - entries.Length;
        if (truncation is null)
        {
            if (omitted != 0)
            {
                throw new ArgumentException(
                    "A complete projection must contain every admitted Library.",
                    nameof(entries));
            }
            return;
        }

        if (omitted == 0
            || truncation.OmittedParticipants != omitted
            || truncation.ProjectedParticipants != entries.Length)
        {
            throw new ArgumentException(
                "Projection truncation must account for the exact omitted Library suffix.",
                nameof(truncation));
        }
    }

    static NavigationTypeInventoryOutcome ClassifyEntry(
        StructuralSubjectIdentity.LibrarySubject library,
        AssemblyContextEntry<AssemblyApiSurface> entry)
        => entry switch
        {
            AssemblyContextEntry<AssemblyApiSurface>.Available available =>
                ClassifyAvailable(library, available.Value),
            AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected =>
                new NavigationTypeInventoryOutcome.Failed(
                    [
                        new NavigationInventoryEvidence.ParticipantRejected(
                            library,
                            rejected.Subject,
                            rejected.Failure),
                    ]),
            AssemblyContextEntry<AssemblyApiSurface>.Failed failed =>
                new NavigationTypeInventoryOutcome.Failed(
                    [
                        new NavigationInventoryEvidence.ParticipantFailed(
                            library,
                            failed.Subject,
                            failed.Error),
                    ]),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context API-surface outcome."),
        };

    static NavigationTypeInventoryOutcome ClassifyAvailable(
        StructuralSubjectIdentity.LibrarySubject library,
        AssemblyApiSurface? available)
    {
        if (available?.Surface?.Types is null
            || available.InspectionFailures.IsDefault)
        {
            throw new ArgumentException(
                "An available API surface must retain rows and inspection evidence.",
                nameof(available));
        }

        var rows = ImmutableArray.CreateBuilder<NavigationTypeInventoryRow>();
        var evidence =
            ImmutableArray.CreateBuilder<NavigationInventoryEvidence>();
        foreach (ApiSurfaceInspectionFailure? failure
            in available.InspectionFailures)
        {
            if (failure is null)
            {
                throw new ArgumentException(
                    "Inspection evidence cannot contain null values.",
                    nameof(available));
            }
            evidence.Add(
                new NavigationInventoryEvidence.InspectionFailed(
                    library,
                    failure));
        }

        var subjectByType =
            new Dictionary<
                ApiType,
                StructuralSubjectIdentity.TypeSubject>(
                    ReferenceEqualityComparer.Instance);
        foreach (ApiType? type in available.Surface.Types)
        {
            if (type is null)
            {
                throw new ArgumentException(
                    "API surfaces cannot contain null Type rows.",
                    nameof(available));
            }
            if (type.DefinitionName is not { } definition)
            {
                evidence.Add(
                    new NavigationInventoryEvidence.TypeIdentityMissing(
                        library,
                        type));
                continue;
            }
            StructuralSubjectIdentity.TypeSubject subject =
                StructuralSubjectIdentity.ForType(library, definition);
            subjectByType.Add(type, subject);
        }

        foreach (ApiType? type in available.Surface.Types)
        {
            if (type is null || !subjectByType.TryGetValue(
                type,
                out StructuralSubjectIdentity.TypeSubject? typeSubject))
            {
                continue;
            }
            if (type.Members is null)
            {
                throw new ArgumentException(
                    "API Type rows must retain their Member rows.",
                    nameof(available));
            }

            var members =
                ImmutableArray.CreateBuilder<NavigationMemberInventoryRow>(
                    type.Members.Count);
            foreach (ApiMember? member in type.Members)
            {
                if (member is null)
                {
                    throw new ArgumentException(
                        "API Type rows cannot contain null Member rows.",
                        nameof(available));
                }
                if (member.DeclaringTypeCanonicalName is not null)
                {
                    evidence.Add(
                        new NavigationInventoryEvidence.MemberIdentityMissing(
                            library,
                            type,
                            member));
                    continue;
                }
                members.Add(
                    new NavigationMemberInventoryRow(
                        member,
                        StructuralSubjectIdentity.ForMember(
                            typeSubject,
                            ApiMemberIdentity.GetMemberAnchor(type, member))));
            }

            rows.Add(
                new NavigationTypeInventoryRow(
                    type,
                    typeSubject,
                    ApiAccessibility.Classify(type.Accessibility),
                    members.ToImmutable()));
        }

        return Outcome(rows.ToImmutable(), evidence.ToImmutable());
    }

    static NavigationTypeInventoryOutcome Outcome(
        ImmutableArray<NavigationTypeInventoryRow> rows,
        ImmutableArray<NavigationInventoryEvidence> evidence) =>
        !rows.IsEmpty
            ? new NavigationTypeInventoryOutcome.Available(rows, evidence)
            : evidence.IsEmpty
                ? new NavigationTypeInventoryOutcome.Unavailable()
                : new NavigationTypeInventoryOutcome.Failed(evidence);
}
