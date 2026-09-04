using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class ProductDemoSourceBindingTests
{
    [Fact]
    public void CreateAcceptsOneTargetFreeStaticMethodGroup()
    {
        ProductDemoSourceBinding binding =
            ProductDemoSourceBinding.Create("scenario", CreateRecords);

        Assert.Equal("scenario", binding.ScenarioId);
        Assert.Equal("scenario", binding.Resolve().ScenarioId);
    }

    [Fact]
    public void CreateRejectsTargetedAndMulticastDelegates()
    {
        var source = new TargetedSource();
        Assert.Throws<ArgumentException>(() =>
            ProductDemoSourceBinding.Create("scenario", source.CreateRecords));

        Func<InspectionDefinitionRecord[]> first = CreateRecords;
        Func<InspectionDefinitionRecord[]> second = CreateRecords;
        Assert.Throws<ArgumentException>(() =>
            ProductDemoSourceBinding.Create("scenario", first + second));

        Assert.Throws<ArgumentException>(() =>
            ProductDemoSourceBinding.Create(
                "scenario",
                static () => CreateRecords()));

        string capturedScenarioId = "scenario";
        Assert.Throws<ArgumentException>(() =>
            ProductDemoSourceBinding.Create(
                "scenario",
                () => CreateRecords(
                    capturedScenarioId,
                    ProductDemoSections.Methods)));
    }

    [Fact]
    public void ResolveInvokesSelectedSourceExactlyOnce()
    {
        s_invocations = 0;
        ProductDemoSourceBinding binding =
            ProductDemoSourceBinding.Create("scenario", CreateCountedRecords);

        Assert.Equal("scenario", binding.Resolve().ScenarioId);
        Assert.Equal(1, s_invocations);
    }

    [Fact]
    public void ResolveRequiresExactlyOneMatchingScenario()
    {
        Assert.Contains(
            "exactly one scenario",
            Assert.Throws<InspectionDefinitionException>(() =>
                ProductDemoSourceBinding.Create(
                    "scenario",
                    CreateNoScenarioRecords).Resolve()).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "exactly one scenario",
            Assert.Throws<InspectionDefinitionException>(() =>
                ProductDemoSourceBinding.Create(
                    "scenario",
                    CreateTwoScenarioRecords).Resolve()).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "returned scenario 'other'",
            Assert.Throws<InspectionDefinitionException>(() =>
                ProductDemoSourceBinding.Create(
                    "scenario",
                    CreateMismatchedScenarioRecords).Resolve()).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePreservesDefinitionAndSectionFailures()
    {
        Assert.Throws<InspectionDefinitionException>(() =>
            ProductDemoSourceBinding.Create(
                "scenario",
                CreateMissingReferenceRecords).Resolve());
        Assert.Contains(
            "unknown section",
            Assert.Throws<InspectionDefinitionException>(() =>
                ProductDemoSourceBinding.Create(
                    "scenario",
                    CreateUnsupportedSectionRecords).Resolve()).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static InspectionDefinitionRecord[] CreateRecords() =>
        CreateRecords("scenario", ProductDemoSections.Methods);

    private static int s_invocations;

    private static InspectionDefinitionRecord[] CreateCountedRecords()
    {
        s_invocations++;
        return CreateRecords();
    }

    private static InspectionDefinitionRecord[] CreateNoScenarioRecords() => [];

    private static InspectionDefinitionRecord[] CreateTwoScenarioRecords() =>
    [
        .. CreateRecords("scenario", ProductDemoSections.Methods),
        .. CreateRecords("other", ProductDemoSections.Methods),
    ];

    private static InspectionDefinitionRecord[] CreateMismatchedScenarioRecords() =>
        CreateRecords("other", ProductDemoSections.Methods);

    private static InspectionDefinitionRecord[] CreateMissingReferenceRecords() =>
    [
        new ScenarioDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            "scenario",
            workspace: "missing"),
    ];

    private static InspectionDefinitionRecord[] CreateUnsupportedSectionRecords() =>
        CreateRecords("scenario", "Unsupported");

    private static InspectionDefinitionRecord[] CreateRecords(
        string scenarioId,
        string section)
    {
        const int version = InspectionDefinitionJson.CurrentSchemaVersion;
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "Example.Package",
            "1.0.0",
            "net10.0");
        return
        [
            new WorkspaceDefinition(
                version,
                "workspace",
                [new WorkspaceContextDefinition("context", members: [package])]),
            new ViewDefinition(
                version,
                "view",
                type: "Example.Type",
                section: section),
            new NavigationDefinition(
                version,
                "navigation",
                [new NavigationTabDefinition("package", coordinate: package)],
                focus: "package"),
            new ScenarioDefinition(
                version,
                scenarioId,
                workspace: "workspace",
                context: "context",
                view: "view",
                navigation: "navigation"),
        ];
    }

    private sealed class TargetedSource
    {
        internal InspectionDefinitionRecord[] CreateRecords() =>
            ProductDemoSourceBindingTests.CreateRecords();
    }
}
