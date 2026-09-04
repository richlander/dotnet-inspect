#:project ../../../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property OwnsItsOwnStderr=true

using ILInspector.Metadata;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: verify-async-lowering.cs "
        + "<InspectWeb.Engine.dll> <compiler|runtime> <census.json>");
    return 1;
}

string hostAssemblyPath = Path.GetFullPath(args[0]);
string expectedLowering = args[1];
string censusPath = args[2];
MethodClassification expected = expectedLowering switch
{
    "compiler" => MethodClassification.StateMachineAsync,
    "runtime" => MethodClassification.RuntimeAsync,
    _ => throw new ArgumentException(
        "Expected lowering must be 'compiler' or 'runtime'.",
        nameof(expectedLowering)),
};

string assemblyDirectory = Path.GetDirectoryName(hostAssemblyPath)!;
string[] contextAssemblies = ReadContextAssemblyNames(hostAssemblyPath);
var assemblyCensus = new List<AssemblyCensus>();
foreach (string assemblyName in contextAssemblies.Order(StringComparer.Ordinal))
{
    string assemblyPath = Path.Combine(assemblyDirectory, $"{assemblyName}.dll");
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine(
            $"Context-rooted export assembly was not found: {assemblyPath}");
        return 1;
    }

    using FileStream stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    MetadataReader reader = peReader.GetMetadataReader();
    string actualAssemblyName =
        reader.GetString(reader.GetAssemblyDefinition().Name);
    if (!actualAssemblyName.Equals(assemblyName, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"Context root '{assemblyName}' resolved to assembly "
            + $"'{actualAssemblyName}'.");
        return 1;
    }

    var exports = new HashSet<(string Type, string Method)>();
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        string typeName = reader.GetString(type.Name);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            RuntimeJsExportAttributeEvidence evidence =
                AttributeReader.ReadRuntimeJsExportAttributes(
                    reader,
                    method.GetCustomAttributes());
            if (evidence.Count == 0)
                continue;
            if (evidence is not
                { Count: 1, ValidRowCount: 1, HasMalformedRow: false })
            {
                Console.Error.WriteLine(
                    $"{assemblyName} contains malformed or duplicate [JSExport] "
                    + $"metadata on {typeName}.{reader.GetString(method.Name)}.");
                return 1;
            }

            exports.Add((typeName, reader.GetString(method.Name)));
        }
    }
    if (exports.Count == 0)
    {
        Console.Error.WriteLine(
            $"Context-rooted assembly {assemblyName} has no [JSExport] methods.");
        return 1;
    }

    stream.Position = 0;
    ClassifiedMethodInfo[] asyncExports =
    [
        .. MethodClassificationScanner.Scan(stream)
            .Where(method =>
                method.Classification is MethodClassification.StateMachineAsync
                    or MethodClassification.RuntimeAsync)
            .Where(method =>
                exports.Contains(
                    (SimpleTypeName(method.DeclaringType), method.MethodName))),
    ];
    int compilerAsyncCount = asyncExports.Count(method =>
        method.Classification == MethodClassification.StateMachineAsync);
    int runtimeAsyncCount = asyncExports.Count(method =>
        method.Classification == MethodClassification.RuntimeAsync);
    if (asyncExports.Any(method => method.Classification != expected))
    {
        Console.Error.WriteLine(
            $"Expected every async [JSExport] in {assemblyName} to use "
            + $"{expectedLowering} lowering; found compiler={compilerAsyncCount}, "
            + $"runtime={runtimeAsyncCount}.");
        return 1;
    }

    assemblyCensus.Add(new(
        assemblyName,
        Path.GetFileName(assemblyPath),
        exports.Count,
        asyncExports.Length,
        compilerAsyncCount,
        runtimeAsyncCount,
        asyncExports.Count(method =>
            method.DeclaringType == "InspectionEngine"
            && method.MethodName == "AsyncLoweringCanary")));
}

int canaryCount = assemblyCensus.Sum(assembly => assembly.CanaryCount);
if (canaryCount != 1)
{
    Console.Error.WriteLine(
        "Expected exactly one async InspectionEngine.AsyncLoweringCanary "
        + $"in the compiled context; found {canaryCount}.");
    return 1;
}

int asyncMethodCount =
    assemblyCensus.Sum(assembly => assembly.AsyncMethodCount);
int compilerAsyncMethodCount =
    assemblyCensus.Sum(assembly => assembly.CompilerAsyncMethodCount);
int runtimeAsyncMethodCount =
    assemblyCensus.Sum(assembly => assembly.RuntimeAsyncMethodCount);
if (asyncMethodCount == 0
    || asyncMethodCount
        != compilerAsyncMethodCount + runtimeAsyncMethodCount)
{
    Console.Error.WriteLine("The compiled context has an invalid async census.");
    return 1;
}

Directory.CreateDirectory(
    Path.GetDirectoryName(Path.GetFullPath(censusPath))!);
using (FileStream censusStream = File.Create(censusPath))
{
    using (var writer = new Utf8JsonWriter(censusStream))
    {
        writer.WriteStartObject();
        writer.WriteNumber("assembly_count", assemblyCensus.Count);
        writer.WriteNumber(
            "js_export_method_count",
            assemblyCensus.Sum(assembly => assembly.JsExportMethodCount));
        writer.WriteNumber("async_method_count", asyncMethodCount);
        writer.WriteNumber(
            "compiler_async_method_count",
            compilerAsyncMethodCount);
        writer.WriteNumber(
            "runtime_async_method_count",
            runtimeAsyncMethodCount);
        writer.WriteStartArray("assemblies");
        foreach (AssemblyCensus assembly in assemblyCensus)
        {
            writer.WriteStartObject();
            writer.WriteString("name", assembly.Name);
            writer.WriteString("file", assembly.File);
            writer.WriteNumber(
                "js_export_method_count",
                assembly.JsExportMethodCount);
            writer.WriteNumber(
                "async_method_count",
                assembly.AsyncMethodCount);
            writer.WriteNumber(
                "compiler_async_method_count",
                assembly.CompilerAsyncMethodCount);
            writer.WriteNumber(
                "runtime_async_method_count",
                assembly.RuntimeAsyncMethodCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    censusStream.WriteByte((byte)'\n');
}

Console.WriteLine(
    $"InspectWeb async census found "
    + $"{assemblyCensus.Sum(assembly => assembly.JsExportMethodCount)} exports "
    + $"({asyncMethodCount} async) across {assemblyCensus.Count} context-rooted "
    + $"assemblies: compiler={compilerAsyncMethodCount}, "
    + $"runtime={runtimeAsyncMethodCount}; canary={expectedLowering}.");
foreach (AssemblyCensus assembly in assemblyCensus)
{
    Console.WriteLine(
        $"  {assembly.Name}: exports={assembly.JsExportMethodCount}, "
        + $"async={assembly.AsyncMethodCount} "
        + $"(compiler={assembly.CompilerAsyncMethodCount}, "
        + $"runtime={assembly.RuntimeAsyncMethodCount})");
}
return 0;

static string[] ReadContextAssemblyNames(string hostAssemblyPath)
{
    using FileStream stream = File.OpenRead(hostAssemblyPath);
    using var peReader = new PEReader(stream);
    MetadataReader reader = peReader.GetMetadataReader();
    string hostAssemblyName =
        reader.GetString(reader.GetAssemblyDefinition().Name);
    TypeDefinitionHandle contextHandle = reader.TypeDefinitions
        .FirstOrDefault(handle =>
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Namespace) == "InspectWeb.Engine"
                && reader.GetString(type.Name) == "InspectWebJsExportContext";
        });
    if (contextHandle.IsNil)
    {
        throw new InvalidOperationException(
            "InspectWeb.Engine.InspectWebJsExportContext was not found.");
    }

    var assemblies = new List<string>();
    foreach (CustomAttributeHandle attributeHandle in
        reader.GetTypeDefinition(contextHandle).GetCustomAttributes())
    {
        CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
        if (CustomAttributeTypeName(reader, attribute.Constructor)
            != "TsJsExport.JsExportRootAttribute")
        {
            continue;
        }

        BlobReader blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1)
        {
            throw new BadImageFormatException(
                "JsExportRoot attribute has an invalid prolog.");
        }
        string typeIdentity = blob.ReadSerializedString()
            ?? throw new BadImageFormatException(
                "JsExportRoot attribute has no root type.");
        int typeSeparator = typeIdentity.IndexOf(',');
        if (typeSeparator < 0)
        {
            assemblies.Add(hostAssemblyName);
            continue;
        }
        string assemblyIdentity = typeIdentity[(typeSeparator + 1)..].Trim();
        int assemblySeparator = assemblyIdentity.IndexOf(',');
        string assemblyName = (
            assemblySeparator < 0
                ? assemblyIdentity
                : assemblyIdentity[..assemblySeparator]).Trim();
        if (assemblyName.Length == 0)
        {
            throw new BadImageFormatException(
                $"JsExportRoot type '{typeIdentity}' has an empty assembly identity.");
        }
        assemblies.Add(assemblyName);
    }

    if (assemblies.Count == 0
        || assemblies.Distinct(StringComparer.Ordinal).Count() != assemblies.Count)
    {
        throw new InvalidOperationException(
            "InspectWebJsExportContext has no roots or roots an assembly twice.");
    }

    return [.. assemblies];
}

static string? CustomAttributeTypeName(
    MetadataReader reader,
    EntityHandle constructor)
{
    EntityHandle typeHandle = constructor.Kind switch
    {
        HandleKind.MemberReference =>
            reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
        HandleKind.MethodDefinition =>
            reader.GetMethodDefinition(
                (MethodDefinitionHandle)constructor).GetDeclaringType(),
        _ => default,
    };
    return typeHandle.Kind switch
    {
        HandleKind.TypeReference => FullName(
            reader.GetString(
                reader.GetTypeReference((TypeReferenceHandle)typeHandle).Namespace),
            reader.GetString(
                reader.GetTypeReference((TypeReferenceHandle)typeHandle).Name)),
        HandleKind.TypeDefinition => FullName(
            reader.GetString(
                reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Namespace),
            reader.GetString(
                reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Name)),
        _ => null,
    };
}

static string FullName(string @namespace, string name) =>
    @namespace.Length == 0 ? name : $"{@namespace}.{name}";

static string SimpleTypeName(string declaringType)
{
    int separator = Math.Max(
        declaringType.LastIndexOf('.'),
        declaringType.LastIndexOf('+'));
    return separator < 0
        ? declaringType
        : declaringType[(separator + 1)..];
}

internal sealed record AssemblyCensus(
    string Name,
    string File,
    int JsExportMethodCount,
    int AsyncMethodCount,
    int CompilerAsyncMethodCount,
    int RuntimeAsyncMethodCount,
    int CanaryCount);
