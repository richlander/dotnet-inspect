using ILInspector.CSharp;
using System.Text.Json.Serialization;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspector.Views;

internal static class ApiViewText
{
    public static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    public static InertString? OptionalField(string? value) =>
        value is null ? null : Field(value);
}

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

    [MarkoutIgnore]
    [JsonIgnore]
    public string? SourceUrl { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? SourceFilePath { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal byte[]? SourceChecksum { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? SourceChecksumAlgorithm { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    public List<PartialSourceFileInfo>? AdditionalSourceFiles { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    public bool CallGraphIncomplete { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Samples")]
    public string? SamplesInfo { get; set; }

    // Member stats (quiet verbosity only)
    [MarkoutSkipNull] public int? Constructors { get; set; }
    [MarkoutSkipNull] public int? Finalizer { get; set; }
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
    /// Type identity fact table. Unlike every other section on this view, the row set does not
    /// grow with the type: it answers "what is this type" rather than listing its members, so it
    /// is the same shape for <c>System.String</c> and for a one-member struct.
    /// </summary>
    [MarkoutSection(Name = "Type Info")]
    [JsonIgnore]
    public TypeInfoSection? TypeInfo { get; set; }

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

    [MarkoutSection(Name = "Finalizer", IgnoreProperty = "Description,Select")]
    [JsonIgnore]
    public List<MemberRow>? FinalizerRows { get; set; }
    [MarkoutSection(Name = "Finalizer", IgnoreProperty = "Select")]
    [JsonIgnore]
    public List<MemberRow>? FinalizerRowsWithDocs { get; set; }

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

    [MarkoutSection(Name = "Finalizer", IgnoreProperty = "Description")]
    [JsonIgnore]
    public List<MemberRow>? FinalizerSelectRows { get; set; }
    [MarkoutSection(Name = "Finalizer")]
    [JsonIgnore]
    public List<MemberRow>? FinalizerSelectRowsWithDocs { get; set; }

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
    [MarkoutIgnoreColumnWhen(nameof(ConstructorSummaryDecodeIsEmpty), nameof(ConstructorSummaryRow.Decode))]
    [JsonIgnore]
    public List<ConstructorSummaryRow>? ConstructorSummaryRows { get; set; }

    [MarkoutSection(Name = "Constructors")]
    [MarkoutIgnoreColumnWhen(nameof(ConstructorSummaryDecodeIsEmpty), nameof(ConstructorSummaryRow.Decode))]
    [JsonIgnore]
    public List<ConstructorSummaryRow>? ConstructorSummaryRowsWithOverloads { get; set; }

    [MarkoutSection(Name = "Finalizer", IgnoreProperty = nameof(ConstructorSummaryRow.Overloads))]
    [MarkoutIgnoreColumnWhen(nameof(FinalizerSummaryDecodeIsEmpty), nameof(ConstructorSummaryRow.Decode))]
    [JsonIgnore]
    public List<ConstructorSummaryRow>? FinalizerSummaryRows { get; set; }

    [MarkoutSection(Name = "Fields")]
    [MarkoutIgnoreColumnWhen(nameof(FieldSummaryDecodeIsEmpty), nameof(FieldSummaryRow.Decode))]
    [JsonIgnore]
    public List<FieldSummaryRow>? FieldSummaryRows { get; set; }

    [MarkoutSection(Name = "Properties")]
    [MarkoutIgnoreColumnWhen(nameof(PropertySummaryDecodeIsEmpty), nameof(PropertySummaryRow.Decode))]
    [JsonIgnore]
    public List<PropertySummaryRow>? PropertySummaryRows { get; set; }

    // Index mode sections (--index path)
    [MarkoutSection(Name = "Signature")]
    [MarkoutIgnoreColumnWhen(nameof(SignatureDecodeIsEmpty), nameof(MemberSignatureRow.Decode))]
    [JsonIgnore]
    public List<MemberSignatureRow>? SignatureRows { get; set; }

    [MarkoutSection(Name = "Custom Attributes")]
    [JsonIgnore]
    public List<MethodAttributeRow>? MethodAttributeRows { get; set; }

    [MarkoutSection(Name = "Unsafe Members")]
    [JsonIgnore]
    public List<UnsafeMemberRow>? UnsafeMemberRows { get; set; }

    [MarkoutSection(Name = SectionNames.ExceptionRegions, EmptyText = "No exception regions found on this type.")]
    [MarkoutIgnoreColumnWhen(nameof(TypeExceptionRegionFilterRangeIsEmpty), nameof(TypeExceptionRegionRow.FilterRange))]
    [MarkoutIgnoreColumnWhen(nameof(TypeExceptionRegionCaughtTypeIsEmpty), nameof(TypeExceptionRegionRow.CaughtType))]
    [JsonIgnore]
    public List<TypeExceptionRegionRow>? ExceptionRegionRows { get; set; }

    [MarkoutSection(Name = SectionNames.CalledTypes, EmptyText = "No called types found for this type.")]
    [JsonIgnore]
    public List<CalledTypeRow>? CalledTypeRows { get; set; }

    [MarkoutSection(Name = SectionNames.AllocationFacts, EmptyText = "No allocation facts found for this type.")]
    [JsonIgnore]
    public List<AllocationFactRow>? AllocationFactRows { get; set; }

    [MarkoutSection(Name = SectionNames.SafetyFacts, EmptyText = "No safety facts found for this type.")]
    [JsonIgnore]
    public List<SafetyFactRow>? SafetyFactRows { get; set; }

    [MarkoutSection(Name = SectionNames.CostFacts, EmptyText = "No cost facts found for this type.")]
    [JsonIgnore]
    public List<CostFactRow>? CostFactRows { get; set; }

    [MarkoutSection(Name = "Top Leverage", EmptyText = "No intra-assembly call-graph leverage found for this type.")]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageVisibilityEmpty), nameof(TopLeverageRow.Visibility))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageGeneratedEmpty), nameof(TopLeverageRow.Generated))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageStableEmpty), nameof(TopLeverageRow.Stable))]
    [MarkoutIgnoreColumnWhen(nameof(TopLeverageSelectorEmpty), nameof(TopLeverageRow.Selector))]
    [JsonIgnore]
    public List<TopLeverageRow>? TopLeverageRows { get; set; }

    [MarkoutSection(Name = SectionNames.PerformanceTriage, EmptyText = "No optimization opportunities were found for this type.")]
    [JsonIgnore]
    public List<OptimizationOpportunityRow>? OptimizationOpportunityRows { get; set; }

    [MarkoutSection(Name = SectionNames.BodyShapes, EmptyText = "No matching body shapes found.")]
    [JsonIgnore]
    public List<ApiBodyShapeRow>? BodyShapeRows { get; set; }

    public static bool TopLeverageVisibilityEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Visibility));
    public static bool TopLeverageGeneratedEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Generated));
    public static bool TopLeverageStableEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Stable));
    public static bool TopLeverageSelectorEmpty(List<TopLeverageRow>? rows) => rows is null || rows.All(r => string.IsNullOrEmpty(r.Selector));
    public static bool TypeExceptionRegionFilterRangeIsEmpty(List<TypeExceptionRegionRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.FilterRange));
    public static bool TypeExceptionRegionCaughtTypeIsEmpty(List<TypeExceptionRegionRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.CaughtType));

    // The Decode column carries a signature-decode degradation marker that is null for
    // well-formed metadata (the common case). Drop the column when no member is degraded.
    public static bool SignatureDecodeIsEmpty(List<MemberSignatureRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));

    // Same treatment for the compact member-summary tables: the Decode degradation
    // marker is null for well-formed metadata, so drop the column when nothing is degraded.
    public static bool ConstructorSummaryDecodeIsEmpty(List<ConstructorSummaryRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));
    public static bool FinalizerSummaryDecodeIsEmpty(List<ConstructorSummaryRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));
    public static bool PropertySummaryDecodeIsEmpty(List<PropertySummaryRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));
    public static bool FieldSummaryDecodeIsEmpty(List<FieldSummaryRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));

    [MarkoutSection(Name = "Source Files", EmptyText = "No SourceLink source files found for this type.")]
    [JsonIgnore]
    public List<TypeSourceFileRow>? SourceFileRows => TypeSourceFiles();

    [MarkoutSection(Name = SectionNames.SourceLocations, EmptyText = "No SourceLink source locations found for the selected member(s).")]
    [MarkoutIgnoreColumnWhen(nameof(SourceLocationSelectorIsEmpty), nameof(MemberSourceLocationRow.Selector))]
    [JsonIgnore]
    public List<MemberSourceLocationRow>? SourceLocationRows { get; set; }

    public static bool SourceLocationSelectorIsEmpty(List<MemberSourceLocationRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.Selector));

    // Member code sections (populated by member command only, serialized separately)
    [MarkoutIgnore]
    [JsonIgnore]
    public MemberCodeView? MemberCode { get; set; }

    // Signatures of rendered members whose metadata signature blob could not be fully
    // decoded. Never rendered as a column; surfaced as a stderr warning after the table
    // so degradation stays visible without polluting the default output with an empty column.
    [MarkoutIgnore]
    [JsonIgnore]
    public List<string>? DegradedSignatureMembers { get; set; }

    private List<TypeSourceFileRow>? TypeSourceFiles()
    {
        List<TypeSourceFileRow> rows = [];
        if (SourceUrl != null)
        {
            rows.Add(new TypeSourceFileRow(SourceUrl)
            {
                FilePath = SourceFilePath,
                Checksum = SourceChecksum,
                ChecksumAlgorithm = SourceChecksumAlgorithm,
            });
        }
        if (AdditionalSourceFiles is { Count: > 0 })
            rows.AddRange(AdditionalSourceFiles
                .Where(file => file.SourceUrl != null)
                .Select(file => new TypeSourceFileRow(file.SourceUrl!)
                {
                    FilePath = file.FilePath,
                    Checksum = file.SourceChecksum,
                    ChecksumAlgorithm = file.SourceChecksumAlgorithm,
                }));
        return rows.Count > 0 ? rows : null;
    }
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
    [MarkoutIgnoreColumnWhen(nameof(MethodSummaryDecodeIsEmpty), nameof(MethodSummaryRow.Decode))]
    public List<MethodSummaryRow>? Rows { get; set; }

    [MarkoutSection(Name = "Method Groups")]
    [MarkoutIgnoreColumnWhen(nameof(MethodSummaryDecodeIsEmpty), nameof(MethodSummaryRow.Decode))]
    public List<MethodSummaryRow>? RowsWithOverloads { get; set; }

    [MarkoutIgnore]
    public bool HasRows => Rows is { Count: > 0 } || RowsWithOverloads is { Count: > 0 };

    public static bool MethodSummaryDecodeIsEmpty(List<MethodSummaryRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));
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
    [MarkoutIgnoreColumnWhen(nameof(DecodeIsEmpty), nameof(MemberIndexRow.Decode))]
    public List<MemberIndexRow>? Rows { get; set; }

    [MarkoutIgnore]
    public bool HasRows => Rows is { Count: > 0 };

    // Drop the Decode degradation-marker column when no member is degraded (the common case).
    public static bool DecodeIsEmpty(List<MemberIndexRow>? rows) => rows is null || rows.All(row => string.IsNullOrEmpty(row.Decode));
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
/// Type identity fact table for the <c>Type Info</c> section.
/// </summary>
/// <remarks>
/// The row set is structurally fixed: every property here is a fact *about* the type rather than
/// an entry *from* it, so the section is the same size for a 200-member type and a 2-member one.
/// That is what makes it the bare <c>-S</c> overview for the type view. Adding a property whose
/// count of rows depends on the type under inspection would break that contract.
/// </remarks>
[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class TypeInfoSection
{
    public string? Type { get; init; }
    public string? Kind { get; init; }
    public string? Modifiers { get; init; }

    [MarkoutPropertyName("Base")]
    public string? BaseType { get; init; }

    [MarkoutPropertyName("Type Parameters")]
    public string? TypeParameters { get; init; }

    public int? Interfaces { get; init; }

    [MarkoutPropertyName("Library")]
    public string? Assembly { get; init; }

    public string? Package { get; init; }
    public string? Version { get; init; }

    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; init; }

    public string? Source { get; init; }
}

/// <summary>
/// View model for the API surface identity fact table.
/// </summary>
/// <remarks>
/// The row set is structurally fixed: every property here is a fact *about* the surface being
/// listed rather than an entry *from* it, so the section is the same size whether the match is two
/// types or twenty thousand. The three counts are counts, not enumerations — each contributes
/// exactly one row no matter how large it gets. That is what makes this the bare <c>-S</c>
/// candidate for the type-listing view. Adding a property whose count of rows depends on the
/// assembly or the match would break that contract.
/// </remarks>
[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ApiInfoSection
{
    public ApiInfoSection(
        InertString? assemblyText,
        int? types,
        int? methods,
        int? properties,
        InertString? versionText,
        InertString? tfmText,
        InertString? sourceText)
    {
        AssemblyText = assemblyText;
        Types = types;
        Methods = methods;
        Properties = properties;
        VersionText = versionText;
        TfmText = tfmText;
        SourceText = sourceText;
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString? AssemblyText { get; }

    [MarkoutPropertyName("Library")]
    public string? Assembly => AssemblyText?.ToString();

    public int? Types { get; }
    public int? Methods { get; }
    public int? Properties { get; }

    [MarkoutIgnore, JsonIgnore]
    public InertString? VersionText { get; }

    public string? Version => VersionText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? TfmText { get; }

    [MarkoutPropertyName("TFM")]
    public string? Tfm => TfmText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? SourceText { get; }

    public string? Source => SourceText?.ToString();
}

/// <summary>
/// View model for full API surface rendering (all types in an assembly).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Name), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class CliApiSurface
{
    public CliApiSurface(
        InertString? nameText,
        InertString? descriptionText,
        InertString? libraryText,
        InertString? sourceText,
        InertString? versionText,
        InertString? tfmText)
    {
        NameText = nameText;
        DescriptionText = descriptionText;
        LibraryText = libraryText;
        SourceText = sourceText;
        VersionText = versionText;
        TfmText = tfmText;
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString? NameText { get; }

    [MarkoutIgnore]
    public string? Name => NameText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? DescriptionText { get; set; }

    [MarkoutIgnore]
    public string? Description => DescriptionText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? LibraryText { get; }

    [MarkoutSkipNull]
    public string? Library => LibraryText?.ToString();

    [MarkoutSkipNull] public int? Types { get; set; }
    [MarkoutSkipNull] public int? Methods { get; set; }
    [MarkoutSkipNull] public int? Properties { get; set; }

    [MarkoutIgnore, JsonIgnore]
    public InertString? SourceText { get; }

    [MarkoutSkipNull]
    public string? Source => SourceText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? VersionText { get; }

    [MarkoutSkipNull]
    public string? Version => VersionText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? TfmText { get; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm => TfmText?.ToString();

    /// <summary>
    /// API surface identity fact table. Unlike every other section on this view, the row set does
    /// not grow with the assembly or the match: it answers "what am I listing" rather than listing
    /// the matched types, so it is the same shape for a two-type match and for all of
    /// <c>System.Private.CoreLib</c>.
    /// </summary>
    [MarkoutSection(Name = "API Info")]
    [JsonIgnore]
    public ApiInfoSection? ApiInfo { get; set; }

    // Type forwarders (edge case: types == 0 with forwarders)
    [MarkoutSection(Name = "Type Forwarders")]
    public List<ForwarderSummaryRow>? TypeForwarders { get; set; }

    [MarkoutSection(Name = "Inspection Failures")]
    public List<ApiInspectionFailureRow>? InspectionFailures { get; set; }

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
public record TypeSummaryRow(
    InertString KindText,
    InertString TypeText,
    InertString MembersText,
    string? Description)
{
    public TypeSummaryRow(
        string kind,
        string type,
        string members,
        string? description)
        : this(
            ApiViewText.Field(kind),
            MarkoutInline.CodeText(ApiViewText.Field(type)),
            ApiViewText.Field(members),
            description)
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString KindText { get; init; } = KindText;

    public string Kind => KindText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString TypeText { get; init; } = TypeText;

    public string Type => TypeText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString MembersText { get; init; } = MembersText;

    public string Members => MembersText.ToString();

    [MarkoutSkipNull]
    public string? Description { get; init; } = Description;
}

[MarkoutSerializable]
public record ForwarderSummaryRow(
    [property: MarkoutPropertyName("Target Library")] string TargetLibrary,
    string Types);

[MarkoutSerializable]
public record ApiInspectionFailureRow(
    InertString OperationText,
    InertString SubjectText,
    InertString MechanismText,
    InertString KindText,
    InertString DetailText,
    InertString? AssemblyText = null,
    InertString? DependencyAssemblyText = null)
{
    public ApiInspectionFailureRow(
        string operation,
        string subject,
        string mechanism,
        string kind,
        string detail,
        string? assembly = null,
        string? dependencyAssembly = null)
        : this(
            ApiViewText.Field(operation),
            ApiViewText.Field(subject),
            ApiViewText.Field(mechanism),
            ApiViewText.Field(kind),
            ApiViewText.Field(detail),
            ApiViewText.OptionalField(assembly),
            ApiViewText.OptionalField(dependencyAssembly))
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString OperationText { get; init; } = OperationText;

    public string Operation => OperationText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString SubjectText { get; init; } = SubjectText;

    public string Subject => SubjectText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString MechanismText { get; init; } = MechanismText;

    public string Mechanism => MechanismText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString KindText { get; init; } = KindText;

    public string Kind => KindText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString DetailText { get; init; } = DetailText;

    public string Detail => DetailText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? AssemblyText { get; init; } = AssemblyText;

    [MarkoutSkipNull]
    public string? Assembly => AssemblyText?.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString? DependencyAssemblyText { get; init; } =
        DependencyAssemblyText;

    [MarkoutSkipNull]
    public string? DependencyAssembly =>
        DependencyAssemblyText?.ToString();
}

[MarkoutSerializable]
public record MemberRow(
    [property: MarkoutSkipNull] string? Select,
    string Name,
    string Digest,
    string Signature,
    string? Description)
{
    /// <summary>
    /// Creates a MemberRow without Select column.
    /// </summary>
    public MemberRow(string name, string digest, string signature, string? description)
        : this(null, name, digest, signature, description) { }
}

[MarkoutSerializable]
public record MemberIndexRow(
    string Selector,
    string Stable,
    [property: MarkoutPropertyName("Canonical Signature")] string CanonicalSignature,
    [property: MarkoutSkipNull] string? Decode,
    [property: MarkoutIgnore] string Digest);

[MarkoutSerializable]
/// <inheritdoc cref="SourceFileRow"/>
public record TypeSourceFileRow(string Url)
{
    public string Url { get; init; } = CSharpIdentifier.ContainRenderedText(Url);

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? FilePath { get; init; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal byte[]? Checksum { get; init; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? ChecksumAlgorithm { get; init; }
}

[MarkoutSerializable]
/// <inheritdoc cref="SourceFileRow"/>
public record MemberSourceLocationRow(
    string? Selector,
    string? Signature,
    string? File,
    int? Line,
    int? EndLine,
    string? Url)
{
    // Redeclared in full, in constructor order; partial redeclaration reorders
    // the rendered columns.
    [MarkoutSkipNull]
    public string? Selector { get; init; } = Contain(Selector);
    [MarkoutSkipNull]
    public string? Signature { get; init; } = Contain(Signature);
    [MarkoutSkipNull]
    public string? File { get; init; } = Contain(File);
    [MarkoutSkipNull]
    public int? Line { get; init; } = Line;
    [MarkoutPropertyName("End Line")]
    [MarkoutSkipNull]
    public int? EndLine { get; init; } = EndLine;
    [MarkoutSkipNull]
    public string? Url { get; init; } = Contain(Url);

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? FilePath { get; init; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal byte[]? Checksum { get; init; }

    [MarkoutIgnore]
    [JsonIgnore]
    internal string? ChecksumAlgorithm { get; init; }

    private static string? Contain(string? value)
        => value is null ? null : CSharpIdentifier.ContainRenderedText(value);
}

[MarkoutSerializable]
public record MemberSignatureRow(
    string Signature,
    string Digest,
    [property: MarkoutPropertyName("Canonical Signature")] string CanonicalSignature,
    [property: MarkoutSkipNull] string? Decode,
    [property: MarkoutSkipNull] string? Description);

/// <summary>
/// Compact summary row for Minimal verbosity: one row per unique member name with overload count.
/// </summary>
[MarkoutSerializable]
public record MethodSummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    string Overloads,
    [property: MarkoutSkipNull] string? Decode);

[MarkoutSerializable]
public record ConstructorSummaryRow(
    string Name,
    string Overloads,
    [property: MarkoutSkipNull] string? Decode);

[MarkoutSerializable]
public record PropertySummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    string Accessors,
    [property: MarkoutSkipNull] string? Decode);

[MarkoutSerializable]
public record FieldSummaryRow(
    string Name,
    [property: MarkoutPropertyName("Return Type")] string ReturnType,
    [property: MarkoutSkipNull] string? Decode);

[MarkoutSerializable]
public record EventSummaryRow(string Name, string Type);

[MarkoutSerializable]
public record MethodAttributeRow(
    [property: MarkoutIgnore] InertString NameText,
    [property: MarkoutIgnore] InertString ValueText)
{
    public string Name => NameText.ToString();
    public string Value => ValueText.ToString();
}

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
public class ApiTypeTableView
{
    [MarkoutSection(Name = "Members")]
    public List<ApiTableRow>? Rows { get; set; }
}

/// <summary>
/// View model for tabular full-API output: one unified table of all types.
/// </summary>
[MarkoutSerializable]
public class ApiSurfaceTableView
{
    [MarkoutSection(Name = "Types", IgnoreProperty = nameof(ApiSurfaceTableRow.Description))]
    public List<ApiSurfaceTableRow>? Rows { get; set; }

    [MarkoutSection(Name = "Types")]
    public List<ApiSurfaceTableRow>? RowsWithDescription { get; set; }
}

[MarkoutSerializable]
public record ApiTableRow(
    InertString KindText,
    InertString NameText,
    InertString ReturnTypeText,
    InertString DetailText)
{
    public ApiTableRow(
        string kind,
        string name,
        string returnType,
        string detail)
        : this(
            ApiViewText.Field(kind),
            ApiViewText.Field(name),
            ApiViewText.Field(returnType),
            ApiViewText.Field(detail))
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString KindText { get; init; } = KindText;

    public string Kind => KindText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString NameText { get; init; } = NameText;

    public string Name => NameText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString ReturnTypeText { get; init; } = ReturnTypeText;

    [MarkoutPropertyName("Return Type")]
    public string ReturnType => ReturnTypeText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString DetailText { get; init; } = DetailText;

    public string Detail => DetailText.ToString();
}

[MarkoutSerializable]
public record ApiSurfaceTableRow(
    InertString KindText,
    InertString TypeText,
    InertString MembersText,
    string? Description)
{
    public ApiSurfaceTableRow(
        string kind,
        string type,
        string members,
        string? description)
        : this(
            ApiViewText.Field(kind),
            MarkoutInline.CodeText(ApiViewText.Field(type)),
            ApiViewText.Field(members),
            description)
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public InertString KindText { get; init; } = KindText;

    public string Kind => KindText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString TypeText { get; init; } = TypeText;

    public string Type => TypeText.ToString();

    [MarkoutIgnore, JsonIgnore]
    public InertString MembersText { get; init; } = MembersText;

    public string Members => MembersText.ToString();

    [MarkoutSkipNull]
    public string? Description { get; init; } = Description;
}

[MarkoutSerializable]
public sealed record FidelityCauseRow(
    string State,
    string? Code,
    string? Location,
    [property: MarkoutPropertyName("Node Kind")] string? NodeKind,
    string? Node,
    string? Discriminator,
    string? Reason);

[MarkoutSerializable]
public sealed record AppliedTasteRow(
    string Rule,
    string Fidelity,
    string? Subject,
    string? Detail);

/// <summary>
/// Code sections for member command output (Decompiled Source, Annotated Source, PDB Source, IL).
/// Serialized separately after the main TypeView.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public class MemberCodeView
{
    [MarkoutSection(Name = "Decompiled Source")]
    public CodeSection DecompiledSourceCode { get; set; }

    [MarkoutSection(Name = SectionNames.FidelityCauses)]
    public List<FidelityCauseRow>? FidelityCauseRows { get; set; }

    [MarkoutSection(Name = SectionNames.AppliedTaste, EmptyText = "No recorded style choices were applied to this member.")]
    public List<AppliedTasteRow>? AppliedTasteRows { get; set; }

    [MarkoutSection(Name = "Annotated Source")]
    public CodeSection AnnotatedSourceCode { get; set; }

    [MarkoutSection(Name = SectionNames.AnnotatedSourceDocument)]
    public CodeSection AnnotatedSourceDocumentCode { get; set; }

    /// <summary>The structured value backing <see cref="AnnotatedSourceDocumentCode"/>.</summary>
    [MarkoutIgnore]
    [JsonIgnore]
    public ILInspector.Decompiler.AnnotatedSourceDocument? AnnotatedSourceDocument { get; set; }

    [MarkoutIgnore]
    [JsonIgnore]
    public ILInspector.Decompiler.DecompilerResult? AnnotatedSourceDocumentFailure { get; set; }

    [MarkoutSection(Name = "Cost Overlay")]
    public CodeSection CostOverlayCode { get; set; }

    [MarkoutSection(Name = "Semantics Overlay")]
    public CodeSection SemanticsOverlayCode { get; set; }

    [MarkoutSection(Name = SectionNames.PdbSource)]
    public CodeSection PdbSourceCode { get; set; }

    /// <summary>
    /// True when <see cref="PdbSourceCode"/> holds an explanation rather than PDB-selected source.
    /// Not a section: consumers that treat the PDB source as text (Source Diff) must skip it
    /// (issue #3299).
    /// </summary>
    public bool PdbSourceUnavailable { get; set; }

    [MarkoutSection(Name = SectionNames.SourceDiff)]
    public CodeSection SourceDiffCode { get; set; }

    [MarkoutSection(Name = "Calls", EmptyText = "No calls to other methods found in this method body.")]
    [MarkoutIgnoreColumnWhen(nameof(CallEvidenceMethodIsEmpty), nameof(CallSiteRow.EvidenceMethod))]
    public List<CallSiteRow>? CallRows { get; set; }

    [MarkoutSection(Name = "Exception Regions", EmptyText = "No exception regions found in this method body.")]
    [MarkoutIgnoreColumnWhen(nameof(ExceptionRegionFilterRangeIsEmpty), nameof(ExceptionRegionRow.FilterRange))]
    [MarkoutIgnoreColumnWhen(nameof(ExceptionRegionCaughtTypeIsEmpty), nameof(ExceptionRegionRow.CaughtType))]
    public List<ExceptionRegionRow>? ExceptionRegionRows { get; set; }

    [MarkoutSection(Name = "Callers", EmptyText = "No callers found in this assembly.")]
    [MarkoutIgnoreColumnWhen(nameof(CallerSourceIsUniform), nameof(CallerSiteRow.Source))]
    [MarkoutIgnoreColumnWhen(nameof(CallerEvidenceMethodIsEmpty), nameof(CallerSiteRow.EvidenceMethod))]
    public List<CallerSiteRow>? CallerRows { get; set; }

    /// <summary>
    /// Hides the Callers "Source" column when every caller comes from a single assembly
    /// (the default single-assembly scan), keeping that output unchanged. The column appears
    /// only when a caller scope (<c>--bin</c>/<c>--project</c>) brings in additional assemblies.
    /// </summary>
    public static bool CallerSourceIsUniform(List<CallerSiteRow>? rows)
        => rows is null || rows.Select(r => r.Source).Distinct(StringComparer.Ordinal).Count() <= 1;

    public static bool CallerEvidenceMethodIsEmpty(List<CallerSiteRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.EvidenceMethod));

    public static bool CallEvidenceMethodIsEmpty(List<CallSiteRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.EvidenceMethod));

    public static bool ExceptionRegionFilterRangeIsEmpty(List<ExceptionRegionRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.FilterRange));

    public static bool ExceptionRegionCaughtTypeIsEmpty(List<ExceptionRegionRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.CaughtType));

    public static bool AllocationFactMemberIsEmpty(List<AllocationFactRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.Member));

    public static bool SafetyFactMemberIsEmpty(List<SafetyFactRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.Member));

    public static bool CostFactMemberIsEmpty(List<CostFactRow>? rows)
        => rows is null || rows.All(row => string.IsNullOrEmpty(row.Member));

    [MarkoutSection(Name = SectionNames.CallGraph, EmptyText = "No inbound callers or outbound calls found for this method.")]
    public Markout.Graph? CallGraph { get; set; }

    [MarkoutIgnore]
    public int? CallGraphRowCount { get; set; }

    [MarkoutSection(Name = "Unsafe Operations", EmptyText = "No unsafe operations found in this method body.")]
    public List<UnsafeOperationRow>? UnsafeOperationRows { get; set; }

    [MarkoutSection(Name = "Facts", EmptyText = "No hidden facts found in this method body.")]
    public List<FactRow>? FactRows { get; set; }

    [MarkoutSection(Name = SectionNames.AllocationFacts, EmptyText = "No allocation facts found in this method body.")]
    [MarkoutIgnoreColumnWhen(nameof(AllocationFactMemberIsEmpty), nameof(AllocationFactRow.Member))]
    public List<AllocationFactRow>? AllocationFactRows { get; set; }

    [MarkoutSection(Name = SectionNames.SafetyFacts, EmptyText = "No safety facts found in this method body.")]
    [MarkoutIgnoreColumnWhen(nameof(SafetyFactMemberIsEmpty), nameof(SafetyFactRow.Member))]
    public List<SafetyFactRow>? SafetyFactRows { get; set; }

    [MarkoutSection(Name = SectionNames.CostFacts, EmptyText = "No cost facts found in this method body.")]
    [MarkoutIgnoreColumnWhen(nameof(CostFactMemberIsEmpty), nameof(CostFactRow.Member))]
    public List<CostFactRow>? CostFactRows { get; set; }

    [MarkoutSection(Name = "IL")]
    public CodeSection ILCode { get; set; }

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
[MarkoutContext(typeof(ExceptionRegionRow))]
[MarkoutContext(typeof(CallerSiteRow))]
[MarkoutContext(typeof(UnsafeOperationRow))]
[MarkoutContext(typeof(FactRow))]
[MarkoutContext(typeof(FidelityCauseRow))]
[MarkoutContext(typeof(AppliedTasteRow))]
[MarkoutContext(typeof(TypeSourceFileRow))]
[MarkoutContext(typeof(MemberSourceLocationRow))]
[MarkoutContext(typeof(TypeSummaryRow))]
[MarkoutContext(typeof(ForwarderSummaryRow))]
[MarkoutContext(typeof(MemberRow))]
[MarkoutContext(typeof(MemberSignatureRow))]
[MarkoutContext(typeof(MethodAttributeRow))]
[MarkoutContext(typeof(UnsafeMemberRow))]
[MarkoutContext(typeof(TypeExceptionRegionRow))]
[MarkoutContext(typeof(CalledTypeRow))]
[MarkoutContext(typeof(AllocationFactRow))]
[MarkoutContext(typeof(SafetyFactRow))]
[MarkoutContext(typeof(CostFactRow))]
[MarkoutContext(typeof(TopLeverageRow))]
[MarkoutContext(typeof(OptimizationOpportunityRow))]
[MarkoutContext(typeof(ApiBodyShapeRow))]
[MarkoutContext(typeof(ConstructorOverloadView))]
[MarkoutContext(typeof(ConstructorParameterRow))]
[MarkoutContext(typeof(EnumValueRow))]
[MarkoutContext(typeof(TypeParameterRow))]
[MarkoutContext(typeof(InterfaceRow))]
[MarkoutContext(typeof(BaseclassRow))]
[MarkoutContext(typeof(ApiTypeTableView))]
[MarkoutContext(typeof(ApiTableRow))]
[MarkoutContext(typeof(ApiSurfaceTableView))]
[MarkoutContext(typeof(ApiSurfaceTableRow))]
[MarkoutContext(typeof(SampleRow))]
[MarkoutContext(typeof(ApiInspectionFailureRow))]
[MarkoutContext(typeof(ApiInfoSection))]
public partial class ApiViewContext : MarkoutSerializerContext
{
}

[MarkoutSerializable]
public sealed record ApiBodyShapeRow(
    string Kind,
    string Member,
    string Token,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Match)
{
    public string Kind { get; init; } = CSharpIdentifier.ContainRenderedText(Kind);
    public string Member { get; init; } = MarkoutInline.Code(Member);
    public string Token { get; init; } = MarkoutInline.Code(Token);
    [MarkoutPropertyName("Start Line")]
    public int StartLine { get; init; } = StartLine;
    [MarkoutPropertyName("Start Column")]
    public int StartColumn { get; init; } = StartColumn;
    [MarkoutPropertyName("End Line")]
    public int EndLine { get; init; } = EndLine;
    [MarkoutPropertyName("End Column")]
    public int EndColumn { get; init; } = EndColumn;
    public string Match { get; init; } = MarkoutInline.Code(Match);

    internal static ApiBodyShapeRow FromMatch(
        ILInspector.Decompiler.BodyShapeMatch match)
        => new(
            match.Kind,
            match.Member,
            $"0x{match.MethodToken:X8}",
            match.Extent.StartLine + 1,
            match.Extent.StartColumn + 1,
            match.Extent.EndLine + 1,
            match.Extent.EndColumn + 1,
            match.Text);
}

[MarkoutSerializable]
public record CallSiteRow(
    string ILOffset,
    string? EvidenceMethod,
    string Opcode,
    string CallKind,
    string Callee,
    string OperandToken,
    string? ReturnAddress)
{
    [MarkoutPropertyName("IL Offset")]
    public string ILOffset { get; init; } = ILOffset;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Evidence Method")]
    [MarkoutSkipNull]
    public string? EvidenceMethod { get; init; } =
        LibraryViewText.Contain(EvidenceMethod);

    public string Opcode { get; init; } = Opcode;

    [MarkoutPropertyName("Call Kind")]
    public string CallKind { get; init; } = CallKind;

    public string Callee { get; init; } = Callee;

    [MarkoutPropertyName("Operand Token")]
    public string OperandToken { get; init; } = OperandToken;

    [MarkoutPropertyName("Return Address")]
    [MarkoutSkipNull]
    public string? ReturnAddress { get; init; } = ReturnAddress;
}

[MarkoutSerializable]
public record TypeExceptionRegionRow(
    string Member,
    int Region,
    string Clause,
    [property: MarkoutPropertyName("Try Range")] string TryRange,
    [property: MarkoutPropertyName("Handler Range")] string HandlerRange,
    [property: MarkoutPropertyName("Filter Range")]
    [property: MarkoutSkipNull] string? FilterRange,
    [property: MarkoutPropertyName("Caught Type")]
    [property: MarkoutSkipNull] string? CaughtType);

[MarkoutSerializable]
public record CalledTypeRow(
    string Type,
    [property: MarkoutSkipNull] string? Assembly,
    int Calls,
    int Members,
    [property: MarkoutPropertyName("Call Kinds")] string CallKinds);

[MarkoutSerializable]
public record AllocationFactRow(
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")] string ILOffset,
    [property: MarkoutPropertyName("Allocation Kind")] string AllocationKind,
    [property: MarkoutPropertyName("Allocated Type")] string? AllocatedType,
    [property: MarkoutPropertyName("Counted As Heap")] string CountedAsHeap,
    string Frequency,
    string Escape,
    [property: MarkoutPropertyName("In Loop")] string InLoop,
    string Evidence);

[MarkoutSerializable]
public record SafetyFactRow(
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")]
    [property: MarkoutSkipNull] string? ILOffset,
    [property: MarkoutPropertyName("Safety Kind")] string SafetyKind,
    string Operation,
    string Requirement,
    string Evidence);

[MarkoutSerializable]
public record CostFactRow(
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")] string ILOffset,
    [property: MarkoutPropertyName("Cost Kind")] string CostKind,
    string Operation,
    [property: MarkoutPropertyName("In Loop")] string InLoop,
    string Evidence);

[MarkoutSerializable]
public record ExceptionRegionRow(
    int Region,
    string Clause,
    [property: MarkoutPropertyName("Try Range")] string TryRange,
    [property: MarkoutPropertyName("Handler Range")] string HandlerRange,
    [property: MarkoutPropertyName("Filter Range")]
    [property: MarkoutSkipNull] string? FilterRange,
    [property: MarkoutPropertyName("Caught Type")]
    [property: MarkoutSkipNull] string? CaughtType);

[MarkoutSerializable]
public record CallerSiteRow(
    string Source,
    string Caller,
    string? EvidenceMethod,
    string ILOffset,
    string Opcode,
    string CallKind,
    string OperandToken,
    string? ReturnAddress)
{
    public string Source { get; init; } = Source;

    public string Caller { get; init; } = Caller;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Evidence Method")]
    [MarkoutSkipNull]
    public string? EvidenceMethod { get; init; } =
        LibraryViewText.Contain(EvidenceMethod);

    [MarkoutPropertyName("IL Offset")]
    public string ILOffset { get; init; } = ILOffset;

    public string Opcode { get; init; } = Opcode;

    [MarkoutPropertyName("Call Kind")]
    public string CallKind { get; init; } = CallKind;

    [MarkoutPropertyName("Operand Token")]
    public string OperandToken { get; init; } = OperandToken;

    [MarkoutPropertyName("Return Address")]
    [MarkoutSkipNull]
    public string? ReturnAddress { get; init; } = ReturnAddress;
}

[MarkoutSerializable]
public record UnsafeOperationRow(
    string Reason,
    string Detail,
    string Kind,
    [property: MarkoutSkipNull] string? IL,
    [property: MarkoutSkipNull] string? Token);

[MarkoutSerializable]
public record FactRow(
    string Member,
    [property: MarkoutSkipNull] string? IL,
    [property: MarkoutSkipNull] string? CsLine,
    string Anchor,
    string Category,
    string Id,
    [property: MarkoutSkipNull] string? Detail,
    string Conditionality);

[MarkoutSerializable]
public record UnsafeMemberRow(
    string Member,
    string Reason,
    string Detail,
    string Kind,
    string? IL,
    string? Token)
{
    // Every positional property is redeclared, in constructor order, because
    // PositionalRecordPropertyOrderTests requires all or none: a partial
    // redeclaration reorders the reflected properties and so reorders the
    // rendered columns.
    public string Member { get; init; } = Member;

    /// <inheritdoc cref="LibraryViewText"/>
    public string Reason { get; init; } = LibraryViewText.Contain(Reason);

    public string Detail { get; init; } = Detail;

    /// <inheritdoc cref="LibraryViewText"/>
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    [MarkoutSkipNull]
    public string? IL { get; init; } = IL;

    [MarkoutSkipNull]
    public string? Token { get; init; } = Token;
}

[MarkoutSerializable]
public record OptimizationOpportunityRow(
    string Member,
    string? Candidate,
    string? Finding,
    string? Provenance,
    string RootReach,
    string Shape,
    string? Operation,
    string? Token,
    string? EvidenceMethod,
    string? SupportingFinding,
    string? SupportingOperation,
    string? SupportingToken,
    string? SupportingEvidenceMethod,
    string? SupportingIL,
    string Evidence,
    string Fix,
    string Priority,
    string Confidence,
    string Loop,
    string? CallerLoop,
    string? CallerLoopDepth,
    string? CallerLoopWitness,
    string? Allocation,
    string? Path,
    string? PathConfidence,
    string? PostDominance,
    string? IL,
    string? Weight,
    string? DirectSites,
    string? OncePaths,
    string? ConditionalPaths,
    string? RepeatedPaths,
    string? UnknownPaths,
    string? CachedSites,
    string? OpaquePaths,
    string? Saturated)
{
    // All or none, in constructor order -- see UnsafeMemberRow.
    public string Member { get; init; } = Member;

    [MarkoutSkipNull]
    public string? Candidate { get; init; } = Candidate;

    [MarkoutSkipNull]
    public string? Finding { get; init; } = Finding;

    [MarkoutSkipNull]
    public string? Provenance { get; init; } = Provenance;

    public string RootReach { get; init; } = RootReach;

    public string Shape { get; init; } = Shape;

    [MarkoutSkipNull]
    public string? Operation { get; init; } = Operation;

    [MarkoutSkipNull]
    public string? Token { get; init; } = Token;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Evidence Method")]
    [MarkoutSkipNull]
    public string? EvidenceMethod { get; init; } =
        LibraryViewText.Contain(EvidenceMethod);

    [MarkoutPropertyName("Supporting Finding")]
    [MarkoutSkipNull]
    public string? SupportingFinding { get; init; } =
        LibraryViewText.Contain(SupportingFinding);

    [MarkoutPropertyName("Supporting Operation")]
    [MarkoutSkipNull]
    public string? SupportingOperation { get; init; } =
        LibraryViewText.Contain(
            SupportingOperation);

    [MarkoutPropertyName("Supporting Token")]
    [MarkoutSkipNull]
    public string? SupportingToken { get; init; } =
        LibraryViewText.Contain(SupportingToken);

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutPropertyName("Supporting Evidence Method")]
    [MarkoutSkipNull]
    public string? SupportingEvidenceMethod
        { get; init; } = LibraryViewText.Contain(
            SupportingEvidenceMethod);

    [MarkoutPropertyName("Supporting IL")]
    [MarkoutSkipNull]
    public string? SupportingIL { get; init; } =
        LibraryViewText.Contain(SupportingIL);

    public string Evidence { get; init; } = Evidence;

    /// <inheritdoc cref="LibraryViewText"/>
    public string Fix { get; init; } =
        LibraryViewText.Contain(Fix);

    public string Priority { get; init; } =
        LibraryViewText.Contain(Priority);

    public string Confidence { get; init; } = Confidence;

    public string Loop { get; init; } = Loop;

    [MarkoutSkipNull]
    public string? CallerLoop { get; init; } = CallerLoop;

    [MarkoutSkipNull]
    public string? CallerLoopDepth { get; init; } = CallerLoopDepth;

    [MarkoutSkipNull]
    public string? CallerLoopWitness { get; init; } = CallerLoopWitness;

    [MarkoutSkipNull]
    public string? Allocation { get; init; } = Allocation;

    [MarkoutSkipNull]
    public string? Path { get; init; } = Path;

    [MarkoutSkipNull]
    public string? PathConfidence { get; init; } = PathConfidence;

    [MarkoutPropertyName("Post Dominance")]
    [MarkoutSkipNull]
    public string? PostDominance { get; init; } = PostDominance;

    [MarkoutSkipNull]
    public string? IL { get; init; } = IL;

    [MarkoutSkipNull]
    public string? Weight { get; init; } = Weight;

    [MarkoutPropertyName("Direct Sites")]
    [MarkoutSkipNull]
    public string? DirectSites { get; init; } = DirectSites;

    [MarkoutPropertyName("Once Paths")]
    [MarkoutSkipNull]
    public string? OncePaths { get; init; } = OncePaths;

    [MarkoutPropertyName("Conditional Paths")]
    [MarkoutSkipNull]
    public string? ConditionalPaths { get; init; } = ConditionalPaths;

    [MarkoutPropertyName("Repeated Paths")]
    [MarkoutSkipNull]
    public string? RepeatedPaths { get; init; } = RepeatedPaths;

    [MarkoutPropertyName("Unknown Paths")]
    [MarkoutSkipNull]
    public string? UnknownPaths { get; init; } = UnknownPaths;

    [MarkoutPropertyName("Cached Sites")]
    [MarkoutSkipNull]
    public string? CachedSites { get; init; } = CachedSites;

    [MarkoutPropertyName("Opaque Paths")]
    [MarkoutSkipNull]
    public string? OpaquePaths { get; init; } = OpaquePaths;

    [MarkoutSkipNull]
    public string? Saturated { get; init; } = Saturated;
}

[MarkoutSerializable]
public record TopLeverageRow(
    string Member,
    string Callers,
    string RootReach,
    string Fanout,
    string Depth,
    string LoopCalls,
    string? Visibility = null,
    string? Generated = null,
    string? Stable = null,
    string? Selector = null)
{
    // All or none, in constructor order — see UnsafeMemberRow.
    public string Member { get; init; } = Member;

    public string Callers { get; init; } = Callers;

    public string RootReach { get; init; } = RootReach;

    public string Fanout { get; init; } = Fanout;

    public string Depth { get; init; } = Depth;

    public string LoopCalls { get; init; } = LoopCalls;

    /// <inheritdoc cref="LibraryViewText"/>
    [MarkoutSkipNull]
    public string? Visibility { get; init; } = LibraryViewText.Contain(Visibility);

    [MarkoutSkipNull]
    public string? Generated { get; init; } = Generated;

    [MarkoutSkipNull]
    public string? Stable { get; init; } = Stable;

    [MarkoutSkipNull]
    public string? Selector { get; init; } = Selector;
}
