using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class BodyShapeCommandTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;

    [Fact]
    public void Search_DefaultsToPublicSurface_AndAllIncludesPrivateMembers()
    {
        using var source = MetadataSource.Open(FixturePath);

        var publicResult = BodyShapeSearch.Search(
            source,
            "ObjectCreationExpression",
            cancellationToken: TestContext.Current.CancellationToken);
        var publicFixtureMatches = publicResult.Matches
            .Where(match => match.TypeName == typeof(BodyShapeFixture).FullName)
            .ToList();

        var publicMatch = Assert.Single(publicFixtureMatches);
        Assert.Contains(nameof(BodyShapeFixture.PublicCreation), publicMatch.Member);
        Assert.Equal("new object()", publicMatch.Text);

        var allResult = BodyShapeSearch.Search(
            source,
            "ObjectCreationExpression",
            includeAll: true,
            cancellationToken: TestContext.Current.CancellationToken);
        var allFixtureMatches = allResult.Matches
            .Where(match => match.TypeName == typeof(BodyShapeFixture).FullName)
            .ToList();

        Assert.Contains(allFixtureMatches, match =>
            match.Member.Contains("PublicCreation", StringComparison.Ordinal));
        Assert.Contains(allFixtureMatches, match =>
            match.Member.Contains("PrivateCreation", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_ReturnsExactMultiLineTextAndExtent()
    {
        using var source = MetadataSource.Open(FixturePath);

        var result = BodyShapeSearch.Search(
            source,
            "IfStatement",
            cancellationToken: TestContext.Current.CancellationToken);
        var match = Assert.Single(result.Matches, candidate =>
            candidate.TypeName == typeof(BodyShapeFixture).FullName
            && candidate.Member.Contains(nameof(BodyShapeFixture.Branch), StringComparison.Ordinal));

        Assert.Contains('\n', match.Text);
        Assert.StartsWith("if (value)", match.Text, StringComparison.Ordinal);
        Assert.EndsWith("}", match.Text, StringComparison.Ordinal);
        Assert.True(match.Extent.EndLine > match.Extent.StartLine);

        var surface = source.ExtractApiSurface(includeAll: false);
        var type = Assert.Single(surface.Types, candidate =>
            candidate.FullName == typeof(BodyShapeFixture).FullName);
        var member = Assert.Single(type.Members, candidate =>
            candidate.Name == nameof(BodyShapeFixture.Branch));
        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(type, member).Format(MemberAnchorFormat.Qualified),
            match.Member);
    }

    [Fact]
    public void Search_RejectsUnknownAndNonNodeKinds_AndHonorsLimit()
    {
        using var source = MetadataSource.Open(FixturePath);

        Assert.DoesNotContain("CatchClause", BodyShapeSearch.SupportedKinds);
        Assert.DoesNotContain("SwitchSection", BodyShapeSearch.SupportedKinds);
        Assert.DoesNotContain("Block", BodyShapeSearch.SupportedKinds);
        Assert.DoesNotContain("UnsupportedExpression", BodyShapeSearch.SupportedKinds);
        Assert.Contains("TryStatement", BodyShapeSearch.SupportedKinds);
        Assert.Throws<ArgumentException>(
            () => BodyShapeSearch.Search(
                source,
                "objectcreationexpression",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(
            () => BodyShapeSearch.Search(
                source,
                "CatchClause",
                cancellationToken: TestContext.Current.CancellationToken));
        var limited = BodyShapeSearch.Search(
            source,
            "ObjectCreationExpression",
            includeAll: true,
            limit: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(limited.Matches);
    }

    [Fact]
    public void Search_ReportsUnreconstructedStateMachineBodies()
    {
        using var source = MetadataSource.Open(
            FixtureCatalog.DecompilerClassicStateMachines.AssemblyPath());
        var surface = source.ExtractApiSurface(includeAll: true);
        var type = Assert.Single(surface.Types, candidate =>
            candidate.Name == "ClassicStateMachineFixtures");
        var asyncMember = Assert.Single(type.Members, candidate =>
            candidate.Name == "Async_AwaitInCatchAndFinally");
        var iteratorMember = Assert.Single(type.Members, candidate =>
            candidate.Name == "Iterator_YieldInTryFinally");

        var result = BodyShapeSearch.Search(
            source,
            "InvocationExpression",
            includeAll: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result.Matches, match =>
            match.MethodToken == asyncMember.MetadataToken
            || match.MethodToken == iteratorMember.MetadataToken);
        Assert.Contains(result.Failures, failure =>
            failure.Subject.Contains($"0x{asyncMember.MetadataToken:X8}", StringComparison.Ordinal)
            && failure.Reason.Contains("requires Full fidelity", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Subject.Contains($"0x{iteratorMember.MetadataToken:X8}", StringComparison.Ordinal)
            && failure.Reason.Contains("requires Full fidelity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Command_TsvReportsExactCoordinatesAndPublicMatch()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "ObjectCreationExpression",
                LibraryPath = FixturePath,
                Tabular = true,
                Tsv = true
            })));

        Assert.Equal(0, exit);
        Assert.Contains("Warning: Body-shape search skipped", error);
        Assert.Contains("start_line\tstart_column\tend_line\tend_column", output);
        Assert.Contains(nameof(BodyShapeFixture.PublicCreation), output);
        Assert.DoesNotContain("PrivateCreation", output);
        Assert.Contains("new object()", output);
    }

    [Fact]
    public async Task Command_MarkdownHonorsColumnProjection()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "ObjectCreationExpression",
                LibraryPath = FixturePath,
                Columns = ["Token"]
            })));

        Assert.Equal(0, exit);
        Assert.Contains("Warning: Body-shape search skipped", error);
        Assert.Contains("| Token |", output);
        Assert.DoesNotContain("Member", output);
        Assert.DoesNotContain("Start Line", output);
        Assert.DoesNotContain("new object()", output);
    }

    [Fact]
    public async Task Command_JsonPreservesMultiLineMatch()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "IfStatement",
                LibraryPath = FixturePath,
                JsonOutput = true
            })));

        Assert.Equal(0, exit);
        Assert.Contains("Warning: Body-shape search skipped", error);
        using var document = JsonDocument.Parse(output);
        var match = document.RootElement.EnumerateArray().Single(element =>
            element.GetProperty("type_name").GetString() == typeof(BodyShapeFixture).FullName
            && element.GetProperty("method_name").GetString() == nameof(BodyShapeFixture.Branch));
        Assert.Equal(
            $"0x{typeof(BodyShapeFixture).GetMethod(nameof(BodyShapeFixture.Branch))!.MetadataToken:X8}",
            match.GetProperty("method_token").GetString());
        Assert.Contains('\n', match.GetProperty("text").GetString()!);
        Assert.True(
            match.GetProperty("extent").GetProperty("end_line").GetInt32()
            > match.GetProperty("extent").GetProperty("start_line").GetInt32());
    }

    [Fact]
    public async Task Command_CountReportsNoMatches_AndUnreadableLibraryFails()
    {
        var noMatches = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "FixedStatement",
                LibraryPath = FixturePath,
                Count = true
            })));

        Assert.Equal(0, noMatches.ExitCode);
        Assert.Equal("0", noMatches.Output.Trim());

        string missing = Path.Combine(Path.GetTempPath(), $"missing-body-shape-{Guid.NewGuid():N}.dll");
        var unreadable = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "ObjectCreationExpression",
                LibraryPath = missing
            })));

        Assert.Equal(1, unreadable.ExitCode);
        Assert.Contains("Could not find file", unreadable.Error, StringComparison.OrdinalIgnoreCase);
    }
}
