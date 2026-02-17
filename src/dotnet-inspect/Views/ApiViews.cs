using System.Text.Json.Serialization;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for single-type rendering. Pre-computes all display values from ApiType + options.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class ApiTypeView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutValueMap("class=📦", "struct=🔲", "interface=🔌", "enum=🔢", "delegate=⚡")]
    public string Kind { get; set; } = "";

    [MarkoutSkipNull]
    public string? Modifiers { get; set; }

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

    [MarkoutSkipNull]
    public string? Package { get; set; }

    [MarkoutSkipNull]
    public string? Version { get; set; }

    [MarkoutSkipNull]
    public string? Source { get; set; }

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

    /// <summary>
    /// Source files (populated via SourceLink). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Remote Source")]
    [JsonIgnore]
    public List<SourceRow>? SourceRows { get; set; }

    // Member sections (populated by ApiOutputFormatter.PopulateMemberSections)
    // Without --select: hide Select column
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

    [MarkoutSection(Name = "Methods", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? MethodRows { get; set; }
    [MarkoutSection(Name = "Methods", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? MethodRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Events", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? EventRows { get; set; }
    [MarkoutSection(Name = "Events", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? EventRowsWithDocs { get; set; }

    // With --select: show Select column
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

    [MarkoutSection(Name = "Methods", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? MethodSelectRows { get; set; }
    [MarkoutSection(Name = "Methods")]
    [JsonIgnore]
    public List<MemberRow>? MethodSelectRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Events", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? EventSelectRows { get; set; }
    [MarkoutSection(Name = "Events")]
    [JsonIgnore]
    public List<MemberRow>? EventSelectRowsWithDocs { get; set; }

    // Constructor emphasis (--ctor mode, Normal+ verbosity)
    [MarkoutSection(Name = "Constructors")]
    [JsonIgnore]
    public List<ConstructorOverloadView>? ConstructorOverloads { get; set; }

    // Index mode sections (--index path)
    [MarkoutSection(Name = "Custom Attributes")]
    [JsonIgnore]
    public List<MethodAttributeRow>? MethodAttributeRows { get; set; }

    [MarkoutSection(Name = "Source")]
    [MarkoutIgnoreInTable]
    [JsonIgnore]
    public CodeSection SourceCode { get; set; }

    [MarkoutSection(Name = "Lowered C#")]
    [MarkoutIgnoreInTable]
    [JsonIgnore]
    public CodeSection LoweredCSharp { get; set; }

    [MarkoutSection(Name = "IL")]
    [MarkoutIgnoreInTable]
    [JsonIgnore]
    public CodeSection ILCode { get; set; }

    [MarkoutSection(Name = "IL (Annotated)")]
    [MarkoutIgnoreInTable]
    [JsonIgnore]
    public CodeSection AnnotatedIL { get; set; }

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

[MarkoutSerializable]
public class SourceRow
{
    public string File { get; set; } = "";

    [MarkoutSkipNull]
    public string? Url { get; set; }
}

/// <summary>
/// View model for full API surface rendering (all types in an assembly).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Name), DescriptionProperty = nameof(Description))]
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
    [MarkoutSection(Name = "Classes", IgnoreProperty = nameof(TypeSummaryRow.Description))]
    public List<TypeSummaryRow>? Classes { get; set; }
    [MarkoutSection(Name = "Classes")]
    public List<TypeSummaryRow>? ClassesWithDocs { get; set; }

    [MarkoutSection(Name = "Structs", IgnoreProperty = nameof(TypeSummaryRow.Description))]
    public List<TypeSummaryRow>? Structs { get; set; }
    [MarkoutSection(Name = "Structs")]
    public List<TypeSummaryRow>? StructsWithDocs { get; set; }

    [MarkoutSection(Name = "Interfaces", IgnoreProperty = nameof(TypeSummaryRow.Description))]
    public List<TypeSummaryRow>? Interfaces { get; set; }
    [MarkoutSection(Name = "Interfaces")]
    public List<TypeSummaryRow>? InterfacesWithDocs { get; set; }

    [MarkoutSection(Name = "Enums", IgnoreProperty = nameof(TypeSummaryRow.Description))]
    public List<TypeSummaryRow>? Enums { get; set; }
    [MarkoutSection(Name = "Enums")]
    public List<TypeSummaryRow>? EnumsWithDocs { get; set; }

    [MarkoutSection(Name = "Delegates", IgnoreProperty = nameof(TypeSummaryRow.Description))]
    public List<TypeSummaryRow>? Delegates { get; set; }
    [MarkoutSection(Name = "Delegates")]
    public List<TypeSummaryRow>? DelegatesWithDocs { get; set; }
}

[MarkoutSerializable]
public record TypeSummaryRow(string Type, string Members, string? Description);

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
    /// Creates a MemberRow without a Select column.
    /// </summary>
    public MemberRow(string name, string signature, string? description)
        : this(null, name, signature, description) { }
}

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

    [MarkoutValueMap("class=📦", "struct=🔲", "interface=🔌", "enum=🔢", "delegate=⚡")]
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

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}
