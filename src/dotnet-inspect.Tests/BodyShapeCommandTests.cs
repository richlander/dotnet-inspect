using System.Reflection;
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

        var publicMatch = Assert.Single(publicFixtureMatches, match =>
            match.MethodName == nameof(BodyShapeFixture.PublicCreation));
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
    public void Search_UsesExplicitReadableLocalNameOptions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"body-shape-readable-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, Path.GetFileName(FixturePath));
        File.Copy(FixturePath, assemblyPath);
        try
        {
            using var source = MetadataSource.Open(assemblyPath);

            var defaultResult = BodyShapeSearch.Search(
                source,
                "NameExpression",
                cancellationToken: TestContext.Current.CancellationToken);
            var readableResult = BodyShapeSearch.Search(
                source,
                "NameExpression",
                printerOptions: StyleOptionCatalog.DefaultOptions,
                cancellationToken: TestContext.Current.CancellationToken);

            var defaultMatches = defaultResult.Matches
                .Where(match => match.TypeName == typeof(BodyShapeFixture).FullName
                    && match.MethodName == nameof(BodyShapeFixture.ReadableLocal))
                .ToList();
            var readableMatches = readableResult.Matches
                .Where(match => match.TypeName == typeof(BodyShapeFixture).FullName
                    && match.MethodName == nameof(BodyShapeFixture.ReadableLocal))
                .ToList();
            Assert.Contains(defaultMatches, match => match.Text.StartsWith("V_", StringComparison.Ordinal));
            Assert.NotEmpty(readableMatches);
            Assert.DoesNotContain(readableMatches, match => match.Text.StartsWith("V_", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(assemblyPath);
            Directory.Delete(directory);
        }
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
    public void Search_DefaultExcludesInternalExplicitInterfaceBodies()
    {
        using var source = MetadataSource.Open(FixtureCatalog.DiffPair.OldAssemblyPath());
        var surface = source.ExtractApiSurface(includeAll: true);
        var internalType = Assert.Single(surface.Types, candidate =>
            candidate.Name == "InternalExplicitSurface");
        var internalMember = Assert.Single(internalType.Members, candidate =>
            candidate.Kind == "explicit-interface-implementation");

        var publicResult = BodyShapeSearch.Search(
            source,
            "LiteralExpression",
            cancellationToken: TestContext.Current.CancellationToken);
        var allResult = BodyShapeSearch.Search(
            source,
            "LiteralExpression",
            includeAll: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(publicResult.Matches, match =>
            match.MethodToken == internalMember.MetadataToken);
        Assert.Contains(allResult.Matches, match =>
            match.MethodToken == internalMember.MetadataToken);
    }

    [Fact]
    public void Search_PrefersDeclaringExtensionMemberIdentity()
    {
        using var source = MetadataSource.Open(typeof(SampleExtensions).Assembly.Location);
        var surface = source.ExtractApiSurface(includeAll: false);
        var declaringType = Assert.Single(surface.Types, candidate =>
            candidate.Name == nameof(SampleExtensions));
        var declaringMember = Assert.Single(declaringType.Members, candidate =>
            candidate.Name == nameof(SampleExtensions.GetInfo));

        var result = BodyShapeSearch.Search(
            source,
            "MemberAccessExpression",
            cancellationToken: TestContext.Current.CancellationToken);
        var match = Assert.Single(result.Matches, candidate =>
            candidate.MethodToken == declaringMember.MetadataToken);

        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(declaringType, declaringMember)
                .Format(MemberAnchorFormat.Qualified),
            match.Member);
        Assert.Equal(typeof(SampleExtensions).FullName, match.TypeName);
    }

    [Fact]
    public void Search_PrefersExplicitAccessorIdentity_AndFormatsGenericTypeName()
    {
        using var source = MetadataSource.Open(FixturePath);

        var result = BodyShapeSearch.Search(
            source,
            "ObjectCreationExpression",
            includeAll: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var explicitProperty = typeof(BodyShapeFixture).GetProperty(
            $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Value)}",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var explicitMatch = Assert.Single(result.Matches, match =>
            match.MethodToken == explicitProperty.GetMethod!.MetadataToken);
        Assert.Contains(".explicit:", explicitMatch.Member, StringComparison.Ordinal);
        Assert.Contains(".get_Value~", explicitMatch.Member, StringComparison.Ordinal);

        var explicitEvent = typeof(BodyShapeFixture).GetEvent(
            $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Changed)}",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var eventMatch = Assert.Single(result.Matches, match =>
            match.MethodToken == explicitEvent.AddMethod!.MetadataToken);
        Assert.Contains(".explicit:", eventMatch.Member, StringComparison.Ordinal);
        Assert.Contains(".add_Changed~", eventMatch.Member, StringComparison.Ordinal);

        var genericMatch = Assert.Single(result.Matches, match =>
            match.MethodToken == typeof(GenericBodyShapeFixture<>)
                .GetMethod(nameof(GenericBodyShapeFixture<object>.Create))!
                .MetadataToken);
        Assert.Equal(
            "DotnetInspector.Fixtures.GenericBodyShapeFixture<T>",
            genericMatch.TypeName);
        Assert.StartsWith(
            $"{genericMatch.TypeName}.",
            genericMatch.Member,
            StringComparison.Ordinal);
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
    public async Task Command_UsesExternalPdbForLocalNames()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"body-shape-external-pdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, Path.GetFileName(FixturePath));
        File.Copy(FixturePath, assemblyPath);
        try
        {
            var (exit, output, _) = await ConsoleCapture.RunAsync(() => Task.FromResult(
                BodyShapeCommand.Execute(new BodyShapeOptions
                {
                    Kind = "NameExpression",
                    LibraryPath = assemblyPath,
                    PdbPath = Path.ChangeExtension(FixturePath, ".pdb"),
                    RenderOptions = StyleOptionCatalog.DefaultOptions,
                    Tabular = true,
                    Tsv = true
                })));

            Assert.Equal(0, exit);
            var rows = output.Split('\n')
                .Where(row => row.Contains(
                    $".{nameof(BodyShapeFixture.ReadableLocal)}~",
                    StringComparison.Ordinal))
                .ToList();
            Assert.Contains(rows, row => row.EndsWith("\tbuilder", StringComparison.Ordinal));
            Assert.DoesNotContain(rows, row => row.EndsWith("\tstringBuilder", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(assemblyPath);
            Directory.Delete(directory);
        }
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
    public async Task Command_TsvHonorsKindOnlyProjection()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            BodyShapeCommand.Execute(new BodyShapeOptions
            {
                Kind = "ObjectCreationExpression",
                LibraryPath = FixturePath,
                Tabular = true,
                Tsv = true,
                Columns = ["Kind"]
            })));

        Assert.Equal(0, exit);
        Assert.Contains("Warning: Body-shape search skipped", error);
        Assert.Equal(
            ["kind", "ObjectCreationExpression"],
            output.Trim().Split('\n').Distinct(StringComparer.Ordinal).ToArray());
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
