using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

var metadata = new MetadataBuilder();
metadata.AddModule(
    0,
    metadata.GetOrAddString("Probe.dll"),
    metadata.GetOrAddGuid(Guid.NewGuid()),
    default,
    default);
metadata.AddAssembly(
    metadata.GetOrAddString("Probe"),
    new Version(1, 0, 0, 0),
    default,
    default,
    default,
    default);
metadata.AddTypeDefinition(
    TypeAttributes.NotPublic,
    default,
    metadata.GetOrAddString("<Module>"),
    default,
    MetadataTokens.FieldDefinitionHandle(1),
    MetadataTokens.MethodDefinitionHandle(1));
TypeDefinitionHandle owner = metadata.AddTypeDefinition(
    TypeAttributes.Public | TypeAttributes.Abstract,
    default,
    metadata.GetOrAddString("Owner"),
    default,
    MetadataTokens.FieldDefinitionHandle(1),
    MetadataTokens.MethodDefinitionHandle(1));
var methodSignature = new BlobBuilder();
new BlobEncoder(methodSignature)
    .MethodSignature(isInstanceMethod: true)
    .Parameters(0, result => result.Void(), _ => { });
MethodDefinitionHandle getter = metadata.AddMethodDefinition(
    MethodAttributes.Public
        | MethodAttributes.Abstract
        | MethodAttributes.Virtual,
    MethodImplAttributes.IL,
    metadata.GetOrAddString("get_Value"),
    metadata.GetOrAddBlob(methodSignature),
    bodyOffset: -1,
    MetadataTokens.ParameterHandle(1));
var propertySignature = new BlobBuilder();
new BlobEncoder(propertySignature)
    .PropertySignature(isInstanceProperty: true)
    .Parameters(
        0,
        result => result.Type().Int32(),
        _ => { });
PropertyDefinitionHandle property = metadata.AddProperty(
    PropertyAttributes.None,
    metadata.GetOrAddString("Value"),
    metadata.GetOrAddBlob(propertySignature));
metadata.AddPropertyMap(owner, property);
metadata.AddMethodSemantics(
    property,
    MethodSemanticsAttributes.Getter,
    getter);
var peBuilder = new ManagedPEBuilder(
    PEHeaderBuilder.CreateLibraryHeader(),
    new MetadataRootBuilder(
        metadata,
        "v4.0.30319",
        suppressValidation: true),
    new BlobBuilder(),
    flags: CorFlags.ILOnly);
var image = new BlobBuilder();
peBuilder.Serialize(image);
using var peReader = new PEReader(
    ImmutableArray.Create(image.ToArray()));
var success = (MethodSemanticsReadResult.Success)
    MethodSemanticsRowReader.Read(
        peReader,
        MethodSemanticsReadBudget.Default);
MethodSemanticsRow row = success.Rows.Single();
if (row.RawSemantics != (ushort)MethodSemanticsAttributes.Getter
    || row.AssociationKind
        != MethodSemanticsAssociationKind.Property
    || row.AssociationRowNumber != 1
    || MetadataTokens.GetRowNumber(row.Method) != 1)
{
    throw new InvalidOperationException(
        "Unexpected MethodSemantics result.");
}

Console.WriteLine("method-semantics-platform-probe: supported");
