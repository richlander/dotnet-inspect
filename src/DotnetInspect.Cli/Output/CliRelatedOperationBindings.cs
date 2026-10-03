using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal sealed record CliRelatedOperationBindingId
{
    internal CliRelatedOperationBindingId(string value)
    {
        try
        {
            _ = new RelatedOperationAffordanceId(value);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "A CLI related-operation binding id must be a canonical dotted identifier.",
                nameof(value),
                exception);
        }

        Value = value;
    }

    internal string Value { get; }
}

internal enum CliCommandTokenKind
{
    Syntax,
    Value,
}

internal readonly record struct CliCommandToken
{
    private CliCommandToken(string value, CliCommandTokenKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (kind == CliCommandTokenKind.Syntax
            && value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "CLI syntax tokens cannot contain whitespace.",
                nameof(value));
        }

        Value = value;
        Kind = kind;
    }

    internal string Value { get; }
    internal CliCommandTokenKind Kind { get; }

    internal static CliCommandToken Syntax(string value) =>
        new(value, CliCommandTokenKind.Syntax);

    internal static CliCommandToken ValueToken(string value) =>
        new(value, CliCommandTokenKind.Value);
}

internal sealed record CliRelatedOperationGesture
{
    internal CliRelatedOperationGesture(
        ImmutableArray<CliCommandToken> command,
        string purpose)
    {
        if (command.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A related-operation gesture requires a command.",
                nameof(command));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        Command = command;
        Purpose = purpose;
    }

    internal ImmutableArray<CliCommandToken> Command { get; }
    internal string Purpose { get; }
}

internal sealed class CliRelatedOperationBinding<TContext>
{
    internal CliRelatedOperationBinding(
        CliRelatedOperationBindingId id,
        RelatedOperationAffordanceId affordanceId,
        int order,
        Func<TContext, bool> isApplicable,
        Func<TContext, CliRelatedOperationGesture> createGesture)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(affordanceId);
        ArgumentNullException.ThrowIfNull(isApplicable);
        ArgumentNullException.ThrowIfNull(createGesture);

        Id = id;
        AffordanceId = affordanceId;
        Order = order;
        IsApplicable = isApplicable;
        CreateGesture = createGesture;
    }

    internal CliRelatedOperationBindingId Id { get; }
    internal RelatedOperationAffordanceId AffordanceId { get; }
    internal int Order { get; }
    internal Func<TContext, bool> IsApplicable { get; }
    internal Func<TContext, CliRelatedOperationGesture> CreateGesture { get; }
}

internal sealed class CliRelatedOperationBindingRegistry<TContext>
{
    private readonly ImmutableArray<
        CliRelatedOperationBinding<TContext>> _bindings;
    private readonly HashSet<RelatedOperationAffordanceId> _affordanceIds;

    internal CliRelatedOperationBindingRegistry(
        IEnumerable<RelatedOperationAffordance> affordances,
        IEnumerable<CliRelatedOperationBinding<TContext>> bindings)
    {
        ArgumentNullException.ThrowIfNull(affordances);
        ArgumentNullException.ThrowIfNull(bindings);

        RelatedOperationAffordance[] affordanceArray = [.. affordances];
        _affordanceIds = new();
        foreach (RelatedOperationAffordance affordance in affordanceArray)
        {
            if (!_affordanceIds.Add(affordance.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate related-operation affordance '{affordance.Id}'.");
            }
        }

        _bindings = [.. bindings];
        var bindingIds =
            new HashSet<CliRelatedOperationBindingId>();
        foreach (CliRelatedOperationBinding<TContext> binding in _bindings)
        {
            if (!bindingIds.Add(binding.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate CLI related-operation binding '{binding.Id.Value}'.");
            }
            if (!_affordanceIds.Contains(binding.AffordanceId))
            {
                throw new InvalidOperationException(
                    $"CLI binding '{binding.Id.Value}' targets unregistered "
                    + $"affordance '{binding.AffordanceId}'.");
            }
        }
    }

    internal Tip[] Resolve(
        IEnumerable<RelatedOperationAffordance> affordances,
        TContext context)
    {
        ArgumentNullException.ThrowIfNull(affordances);

        var activeIds = new HashSet<RelatedOperationAffordanceId>();
        foreach (RelatedOperationAffordance affordance in affordances)
        {
            if (!_affordanceIds.Contains(affordance.Id))
            {
                throw new InvalidOperationException(
                    $"Unregistered related-operation affordance '{affordance.Id}'.");
            }

            activeIds.Add(affordance.Id);
        }

        var gestures =
            new List<(
                int Order,
                CliRelatedOperationBindingId Id,
                CliRelatedOperationGesture Gesture)>();
        foreach (CliRelatedOperationBinding<TContext> binding in _bindings)
        {
            if (activeIds.Contains(binding.AffordanceId)
                && binding.IsApplicable(context))
            {
                gestures.Add((
                    binding.Order,
                    binding.Id,
                    binding.CreateGesture(context)));
            }
        }

        return
        [
            .. gestures
                .OrderBy(static gesture => gesture.Order)
                .ThenBy(
                    static gesture => gesture.Id.Value,
                    StringComparer.Ordinal)
                .Select(static gesture => new Tip(
                    CliCommandText.Render(gesture.Gesture.Command),
                    "",
                    gesture.Gesture.Purpose)),
        ];
    }
}

internal static class CliCommandText
{
    internal static string Render(
        ImmutableArray<CliCommandToken> command) =>
        string.Join(
            " ",
            command.Select(static token =>
                token.Kind == CliCommandTokenKind.Value
                    ? RenderValue(token.Value)
                    : token.Value));

    private static string RenderValue(string value) =>
        value.All(IsSafeUnquoted)
            ? value
            : ShellCommandText.Quote(value);

    private static bool IsSafeUnquoted(char value) =>
        value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or '-'
            or '/'
            or ':'
            or '@'
            or '+'
            or '=';
}
