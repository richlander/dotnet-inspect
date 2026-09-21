using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Tests;

public partial class MatchDiscoveryTests
{
    static byte[] BuildForwarderFacade(
        string assemblyName,
        string targetAssemblyPath,
        Type forwardedType)
    {
        using var targetPe = new PEReader(File.OpenRead(targetAssemblyPath));
        MetadataReader targetReader = targetPe.GetMetadataReader();
        AssemblyDefinition target = targetReader.GetAssemblyDefinition();
        return BuildForwarderFacade(
            assemblyName,
            new AssemblyReferenceIdentity(
                targetReader.GetString(target.Name),
                target.Version,
                null,
                null),
            forwardedType.Namespace!,
            forwardedType.Name);
    }

    static byte[] BuildForwarderFacade(
        string assemblyName,
        AssemblyReferenceIdentity target,
        string typeNamespace,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        AssemblyReferenceHandle targetReference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target.Name),
            target.Version
                ?? throw new InvalidOperationException(
                    "The forwarder fixture requires a target assembly version."),
            culture: target.Culture is null
                ? default
                : metadata.GetOrAddString(target.Culture),
            publicKeyOrToken: target.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(target.PublicKeyToken)),
            flags: default,
            hashValue: default);
        metadata.AddExportedType(
            TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(typeName),
            targetReference,
            typeDefinitionId: 0);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static string CreatePackageArchive(
        string root,
        string fileName,
        string packageName,
        string version,
        string asset,
        byte[] assetBytes)
    {
        string path = Path.Combine(root, $"{fileName}.nupkg");
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        ZipArchiveEntry library = archive.CreateEntry(asset);
        using (Stream stream = library.Open())
            stream.Write(assetBytes);
        ZipArchiveEntry nuspec =
            archive.CreateEntry($"{packageName}.nuspec");
        using (Stream stream = nuspec.Open())
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageName}</id>
                    <version>{version}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>range replay fixture</description>
                  </metadata>
                </package>
                """);
        }

        return path;
    }

    static string CreateToolWrapperArchive(
        string root,
        string packageName,
        string version,
        string redirectPackageName)
    {
        string path = Path.Combine(root, "wrapper.nupkg");
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        ZipArchiveEntry nuspec =
            archive.CreateEntry($"{packageName}.nuspec");
        using (Stream stream = nuspec.Open())
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageName}</id>
                    <version>{version}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>range wrapper replay fixture</description>
                  </metadata>
                </package>
                """);
        }

        ZipArchiveEntry settings = archive.CreateEntry(
            "tools/net10.0/any/DotnetToolSettings.xml");
        using (Stream stream = settings.Open())
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(
                $"""
                <DotNetCliTool Version="2">
                  <Commands>
                    <Command Name="fixture" EntryPoint="fixture.dll" Runner="dotnet" />
                  </Commands>
                  <RuntimeIdentifierPackages>
                    <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="{redirectPackageName}" />
                  </RuntimeIdentifierPackages>
                </DotNetCliTool>
                """);
        }

        return path;
    }

    static void CommitCachedPackage(
        string root,
        string directoryName,
        string nupkg,
        string packageName,
        string version,
        string source)
    {
        string extracted = Path.Combine(root, directoryName);
        ZipFile.ExtractToDirectory(nupkg, extracted);
        NuGetCache.CommitPackage(
            extracted,
            nupkg,
            packageName,
            version,
            NuGetCache.GetSourceKey(source));
    }

    private sealed class RangeReplayFeed : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly string _packageId;
        private readonly string _version;
        private readonly ConcurrentDictionary<string, byte[]> _packages = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _payloadRequests = new(StringComparer.OrdinalIgnoreCase);

        public RangeReplayFeed(string packageId, string version, bool queryDistinctSources = false)
        {
            _packageId = packageId.ToLowerInvariant();
            _version = version;
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            SourceA = queryDistinctSources
                ? $"http://127.0.0.1:{port}/index.json?channel=older"
                : $"http://127.0.0.1:{port}/a/index.json";
            SourceB = queryDistinctSources
                ? $"http://127.0.0.1:{port}/index.json?channel=newer"
                : $"http://127.0.0.1:{port}/b/index.json";
            _ = Task.Run(() => ServeAsync(_shutdown.Token));
        }

        public string SourceA { get; }

        public string SourceB { get; }

        public void AddPackage(string source, string packageId, string version, string packagePath)
            => _packages[PayloadPath(source, packageId, version)] = File.ReadAllBytes(packagePath);

        public int PayloadRequests(string source, string packageId, string version)
            => _payloadRequests.GetValueOrDefault(PayloadPath(source, packageId, version));

        private string PayloadPath(string source, string packageId, string version)
        {
            string feed = source == SourceA ? "a"
                : source == SourceB ? "b"
                : throw new ArgumentException("Unknown fixture source.", nameof(source));
            string id = packageId.ToLowerInvariant();
            return $"/{feed}/flat/{id}/{version}/{id}.{version}.nupkg";
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _listener.Stop();
            _shutdown.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = Task.Run(
                    () => RespondAsync(client, cancellationToken),
                    CancellationToken.None);
            }
        }

        private async Task RespondAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer, cancellationToken);
                string request = Encoding.ASCII.GetString(buffer, 0, read);
                string path =
                    request.Split(' ').Skip(1).FirstOrDefault() ?? string.Empty;
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                string? body = path switch
                {
                    "/a/index.json" or "/index.json?channel=older" =>
                        $$"""{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"http://127.0.0.1:{{port}}/a/flat/"}]}""",
                    "/b/index.json" or "/index.json?channel=newer" =>
                        $$"""{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"http://127.0.0.1:{{port}}/b/flat/"}]}""",
                    var value when value.Equals(
                        $"/a/flat/{_packageId}/index.json",
                        StringComparison.OrdinalIgnoreCase) =>
                        """{"versions":[]}""",
                    var value when value.Equals(
                        $"/b/flat/{_packageId}/index.json",
                        StringComparison.OrdinalIgnoreCase) =>
                        $$"""{"versions":["{{_version}}"]}""",
                    _ => null,
                };
                bool isPackage = _packages.TryGetValue(path, out byte[]? packageBytes);
                if (isPackage)
                    _payloadRequests.AddOrUpdate(path, 1, (_, count) => count + 1);
                HttpStatusCode status = body is not null || isPackage
                    ? HttpStatusCode.OK : HttpStatusCode.NotFound;
                byte[] bytes = packageBytes ?? Encoding.UTF8.GetBytes(body ?? "");
                byte[] head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(int)status} {status}\r\n"
                        + $"Content-Type: {(isPackage ? "application/octet-stream" : "application/json")}\r\n"
                        + $"Content-Length: {bytes.Length}\r\n"
                        + "Connection: close\r\n\r\n");
                await stream.WriteAsync(head, cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
    }

    static byte[] BuildMatchTargetAssembly(
        string assemblyName,
        string typeNamespace,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMatchTargetMethod(metadata, bodyEncoder, "Seed");
        AddMatchTargetMethod(metadata, bodyEncoder, "ExactPeer");

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static void AddMatchTargetMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name)
    {
        var code = new BlobBuilder();
        code.WriteBytes(new byte[] { 0x17, 0x18, 0x58, 0x26, 0x2A });
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 2);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }

    static ApiSurfaceInspectionFailure ForwardingFailure(
        MetadataTypeDefinitionName type,
        string targetAssembly)
        => new(
            "resolve forwarded type",
            0,
            MetadataTypeNameFailureMechanism.Metadata,
            "UnboundBinding",
            $"Forwarded type '{type.ToMetadataFullName()}' could not be resolved: UnboundBinding.",
            new AssemblyReferenceIdentity("Facade", new Version(1, 0, 0, 0), null, null),
            new AssemblyReferenceIdentity(targetAssembly, new Version(1, 0, 0, 0), null, null))
        {
            AffectedTypeDefinitions = [type],
        };

    static MetadataTypeDefinitionName DefinitionName(
        string @namespace,
        string name)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [name])).Name;

}
