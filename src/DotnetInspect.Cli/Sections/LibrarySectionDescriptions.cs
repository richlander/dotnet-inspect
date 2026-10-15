using DotnetInspector.Sections;

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
        "Compiled IL method bodies: call, callvirt, newobj, ldftn, and "
        + "ldvirtftn sites.",
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
        + "targets as compiled, not virtual dispatch; unresolved calls or "
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
        "Delegates the compiler caches, such as non-capturing lambdas and "
        + "static method groups, are not reported.");

    public static SectionDescription PerformanceEnumerators { get; } = Opportunity(
        "Heap-allocated enumerators from a foreach over an interface-typed "
        + "sequence inside a loop.");

    public static SectionDescription PerformanceStrings { get; } = Opportunity(
        "Exact IL operations that can produce a string value.",
        "A row is exact evidence that an operation can produce a string, "
        + "not proof of a new allocation, retained bytes, frequency, or "
        + "that a change is safe.");

    public static SectionDescription PerformanceLoops { get; } = Opportunity(
        "Work repeated on every loop iteration that could be done once: "
        + "LINQ scans and materializations and string accumulation inside "
        + "loops, and sequence scans reached from a caller's loop or a "
        + "recursive traversal.");

    public static SectionDescription PerformanceHotspots { get; } = Opportunity(
        "Methods with many heap allocations in one loop and no more "
        + "specific candidate.",
        "A hotspot counts allocation sites, not measured cost, and some "
        + "sites may be required object construction.");

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
}
