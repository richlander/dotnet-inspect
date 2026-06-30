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

    /// <summary>Section for the decompiled C# method body with hidden-fact annotations and interleaved IL.</summary>
    public const string AnnotatedSource = "Annotated Source";

    /// <summary>Section for explicit inter-method cost annotations over the decompiled C# method body.</summary>
    public const string CostOverlay = "Cost Overlay";

    /// <summary>Section for explicit inter-method semantics/safety annotations over the decompiled C# method body.</summary>
    public const string SemanticsOverlay = "Semantics Overlay";

    /// <summary>Section for original method source code resolved via SourceLink.</summary>
    public const string OriginalSource = "Original Source";

    /// <summary>Section for SourceLink source file URLs for a type.</summary>
    public const string SourceFiles = "Source Files";

    /// <summary>Section for SourceLink source locations for member signatures.</summary>
    public const string SourceLocations = "Source Locations";

    /// <summary>Section for the raw IL disassembly.</summary>
    public const string IL = "IL";

    /// <summary>Section for resolving a MethodDef token + IL offset to source.</summary>
    public const string ILOffset = "IL Offset";

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

    /// <summary>Section for the bounded outbound call tree (callees) rooted at the selected member.</summary>
    public const string CallGraph = "Call Graph";

    /// <summary>Section for the bounded reverse call tree (callers) rooted at the selected member.</summary>
    public const string CallerGraph = "Caller Graph";

    /// <summary>Section for unsafe-relevant members in a type.</summary>
    public const string UnsafeMembers = "Unsafe Members";

    /// <summary>Type-level section ranking members by call-graph leverage (direct callers, fanout, depth, loop calls).</summary>
    public const string TopLeverage = "Top Leverage";

    /// <summary>Section for safe, local optimization opportunities inferred from IL/body evidence.</summary>
    public const string PerformanceTriage = "Performance Triage";

    /// <summary>Section for unsafe-relevant evidence from the selected member body.</summary>
    public const string UnsafeOperations = "Unsafe Operations";

}
