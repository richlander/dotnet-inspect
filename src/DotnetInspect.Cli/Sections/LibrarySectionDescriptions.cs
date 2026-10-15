using System.Reflection.Metadata.Ecma335;
using DotnetInspector.MetadataRendering;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Owner-issued descriptions of the Library sections that have adopted
/// <c>docs/design/section-model.md#description</c>. Each descriptor in
/// <see cref="LibrarySections"/> returns its entry; a section absent here
/// has not declared a description.
/// </summary>
internal static class LibrarySectionDescriptions
{
    private const string IlBodies = "Compiled IL method bodies.";

    private const string ComplexityBoundary =
        "Complexity is normal-flow cyclomatic complexity over compiled IL, "
        + "excluding exception edges; it is not authored intent or a "
        + "maintainability score.";

    public static SectionDescription LibraryMetrics { get; } = new(
        "Implementation volume, distribution, and concentration for the "
        + "library's method bodies, with namespace and type leverage from "
        + "declared signatures.",
        "Compiled IL method bodies and metadata signatures.",
        "A completed report always states its populations, so a library "
        + "with no analyzed bodies reports zero counts; an unavailable "
        + "report is stated as unavailable, not as an empty result.",
        ComplexityBoundary);

    public static SectionDescription DependencyStructure { get; } = new(
        "Type and namespace dependencies within the library, from direct "
        + "calls and function references in its method bodies; `cycles` "
        + "lists each namespace cycle, and each namespace carries `level` "
        + "and `cycleIndex`.",
        "Compiled IL method bodies: call, callvirt, newobj, calli, ldftn, "
        + "and ldvirtftn sites.",
        "Empty `namespaceEdges` means no resolved call or function "
        + "reference crosses between two of the library's namespaces; "
        + "empty `cycles` means the namespace graph has no cycle.",
        "`level` is 0 for a namespace that depends on no other namespace "
        + "in the library and otherwise one more than the highest level it "
        + "depends on; namespaces in one cycle share a level. `cycleIndex` "
        + "is the namespace's position in `cycles`, or null outside any "
        + "cycle. Each namespace edge's `explainingTypeEdges` holds its "
        + "strongest contributing type pairs, with "
        + "`remainingContributorCount` for the rest. Edges follow call "
        + "targets as compiled, not virtual dispatch. A calli site has no "
        + "static target, so it counts as unresolved; unresolved calls or "
        + "incomplete bodies mark `completeness` as Qualified.");

    public static SectionDescription MemberMetrics { get; } = new(
        "Structural measurements for each compiled method body, attributed "
        + "to the declared method it implements.",
        IlBodies,
        "The library has no method bodies to profile.",
        "Measurements describe compiled IL, including compiler-generated "
        + "bodies, and incomplete profiles are marked as such. "
        + ComplexityBoundary);

    public static SectionDescription TopLeverage { get; } = new(
        "The library's methods ranked by how much of its own call graph "
        + "leans on them: distinct direct callers, then root reach, "
        + "fan-out, and calls inside loops.",
        "Compiled IL method bodies: the direct-call graph within the "
        + "library.",
        "The library has no methods in its call graph to rank.",
        "Static call-graph reach, not runtime frequency; chain depth and "
        + "root reach are budgeted and can be lower bounds on very large "
        + "libraries.");

    public static SectionDescription NameFamilies { get; } = new(
        "The library's type definitions grouped by the one- or two-word "
        + "suffix of their simple names.",
        "Metadata type definitions, with Portable PDB source provenance "
        + "for the source-evidence populations.",
        "No type definition in the selected population has a name that "
        + "yields a suffix family.",
        "Grouping follows name grammar only: a shared suffix is a naming "
        + "convention, not shared behavior or inheritance. "
        + "Compiler-generated type names take part.");

    public static SectionDescription ArchitecturalFamilies { get; } = new(
        "Name families combined with each member type's structural role "
        + "and pole, from signature references between types in the same "
        + "namespace.",
        "Metadata type definitions and declared member signatures, with "
        + "Portable PDB source provenance for the source-evidence "
        + "populations.",
        "No type definition in the selected population has a name that "
        + "yields a suffix family.",
        "Roles come from declared signatures (base types, interfaces, "
        + "constraints, and field, property, event, return, and parameter "
        + "types), not method-body calls. Names are grouped by grammar, "
        + "not design intent, and compiler-generated names take part.");

    public static SectionDescription CloneCandidates { get; } = new(
        "Pairs of the library's methods ranked by structural similarity of "
        + "their compiled bodies.",
        "Compiled IL method bodies, with metadata type and member names "
        + "for similar-name admission.",
        "With complete coverage, no pair of the library's methods was "
        + "admitted for ranking; an incomplete result is reported as "
        + "incomplete, not as an empty one.",
        "A ranked retrieval candidate, not a checked clone relation. The "
        + "similar-name admission can miss renamed or moved clones, and the "
        + "search covers only the selected library.");

    public static SectionDescription UnsafeMembers { get; } = new(
        "The library's members whose signatures, locals, or method bodies "
        + "use unsafe constructs: pointer or function-pointer types, pinned "
        + "locals, the Unsafe class, calls that require an unsafe context, "
        + "calli, localloc, cpblk, and initblk.",
        "Metadata signatures and compiled IL method bodies.",
        "No member's signature, locals, or body uses a recognized unsafe "
        + "construct; a degraded signature scan is stated as a diagnostic, "
        + "not as an empty result.",
        "Construct evidence, not a memory-safety verdict.");

    public static SectionDescription PInvokeMethods { get; } = new(
        "The library's public methods that are P/Invoke imports, with the "
        + "native module each one names.",
        "Metadata tables: method flags and the implementation map.",
        "No public, non-accessor method carries the P/Invoke flag; the "
        + "library may still declare non-public imports.",
        "Only methods declared public are listed, whatever their declaring "
        + "type's visibility; private and internal imports, accessors, and "
        + "methods on compiler-generated types are not.");

    public static SectionDescription AsyncMethods { get; } = new(
        "The library's public methods compiled as async, as runtime async "
        + "or as a compiler state machine, including async iterators.",
        "Metadata tables: method implementation flags and custom "
        + "attributes.",
        "No public, non-accessor method has the runtime-async flag or a "
        + "compiler async state-machine attribute.",
        "Async is recognized by the compiled flag or attribute, not by a "
        + "Task-returning signature, and only public non-accessor methods "
        + "are listed.");

    private const string OpportunityBasis =
        "Compiled IL method bodies and metadata of the inspected assembly.";

    private const string OpportunityEmpty =
        "A completed scan of the assembly's method bodies found no "
        + "candidates of this kind; a failed scan or unreadable bodies are "
        + "reported as a failure or diagnostic, not as an empty result.";

    private const string OpportunityBoundary =
        "Rows are static candidates inferred from IL shape, not measured "
        + "cost or runtime frequency; a benchmark or trace confirms them.";

    private static SectionDescription Opportunity(
        string summary,
        string? boundary = null) =>
        new(
            summary,
            OpportunityBasis,
            OpportunityEmpty,
            boundary is null
                ? OpportunityBoundary
                : OpportunityBoundary + " " + boundary);

    public static SectionDescription PerformanceBoxing { get; } = Opportunity(
        "Value-type boxing conversions whose boxed object escapes the "
        + "method, and generic parameters boxed to call Object.Equals.");

    public static SectionDescription PerformanceArrays { get; } = Opportunity(
        "Short-lived array allocations that a span or stack allocation could "
        + "replace: small constant-length arrays, temporary byte-array "
        + "copies, and span-to-array copies.");

    public static SectionDescription PerformanceClosures { get; } = Opportunity(
        "Delegate allocations made on every call: capturing lambdas, "
        + "instance method groups that bind a receiver, and value-factory "
        + "delegates passed to ConcurrentDictionary.GetOrAdd.",
        "Delegates cached in a field, as the compiler does for "
        + "non-capturing lambdas, and static method groups, which bind no "
        + "receiver, are not reported.");

    public static SectionDescription PerformanceEnumerators { get; } = Opportunity(
        "Heap-allocated enumerators from a foreach over an interface-typed "
        + "sequence inside a loop.");

    public static SectionDescription PerformanceStrings { get; } = new(
        "Exact IL operations that can produce a string value.",
        OpportunityBasis,
        OpportunityEmpty,
        "Each row is exact evidence that an IL operation can produce a "
        + "string, not proof of a new allocation, retained bytes, runtime "
        + "frequency, or that a change is safe.");

    public static SectionDescription PerformanceLoops { get; } = Opportunity(
        "Work repeated on every loop iteration that could be done once: "
        + "LINQ scans and materializations and string accumulation inside "
        + "loops, and sequence scans reached from a caller's loop or a "
        + "recursive traversal.");

    public static SectionDescription PerformanceHotspots { get; } = Opportunity(
        "Methods with many steady heap allocations, at least one of them "
        + "inside a loop, and no more specific candidate.",
        "The count covers every steady allocation site in the method, "
        + "excluding throw paths and exception construction, not only "
        + "sites inside loops; some sites may be required object "
        + "construction.");

    public static SectionDescription PerformanceAsync { get; } = Opportunity(
        "Async state-machine allocations, with sites inside loops marked as "
        + "repeated.",
        "State machines are inherent to compiled async code and usually "
        + "allocate once per call, not per item.");

    public static SectionDescription PerformanceOther { get; } = Opportunity(
        "Optimization candidates whose shape has no dedicated Performance "
        + "section, so the scan never silently drops a result.");

    public static SectionDescription PerformanceSyncCallsInAsync { get; } = new(
        "Synchronous calls inside async methods where the callee's type "
        + "offers an accessible async counterpart the method does not "
        + "already call, including Dispose and DisposeAsync pairs.",
        "Compiled IL bodies of async methods, with metadata of the callee "
        + "types in referenced assemblies.",
        "A completed scan found no synchronous call in an async method "
        + "whose callee type offers an async counterpart; a callee type "
        + "that could not be resolved is reported as a diagnostic, not "
        + "ruled out.",
        "Each row is a candidate, not proof that the async counterpart is "
        + "equivalent or appropriate; blocking patterns such as .Result and "
        + ".Wait() are not covered.");

    public static SectionDescription ArrayPoolEscapes { get; } = new(
        "Buffers rented from ArrayPool<T> that are not returned when an "
        + "exception escapes between the rent and the return.",
        "Compiled IL method bodies and their exception-handling regions, "
        + "read through the product's model of how the array pool rents "
        + "and returns buffers.",
        "A completed lifecycle analysis found no rented buffer that misses "
        + "its return on an exception path; analysis that cannot cover some "
        + "code reports that limitation instead of an empty result.",
        "Rows are hardening candidates for wasted pool buffers, not proven "
        + "leaks. Buffers that leave the method are treated as handed off, "
        + "and a return missing on the normal path is not reported here, so "
        + "an empty result does not prove every rented buffer is returned.");

    private const string MetadataBasis =
        "ECMA-335 metadata tables of the inspected image.";

    public static SectionDescription MetadataImage { get; } = new(
        "The inspected image's metadata root, PE and CLI header facts, and "
        + "the size and addressing of each metadata heap.",
        "PE and CLI headers and the metadata root and stream headers of the "
        + "inspected image.",
        "The section is not listed when the image carries no managed "
        + "metadata or the requested `--metadata-root` is absent; a failed "
        + "read is reported as an inspection failure, not as absence.",
        "Per-table row counts are not included; they are the counts of the "
        + "`Metadata:` table sections.");

    public static SectionDescription MetadataHeapValue { get; } = new(
        "The single metadata heap value at the heap coordinate given to "
        + "`library address`.",
        "The named metadata heap of the inspected image, read by address.",
        "The section exists only when `library address` supplies a heap "
        + "coordinate; address 0 reads as nil, and an address past the "
        + "heap's end fails the command and produces no section.",
        "The value is read by address alone, independent of any table row "
        + "that references it.");

    /// <summary>The description of one generated metadata heap section.</summary>
    public static SectionDescription ForHeap(HeapKind heap)
    {
        string name = MetadataHeapCoordinate.StreamName(heap);
        string listed =
            $"The section is listed only when the image's {name} heap holds "
            + "bytes";
        string referencedBasis =
            $"The image's {name} heap, read at the addresses that projected "
            + "metadata table cells reference.";
        return heap switch
        {
            HeapKind.UserString => new(
                $"The image's {name} user-string heap, whose entries cannot "
                + "be listed.",
                $"The size of the image's {name} heap from its stream header.",
                listed + ", and it never lists entries: no metadata table "
                + "points into the heap and it cannot be walked.",
                "Its references are ldstr operands in method bodies, which "
                + "this section does not read; `library address` reads one "
                + "value by address."),
            HeapKind.Guid => new(
                $"Every {name} entry in the image by index, with the number "
                + "of projected table cells that reference it.",
                $"The image's {name} heap, read completely by index.",
                listed + "; an image with no readable metadata lists no heap "
                + "section.",
                "Reference counts cover the projected tables only; zero means "
                + "stored but unreferenced by them."),
            _ => new(
                $"The distinct {name} entries that projected metadata table "
                + "rows reference, by heap address, with each entry's "
                + "reference count.",
                referencedBasis,
                listed + "; no rows means no projected table cell points into "
                + "the heap.",
                "This is not a walk of the heap: entries referenced only from "
                + "unprojected tables or from nothing are not listed, and "
                + "equal values stored at two addresses are two entries."),
        };
    }

    private const string MetadataTableBoundary =
        "Rows are physical table rows, not resolved declarations: a "
        + "reference shows its target table and row id, with a resolved name "
        + "only for type, method, field, member, assembly, and module "
        + "targets; signature and value blobs are bounded hex previews, not "
        + "decoded, and a dangling reference is marked malformed.";

    /// <summary>The description of one generated metadata table section.</summary>
    public static SectionDescription ForTable(TableIndex table) => new(
        $"The rows of the image's ECMA-335 {table} table.",
        MetadataBasis,
        $"The section is listed only when the image's {table} table has "
        + "rows, so its absence means the table is empty or the image has no "
        + "readable metadata; a projection failure is reported as a caveat, "
        + "not as an empty table.",
        MetadataTableBoundary);

    public static SectionDescription ReadyToRunImage { get; } = new(
        "The ReadyToRun envelope of the image: its role, how it was "
        + "advertised, its format version and flags, and its header and "
        + "manifest-metadata locations.",
        "PE headers: the CLI managed-native header directory, the "
        + "`RTR_HEADER` export, and the ReadyToRun header they locate.",
        "The section is not listed when neither location advertises a "
        + "ReadyToRun header, meaning the image is not ReadyToRun; an "
        + "advertised but malformed header is reported as an inspection "
        + "failure, not as absence.",
        "The format version is reported as encoded, with no runtime "
        + "compatibility judgment; native code is not read.");

    public static SectionDescription ReadyToRunSections { get; } = new(
        "Each entry of the image's ReadyToRun section directory, in the "
        + "order the image encodes.",
        "The ReadyToRun header's section directory.",
        "The section is not listed when the image is not ReadyToRun or its "
        + "header declares no sections.",
        "Entries locate section payloads without decoding them; unknown "
        + "section type IDs are kept and shown as Unknown.");

    private const string IlBodiesAndMetadata =
        "Compiled IL method bodies and metadata.";

    private const string FocusedBodyAnalysis =
        "Semantic analysis of the addressed method's compiled IL body.";

    public static SectionDescription ILOffset { get; } = new(
        "The source file, line, and Source Link URL of the nearest non-hidden "
        + "sequence point at or before the addressed IL offset, with the "
        + "matched offset when it differs.",
        "Portable PDB sequence points and Source Link.",
        "A produced result always has one location; a missing readable PDB "
        + "or no sequence point at or before the offset is reported as a "
        + "query failure, not as an empty result.",
        "The line is where the matched sequence point starts, so an offset "
        + "between sequence points maps to the earlier statement.");

    public static SectionDescription MemberContext { get; } = new(
        "The assembly, declaring type, and method that own the addressed "
        + "MethodDef token, with its signature, kind, visibility, and static "
        + "and async classification.",
        "Metadata tables.",
        "A produced result always names one method; a token that does not "
        + "resolve to a MethodDef is reported as a query failure, not as an "
        + "empty result.");

    public static SectionDescription InstructionContext { get; } = new(
        "The IL instruction that begins at the addressed offset: its opcode, "
        + "resolved operand, branch targets, next offset, basic block, and "
        + "control-flow role.",
        IlBodiesAndMetadata,
        "The offset is not an instruction start; a single-coordinate query "
        + "reports that as a failure, and discovery omits the section.",
        "Only exact instruction starts match; an offset inside an "
        + "instruction is not rounded to it.");

    public static SectionDescription ExceptionContext { get; } = new(
        "Each exception-handling clause whose try, filter, or handler extent "
        + "contains the addressed offset, with the role the offset plays and "
        + "the clause's ranges and caught type.",
        "The compiled method body's exception-region table.",
        "The addressed offset lies in no exception-handling region; the "
        + "section is not rendered.",
        "Containment is numeric over the declared extents; it does not "
        + "establish that the offset is an instruction start or reachable.");

    public static SectionDescription CallsiteContext { get; } = new(
        "The call-like instruction at the addressed offset: its opcode, call "
        + "kind, callee, and the return-address offset that follows it.",
        IlBodiesAndMetadata,
        "The instruction at the offset is not call, callvirt, newobj, calli, "
        + "ldftn, or ldvirtftn; the section is not rendered.",
        "Callees are named as compiled, not resolved through virtual "
        + "dispatch; ldftn and ldvirtftn load a method pointer without "
        + "calling it.");

    public static SectionDescription ReturnAddressContext { get; } = new(
        "The call whose return address is the addressed offset: the "
        + "immediately preceding call, callvirt, newobj, or calli instruction "
        + "and its callee.",
        IlBodiesAndMetadata,
        "The instruction that ends at the offset is not a call that returns; "
        + "the section is not rendered.",
        "The offset is read as the address after a call, so the end of the "
        + "method body qualifies.");

    public static SectionDescription AllocationContext { get; } = new(
        "Allocations the analysis attributes to the addressed offset, with "
        + "their kind, allocated type, frequency, escape, lifetime evidence, "
        + "size estimate, and path context.",
        FocusedBodyAnalysis,
        "No allocation is attributed to the addressed offset; the section is "
        + "not rendered.",
        "Static analysis of compiled IL, not runtime measurement; escape and "
        + "lifetime results carry their stated limitations.");

    public static SectionDescription SafetyContext { get; } = new(
        "Operations at the addressed offset that require an unsafe context: "
        + "pointer dereference, stackalloc, calli, unsafe opcodes, and calls "
        + "to unsafe members.",
        FocusedBodyAnalysis,
        "No unsafe operation or unsafe call occurs at the addressed offset; "
        + "the section is not rendered.",
        "Method-level evidence, such as unsafe signatures and pointer or "
        + "pinned locals, has no offset and is not reported here.");

    public static SectionDescription CostContext { get; } = new(
        "Indirect-call costs at the addressed offset: virtual dispatch, "
        + "delegate or function-pointer loads, and function-pointer calls, "
        + "with whether the site is in a loop.",
        FocusedBodyAnalysis,
        "No callvirt, ldftn, ldvirtftn, or calli occurs at the addressed "
        + "offset; direct calls and constructor calls report no cost, and "
        + "the section is not rendered.",
        "Dispatch shape as compiled, not measured time or runtime frequency: "
        + "every callvirt counts as virtual dispatch, including calls to "
        + "non-virtual methods.");

    private const string SourceLanguages =
        "C#, Visual Basic, and F# source documents";

    public static SectionDescription SourceFiles { get; } = new(
        "The SourceLink URL of each source document behind each of the "
        + "library's public types.",
        "Portable PDB document records and the PDB's SourceLink map; the PDB "
        + "may be downloaded from a symbol server.",
        "The library has no public types, or no portable PDB with a "
        + "SourceLink map was resolved; a type whose document did not "
        + "resolve is listed without a URL.",
        "URLs are built from the map and not requested, so a row does not "
        + "show that the source is reachable; `SourceLink: Availability` "
        + "checks that.");

    public static SectionDescription SourceLinkAudit { get; } = new(
        "Whether each of the library's " + SourceLanguages + " can be "
        + "retrieved, either embedded in the PDB or through its SourceLink "
        + "URL.",
        "Portable PDB document records and SourceLink map, with one HTTP "
        + "HEAD request per uncached document URL.",
        "The section is not rendered when no portable PDB with a SourceLink "
        + "map is available; a failed audit is reported as a failure, not as "
        + "an empty result.",
        "A document counts as available when it is embedded or its HEAD "
        + "request succeeds without leaving the URL's origin; paths under "
        + "`artifacts/obj/` and URLs without an allowed scheme count as "
        + "missing. Successful checks are cached.");

    public static SectionDescription MissingSourceFiles { get; } = new(
        "The paths of the library's " + SourceLanguages + " that are neither "
        + "embedded in the PDB nor retrievable through their SourceLink URL.",
        "The same per-document HTTP HEAD pass as `SourceLink: "
        + "Availability`, over portable PDB document records.",
        "The section is not rendered when every checked document is "
        + "available, and also when the availability audit did not run or "
        + "failed; `SourceLink: Availability` tells these apart.",
        "Paths are the original document paths recorded in the PDB. A "
        + "document is missing when its HEAD request fails or leaves the "
        + "URL's origin, when it has no resolved URL, or when it is a build "
        + "artifact.");

    public static SectionDescription SourceIntegrity { get; } = new(
        "Whether the downloaded content of the library's " + SourceLanguages
        + " matches the checksums recorded in its portable PDB.",
        "Portable PDB document checksums and SourceLink map, with an HTTP "
        + "GET of each non-embedded document URL.",
        "The section is rendered only when the check completed; a missing "
        + "portable PDB or SourceLink map leaves it unrendered, and a failed "
        + "check is reported as a failure.",
        "A match after line-ending normalization counts as verified. "
        + "Documents with no URL or checksum, or whose download fails or "
        + "leaves the URL's origin, are unverifiable rather than mismatched; "
        + "embedded documents are not checked.");

    public static SectionDescription SourceLinkDiagnostics { get; } = new(
        "Problems in the library's SourceLink map: an error that makes the "
        + "whole map unusable, and document keys whose entries were "
        + "rejected.",
        "The SourceLink JSON map in a local, embedded, or already cached "
        + "portable PDB.",
        "The map parsed and every entry was accepted, or no SourceLink map "
        + "is present; the section is not rendered.",
        "The map is read without downloading a PDB or sending any request; "
        + "entries are checked against the SourceLink document-map schema, "
        + "not for whether their URLs resolve.");

    public static SectionDescription IdentifierConfusion { get; } = new(
        "Assembly names in the library's reference graph that contain "
        + "non-ASCII characters, and those whose leading characters imitate "
        + "the System, Microsoft, or Azure prefixes with Greek or Cyrillic "
        + "lookalikes.",
        "Metadata names: the assembly's own name, its assembly references, "
        + "and the names in its resolved transitive reference closure.",
        "Every inspected name is ASCII; the section is not rendered. A "
        + "reference traversal that fails is reported as a warning, not as "
        + "an empty result.",
        "Only assembly names are inspected, not type or member identifiers. "
        + "Detection is any non-ASCII character plus a deliberately small "
        + "homoglyph table, not the full Unicode confusables set, so "
        + "case-only and other ASCII lookalikes are not detected.");

    public static SectionDescription ArchitecturalFamilyTypes { get; } = new(
        "Each type definition in the selected population with its one- and "
        + "two-word suffix families, source disposition, signature reference "
        + "degrees, and structural role and pole: the per-type rows behind "
        + "Architectural Families.",
        "Metadata type definitions and declared member signatures, with "
        + "Portable PDB source provenance for the source-evidence "
        + "populations.",
        "No type definition is in the selected population; a failed "
        + "composition is reported as a failure, not as an empty result.",
        "Rows cover every type in the population, including types whose "
        + "name yields no suffix family. Roles come from declared "
        + "signatures, not method-body calls, and compiler-generated names "
        + "take part.");

    private const string BodyShapeBasis =
        "Compiled IL method bodies, decompiled and rendered as C#.";

    private const string BodyShapeEmpty =
        "With no bodies skipped, no searched body contains the requested "
        + "kind; when bodies could not be decompiled at full fidelity, the "
        + "search is reported as incomplete, not as an empty result.";

    public static SectionDescription BodyShapes { get; } = new(
        "Each occurrence of the requested C# syntax kind in the library's "
        + "decompiled public method bodies, with its member and exact text.",
        BodyShapeBasis,
        BodyShapeEmpty,
        "Matches are C# syntax recovered from IL, not the authored source. "
        + "Only full-fidelity bodies of public API members are searched.");

    public static SectionDescription BodyShapeSummary { get; } = new(
        "The requested C# syntax kind's occurrences in the library's "
        + "decompiled public method bodies, grouped by identical rendered "
        + "text with a count for each.",
        BodyShapeBasis,
        BodyShapeEmpty,
        "Grouping is by exact rendered text, so equivalent code that renders "
        + "differently forms separate groups. The summary covers the "
        + "complete search even when rows are windowed, with the same "
        + "fidelity and visibility limits as Body Shapes.");

    private const string ScanFailure =
        "a failed scan is reported under Inspection Failures, not as an empty result.";

    public static SectionDescription LibraryInfo { get; } = new(
        "Identity, build, and composition facts for the library, with counts "
        + "of its other inventories.",
        "Assembly manifest metadata and PE headers, host source provenance, "
        + "and counts from the library's metadata inventories.",
        "The section is present whenever the assembly could be read, and a "
        + "fact with no value is omitted. The async and extension method "
        + "counts are omitted when their analysis failed, and "
        + "Classified Methods says why; the other inventory counts report 0 "
        + "when their scan did not complete, so a 0 is read against "
        + "Inspection Failures.");

    public static SectionDescription InspectionFailures { get; } = new(
        "Scans run for this inspection that failed, with the section each "
        + "one feeds and the reason.",
        "The typed failure outcomes of the inspection's own queries and "
        + "scans.",
        "Every scan that ran completed; a scan that was not requested is "
        + "neither listed nor implied clean.",
        "A section whose scan failed is withheld rather than shown empty, so "
        + "a missing section elsewhere is read against this one.");

    public static SectionDescription Symbols { get; } = new(
        "Debug-symbol availability and provenance: PDB format and location, "
        + "symbol server, SourceLink presence, and package publisher and "
        + "signature status.",
        "The PE debug directory, an embedded, adjacent, cached, or downloaded "
        + "Portable PDB, and the NuGet package signature when a package is "
        + "inspected.",
        "The section always renders; an `Unknown` PDB format and location "
        + "mean no Portable PDB was found from the sources this request "
        + "allowed, and SourceLink states why it is absent.",
        "A PDB is downloaded from a symbol server only when this section is "
        + "selected explicitly or at detailed verbosity; otherwise only "
        + "embedded, adjacent, or already cached PDBs are read. Build path, "
        + "repository URL, and publisher are author-supplied values.");

    public static SectionDescription Signals { get; } = new(
        "A fixed set of audit answers about the library's provenance, "
        + "dependencies, trimming and AOT compatibility, memory safety, and "
        + "interop.",
        "The PE debug directory and PDB, the AssemblyRef table, assembly- "
        + "and member-level custom attributes, and method classification "
        + "counts.",
        "No signal could be computed, as for an image with no managed "
        + "metadata; a signal whose evidence could not be read shows "
        + "`Unavailable` or is left out rather than reporting a clean value.",
        "Rows come from a product-owned signal list, not from heuristics; "
        + "transitive reference rows appear only when a reference closure "
        + "was resolved.");

    public static SectionDescription Switches { get; } = new(
        "Feature, runtime host configuration, and AppContext switches the "
        + "library declares or reads.",
        "Metadata custom attributes (FeatureSwitchDefinitionAttribute on "
        + "properties, RuntimeHostConfigurationOptionAttribute on the "
        + "assembly or module) and compiled IL calls to "
        + "AppContext.TryGetSwitch and AppContext.SetSwitch.",
        "No declared switch attribute or AppContext switch call was found; "
        + ScanFailure,
        "AppContext switch names are taken from the most recent string "
        + "literal before the call, so names computed at run time are not "
        + "found. Names starting with `Switch.`, `TestSwitch.`, or "
        + "`System.Resources.UseSystemResourceKeys` are not reported.");

    public static SectionDescription Integrations { get; } = new(
        "The library's public types and extension methods recognized as "
        + "integrating with a .NET ecosystem concept, such as dependency "
        + "injection, logging, configuration, ASP.NET Core, Aspire, AI, or "
        + "OpenTelemetry.",
        "Metadata type definitions and decoded signatures of public static "
        + "extension methods on static extension classes.",
        "No public type or extension method matched a recognized concept; "
        + ScanFailure,
        "Recognition uses the product's integration concept catalog and "
        + "name and signature rules, not runtime behavior, over public and "
        + "nested public types. For some concepts, type rows are dropped "
        + "when matching API rows exist.");

    public static SectionDescription IntegrationOpportunities { get; } = new(
        "Public types whose shape suggests an ecosystem integration the "
        + "library does not already offer, with the API to look for.",
        "Metadata type definitions, compared against the concepts found in "
        + "Integrations.",
        "No public type matched an opportunity rule for a concept the "
        + "library lacks; a failed or rejected scan is reported under "
        + "Inspection Failures, not as an empty result.",
        "Opportunities are name-shape heuristics over cloud, database, "
        + "authentication, configuration, and AI client types, not checked "
        + "gaps; at most one row is kept per concept and integration kind.");

    public static SectionDescription References { get; } = new(
        "The library's direct assembly references, with version and public "
        + "key token.",
        "The metadata AssemblyRef table.",
        "An empty table means the library declares no assembly references; "
        + "a failed read is reported under Inspection Failures, not as an "
        + "empty result.",
        "Direct references only; Reference Hierarchy resolves them "
        + "transitively.");

    public static SectionDescription EcosystemDependencies { get; } = new(
        "The library's direct assembly references recognized as belonging "
        + "to a known .NET ecosystem, such as the runtime, "
        + "Microsoft.Extensions, ASP.NET Core, Aspire, AI, Blazor, or MAUI.",
        "The metadata AssemblyRef table, matched against the product's "
        + "ecosystem dependency recognition profile.",
        "No direct reference matched a recognized ecosystem; when references "
        + "could not be read, recognition is unavailable and the section is "
        + "not shown, rather than reporting that nothing matched.",
        "Matching is by exact assembly name or dotted name family, ignoring "
        + "version, against a product-owned profile, so unlisted ecosystems "
        + "are not recognized; no package graph or network is read.");

    public static SectionDescription ReferenceHierarchy { get; } = new(
        "The library's assembly references resolved transitively into a "
        + "rooted hierarchy of reference occurrences.",
        "The AssemblyRef tables of the library and of each resolved "
        + "assembly, bound to sibling assemblies in the referencing "
        + "assembly's directory or to the installed platform.",
        "An empty hierarchy means the library declares no assembly "
        + "references; references that could not be resolved are reported "
        + "as missing, and a depth limit marks the hierarchy as "
        + "depth-bounded.",
        "Binding ignores assembly version and does not expand NuGet packages "
        + "or deps.json, so it shows what is available locally, not a "
        + "restored package graph. A shared assembly appears once under each "
        + "parent that references it.");

    public static SectionDescription ExtensionMethods { get; } = new(
        "Extension methods and extension properties with public member "
        + "visibility that the library declares, grouped by extended type "
        + "with overload counts.",
        "Metadata type definitions and member signatures: static classes and "
        + "members marked with ExtensionAttribute, including C# extension "
        + "blocks.",
        "No visible extension member is declared; " + ScanFailure,
        "Visibility is checked on the member, not its class, so public "
        + "members of an internal extension class are listed. Non-public "
        + "members, and those whose class or member is hidden by "
        + "EditorBrowsable(Never) or a non-compiler Obsolete attribute, are "
        + "excluded; members whose signature cannot be decoded are skipped.");

    public static SectionDescription Resources { get; } = new(
        "Manifest resources the library declares, with visibility, whether "
        + "each is embedded or linked, and embedded size.",
        "The metadata ManifestResource table and the CLI header's resources "
        + "directory.",
        "The library declares no manifest resources; " + ScanFailure,
        "Public and private resources are both listed; Win32 resources are "
        + "not read.");

    public static SectionDescription CustomAttributes { get; } = new(
        "Assembly- and module-level custom attributes, with their display "
        + "values and AssemblyMetadata entries by key.",
        "Metadata custom attributes on the assembly and module definitions.",
        "No assembly- or module-level attribute remains after excluding the "
        + "well-known ones; " + ScanFailure,
        "Version, product, and company attributes already shown in Library "
        + "Info, and compiler or runtime infrastructure attributes, are "
        + "excluded; type and member attributes are not included.");

    public static SectionDescription UnionTypes { get; } = new(
        "Types the library marks as unions, with their kind, whether they "
        + "implement IUnion, and their case types.",
        "Metadata type definitions, interface implementations, and "
        + "constructor signatures.",
        "No type carries System.Runtime.CompilerServices.UnionAttribute; "
        + ScanFailure,
        "Case types are inferred from public single-parameter instance "
        + "constructors, not from a declared case list.");

    public static SectionDescription TypeForwarders { get; } = new(
        "Types the library forwards to another assembly, with the target "
        + "assembly.",
        "The metadata ExportedType table.",
        "The library forwards no types; " + ScanFailure);

    public static SectionDescription NonNormalizedPaths { get; } = new(
        "Build paths recorded in the library's debug information that expose "
        + "a local or CI directory instead of a deterministic root.",
        "The PE CodeView debug record and the SourceLink document map of an "
        + "embedded, adjacent, or cached Portable PDB.",
        "Every recorded path checked uses a deterministic root, and the "
        + "section is not shown; without a readable PDB only the CodeView "
        + "path is checked.",
        "A SourceLink map key counts as normalized when it starts with `/_/` "
        + "or `/_N/`; the CodeView path counts as normalized when it starts "
        + "with `/_/` or is a bare file name. This is a reproducibility "
        + "check, not a full determinism verdict.");
}
