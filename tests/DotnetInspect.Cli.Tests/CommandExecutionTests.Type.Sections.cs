using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task
        Type_HierarchySections_ComposeWithExactTypeAndBoundedRows()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.TypeInfo,
            "-S",
            SectionNames.Implementers,
            "--rows",
            "2",
            "--markdown");

        Assert.Equal(1, exit);
        Assert.Contains("## Type Info", output);
        Assert.Contains("## Implementers", output);
        Assert.Contains(
            typeof(WorkspaceImplementation).FullName!,
            output);
        Assert.Contains(
            typeof(WorkspaceImplementationA).FullName!,
            output);
        Assert.DoesNotContain(
            typeof(WorkspaceImplementationB).FullName!,
            output);
        Assert.Contains(
            "Hierarchy relation output reached the CLI row bound",
            error);
    }

    public abstract class HierarchyArityCollision;

    public sealed class HierarchyArityCollision<T> :
        HierarchyArityCollision;

    [Fact]
    public async Task Type_HierarchyCountOnlyReturnsExactProducerCount()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.Implementers,
            "--count");

        Assert.Equal(0, exit);
        Assert.Equal("4", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_HierarchyRetainsPackageInternalLibrarySelection()
    {
        string package = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "fixtures",
            "services",
            "signatures",
            "system.text.json.9.0.4.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Text.Json.Serialization.JsonConverter",
            "--package",
            package,
            "--library",
            "System.Text.Json.dll",
            "-S",
            SectionNames.Implementers,
            "--count");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_HierarchyRetainsRuntimeOnlyPackageSelection()
    {
        string directory =
            Directory.CreateTempSubdirectory(
                "type-hierarchy-runtime-package-test").FullName;
        string package =
            Path.Combine(directory, "runtime-only.1.0.0.nupkg");
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       package,
                       ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(
                    assembly,
                    "runtimes/any/lib/net11.0/"
                        + Path.GetFileName(assembly));
            }

            var (exit, output, error) = await RunAppAsync(
                "type",
                typeof(IWorkspaceImplementationMarker).FullName!,
                "--package",
                package,
                "--library",
                Path.GetFileName(assembly),
                "--tfm",
                "net11.0",
                "-S",
                SectionNames.Implementers,
                "--count");

            Assert.Equal(0, exit);
            Assert.Equal("4", output.Trim());
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task
        Type_HierarchyComposedTypeInfoRetainsPackageRelativeAsset()
    {
        string package = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "fixtures",
            "services",
            "signatures",
            "system.text.json.9.0.4.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Text.Json.Serialization.JsonConverter",
            "--package",
            package,
            "--library",
            "System.Text.Json.dll",
            "-S",
            SectionNames.TypeInfo,
            "-S",
            SectionNames.DerivedTypes,
            "--markdown");

        Assert.Equal(0, exit);
        Assert.Contains(
            "| Library | lib/net9.0/System.Text.Json.dll |",
            output);
        Assert.DoesNotContain("inspect-type-hierarchy", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_HierarchyPrefersSelectedReferenceAssetOverEquivalentLibrary()
    {
        string package = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "fixtures",
            "cli",
            "package-archives",
            "avalonia.12.1.2.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "type",
            "Avalonia.Controls.Button",
            "--package",
            package,
            "--library",
            "Avalonia.Controls.dll",
            "--tfm",
            "net10.0",
            "-S",
            SectionNames.DerivedTypes,
            "--count");

        Assert.Equal(0, exit);
        Assert.True(
            int.TryParse(
                output.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _),
            $"Expected a hierarchy count, got '{output}'.");
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_HierarchyRowsRetainDistinctPackageAssetCoordinates()
    {
        string package = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "fixtures",
            "cli",
            "package-archives",
            "avalonia.12.1.2.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "type",
            "Avalonia.Controls.Button",
            "--package",
            package,
            "--library",
            "Avalonia.Controls.dll",
            "--tfm",
            "net10.0",
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "50",
            "--tsv");

        Assert.Equal(0, exit);
        string[] rows =
        [
            .. output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries)
                .Skip(1),
        ];
        Assert.Equal(14, rows.Length);
        Assert.Equal(14, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            rows,
            row => row.Contains(
                "avalonia@12.1.2 "
                    + "(ref/net10.0/Avalonia.Controls.dll)",
                StringComparison.Ordinal));
        Assert.Contains(
            rows,
            row => row.Contains(
                "avalonia@12.1.2 "
                    + "(lib/net10.0/Avalonia.Controls.dll)",
                StringComparison.Ordinal));
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_DerivedTypesUsesTheSameLocalPopulation()
    {
        string assembly = typeof(SampleBaseClass).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(SampleBaseClass).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "10",
            "--tsv");

        Assert.Equal(0, exit);
        Assert.Contains(typeof(SampleDerivedClass).FullName!, output);
        Assert.Contains(typeof(AnotherDerivedClass).FullName!, output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_RelationsCategoryDiscoversBothHierarchySections()
    {
        string assembly = typeof(SampleBaseClass).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(SampleBaseClass).FullName!,
            "--library",
            assembly,
            "-D",
            SectionCategoryNames.Relations,
            "--tsv");

        Assert.Equal(0, exit);
        Assert.Contains(SectionNames.Implementers, output);
        Assert.Contains(SectionNames.DerivedTypes, output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_HierarchyJsonlRetainsBoundedRowsAndProvenance()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.Implementers,
            "--rows",
            "2",
            "--jsonl");

        Assert.Equal(1, exit);
        string[] lines =
            output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
        {
            using JsonDocument row = JsonDocument.Parse(line);
            Assert.Equal(
                "DotnetInspect.Cli.Tests",
                row.RootElement.GetProperty("library").GetString());
            Assert.Equal(
                assembly,
                row.RootElement.GetProperty("source").GetString());
        }
        Assert.Contains(
            "Hierarchy relation output reached the CLI row bound",
            error);
    }

    [Fact]
    public async Task Type_HierarchyTailRowsAreRejectedExplicitly()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.Implementers,
            "--rows",
            "2",
            "--tail");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot select tail rows without materializing the complete "
                + "relation population",
            error);
    }

    [Fact]
    public async Task
        Type_ImplementersMatchesLegacyImplementsResultSetForLocalFixture()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;
        string target =
            typeof(IWorkspaceImplementationMarker).FullName!;

        var legacy = await RunAppAsync(
            "implements",
            target,
            "--library",
            assembly,
            "--all",
            "--json");
        var hierarchy = await RunAppAsync(
            "type",
            target,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.Implementers,
            "--rows",
            "100",
            "--jsonl");

        Assert.Equal(0, legacy.Exit);
        Assert.Equal(0, hierarchy.Exit);
        Assert.Empty(legacy.Error);
        Assert.Empty(hierarchy.Error);
        using JsonDocument legacyDocument =
            JsonDocument.Parse(legacy.Output);
        string[] legacyTypes =
        [
            .. legacyDocument.RootElement
                .EnumerateArray()
                .Select(row => row.GetProperty("type").GetString()!),
        ];
        string[] hierarchyTypes =
        [
            .. hierarchy.Output
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement
                        .GetProperty("type")
                        .GetString()!;
                }),
        ];

        Assert.Equal(legacyTypes, hierarchyTypes);
    }

    [Fact]
    public async Task
        Type_HierarchyPrefersExactNonGenericFocusOverGenericShorthand()
    {
        string assembly = typeof(HierarchyArityCollision).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(HierarchyArityCollision).FullName!,
            "--library",
            assembly,
            "--all",
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "10",
            "--tsv");

        Assert.Equal(0, exit);
        Assert.Contains(
            typeof(HierarchyArityCollision<>)
                .FullName!
                .Replace('+', '.'),
            output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_PlatformHierarchyDefaultsToAllFrameworkFamilies()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IO.Stream",
            "--platform",
            "System.Runtime",
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "100",
            "--jsonl");

        Assert.Equal(0, exit);
        string[] types =
        [
            .. output
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement
                        .GetProperty("type")
                        .GetString()!;
                }),
        ];
        Assert.Equal(22, types.Length);
        Assert.Equal(22, types.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            "Microsoft.AspNetCore.WebUtilities.BufferedReadStream",
            types);
        Assert.Contains(
            "Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream",
            types);
        Assert.Contains(
            "Microsoft.AspNetCore.WebUtilities.FileBufferingWriteStream",
            types);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_PlatformHierarchyExplicitFrameworkNarrowsThePopulation()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IO.Stream",
            "--platform",
            "System.Runtime",
            "--framework",
            "runtime",
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "100",
            "--jsonl");

        Assert.Equal(0, exit);
        string[] types =
        [
            .. output
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement
                        .GetProperty("type")
                        .GetString()!;
                }),
        ];
        Assert.Equal(19, types.Length);
        Assert.Equal(19, types.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.WebUtilities.BufferedReadStream",
            types);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream",
            types);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.WebUtilities.FileBufferingWriteStream",
            types);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_PlatformHierarchyKeepsCoreLibAsTheExactTypeFocus()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IO.Stream",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.TypeInfo,
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "100",
            "--markdown");

        Assert.Equal(0, exit);
        AssertPlatformTypeInfo(output, "System.Private.CoreLib");
        Assert.Contains(
            "Microsoft.AspNetCore.WebUtilities.BufferedReadStream",
            output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_PlatformHierarchyKeepsForwardingSourceCoordinate()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IO.Stream",
            "--platform",
            "System.Runtime",
            "-S",
            SectionNames.TypeInfo,
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "100",
            "--markdown");

        Assert.Equal(0, exit);
        string normalizedOutput = output.Replace('\\', '/');
        Assert.Contains(
            "/shared/Microsoft.NETCore.App/",
            normalizedOutput);
        Assert.Contains(
            "/System.Runtime.dll",
            normalizedOutput);
        Assert.DoesNotContain(
            "/packs/Microsoft.NETCore.App.Ref/",
            normalizedOutput);
        Assert.DoesNotContain(
            "/System.Private.CoreLib.dll",
            normalizedOutput);
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        Type_PlatformHierarchyKeepsEqualIdentityImplementationFocus()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "Microsoft.AspNetCore.Http.DefaultHttpContext",
            "--platform",
            "Microsoft.AspNetCore.Http",
            "--all",
            "-S",
            SectionNames.Fields,
            "-S",
            SectionNames.DerivedTypes,
            "--count");

        Assert.Equal(0, exit);
        Assert.Contains("| Derived Types | 0 |", output);
        Assert.Contains("| Fields | 14 |", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("ConcurrentDictionary")]
    [InlineData(
        "System.Collections.Concurrent.ConcurrentDictionary`2")]
    public async Task
        Type_PlatformHierarchyKeepsGenericImplementationFocus(
            string typeName)
    {
        var (countExit, countOutput, countError) = await RunAppAsync(
            "type",
            typeName,
            "--platform",
            "System.Collections.Concurrent",
            "--all",
            "-S",
            SectionNames.Fields,
            "-S",
            SectionNames.DerivedTypes,
            "--count");

        Assert.Equal(0, countExit);
        Assert.Contains("| Fields | 5 |", countOutput);
        Assert.Empty(countError);

        var (infoExit, infoOutput, infoError) = await RunAppAsync(
            "type",
            typeName,
            "--platform",
            "System.Collections.Concurrent",
            "--all",
            "-S",
            SectionNames.TypeInfo,
            "-S",
            SectionNames.DerivedTypes,
            "--rows",
            "10",
            "--markdown");

        Assert.Equal(0, infoExit);
        string normalizedOutput = infoOutput.Replace('\\', '/');
        Assert.Contains(
            "/shared/Microsoft.NETCore.App/",
            normalizedOutput);
        Assert.Contains(
            "/System.Collections.Concurrent.dll",
            normalizedOutput);
        Assert.DoesNotContain(
            "/packs/Microsoft.NETCore.App.Ref/",
            normalizedOutput);
        Assert.DoesNotContain(
            "/System.Private.CoreLib.dll",
            normalizedOutput);
        Assert.Empty(infoError);
    }

    [Fact]
    public async Task Type_SingleType_SelectSection_RendersSectionNotShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            FormatExplicitlySet = true,
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selection produces a focused section view, not the tree shape.
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("├─", output);
    }

    /// <summary>
    /// <c>Type Info</c> is the type view's identity fact table, and the only section on the type
    /// pipeline that does not grow with the type under inspection.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_RendersIdentityFactsRatherThanMembers()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = [SectionNames.TypeInfo],
            FormatExplicitlySet = true,
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        AssertPlatformTypeInfo(output, "System.Text.Json");
        Assert.Contains("| Type | System.Text.Json.JsonSerializer |", output);
        Assert.Contains("| Kind | class |", output);
        // Identity, not inventory: the member sections stay out.
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Method Groups", output);
    }

    /// <summary>
    /// The section is <c>ExplicitOnly</c>, so it must not join the default markdown view, where
    /// the same facts already render as the inline identity line. This is the gate for the
    /// "new sections do not enter the default -v:m view" rule for this section.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotEnterTheDefaultMarkdownView()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MarkdownExplicitlySet = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Type Info", output);
        // The identity facts are present, just inline rather than as a section.
        Assert.Contains("Kind: class", output);
    }

    /// <summary>
    /// The boundedness claim: the row set is a function of which facts apply to the type, never of
    /// how many members it has. A 200+ member type must not produce a materially larger section
    /// than an 8-member enum, and every label it emits must come from the same fixed vocabulary.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotGrowWithTheType()
    {
        var large = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "String");
        var small = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "DayOfWeek");

        Assert.NotEmpty(large);
        Assert.NotEmpty(small);

        // The vocabulary is derived from TypeInfoSection's declaration rather than copied here, so
        // the declaration drives the gate: a row whose label is not a declared property fails, and
        // the bound tracks the property count instead of a hand-maintained literal that goes stale.
        var vocabulary = DeclaredTypeInfoLabels();

        // Deriving the vocabulary keeps it from going stale, but on its own it would absorb a new
        // property silently - including an unbounded one. Pinning the count makes any addition fail
        // here, so whoever adds a property has to state that it is a fixed fact about the type and
        // not a per-member row. Bump this only alongside that judgement.
        Assert.Equal(11, vocabulary.Count);

        Assert.All(large, label => Assert.Contains(label, vocabulary));
        Assert.All(small, label => Assert.Contains(label, vocabulary));

        // The bound that makes the section Fixed: one row per declared property, never one per
        // member. String has 250+ members and DayOfWeek has 7; both stay inside a constant.
        Assert.True(
            large.Count <= vocabulary.Count,
            $"Type Info grew past its declared vocabulary: {string.Join(", ", large)}");
    }

    /// <summary>
    /// Effective <c>-D</c> must list the fields <c>Type Info</c> actually renders. Two things can
    /// break this and both did: the section is a <c>Field</c>/<c>Value</c> fact table, so matching
    /// the schema against rendered *table columns* intersects nothing and reports the section as
    /// having no queryable fields at all; and the discovery render manifest is built without
    /// acquisition context, so the provenance rows are invisible to it unless that context is
    /// threaded in. Either failure is silent — exit 0 with fields missing.
    /// </summary>
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Collections.Generic.List`1")]
    // An enum is the case that catches a census computed off a different member list than the one
    // discovery sees: DayOfWeek's only field is the compiler-generated `value__`.
    [InlineData("System.DayOfWeek")]
    // A readonly ref struct: BuildFilteredTypeForSections did not copy IsReadOnly/IsByRefLike, so
    // discovery hid the Modifiers row that -S renders.
    [InlineData("System.Span`1")]
    [InlineData("System.DateTime")]
    // Filters narrow the type discovery builds its manifest from. Type Info reports identity, not
    // the filtered slice, so its field set must not move when a filter is active.
    [InlineData("System.String", "-m", "Contains")]
    [InlineData("System.String", "--all")]
    [InlineData("System.String", "-k", "property")]
    [InlineData("System.Span`1", "--unsafe")]
    public async Task Type_TypeInfoSection_EffectiveDiscovery_ListsTheFieldsItRenders(
        string typeName,
        params string[] extraArgs)
    {
        string[] discoverArgs = ["type", typeName, .. extraArgs, "-D", SectionNames.TypeInfo];
        string[] renderArgs = ["type", typeName, .. extraArgs, "-S", SectionNames.TypeInfo, "--markdown"];

        var (discoverExit, discoverOutput, _) = await RunAppAsync(discoverArgs);
        var (renderExit, renderOutput, _) = await RunAppAsync(renderArgs);

        Assert.Equal(0, discoverExit);
        Assert.Equal(0, renderExit);

        var advertised = ParseFirstColumn(discoverOutput, "Name");
        var rendered = ParseFirstColumn(renderOutput, "Field");

        Assert.NotEmpty(advertised);
        Assert.NotEmpty(rendered);

        // Structural facts the manifest can see from the type itself.
        Assert.Contains("Kind", advertised);
        // Provenance facts that only exist if acquisition context reached the manifest.
        Assert.Contains("Library", advertised);
        Assert.Contains("Source", advertised);

        // Set equality, not containment: -D over-reporting a field -S never renders is the same
        // contract break as under-reporting one, and only equality catches both.
        Assert.Equal(
            rendered.OrderBy(f => f, StringComparer.Ordinal),
            advertised.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("System.Text.StringBuilder", "Append")]
    [InlineData("System.Collections.Generic.List`1", "Add")]
    [InlineData("System.IDisposable", "Dispose")]
    [InlineData("System.Action", "Invoke")]
    [InlineData("System.Enum", "HasFlag")]
    public async Task
        Type_DirectLibraryExactType_TypeInfoDiscoveryMatchesLegacyProjection(
            string typeName,
            string memberName)
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        string[] directArgs =
        [
            "type",
            typeName,
            "--library",
            assemblyPath,
            "-D",
            SectionNames.TypeInfo,
            "--tsv",
        ];
        string[] legacyArgs =
        [
            "type",
            typeName,
            "--library",
            assemblyPath,
            "-m",
            memberName,
            "-D",
            SectionNames.TypeInfo,
            "--tsv",
        ];

        var (directExit, directOutput, directError) =
            await RunAppAsync(directArgs);
        var (legacyExit, legacyOutput, legacyError) =
            await RunAppAsync(legacyArgs);

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            legacyExit == 0,
            $"Legacy discovery failed: {legacyError}");
        Assert.Empty(directError);
        Assert.Empty(legacyError);
        Assert.Equal(legacyOutput, directOutput);
    }

    [Fact]
    public async Task
        Type_DirectLibraryCoreDelegate_TypeInfoDiscoveryMatchesLegacyProjection()
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                "System.MulticastDelegate",
                "--library",
                assemblyPath,
                "-D",
                SectionNames.TypeInfo,
                "--tsv");
        var (legacyExit, legacyOutput, legacyError) =
            await RunAppAsync(
                "type",
                "System.MulticastDelegate",
                "--library",
                assemblyPath,
                "-k",
                "constructor",
                "-D",
                SectionNames.TypeInfo,
                "--tsv");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            legacyExit == 0,
            $"Legacy discovery failed: {legacyError}");
        Assert.Empty(directError);
        Assert.Empty(legacyError);
        Assert.Equal(legacyOutput, directOutput);
    }

    [Theory]
    [InlineData("System.Text.StringBuilder")]
    [InlineData("System.IDisposable")]
    [InlineData("System.Action")]
    [InlineData("System.Delegate")]
    [InlineData("System.IAsyncResult")]
    public async Task
        Type_DirectLibraryExactType_BareDiscoveryMatchesPlatformProjection(
            string typeName)
    {
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                typeof(System.Text.StringBuilder).Assembly.Location,
                "-D",
                "--tsv");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "--tsv");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
    }

    [Fact]
    public async Task
        Type_DirectLibraryExactType_IncludeAllBareDiscoveryMatchesPlatformProjection()
    {
        string typeName = "System.Delegate";
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                typeof(System.Text.StringBuilder).Assembly.Location,
                "-D",
                "--all",
                "--tsv");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "--all",
                "--tsv");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
        Assert.Contains(
            $"{SectionNames.ExtensionMethods}\tsection",
            directOutput);
    }

    [Fact]
    public async Task
        Type_PackageBackedLibraryDiscoveryRetainsPackageMembershipValidation()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) =
                await RunAppAsync(
                    "type",
                    "DotnetInspect.Cli.Tests.CommandExecutionTests",
                    "--package",
                    packagePath,
                    "--library",
                    TestAssemblyPath,
                    "-D",
                    SectionNames.TypeInfo,
                    "--tsv");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "not found in package",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        Type_DirectLibraryGenericBareDiscoveryRetainsContextualExtensions()
    {
        var (exitCode, output, error) =
            await RunAppAsync(
                "type",
                "System.Collections.Generic.List`1",
                "--library",
                typeof(System.Text.StringBuilder).Assembly.Location,
                "-D",
                "--tsv");

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.Contains("Extension Methods\tsection", output);
        Assert.Contains("Interfaces\tsection", output);
    }

    [Theory]
    [InlineData(
        "System.Text.StringBuilder",
        "System.Private.CoreLib")]
    [InlineData(
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Private.CoreLib")]
    [InlineData(
        "System.Text.Json.Nodes.JsonArray",
        "System.Text.Json")]
    public async Task
        Type_DirectLibraryExactType_AuditDiscoveryMatchesPlatformProjection(
            string typeName,
            string platformAssembly)
    {
        string assemblyPath = platformAssembly
            == "System.Private.CoreLib"
                ? typeof(System.Text.StringBuilder).Assembly.Location
                : typeof(System.Text.Json.Nodes.JsonArray)
                    .Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                assemblyPath,
                "-D",
                SectionCategoryNames.Audit,
                "--tsv");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                platformAssembly,
                "-D",
                SectionCategoryNames.Audit,
                "--tsv");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
    }

    [Theory]
    [InlineData(
        "System.Text.StringBuilder",
        "System.Private.CoreLib",
        SectionNames.SafetyFacts)]
    [InlineData(
        "System.Text.StringBuilder",
        "System.Private.CoreLib",
        SectionNames.UnsafeMembers)]
    [InlineData(
        "System.Text.Json.Nodes.JsonArray",
        "System.Text.Json",
        SectionNames.SafetyFacts)]
    [InlineData(
        "System.Text.Json.Nodes.JsonArray",
        "System.Text.Json",
        SectionNames.UnsafeMembers)]
    public async Task
        Type_DirectLibraryExactType_AuditSectionDiscoveryMatchesPlatformProjection(
            string typeName,
            string platformAssembly,
            string section)
    {
        string assemblyPath = platformAssembly
            == "System.Private.CoreLib"
                ? typeof(System.Text.StringBuilder).Assembly.Location
                : typeof(System.Text.Json.Nodes.JsonArray)
                    .Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                assemblyPath,
                "-D",
                section,
                "--tsv");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                platformAssembly,
                "-D",
                section,
                "--tsv");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
    }

    [Theory]
    [InlineData("System.IDisposable", SectionNames.Facts)]
    [InlineData("System.Attribute", SectionNames.Methods)]
    public async Task
        Type_SelectedSectionDiscoveryMatchesPlatformProjection(
            string typeName,
            string section)
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                assemblyPath,
                "-D",
                "-S",
                section);
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "-S",
                section);

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
    }

    [Fact]
    public async Task
        Type_DeferredSelectedSectionMatchesPlatformRejection()
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                "System.Attribute",
                "--library",
                assemblyPath,
                "-D",
                "-S",
                "Classes",
                "--tsv");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                "System.Attribute",
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "-S",
                "Classes",
                "--tsv");

        Assert.Equal(1, directExit);
        Assert.Equal(platformExit, directExit);
        Assert.Equal(platformOutput, directOutput);
        Assert.Equal(platformError, directError);
    }

    [Fact]
    public async Task
        Type_DirectLibraryExactType_NonPublicRequiresIncludeAll()
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                "System.Text.StringBuilderCache",
                "--library",
                assemblyPath,
                "-D",
                SectionNames.TypeInfo,
                "--tsv");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                "System.Text.StringBuilderCache",
                "--library",
                assemblyPath,
                "--all",
                "-D",
                SectionNames.TypeInfo,
                "--tsv");

        Assert.Equal(1, defaultExit);
        Assert.Empty(defaultOutput);
        Assert.Contains(
            "Type 'System.Text.StringBuilderCache' not found.",
            defaultError);
        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.NotEmpty(allOutput);
    }

    [Fact]
    public async Task
        Type_DirectLibraryExactType_HiddenRequiresIncludeAll()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.HiddenExactTypeDiscoveryProbe";
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                TestAssemblyPath,
                "-D",
                SectionNames.TypeInfo,
                "--tsv");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                TestAssemblyPath,
                "--all",
                "-D",
                SectionNames.TypeInfo,
                "--tsv");

        Assert.Equal(1, defaultExit);
        Assert.Empty(defaultOutput);
        Assert.Contains(
            $"Type '{typeName}' not found.",
            defaultError);
        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.NotEmpty(allOutput);
    }

    [Theory]
    [InlineData("System.__Canon", true)]
    [InlineData(
        "DotnetInspect.Cli.Tests.__CompilerGeneratedExactTypeDiscoveryProbe",
        false)]
    public async Task
        Type_DirectLibraryExactType_CompilerGeneratedIsNotApi(
            string typeName,
            bool coreLibrary)
    {
        string assemblyPath = coreLibrary
            ? typeof(System.Text.StringBuilder).Assembly.Location
            : TestAssemblyPath;
        var (exit, output, error) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                assemblyPath,
                "--all",
                "-D",
                SectionNames.TypeInfo,
                "--tsv");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Type '{typeName}' not found.",
            error);
    }

    [Theory]
    [InlineData("System.Attribute", false)]
    [InlineData("System.TimeSpan", true)]
    public async Task
        Type_DetailedDiscoveryMatchesPlatformProjection(
            string typeName,
            bool excludesUnsafeMembers)
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                typeName,
                "--library",
                assemblyPath,
                "-D",
                "-v:d");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                typeName,
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "-v:d");

        Assert.True(
            directExit == 0,
            $"Direct discovery failed: {directError}");
        Assert.True(
            platformExit == 0,
            $"Platform discovery failed: {platformError}");
        Assert.Empty(directError);
        Assert.Empty(platformError);
        Assert.Equal(platformOutput, directOutput);
        if (excludesUnsafeMembers)
        {
            Assert.DoesNotContain(
                SectionNames.UnsafeMembers,
                directOutput);
        }

    }

    [Fact]
    public async Task
        Type_TreeDiscoveryMatchesPlatformProjection()
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        var (directExit, directOutput, directError) =
            await RunAppAsync(
                "type",
                "System.IDisposable",
                "--library",
                assemblyPath,
                "-D",
                "--tree");
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(
                "type",
                "System.IDisposable",
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "--tree");

        Assert.Equal(platformExit, directExit);
        Assert.Equal(platformOutput, directOutput);
        Assert.Equal(platformError, directError);
        Assert.Contains("Signature", directOutput);
        Assert.Contains("Return Type", directOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        Type_QuietDiscoveryMatchesPlatformRejection(
            bool tree)
    {
        string assemblyPath =
            typeof(System.Text.StringBuilder).Assembly.Location;
        string[] directArgs = tree
            ?
            [
                "type",
                "System.Attribute",
                "--library",
                assemblyPath,
                "-D",
                "--tree",
                "-v:q",
            ]
            :
            [
                "type",
                "System.Attribute",
                "--library",
                assemblyPath,
                "-D",
                "-v:q",
            ];
        string[] platformArgs = tree
            ?
            [
                "type",
                "System.Attribute",
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "--tree",
                "-v:q",
            ]
            :
            [
                "type",
                "System.Attribute",
                "--platform",
                "System.Private.CoreLib",
                "-D",
                "-v:q",
            ];

        var (directExit, directOutput, directError) =
            await RunAppAsync(directArgs);
        var (platformExit, platformOutput, platformError) =
            await RunAppAsync(platformArgs);

        Assert.Equal(1, directExit);
        Assert.Equal(platformExit, directExit);
        Assert.Equal(platformOutput, directOutput);
        Assert.Equal(platformError, directError);
        Assert.Contains(
            "-v:q is not supported by the type shape renderer.",
            directError);
    }

    [Fact]
    public async Task Type_TypeInfoDiscovery_DoesNotRunUnrequestedUnsafeProbe()
    {
        var (assemblyPath, fixtureDir) =
            CreateIncompleteUnsafeDiscoveryAssembly();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type",
                "DiscoveryFixtures.IncompleteUnsafeDiscovery",
                "--library",
                assemblyPath,
                "-D",
                SectionNames.TypeInfo);

            Assert.Equal(0, exit);
            Assert.NotEmpty(output);
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
        }
    }

    [Fact]
    public async Task Type_BareDiscovery_DoesNotRunUnlistedUnsafeProbe()
    {
        var (assemblyPath, fixtureDir) =
            CreateIncompleteUnsafeDiscoveryAssembly();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type",
                "DiscoveryFixtures.IncompleteUnsafeDiscovery",
                "--library",
                assemblyPath,
                "-D",
                "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("@Audit\tcategory", output);
            Assert.DoesNotContain(
                $"{SectionNames.UnsafeMembers}\tsection",
                output);
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
        }
    }

    /// <summary>
    /// Type parameters are an identity fact, so an open generic must report them. The summary used
    /// to be computed only at quiet verbosity for the inline header, which left the section's
    /// declared field permanently empty and made <c>--fields "Type Parameters"</c> report no data.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_ReportsTypeParametersForOpenGenerics()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "System.Collections.Generic.List`1", "-S", SectionNames.TypeInfo, "--markdown");

        Assert.Equal(0, exit);
        Assert.Contains("| Type Parameters | T |", output);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_TypeInfoSection_NonTabularValidEmptyFieldReportsNoData(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.TypeInfo,
            "--fields",
            "Type Parameters",
            format);

        Assert.Equal(0, exit);
        Assert.Empty(output.Trim());
        Assert.Contains(
            "Note: 1 field has no data: Type Parameters",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_FieldReplayDoesNotCreditProjectedAwayFieldTable(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info,Methods",
            "--fields",
            "Interfaces",
            "--columns",
            "Signature",
            "--rows",
            "1",
            format);

        Assert.Equal(0, exit);
        Assert.Contains("Methods", output);
        Assert.DoesNotContain("Type Info", output);
        Assert.Contains(
            "Note: 1 field has no data: Interfaces",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_NonTabularUnknownFieldWithoutSectionFails(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--fields",
            "NoSuchField",
            format);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "No fields matched projection: NoSuchField",
            error);
    }

    /// <summary>
    /// Explicit selection reaches sections that are not in the fixed overview.
    /// </summary>
    [Fact]
    public async Task Type_ExplicitSelect_StillReachesGrowingSections()
    {
        var (exit, output, _) = await RunAppAsync("type", "System.String", "-S", "Fields", "--markdown");

        Assert.Equal(0, exit);
        Assert.Equal(["Fields"], SectionHeadings(output));
    }

    /// <summary>
    /// A dotted name that does not resolve to a type renders a listing, so a listing section name
    /// has to be selectable on it. The preamble picks its pipeline from the argument shape, long
    /// before the assembly is read, so it was answering for the single-type view: <c>-D</c>
    /// advertised <c>Classes</c> and <c>-S Classes</c> was rejected on the same command line,
    /// against a section list the user was never shown.
    /// </summary>
    [Theory]
    [InlineData("Classes")]
    [InlineData(SectionNames.ApiInfo)]
    public async Task Type_PrefixBrowse_ListingSectionName_IsSelectable(string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", section, "--markdown");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error, StringComparison.Ordinal);

        // Renders the named section and only it -- a fall-through to the verbosity ladder would
        // also pass a bare "contains Classes" check.
        Assert.Equal([section], SectionHeadings(output));
    }

    /// <summary>
    /// The deferral must not leak into the view it was deferred for. A type that resolves renders a
    /// single type, where a listing section name is exactly as wrong as it was before -- reported
    /// against the single-type pipeline, with that pipeline's sections offered.
    /// </summary>
    [Fact]
    public async Task Type_SingleType_ListingSectionName_IsStillRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Classes");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);

        // Names the pipeline that rejected it, so the message is actionable rather than merely
        // negative -- and proves the single-type list is what was consulted.
        Assert.Contains(SectionNames.Baseclass, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no prefix matches there is no listing for a deferred select to belong to, so it is
    /// reported exactly as the preamble would have reported it. Holding the rejection must not turn
    /// into dropping it.
    /// </summary>
    [Fact]
    public async Task Type_UnresolvedTypeWithoutPrefixMatches_StillReportsTheDeferredSelect()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Zqqxnomatch", "--library", TestAssemblyPath, "-S", "Classes");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that is valid for neither pipeline is a plain typo and still fails in the preamble,
    /// keeping the fast rejection -- and the single-type suggestions -- for the case that cannot be
    /// a listing.
    /// </summary>
    /// <remarks>
    /// This is the gate for that claim, and for the guard that carries it: dropping the
    /// "resolves against the listing" test from <c>ShouldDeferSelectToListing</c> would defer every
    /// total failure, so a typo would announce a prefix browse it never performs and then offer the
    /// listing's sections. Asserting only the exit code and the "not found" text cannot see that --
    /// both survive the deferral, because a landing site rejects the typo either way. The two
    /// negative assertions below are what make the difference observable.
    /// </remarks>
    [Theory]
    [InlineData("Command")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests")]
    public async Task Type_SelectValidForNeitherPipeline_FailsRegardlessOfWhatTheNameResolvesTo(string target)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", target, "--library", TestAssemblyPath, "-S", "Zzznosuchsection");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", error, StringComparison.Ordinal);
        Assert.DoesNotContain("best-effort prefix matches", error, StringComparison.Ordinal);
        Assert.Contains("Baseclass", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every consumer of a deferred select has to resolve it, not just the one that renders the
    /// table. <c>--count</c> checks section arity in the preamble and discovery filters by the
    /// selected sections, so both would otherwise read the deferral's empty include set as "no
    /// sections" and answer about the wrong thing.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_ListingSectionName_ReachesCountAndDiscovery()
    {
        var (countExit, countOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--count");

        Assert.Equal(0, countExit);

        // Agrees with the rows the same selection renders, so this cannot pass by counting a
        // different section or an unfiltered surface.
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv");
        Assert.Equal(0, rowsExit);
        var rowCount = rowsOutput.Split('\n').Count(l => l.Trim().Length > 0) - 1;
        Assert.Equal(rowCount, int.Parse(countOutput.Trim()));

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "-D");

        Assert.Equal(0, discoverExit);
        Assert.Contains("Classes", discoverOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Enums", discoverOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DefaultCount_UsesTypePopulation()
    {
        var (countExit, countOutput, countError) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath,
            "--count");
        var (rowsExit, rowsOutput, rowsError) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath,
            "--tsv");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);
        Assert.Contains(
            "Showing best-effort prefix matches",
            countError,
            StringComparison.Ordinal);
        Assert.Contains(
            "Showing best-effort prefix matches",
            rowsError,
            StringComparison.Ordinal);

        int rowCount = rowsOutput
            .Split('\n')
            .Count(line => line.Trim().Length > 0)
            - 1;
        Assert.Equal(
            rowCount,
            int.Parse(
                countOutput.Trim(),
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryValidOnlyForListingIsDeferred()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "Command",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Kind", output, StringComparison.Ordinal);
        Assert.Contains("Type", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_ExactMatch_ListingOnlyDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "DotnetInspect.Cli.Tests.CommandExecutionTests",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            SectionNames.Baseclass,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_NoPrefixMatch_DeferredDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "Zqqxnomatch",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IAsync",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Classes",
            "--table");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_SharedDiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Str",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Interfaces",
            "--table");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Interfaces' not found",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred select must use the listing pipeline for both count-map ordering and reduction;
    /// otherwise it either retains the obsolete single-section rejection or emits one scalar total.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,Enums",
            "--count", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Classes", "Enums"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    [Fact]
    public async Task Type_ExactMatch_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Type Info,Methods", "--count", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Methods", "Type Info"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    /// <summary>
    /// The preamble runs four selection checks -- --count arity, shape-projection arity, --print
    /// selection, and tabular arity -- and every one of them reads the include set. A deferred
    /// select leaves that set empty, so each check has to ask the listing pipeline what the select
    /// resolves to or it silently judges nothing. Only --count was made deferral-aware at first;
    /// the tabular check then let a two-section select through and rendered just the first table at
    /// exit 0, which the direct listing rejects. Pinned per flag so a regression names its own site.
    /// </summary>
    [Theory]
    [InlineData("--tsv")]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    public async Task Type_PrefixBrowse_MultiSectionSelect_FailsTabularArityLikeTheDirectListing(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,API Info", format);

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error, StringComparison.Ordinal);
        Assert.DoesNotContain("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single-section select must still reach the renderer once the tabular check consults the
    /// listing pipeline, or the fix for the multi-section case would simply reject everything.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_SingleSectionSelect_StillRendersTabular()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv");

        Assert.Equal(0, exit);
        Assert.Contains("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The payload projections are not supported by the listing at all, so the deferred path must
    /// reach that diagnostic rather than the arity one. Judging the deferral's empty include set
    /// reported "requires -S/--select to match exactly one section" for a select that resolves to
    /// exactly one -- telling the user to narrow a selector that was never the problem.
    /// </summary>
    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    [InlineData("--print")]
    public async Task Type_PrefixBrowse_PayloadProjection_ReportsTheListingReasonNotArity(string flag)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", flag);

        Assert.Equal(1, exit);
        Assert.Contains("is not supported when listing types", error, StringComparison.Ordinal);
        Assert.DoesNotContain("exactly one section", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deferred select must actually narrow the rendered listing, not merely stop failing.
    /// A prefix whose matches are all one kind cannot show the difference -- selecting Classes and
    /// selecting nothing render the same single table -- so this uses a prefix that matches four
    /// kinds, where a dropped selector is visible as the other three sections surviving.
    /// </summary>
    [Theory]
    [InlineData("--all")]
    public async Task Type_PrefixBrowse_DeferredSelect_NarrowsAMultiKindListing(string flag)
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Json", "--platform", "System.Text.Json", "-S", "Classes", "--markdown", flag);

        Assert.Equal(0, exit);

        var headings = SectionHeadings(output);
        Assert.Equal(["Classes"], headings);
    }

    /// <summary>
    /// The platform prefix browse renders a listing for what entered as a single-type request, so
    /// it needs the same re-resolution the local prefix browse does. It is reached by a different
    /// route -- the wide fallback, after the local lookup finds neither the type nor a prefix match
    /// -- so covering only the local browse would leave this one dropping the selector silently.
    /// </summary>
    [Fact]
    public async Task Type_PlatformPrefixBrowse_ListingSectionName_IsSelectable()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Classes", "--markdown");

        Assert.Equal(0, exit);

        // Names the route, so that this silently becoming the local browse -- which has its own
        // coverage -- shows up as a failure rather than as duplicate coverage of one path.
        Assert.Contains("platform prefix matches", error, StringComparison.Ordinal);
        Assert.Equal(["Classes"], SectionHeadings(output));

        // A name valid for neither pipeline still fails on this route.
        var (bogusExit, _, bogusError) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Zzznosuchsection");
        Assert.Equal(1, bogusExit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", bogusError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_DiscoverDetails_ReportsDeclaredShapeAndCardinality()
    {
        // Type adopts Section shapes: -D --details is a structural view that
        // carries each section's declared shape beside its cardinality, for
        // the type listing (type/...) and the single-type catalog (member/...).
        var (listingExit, listing, listingError) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-D", "--details");

        Assert.Equal(0, listingExit);
        Assert.Empty(listingError);
        Assert.Contains(
            "| Name | Kind | Path | Formats | Shape | Cardinality | Terminals |",
            listing);
        Assert.Contains(
            "| Classes | section | type/sections/classes "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl "
            + "| table | inventory | rows, count |",
            listing);
        Assert.Contains(
            "| API Info | section | type/sections/api-info "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl "
            + "| table | scalar |  |",
            listing);

        var (typeExit, type, typeError) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-D", "--details");

        Assert.Equal(0, typeExit);
        Assert.Empty(typeError);
        Assert.Contains(
            "| Type Info | section | member/sections/type-info "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl "
            + "| table | scalar |  |",
            type);
        Assert.Contains(
            "| Methods | section | member/sections/methods "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl "
            + "| table | inventory | rows, count |",
            type);

        // --details is structural: it does not probe the type, so it lists
        // the catalog rather than the sections with data, and JSON rows carry
        // the same declared facts.
        var (jsonExit, json, jsonError) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath,
            "-D", SectionNames.DecompiledSource, "--details", "--json");

        Assert.Equal(0, jsonExit);
        Assert.Empty(jsonError);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("text", row.GetProperty("shape").GetString());
        Assert.Equal("scalar", row.GetProperty("cardinality").GetString());
        Assert.Equal("member/sections/decompiled-source", row.GetProperty("path").GetString());

        // Target-free: --details is structural, so it needs no type or source
        // and reports the type listing catalog with the same declared facts
        // (round-2 finding: this path used to fall back to the Name/Kind view).
        var (freeExit, free, freeError) = await RunAppAsync(
            "type", "-D", "--details", "--json");

        Assert.Equal(0, freeExit);
        Assert.Empty(freeError);
        using JsonDocument freeDocument = JsonDocument.Parse(free);
        JsonElement classes = freeDocument.RootElement.EnumerateArray()
            .Single(static r => r.GetProperty("name").GetString() == "Classes");
        Assert.Equal("type/sections/classes", classes.GetProperty("path").GetString());
        Assert.Equal("table", classes.GetProperty("shape").GetString());
        Assert.Equal("inventory", classes.GetProperty("cardinality").GetString());

        var (bareExit, bare, bareError) = await RunAppAsync("type", "-D", "--json");

        Assert.Equal(0, bareExit);
        Assert.Empty(bareError);
        Assert.DoesNotContain("\"shape\"", bare);

        // Source-less qualified type: --details follows the same target
        // resolution as bare -D (the platform type's member catalog), not the
        // parser-level --schema ambiguity rule (round-3 finding).
        var (platformExit, platform, platformError) = await RunAppAsync(
            "type", "System.String", "-D", SectionNames.Methods, "--details", "--json");

        Assert.Equal(0, platformExit);
        Assert.Empty(platformError);
        using JsonDocument platformDocument = JsonDocument.Parse(platform);
        JsonElement platformRow = Assert.Single(platformDocument.RootElement.EnumerateArray());
        Assert.Equal("member/sections/methods", platformRow.GetProperty("path").GetString());
        Assert.Equal("table", platformRow.GetProperty("shape").GetString());
    }

    [Fact]
    public async Task Type_LoneTableSection_StreamsTsvUnlessAFormatIsNamed()
    {
        // Section shapes, type adoption slice 2: a lone explicitly selected
        // Table renders its native TSV rows when no format is named; an
        // explicit --markdown keeps the composed document, and --json is
        // untouched. A bare -n on the TSV stream is the rendered-line window.
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
            "--library", TestAssemblyPath, "--all", "-S", SectionNames.Constructors);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("name\t", lines[0]);
        Assert.DoesNotContain("## Constructors", output);
        Assert.DoesNotContain("# DotnetInspect.Cli.Tests", output);

        var (markdownExit, markdown, markdownError) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
            "--library", TestAssemblyPath, "--all", "-S", SectionNames.Constructors, "--markdown");

        Assert.Equal(0, markdownExit);
        Assert.Empty(markdownError);
        Assert.Contains("## Constructors", markdown);

        // The type listing's lone Table streams TSV too, through the deferred
        // listing path.
        var (listingExit, listing, listingError) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", SectionNames.Classes);

        Assert.Equal(0, listingExit);
        Assert.Empty(listingError);
        Assert.StartsWith("kind\ttype\t", listing);
    }

    [Fact]
    public async Task Type_LoneScalarSection_RejectsRowTerminalsBeforeAcquisition()
    {
        // Type Info and API Info are scalar records: --count and --rows fail
        // with the shape-aware diagnostic before the type is inspected, while
        // the record itself streams as a field/value TSV.
        var (countExit, countOutput, countError) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
            "--library", TestAssemblyPath, "--all", "-S", SectionNames.TypeInfo, "--count");

        Assert.Equal(1, countExit);
        Assert.Empty(countOutput);
        Assert.Contains("Section 'Type Info' is scalar and does not support --count", countError);

        var (rowsExit, _, rowsError) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", SectionNames.ApiInfo, "--rows", "1");

        Assert.Equal(1, rowsExit);
        Assert.Contains("Section 'API Info' is scalar and does not support --rows", rowsError);

        var (recordExit, record, recordError) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
            "--library", TestAssemblyPath, "--all", "-S", SectionNames.TypeInfo);

        Assert.Equal(0, recordExit);
        Assert.Empty(recordError);
        Assert.StartsWith("field\tvalue", record);

        // A count map over several sections keeps its per-section meaning.
        var (mapExit, map, mapError) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
            "--library", TestAssemblyPath, "--all",
            "-S", $"{SectionNames.TypeInfo},{SectionNames.Constructors}", "--count", "--json");

        Assert.Equal(0, mapExit);
        Assert.Empty(mapError);
        Assert.Contains("\"section\"", map);
    }
}
