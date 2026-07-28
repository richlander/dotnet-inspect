namespace DotnetInspector.Sections;

/// <summary>
/// Constants for section names used in the api command output.
/// </summary>
public static class SectionNames
{
    /// <summary>Headless compact context section.</summary>
    public const string Summary = "Summary";

    // ===== Type Sections (ApiTypeSectionDescriptors) =====

    /// <summary>Section for class types.</summary>
    public const string Classes = "Classes";

    /// <summary>Section for struct types.</summary>
    public const string Structs = "Structs";

    /// <summary>Section for interface types.</summary>
    public const string Interfaces = "Interfaces";

    /// <summary>Section for enum types.</summary>
    public const string Enums = "Enums";

    /// <summary>Section for delegate types.</summary>
    public const string Delegates = "Delegates";

    // ===== Member Sections (ApiMemberSectionDescriptors) =====

    /// <summary>Section for enum values.</summary>
    public const string Values = "Values";

    /// <summary>Section for a single selected member signature.</summary>
    public const string Signature = "Signature";

    /// <summary>Section for type parameters.</summary>
    public const string TypeParameters = "Type Parameters";

    /// <summary>Section for interfaces implemented by a type.</summary>
    public const string TypeInterfaces = "Interfaces";

    /// <summary>Section for base class.</summary>
    public const string Baseclass = "Baseclass";

    /// <summary>Section for remote source file links (SourceLink).</summary>
    public const string RemoteSource = "Remote Source";

    /// <summary>Section for constructors.</summary>
    public const string Constructors = "Constructors";

    /// <summary>Section for the class finalizer (destructor).</summary>
    public const string Finalizer = "Finalizer";

    /// <summary>Section for fields.</summary>
    public const string Fields = "Fields";

    /// <summary>Section for properties.</summary>
    public const string Properties = "Properties";

    /// <summary>Section for logical method-name groups.</summary>
    public const string MethodGroups = "Method Groups";

    /// <summary>Section for method overload rows.</summary>
    public const string Methods = "Methods";

    /// <summary>Section for copyable member selectors and canonical signatures.</summary>
    public const string MemberIndex = "Member Index";

    /// <summary>Section for operators.</summary>
    public const string Operators = "Operators";

    /// <summary>Section for explicit interface implementations.</summary>
    public const string ExplicitInterfaceImplementations = "Explicit Interface Implementations";

    /// <summary>Section for extension methods defined in the inspected binary.</summary>
    public const string ExtensionMethods = "Extension Methods";

    /// <summary>Section for events.</summary>
    public const string Events = "Events";

    /// <summary>Section for custom attributes on methods.</summary>
    public const string CustomAttributes = "Custom Attributes";

    /// <summary>Section for the decompiled C# method body.</summary>
    public const string DecompiledSource = "Decompiled Source";

    /// <summary>Section for typed causes that prevent Full decompiler fidelity.</summary>
    public const string FidelityCauses = "Fidelity Causes";

    /// <summary>Section listing the configurable style choices the decompiler recorded while rendering this member, each tagged byte-preserving or byte-divergent. Primarily surfaces the opt-in byte-divergent style lenses (the #3127 signal). Some byte-preserving knobs (e.g. this.-qualification) are not yet recorded; see issue #3156.</summary>
    public const string AppliedTaste = "Applied Taste";

    /// <summary>Section for the decompiled C# method body with hidden-fact annotations and interleaved IL.</summary>
    public const string AnnotatedSource = "Annotated Source";

    /// <summary>Section for explicit inter-method cost annotations over the decompiled C# method body.</summary>
    public const string CostOverlay = "Cost Overlay";

    /// <summary>Section for explicit inter-method semantics/safety annotations over the decompiled C# method body.</summary>
    public const string SemanticsOverlay = "Semantics Overlay";

    /// <summary>Section for original method source code resolved via SourceLink.</summary>
    public const string OriginalSource = "Original Source";

    /// <summary>Section for a line diff between original and decompiled method source.</summary>
    public const string SourceDiff = "Source Diff";

    /// <summary>Section for SourceLink source file URLs for a type (API/package scope).</summary>
    public const string SourceFiles = "Source Files";

    /// <summary>
    /// SourceLink source file listing (library scope). Part of the <c>@SourceLink</c> family;
    /// prefixed <c>SourceLink:</c> to match the category door so the family sorts together.
    /// </summary>
    public const string SourceLinkFiles = "SourceLink: Files";

    /// <summary>
    /// SourceLink availability audit (library scope): per-source-file HEAD probe results. Part of
    /// the <c>@SourceLink</c> family; prefixed <c>SourceLink:</c> to match the category door.
    /// </summary>
    public const string SourceLinkAvailability = "SourceLink: Availability";

    /// <summary>SourceLink audit rows for source files that failed their availability probe.</summary>
    public const string SourceLinkMissingFiles = "SourceLink: Missing Files";

    /// <summary>SourceLink integrity audit (library scope): content hash verification of sources.</summary>
    public const string SourceLinkIntegrity = "SourceLink: Integrity";

    /// <summary>Section for SourceLink source locations for member signatures.</summary>
    public const string SourceLocations = "Source Locations";

    /// <summary>Section for the raw IL disassembly.</summary>
    public const string IL = "IL";

    /// <summary>Section for resolving a MethodDef token + IL offset to source.</summary>
    public const string ILOffset = "Context: Source Location";

    /// <summary>Section for resolving a MethodDef token + IL offset to its owning member/type.</summary>
    public const string MemberContext = "Context: Member";

    /// <summary>Section for resolving a MethodDef token + IL offset to its exact IL instruction.</summary>
    public const string InstructionContext = "Context: Instruction";

    /// <summary>Section for resolving a MethodDef token + IL offset to containing exception regions.</summary>
    public const string ExceptionContext = "Context: Exception";

    /// <summary>Section for exception regions contained by a selected member body.</summary>
    public const string ExceptionRegions = "Exception Regions";

    /// <summary>Section for resolving a MethodDef token + IL offset to a call-like instruction.</summary>
    public const string CallsiteContext = "Context: Callsite";

    /// <summary>Section for resolving a MethodDef token + IL offset to the preceding call instruction.</summary>
    public const string ReturnAddressContext = "Context: Return Address";

    /// <summary>Section for allocation facts at an exact IL coordinate.</summary>
    public const string AllocationContext = "Context: Allocation";

    /// <summary>Section for safety facts at an exact IL coordinate.</summary>
    public const string SafetyContext = "Context: Safety";

    /// <summary>Section for objective cost facts at an exact IL coordinate.</summary>
    public const string CostContext = "Context: Cost";

    /// <summary>
    /// Section for the structured hidden-fact table: the same annotations the
    /// Decompiled Source view renders inline, as rows (id, category, detail, IL
    /// offset) for agents to consume via --json/--tsv/--table.
    /// </summary>
    public const string Facts = "Facts";

    /// <summary>Section for direct call-site evidence from the selected member body.</summary>
    public const string Calls = "Calls";

    /// <summary>Section for callers (reverse call edges) of the selected member within the assembly.</summary>
    public const string Callers = "Callers";

    /// <summary>Type-level aggregate of distinct callee types touched by members on the selected type.</summary>
    public const string CalledTypes = "Called Types";

    /// <summary>Section for objective allocation facts.</summary>
    public const string AllocationFacts = "Allocation Facts";

    /// <summary>Section for objective safety facts.</summary>
    public const string SafetyFacts = "Safety Facts";

    /// <summary>Section for objective cost facts.</summary>
    public const string CostFacts = "Cost Facts";

    /// <summary>
    /// Section for the bidirectional call graph centred on the selected member: bounded inbound
    /// callers and bounded outbound callees in one shape.
    /// </summary>
    public const string CallGraph = "Call Graph";

    /// <summary>Section for unsafe-relevant members in a type.</summary>
    public const string UnsafeMembers = "Unsafe Members";

    /// <summary>Type-level section ranking members by call-graph leverage (direct callers, fanout, depth, loop calls).</summary>
    public const string TopLeverage = "Top Leverage";

    /// <summary>Section for safe, local optimization opportunities inferred from IL/body evidence.</summary>
    public const string PerformanceTriage = "Performance Triage";

    // Kind-scoped performance sections (library scope). Each renders one family of the
    // optimization-opportunity scan, is explicit-only, and is absent when its family has no
    // findings — following the il-offset context-section model. Grouped under @Performance.
    /// <summary>Boxing (value type heap-allocated) performance findings.</summary>
    public const string PerformanceBoxing = "Performance: Boxing";

    /// <summary>Array-allocation performance findings (small arrays, byte-copies, span/stackalloc candidates).</summary>
    public const string PerformanceArrays = "Performance: Arrays";

    /// <summary>Closure/delegate-allocation performance findings.</summary>
    public const string PerformanceClosures = "Performance: Closures and Delegates";

    /// <summary>Enumerator-allocation performance findings.</summary>
    public const string PerformanceEnumerators = "Performance: Enumerators";

    /// <summary>Loop hot-path performance findings (scan/materialize/build inside loops).</summary>
    public const string PerformanceLoops = "Performance: Loop Hot Paths";

    /// <summary>Aggregate allocation hotspot/fanout performance findings.</summary>
    public const string PerformanceHotspots = "Performance: Allocation Hotspots";

    /// <summary>Async state-machine performance findings.</summary>
    public const string PerformanceAsync = "Performance: Async";

    /// <summary>Catch-all for performance findings whose shape has no dedicated section (keeps the scan non-lossy).</summary>
    public const string PerformanceOther = "Performance: Other";

    /// <summary>
    /// Escape/exception-safety section for the array-pool shape: an <c>ArrayPool&lt;T&gt;.Shared.Rent</c>
    /// that is not returned on an exception path. Named for the specific resource because the
    /// analysis is array-pool-specific today. Unprefixed: a prefix marks an exclusive family with
    /// a matching category door, and this section has neither.
    /// </summary>
    public const string ArrayPoolEscapes = "Array Pool Escapes";

    /// <summary>Section for unsafe-relevant evidence from the selected member body.</summary>
    public const string UnsafeOperations = "Unsafe Operations";

    // ===== Library-scope sections (LibrarySections) =====
    // Declared here rather than as descriptor-local literals so that category membership in
    // LibrarySections.AddCategory references the same symbol the descriptor's Name returns. A
    // rename then moves both together instead of silently dropping the section from its category.

    /// <summary>Section for the assembly identity header.</summary>
    public const string LibraryInfo = "Library Info";

    /// <summary>Section for scans that failed, so a partial inspection never reads as a clean one.</summary>
    public const string InspectionFailures = "Inspection Failures";

    /// <summary>Section for debug symbol availability and provenance.</summary>
    public const string Symbols = "Symbols";

    /// <summary>Section summarizing high-value audit answers across the assembly.</summary>
    public const string Signals = "Signals";

    /// <summary>Section for AppContext feature switches.</summary>
    public const string Switches = "Switches";

    /// <summary>Section for direct assembly references.</summary>
    public const string References = "References";

    /// <summary>Section for transitive assembly dependencies.</summary>
    public const string Dependencies = "Dependencies";

    /// <summary>Section for P/Invoke declarations.</summary>
    public const string PInvokeMethods = "P/Invoke Methods";

    /// <summary>Section for async methods.</summary>
    public const string AsyncMethods = "Async Methods";

    /// <summary>Section for embedded resources.</summary>
    public const string Resources = "Resources";

    /// <summary>Section for union types.</summary>
    public const string UnionTypes = "Union Types";

    /// <summary>Section for type forwarders.</summary>
    public const string TypeForwarders = "Type Forwarders";

    /// <summary>Section for build paths that leak non-deterministic local or CI directories.</summary>
    public const string NonNormalizedPaths = "Non-normalized Paths";

}
