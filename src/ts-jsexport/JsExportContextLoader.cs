using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;
using ILInspector.TypeScriptGeneration;

namespace TsJsExport;

internal sealed record JsExportContextRoot(
    string SerializedTypeName,
    MetadataTypeDefinitionName Type,
    AssemblyReferenceIdentity Assembly,
    string AssemblyPath,
    string ArtifactName);

internal sealed record GeneratedJsExportFacade(
    JsExportContextRoot Root,
    string Source);

internal static class JsExportContextLoader
{
    const int MaxPortableFileNameLength = 255;
    const string RootAttributeName = "JsExportRootAttribute";
    const string RootAttributeNamespace = "TsJsExport";

    static readonly AssemblyReferenceIdentity s_contractIdentity =
        ContractIdentity();
    static readonly Encoding s_strictUtf8 =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
    public static bool TryResolve(
        string contextAssemblyPath,
        string contextTypeName,
        IReadOnlyList<string> searchLocations,
        string toolName,
        TextWriter error,
        out ImmutableArray<JsExportContextRoot> roots)
    {
        roots = [];
        if (!File.Exists(contextAssemblyPath))
        {
            error.WriteLine(
                $"{toolName}: context assembly not found: {contextAssemblyPath}");
            return false;
        }

        try
        {
            using var stream = File.OpenRead(contextAssemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                error.WriteLine(
                    $"{toolName}: context image has no managed metadata: "
                        + contextAssemblyPath);
                return false;
            }

            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                error.WriteLine(
                    $"{toolName}: context image is not an assembly: "
                        + contextAssemblyPath);
                return false;
            }

            string? typeFailure = null;
            if (MetadataTypeDefinitionName.ParseSerialized(contextTypeName)
                    is not MetadataTypeDefinitionNameResult.Valid contextName
                || !TryFindType(
                    reader,
                    contextName.Name,
                    out TypeDefinitionHandle contextType,
                    out typeFailure))
            {
                error.WriteLine(
                    $"{toolName}: context type '{contextTypeName}' "
                        + $"{typeFailure ?? "is not a valid exact type name"}.");
                return false;
            }

            AssemblyReferenceIdentity contextIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            var declarations =
                ImmutableArray.CreateBuilder<RootDeclaration>();
            foreach (CustomAttributeHandle handle in
                reader.GetTypeDefinition(contextType).GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                RootAttributeDisposition disposition =
                    ClassifyRootAttribute(
                        reader,
                        attribute,
                        out AssemblyReferenceIdentity? attributeIdentity);
                if (disposition == RootAttributeDisposition.NotRoot)
                    continue;
                if (disposition == RootAttributeDisposition.WrongContract)
                {
                    error.WriteLine(
                        $"{toolName}: context type '{contextTypeName}' uses "
                            + "JsExportRootAttribute from incompatible contract "
                            + $"assembly '{FormatIdentity(attributeIdentity!)}'; "
                            + $"expected '{FormatIdentity(s_contractIdentity)}'.");
                    return false;
                }
                if (AttributeDecoder
                        .TryDecodePreservingSerializedTypeNames(
                            reader,
                            attribute)
                        is not
                        {
                            FixedArguments.Length: 1,
                            NamedArguments.Length: 0,
                        } decoded
                    || decoded.FixedArguments[0].Type != "System.Type"
                    || decoded.FixedArguments[0].Value
                        is not string serializedType
                    || string.IsNullOrWhiteSpace(serializedType)
                    || !TryParseRootType(
                        serializedType,
                        out MetadataTypeDefinitionName? type,
                        out AssemblyReferenceIdentity? assembly))
                {
                    error.WriteLine(
                        $"{toolName}: context type '{contextTypeName}' contains "
                            + "a malformed JsExportRoot declaration.");
                    return false;
                }

                declarations.Add(
                    new RootDeclaration(
                        serializedType,
                        type,
                        assembly));
            }

            if (declarations.Count == 0)
            {
                error.WriteLine(
                    $"{toolName}: context type '{contextTypeName}' has no "
                        + "JsExportRoot declarations from TsJsExport.Contracts.");
                return false;
            }

            if (!TryIndexCandidates(
                    contextAssemblyPath,
                    searchLocations,
                    toolName,
                    error,
                    out ImmutableArray<AssemblyCandidate> candidates))
            {
                return false;
            }

            var resolved = ImmutableArray.CreateBuilder<JsExportContextRoot>(
                declarations.Count);
            var rootedAssemblies = new HashSet<AssemblyReferenceIdentity>(
                AssemblyReferenceIdentity.EquivalentComparer);
            foreach (RootDeclaration declaration in declarations)
            {
                AssemblyReferenceIdentity requested =
                    declaration.Assembly ?? contextIdentity;
                ImmutableArray<AssemblyCandidate> matches =
                    declaration.Assembly is null
                        ? [
                            new AssemblyCandidate(
                                Path.GetFullPath(contextAssemblyPath),
                                contextIdentity),
                        ]
                        : [
                            .. candidates.Where(candidate =>
                                MatchesQualification(
                                    requested,
                                    candidate.Identity)),
                        ];

                if (matches.Length == 0)
                {
                    error.WriteLine(
                        $"{toolName}: rooted assembly was not found for "
                            + $"'{declaration.SerializedTypeName}'.");
                    return false;
                }
                if (matches.Length > 1)
                {
                    error.WriteLine(
                        $"{toolName}: rooted assembly resolution is ambiguous "
                            + $"for '{declaration.SerializedTypeName}'.");
                    return false;
                }

                AssemblyCandidate candidate = matches[0];
                if (!rootedAssemblies.Add(candidate.Identity))
                {
                    error.WriteLine(
                        $"{toolName}: context type '{contextTypeName}' contains "
                            + $"more than one root for assembly "
                            + $"'{candidate.Identity.Name}'.");
                    return false;
                }
                if (!TryGetArtifactName(
                        candidate.Identity.Name,
                        out string? artifactName))
                {
                    error.WriteLine(
                        $"{toolName}: assembly name "
                            + $"'{candidate.Identity.Name}' is not a portable "
                            + "context artifact name.");
                    return false;
                }
                if (resolved.Any(root =>
                        ArtifactNamesCollide(
                            root.ArtifactName,
                            artifactName)))
                {
                    error.WriteLine(
                        $"{toolName}: context artifact name '{artifactName}' "
                            + "is not unique under case-insensitive comparison.");
                    return false;
                }

                using var rootStream = File.OpenRead(candidate.Path);
                using var rootPeReader = new PEReader(rootStream);
                MetadataReader rootReader = rootPeReader.GetMetadataReader();
                if (!TryFindType(
                        rootReader,
                        declaration.Type,
                        out TypeDefinitionHandle rootType,
                        out typeFailure))
                {
                    error.WriteLine(
                        $"{toolName}: rooted type "
                            + $"'{declaration.SerializedTypeName}' "
                            + $"{typeFailure ?? "was not found"}.");
                    return false;
                }
                if (rootReader.GetTypeDefinition(rootType)
                        .GetGenericParameters().Count != 0)
                {
                    error.WriteLine(
                        $"{toolName}: rooted type "
                            + $"'{declaration.SerializedTypeName}' is generic.");
                    return false;
                }

                resolved.Add(
                    new JsExportContextRoot(
                        declaration.SerializedTypeName,
                        declaration.Type,
                        candidate.Identity,
                        candidate.Path,
                        artifactName));
            }

            roots = [
                .. resolved
                    .OrderBy(root => root.Assembly.Name, StringComparer.Ordinal)
                    .ThenBy(
                        root => root.Assembly.Version,
                        Comparer<Version?>.Default)
                    .ThenBy(
                        root => root.Type.ToEscapedFullName(),
                        StringComparer.Ordinal),
            ];
            return true;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            error.WriteLine(
                $"{toolName}: could not resolve facade context from "
                    + $"'{contextAssemblyPath}': {ex.Message}");
            return false;
        }
    }

    static RootAttributeDisposition ClassifyRootAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out AssemblyReferenceIdentity? identity)
    {
        identity = null;
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return RootAttributeDisposition.NotRoot;

        MemberReference constructor = reader.GetMemberReference(
            (MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference
            || !reader.StringComparer.Equals(
                constructor.Name,
                ".ctor"))
        {
            return RootAttributeDisposition.NotRoot;
        }

        TypeReference type = reader.GetTypeReference(
            (TypeReferenceHandle)constructor.Parent);
        if (!reader.StringComparer.Equals(type.Namespace, RootAttributeNamespace)
            || !reader.StringComparer.Equals(type.Name, RootAttributeName)
            || type.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return RootAttributeDisposition.NotRoot;
        }

        identity = AssemblyReferenceIdentity.From(
            reader,
            (AssemblyReferenceHandle)type.ResolutionScope);
        return identity.IsEquivalentTo(s_contractIdentity)
            ? RootAttributeDisposition.Root
            : RootAttributeDisposition.WrongContract;
    }

    static bool TryIndexCandidates(
        string contextAssemblyPath,
        IReadOnlyList<string> searchLocations,
        string toolName,
        TextWriter error,
        out ImmutableArray<AssemblyCandidate> candidates)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(contextAssemblyPath),
        };

        foreach (string location in searchLocations)
        {
            if (File.Exists(location))
            {
                paths.Add(Path.GetFullPath(location));
            }
            else if (Directory.Exists(location))
            {
                AddDirectoryCandidates(
                    Path.GetFullPath(location),
                    paths);
            }
            else
            {
                error.WriteLine(
                    $"{toolName}: assembly search location not found: "
                        + location);
                candidates = [];
                return false;
            }
        }

        var indexed = ImmutableArray.CreateBuilder<AssemblyCandidate>();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (TryReadAssemblyIdentity(path, out AssemblyReferenceIdentity? identity))
                indexed.Add(new AssemblyCandidate(path, identity));
        }
        candidates = indexed.ToImmutable();
        return true;
    }

    static void AddDirectoryCandidates(
        string directory,
        HashSet<string> paths)
    {
        foreach (string path in Directory.EnumerateFiles(
            directory,
            "*.dll",
            SearchOption.TopDirectoryOnly))
        {
            paths.Add(Path.GetFullPath(path));
        }
    }

    static bool TryReadAssemblyIdentity(
        string path,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out AssemblyReferenceIdentity? identity)
    {
        identity = null;
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;
            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                return false;
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            return true;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool TryFindType(
        MetadataReader reader,
        MetadataTypeDefinitionName name,
        out TypeDefinitionHandle match,
        out string? failure)
    {
        match = default;
        failure = null;
        int count = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    handle,
                    name,
                    out MetadataTypeNameFailure? nameFailure);
            if (result == MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                failure =
                    $"could not be resolved because {nameFailure?.Detail}";
                return false;
            }
            if (result != MetadataTypeDefinitionNameMatchResult.Match)
                continue;

            match = handle;
            count++;
        }

        if (count == 1)
            return true;
        failure = count == 0
            ? "was not found"
            : "is ambiguous";
        return false;
    }

    static bool TryParseRootType(
        string serializedName,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out MetadataTypeDefinitionName? type,
        out AssemblyReferenceIdentity? assembly)
    {
        type = null;
        assembly = null;
        if (serializedName.Length
            > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            return false;
        }

        var options = new TypeNameParseOptions
        {
            MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
        };
        if (!TypeName.TryParse(
                serializedName,
                out TypeName? parsed,
                options)
            || !parsed.IsSimple)
        {
            return false;
        }

        if (parsed.AssemblyName is { } assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.Name)
                || !TryGetPublicKeyToken(
                    assemblyName,
                    out string? publicKeyToken))
            {
                return false;
            }

            assembly = new AssemblyReferenceIdentity(
                assemblyName.Name,
                assemblyName.Version,
                ExplicitCultureOrNull(assemblyName.CultureName),
                publicKeyToken);
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        TypeName current = parsed;
        while (true)
        {
            if (!current.IsSimple)
                return false;
            segments.Add(TypeName.Unescape(current.Name));
            if (!current.IsNested)
                break;
            current = current.DeclaringType;
        }

        var rootToLeaf =
            ImmutableArray.CreateBuilder<string>(segments.Count);
        for (int i = segments.Count - 1; i >= 0; i--)
            rootToLeaf.Add(segments[i]);
        if (MetadataTypeDefinitionName.Create(
                TypeName.Unescape(current.Namespace),
                rootToLeaf.MoveToImmutable())
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return false;
        }

        type = valid.Name;
        return true;
    }

    static bool MatchesQualification(
        AssemblyReferenceIdentity expected,
        AssemblyReferenceIdentity candidate)
    {
        if (!expected.MatchesCandidate(candidate))
            return false;
        return expected.PublicKeyToken is not { Length: 0 }
            || string.IsNullOrEmpty(candidate.PublicKeyToken);
    }

    static string? ExplicitCultureOrNull(string? cultureName)
        => cultureName is null ? null
            : cultureName.Length == 0 ? "neutral"
            : cultureName;

    static bool TryGetPublicKeyToken(
        AssemblyNameInfo assemblyName,
        out string? publicKeyToken)
    {
        ImmutableArray<byte> token = assemblyName.PublicKeyOrToken;
        if (token.IsDefault)
        {
            publicKeyToken = null;
            return true;
        }
        if (token.IsEmpty)
        {
            publicKeyToken = "";
            return true;
        }
        if ((assemblyName.Flags & AssemblyNameFlags.PublicKey) != 0)
        {
            publicKeyToken =
                AssemblyReferenceIdentity.ComputePublicKeyToken(
                    token.ToArray());
            return true;
        }
        if (token.Length != 8)
        {
            publicKeyToken = null;
            return false;
        }

        publicKeyToken =
            Convert.ToHexString(token.AsSpan()).ToLowerInvariant();
        return true;
    }

    static AssemblyReferenceIdentity ContractIdentity()
    {
        AssemblyName name = typeof(JsExportRootAttribute).Assembly.GetName();
        byte[]? token = name.GetPublicKeyToken();
        return new AssemblyReferenceIdentity(
            name.Name!,
            name.Version,
            name.CultureName,
            token is { Length: > 0 }
                ? Convert.ToHexString(token).ToLowerInvariant()
                : null);
    }

    internal static bool ArtifactNamesCollide(
        string first,
        string second) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            first.Normalize(NormalizationForm.FormC),
            second.Normalize(NormalizationForm.FormC));

    internal static bool TryGetArtifactName(
        string assemblyName,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out string? artifactName)
    {
        artifactName = null;
        if (string.IsNullOrWhiteSpace(assemblyName)
            || assemblyName is "." or ".."
            || assemblyName.EndsWith(' ')
            || assemblyName.EndsWith('.')
            || assemblyName.Any(character =>
                character < ' '
                || character is '<' or '>' or ':' or '"' or '/'
                    or '\\' or '|' or '?' or '*'
                || Array.IndexOf(
                    Path.GetInvalidFileNameChars(),
                    character) >= 0))
        {
            return false;
        }

        string deviceName = assemblyName.Split('.')[0].TrimEnd(' ', '.');
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)
            || deviceName.Length == 4
                && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && IsReservedDeviceNumber(deviceName[3]))
        {
            return false;
        }

        string candidate = $"{assemblyName}.ts";
        try
        {
            if (candidate.Length > MaxPortableFileNameLength
                || s_strictUtf8.GetByteCount(candidate)
                    > MaxPortableFileNameLength)
            {
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        artifactName = candidate;
        return true;
    }

    static bool IsReservedDeviceNumber(char character) =>
        character is >= '1' and <= '9' or '¹' or '²' or '³';

    static string FormatIdentity(AssemblyReferenceIdentity identity) =>
        $"{identity.Name}, Version={identity.Version?.ToString() ?? "<omitted>"}, "
            + $"Culture={identity.Culture ?? "<omitted>"}, "
            + $"PublicKeyToken={identity.PublicKeyToken ?? "<omitted>"}";

    enum RootAttributeDisposition
    {
        NotRoot,
        Root,
        WrongContract,
    }

    sealed record RootDeclaration(
        string SerializedTypeName,
        MetadataTypeDefinitionName Type,
        AssemblyReferenceIdentity? Assembly);

    sealed record AssemblyCandidate(
        string Path,
        AssemblyReferenceIdentity Identity);
}

internal static class JsExportContextGenerator
{
    public static bool TryGenerate(
        string contextAssemblyPath,
        string contextTypeName,
        IReadOnlyList<string> searchLocations,
        string runtimeModule,
        string toolName,
        TextWriter error,
        out ImmutableArray<GeneratedJsExportFacade> facades)
    {
        facades = [];
        if (!JsExportContextLoader.TryResolve(
                contextAssemblyPath,
                contextTypeName,
                searchLocations,
                toolName,
                error,
                out ImmutableArray<JsExportContextRoot> roots))
        {
            return false;
        }

        var generated =
            ImmutableArray.CreateBuilder<GeneratedJsExportFacade>(
                roots.Length);
        foreach (JsExportContextRoot root in roots)
        {
            if (!JsExportSurfaceLoader.TryLoad(
                    root.AssemblyPath,
                    toolName,
                    error,
                    out global::ILInspector.JsExportSurface.JsExportSurface?
                        surface))
            {
                return false;
            }
            if (surface!.Functions.Count == 0)
            {
                error.WriteLine(
                    $"{toolName}: rooted assembly '{root.Assembly.Name}' "
                        + "has no supported [JSExport] methods.");
                return false;
            }

            var diagnostics = new TypeScriptGenerationDiagnostics();
            string source;
            try
            {
                source = TypeScriptFacadeEmitter.Emit(
                    surface,
                    runtimeModule,
                    diagnostics);
            }
            catch (UnsupportedWireContractException ex)
            {
                error.WriteLine($"{toolName}: {ex.Message}");
                return false;
            }

            foreach (TypeScriptGenerationDiagnostic diagnostic
                in diagnostics.UnmappedTypes)
            {
                error.WriteLine(
                    $"{toolName}: {diagnostic.Location}: "
                        + $"{diagnostic.CSharpType} has no TypeScript mapping.");
            }
            if (diagnostics.HasUnmappedTypes)
                return false;

            generated.Add(new GeneratedJsExportFacade(root, source));
        }

        facades = generated.ToImmutable();
        return true;
    }
}
