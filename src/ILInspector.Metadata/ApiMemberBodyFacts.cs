using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Body-backing facts shared by Member population and presentation.</summary>
public static class ApiMemberBodyFacts
{
    public static bool IsBodyBacked(ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return IsMethodLike(member.Kind)
            || member.Kind is "property" or "event"
                && (member.GetterToken is not null
                    || member.SetterToken is not null
                    || member.AdderToken is not null
                    || member.RemoverToken is not null);
    }

    internal static bool IsBodyBacked(
        MetadataReader reader,
        in ClassifiedMember member) =>
        member.Kind switch
        {
            ClassifiedMemberKind.Method
                or ClassifiedMemberKind.Constructor
                or ClassifiedMemberKind.Finalizer
                or ClassifiedMemberKind.Operator
                or ClassifiedMemberKind
                    .ExplicitInterfaceImplementation
                or ClassifiedMemberKind.ExtensionMethod =>
                    true,
            ClassifiedMemberKind.Property =>
                HasPropertyAccessor(
                    reader,
                    (PropertyDefinitionHandle)member.Handle),
            ClassifiedMemberKind.Event =>
                HasEventAccessor(
                    reader,
                    (EventDefinitionHandle)member.Handle),
            _ => false,
        };

    private static bool IsMethodLike(string kind) =>
        kind is "method"
            or "constructor"
            or "finalizer"
            or "operator"
            or "explicit-interface-implementation"
            or "extension-method";

    private static bool HasPropertyAccessor(
        MetadataReader reader,
        PropertyDefinitionHandle handle)
    {
        PropertyAccessors accessors =
            reader.GetPropertyDefinition(handle).GetAccessors();
        return !accessors.Getter.IsNil
            || !accessors.Setter.IsNil;
    }

    private static bool HasEventAccessor(
        MetadataReader reader,
        EventDefinitionHandle handle)
    {
        EventAccessors accessors =
            reader.GetEventDefinition(handle).GetAccessors();
        return !accessors.Adder.IsNil
            || !accessors.Remover.IsNil;
    }
}
