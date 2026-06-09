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

    /// <summary>Section for lowered/decompiled C# method body.</summary>
    public const string DecompiledSource = "Decompiled Source";

    /// <summary>Section for original method source code resolved via SourceLink.</summary>
    public const string OriginalSource = "Original Source";

    /// <summary>Section for IL disassembly.</summary>
    public const string IL = "IL";

    /// <summary>Section for annotated IL disassembly.</summary>
    public const string ILAnnotated = "IL (Annotated)";

}
