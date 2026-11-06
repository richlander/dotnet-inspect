using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class UnsafeCodeSectionTests
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
                    Discover = [SectionNames.UnsafeCode],
                    CompanionOutput = CompanionOutput.None,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Evidence", result.Output);
    }

    // One row per declaring Type at Library scope, with no counts and no
    // State column when every Type settled. The motivating asset's lambda
    // dereference folds into CaptureLocal, never a closure row or Type.
    [Fact]
    public async Task LibraryUnsafeCode_RendersOneRowPerType()
    {
        string path = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            Markdown = true,
        }));
        var count = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            Count = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("| Type |\n", result.Output.ReplaceLineEndings("\n"));
        string[] rows = UnsafeCodeRows(result.Output);
        Assert.Contains(
            "| `ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples` |",
            rows);
        Assert.Contains(
            "| `ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples.Cursor` |",
            rows);
        Assert.DoesNotContain("CaptureLocal", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayClass", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(">b__", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, count.ExitCode);
        Assert.Equal(
            rows.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            count.Output.Trim());
    }

    // Finding Unsafe Any lists exactly the declaring Types of the census's
    // findings, and its limitations are the census's limitations in Types
    // with no finding.
    [Theory]
    [InlineData("DecompilerUnsafeNew")]
    [InlineData("AnalysisIterators")]
    public void LibraryUnsafeCode_ListsExactlyTheCensusTypes(string fixture)
    {
        string path = fixture == "DecompilerUnsafeNew"
            ? FixtureCatalog.DecompilerUnsafeNew.AssemblyPath()
            : FixtureCatalog.AnalysisIterators.AssemblyPath();
        using var context = ILInspector.Metadata.PdbContext.OpenMetadataOnly(path);

        var census = Assert.IsType<ILInspector.Analysis.Planning.UnsafeMemberCensusResult.Available>(
            Assert.IsType<UnsafeMemberCensusQueryResult.Inspected>(
                UnsafeMemberCensusQuery.Execute(path, context)).Census).Census;
        var any = Assert.IsType<ILInspector.Analysis.Planning.FindingUnsafeAnyResult.Available>(
            Assert.IsType<FindingUnsafeAnyQueryResult.Inspected>(
                FindingUnsafeAnyQuery.Execute(path, context)).Answer).Types;

        Assert.NotEmpty(census.Members);
        Assert.Equal(
            ILInspector.Analysis.UnsafeMemberTypeGroup.Group(census.Members)
                .Select(group => group.DeclaringType)
                .ToHashSet(),
            any.Types
                .Where(type => type.State == ILInspector.Analysis.UnsafeCodeTypeState.Unsafe)
                .Select(type => type.DeclaringType)
                .ToHashSet());
        Assert.DoesNotContain(
            any.Types,
            type => type.State == ILInspector.Analysis.UnsafeCodeTypeState.Unknown);
        Assert.Equal(census.IsComplete, any.IsComplete);
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
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "| `CallsUnsafeAs(ref int)` | Public |  | legacy rules | explicit-contract call: `System.Runtime.CompilerServices.Unsafe::As` |  |",
                "| `UnsafePointerMethod(int*)` | Public |  | legacy rules | pointer dereference |  |",
            ],
            UnsafeCodeRows(result.Output));
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
        const string Row = " | Public |  | legacy rules | explicit-contract call: `System.Runtime.CompilerServices.Unsafe::As` |  |";
        string path = FixtureCatalog.AnalysisIterators.AssemblyPath();

        var typeResult = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = $"{Namespace}.{type}",
            AssemblyPath = path,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));
        var libraryResult = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            Markdown = true,
        }));

        Assert.Equal(0, typeResult.ExitCode);
        string[] typeRows = UnsafeCodeRows(typeResult.Output);
        Assert.Equal([$"| `{member}`{Row}"], typeRows);
        Assert.Contains($"| `{Namespace}.{type}` |", UnsafeCodeRows(libraryResult.Output));
        Assert.DoesNotContain(">b__", typeResult.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("g__Local", typeResult.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveNext", typeResult.Output, StringComparison.Ordinal);
    }

    // An incomplete answer renders its observed rows, discloses the
    // limitation on stderr, counts the observed rows, and exits 1.
    [Fact]
    public async Task LibraryUnsafeCode_IncompleteAnswerRendersObservedRowsAndFails()
    {
        string path = FixtureCatalog.AnalysisIterators.AssemblyPath();

        var rows = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            Markdown = true,
        }));
        var count = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            Count = true,
        }));

        Assert.Equal(1, rows.ExitCode);
        Assert.Contains(
            "Unsafe Code inspection incomplete: the search has 1 limitation;",
            rows.Error);
        int observed = UnsafeCodeRows(rows.Output).Length;
        Assert.Equal(7, observed);
        Assert.Equal(1, count.ExitCode);
        Assert.Equal(observed.ToString(System.Globalization.CultureInfo.InvariantCulture), count.Output.Trim());
        Assert.Contains("Unsafe Code inspection incomplete", count.Error);
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
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "Unsafe Code inspection incomplete: no findings were observed, but the census has 1 limitation;",
            result.Error);
        Assert.DoesNotContain("No unsafe code", result.Output);
        Assert.Empty(UnsafeCodeRows(result.Output));
    }

    // A complete census with no findings is the explicit empty state.
    [Fact]
    public async Task TypeUnsafeMembers_CompleteCensusWithNoFindingsIsTheEmptyState()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SamplePInvokeClass).FullName,
            AssemblyPath = typeof(SamplePInvokeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No unsafe code under the updated memory-safety rules.", result.Output);
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
            IncludeSections = [SectionNames.UnsafeCode],
            Markdown = true,
        }));
        var json = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = path,
            IncludeSections = [SectionNames.UnsafeCode],
            JsonOutput = true,
        }));

        Assert.Equal(0, table.ExitCode);
        Assert.Empty(table.Error);
        Assert.Contains("No unsafe code under the updated memory-safety rules.", table.Output);
        Assert.Equal(0, json.ExitCode);
        Assert.Contains("\"unsafe_code\": []", json.Output);
        Assert.Contains("\"unsafe_code_complete\": true", json.Output);
        Assert.DoesNotContain("unsafe_members", json.Output, StringComparison.Ordinal);
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
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, discovery.ExitCode);
        Assert.DoesNotContain("Unsafe Code\tsection", discovery.Output);
        Assert.Equal(0, rows.ExitCode);
        Assert.Contains("No unsafe code under the updated memory-safety rules.", rows.Output);
    }

    static string[] UnsafeCodeRows(string output) =>
    [
        .. output
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("## Unsafe Code", StringComparison.Ordinal))
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
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Code", result.Output);
        Assert.Contains("| `UnsafePointerMethod(int*)` | Public |  | legacy rules | pointer dereference |  |", result.Output);
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
        Assert.Contains("Unsafe Code\tsection", result.Output);
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
        Assert.Contains("Unsafe Code\tsection", result.Output);
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
        Assert.DoesNotContain("Unsafe Code\tsection", result.Output);
    }

    [Fact]
    public async Task MemberTypeUnsafeMembers_FiltersToSelectedType()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Code", result.Output);
        Assert.Contains("`UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("`CallsUnsafeAs(ref int)`", result.Output);
    }

    // One method renders its evidence rows from the exact-method census,
    // each carrying the member's Type-row values.
    [Fact]
    public async Task MemberUnsafeCode_RendersTheMethodsEvidenceRows()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples",
            AssemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            MemberFilter = ["CaptureLocal"],
            IncludeAll = true,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "| Exposure | Contract | Safe Boundary | Role | Body | IL | Detail | Partial |",
            result.Output);
        string row = Assert.Single(EvidenceRows(result.Output));
        Assert.StartsWith("| Non-public |  | safe boundary | pointer dereference | `", row, StringComparison.Ordinal);
        Assert.Contains(">b__", row, StringComparison.Ordinal);
    }

    // A member group renders its overloads' Type rows, from a census over
    // only the group's methods.
    [Fact]
    public async Task MemberUnsafeCode_GroupRendersItsOverloadsTypeRows()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod), nameof(SampleUnsafeClass.UnsafeReturnPointer)],
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            ["| `UnsafePointerMethod(int*)` | Public |  | legacy rules | pointer dereference |  |"],
            UnsafeCodeRows(result.Output));
    }

    // A method whose only evidence is a legacy structural form has nothing
    // attributed to it: the Member empty state, not a claim of safety.
    [Fact]
    public async Task MemberUnsafeCode_MethodWithNoAttributedEvidenceIsTheMemberEmptyState()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafeReturnPointer)],
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No unsafe evidence was attributed to this member.", result.Output);
    }

    const string Cursor = "ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples.Cursor";

    // A Type whose API members are only properties offers the section, and
    // its rows are the accessors' findings.
    [Fact]
    public async Task TypeUnsafeCode_PropertyOnlyTypeRendersItsAccessorRows()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = Cursor,
            AssemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            IncludeAll = true,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, UnsafeCodeRows(result.Output).Length);
        Assert.Contains(
            "| `get_Item(int)` | Non-public | explicit | not a safe boundary | explicit contract |  |",
            UnsafeCodeRows(result.Output));
    }

    // A property selection covers its accessors, one row each.
    [Fact]
    public async Task MemberUnsafeCode_PropertySelectionCoversItsAccessors()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = Cursor,
            AssemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            MemberFilter = ["Item"],
            IncludeAll = true,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "| `get_Item(int)` | Non-public | explicit | not a safe boundary | explicit contract |  |",
                "| `set_Item(int, ulong*)` | Non-public | explicit | not a safe boundary | explicit contract |  |",
            ],
            UnsafeCodeRows(result.Output));
    }

    // A selection that names no method definition fails visibly rather than
    // rendering an empty state that reads as an answer.
    [Fact]
    public async Task MemberUnsafeCode_SelectionWithNoMethodDefinitionFails()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = Cursor,
            AssemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            MemberFilter = ["get_Item"],
            IncludeAll = true,
            IncludeSections = [SectionNames.UnsafeCode],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("the selection names no method", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("No unsafe", result.Output, StringComparison.Ordinal);
    }

    static string[] EvidenceRows(string output) =>
    [
        .. output
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("## Unsafe Code", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("| Non-public", StringComparison.Ordinal)
                || line.StartsWith("| Public", StringComparison.Ordinal)),
    ];
}
