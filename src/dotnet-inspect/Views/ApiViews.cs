using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for single-type rendering. Pre-computes all display values from ApiType + options.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class TypeView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutSection(Name = "Summary", Headless = true)]
    [JsonIgnore]
    public List<MarkoutField>? Summary { get; set; }

    // Top fields (rendered inline for -v:q compact summary only)
    [MarkoutSkipNull] public string? Kind { get; set; }
    [MarkoutSkipNull] public string? Modifiers { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Base")]
    public string? BaseType { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Type Parameters")]
    public string? TypeParametersInline { get; set; }

    [MarkoutIgnore] public string? Implements { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Library")]
    public string? Assembly { get; set; }

    [MarkoutSkipNull] public string? Package { get; set; }
    [MarkoutSkipNull] public string? Version { get; set; }
    [MarkoutSkipNull] public string? Source { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Samples")]
    public string? SamplesInfo { get; set; }

    // Member stats (quiet verbosity only)
    [MarkoutSkipNull] public int? Constructors { get; set; }
    [MarkoutSkipNull] public int? Fields { get; set; }
    [MarkoutSkipNull] public int? Properties { get; set; }
    [MarkoutSkipNull] public int? Methods { get; set; }
    [MarkoutSkipNull] public int? Operators { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Explicit Interface Implementations")]
    public int? ExplicitInterfaceImplementations { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Extension Methods")]
    public int? ExtensionMethods { get; set; }

    [MarkoutSkipNull] public int? Events { get; set; }

    /// <summary>
    /// Enum values without Description column (default).
    /// </summary>
    [MarkoutSection(Name = "Values", IgnoreProperty = nameof(EnumValueRow.Description))]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValues { get; set; }

    /// <summary>
    /// Enum values with Description column (--docs).
    /// </summary>
    [MarkoutSection(Name = "Values")]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValuesWithDocs { get; set; }

    /// <summary>
    /// Type parameters table (Normal+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Type Parameters")]
    [JsonIgnore]
    public List<TypeParameterRow>? TypeParameterRows { get; set; }

    /// <summary>
    /// Implemented interfaces (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Interfaces")]
    [JsonIgnore]
    public List<InterfaceRow>? InterfaceRows { get; set; }

    /// <summary>
    /// Base class hierarchy (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Baseclass")]
    [JsonIgnore]
    public List<BaseclassRow>? BaseclassRows { get; set; }

    // Member sections (populated by ApiOutputFormatter.PopulateMemberSections).
    // The historical Select variants are retained in the schema model but are no
    // longer populated; selectors live in the dedicated Member Index section.
    [MarkoutSection(Name = "Constructors", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? ConstructorRows { get; set; }
    [MarkoutSection(Name = "Constructors", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? ConstructorRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Fields", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? FieldRows { get; set; }
    [MarkoutSection(Name = "Fields", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? FieldRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Properties", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? PropertyRows { get; set; }
    [MarkoutSection(Name = "Properties", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? PropertyRowsWithDocs { get; set; }

    // Historical Select-column variants.
    [MarkoutSection(Name = "Constructors", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? ConstructorSelectRows { get; set; }
    [MarkoutSection(Name = "Constructors")]
    [JsonIgnore]
    public List<MemberRow>? ConstructorSelectRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Fields", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? FieldSelectRows { get; set; }
    [MarkoutSection(Name = "Fields")]
    [JsonIgnore]
    public List<MemberRow>? FieldSelectRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Properties", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? PropertySelectRows { get; set; }
    [MarkoutSection(Name = "Properties")]
    [JsonIgnore]
    public List<MemberRow>? PropertySelectRowsWithDocs { get; set; }
    // Constructor emphasis (--ctor mode, Normal+ verbosity)
    [MarkoutSection(Name = "Constructors")]
    [JsonIgnore]
    public List<ConstructorOverloadView>? ConstructorOverloads { get; set; }

    // Compact member summary sections (Minimal verbosity, matching old QuietMemberFormatter)
    [MarkoutSection(Name = "Constructors", IgnoreProperty = nameof(ConstructorSummaryRow.Overloads))]
    [JsonIgnore]
    public List<ConstructorSummaryRow>? ConstructorSummaryRows { get; set; }

    [MarkoutSection(Name = "Constructors")]
    [JsonIgnore]
    public List<ConstructorSummaryRow>? ConstructorSummaryRowsWithOverloads { get; set; }

    [MarkoutSection(Name = "Fields")]
    [JsonIgnore]
    public List<FieldSummaryRow>? FieldSummaryRows { get; set; }

    [MarkoutSection(Name = "Properties")]
    [JsonIgnore]
    public List<PropertySummaryRow>? PropertySummaryRows { get; set; }

    // Index mode sections (--index path)
    [MarkoutSection(Name = "Signature")]
    [JsonIgnore]
    public List<MemberSignatureRow>? SignatureRows { get; set; }

    [MarkoutSection(Name = "Custom Attributes")]
    [JsonIgnore]
    public List<MethodAttributeRow>? MethodAttributeRows { get; set; }

    [MarkoutSection(Name = "Unsafe Members")]
    [JsonIgnore]
    public List<UnsafeMemberRow>? UnsafeMemberRows { get; set; }

    // Member code sections (populated by member command only, serialized separately)
    [MarkoutIgnore]
    [JsonIgnore]
    public MemberCodeView? MemberCode { get; set; }

}

[MarkoutSerializable(AutoFields = false)]
public class EventsView
{
    [MarkoutSection(Name = "Events", IgnoreProperty = "Description,Select")]
    public List<MemberRow>? Rows { get; set; }

    [MarkoutSection(Name = "Events", IgnoreProperty = "Select")]
    public List<MemberRow>? RowsWithDocs { get; set; }

    [MarkoutSection(Name = "Events", IgnoreProperty = "Description")]
    public List<MemberRow>? SelectRows { get; set; }

    [MarkoutSection(Name = "Events")]
    public List<MemberRow>? SelectRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Events")]
    public List<EventSummaryRow>? SummaryRows { get; set; }
    [MarkoutIgnore]
    public bool HasRows =>
        Rows is { Count: > 0 }
        || RowsWithDocs is { Count: > 0 }
        || SelectRows is { Count: > 0 }
        || SelectRowsWithDocs is { Count: > 0 }
        || SummaryRows is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class MethodGroupsView
{
    [MarkoutSection(Name = "Method Groups", IgnoreProperty = nameof(MethodSummaryRow.Overloads))]
    public List<MethodSummaryRow>? Rows { get; set; }

    [MarkoutSection(Name = "Method Groups")]
    public List<MethodSummaryRow>? RowsWithOverloads { get; set; }

    [MarkoutIgnore]
    public bool HasRows => Rows is { Count: > 0 } || RowsWithOverloads is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class MethodsView
{
    [MarkoutSection(Name = "Methods", IgnoreProperty = "Description,Select")]
    public List<MemberRow>? Rows { get; set; }

    [MarkoutSection(Name = "Methods", IgnoreProperty = "Select")]
    public List<MemberRow>? RowsWithDocs { get; set; }

    [MarkoutSection(Name = "Methods", IgnoreProperty = "Description")]
    public List<MemberRow>? SelectRows { get; set; }

    [MarkoutSection(Name = "Methods")]
    public List<MemberRow>? SelectRowsWithDocs { get; set; }
    [MarkoutIgnore]
    public bool HasRows =>
        Rows is { Count: > 0 }
        || RowsWithDocs is { Count: > 0 }
        || SelectRows is { Count: > 0 }
        || SelectRowsWithDocs is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class MemberIndexView
{
    [MarkoutSection(Name = SectionNames.MemberIndex)]
    public List<MemberIndexRow>? Rows { get; set; }

    [MarkoutIgnore]
    public bool HasRows => Rows is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class OperatorsView
{
    [MarkoutSection(Name = "Operators", IgnoreProperty = "Description,Select")]
    public List<MemberRow>? Rows { get; set; }

    [MarkoutSection(Name = "Operators", IgnoreProperty = "Select")]
    public List<MemberRow>? RowsWithDocs { get; set; }

    [MarkoutSection(Name = "Operators", IgnoreProperty = "Description")]
    public List<MemberRow>? SelectRows { get; set; }

    [MarkoutSection(Name = "Operators")]
    public List<MemberRow>? SelectRowsWithDocs { get; set; }
    [MarkoutIgnore]
    public bool HasRows =>
        Rows is { Count: > 0 }
        || RowsWithDocs is { Count: > 0 }
        || SelectRows is { Count: > 0 }
        || SelectRowsWithDocs is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class ExplicitInterfaceImplementationsView
{
    [MarkoutSection(Name = "Explicit Interface Implementations", IgnoreProperty = "Description,Select")]
    public List<MemberRow>? Rows { get; set; }

    [MarkoutSection(Name = "Explicit Interface Implementations", IgnoreProperty = "Select")]
    public List<MemberRow>? RowsWithDocs { get; set; }

    [MarkoutSection(Name = "Explicit Interface Implementations", IgnoreProperty = "Description")]
    public List<MemberRow>? SelectRows { get; set; }

    [MarkoutSection(Name = "Explicit Interface Implementations")]
    public List<MemberRow>? SelectRowsWithDocs { get; set; }
    [MarkoutIgnore]
    public bool HasRows =>
        Rows is { Count: > 0 }
        || RowsWithDocs is { Count: > 0 }
        || SelectRows is { Count: > 0 }
        || SelectRowsWithDocs is { Count: > 0 };
}

[MarkoutSerializable(AutoFields = false)]
public class ExtensionMethodsView
{
    [MarkoutSection(Name = "Extension Methods", IgnoreProperty = "Description,Select")]
    public List<MemberRow>? Rows { get; set; }

    [MarkoutSection(Name = "Extension Methods", IgnoreProperty = "Select")]
    public List<MemberRow>? RowsWithDocs { get; set; }

    [MarkoutSection(Name = "Extension Methods", IgnoreProperty = "Description")]
    public List<MemberRow>? SelectRows { get; set; }

    [MarkoutSection(Name = "Extension Methods")]
    public List<MemberRow>? SelectRowsWithDocs { get; set; }
    [MarkoutIgnore]
    public bool HasRows =>
        Rows is { Count: > 0 }
        || RowsWithDocs is { Count: > 0 }
        || SelectRows is { Count: > 0 }
        || SelectRowsWithDocs is { Count: > 0 };
}

[MarkoutSerializable]
public class EnumValueRow
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

[MarkoutSerializable]
public class TypeParameterRow
{
    public string Parameter { get; set; } = "";
    public string Constraints { get; set; } = "";
}

[MarkoutSerializable]
public class InterfaceRow
{
    public string Interface { get; set; } = "";
}

[MarkoutSerializable]
public class BaseclassRow
{
    public string Type { get; set; } = "";
}

/// <summary>
/// View model for full API surface rendering (all types in an assembly).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Name), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class CliApiSurface
{
    [MarkoutIgnore] public string? Name { get; set; }
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutSkipNull] public string? Library { get; set; }
    public int Types { get; set; }
    public int Methods { get; set; }
    public int Properties { get; set; }
    [MarkoutSkipNull] public string? Source { get; set; }
    [MarkoutSkipNull] public string? Version { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    // Type forwarders (edge case: types == 0 with forwarders)
    [MarkoutSection(Name = "Type Forwarders")]
    public List<ForwarderSummaryRow>? TypeForwarders { get; set; }

    // Per-kind type sections (5 kinds × with/without docs)
    [MarkoutSection(Name = "Classes", IgnoreProperty = "Kind,Description")]
    public List<TypeSummaryRow>? Classes { get; set; }
    [MarkoutSection(Name = "Classes")]
    public List<TypeSummaryRow>? ClassesWithDocs { get; set; }

    [MarkoutSection(Name = "Structs", IgnoreProperty = "Kind,Description")]
    public List<TypeSummaryRow>? Structs { get; set; }
    [MarkoutSection(Name = "Structs")]
    public List<TypeSummaryRow>? StructsWithDocs { get; set; }

    [MarkoutSection(Name = "Interfaces", IgnoreProperty = "Kind,Description")]
    public List<TypeSummaryRow>? Interfaces { get; set; }
    [MarkoutSection(Name = "Interfaces")]
    public List<TypeSummaryRow>? InterfacesWithDocs { get; set; }

    [MarkoutSection(Name = "Enums", IgnoreProperty = "Kind,Description")]
    public List<TypeSummaryRow>? Enums { get; set; }
    [MarkoutSection(Name = "Enums")]
    public List<TypeSummaryRow>? EnumsWithDocs { get; set; }

    [MarkoutSection(Name = "Delegates", IgnoreProperty = "Kind,Description")]
    public List<TypeSummaryRow>? Delegates { get; set; }
    [MarkoutSection(Name = "Delegates")]
    public List<TypeSummaryRow>? DelegatesWithDocs { get; set; }
}

[MarkoutSerializable]
public record TypeSummaryRow(string Kind, string Type, string Members, string? Description);

[MarkoutSerializable]
public record ForwarderSummaryRow(
    [property: MarkoutPropertyName("Target Library")] string TargetLibrary,
    string Types);

[MarkoutSerializable]
public record MemberRow(
    [property: MarkoutSkipNull] string? Select,
    string Name,
    string Signature,
    string? Description)
{
    /// <summary>
    /// Creates a MemberRow without Select column.
    /// </summary>
    public MemberRow(string name, string signature, string? description)
        : this(null, name, signature, description) { }
}

[MarkoutSerializable]
public record MemberIndexRow(
    string Selector,
    string Stable,
    [property: MarkoutPropertyName("Canonical Signature")] string CanonicalSignature,
    [property: MarkoutIgnore] string Digest);

[MarkoutSerializable]
public record MemberSignatureRow(
    string Signature,
    [property: MarkoutSkipNull] string? Description);

/// <summary>
/// Compact summary row for Minimal verbosity: one row per unique member name with overload count.
/// </summary>
[MarkoutSerializable]
public record MethodSummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    string Overloads);

[MarkoutSerializable]
public record ConstructorSummaryRow(string Name, string Overloads);

[MarkoutSerializable]
public record PropertySummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    string Accessors);

[MarkoutSerializable]
public record FieldSummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType);

[MarkoutSerializable]
public record EventSummaryRow(string Name, string Type);

[MarkoutSerializable]
public record MethodAttributeRow(string Name, string Value);

/// <summary>
/// View model for constructor emphasis (--ctor mode).
/// Each overload renders as a subheading with a code block and parameter table.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class ConstructorOverloadView
{
    [MarkoutIgnore] public string Title { get; set; } = "";

    [MarkoutIgnoreInTable]
    public CodeSection Signature { get; set; }

    [MarkoutSection(Name = "Parameters")]
    public List<ConstructorParameterRow>? Parameters { get; set; }
}

[MarkoutSerializable]
public record ConstructorParameterRow(string Parameter, string Type, string Notes);

/// <summary>
/// View model for type shape output (--shape).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(FullName))]
public class TypeShapeView
{
    [MarkoutIgnore]
    public string FullName { get; set; } = "";

    public string Kind { get; set; } = "";

    [MarkoutSkipNull]
    public string? Modifiers { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Library")]
    public string? Assembly { get; set; }

    [MarkoutSkipNull]
    public string? Package { get; set; }

    [MarkoutSkipNull]
    public string? Version { get; set; }

    [MarkoutIgnoreInTable]
    public List<TreeNode> Members { get; set; } = [];
}

/// <summary>
/// View model for tabular single-type output: one unified table of all members.
/// </summary>
[MarkoutSerializable]
public class ApiTypeOneLineView
{
    [MarkoutSection(Name = "Members")]
    public List<ApiOneLineRow>? Rows { get; set; }
}

/// <summary>
/// View model for tabular full-API output: one unified table of all types.
/// </summary>
[MarkoutSerializable]
public class ApiSurfaceOneLineView
{
    [MarkoutSection(Name = "Types", IgnoreProperty = nameof(ApiSurfaceOneLineRow.Description))]
    public List<ApiSurfaceOneLineRow>? Rows { get; set; }

    [MarkoutSection(Name = "Types")]
    public List<ApiSurfaceOneLineRow>? RowsWithDescription { get; set; }
}

[MarkoutSerializable]
public record ApiOneLineRow(string Kind, string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    string Detail);

[MarkoutSerializable]
public record ApiSurfaceOneLineRow(string Kind, string Type, string Members, string? Description);

/// <summary>
/// Code sections for member command output (Decompiled Source, Annotated Source, Original Source, IL).
/// Serialized separately after the main TypeView.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public class MemberCodeView
{
    [MarkoutSection(Name = "Decompiled Source")]
    public CodeSection DecompiledSourceCode { get; set; }

    [MarkoutSection(Name = "Annotated Source")]
    public CodeSection AnnotatedSourceCode { get; set; }

    [MarkoutSection(Name = "Original Source")]
    public CodeSection OriginalSourceCode { get; set; }

    [MarkoutSection(Name = "Calls", EmptyText = "No calls to other methods found in this method body.")]
    public List<CallSiteRow>? CallRows { get; set; }

    [MarkoutSection(Name = "Callers", EmptyText = "No callers found in this assembly.")]
    [MarkoutIgnoreColumnWhen(nameof(CallerSourceIsUniform), nameof(CallerSiteRow.Source))]
    public List<CallerSiteRow>? CallerRows { get; set; }

    /// <summary>
    /// Hides the Callers "Source" column when every caller comes from a single assembly
    /// (the default single-assembly scan), keeping that output unchanged. The column appears
    /// only when a caller scope (<c>--bin</c>/<c>--project</c>) brings in additional assemblies.
    /// </summary>
    public static bool CallerSourceIsUniform(List<CallerSiteRow>? rows)
        => rows is null || rows.Select(r => r.Source).Distinct(StringComparer.Ordinal).Count() <= 1;

    [MarkoutSection(Name = "Call Graph", EmptyText = "No outbound calls found in this method body.")]
    public List<TreeNode>? CallGraphNodes { get; set; }

    [MarkoutSection(Name = "Unsafe Operations", EmptyText = "No unsafe operations found in this method body.")]
    public List<UnsafeOperationRow>? UnsafeOperationRows { get; set; }

    [MarkoutSection(Name = "Facts", EmptyText = "No hidden facts found in this method body.")]
    public List<FactRow>? FactRows { get; set; }

    [MarkoutSection(Name = "Recovered IL")]
    public CodeSection ILCode { get; set; }

    [MarkoutSection(Name = "IR (Stages)")]
    public CodeSection IRStages { get; set; }
}

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(CliApiSurface))]
[MarkoutContext(typeof(TypeView))]
[MarkoutContext(typeof(EventsView))]
[MarkoutContext(typeof(MethodGroupsView))]
[MarkoutContext(typeof(MethodsView))]
[MarkoutContext(typeof(MemberIndexView))]
[MarkoutContext(typeof(OperatorsView))]
[MarkoutContext(typeof(ExplicitInterfaceImplementationsView))]
[MarkoutContext(typeof(ExtensionMethodsView))]
[MarkoutContext(typeof(MemberCodeView))]
[MarkoutContext(typeof(CallSiteRow))]
[MarkoutContext(typeof(CallerSiteRow))]
[MarkoutContext(typeof(UnsafeOperationRow))]
[MarkoutContext(typeof(FactRow))]
[MarkoutContext(typeof(TypeSummaryRow))]
[MarkoutContext(typeof(ForwarderSummaryRow))]
[MarkoutContext(typeof(MemberRow))]
[MarkoutContext(typeof(MemberSignatureRow))]
[MarkoutContext(typeof(MethodAttributeRow))]
[MarkoutContext(typeof(UnsafeMemberRow))]
[MarkoutContext(typeof(ConstructorOverloadView))]
[MarkoutContext(typeof(ConstructorParameterRow))]
[MarkoutContext(typeof(EnumValueRow))]
[MarkoutContext(typeof(TypeParameterRow))]
[MarkoutContext(typeof(InterfaceRow))]
[MarkoutContext(typeof(BaseclassRow))]
[MarkoutContext(typeof(ApiTypeOneLineView))]
[MarkoutContext(typeof(ApiOneLineRow))]
[MarkoutContext(typeof(ApiSurfaceOneLineView))]
[MarkoutContext(typeof(ApiSurfaceOneLineRow))]
[MarkoutContext(typeof(SampleRow))]
public partial class ApiViewContext : MarkoutSerializerContext
{
}

[MarkoutSerializable]
public record CallSiteRow(string Callee, string Kind, string IL, string Token);

[MarkoutSerializable]
public record CallerSiteRow(string Source, string Caller, string Kind, string IL, string Token);

[MarkoutSerializable]
public record UnsafeOperationRow(
    string Reason,
    string Detail,
    string Kind,
    [property: MarkoutSkipNull] string? IL,
    [property: MarkoutSkipNull] string? Token);

[MarkoutSerializable]
public record FactRow(
    string Id,
    string Category,
    [property: MarkoutSkipNull] string? Detail,
    string Conditionality,
    [property: MarkoutSkipNull] string? IL);

[MarkoutSerializable]
public record UnsafeMemberRow(
    string Member,
    string Reason,
    string Detail,
    string Kind,
    [property: MarkoutSkipNull] string? IL,
    [property: MarkoutSkipNull] string? Token);
