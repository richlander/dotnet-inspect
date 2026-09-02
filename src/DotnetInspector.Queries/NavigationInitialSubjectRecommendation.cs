using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// One trustworthy Type candidate in its producer-issued navigation order.
/// </summary>
public sealed record NavigationInitialTypeCandidate
{
    public NavigationInitialTypeCandidate(
        StructuralSubjectIdentity.TypeSubject subject,
        ApiAccessibilityBucket accessibility)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(accessibility);
        ApiAccessibilityBucket? canonicalAccessibility =
            ApiAccessibility.Values.FirstOrDefault(
                value =>
                    value.Id == accessibility.Id
                    && value.Label == accessibility.Label
                    && value.Order == accessibility.Order
                    && value.IsDefault == accessibility.IsDefault);
        if (accessibility.Count < 0
            || canonicalAccessibility is null)
        {
            throw new ArgumentException(
                "Type candidates require a product-owned accessibility bucket.",
                nameof(accessibility));
        }

        Subject = subject;
        Accessibility = canonicalAccessibility;
    }

    public StructuralSubjectIdentity.TypeSubject Subject { get; }
    public ApiAccessibilityBucket Accessibility { get; }
}

/// <summary>
/// One available Library and its trustworthy Types in producer order.
/// </summary>
public sealed record NavigationInitialLibraryCandidate
{
    public NavigationInitialLibraryCandidate(
        StructuralSubjectIdentity.LibrarySubject subject,
        bool isPrimary,
        ImmutableArray<NavigationInitialTypeCandidate> types)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (types.IsDefault)
        {
            throw new ArgumentException(
                "Type candidates must be an initialized immutable array.",
                nameof(types));
        }
        foreach (NavigationInitialTypeCandidate? type in types)
        {
            if (type is null || type.Subject.Library != subject)
            {
                throw new ArgumentException(
                    "Every Type candidate must belong to its containing Library.",
                    nameof(types));
            }
        }

        Subject = subject;
        IsPrimary = isPrimary;
        Types = types;
    }

    public StructuralSubjectIdentity.LibrarySubject Subject { get; }
    public bool IsPrimary { get; }
    public ImmutableArray<NavigationInitialTypeCandidate> Types { get; }

    public bool Equals(NavigationInitialLibraryCandidate? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Subject == other.Subject
        && IsPrimary == other.IsPrimary
        && Types.SequenceEqual(other.Types);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Subject);
        hash.Add(IsPrimary);
        foreach (NavigationInitialTypeCandidate type in Types)
            hash.Add(type);
        return hash.ToHashCode();
    }
}

/// <summary>The exact preclassified input retained for initial recommendation.</summary>
public sealed record NavigationInitialSubjectBasis
{
    internal NavigationInitialSubjectBasis(
        StructuralSubjectIdentity.RootSubject root,
        StructuralSubjectIdentity.AllLibrariesSubject? allLibraries,
        ImmutableArray<NavigationInitialLibraryCandidate> libraries)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (libraries.IsDefault)
        {
            throw new ArgumentException(
                "Library candidates must be an initialized immutable array.",
                nameof(libraries));
        }
        if (allLibraries is not null
            && allLibraries.Coordinate != root.Coordinate)
        {
            throw new ArgumentException(
                "The aggregate Library candidate must belong to the Root coordinate.",
                nameof(allLibraries));
        }

        var identities =
            new HashSet<StructuralSubjectIdentity.LibrarySubject>();
        bool hasPrimary = false;
        foreach (NavigationInitialLibraryCandidate? library in libraries)
        {
            if (library is null
                || library.Subject.Coordinate != root.Coordinate)
            {
                throw new ArgumentException(
                    "Every Library candidate must belong to the Root coordinate.",
                    nameof(libraries));
            }
            if (!identities.Add(library.Subject))
            {
                throw new ArgumentException(
                    "Library candidates must have distinct exact identities.",
                    nameof(libraries));
            }
            if (library.IsPrimary && hasPrimary)
            {
                throw new ArgumentException(
                    "At most one Library candidate may be primary.",
                    nameof(libraries));
            }
            hasPrimary |= library.IsPrimary;
        }

        Root = root;
        AllLibraries = allLibraries;
        Libraries = libraries;
    }

    public StructuralSubjectIdentity.RootSubject Root { get; }
    public StructuralSubjectIdentity.AllLibrariesSubject? AllLibraries
    {
        get;
    }
    public ImmutableArray<NavigationInitialLibraryCandidate> Libraries
    {
        get;
    }

    public bool Equals(NavigationInitialSubjectBasis? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Root == other.Root
        && AllLibraries == other.AllLibraries
        && Libraries.SequenceEqual(other.Libraries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        hash.Add(AllLibraries);
        foreach (NavigationInitialLibraryCandidate library in Libraries)
            hash.Add(library);
        return hash.ToHashCode();
    }
}

/// <summary>An exact recommended structural subject and its retained basis.</summary>
public sealed record NavigationInitialSubjectOutcome
{
    internal NavigationInitialSubjectOutcome(
        NavigationInitialSubjectBasis basis,
        StructuralSubjectIdentity subject)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(subject);
        if (!Contains(basis, subject))
        {
            throw new ArgumentException(
                "The recommended subject must occur in the retained basis.",
                nameof(subject));
        }

        Basis = basis;
        Subject = subject;
    }

    public NavigationInitialSubjectBasis Basis { get; }
    public StructuralSubjectIdentity Subject { get; }

    static bool Contains(
        NavigationInitialSubjectBasis basis,
        StructuralSubjectIdentity subject) =>
        subject == basis.Root
        || subject == basis.AllLibraries
        || basis.Libraries.Any(
            library =>
                subject == library.Subject
                || library.Types.Any(type => subject == type.Subject));
}

/// <summary>Pure product policy for an initial structural subject.</summary>
public static class NavigationInitialSubjectRecommendation
{
    public static NavigationInitialSubjectOutcome Recommend(
        StructuralSubjectIdentity.RootSubject root,
        StructuralSubjectIdentity.AllLibrariesSubject? allLibraries,
        ImmutableArray<NavigationInitialLibraryCandidate> libraries)
    {
        var basis = new NavigationInitialSubjectBasis(
            root,
            allLibraries,
            libraries);
        StructuralSubjectIdentity subject =
            FindType(basis, isPrimary: true, isDefault: true)
            ?? FindType(basis, isPrimary: false, isDefault: true)
            ?? FindType(basis, isPrimary: true, isDefault: false)
            ?? FindType(basis, isPrimary: false, isDefault: false)
            ?? (StructuralSubjectIdentity?)basis.AllLibraries
            ?? (StructuralSubjectIdentity?)basis.Libraries.FirstOrDefault(
                library => library.IsPrimary)?.Subject
            ?? (StructuralSubjectIdentity?)basis.Libraries
                .FirstOrDefault()?.Subject
            ?? basis.Root;
        return new NavigationInitialSubjectOutcome(basis, subject);
    }

    static StructuralSubjectIdentity.TypeSubject? FindType(
        NavigationInitialSubjectBasis basis,
        bool isPrimary,
        bool isDefault)
    {
        foreach (NavigationInitialLibraryCandidate library in basis.Libraries)
        {
            if (library.IsPrimary != isPrimary)
                continue;
            foreach (NavigationInitialTypeCandidate type in library.Types)
            {
                if (type.Accessibility.IsDefault == isDefault)
                    return type.Subject;
            }
        }
        return null;
    }
}
