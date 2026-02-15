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
    [MarkoutSection(Name = "Constructors", IgnoreProperty = nameof(MemberRow.Description))]
    [JsonIgnore]
    public List<MemberRow>? ConstructorRows { get; set; }
    [MarkoutSection(Name = "Constructors")]
    [JsonIgnore]
    public List<MemberRow>? ConstructorRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Fields", IgnoreProperty = nameof(MemberRow.Description))]
    [JsonIgnore]
    public List<MemberRow>? FieldRows { get; set; }
    [MarkoutSection(Name = "Fields")]
    [JsonIgnore]
    public List<MemberRow>? FieldRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Properties", IgnoreProperty = nameof(MemberRow.Description))]
    [JsonIgnore]
    public List<MemberRow>? PropertyRows { get; set; }
    [MarkoutSection(Name = "Properties")]
    [JsonIgnore]
    public List<MemberRow>? PropertyRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Methods", IgnoreProperty = nameof(MemberRow.Description))]
    [JsonIgnore]
    public List<MemberRow>? MethodRows { get; set; }
    [MarkoutSection(Name = "Methods")]
    [JsonIgnore]
    public List<MemberRow>? MethodRowsWithDocs { get; set; }

    [MarkoutSection(Name = "Events", IgnoreProperty = nameof(MemberRow.Description))]
    [JsonIgnore]
    public List<MemberRow>? EventRows { get; set; }
    [MarkoutSection(Name = "Events")]
    [JsonIgnore]
    public List<MemberRow>? EventRowsWithDocs { get; set; }

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
public record MemberRow(string Name, string Signature, string? Description);

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

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}
