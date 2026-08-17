using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class UnsafeMembersSectionTests
{
    [Fact]
    public async Task LibraryUnsafeMembers_IncludesSignaturesCallsAndOpcodes()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [ "Unsafe Members" ],
            Markdown = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("`DotnetInspector.Tests.SampleUnsafeClass.UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("| Unsafe signature |", result.Output);
        Assert.Contains("| Unsafe operation | `ldind.i4` | opcode |", result.Output);
        Assert.Contains("`DotnetInspector.Tests.SampleUnsafeClass.CallsUnsafeAs(ref int)`", result.Output);
        Assert.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", result.Output);
        Assert.DoesNotContain("SamplePInvokeClass.GetCurrentProcessId", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeOperations_ShowsSelectedMemberEvidence()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod)],
            IncludeSections = [SectionNames.UnsafeOperations],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Operations", result.Output);
        Assert.Contains("| Unsafe signature | `int*` | signature |", result.Output);
        Assert.Contains("| Unsafe operation | `ldind.i4` | opcode |", result.Output);
    }

    [Fact]
    public async Task MemberAuditCategory_IsNotAvailableForSelectedMember()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod)],
            OverloadIndex = 1,
            Select = [SectionCategoryNames.Audit],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("@Audit", result.Error);
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
            TipLevel = TipLevel.Quiet,
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
            TipLevel = TipLevel.Quiet,
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
            TipLevel = TipLevel.Quiet,
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
    public async Task MemberEffectiveDiscovery_ListsUnsafeOperationsForUnsafeApiMember()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "System.Runtime.CompilerServices.Unsafe",
            PlatformAssembly = "System.Runtime",
            MemberFilter = ["Add"],
            OverloadIndex = 1,
            Discover = [],
            TipLevel = TipLevel.Quiet,
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
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("`UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("`CallsUnsafeAs(ref int)`", result.Output);
        Assert.DoesNotContain("SamplePInvokeClass", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_AccessorEvidenceSuppressesPropertyFallback()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("get_UnsafePointerProperty()", result.Output);
        Assert.DoesNotContain("| `UnsafePointerProperty` | Unsafe declaration |", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_SafeTypeRendersEmptyState()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleClassForTesting).FullName,
            AssemblyPath = typeof(SampleClassForTesting).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("No unsafe members found on this type.", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_JsonFailsClosedInsteadOfReturningAnUnrelatedTypeProjection()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            JsonOutput = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot represent the analysis rows", result.Error);
        Assert.Contains("--jsonl", result.Error);
    }

    [Fact]
    public async Task TypeUnsafeMembers_AuditCategoryJsonFailsClosed()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            Select = [SectionCategoryNames.Audit],
            IncludeSections = [SectionNames.UnsafeMembers],
            JsonOutput = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot represent the analysis rows", result.Error);
        Assert.Contains("--jsonl", result.Error);
        Assert.DoesNotContain("\"name\"", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_InapplicableJsonSelectionStillFailsClosed()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleClassForTesting).FullName,
            AssemblyPath = typeof(SampleClassForTesting).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            JsonOutput = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot represent the analysis rows", result.Error);
        Assert.Contains("--jsonl", result.Error);
        Assert.DoesNotContain("\"name\"", result.Output);
    }

    [Fact]
    public async Task TypeUnsafeMembers_MultiSectionJsonSelectionRecommendsDocumentOutput()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleClassForTesting).FullName,
            AssemblyPath = typeof(SampleClassForTesting).Assembly.Location,
            IncludeSections = [SectionNames.TypeInfo, SectionNames.UnsafeMembers],
            JsonOutput = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot represent the analysis rows", result.Error);
        Assert.Contains("Markdown", result.Error);
        Assert.DoesNotContain("--jsonl", result.Error);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("@All")]
    public async Task TypeUnsafeMembers_AllSelectorJsonKeepsDocumentProjection(string selector)
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleClassForTesting).FullName,
            AssemblyPath = typeof(SampleClassForTesting).Assembly.Location,
            Select = [selector],
            IncludeSections =
            [
                SectionNames.TypeInfo,
                SectionNames.UnsafeMembers
            ],
            JsonOutput = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"name\"", result.Output);
        Assert.DoesNotContain("cannot represent the analysis rows", result.Error);
    }

    [Fact]
    public void TypeUnsafeMembers_RendersDeclarationWhenAnalysisHasNoEvidence()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "OnlyUnsafe",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Risky",
                    Kind = "method",
                    Signature = "int Risky()",
                    MetadataToken = 0x0600FFFF,
                    IsUnsafe = true
                }
            ]
        };
        var index = LibraryBodyIndex.Open(typeof(SampleUnsafeClass).Assembly.Location);
        var view = new TypeView();

        ApiOutputFormatter.PopulateUnsafeMembers(view, type, index);

        var row = Assert.Single(view.UnsafeMemberRows!);
        Assert.Equal("<code>Risky()</code>", row.Member);
        Assert.Equal("Unsafe declaration", row.Reason);
        Assert.Equal("<code>int Risky()</code>", row.Detail);
        Assert.Equal("metadata", row.Kind);
    }

    [Fact]
    public void TypeUnsafeMembers_RendersFailuresFromNonSurfaceDeclaredMethods()
    {
        var index = LibraryBodyIndex.Open(typeof(SampleUnsafeClass).Assembly.Location);
        var otherTypeMethod = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == nameof(SamplePInvokeClass.GetCurrentProcessId));
        var type = new ApiType
        {
            Namespace = typeof(SampleUnsafeClass).Namespace,
            Name = nameof(SampleUnsafeClass),
            Kind = "class",
            MetadataToken = 0x0200FFFE,
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateUnsafeMembers(
            view,
            type,
            index,
            [
                new AnalysisDiagnostic(
                    0x0600FFFE,
                    "MalformedIdentity",
                    "BadImageFormatException: malformed signature",
                    type.MetadataToken),
                new AnalysisDiagnostic(
                    otherTypeMethod.MetadataToken,
                    otherTypeMethod.Name,
                    "BadImageFormatException: unrelated body",
                    0x0200FFFF)
            ]);

        var row = Assert.Single(
            view.UnsafeMemberRows!,
            candidate => candidate.Kind == "diagnostic");
        Assert.Equal("Analysis failed", row.Reason);
        Assert.Contains("malformed signature", row.Detail);
        Assert.DoesNotContain(
            view.UnsafeMemberRows!,
            candidate => candidate.Detail.Contains("unrelated body", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeUnsafeMembers_NullTypeTokensUseDeclaredMethodFallback()
    {
        var index = LibraryBodyIndex.Open(typeof(SampleUnsafeClass).Assembly.Location);
        var matchingMethod = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == nameof(SampleUnsafeClass.UnsafePointerMethod));
        var unrelatedMethod = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == nameof(SamplePInvokeClass.GetCurrentProcessId));
        var type = new ApiType
        {
            Namespace = typeof(SampleUnsafeClass).Namespace,
            Name = nameof(SampleUnsafeClass),
            Kind = "class",
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateUnsafeMembers(
            view,
            type,
            index,
            [
                new AnalysisDiagnostic(
                    matchingMethod.MetadataToken,
                    matchingMethod.Name,
                    "BadImageFormatException: matching legacy diagnostic"),
                new AnalysisDiagnostic(
                    unrelatedMethod.MetadataToken,
                    unrelatedMethod.Name,
                    "BadImageFormatException: unrelated legacy diagnostic"),
            ]);

        var row = Assert.Single(
            view.UnsafeMemberRows!,
            candidate => candidate.Kind == "diagnostic");
        Assert.Contains("matching legacy diagnostic", row.Detail);
        Assert.DoesNotContain(
            view.UnsafeMemberRows!,
            candidate => candidate.Detail.Contains(
                "unrelated legacy diagnostic",
                StringComparison.Ordinal));
    }

    [Fact]
    public void TypeUnsafeMembers_FieldFallbackPreservesPointerType()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "UnsafeFields",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Callback",
                    Kind = "field",
                    ReturnType = "delegate*<int, int>",
                    IsUnsafe = true
                }
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateUnsafeMembers(
            view,
            type,
            LibraryBodyIndex.Open(typeof(SampleUnsafeClass).Assembly.Location));

        Assert.Equal(
            "<code>delegate*&lt;int, int&gt; Callback</code>",
            Assert.Single(view.UnsafeMemberRows!).Detail);
    }

    [Fact]
    public async Task TypeEffectiveDiscovery_ListsUnsafeMembers()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            Discover = [],
            TipLevel = TipLevel.Quiet,
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
    public async Task MemberTypeUnsafeMembers_FiltersToSelectedType()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [SectionNames.UnsafeMembers],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("`UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("`CallsUnsafeAs(ref int)`", result.Output);
    }
}
