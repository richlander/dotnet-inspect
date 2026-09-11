using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Queries;

/// <summary>A canonical, globally unique product view-facet identifier.</summary>
public sealed record ViewFacetId
{
    public const int MaximumLength = 80;

    public ViewFacetId(string value)
    {
        if (!TryGetKind(value, out _))
        {
            throw new ArgumentException(
                "A view-facet id must be a canonical absolute subject-prefixed identifier.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    internal static bool TryGetKind(
        string? value,
        out StructuralSubjectKind kind)
    {
        kind = default;
        if (value is null
            || value.Length == 0
            || value.Length > MaximumLength)
        {
            return false;
        }

        int dot = value.IndexOf('.');
        if (dot <= 0 || dot == value.Length - 1)
            return false;

        ReadOnlySpan<char> prefix = value.AsSpan(0, dot);
        if (prefix.SequenceEqual("root"))
            kind = StructuralSubjectKind.Root;
        else if (prefix.SequenceEqual("library"))
            kind = StructuralSubjectKind.Library;
        else if (prefix.SequenceEqual("type"))
            kind = StructuralSubjectKind.Type;
        else if (prefix.SequenceEqual("member"))
            kind = StructuralSubjectKind.Member;
        else
            return false;

        ReadOnlySpan<char> name = value.AsSpan(dot + 1);
        if (!IsLowerAsciiLetter(name[0]))
            return false;

        bool afterSeparator = false;
        for (int i = 1; i < name.Length; i++)
        {
            char character = name[i];
            if (character == '-')
            {
                if (afterSeparator || i == name.Length - 1)
                    return false;
                afterSeparator = true;
                continue;
            }

            if (!IsLowerAsciiLetter(character)
                && !IsAsciiDigit(character))
            {
                return false;
            }

            afterSeparator = false;
        }

        return true;
    }

    static bool IsLowerAsciiLetter(char value) =>
        value is >= 'a' and <= 'z';

    static bool IsAsciiDigit(char value) =>
        value is >= '0' and <= '9';
}

/// <summary>Semantic roles consumed by adjacent product policy.</summary>
public enum ViewFacetRole
{
    PackageOverview,
    RootOverview,
    LibraryReferences,
    TypeApi,
    MemberOverview,
}

/// <summary>One immutable selectable view-facet descriptor.</summary>
public sealed record ViewFacetDescriptor
{
    public ViewFacetDescriptor(
        ViewFacetId id,
        StructuralSubjectKind kind,
        string title,
        string summary,
        int order,
        ViewFacetRole? role = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!ViewFacetId.TryGetKind(id.Value, out StructuralSubjectKind prefix)
            || prefix != kind)
        {
            throw new ArgumentException(
                "The view-facet id prefix must agree with its structural subject kind.",
                nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (role is not null && !Enum.IsDefined(role.Value))
            throw new ArgumentOutOfRangeException(nameof(role));

        Id = id;
        Kind = kind;
        Title = title;
        Summary = summary;
        Order = order;
        Role = role;
    }

    public ViewFacetId Id { get; }
    public StructuralSubjectKind Kind { get; }
    public string Title { get; }
    public string Summary { get; }
    public int Order { get; }
    public ViewFacetRole? Role { get; }
}

/// <summary>Why one known and applicable facet is unavailable.</summary>
public enum ViewFacetUnavailabilityKind
{
    CapabilityAbsent,
    Retired,
}

/// <summary>Typed unavailability plus owner-issued presentation text.</summary>
public sealed record ViewFacetUnavailableReason
{
    ViewFacetUnavailableReason(
        ViewFacetUnavailabilityKind kind,
        string message)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Message = message;
    }

    public ViewFacetUnavailabilityKind Kind { get; }
    public string Message { get; }

    public static ViewFacetUnavailableReason CapabilityAbsent(
        string message) =>
        new(ViewFacetUnavailabilityKind.CapabilityAbsent, message);

    internal static ViewFacetUnavailableReason Retired() =>
        new(
            ViewFacetUnavailabilityKind.Retired,
            "This view is retired.");
}

/// <summary>Owner-issued typed evidence for failed facet preparation.</summary>
public interface IViewFacetDiagnosticEvidence
{
}

/// <summary>Availability of one known, structurally applicable facet.</summary>
public abstract record ViewFacetAvailability
{
    private protected ViewFacetAvailability()
    {
    }

    public sealed record Available : ViewFacetAvailability
    {
        public static Available Instance { get; } = new();

        private Available()
        {
        }
    }

    public sealed record Unavailable : ViewFacetAvailability
    {
        public Unavailable(ViewFacetUnavailableReason reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        public ViewFacetUnavailableReason Reason { get; }
    }

    public sealed record Failed : ViewFacetAvailability
    {
        public Failed(
            string message,
            IViewFacetDiagnosticEvidence evidence)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ArgumentNullException.ThrowIfNull(evidence);
            Message = message;
            Evidence = evidence;
        }

        public string Message { get; }
        public IViewFacetDiagnosticEvidence Evidence { get; }
    }
}

/// <summary>One explicit producer-issued availability fact.</summary>
public sealed record ViewFacetAvailabilityFact
{
    public ViewFacetAvailabilityFact(
        ViewFacetId id,
        ViewFacetAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(availability);
        Id = id;
        Availability = availability;
    }

    public ViewFacetId Id { get; }
    public ViewFacetAvailability Availability { get; }
}

/// <summary>
/// Explicit target facts consumed by active registry availability evaluators.
/// </summary>
public interface IViewFacetAvailabilityFacts
{
    ViewFacetAvailability Get(ViewFacetId id);
}

/// <summary>An immutable exact-ID availability snapshot.</summary>
public sealed class ViewFacetAvailabilitySnapshot :
    IViewFacetAvailabilityFacts
{
    readonly Dictionary<ViewFacetId, ViewFacetAvailability> _facts;

    public ViewFacetAvailabilitySnapshot(
        IEnumerable<ViewFacetAvailabilityFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        _facts = [];
        foreach (ViewFacetAvailabilityFact fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (!_facts.TryAdd(fact.Id, fact.Availability))
            {
                throw new ArgumentException(
                    "Availability facts must contain each view-facet id at most once.",
                    nameof(facts));
            }
        }
    }

    public ViewFacetAvailability Get(ViewFacetId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _facts.TryGetValue(id, out ViewFacetAvailability? fact)
            ? fact
            : throw new KeyNotFoundException(
                $"No availability fact was supplied for view facet '{id.Value}'.");
    }
}

/// <summary>Typed Root facts used only for Root-facet applicability.</summary>
public enum ViewFacetRootKind
{
    PackageCapable,
    NonPackage,
}

/// <summary>One exact structural subject plus applicability facts.</summary>
public sealed record ViewFacetTarget
{
    ViewFacetTarget(
        StructuralSubjectIdentity subject,
        ViewFacetRootKind? rootKind)
    {
        Subject = subject;
        RootKind = rootKind;
    }

    public StructuralSubjectIdentity Subject { get; }
    public ViewFacetRootKind? RootKind { get; }

    public static ViewFacetTarget ForRoot(
        StructuralSubjectIdentity.RootSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ViewFacetRootKind rootKind = subject.Coordinate switch
        {
            RealizedMemberCoordinate.Package =>
                ViewFacetRootKind.PackageCapable,
            RealizedMemberCoordinate.Platform
                or RealizedMemberCoordinate.Embedded =>
                ViewFacetRootKind.NonPackage,
            _ => throw new InvalidOperationException(
                "Unknown realized coordinate kind."),
        };
        return new(subject, rootKind);
    }

    public static ViewFacetTarget ForSubject(
        StructuralSubjectIdentity subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind == StructuralSubjectKind.Root)
        {
            throw new ArgumentException(
                "Root targets require an explicit typed Root kind.",
                nameof(subject));
        }

        return new(subject, rootKind: null);
    }
}

/// <summary>One applicable descriptor and its exact availability.</summary>
public sealed record ViewFacetOption
{
    public ViewFacetOption(
        ViewFacetDescriptor descriptor,
        ViewFacetAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(availability);
        Descriptor = descriptor;
        Availability = availability;
    }

    public ViewFacetDescriptor Descriptor { get; }
    public ViewFacetAvailability Availability { get; }
}

/// <summary>The exact result of resolving one requested facet.</summary>
public abstract record ViewFacetResolution
{
    private protected ViewFacetResolution()
    {
    }

    public sealed record Available : ViewFacetResolution
    {
        internal Available(ViewFacetDescriptor descriptor) =>
            Descriptor = descriptor;

        public ViewFacetDescriptor Descriptor { get; }
    }

    public sealed record Unavailable : ViewFacetResolution
    {
        internal Unavailable(
            ViewFacetDescriptor descriptor,
            ViewFacetUnavailableReason reason)
        {
            Descriptor = descriptor;
            Reason = reason;
        }

        public ViewFacetDescriptor Descriptor { get; }
        public ViewFacetUnavailableReason Reason { get; }
    }

    public sealed record Failed : ViewFacetResolution
    {
        internal Failed(
            ViewFacetDescriptor descriptor,
            string message,
            IViewFacetDiagnosticEvidence evidence)
        {
            Descriptor = descriptor;
            Message = message;
            Evidence = evidence;
        }

        public ViewFacetDescriptor Descriptor { get; }
        public string Message { get; }
        public IViewFacetDiagnosticEvidence Evidence { get; }
    }

    public sealed record Inapplicable : ViewFacetResolution
    {
        internal Inapplicable(ViewFacetDescriptor descriptor) =>
            Descriptor = descriptor;

        public ViewFacetDescriptor Descriptor { get; }
    }

    public sealed record Unknown : ViewFacetResolution
    {
        internal Unknown()
        {
        }
    }
}

internal sealed class ViewFacetExecutionBinding
{
    internal ViewFacetExecutionBinding(ViewFacetId id, object target)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(target);
        Id = id;
        Target = target;
    }

    internal ViewFacetId Id { get; }
    internal object Target { get; }
}

internal abstract record ViewFacetRegistration
{
    private protected ViewFacetRegistration(
        ViewFacetDescriptor descriptor,
        string purpose,
        Func<ViewFacetTarget, bool> applies)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(applies);
        Descriptor = descriptor;
        Purpose = purpose;
        Applies = applies;
    }

    internal ViewFacetDescriptor Descriptor { get; }
    internal string Purpose { get; }
    internal Func<ViewFacetTarget, bool> Applies { get; }
    internal abstract ViewFacetExecutionBinding? Binding { get; }

    internal abstract ViewFacetAvailability Evaluate(
        ViewFacetTarget target,
        IViewFacetAvailabilityFacts facts);

    internal sealed record Active : ViewFacetRegistration
    {
        readonly Func<
            ViewFacetTarget,
            IViewFacetAvailabilityFacts,
            ViewFacetAvailability> _evaluate;

        internal Active(
            ViewFacetDescriptor descriptor,
            string purpose,
            Func<ViewFacetTarget, bool> applies,
            ViewFacetExecutionBinding binding,
            Func<
                ViewFacetTarget,
                IViewFacetAvailabilityFacts,
                ViewFacetAvailability> evaluate)
            : base(descriptor, purpose, applies)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(evaluate);
            if (binding.Id != descriptor.Id)
            {
                throw new ArgumentException(
                    "The execution binding id must equal its descriptor id.",
                    nameof(binding));
            }
            Binding = binding;
            _evaluate = evaluate;
        }

        internal override ViewFacetExecutionBinding Binding { get; }

        internal override ViewFacetAvailability Evaluate(
            ViewFacetTarget target,
            IViewFacetAvailabilityFacts facts)
        {
            ViewFacetAvailability availability =
                _evaluate(target, facts)
                ?? throw new InvalidOperationException(
                    "A view-facet availability evaluator returned null.");
            if (availability is ViewFacetAvailability.Unavailable
                {
                    Reason.Kind: ViewFacetUnavailabilityKind.Retired,
                })
            {
                throw new InvalidOperationException(
                    "An active view facet cannot return the tombstone-only Retired reason.");
            }
            return availability;
        }
    }

    internal sealed record Tombstone : ViewFacetRegistration
    {
        static readonly ViewFacetUnavailableReason Retired =
            ViewFacetUnavailableReason.Retired();

        internal Tombstone(
            ViewFacetDescriptor descriptor,
            string purpose,
            Func<ViewFacetTarget, bool> applies)
            : base(descriptor, purpose, applies)
        {
        }

        internal override ViewFacetExecutionBinding? Binding => null;

        internal override ViewFacetAvailability Evaluate(
            ViewFacetTarget target,
            IViewFacetAvailabilityFacts facts) =>
            new ViewFacetAvailability.Unavailable(Retired);
    }
}

/// <summary>
/// Closed product registry for static discovery and exact facet resolution.
/// </summary>
public sealed class ViewFacetRegistry
{
    readonly ImmutableArray<ViewFacetRegistration> _registrations;
    readonly Dictionary<string, ViewFacetRegistration> _registrationById;

    internal ViewFacetRegistry(
        IEnumerable<ViewFacetRegistration> registrations,
        IEnumerable<ViewFacetExecutionBinding> activeBindings)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(activeBindings);
        ViewFacetRegistration[] input = [.. registrations];
        if (input.Any(static registration => registration is null))
        {
            throw new ArgumentException(
                "Registrations cannot contain null.",
                nameof(registrations));
        }

        _registrations =
        [
            .. input.OrderBy(static registration =>
                    registration.Descriptor.Kind)
                .ThenBy(static registration =>
                    registration.Descriptor.Order),
        ];
        _registrationById = new(StringComparer.Ordinal);
        var orders = new HashSet<(StructuralSubjectKind Kind, int Order)>();
        var roles = new HashSet<(StructuralSubjectKind Kind, ViewFacetRole Role)>();
        foreach (ViewFacetRegistration registration in _registrations)
        {
            ViewFacetDescriptor descriptor = registration.Descriptor;
            if (!_registrationById.TryAdd(descriptor.Id.Value, registration))
            {
                throw new ArgumentException(
                    "View-facet ids must be unique.",
                    nameof(registrations));
            }
            if (!orders.Add((descriptor.Kind, descriptor.Order)))
            {
                throw new ArgumentException(
                    "View-facet order must be unique within one structural kind.",
                    nameof(registrations));
            }
            if (descriptor.Role is ViewFacetRole role
                && !roles.Add((descriptor.Kind, role)))
            {
                throw new ArgumentException(
                    "A semantic role may occur at most once within one structural kind.",
                    nameof(registrations));
            }
        }

        ActiveBindings = [.. activeBindings];
        if (ActiveBindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Execution bindings cannot contain null.",
                nameof(activeBindings));
        }
        if (ActiveBindings.Select(static binding => binding.Id)
            .Distinct()
            .Count() != ActiveBindings.Length)
        {
            throw new ArgumentException(
                "Execution binding ids must be unique.",
                nameof(activeBindings));
        }

        ViewFacetId[] activeRegistrationIds =
        [
            .. _registrations
                .OfType<ViewFacetRegistration.Active>()
                .Select(static registration => registration.Descriptor.Id),
        ];
        if (!activeRegistrationIds.OrderBy(static id => id.Value)
            .SequenceEqual(
                ActiveBindings.Select(static binding => binding.Id)
                    .OrderBy(static id => id.Value)))
        {
            throw new ArgumentException(
                "Active registrations and execution bindings must have identical ids.",
                nameof(activeBindings));
        }

        Descriptors =
        [
            .. _registrations.Select(static registration =>
                registration.Descriptor),
        ];
    }

    public ImmutableArray<ViewFacetDescriptor> Descriptors { get; }

    internal ImmutableArray<ViewFacetRegistration> Registrations =>
        _registrations;

    internal ImmutableArray<ViewFacetExecutionBinding> ActiveBindings
    {
        get;
    }

    public ImmutableArray<ViewFacetDescriptor> Discover(
        StructuralSubjectKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return
        [
            .. Descriptors.Where(descriptor => descriptor.Kind == kind),
        ];
    }

    public bool TryGetDescriptor(
        string requestedId,
        [NotNullWhen(true)] out ViewFacetDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(requestedId);
        if (ViewFacetId.TryGetKind(requestedId, out _)
            && _registrationById.TryGetValue(
                requestedId,
                out ViewFacetRegistration? registration))
        {
            descriptor = registration.Descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }

    public ImmutableArray<ViewFacetOption> Discover(
        ViewFacetTarget target,
        IViewFacetAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(facts);
        var options = ImmutableArray.CreateBuilder<ViewFacetOption>();
        foreach (ViewFacetRegistration registration in _registrations)
        {
            if (!registration.Applies(target))
                continue;
            options.Add(
                new ViewFacetOption(
                    registration.Descriptor,
                    registration.Evaluate(target, facts)));
        }
        return options.ToImmutable();
    }

    public ViewFacetResolution Resolve(
        string requestedId,
        ViewFacetTarget target,
        IViewFacetAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(requestedId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(facts);
        if (!ViewFacetId.TryGetKind(requestedId, out _)
            || !_registrationById.TryGetValue(
                requestedId,
                out ViewFacetRegistration? registration))
        {
            return new ViewFacetResolution.Unknown();
        }

        if (!registration.Applies(target))
            return new ViewFacetResolution.Inapplicable(
                registration.Descriptor);

        return registration.Evaluate(target, facts) switch
        {
            ViewFacetAvailability.Available =>
                new ViewFacetResolution.Available(
                    registration.Descriptor),
            ViewFacetAvailability.Unavailable unavailable =>
                new ViewFacetResolution.Unavailable(
                    registration.Descriptor,
                    unavailable.Reason),
            ViewFacetAvailability.Failed failed =>
                new ViewFacetResolution.Failed(
                    registration.Descriptor,
                    failed.Message,
                    failed.Evidence),
            _ => throw new InvalidOperationException(
                "Unknown view-facet availability result."),
        };
    }
}
