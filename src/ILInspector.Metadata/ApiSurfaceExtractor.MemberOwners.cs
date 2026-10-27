using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{
    // The Type-level fold uses the declared C# population at every access,
    // plus the physical rows that population raises to one declaration.
    internal static Dictionary<EntityHandle, EntityHandle> DeclaredMemberOwners(
        MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        var sink = new MemberOwnerSink(reader, []);
        ClassifyDeclaredMembers(reader, handle, type, MetadataMemberSpelling.CSharp,
            publicOnly: false, extensionContainer: false, classifyLogicalMethodKinds: false, ref sink);
        Dictionary<EntityHandle, EntityHandle> owners = sink.Owners;
        foreach (PropertyDefinitionHandle property in type.GetProperties())
        {
            PropertyAccessors accessors = reader.GetPropertyDefinition(property).GetAccessors();
            Add(accessors.Getter, property);
            Add(accessors.Setter, property);
            foreach (MethodDefinitionHandle other in accessors.Others)
                Add(other, property);
        }
        foreach (EventDefinitionHandle @event in type.GetEvents())
        {
            EventAccessors accessors = reader.GetEventDefinition(@event).GetAccessors();
            Add(accessors.Adder, @event);
            Add(accessors.Remover, @event);
            Add(accessors.Raiser, @event);
            foreach (MethodDefinitionHandle other in accessors.Others)
                Add(other, @event);
        }

        GenericContext context = GenericContext.ForType(reader, type);
        var properties = AutoPropertyBackingFieldDescriptors(reader, type, context);
        var events = FieldLikeEventBackingFieldNames(reader, type);
        foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
        {
            FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
            string name = reader.GetString(field.Name);
            if (IsAutoPropertyBackingField(reader, field, name, properties, context))
                owners[fieldHandle] = MetadataTokens.EntityHandle(properties![name].PropertyToken);
            else if (IsFieldLikeEventBackingField(reader, field, name, events))
            {
                foreach (EventDefinitionHandle @event in type.GetEvents())
                {
                    if (reader.StringComparer.Equals(reader.GetEventDefinition(@event).Name, name))
                    {
                        owners[fieldHandle] = @event;
                        break;
                    }
                }
            }
        }
        return owners;

        void Add(MethodDefinitionHandle method, EntityHandle owner)
        {
            if (!method.IsNil)
                owners[method] = owner;
        }
    }

    readonly struct MemberOwnerSink(MetadataReader reader, Dictionary<EntityHandle, EntityHandle> owners)
        : IClassifiedMemberSink
    {
        public Dictionary<EntityHandle, EntityHandle> Owners => owners;

        public void Add(in ClassifiedMember member)
        {
            if (member.Handle.Kind == HandleKind.MethodDefinition
                && reader.StringComparer.Equals(reader.GetMethodDefinition((MethodDefinitionHandle)member.Handle).Name, ".cctor"))
            {
                return;
            }
            owners[member.Handle] = member.Handle;
        }
    }
}
