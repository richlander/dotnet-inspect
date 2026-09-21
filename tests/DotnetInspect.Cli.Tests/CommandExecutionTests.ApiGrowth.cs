namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Member_CustomAttributesCanExceedInformativeRange()
    {
        Type fixture = typeof(DotnetInspector.Fixtures.MemberGrowthFixture);
        var (exit, output, error) = await RunAppAsync(
            "member",
            fixture.FullName!,
            nameof(DotnetInspector.Fixtures.MemberGrowthFixture.Annotated),
            "--library",
            fixture.Assembly.Location,
            "--index",
            "1",
            "-S",
            "Custom Attributes",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("31", output.Trim());
    }

    [Theory]
    [InlineData(
        "System.ConsoleKey",
        "System.Console",
        "Values",
        145)]
    [InlineData(
        "System.Func<T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11,T12,T13,T14,T15,T16,TResult>",
        "System.Runtime",
        "Type Parameters",
        17)]
    [InlineData(
        "System.Decimal",
        "System.Runtime",
        "Interfaces",
        31)]
    [InlineData(
        "System.String",
        "System.Runtime",
        "Constructors",
        9)]
    [InlineData(
        "System.Reflection.Emit.OpCodes",
        "System.Reflection.Primitives",
        "Fields",
        226)]
    [InlineData(
        "System.Type",
        "System.Runtime",
        "Properties",
        70)]
    [InlineData(
        "System.Linq.Enumerable",
        "System.Linq",
        "Method Groups",
        75)]
    [InlineData(
        "System.Linq.Enumerable",
        "System.Linq",
        "Methods",
        234)]
    [InlineData(
        "System.Decimal",
        "System.Runtime",
        "Operators",
        37)]
    [InlineData(
        "System.Decimal",
        "System.Runtime",
        "Explicit Interface Implementations",
        99)]
    [InlineData(
        "System.Span<T>",
        "System.Runtime",
        "Extension Methods",
        83)]
    public async Task Member_PlatformBaseSectionCountsMatchGrowthEvidence(
        string type,
        string assembly,
        string section,
        int expected)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            type,
            "--platform",
            assembly,
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expected.ToString(), output.Trim());
    }

    [Fact]
    public async Task
        Member_PlatformNamedMemberOverloadsMatchGrowthEvidence()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Runtime.Intrinsics.Arm.AdvSimd",
            "Store",
            "--platform",
            "System.Runtime.Intrinsics",
            "-S",
            "Methods",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("41", output.Trim());
    }

    [Theory]
    [InlineData("Member Index")]
    [InlineData("Called Types")]
    [InlineData("Unsafe Members")]
    [InlineData("Safety Facts")]
    [InlineData("Allocation Facts")]
    [InlineData("Cost Facts")]
    [InlineData("Performance Triage")]
    [InlineData("Top Leverage")]
    [InlineData("Type Metrics")]
    public async Task
        Type_PlatformMemberDomainSectionsExceedInformativeRange(
            string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Runtime",
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out int count));
        Assert.True(
            count > 24,
            $"Expected {section} to exceed the informative range; observed {count}.");
    }

    [Fact]
    public async Task Member_PlatformMemberMetricsExceedInformativeRange()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Runtime.Intrinsics.Arm.AdvSimd",
            "Store",
            "--platform",
            "System.Runtime.Intrinsics",
            "-S",
            "Member Metrics",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out int count));
        Assert.True(
            count > 24,
            $"Expected Member Metrics to exceed the informative range; observed {count}.");
    }

    [Fact]
    public async Task
        Member_PlatformExactMemberAnnotatedSourceExceedsInformativeRange()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Linq.Enumerable",
            "ToArray:1",
            "--platform",
            "System.Linq",
            "-S",
            "Annotated Source",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        int renderedLines =
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(
            renderedLines > 24,
            "Expected Annotated Source output to exceed 24 rendered lines; "
            + $"observed {renderedLines}.");
    }

    [Fact]
    public async Task Member_PlatformExactMemberIlExceedsInformativeRange()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Linq.Enumerable",
            "ToArray:1",
            "--platform",
            "System.Linq",
            "-S",
            "IL",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        int renderedLines =
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(
            renderedLines > 24,
            $"Expected IL output to exceed 24 rendered lines; observed {renderedLines}.");
    }

    [Theory]
    [InlineData("Classes")]
    [InlineData("Structs")]
    [InlineData("Interfaces")]
    [InlineData("Enums")]
    [InlineData("Delegates")]
    public async Task Type_PlatformSurfaceKindCountsExceedInformativeRange(
        string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out int count));
        Assert.True(
            count > 24,
            $"Expected {section} to exceed the informative range; observed {count}.");
    }

    [Fact]
    public async Task Type_PlatformClassInventoryUsesPrimaryAndDetailedViews()
    {
        var (minimalExit, minimal, minimalError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:m",
            "--markdown",
            "--tips",
            "q");
        var (normalExit, normal, normalError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:n",
            "--markdown",
            "--tips",
            "q");
        var (detailedExit, detailed, detailedError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:d",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, minimalExit);
        Assert.Equal(0, normalExit);
        Assert.Equal(0, detailedExit);
        Assert.Empty(minimalError);
        Assert.Empty(normalError);
        Assert.Empty(detailedError);
        Assert.Contains("Classes", SectionHeadings(minimal));
        Assert.DoesNotContain("Classes", SectionHeadings(normal));
        Assert.Contains("Classes", SectionHeadings(detailed));
    }
}
