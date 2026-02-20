namespace DotnetInspector.Sections;

/// <summary>
/// Constants for section names used in the api command output.
/// </summary>
public static class SectionNames
{
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

    /// <summary>Section for methods.</summary>
    public const string Methods = "Methods";

    /// <summary>Section for events.</summary>
    public const string Events = "Events";

    /// <summary>Section for custom attributes on methods.</summary>
    public const string CustomAttributes = "Custom Attributes";

    /// <summary>Section for method source code.</summary>
    public const string Source = "Source";

    /// <summary>Section for IL disassembly.</summary>
    public const string IL = "IL";

    /// <summary>Section for annotated IL disassembly.</summary>
    public const string ILAnnotated = "IL (Annotated)";

    /// <summary>Section for lowered C# (decompiled).</summary>
    public const string LoweredCSharp = "Lowered C#";
}
