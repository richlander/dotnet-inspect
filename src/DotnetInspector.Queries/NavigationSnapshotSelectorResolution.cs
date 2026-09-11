namespace DotnetInspector.Queries;

/// <summary>
/// Typed rebinding of one portable CLI selector against a complete evaluated
/// Navigation snapshot.
/// </summary>
public abstract record NavigationSnapshotSelectorResolution
{
    private protected NavigationSnapshotSelectorResolution(
        NavigationWorkspaceSnapshot snapshot,
        string selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        Snapshot = snapshot;
        Selector = selector;
    }

    public NavigationWorkspaceSnapshot Snapshot { get; }

    public string Selector { get; }

    public sealed record Selected : NavigationSnapshotSelectorResolution
    {
        internal Selected(
            NavigationWorkspaceSnapshot snapshot,
            string selector,
            StructuralSubjectIdentity subject)
            : base(snapshot, selector)
        {
            ArgumentNullException.ThrowIfNull(subject);
            Subject = subject;
        }

        public StructuralSubjectIdentity Subject { get; }
    }

    public sealed record NotPresent : NavigationSnapshotSelectorResolution
    {
        internal NotPresent(
            NavigationWorkspaceSnapshot snapshot,
            string selector)
            : base(snapshot, selector)
        {
        }
    }

    public sealed record Ambiguous : NavigationSnapshotSelectorResolution
    {
        internal Ambiguous(
            NavigationWorkspaceSnapshot snapshot,
            string selector)
            : base(snapshot, selector)
        {
        }
    }

    public sealed record Incomplete : NavigationSnapshotSelectorResolution
    {
        internal Incomplete(
            NavigationWorkspaceSnapshot snapshot,
            string selector,
            NavigationTypeInventoryOutcome inventory)
            : base(snapshot, selector)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            Inventory = inventory;
        }

        public NavigationTypeInventoryOutcome Inventory { get; }
    }

    public sealed record Unavailable : NavigationSnapshotSelectorResolution
    {
        internal Unavailable(
            NavigationWorkspaceSnapshot snapshot,
            string selector)
            : base(snapshot, selector)
        {
        }
    }

    public sealed record Invalid : NavigationSnapshotSelectorResolution
    {
        internal Invalid(
            NavigationWorkspaceSnapshot snapshot,
            string selector)
            : base(snapshot, selector)
        {
        }
    }
}

/// <summary>Resolves portable selectors without discarding snapshot evidence.</summary>
public static class NavigationSnapshotSelector
{
    public static NavigationSnapshotSelectorResolution Invalid(
        NavigationWorkspaceSnapshot snapshot,
        string selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return new NavigationSnapshotSelectorResolution.Invalid(
            snapshot,
            selector);
    }

    public static NavigationSnapshotSelectorResolution ResolveLibrary(
        NavigationWorkspaceSnapshot snapshot,
        string assetId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        NavigationLibraryDescriptor[] matches =
        [
            .. snapshot.Libraries.Where(library =>
                library.Subject is StructuralSubjectIdentity.LibrarySubject
                && library.Asset?.Id.Equals(
                    assetId,
                    StringComparison.Ordinal) == true),
        ];
        return matches.Length switch
        {
            0 => new NavigationSnapshotSelectorResolution.NotPresent(
                snapshot,
                assetId),
            1 => new NavigationSnapshotSelectorResolution.Selected(
                snapshot,
                assetId,
                matches[0].Subject),
            _ => new NavigationSnapshotSelectorResolution.Ambiguous(
                snapshot,
                assetId),
        };
    }

    public static NavigationSnapshotSelectorResolution ResolveAllLibraries(
        NavigationWorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        const string selector = "All libraries";
        NavigationLibraryDescriptor[] matches =
        [
            .. snapshot.Libraries.Where(library =>
                library.Subject
                    is StructuralSubjectIdentity.AllLibrariesSubject),
        ];
        return matches.Length switch
        {
            0 => new NavigationSnapshotSelectorResolution.Unavailable(
                snapshot,
                selector),
            1 => new NavigationSnapshotSelectorResolution.Selected(
                snapshot,
                selector,
                matches[0].Subject),
            _ => new NavigationSnapshotSelectorResolution.Ambiguous(
                snapshot,
                selector),
        };
    }

    public static NavigationSnapshotSelectorResolution ResolveType(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity.LibrarySubject library,
        string fullName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        NavigationLibraryInventory inventory = LibraryInventory(
            snapshot,
            library);
        NavigationTypeInventoryRow[] matches =
        [
            .. inventory.Types.Rows.Where(row =>
                row.Subject.Identity.Type.ToEscapedFullName().Equals(
                    fullName,
                    StringComparison.Ordinal)),
        ];
        return Resolve(snapshot, fullName, inventory.Types, matches.Select(
            static row => (StructuralSubjectIdentity)row.Subject));
    }

    public static NavigationSnapshotSelectorResolution ResolveMember(
        NavigationWorkspaceSnapshot snapshot,
        NavigationTypeInventoryRow containingType,
        string stableSelector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(containingType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableSelector);
        NavigationLibraryInventory inventory = LibraryInventory(
            snapshot,
            containingType.Subject.Library);
        if (!inventory.Types.Rows.Any(candidate =>
            ReferenceEquals(candidate, containingType)))
        {
            throw new ArgumentException(
                "The containing Type row must come from the exact evaluated "
                    + "snapshot inventory.",
                nameof(containingType));
        }
        NavigationMemberInventoryRow[] matches =
        [
            .. containingType.Members.Where(member =>
                member.ContainingType == containingType.Subject
                && member.Subject.DeclaringType == containingType.Subject
                && member.Subject.Identity.Member.StableSelector.Equals(
                    stableSelector,
                    StringComparison.Ordinal)),
        ];
        return Resolve(
            snapshot,
            stableSelector,
            inventory.Types,
            matches.Select(
                static row => (StructuralSubjectIdentity)row.Subject));
    }

    static NavigationSnapshotSelectorResolution Resolve(
        NavigationWorkspaceSnapshot snapshot,
        string selector,
        NavigationTypeInventoryOutcome inventory,
        IEnumerable<StructuralSubjectIdentity> candidates)
    {
        StructuralSubjectIdentity[] matches = [.. candidates];
        if (matches.Length == 1)
        {
            return new NavigationSnapshotSelectorResolution.Selected(
                snapshot,
                selector,
                matches[0]);
        }
        if (matches.Length > 1)
        {
            return new NavigationSnapshotSelectorResolution.Ambiguous(
                snapshot,
                selector);
        }
        return inventory.Evidence.IsEmpty
            ? new NavigationSnapshotSelectorResolution.NotPresent(
                snapshot,
                selector)
            : new NavigationSnapshotSelectorResolution.Incomplete(
                snapshot,
                selector,
                inventory);
    }

    static NavigationLibraryInventory LibraryInventory(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity.LibrarySubject library) =>
        snapshot.Inventory?.Libraries.SingleOrDefault(candidate =>
            candidate.Subject == library)
        ?? throw new ArgumentException(
            "The selected Library must belong to the evaluated snapshot.",
            nameof(library));
}
