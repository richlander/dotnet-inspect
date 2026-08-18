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
    int typeReferenceCount = 0;
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

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            typeReferenceCount++;
            TypeReference reference = metadata.GetTypeReference(handle);
            if (IsRuntimeAsyncHelper(
                metadata.GetString(reference.Namespace),
                metadata.GetString(reference.Name)))
            {
                throw new InvalidOperationException(
                    $"{path} references System.Runtime.CompilerServices.AsyncHelpers, "
                    + "which the Browser/Wasm host cannot execute.");
            }
        }
    }

    if (typeReferenceCount == 0)
    {
        throw new InvalidOperationException(
            "The runtime-async metadata reader found no type references.");
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

static bool IsRuntimeAsyncHelper(string @namespace, string name) =>
    @namespace == "System.Runtime.CompilerServices"
    && name == "AsyncHelpers";

static void RunSelfTest()
{
    RuntimeAsyncCanary().GetAwaiter().GetResult();

    if (!IsRuntimeAsyncHelper("System.Runtime.CompilerServices", "AsyncHelpers")
        || IsRuntimeAsyncHelper("System.Runtime.CompilerServices", "AsyncHelper")
        || IsRuntimeAsyncHelper("Example", "AsyncHelpers"))
    {
        throw new InvalidOperationException(
            "The runtime-async metadata discriminator failed its canaries.");
    }

    string assemblyPath = Path.Combine(
        AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.dll");
    if (!ReferencesRuntimeAsync(assemblyPath))
    {
        throw new InvalidOperationException(
            "The runtime-async metadata reader missed its compiled async canary.");
    }
}

static bool ReferencesRuntimeAsync(string path)
{
    using FileStream stream = File.OpenRead(path);
    using PEReader pe = new(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeReferenceHandle handle in metadata.TypeReferences)
    {
        TypeReference reference = metadata.GetTypeReference(handle);
        if (IsRuntimeAsyncHelper(
            metadata.GetString(reference.Namespace),
            metadata.GetString(reference.Name)))
        {
            return true;
        }
    }

    return false;
}

static async Task RuntimeAsyncCanary() => await Task.Yield();
