using DotnetInspector.Commands;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class TimelineCommandTests
{
    [Fact]
    public void ZeroEvaluationVector_RemainsUnevaluatedAndRecommendsProbe()
    {
        var vector = Vector("1.0.0", "1.0.1", "1.0.2");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [],
            Sections());

        Assert.Equal(3, view.Evaluations!.Count);
        Assert.All(view.Evaluations, row => Assert.Equal("Unevaluated", row.State));
        Assert.Empty(view.Transitions!);
        Assert.Contains("--at #2", view.Recommendation, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseMemberTimeline_QualifiesGapWithoutClaimingExactVersion()
    {
        var vector = Vector("1.0.0", "1.0.1", "1.0.2");
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", members: [Method("Run", "void Run()")]));

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [
                Evaluation(vector, 0, oldSurface),
                Evaluation(vector, 2, newSurface),
            ],
            Sections());

        var row = Assert.Single(view.Transitions!);
        Assert.Equal("Gap (1)", row.Span);
        Assert.Equal("Added", row.Transition);
        Assert.Contains("exact transition version is unknown", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DenseTypePresenceTimeline_ReportsNativeAddition()
    {
        var vector = Vector("1.0.0", "1.0.1");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.type",
            [
                Evaluation(vector, 0, Surface()),
                Evaluation(vector, 1, Surface(Type("Widget"))),
            ],
            Sections());

        var row = Assert.Single(view.Transitions!);
        Assert.Equal("Adjacent", row.Span);
        Assert.Equal("Added", row.Transition);
        Assert.Equal("Sample.Widget", row.Target);
    }

    [Fact]
    public void MemberTimeline_PreservesMetadataFacetChanges()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var oldSurface = Surface(Type("Widget", members: [Method("Run", "void Run()")]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()", accessibility: "protected"),
        ]));

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [
                Evaluation(vector, 0, oldSurface),
                Evaluation(vector, 1, newSurface),
            ],
            Sections());

        var row = Assert.Single(view.Transitions!);
        Assert.Equal("Changed", row.Transition);
        Assert.Contains("accessibility: public -> protected", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeTimeline_ReportsExactAppliedOccurrenceTransitions()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var oldSurface = Surface(Type("Widget", attributes: ["System.Obsolete(\"old\")"]));
        var newSurface = Surface(Type("Widget", attributes: ["System.Obsolete(\"new\")"]));

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.attribute",
            [
                Evaluation(vector, 0, oldSurface),
                Evaluation(vector, 1, newSurface),
            ],
            Sections());

        var changed = Assert.Single(view.Transitions!);
        Assert.Equal("Changed", changed.Transition);
        Assert.Equal("System.Obsolete(\"new\")", changed.Target);
        Assert.Contains("System.Obsolete(\"old\") -> System.Obsolete(\"new\")", changed.Detail);
    }

    [Fact]
    public void TypePresenceEvaluation_DistinguishesMissingFromSubjectAbsent()
    {
        var vector = Vector("1.0.0");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.type",
            [Evaluation(vector, 0, Surface(Type("Other")))],
            Sections());

        var evaluation = Assert.Single(view.Evaluations!);
        Assert.Equal("Missing", evaluation.State);
        Assert.Equal(0, evaluation.Findings);
    }

    [Fact]
    public void Evaluations_PreserveSubjectAbsentAndFailure()
    {
        var vector = Vector("1.0.0", "1.0.1");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [
                Evaluation(vector, 0, Surface(Type("Other"))),
                new TimelineCommand.TimelineEvaluation(
                    vector.Addresses[1],
                    null,
                    "package unavailable"),
            ],
            Sections());

        Assert.Equal("SubjectAbsent", view.Evaluations![0].State);
        Assert.Equal("Failed", view.Evaluations[1].State);
        Assert.Equal("package unavailable", view.Evaluations[1].Detail);
        Assert.Equal("Failed", Assert.Single(view.Transitions!).Transition);
    }

    static TimelineCommand.TimelineEvaluation Evaluation(
        PackageVersionVector vector,
        int position,
        ApiSurface surface)
        => new(vector.Addresses[position], surface, null);

    static PackageVersionVector Vector(params string[] versions)
    {
        Assert.True(
            PackageVersionRange.TryParse(
                $"Sample@{versions[0]}..{versions[^1]}",
                out var range,
                out var error),
            error);
        return PackageVersionVector.Create(range!, versions);
    }

    static HashSet<string> Sections()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            TimelineCommand.EvaluationsSection,
            TimelineCommand.TransitionsSection,
        };

    static ApiSurface Surface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiType Type(
        string name,
        List<ApiMember>? members = null,
        List<string>? attributes = null)
        => new()
        {
            Namespace = "Sample",
            Name = name,
            Kind = "class",
            Members = members ?? [],
            Attributes = attributes ?? [],
        };

    static ApiMember Method(
        string name,
        string signature,
        string? accessibility = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            Accessibility = accessibility,
        };
}
