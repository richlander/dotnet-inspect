using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class TimelineCommandTests
{
    [Fact]
    public async Task Count_AppliesRowsAndValidatesProjectedColumns()
    {
        var view = new TimelineDocumentView
        {
            Title = "Timeline",
            Evaluations =
            [
                new("Sample@1.0.0", "1.0.0", "Present", 1, null),
                new("Sample@1.0.1", "1.0.1", "Present", 1, null),
                new("Sample@1.0.2", "1.0.2", "Present", 1, null)
            ]
        };
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Evaluations"
        };

        var count = await ConsoleCapture.RunAsync(() => Task.FromResult(
            TimelineCommand.Write(
                view,
                new TimelineOptions
                {
                    Count = true,
                    Columns = ["Version"],
                    Rows = RowWindow.Head(1)
                },
                sections)));
        var invalid = await ConsoleCapture.RunAsync(() => Task.FromResult(
            TimelineCommand.Write(
                view,
                new TimelineOptions
                {
                    Count = true,
                    Columns = ["NoSuchColumn"]
                },
                sections)));

        Assert.Equal(0, count.ExitCode);
        Assert.Equal("1", count.Output.Trim());
        Assert.Empty(count.Error);

        Assert.Equal(1, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchColumn", invalid.Error);
    }

    [Fact]
    public async Task Count_MultipleSectionsWritesAnOrderedCountMap()
    {
        var view = new TimelineDocumentView
        {
            Title = "Timeline",
            Evaluations =
            [
                new("Sample@1.0.0", "1.0.0", "Present", 1, null),
                new("Sample@1.0.1", "1.0.1", "Present", 1, null)
            ],
            Transitions =
            [
                new("1.0.0", "1.0.1", "1.0.0..1.0.1", "Added", "api.member", "Run", null),
                new("1.0.1", "1.0.2", "1.0.1..1.0.2", "Changed", "api.member", "Run", null)
            ]
        };
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Evaluations",
            "Transitions"
        };

        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            TimelineCommand.Write(
                view,
                new TimelineOptions
                {
                    Count = true,
                    JsonOutput = true,
                    Rows = RowWindow.Head(1)
                },
                sections)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Collection(
            document.RootElement.EnumerateArray(),
            row =>
            {
                Assert.Equal("Evaluations", row.GetProperty("section").GetString());
                Assert.Equal(1, row.GetProperty("count").GetInt32());
            },
            row =>
            {
                Assert.Equal("Transitions", row.GetProperty("section").GetString());
                Assert.Equal(1, row.GetProperty("count").GetInt32());
            });
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_UnadoptedTypedDocumentFailsClosed()
    {
        var view = new TimelineDocumentView
        {
            Title = "Timeline",
            Evaluations =
            [
                new("Sample@1.0.0", "1.0.0", "Present", 1, null)
            ]
        };

        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            TimelineCommand.Write(
                view,
                new TimelineOptions
                {
                    JsonOutput = true,
                    Columns = ["Version"],
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Evaluations"
                })));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("requires lowered JSON", result.Error);
    }

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
        Assert.Contains(
            "dotnet-inspect timeline --package 'Sample@1.0.0..1.0.2'",
            view.Recommendation,
            StringComparison.Ordinal);
        Assert.Contains(
            "--type 'Sample.Widget' --finding 'api.member' --at '#2'",
            view.Recommendation,
            StringComparison.Ordinal);
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
    public void MemberSelector_CorrelatesOneExactIdentity()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()"),
            Method("Stop", "void Stop()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Stop", "void Stop()", accessibility: "protected"),
        ]));

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [
                Evaluation(vector, 0, oldSurface),
                Evaluation(vector, 1, newSurface),
            ],
            Sections(),
            memberName: "Run");

        Assert.Equal("Run", view.Member);
        Assert.Collection(
            view.Evaluations!,
            row => Assert.Equal("Present", row.State),
            row => Assert.Equal("Missing", row.State));
        var removed = Assert.Single(view.Transitions!);
        Assert.Equal("Removed", removed.Transition);
        Assert.Contains("Run", removed.Target, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop", removed.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafetyTimeline_UsesMemberScopedAnalysisCensus()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var subject = AnalysisSubject();
        var occurrence = new UnsafetyOccurrence(
            MethodIdentity(),
            4,
            UnsafetyKind.StackAlloc,
            "byte*");

        var view = TimelineCommand.BuildUnsafetyView(
            vector,
            "Sample.Widget",
            "Run",
            [
                new TimelineCommand.TimelineFindingEvaluation<UnsafetyOccurrence>(
                    vector.Addresses[0],
                    new FindingInspection<UnsafetyOccurrence>.Complete([])),
                new TimelineCommand.TimelineFindingEvaluation<UnsafetyOccurrence>(
                    vector.Addresses[1],
                    new FindingInspection<UnsafetyOccurrence>.Complete(
                        AnalysisFindings.InspectUnsafety([occurrence], subject))),
            ],
            Sections());

        Assert.Equal("Run", view.Member);
        Assert.Collection(
            view.Evaluations!,
            row => Assert.Equal(0, row.Findings),
            row => Assert.Equal(1, row.Findings));
        var added = Assert.Single(view.Transitions!);
        Assert.Equal("Added", added.Transition);
        Assert.Equal("analysis.unsafety", added.Finding);
        Assert.Contains("StackAlloc byte*", added.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void AllocationTimeline_FormatsAnalysisTarget()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var subject = AnalysisSubject();
        var occurrence = new AllocationOccurrence(
            MethodIdentity(),
            ILOffset: 4,
            OperandToken: 0x0A000001,
            AllocationKind.Object,
            TypeRef.CoreLib("System", "Object"),
            Detail: null,
            CountsAsHeapAllocation: true,
            AllocationFrequency.Always,
            InLoop: false,
            AllocationEscape.Unknown,
            AllocationFactSource.Newobj);

        var view = TimelineCommand.BuildAllocationView(
            vector,
            "Sample.Widget",
            "Run",
            [
                new TimelineCommand.TimelineFindingEvaluation<AllocationOccurrence>(
                    vector.Addresses[0],
                    new FindingInspection<AllocationOccurrence>.Complete([])),
                new TimelineCommand.TimelineFindingEvaluation<AllocationOccurrence>(
                    vector.Addresses[1],
                    new FindingInspection<AllocationOccurrence>.Complete(
                        AnalysisFindings.InspectAllocations([occurrence], subject))),
            ],
            Sections());

        Assert.Equal(
            "Sample.Widget.Run :: Newobj/Object object",
            Assert.Single(view.Transitions!).Target);
    }

    [Fact]
    public void CallSiteTimeline_FormatsAnalysisTarget()
    {
        var vector = Vector("1.0.0", "1.0.1");
        var subject = AnalysisSubject();
        var call = new DirectCall(
            MethodIdentity(),
            new MemberRef(
                TypeRef.CoreLib("System", "Math"),
                "Abs",
                [TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Int32"),
                MemberKind.Method),
            ILOffset: 4,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            CallKind.Call);

        var view = TimelineCommand.BuildCallSiteView(
            vector,
            "Sample.Widget",
            "Run",
            [
                new TimelineCommand.TimelineFindingEvaluation<DirectCall>(
                    vector.Addresses[0],
                    new FindingInspection<DirectCall>.Complete([])),
                new TimelineCommand.TimelineFindingEvaluation<DirectCall>(
                    vector.Addresses[1],
                    new FindingInspection<DirectCall>.Complete(
                        AnalysisFindings.InspectCallSites([call], subject))),
            ],
            Sections());

        Assert.Equal(
            "Sample.Widget.Run :: System.Math.Abs(int)",
            Assert.Single(view.Transitions!).Target);
    }

    [Fact]
    public async Task AnalysisTimeline_RequiresMemberBeforeAcquisition()
    {
        var result = await ConsoleCapture.RunAsync(() =>
            TimelineCommand.ExecuteAsync(new TimelineOptions
            {
                PackageVersionRange = "Sample@1.0.0..1.0.1",
                TypeName = "Sample.Widget",
                Finding = "analysis.unsafety",
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "--finding analysis.unsafety requires exactly one --member target",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisTimeline_InspectsOnlyResolvedMethodBody()
    {
        var inspection = TimelineCommand.InspectUnsafetyAssemblies(
            [typeof(TimelineCommandTests).Assembly.Location],
            typeof(TimelineCommandTests).FullName!,
            nameof(ReadPointer));

        var complete = inspection switch
        {
            FindingInspection<UnsafetyOccurrence>.Complete value => value,
            _ => throw new InvalidOperationException("Expected a complete unsafety census."),
        };
        var finding = Assert.Single(complete.Findings);
        Assert.Equal(UnsafetyKind.Deref, finding.Payload.Kind);
    }

    [Fact]
    public void AnalysisTimeline_UnreadableAssemblyFailsWithoutClaimingAbsence()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"timeline-unreadable-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllText(path, "not a managed assembly");

            var inspection = TimelineCommand.InspectUnsafetyAssemblies(
                [path],
                "Sample.Widget",
                "Run");

            var failed = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Failed>(
                inspection.Value);
            Assert.Contains(path, failed.Error.Reason);
            Assert.Contains("could not be inspected", failed.Error.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnalysisTimeline_UnsupportedMetadataFormatNamesTheMechanism()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"timeline-winmd-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, BuildWindowsMetadataImage());

            var inspection = TimelineCommand.InspectUnsafetyAssemblies(
                [path],
                "Sample.Widget",
                "Run");

            var failed = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Failed>(
                inspection.Value);
            Assert.Contains(path, failed.Error.Reason);
            Assert.Contains("could not be inspected", failed.Error.Reason);
            Assert.Contains(
                "unsupported metadata format",
                failed.Error.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] BuildWindowsMetadataImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public void AnalysisTimeline_DisposesEndpointAfterCellInspection()
    {
        var vector = Vector("1.0.0");
        string tempDir = Path.Combine(Path.GetTempPath(), $"timeline-endpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var endpoint = new ApiSurfaceEndpoint(
            new AssemblySet(
                assemblies: [],
                diagnostics: [],
                tempDirs: [tempDir]),
            Surface(Type("Widget")));

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "analysis.unsafety",
            [new TimelineCommand.TimelineEvaluation(vector.Addresses[0], endpoint.Surface, null, endpoint)],
            Sections(),
            memberName: "Run");

        Assert.Equal("SubjectAbsent", Assert.Single(view.Evaluations!).State);
        Assert.False(Directory.Exists(tempDir));
    }

    [Fact]
    public void AnalysisTimeline_MissingEndpointFailsWithoutClaimingApplicability()
    {
        var vector = Vector("1.0.0");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "analysis.unsafety",
            [
                new TimelineCommand.TimelineEvaluation(
                    vector.Addresses[0],
                    Surface: null,
                    Error: null,
                    Endpoint: null),
            ],
            Sections(),
            memberName: "Run");

        var evaluation = Assert.Single(view.Evaluations!);
        Assert.Equal("Failed", evaluation.State);
        Assert.Contains("no acquired assembly set", evaluation.Detail);
    }

    [Fact]
    public void AnalysisTimeline_NoApplicableInputHasDistinctPresentation()
    {
        string path = typeof(ISampleInterface).Assembly.Location;
        string typeFullName = typeof(ISampleInterface).FullName!;
        var inspection = TimelineCommand.InspectUnsafetyAssemblies(
            [path],
            typeFullName,
            nameof(ISampleInterface.Execute));
        var absent = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Absent>(
            inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            absent.Kind);

        var vector = Vector("1.0.0");
        var endpoint = new ApiSurfaceEndpoint(
            new AssemblySet(
                assemblies:
                [
                    new AssemblySetEntry(
                        path,
                        path,
                        Version: null,
                        AssemblySetSourceKind.Assembly),
                ],
                diagnostics: [],
                tempDirs: []),
            AssemblyReader.ExtractApiSurface(path)!);
        var view = TimelineCommand.BuildView(
            vector,
            typeFullName,
            "analysis.unsafety",
            [
                new TimelineCommand.TimelineEvaluation(
                    vector.Addresses[0],
                    endpoint.Surface,
                    Error: null,
                    endpoint),
            ],
            Sections(),
            memberName: nameof(ISampleInterface.Execute));

        var evaluation = Assert.Single(view.Evaluations!);
        Assert.Equal("NoApplicableInput", evaluation.State);
        Assert.Contains("no method-body target", evaluation.Detail);
    }

    [Fact]
    public void InspectAnalysisAssemblies_ResolvedNonMethodIsNoApplicableInput()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            Signature = "int Value",
        };

        var inspection = TimelineCommand.InspectAnalysisAssemblies<UnsafetyOccurrence>(
            [("unused.dll", Surface(Type("Widget", members: [property])))],
            "Sample.Widget",
            "Value",
            AnalysisFindings.UnsafetyDescriptor,
            AnalysisSubject(),
            static (_, _, _) =>
                throw new InvalidOperationException(
                    "Non-method subjects have no body inspection."));

        var absent = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Absent>(
            inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            absent.Kind);
    }

    [Fact]
    public void InspectAnalysisAssemblies_CaseDistinctTypeIsSubjectAbsent()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            Signature = "int Value",
        };

        var inspection =
            TimelineCommand.InspectAnalysisAssemblies<UnsafetyOccurrence>(
                [("unused.dll", Surface(Type(
                    "widget",
                    members: [property])))],
                "Sample.Widget",
                "Value",
                AnalysisFindings.UnsafetyDescriptor,
                AnalysisSubject(),
                static (_, _, _) =>
                    throw new InvalidOperationException(
                        "A case-distinct type is not the selected subject."));

        var absent = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Absent>(
            inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            absent.Kind);
    }

    [Fact]
    public void BuildTransitionRows_PreservesNineCompletedTopologyCells()
    {
        FindingInspection<string>[] inspections =
        [
            new FindingInspection<string>.Complete([]),
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.SubjectAbsent),
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput),
        ];

        foreach (FindingInspection<string> oldInspection in inspections)
        {
            foreach (FindingInspection<string> newInspection in inspections)
            {
                var correlation = FindingCensusCorrelation<string>.Create(
                [
                    new(
                        new FindingVersion("v1", "1.0.0", 0),
                        oldInspection),
                    new(
                        new FindingVersion("v2", "2.0.0", 1),
                        newInspection),
                ]);

                var row = Assert.Single(TimelineCommand.BuildTransitionRows(
                    correlation,
                    "test",
                    "Sample.Widget",
                    memberName: null,
                    identityKey: null,
                    static (_, _, oldSide, newSide) =>
                        FindingComparison.Compare(oldSide, newSide)));
                string oldState = InspectionState(oldInspection);
                string newState = InspectionState(newInspection);

                Assert.Equal(
                    oldState == newState
                        ? "None"
                        : $"{oldState}To{newState}",
                    row.Transition);
                if (oldState != newState)
                {
                    Assert.Contains(oldState, row.Detail);
                    Assert.Contains(newState, row.Detail);
                }
            }
        }

        static string InspectionState(FindingInspection<string> inspection)
            => inspection switch
            {
                FindingInspection<string>.Complete => "Complete",
                FindingInspection<string>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.SubjectAbsent,
                } => "SubjectAbsent",
                FindingInspection<string>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.NoApplicableInput,
                } => "NoApplicableInput",
                _ => throw new ArgumentOutOfRangeException(nameof(inspection)),
            };
    }

    [Fact]
    public void AnalysisTimeline_PartialSurfaceFailsWithoutClaimingAbsence()
    {
        var surface = Surface(Type("Other"));
        surface.InspectionFailures.Add(new ApiSurfaceInspectionFailure(
            "type row",
            0x02000002,
            MetadataTypeNameFailureMechanism.Metadata,
            "MalformedMetadata",
            "The type row could not be decoded."));

        var inspection =
            TimelineCommand.InspectAnalysisAssemblies<UnsafetyOccurrence>(
                [("partial.dll", surface)],
                "Sample.Widget",
                "Run",
                AnalysisFindings.UnsafetyDescriptor,
                AnalysisSubject(),
                static (_, _, _) =>
                    throw new InvalidOperationException(
                        "A hidden target must not reach body inspection."));

        var failed = Assert.IsType<FindingInspection<UnsafetyOccurrence>.Failed>(
            inspection.Value);
        Assert.Contains("partial.dll", failed.Error.Reason);
        Assert.Contains("surface is incomplete", failed.Error.Reason);
    }

    [Fact]
    public void AnalysisTimeline_PartialSelectedBodyFailsWithoutPublishingPartialCensus()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"timeline-malformed-body-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, BuildMalformedBodyImage());

            const int MethodToken = 0x06000001;
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: ImmutableHashSet.Create(MethodToken));
            Assert.Single(index.Diagnostics);
            Assert.Single(index.DirectCalls);
            var subject = new FindingSubject(
                "Sample.Broken::Run",
                "Sample.Broken.Run");
            var inspection =
                TimelineCommand.InspectAnalysisAssemblies<DirectCall>(
                [(path, AssemblyReader.ExtractApiSurface(path, includeAll: false))],
                "Sample.Broken",
                "Run",
                AnalysisFindings.CallSiteDescriptor,
                subject,
                static (bodyIndex, token, findingSubject) =>
                {
                    bodyIndex.GetDirectCallsByEvidenceMethod()
                        .TryGetValue(token, out var calls);
                    return new FindingInspection<DirectCall>.Complete(
                        AnalysisFindings.InspectCallSites(
                            calls.IsDefault ? [] : calls,
                            findingSubject));
                });

            var failed =
                Assert.IsType<FindingInspection<DirectCall>.Failed>(
                    inspection.Value);
            Assert.Contains(
                "Method-body analysis failed",
                failed.Error.Reason);
        }
        finally
        {
            File.Delete(path);
        }
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

    [Fact]
    public void EmptyOwnedCensus_PreservesSubjectAvailabilityTransitions()
    {
        var vector = Vector("1.0.0", "1.0.1", "1.0.2");

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [
                Evaluation(vector, 0, Surface()),
                Evaluation(vector, 1, Surface(Type("Widget"))),
                Evaluation(vector, 2, Surface()),
            ],
            Sections());

        Assert.Collection(
            view.Transitions!,
            row =>
            {
                Assert.Equal("SubjectAbsentToComplete", row.Transition);
                Assert.Equal("Adjacent", row.Span);
            },
            row =>
            {
                Assert.Equal("CompleteToSubjectAbsent", row.Transition);
                Assert.Equal("Adjacent", row.Span);
            });
    }

    [Fact]
    public void ProbeOrder_DoesNotChangeTimelineOrder()
    {
        var vector = Vector("1.0.0", "1.0.1", "1.0.2");
        var first = Evaluation(vector, 0, Surface(Type("Widget")));
        var middle = Evaluation(
            vector,
            1,
            Surface(Type("Widget", members: [Method("Run", "void Run()")])));
        var last = Evaluation(
            vector,
            2,
            Surface(Type("Widget", members:
            [
                Method("Run", "void Run()"),
                Method("Stop", "void Stop()"),
            ])));

        var forward = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [first, last, middle],
            Sections());
        var reverse = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            [middle, first, last],
            Sections());

        Assert.Equal(forward.Evaluations, reverse.Evaluations);
        Assert.Equal(forward.Transitions, reverse.Transitions);
        Assert.Equal(forward.Recommendation, reverse.Recommendation);
    }

    [Fact]
    public async Task CellException_BecomesFailureAndLaterCellsStillEvaluate()
    {
        var vector = Vector("1.0.0", "1.0.1", "1.0.2");

        var evaluations = await TimelineCommand.EvaluateCellsAsync(
            vector.Addresses,
            address => address.Position == 1
                ? Task.FromException<(ApiSurface?, string?, DotnetInspector.Inspectors.ApiSurfaceEndpoint?)>(
                    new InvalidOperationException("package exploded"))
                : Task.FromResult<(ApiSurface?, string?, DotnetInspector.Inspectors.ApiSurfaceEndpoint?)>((
                    Surface(Type("Widget")),
                    null,
                    null)));

        Assert.Equal(3, evaluations.Count);
        Assert.Null(evaluations[0].Error);
        Assert.Equal(
            "InvalidOperationException: package exploded",
            evaluations[1].Error);
        Assert.Null(evaluations[2].Error);

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.Widget",
            "api.member",
            evaluations,
            Sections());
        Assert.Equal("Failed", view.Evaluations![1].State);
        Assert.Equal("Complete", view.Evaluations[2].State);
        Assert.Collection(
            view.Transitions!,
            row => Assert.Equal("Failed", row.Transition),
            row => Assert.Equal("Failed", row.Transition));
    }

    [Fact]
    public void ExactFullTypeName_TakesPrecedenceOverSuffixMatch()
    {
        var vector = Vector("1.0.0");
        var evaluations = new[]
        {
            Evaluation(
                vector,
                0,
                Surface(
                    Type("Widget", @namespace: "Other.Sample"),
                    Type("Widget"))),
        };

        bool resolved = TimelineCommand.TryResolveTypeName(
            "Sample.Widget",
            evaluations,
            out var typeFullName,
            out var error);

        Assert.True(resolved, error);
        Assert.Equal("Sample.Widget", typeFullName);
    }

    [Fact]
    public void ExactCaseTypeName_TakesPrecedenceOverCaseInsensitiveMatch()
    {
        var vector = Vector("1.0.0");
        TimelineCommand.TimelineEvaluation[] evaluations =
        [
            Evaluation(
                vector,
                0,
                Surface(
                    Type("Widget"),
                    Type("widget"))),
        ];

        bool resolved = TimelineCommand.TryResolveTypeName(
            "Sample.Widget",
            evaluations,
            out string? typeFullName,
            out string? error);

        Assert.True(resolved, error);
        Assert.Equal("Sample.Widget", typeFullName);
    }

    [Fact]
    public void ExactCaseMemberTimelinePreservesAddedPair()
    {
        var vector = Vector("1.0.0", "2.0.0");
        TimelineCommand.TimelineEvaluation[] evaluations =
        [
            Evaluation(
                vector,
                0,
                Surface(Type(
                    "Widget",
                    members: [Method("Run", "void Run()")]))),
            Evaluation(
                vector,
                1,
                Surface(Type(
                    "widget",
                    members: [Method("Run", "void Run()")]))),
        ];

        var view = TimelineCommand.BuildView(
            vector,
            "Sample.widget",
            MetadataFindings.MemberDescriptor.Id,
            evaluations,
            Sections(),
            memberName: "Run");

        Assert.Contains(
            view.Transitions!,
            row => row.Transition == "Added");
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("sample.widget")]
    public void PartialTypeIdentity_CanonicalizesSelectorAndFailsCensus(
        string selector)
    {
        var owner = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Sample", ["Widget"])).Name;
        var partialSurface = new ApiSurface();
        partialSurface.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "type row",
                0x02000001,
                MetadataTypeNameFailureMechanism.Metadata,
                "MalformedMetadata",
                "The type row could not be decoded.")
            {
                OwningTypeDefinition = owner,
            });
        var vector = Vector("1.0.0");
        TimelineCommand.TimelineEvaluation[] evaluations =
        [
            Evaluation(vector, 0, partialSurface),
        ];

        bool resolved = TimelineCommand.TryResolveTypeName(
            selector,
            evaluations,
            out string? typeFullName,
            out string? error);

        Assert.True(resolved, error);
        Assert.Equal("Sample.Widget", typeFullName);
        var view = TimelineCommand.BuildView(
            vector,
            typeFullName!,
            "api.member",
            evaluations,
            Sections());
        Assert.Equal("Failed", Assert.Single(view.Evaluations!).State);
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

    static byte[] BuildMalformedBodyImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("TimelineMalformedBody.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TimelineMalformedBody"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Broken"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .Pointer()
                        .Int32());
        var helperSignature = new BlobBuilder();
        new BlobEncoder(helperSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                _ => { });
        var malformedIl = new BlobBuilder();
        malformedIl.WriteByte((byte)ILOpCode.Call);
        malformedIl.WriteInt32(0x06000002);
        malformedIl.WriteByte(0xFE);
        malformedIl.WriteByte(0x06);
        malformedIl.WriteInt32(0x0AFFFFFF);
        malformedIl.WriteByte((byte)ILOpCode.Pop);
        malformedIl.WriteByte((byte)ILOpCode.Ret);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int bodyOffset = bodyEncoder.AddMethodBody(
                new InstructionEncoder(malformedIl),
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        var helperIl = new BlobBuilder();
        var helperInstructions = new InstructionEncoder(helperIl);
        helperInstructions.OpCode(ILOpCode.Ret);
        int helperBodyOffset = bodyEncoder.AddMethodBody(
            helperInstructions,
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Helper"),
            metadata.GetOrAddBlob(helperSignature),
            helperBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ApiSurface Surface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiType Type(
        string name,
        List<ApiMember>? members = null,
        List<string>? attributes = null,
        string @namespace = "Sample")
        => new()
        {
            Namespace = @namespace,
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

    static MethodIdentity MethodIdentity()
        => new(
            "Sample",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition("Sample", "Sample", "Widget"),
            "Run",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

    static FindingSubject AnalysisSubject()
        => new(
            "analysis.member:Sample.Widget:Run",
            "Sample.Widget.Run");

    public static unsafe int ReadPointer(int* value)
        => *value;
}
