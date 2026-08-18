using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
    throw new ArgumentException("Expected the Browser runtime-async managed output directory.");

string outputDirectory = Path.GetFullPath(args[0]);
if (!Directory.Exists(outputDirectory))
    throw new DirectoryNotFoundException(outputDirectory);

RunSelfTest();
Validate(outputDirectory);
Console.WriteLine("inspect-web runtime-async artifact gate passed.");

static void Validate(string outputDirectory)
{
    HashSet<string> assemblies = new(StringComparer.Ordinal);
    int methodCount = 0;
    foreach (string path in Directory.EnumerateFiles(
        outputDirectory,
        "*.dll",
        SearchOption.AllDirectories))
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        if (!pe.HasMetadata)
            continue;

        MetadataReader metadata = pe.GetMetadataReader();
        if (metadata.IsAssembly)
        {
            assemblies.Add(metadata.GetString(
                metadata.GetAssemblyDefinition().Name));
        }

        foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
        {
            methodCount++;
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            if (IsRuntimeAsync(method.ImplAttributes))
            {
                throw new InvalidOperationException(
                    $"{path} contains a runtime-async method, "
                    + "which the Browser/Wasm host cannot execute.");
            }
        }
    }

    if (methodCount == 0)
    {
        throw new InvalidOperationException(
            "The runtime-async metadata reader found no method definitions.");
    }

    foreach (string required in new[] { "InspectWeb.Engine", "NuGetFetch" })
    {
        if (!assemblies.Contains(required))
        {
            throw new InvalidOperationException(
                $"Browser runtime-async outputs do not contain {required}.");
        }
    }
}

static bool IsRuntimeAsync(MethodImplAttributes attributes) =>
    (attributes & (MethodImplAttributes)0x2000) != 0;

static void RunSelfTest()
{
    RuntimeAsyncCanary().GetAwaiter().GetResult();

    if (!IsRuntimeAsync((MethodImplAttributes)0x2000)
        || IsRuntimeAsync(MethodImplAttributes.IL))
    {
        throw new InvalidOperationException(
            "The runtime-async metadata discriminator failed its canaries.");
    }

    string assemblyPath = Path.Combine(
        AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.dll");
    if (!ContainsRuntimeAsyncMethod(assemblyPath))
    {
        throw new InvalidOperationException(
            "The runtime-async metadata reader missed its compiled async canary.");
    }
}

static bool ContainsRuntimeAsyncMethod(string path)
{
    using FileStream stream = File.OpenRead(path);
    using PEReader pe = new(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
    {
        MethodDefinition method = metadata.GetMethodDefinition(handle);
        if (IsRuntimeAsync(method.ImplAttributes))
        {
            return true;
        }
    }

    return false;
}

#pragma warning disable CS1998
static async Task RuntimeAsyncCanary() { }
#pragma warning restore CS1998
