using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

internal static class ApiMemorySafetyFactsSamples
{
    public static byte[] BackingFields(
        int fieldCount = 1,
        bool degradedLastField = false,
        TypeAttributes layout = TypeAttributes.AutoLayout,
        bool duplicateProperty = false,
        bool namedType = false,
        bool distinctTypeScopes = false,
        bool fieldLikeEvent = false,
        byte[]? propertyTypeOverride = null,
        byte[]? fieldTypeOverride = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0, metadata.GetOrAddString("Backing.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Backing"), new Version(1, 0),
            default, default, default, default);
        var core = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(11, 0),
            default, default, default, default);
        var objectType = metadata.AddTypeReference(
            core, metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var generatedType = metadata.AddTypeReference(
            core, metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerGeneratedAttribute"));
        var generatedConstructor = metadata.AddMemberReference(
            generatedType, metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 }));
        var declaredType = metadata.AddTypeReference(
            core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Action"));
        var otherScope = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"), new Version(1, 0),
            default, default, default, default);
        var otherType = metadata.AddTypeReference(
            otherScope, metadata.GetOrAddString("System"), metadata.GetOrAddString("Action"));
        byte[] propertyType = propertyTypeOverride
            ?? (namedType || distinctTypeScopes || fieldLikeEvent ? EncodeType(declaredType) : [8]);
        byte[] fieldType = fieldTypeOverride
            ?? (distinctTypeScopes ? EncodeType(otherType) : propertyType);
        var getter = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString(fieldLikeEvent ? "add_Value" : "get_Value"),
            metadata.GetOrAddBlob(fieldLikeEvent
                ? new byte[] { 0, 1, 1, .. propertyType }
                : new byte[] { 0, 0, .. propertyType }),
            -1, MetadataTokens.ParameterHandle(1));
        var fields = new List<FieldDefinitionHandle>();
        for (int i = 0; i < fieldCount; i++)
        {
            fields.Add(metadata.AddFieldDefinition(
                FieldAttributes.Private | FieldAttributes.Static,
                metadata.GetOrAddString(fieldLikeEvent ? "Value" : "<Value>k__BackingField"),
                metadata.GetOrAddBlob(
                    degradedLastField && i == fieldCount - 1
                        ? new byte[] { 6 }
                        : new byte[] { 6, .. fieldType })));
        }
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic, default,
            metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), getter);
        var type = metadata.AddTypeDefinition(
            TypeAttributes.Public | layout,
            metadata.GetOrAddString("Samples"), metadata.GetOrAddString("Backing"),
            objectType, MetadataTokens.FieldDefinitionHandle(1), getter);
        if (fieldLikeEvent)
        {
            var @event = metadata.AddEvent(
                EventAttributes.None, metadata.GetOrAddString("Value"), declaredType);
            metadata.AddEventMap(type, @event);
            metadata.AddMethodSemantics(@event, MethodSemanticsAttributes.Adder, getter);
        }
        else
        {
            var property = metadata.AddProperty(
                PropertyAttributes.None, metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(new byte[] { 8, 0, .. propertyType }));
            metadata.AddPropertyMap(type, property);
            metadata.AddMethodSemantics(property, MethodSemanticsAttributes.Getter, getter);
            if (duplicateProperty)
            {
                var duplicate = metadata.AddProperty(
                    PropertyAttributes.None, metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(new byte[] { 8, 0, .. propertyType }));
                metadata.AddMethodSemantics(
                    duplicate, MethodSemanticsAttributes.Getter, getter);
            }
        }
        var marker = metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 });
        metadata.AddCustomAttribute(getter, generatedConstructor, marker);
        foreach (var field in fields)
            metadata.AddCustomAttribute(field, generatedConstructor, marker);
        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }

    static byte[] EncodeType(EntityHandle type)
    {
        var blob = new BlobBuilder();
        blob.WriteByte(0x12);
        blob.WriteCompressedInteger(CodedIndex.TypeDefOrRef(type));
        return blob.ToArray();
    }
}
