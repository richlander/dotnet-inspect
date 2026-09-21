using DotnetInspect.Cli.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Tests;

public partial class OutputFormatterTests
{
    [Fact]
    public void BuildShapeView_GroupsMethodOverloadsByLogicalName()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(string value)" },
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(ReadOnlySpan<char> value)" },
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (2 logical, 3 overloads)", methods.Text);
        Assert.NotNull(methods.Children);
        Assert.Equal(["string Format()", "Parse (2 overloads)"], methods.Children.Select(c => c.Text));
    }

    [Fact]
    public void BuildShapeView_KeepsSingleOverloadSignature()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (1)", methods.Text);
        var child = Assert.Single(methods.Children!);
        Assert.Equal("string Format()", child.Text);
    }

    [Fact]
    public void BuildShapeView_MemberLimitCountsCollapsedOverloadGroups()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Alpha", Signature = "void Alpha()" },
                new() { Kind = "method", Name = "Alpha", Signature = "void Alpha(int value)" },
                new() { Kind = "method", Name = "Beta", Signature = "void Beta()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            memberLimit: 1);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (1 logical, 2 overloads)", methods.Text);
        var child = Assert.Single(methods.Children!);
        Assert.Equal("Alpha (2 overloads)", child.Text);

        var expanded = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            verbosity: Verbosity.Normal,
            memberLimit: 1);

        var expandedMethods = Assert.Single(expanded.Members);
        Assert.Equal("Methods (1)", expandedMethods.Text);
        var expandedChild = Assert.Single(expandedMethods.Children!);
        Assert.Equal("void Alpha()", expandedChild.Text);
    }

    [Fact]
    public void BuildShapeView_ExpandedOperatorLimitUsesDisplayOrder()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "operator", Name = "op_Addition", Signature = "Widget op_Addition(Widget left, Widget right)" },
                new() { Kind = "operator", Name = "op_Explicit", Signature = "Widget op_Explicit(int value)" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            verbosity: Verbosity.Normal,
            memberLimit: 1);

        var operators = Assert.Single(view.Members);
        var child = Assert.Single(operators.Children!);
        Assert.Equal("Widget op_Explicit(int value)", child.Text);
    }

    [Fact]
    public void GetMemberSignatureSortKey_StripsMethodGenericListOnly()
    {
        var member = new ApiMember
        {
            Kind = "method",
            Name = "Task",
            Signature = "System.Threading.Tasks.Task<T> Task<T>(T value)"
        };

        Assert.Equal(
            "System.Threading.Tasks.Task<T> Task(T value)",
            ApiOutputFormatter.GetMemberSignatureSortKey(member));
    }

    [Fact]
    public void PopulateMemberSignature_EscapesKeywordMethodAndQualifiedTypeNames()
    {
        var keywordMethodType = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "return", Signature = "int return(int value)" },
            ]
        };
        var keywordTypeReturn = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "CreateKeywordType", Signature = "Probe.class CreateKeywordType()" },
            ]
        };
        var defaultStringLiteral = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "DefaultPath", Signature = "void DefaultPath(string value = \"config.in.txt\")" },
            ]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Normal };
        var methodView = new TypeView();
        var returnTypeView = new TypeView();
        var defaultStringView = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(methodView, keywordMethodType, options);
        ApiOutputFormatter.PopulateMemberSignature(returnTypeView, keywordTypeReturn, options);
        ApiOutputFormatter.PopulateMemberSignature(defaultStringView, defaultStringLiteral, options);

        var methodSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(methodView.SignatureRows));
        var returnTypeSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(returnTypeView.SignatureRows));
        var defaultStringSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(defaultStringView.SignatureRows));
        Assert.Equal("<code>public int @return(int value)</code>", methodSignature.Signature);
        Assert.Equal("<code>public Probe.@class CreateKeywordType()</code>", returnTypeSignature.Signature);
        Assert.Equal("<code>public void DefaultPath(string value = \"config.in.txt\")</code>", defaultStringSignature.Signature);
    }

    [Fact]
    public void TypeViewSchema_DoesNotOwnFirstClassMemberRows()
    {
        var schema = ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema();

        Assert.Null(schema.GetSection("Method Groups"));
        Assert.Null(schema.GetSection("Methods"));
        Assert.Null(schema.GetSection("Operators"));
        Assert.Null(schema.GetSection("Explicit Interface Implementations"));
        Assert.Null(schema.GetSection("Extension Methods"));
        Assert.Null(schema.GetSection("Events"));
    }

    [Fact]
    public void TypeDocumentSchema_MergesFirstClassMemberViews()
    {
        var schema = ApiCommand.GetTypeDocumentSchema(new MemberOptions());

        Assert.NotNull(schema.GetSection("Method Groups"));
        Assert.NotNull(schema.GetSection("Methods"));
        Assert.NotNull(schema.GetSection("Operators"));
        Assert.NotNull(schema.GetSection("Explicit Interface Implementations"));
        Assert.NotNull(schema.GetSection("Extension Methods"));
        Assert.NotNull(schema.GetSection("Events"));
    }

    [Fact]
    public void RenderManifestFormatter_CapturesStructuredSectionsColumnsAndFields()
    {
        var schema = new DocumentSchema()
            .Add("Methods", "column", "Field", "Signature | Display")
            .Add("Library Info", "field", "Assembly Version")
            .Add("Other Section", "field", "Methods");
        var formatter = new RenderManifestFormatter(schema);
        var options = MarkoutWriterOptions.Default;
        var sectionLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
        var nestedLevel = Math.Clamp(4 + options.HeadingLevelOffset, 1, 6);
        formatter.BeginDocument(options);

        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Methods", context: null);
        formatter.FormatHeading(TextWriter.Null, nestedLevel, "Method Details", context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Field", "Signature | Display"],
            [["Run", "void Run()"]],
            skippedRows: 0,
            MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Library Info", context: null);
        formatter.BeginTable(
            TextWriter.Null,
            ["Property", "Contents"],
            MarkoutWriterOptions.Default);
        formatter.WriteRow(TextWriter.Null, ["Assembly Version", "1.0.0.0"]);
        formatter.EndTable(TextWriter.Null, skippedRows: 0);
        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Other Section", context: null);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Methods", "polluting value")],
            bold: false);
        formatter.BeginDocument(options);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Kind", "class")],
            bold: false);

        var columns = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetTableColumns("Methods"));
        Assert.Contains("Field", columns);
        Assert.Contains("Signature | Display", columns);
        Assert.Null(formatter.Manifest.GetTableColumns("Method Details"));
        Assert.Null(formatter.Manifest.GetFields("Methods"));
        var fields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetFields("Library Info"));
        Assert.Contains("Assembly Version", fields);
        Assert.DoesNotContain("Methods", fields);
        var otherFields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetFields("Other Section"));
        Assert.Contains("Methods", otherFields);
        Assert.DoesNotContain("Kind", otherFields);
        Assert.Contains(
            "Kind",
            formatter.Manifest.GetRenderedNames("field"));
        Assert.True(formatter.Manifest.HasAnyData);
    }

    [Fact]
    public void RenderManifestFormatter_CapturesRootFieldTableData()
    {
        var schema = new DocumentSchema()
            .Add("API Info", "field", "Version");
        var formatter = new RenderManifestFormatter(schema, "API Info");
        formatter.BeginDocument(MarkoutWriterOptions.Default);

        formatter.BeginTable(
            TextWriter.Null,
            ["Field", "Value"],
            MarkoutWriterOptions.Default);
        formatter.WriteRow(TextWriter.Null, ["Version", "1.0.0"]);
        formatter.EndTable(TextWriter.Null, skippedRows: 0);

        Assert.Contains(
            "Version",
            formatter.Manifest.GetRenderedNames(
                "field",
                ["API Info"]));
        Assert.Equal(
            ["Field", "Value"],
            formatter.Manifest.GetRenderedNames(
                "column",
                ["API Info"]).Order());
    }

    [Fact]
    public void RenderedSectionManifest_MergesReplayedFieldsOnlyForRenderedTables()
    {
        var projected = new RenderedSectionManifest();
        projected.RecordTable(null, ["Signature"]);
        projected.RecordTable("Visible", ["Signature"]);

        var fieldReplay = new RenderedSectionManifest();
        fieldReplay.RecordField(null, "Root Field");
        fieldReplay.RecordField("Visible", "Visible Field");
        fieldReplay.RecordField("Suppressed", "Suppressed Field");

        projected.MergeRenderedFieldTablesFrom(fieldReplay);

        Assert.Contains(
            "Root Field",
            projected.GetRootRenderedNames("field"));
        Assert.Contains(
            "Visible Field",
            projected.GetSectionRenderedNames("field", "Visible"));
        Assert.DoesNotContain(
            "Suppressed Field",
            projected.GetSectionRenderedNames("field", "Suppressed"));
    }

    [Fact]
    public void RenderManifestFormatter_UsesFieldColumnIdentityAfterReordering()
    {
        var schema = new DocumentSchema()
            .Add("API Info", "field", "Version", "Owners");
        var formatter = new RenderManifestFormatter(schema, "API Info");
        formatter.BeginDocument(MarkoutWriterOptions.Default);

        formatter.BeginTable(
            TextWriter.Null,
            ["Value", "Field"],
            MarkoutWriterOptions.Default);
        formatter.WriteRow(TextWriter.Null, ["1.0.0", "Version"]);
        formatter.EndTable(TextWriter.Null, skippedRows: 0);

        IReadOnlySet<string> fields = formatter.Manifest.GetRenderedNames(
            "field",
            ["API Info"]);
        Assert.Contains("Version", fields);
        Assert.DoesNotContain("1.0.0", fields);
        Assert.DoesNotContain("Owners", fields);
    }

    [Fact]
    public void RenderManifestFormatter_ZeroRowTableDeclaresColumnsWithoutData()
    {
        var schema = new DocumentSchema()
            .Add("Methods", "column", "Name", "Signature");
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, 2, "Methods", context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Name", "Signature"],
            [],
            skippedRows: 0,
            MarkoutWriterOptions.Default);

        Assert.Equal(
            ["Name", "Signature"],
            formatter.Manifest.GetTableColumns("Methods")!.Order());
        Assert.Empty(
            formatter.Manifest.GetRenderedNames(
                "column",
                ["Methods"]));
        Assert.False(formatter.Manifest.HasAnyData);
    }

    [Fact]
    public void RenderManifestFormatter_UnionsRepeatedTablesAndTracksDataColumns()
    {
        var schema = new DocumentSchema()
            .Add("Methods", "column", "Name", "Signature", "Source");
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, 2, "Methods", context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Name", "Signature"],
            [["Run", "void Run()"]],
            skippedRows: 0,
            MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, 2, "Methods", context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Name", "Source"],
            [["Stop", ""]],
            skippedRows: 0,
            MarkoutWriterOptions.Default);

        Assert.Equal(
            ["Name", "Signature", "Source"],
            formatter.Manifest.GetTableColumns("Methods")!.Order());
        Assert.Equal(
            ["Name", "Signature"],
            formatter.Manifest.GetRenderedNames(
                "column",
                ["Methods"]).Order());
    }

    [Fact]
    public void RenderManifestFormatter_ScopesSameNamedFieldsBySection()
    {
        var schema = new DocumentSchema()
            .Add("First", "field", "Status")
            .Add("Second", "field", "Status");
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, 2, "First", context: null);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Status", "available")],
            bold: false);
        formatter.FormatHeading(TextWriter.Null, 2, "Second", context: null);

        Assert.Contains(
            "Status",
            formatter.Manifest.GetRenderedNames(
                "field",
                ["First"]));
        Assert.DoesNotContain(
            "Status",
            formatter.Manifest.GetRenderedNames(
                "field",
                ["Second"]));
    }

    [Fact]
    public void RenderManifestFormatter_CanonicalizesMachineColumnNames()
    {
        var schema = new DocumentSchema()
            .Add(
                "Member Index",
                "column",
                new SchemaItem(
                    "Canonical Signature",
                    "column",
                    "CanonicalSignature"));
        var formatter = new RenderManifestFormatter(
            schema,
            "Member Index");
        formatter.BeginDocument(new MarkoutWriterOptions
        {
            TableMode = MarkoutTableMode.Tsv
        });
        formatter.BeginTable(
            TextWriter.Null,
            ["canonical_signature"],
            MarkoutWriterOptions.Default);
        formatter.WriteRow(
            TextWriter.Null,
            ["M:System.String.get_Length"]);
        formatter.EndTable(TextWriter.Null, skippedRows: 0);

        Assert.Contains(
            "Canonical Signature",
            formatter.Manifest.GetRenderedNames(
                "column",
                ["Member Index"]));
    }

    [Fact]
    public void RenderManifestFormatter_RootScopeIgnoresSyntheticHeading()
    {
        var schema = new DocumentSchema()
            .Add("Classes", "column", "Type");
        var formatter = new RenderManifestFormatter(
            schema,
            "Classes");
        formatter.BeginDocument(MarkoutWriterOptions.Default);
        formatter.FormatHeading(
            TextWriter.Null,
            2,
            "Types",
            context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Type"],
            [["System.String"]],
            skippedRows: 0,
            MarkoutWriterOptions.Default);

        Assert.Contains(
            "Type",
            formatter.Manifest.GetRenderedNames(
                "column",
                ["Classes"]));
        Assert.Empty(
            formatter.Manifest.GetRenderedNames(
                "column",
                ["Types"]));
    }

    [Fact]
    public void RenderManifestFormatter_DoesNotTreatTitleTextAsAField()
    {
        var result = new InspectionResult
        {
            PackageName = "Test.Published.PackageInfoDiscovery",
            Version = "1.0.0",
            Authors = "tests"
        };
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo
            }
        };
        var schema = new DocumentSchema()
            .Add(PackageSections.PackageInfo, "field", "Authors", "Published", "Version");
        var manifest = RenderManifestFormatter.Capture(
            new InspectionResultView(result),
            InspectionContext.Default,
            writerOptions,
            schema);

        var fields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            manifest.GetFields(PackageSections.PackageInfo));
        Assert.Contains("Authors", fields);
        Assert.DoesNotContain("Published", fields);
    }

    [Fact]
    public void MemberSignature_ShowsDegradedDecodeMarker()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "object Run()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "object",
                        MemberName = "Run"
                    },
                    SignatureDecodeStatus = SignatureDecodeStatus.Degraded
                }
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(view, type, new MemberOptions());

        var row = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(view.SignatureRows));
        Assert.Equal("degraded", row.Decode);
    }

    [Fact]
    public void SignatureDecodeIsEmpty_HidesColumnOnlyWhenNoMemberDegraded()
    {
        Assert.True(TypeView.SignatureDecodeIsEmpty(null));
        Assert.True(TypeView.SignatureDecodeIsEmpty(
        [
            new MemberSignatureRow("void A()", "aaaa", "M:A", null, null),
            new MemberSignatureRow("void B()", "bbbb", "M:B", "", null),
        ]));

        // A degraded member must keep the Decode column so the failure marker stays visible.
        Assert.False(TypeView.SignatureDecodeIsEmpty(
        [
            new MemberSignatureRow("void A()", "aaaa", "M:A", null, null),
            new MemberSignatureRow("void B()", "bbbb", "M:B", "degraded", null),
        ]));
    }

    [Fact]
    public void SignatureUnavailableIsEmpty_HidesColumnOnlyWhenAllDeclarationsRender()
    {
        Assert.True(TypeView.SignatureUnavailableIsEmpty(null));
        Assert.True(TypeView.SignatureUnavailableIsEmpty(
        [
            new MemberSignatureRow("void A()", "aaaa", "M:A", null, null),
            new MemberSignatureRow("void B()", "bbbb", "M:B", null, null),
        ]));

        Assert.False(TypeView.SignatureUnavailableIsEmpty(
        [
            new MemberSignatureRow(
                "Unavailable",
                "aaaa",
                "M:A",
                null,
                null,
                "model-aware property spelling is not supported"),
        ]));
    }

    [Fact]
    public void MemberIndexDecodeIsEmpty_HidesColumnOnlyWhenNoMemberDegraded()
    {
        Assert.True(MemberIndexView.DecodeIsEmpty(null));
        Assert.True(MemberIndexView.DecodeIsEmpty(
        [
            new MemberIndexRow("A:0", "A~0", "M:A", null, "d0"),
            new MemberIndexRow("B:0", "B~0", "M:B", "", "d1"),
        ]));

        // A degraded member must keep the Decode column so the failure marker stays visible.
        Assert.False(MemberIndexView.DecodeIsEmpty(
        [
            new MemberIndexRow("A:0", "A~0", "M:A", null, "d0"),
            new MemberIndexRow("B:0", "B~0", "M:B", "degraded", "d1"),
        ]));
    }

    [Fact]
    public async Task PopulateMemberSections_CollectsDegradedSignaturesForStderrWarning()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "object Run()",
                    SignatureModel = new ApiSignature { ReturnType = "object", MemberName = "Run" },
                    SignatureDecodeStatus = SignatureDecodeStatus.Degraded
                },
                new ApiMember
                {
                    Kind = "method",
                    Name = "Ok",
                    Signature = "void Ok()",
                    SignatureModel = new ApiSignature { ReturnType = "void", MemberName = "Ok" }
                }
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSections(
            view, new MethodsView(), new OperatorsView(), new ExplicitInterfaceImplementationsView(),
            new ExtensionMethodsView(), new EventsView(), type, new MemberOptions());

        // Only the degraded member is recorded; the healthy member is not.
        var degraded = Assert.Single(view.DegradedSignatureMembers!);
        Assert.Contains("Run", degraded);
        Assert.DoesNotContain("Ok", degraded);

        var warning = await CaptureErrorAsync(() => ApiOutputFormatter.WriteSignatureDecodeWarning(view));
        Assert.Contains("could not be fully decoded", warning);
        Assert.Contains("Run", warning);
    }

    [Fact]
    public async Task WriteSignatureDecodeWarning_EmitsNothingWhenNoMemberDegraded()
    {
        Assert.Empty(await CaptureErrorAsync(() => ApiOutputFormatter.WriteSignatureDecodeWarning(new TypeView())));
    }

    [Fact]
    public void MemberRow_HasNoDecodeColumnInDefaultMemberTables()
    {
        // The Decode degradation marker is reported via stderr, never as a table column.
        Assert.DoesNotContain(
            typeof(MemberRow).GetProperties(),
            p => p.Name == "Decode");
    }

    [Fact]
    public void BuildTypeRenderManifest_CapturesActualMemberTable()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "void Run()"
                }
            ]
        };
        var options = new TypeOptions
        {
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.Methods
            }
        };

        var manifest = ApiCommand.BuildTypeRenderManifest(type, options);

        var columns = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            manifest.GetTableColumns(SectionNames.Methods));
        Assert.Contains("Name", columns);
        Assert.Contains("Signature", columns);
    }
}
