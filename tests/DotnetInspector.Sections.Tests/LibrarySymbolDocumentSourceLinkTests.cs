using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// The source_link section (docs/design/library-symbol-document.md#source-and-documentation)
/// over a compiled Library whose Portable PDB maps one document through
/// SourceLink and leaves a generated one unmapped.
/// </summary>
public sealed class LibrarySymbolDocumentSourceLinkTests
{
    private const string MappedPath = "/_/src/Widgets/Widget.cs";
    private const string UnmappedPath = "/_/obj/Generated.cs";
    private const string RawRoot = "https://raw.example.test/widgets/0123abcd/";

    private const string MappedSource =
        """
        using System;
        using System.Threading.Tasks;

        namespace Widgets;

        public class Widget
        {
            private int _size;

            public int Count;

            public int Size
            {
                get { return _size; }
                set { _size = value; }
            }

            public event EventHandler? Changed
            {
                add { }
                remove { }
            }

            public int Twice(int value)
            {
                return value * 2;
            }

            public async Task<int> LaterAsync()
            {
                await Task.Yield();
                return 1;
            }
        }

        public abstract class Shape
        {
            public abstract double Area();
        }

        public interface IThing
        {
            void Do();
        }

        public enum Color
        {
            Red,
        }
        """;

    private const string UnmappedSource =
        """
        namespace Widgets;

        public class Generated
        {
            public void Run()
            {
            }
        }
        """;

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 1_000,
            maxMembers: 10_000,
            maxInspectionFailures: 100,
            maxTypeForwarders: 100,
            maxMetadataRows: 100_000,
            maxRetainedTextCharacters: 1_000_000);

    private static readonly LibrarySymbolDocumentSource s_source =
        new("Widgets.dll", "file", "", "", "", "");

    [Fact]
    public async Task MapsEveryRecordFromTheSettledPdb()
    {
        Compiled library = Compile(sourceLink: true);
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            library.Image,
            Settled(library.Pdb));

        var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
        Assert.True(written.IsComplete);
        Assert.Equal(
            new LibrarySymbolSectionOutcome("source_link", LibrarySymbolSectionStatus.Complete, ""),
            Assert.Single(written.Sections, section => section.Name == "source_link"));

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Contains(
            "source_link",
            root.GetProperty("request").GetProperty("sections").EnumerateArray().Select(static s => s.GetString()));
        string widgetUrl = RawRoot + "src/Widgets/Widget.cs";

        Assert.Equal(("complete", widgetUrl), TypeSourceOf(root, "T:Widgets.Widget"));
        Assert.Equal(("unmapped_document", ""), TypeSourceOf(root, "T:Widgets.Generated"));
        Assert.Equal(("no_method_body", ""), TypeSourceOf(root, "T:Widgets.IThing"));
        Assert.Equal(("no_method_body", ""), TypeSourceOf(root, "T:Widgets.Color"));
        // Shape's only body is its implicit constructor, which the PDB does not map.
        Assert.Equal(("no_sequence_points", ""), TypeSourceOf(root, "T:Widgets.Shape"));

        Assert.Equal(
            ("complete", widgetUrl, LineAfter(MappedSource, "public int Twice(int value)")),
            MemberSourceOf(root, "M:Widgets.Widget.Twice(System.Int32)"));
        // A classic async method's line is its state machine's.
        Assert.Equal(
            ("complete", widgetUrl, LineAfter(MappedSource, "public async Task<int> LaterAsync()")),
            MemberSourceOf(root, "M:Widgets.Widget.LaterAsync"));
        // A property is located through its getter, an event through its adder.
        Assert.Equal(
            ("complete", widgetUrl, LineOf(MappedSource, "get { return _size; }")),
            MemberSourceOf(root, "P:Widgets.Widget.Size"));
        Assert.Equal(
            ("complete", widgetUrl, LineOf(MappedSource, "add { }")),
            MemberSourceOf(root, "E:Widgets.Widget.Changed"));
        Assert.Equal(("no_method_body", "", 0), MemberSourceOf(root, "F:Widgets.Widget.Count"));
        Assert.Equal(("no_method_body", "", 0), MemberSourceOf(root, "M:Widgets.Shape.Area"));
        Assert.Equal(("no_method_body", "", 0), MemberSourceOf(root, "M:Widgets.IThing.Do"));
        Assert.Equal(("no_sequence_points", "", 0), MemberSourceOf(root, "M:Widgets.Shape.#ctor"));
        Assert.Equal(("unmapped_document", "", 0), MemberSourceOf(root, "M:Widgets.Generated.Run"));
    }

    [Fact]
    public async Task EveryMappedLineEqualsMemberSourceLocations()
    {
        Compiled library = Compile(sourceLink: true);
        (_, byte[] bytes) = await ProduceAsync(library.Image, Settled(library.Pdb));
        using JsonDocument document = JsonDocument.Parse(bytes);

        // Member Source Locations reads the same PDB through the SourceLink
        // findings owner, the best-ranked body's primary document winning.
        using PdbContext context = PdbContext.OpenPrefetchedMetadataOnly(
            new MemoryStream(library.Image),
            "Widgets");
        using SourceLinkService service = SourceLinkService.Borrow(context);
        service.LoadPdbFromStream(new MemoryStream(library.Pdb));
        int compared = 0;
        foreach (JsonElement member in document.RootElement.GetProperty("members").EnumerateArray())
        {
            JsonElement source = member.GetProperty("source");
            if (source.GetProperty("status").GetString() != "complete")
                continue;
            int token = BodyTokens(library.Image, member.GetProperty("metadata_token").GetInt32()).First();
            var inspection = Assert.IsType<FindingInspection<MemberSourceObservation>.Complete>(
                SourceLinkFindings.InspectMemberSources(
                    service,
                    new FindingSubject("Widgets.dll", "Widgets.dll"),
                    new MemberSourceQuery(
                        new HashSet<int> { token },
                        IncludeStateMachineImplementations: true)).Value);
            MemberSourceObservation expected = inspection.Findings
                .Select(static finding => finding.Payload)
                .OrderByDescending(static mapping => mapping.IsPrimaryDocument)
                .ThenBy(static mapping => mapping.DocumentRowId)
                .First();
            Assert.Equal(expected.ResolvedUrl, source.GetProperty("url").GetString());
            Assert.Equal(expected.StartLine, source.GetProperty("first_statement_line").GetInt32());
            compared++;
        }
        Assert.Equal(4, compared);
    }

    [Fact]
    public async Task SettlementOutcomesSettleEveryRecord()
    {
        Compiled library = Compile(sourceLink: true);

        // Offline settles: the document is complete.
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            library.Image,
            new LibrarySymbolDocumentSourceLink.Offline());
        AssertUniform(outcome, bytes, LibrarySymbolSectionStatus.Offline, "offline", complete: true);

        // No PDB anywhere settles too.
        (outcome, bytes) = await ProduceAsync(
            library.Image,
            new LibrarySymbolDocumentSourceLink.Unavailable());
        AssertUniform(outcome, bytes, LibrarySymbolSectionStatus.Complete, "no_pdb", complete: true);

        // A failed settlement leaves the document partial.
        (outcome, bytes) = await ProduceAsync(
            library.Image,
            new LibrarySymbolDocumentSourceLink.Failed("symbol server timed out"));
        AssertUniform(outcome, bytes, LibrarySymbolSectionStatus.Failed, "failed", complete: false);

        // A PDB from another build does not match the image's CodeView identity.
        Compiled other = Compile(
            sourceLink: true,
            mappedSource: MappedSource + "\npublic class Extra { public void M() { } }\n");
        (outcome, bytes) = await ProduceAsync(library.Image, Settled(other.Pdb));
        AssertUniform(outcome, bytes, LibrarySymbolSectionStatus.Failed, "failed", complete: false);

        // Unreadable PDB content is rejected the same way.
        (outcome, bytes) = await ProduceAsync(library.Image, Settled([1, 2, 3, 4]));
        AssertUniform(outcome, bytes, LibrarySymbolSectionStatus.Failed, "failed", complete: false);
    }

    [Fact]
    public async Task APdbWithoutSourceLinkSettlesMappedRecordsAsNoSourceLink()
    {
        Compiled library = Compile(sourceLink: false);
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            library.Image,
            Settled(library.Pdb));
        Assert.True(Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).IsComplete);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(("no_source_link", ""), TypeSourceOf(root, "T:Widgets.Widget"));
        Assert.Equal(("no_source_link", "", 0), MemberSourceOf(root, "M:Widgets.Widget.Twice(System.Int32)"));
        // A record's own body still decides first.
        Assert.Equal(("no_method_body", "", 0), MemberSourceOf(root, "M:Widgets.Shape.Area"));
    }

    [Fact]
    public async Task ExhaustingTheSourceLinkMapLimitAbortsTheWrite()
    {
        Compiled library = Compile(sourceLink: true);
        (LibrarySymbolDocumentOutcome outcome, _) = await ProduceAsync(
            library.Image,
            new LibrarySymbolDocumentSourceLink.Settled(
                () => new MemoryStream(library.Pdb),
                new SourceLinkReadLimits(int.MaxValue, maxMapBytes: 8, int.MaxValue)));
        Assert.Equal(
            LibrarySymbolDocumentAbort.SourceLinkBudget,
            Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(outcome).Reason);
    }

    [Fact]
    public async Task TheSectionNeedsTheHostsSettlement()
    {
        Assert.Throws<ArgumentException>(() => new LibrarySymbolDocumentRequest(
            MetadataLibrarySymbolAdmission.PublicApi,
            new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
            s_source,
            s_bounds,
            [LibrarySymbolDocumentSection.SourceLink]));

        // Without evidence the default request leaves the section out.
        Compiled library = Compile(sourceLink: true);
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(library.Image, null);
        Assert.Equal(
            LibrarySymbolSectionStatus.NotRequested,
            Assert.Single(
                Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).Sections,
                section => section.Name == "source_link").Status);
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal(("not_requested", "", 0), MemberSourceOf(document.RootElement, "M:Widgets.Widget.Twice(System.Int32)"));
    }

    private static void AssertUniform(
        LibrarySymbolDocumentOutcome outcome,
        byte[] bytes,
        LibrarySymbolSectionStatus section,
        string status,
        bool complete)
    {
        var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
        Assert.Equal(complete, written.IsComplete);
        Assert.Equal(section, Assert.Single(written.Sections, s => s.Name == "source_link").Status);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(complete ? "complete" : "partial", root.GetProperty("outcome").GetString());
        foreach (JsonProperty type in root.GetProperty("types").EnumerateObject())
            Assert.Equal(status, type.Value.GetProperty("source").GetProperty("status").GetString());
        foreach (JsonElement member in root.GetProperty("members").EnumerateArray())
            Assert.Equal(status, member.GetProperty("source").GetProperty("status").GetString());
    }

    /// <summary>A Type's source status and its URLs, joined by spaces.</summary>
    private static (string Status, string Urls) TypeSourceOf(JsonElement root, string id)
    {
        JsonElement source = root.GetProperty("types").GetProperty(id).GetProperty("source");
        return (
            source.GetProperty("status").GetString()!,
            string.Join(' ', source.GetProperty("urls").EnumerateArray().Select(static url => url.GetString())));
    }

    private static (string Status, string Url, int Line) MemberSourceOf(JsonElement root, string id)
    {
        JsonElement source = root.GetProperty("members").EnumerateArray()
            .Single(member => member.GetProperty("documentation_id").GetString() == id)
            .GetProperty("source");
        return (
            source.GetProperty("status").GetString()!,
            source.GetProperty("url").GetString()!,
            source.GetProperty("first_statement_line").GetInt32());
    }

    /// <summary>The 1-based line holding <paramref name="text"/>.</summary>
    private static int LineOf(string source, string text) =>
        source.Split('\n').Select((line, index) => (line, index)).Single(entry => entry.line.Contains(text, StringComparison.Ordinal)).index + 1;

    /// <summary>The 1-based line after the one holding <paramref name="text"/>: a block body's opening brace.</summary>
    private static int LineAfter(string source, string text) => LineOf(source, text) + 1;

    /// <summary>The MethodDef tokens of a member token: itself, or a property's getter or an event's adder.</summary>
    private static IEnumerable<int> BodyTokens(byte[] image, int token)
    {
        using var reader = new PEReader(new MemoryStream(image));
        MetadataReader metadata = reader.GetMetadataReader();
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        MethodDefinitionHandle body = handle.Kind switch
        {
            HandleKind.MethodDefinition => (MethodDefinitionHandle)handle,
            HandleKind.PropertyDefinition => metadata.GetPropertyDefinition((PropertyDefinitionHandle)handle).GetAccessors().Getter,
            HandleKind.EventDefinition => metadata.GetEventDefinition((EventDefinitionHandle)handle).GetAccessors().Adder,
            _ => throw new InvalidOperationException(),
        };
        yield return MetadataTokens.GetToken(body);
    }

    private static LibrarySymbolDocumentSourceLink.Settled Settled(byte[] pdb) =>
        new(() => new MemoryStream(pdb), SourceLinkReadLimits.Unlimited);

    private static async Task<(LibrarySymbolDocumentOutcome Outcome, byte[] Bytes)> ProduceAsync(
        byte[] image,
        LibrarySymbolDocumentSourceLink? sourceLink)
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        using var destination = new MemoryStream();
        LibrarySymbolDocumentOutcome outcome = LibrarySymbolDocumentOperation.Execute(
            new LibrarySymbolDocumentRequest(
                MetadataLibrarySymbolAdmission.PublicApi,
                new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
                s_source,
                s_bounds,
                sourceLink: sourceLink),
            library.IssueOperation(),
            destination,
            TestContext.Current.CancellationToken);
        await library.RetireAsync();
        return (outcome, destination.ToArray());
    }

    private sealed record Compiled(byte[] Image, byte[] Pdb);

    private static Compiled Compile(bool sourceLink, string mappedSource = MappedSource)
    {
        const string assemblyName = "Widgets";
        CSharpParseOptions options = new(LanguageVersion.Preview);
        SyntaxTree[] trees =
        [
            CSharpSyntaxTree.ParseText(SourceText.From(mappedSource, Encoding.UTF8), options, MappedPath),
            CSharpSyntaxTree.ParseText(SourceText.From(UnmappedSource, Encoding.UTF8), options, UnmappedPath),
        ];
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(static path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
                .Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        using var map = sourceLink
            ? new MemoryStream(Encoding.UTF8.GetBytes(
                $$$"""{"documents": {"/_/src/*": "{{{RawRoot}}}src/*"}}"""))
            : null;
        EmitResult result = compilation.Emit(
            pe,
            pdb,
            sourceLinkStream: map,
            options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: assemblyName + ".pdb"));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return new(pe.ToArray(), pdb.ToArray());
    }
}
