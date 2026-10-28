using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class UnsafeMembersSectionTests
{
    [Fact]
    public async Task
        DirectLibraryExactTypeDiscovery_UsesUnsafeApplicabilityProbe()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(
                new TypeOptions
                {
                    TypeName = typeof(SampleUnsafeClass).FullName,
                    AssemblyPath =
                        typeof(SampleUnsafeClass).Assembly.Location,
                    Discover = [SectionNames.UnsafeMembers],
                    CompanionOutput = CompanionOutput.None,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Evidence", result.Output);
    }

    // One row per census finding at Library scope: the motivating asset's
    // lambda dereference folds into CaptureLocal, never a closure row.
    [Fact]
    public async Task LibraryUnsafeMembers_RendersOneRowPerCensusFinding()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            IncludeSections = [ "Unsafe Members" ],
            Markdown = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("| Member | Exposure | Contract | Evidence | Partial |", result.Output);
        Assert.Contains(
            "| `ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples.CaptureLocal(nint, int)` | Non-public |  | pointer dereference |  |",
            result.Output);
        Assert.Contains(
            "| `ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples.Bytes(byte*, int)` | Non-public | explicit | explicit contract |  |",
            result.Output);
        Assert.DoesNotContain("DisplayClass", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(">b__", result.Output, StringComparison.Ordinal);
    }

    // A member whose only evidence is a legacy structural form, a pointer
    // return and a pinned local, has no row; a dereference and an
    // explicit-contract call each make one.
    [Fact]
    public async Task TypeUnsafeMembers_AdmitsOnlyCensusRoles()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "| `CallsUnsafeAs(ref int)` | Public |  | explicit-contract call: `System.Runtime.CompilerServices.Unsafe::As` |  |",
                "| `UnsafePointerMethod(int*)` | Public |  | pointer dereference |  |",
            ],
            UnsafeMemberRows(result.Output));
    }

    // Issue #9755: the capturing lambda, static lambda, local function, and
    // async state machine fold into their declared member at Type scope, the
    // same rows Library scope renders for those types.
    [Theory]
    [InlineData("TypeScopeCapturingLambda", "Read(int)")]
    [InlineData("TypeScopeStaticLambda", "Read(int)")]
    [InlineData("TypeScopeLocalFunction", "Read(int)")]
    [InlineData("TypeScopeAsyncMethod", "ReadAsync(int)")]
    [InlineData("TypeScopeDirect", "Read(int)")]
    public async Task TypeUnsafeMembers_FoldGeneratedBodiesIntoTheirDeclaredMember(
        string type,
        string member)
    {
        const string Namespace = "ILInspector.Analysis.IteratorFixtures";
        const string Row = " | Public |  | explicit-contract call: `System.Runtime.CompilerServices.Unsafe::As` |  |";
        string path = FixtureCatalog.AnalysisIterators.AssemblyPath();

        var typeResult = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = $"{Namespace}.{type}",
            AssemblyPath = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));
        var libraryResult = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            Markdown = true,
        }));

        Assert.Equal(0, typeResult.ExitCode);
        string[] typeRows = UnsafeMemberRows(typeResult.Output);
        Assert.Equal([$"| `{member}`{Row}"], typeRows);
        Assert.Contains($"| `{Namespace}.{type}.{member}`{Row}", UnsafeMemberRows(libraryResult.Output));
        Assert.DoesNotContain(">b__", typeResult.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("g__Local", typeResult.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveNext", typeResult.Output, StringComparison.Ordinal);
    }

    // An incomplete census renders its observed rows, discloses the
    // limitation on stderr, counts the observed rows, and exits 1.
    [Fact]
    public async Task LibraryUnsafeMembers_IncompleteCensusRendersObservedRowsAndFails()
    {
        string path = FixtureCatalog.AnalysisIterators.AssemblyPath();

        var rows = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            Markdown = true,
        }));
        var count = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            Count = true,
        }));

        Assert.Equal(1, rows.ExitCode);
        Assert.Contains(
            "Unsafe Members inspection incomplete: the census has 1 limitation;",
            rows.Error);
        int observed = UnsafeMemberRows(rows.Output).Length;
        Assert.Equal(7, observed);
        Assert.Equal(1, count.ExitCode);
        Assert.Equal(observed.ToString(System.Globalization.CultureInfo.InvariantCulture), count.Output.Trim());
        Assert.Contains("Unsafe Members inspection incomplete", count.Error);
    }

    // A type whose only unsafe body no source member claims has a census
    // with no findings and one limitation: an observed zero, never the
    // empty state, and the command exits 1.
    [Fact]
    public async Task TypeUnsafeMembers_IncompleteCensusWithNoRowsIsAnObservedZero()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = "ILInspector.Analysis.IteratorFixtures.UnclaimedGeneratedBodySample",
            AssemblyPath = FixtureCatalog.AnalysisIterators.AssemblyPath(),
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "Unsafe Members inspection incomplete: no findings were observed, but the census has 1 limitation;",
            result.Error);
        Assert.DoesNotContain("No unsafe members", result.Output);
        Assert.Empty(UnsafeMemberRows(result.Output));
    }

    // A complete census with no findings is the explicit empty state.
    [Fact]
    public async Task TypeUnsafeMembers_CompleteCensusWithNoFindingsIsTheEmptyState()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SamplePInvokeClass).FullName,
            AssemblyPath = typeof(SamplePInvokeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No unsafe members under the updated memory-safety rules.", result.Output);
    }

    // A complete Library census with no findings is the explicit empty state
    // in table and JSON output, as at Type scope.
    [Fact]
    public async Task LibraryUnsafeMembers_CompleteCensusWithNoFindingsIsTheEmptyState()
    {
        string path = typeof(System.Linq.Expressions.Expression).Assembly.Location;
        var table = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            Markdown = true,
        }));
        var json = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            JsonOutput = true,
        }));

        Assert.Equal(0, table.ExitCode);
        Assert.Empty(table.Error);
        Assert.Contains("No unsafe members under the updated memory-safety rules.", table.Output);
        Assert.Equal(0, json.ExitCode);
        Assert.Contains("\"unsafe_members\": []", json.Output);
        Assert.Contains("\"unsafe_members_complete\": true", json.Output);
    }

    // An unsafe local function's own contract is not conferred on its
    // declaring member, so without a call the member has no finding and
    // discovery agrees with the empty rows.
    [Theory]
    [InlineData("UnsafeLocalFunctionNeverCalled")]
    [InlineData("UnsafeLocalFunctionAsDelegate")]
    public async Task TypeUnsafeMembers_UncalledUnsafeLocalFunctionIsNeitherRowNorDiscovered(string type)
    {
        string typeName = $"ILInspector.Decompiler.Fixtures.NewUnsafe.{type}";
        string path = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        var discovery = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeName,
            AssemblyPath = path,
            Discover = [SectionCategoryNames.Audit],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));
        var rows = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeName,
            AssemblyPath = path,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, discovery.ExitCode);
        Assert.DoesNotContain("Unsafe Members\tsection", discovery.Output);
        Assert.Equal(0, rows.ExitCode);
        Assert.Contains("No unsafe members under the updated memory-safety rules.", rows.Output);
    }

    static string[] UnsafeMemberRows(string output) =>
    [
        .. output
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("## Unsafe Members", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal)),
    ];

    [Fact]
    public async Task MemberUnsafeOperations_ShowsSelectedMemberEvidence()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod)],
            IncludeSections = [SectionNames.UnsafeOperations],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Operations", result.Output);
        Assert.Contains("| Unsafe signature | `int*` | signature |", result.Output);
        Assert.Contains("| Unsafe operation | `ldind.i4` | opcode |", result.Output);
    }

    [Fact]
    public async Task MemberAuditCategory_SelectsUnsafeEvidence()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod)],
            OverloadIndex = 1,
            Select = [SectionCategoryNames.Audit],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Unsafe Operations", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeOperations_DiscoverListsColumns()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.CallsUnsafeAs)],
            IncludeSections = [SectionNames.UnsafeOperations],
            Discover = ["Unsafe Operations"],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reason\tcolumn", result.Output);
        Assert.Contains("Detail\tcolumn", result.Output);
        Assert.Contains("IL\tcolumn", result.Output);
        Assert.Contains("Token\tcolumn", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeOperations_ListsUnsafeApiMembersInSummaryWithoutBodies()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "System.Runtime.CompilerServices.Unsafe",
            PlatformAssembly = "System.Runtime",
            MemberFilter = ["Add"],
            OverloadIndex = 1,
            IncludeSections = [SectionNames.UnsafeOperations],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Member: System.Runtime.CompilerServices.Unsafe.Add(ref T, int)", result.Output);
        Assert.DoesNotContain("## Unsafe Operations", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeWildcard_RendersApiMemberInSummaryAndOperationsInTable()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "System.Runtime.CompilerServices.Unsafe",
            PlatformAssembly = "System.Runtime",
            MemberFilter = ["BitCast"],
            OverloadIndex = 1,
            IncludeSections = [SectionNames.UnsafeOperations],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Member: System.Runtime.CompilerServices.Unsafe.BitCast(TFrom)", result.Output);
        Assert.Contains("## Unsafe Operations", result.Output);
        Assert.Contains("| Unsafe call |", result.Output);
        Assert.DoesNotContain("| Unsafe API member |", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MemberAuditEffectiveDiscovery_ListsUnsafeOperationsForUnsafeApiMember()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "System.Runtime.CompilerServices.Unsafe",
            PlatformAssembly = "System.Runtime",
            MemberFilter = ["Add"],
            OverloadIndex = 1,
            Discover = [SectionCategoryNames.Audit],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Unsafe Operations\tsection", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_FiltersToSelectedType()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("| `UnsafePointerMethod(int*)` | Public |  | pointer dereference |  |", result.Output);
        Assert.Contains("`CallsUnsafeAs(ref int)`", result.Output);
        Assert.DoesNotContain("UnsafeReturnPointer", result.Output);
        Assert.DoesNotContain("SamplePInvokeClass", result.Output);
    }

    [Fact]
    public async Task TypeAuditEffectiveDiscovery_ListsUnsafeMembers()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            Discover = [SectionCategoryNames.Audit],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Unsafe Members\tsection", result.Output);
    }

    [Fact]
    public async Task
        TypeAuditEffectiveDiscovery_ListsGeneratedUnsafeMembers()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "GeneratedUnsafeEvidenceSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                Discover = [SectionCategoryNames.Audit],
                CompanionOutput = CompanionOutput.None,
                Verbosity = Verbosity.Minimal,
                Tabular = true,
                Tsv = true,
                TabularExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Unsafe Members\tsection", result.Output);
    }

    [Fact]
    public async Task TypeAuditEffectiveDiscovery_OmitsUnsafeMembersForSafeType()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SamplePInvokeClass).FullName,
            AssemblyPath = typeof(SamplePInvokeClass).Assembly.Location,
            Discover = [SectionCategoryNames.Audit],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("Unsafe Members\tsection", result.Output);
    }

    [Fact]
    public async Task MemberTypeUnsafeMembers_FiltersToSelectedType()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("`UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("`CallsUnsafeAs(ref int)`", result.Output);
    }
}
