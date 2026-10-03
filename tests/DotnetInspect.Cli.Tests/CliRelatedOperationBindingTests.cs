using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

public class CliRelatedOperationBindingTests
{
    [Fact]
    public void Resolve_JoinsByExactIdentityNotEqualLabels()
    {
        RelatedOperationAffordance first =
            Affordance("member.first", "Inspect");
        RelatedOperationAffordance second =
            Affordance("member.second", "Inspect");
        var registry =
            new CliRelatedOperationBindingRegistry<string>(
                [first, second],
                [
                    Binding(
                        "binding.first",
                        first.Id,
                        100,
                        static _ => true,
                        "first"),
                    Binding(
                        "binding.second",
                        second.Id,
                        100,
                        static _ => true,
                        "second"),
                ]);

        Tip[] tips = registry.Resolve([second], "");

        Tip tip = Assert.Single(tips);
        Assert.Equal("second", tip.CommandText);
    }

    [Fact]
    public void Resolve_OmitsAffordancesWithoutCliBindings()
    {
        RelatedOperationAffordance bound =
            Affordance("member.bound", "Bound");
        RelatedOperationAffordance unbound =
            Affordance("member.unbound", "Unbound");
        var registry =
            new CliRelatedOperationBindingRegistry<string>(
                [bound, unbound],
                [
                    Binding(
                        "binding.bound",
                        bound.Id,
                        100,
                        static _ => true,
                        "bound"),
                ]);

        Tip[] tips = registry.Resolve([unbound], "");

        Assert.Empty(tips);
    }

    [Fact]
    public void Constructor_RejectsBindingWithoutRegisteredAffordance()
    {
        RelatedOperationAffordance registered =
            Affordance("member.registered", "Registered");
        RelatedOperationAffordance missing =
            Affordance("member.missing", "Missing");

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new CliRelatedOperationBindingRegistry<string>(
                    [registered],
                    [
                        Binding(
                            "binding.missing",
                            missing.Id,
                            100,
                            static _ => true,
                            "missing"),
                    ]));

        Assert.Contains("unregistered affordance", exception.Message);
    }

    [Fact]
    public void Resolve_OrdersByPreferenceThenStableBindingIdentity()
    {
        RelatedOperationAffordance affordance =
            Affordance("member.inspect", "Inspect");
        var registry =
            new CliRelatedOperationBindingRegistry<string>(
                [affordance],
                [
                    Binding(
                        "binding.z",
                        affordance.Id,
                        200,
                        static _ => true,
                        "last"),
                    Binding(
                        "binding.b",
                        affordance.Id,
                        100,
                        static _ => true,
                        "second"),
                    Binding(
                        "binding.a",
                        affordance.Id,
                        100,
                        static _ => true,
                        "first"),
                ]);

        Tip[] tips = registry.Resolve([affordance], "");

        Assert.Equal(
            ["first", "second", "last"],
            tips.Select(static tip => tip.CommandText));
    }

    [Fact]
    public void Resolve_DoesNotCreateInapplicableGesture()
    {
        RelatedOperationAffordance affordance =
            Affordance("member.inspect", "Inspect");
        var registry =
            new CliRelatedOperationBindingRegistry<string>(
                [affordance],
                [
                    new(
                        new CliRelatedOperationBindingId("binding.inspect"),
                        affordance.Id,
                        100,
                        static _ => false,
                        static _ => throw new InvalidOperationException(
                            "An inapplicable binding must not create a gesture.")),
                ]);

        Tip[] tips = registry.Resolve([affordance], "");

        Assert.Empty(tips);
    }

    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", "System.Text.Json.JsonSerializer")]
    [InlineData("Namespace.Generic<T>", "'Namespace.Generic<T>'")]
    [InlineData("path with spaces/library.dll", "'path with spaces/library.dll'")]
    public void CommandRenderer_QuotesOnlyUnsafeDynamicValues(
        string value,
        string expected)
    {
        string command = CliCommandText.Render(
            [
                CliCommandToken.Syntax("member"),
                CliCommandToken.ValueToken(value),
            ]);

        Assert.Equal($"member {expected}", command);
    }

    private static RelatedOperationAffordance Affordance(
        string id,
        string title) =>
        new(
            new RelatedOperationAffordanceId(id),
            title,
            "Summary.");

    private static CliRelatedOperationBinding<string> Binding(
        string id,
        RelatedOperationAffordanceId affordanceId,
        int order,
        Func<string, bool> isApplicable,
        string command) =>
        new(
            new CliRelatedOperationBindingId(id),
            affordanceId,
            order,
            isApplicable,
            _ => new CliRelatedOperationGesture(
                ImmutableArray.Create(
                    CliCommandToken.Syntax(command)),
                "Purpose."));
}
