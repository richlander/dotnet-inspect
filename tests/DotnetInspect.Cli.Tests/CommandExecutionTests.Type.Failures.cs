using DotnetInspect.Cli.Sections;
using System.Globalization;
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
    public async Task ConstraintResolutionFailure_IsVisibleAndNonfatalAcrossTypeCommands()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"constraint-diagnostic-{Guid.NewGuid():N}.dll");
        WriteModuleConstraintAssembly(path);
        try
        {
            var listing = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    new TypeOptions
                    {
                        AssemblyPath = path,
                        Verbosity = Verbosity.Normal,
                    }));
            var selectedType = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    new TypeOptions
                    {
                        AssemblyPath = path,
                        TypeName = "N.Holder<T>",
                        Verbosity = Verbosity.Normal,
                    }));
            var selectedMember = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    new MemberOptions
                    {
                        AssemblyPath = path,
                        TypeName = "N.Holder<T>",
                        Verbosity = Verbosity.Normal,
                    }));

            Assert.Equal(0, listing.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                listing.Error);
            Assert.DoesNotContain(
                "rejected",
                listing.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, selectedType.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                selectedType.Error);
            Assert.Equal(0, selectedMember.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                selectedMember.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectedMetadataRow_IsVisibleAndFatalAcrossSelectedTypeCommands()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"selected-row-failure-{Guid.NewGuid():N}.dll");
        WritePartiallyMalformedTypeNameAssembly(path);
        try
        {
            string[][] typeOutputOptions =
            [
                [],
                ["--format=json"],
                ["--format=table"],
                ["-S", "Type Info", "--count"],
            ];
            string[][] memberOutputOptions =
            [
                [],
                ["--format=json"],
                ["--format=table"],
                ["-S", "Member Index", "--count"],
            ];
            for (int i = 0; i < typeOutputOptions.Length; i++)
            {
                var selectedType = await RunAppAsync(
                    [
                        "type",
                        "N.Good",
                        "--library",
                        path,
                        "--tips",
                        "q",
                        .. typeOutputOptions[i],
                    ]);
                var selectedMember = await RunAppAsync(
                    [
                        "member",
                        "N.Good",
                        "--library",
                        path,
                        "--tips",
                        "q",
                        .. memberOutputOptions[i],
                    ]);

                Assert.Equal(1, selectedType.Exit);
                Assert.Contains(
                    "rejected 1 metadata row",
                    selectedType.Error,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, selectedMember.Exit);
                Assert.Contains(
                    "rejected 1 metadata row",
                    selectedMember.Error,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Type_WildcardFilter_PreservesRejectedMetadataRowDiagnostics()
    {
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "pr3904-r4-repro");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"wildcard-row-failure-{Guid.NewGuid():N}.dll");
        WritePartiallyMalformedTypeNameAssembly(path);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-t",
                "N.*",
                "--tips",
                "q");

            Assert.True(
                result.Exit == 1,
                $"Exit={result.Exit}; output={result.Output}; error={result.Error}");
            Assert.Contains(
                "N.Good",
                result.Output,
                StringComparison.Ordinal);
            Assert.True(
                result.Error.Contains(
                    "rejected 1 metadata row",
                    StringComparison.OrdinalIgnoreCase)
                || result.Output.Contains(
                    "## Inspection Failures",
                    StringComparison.Ordinal),
                $"Exit={result.Exit}; output={result.Output}; error={result.Error}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedRootAdjacency_KeepsHealthySelectedTypeAndIsFatal(
        bool malformedAssemblyReference)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference);
        try
        {
            var selectedType = await RunAppAsync(
                "type",
                "N.Healthy",
                "--library",
                path,
                "--tips",
                "q");
            var selectedMember = await RunAppAsync(
                "member",
                "N.Healthy",
                "--library",
                path,
                "--tips",
                "q");

            Assert.Equal(1, selectedType.Exit);
            Assert.Contains("N.Healthy", selectedType.Output);
            Assert.Contains(
                "rejected 1 metadata row",
                selectedType.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, selectedMember.Exit);
            Assert.Contains("N.Healthy", selectedMember.Output);
            Assert.Contains(
                "rejected 1 metadata row",
                selectedMember.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("m")]
    [InlineData("d")]
    public async Task TypeListing_RendersInspectionFailuresAtRaisedVerbosity(
        string verbosity)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-list-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                $"-v:{verbosity}",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "inventory assembly adjacency",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TypeListing_NormalOmitsInspectionFailuresButKeepsWarning()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-list-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-v:n",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.DoesNotContain(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "rejected 1 metadata row",
                result.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TypeListing_InspectionFailuresSectionIsSelectable()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-section-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "## Classes",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--format=table")]
    [InlineData("--format=jsonl")]
    public async Task
        TypeListing_TabularInspectionFailuresSelectionRendersFailures(
            string format)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-tabular-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                format,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "inventory assembly adjacency",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"kind\":\"class\"",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task
        TypeListing_TabularConstraintFailuresDoNotDuplicateDiagnostics()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"constraint-tabular-{Guid.NewGuid():N}.dll");
        WriteModuleConstraintAssembly(path);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                "--format=jsonl",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "\"operation\":\"resolve generic parameter constraints\"",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"kind\":\"class\"",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedApiCommand_ReportsIncompleteInspectionNonfatally(
        bool memberCommand)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"missing-constraint-{Guid.NewGuid():N}.dll");
        try
        {
            WriteMissingConstraintAssembly(path);

            (int exit, string output, string error) result =
                memberCommand
                    ? await ConsoleCapture.RunAsync(
                        () => MemberCommand.ExecuteAsync(
                            new MemberOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Consumer`1",
                                MemberFilter = ["Value"],
                            }))
                    : await ConsoleCapture.RunAsync(
                        () => TypeCommand.ExecuteAsync(
                            new TypeOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Consumer`1",
                            }));

            Assert.Equal(0, result.exit);
            Assert.NotEmpty(result.output);
            Assert.Contains(
                "Generic-constraint classification was incomplete",
                result.error,
                StringComparison.Ordinal);

            (int exit, string output, string error) healthy =
                memberCommand
                    ? await ConsoleCapture.RunAsync(
                        () => MemberCommand.ExecuteAsync(
                            new MemberOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Healthy",
                                MemberFilter = ["HealthyValue"],
                            }))
                    : await ConsoleCapture.RunAsync(
                        () => TypeCommand.ExecuteAsync(
                            new TypeOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Healthy",
                            }));

            Assert.Equal(0, healthy.exit);
            Assert.NotEmpty(healthy.output);
            Assert.DoesNotContain(
                "Generic-constraint classification was incomplete",
                healthy.error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectedApiInspection_GlobMemberFilterMatchesFailure()
    {
        const int MethodToken = 0x06000001;
        const string Path = "/tmp/member-filter.dll";
        var api = new ApiSurface();
        var type = new ApiType
        {
            Name = "Consumer",
            SourceAssemblyPath = Path,
            Members =
            [
                new ApiMember
                {
                    Name = "GetValue",
                    MetadataToken = MethodToken,
                },
            ],
        };
        var failure = new ApiSurfaceInspectionFailure(
            "resolve generic parameter constraints",
            MethodToken,
            MetadataTypeNameFailureMechanism.Metadata,
            "MalformedMetadata",
            "Dependency unavailable.",
            DependencyAssembly:
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null))
        {
            SourceAssemblyPath = Path,
        };
        api.ConstraintResolutionFailuresBySubject[
            new ApiSurfaceInspectionSubject(Path, MethodToken)] =
            [failure];

        bool incomplete = false;
        var (_, error) = await ConsoleCapture.RunAsync(
            () => incomplete =
                ApiCommand.WarnSelectedApiInspectionIncomplete(
                    api,
                    type,
                    new HashSet<string>(
                        ["Get*"],
                        StringComparer.OrdinalIgnoreCase)));

        Assert.True(incomplete);
        Assert.Contains(
            "Generic-constraint classification was incomplete",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "via 'Dependency, Version=1.0.0.0, "
                + "Culture=neutral, PublicKeyToken=null'",
            error,
            StringComparison.Ordinal);
    }
}
