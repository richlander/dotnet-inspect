#:project ../src/CSharpText/CSharpText.csproj

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using CSharpText;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine(
        "Usage: dotnet run eng/generate-csharp-identifier-word-oracle.cs -- "
        + "<ref-pack-directory> [reviewed-entries] [output]");
    return 2;
}

string packDirectory = Path.GetFullPath(args[0]);
string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
string reviewedPath = args.Length >= 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(repositoryRoot, "eng", "csharp-identifier-word-oracle-reviewed.txt");
string? outputPath = args.Length == 3 ? Path.GetFullPath(args[2]) : null;

if (!Directory.Exists(packDirectory))
    throw new DirectoryNotFoundException(packDirectory);

var entries = File.ReadLines(reviewedPath)
    .Where(static line => line.Length > 0 && line[0] != '#')
    .Select(ParseEntry)
    .ToArray();
string digest = IdentifierWordOracle.ComputeDigest(entries);
IdentifierWordOracle oracle =
    ((IdentifierWordOracleConstruction.Created)IdentifierWordOracle.Create(
        IdentifierWordBreaker.GrammarVersion,
        IdentifierWordProductOracle.VocabularyVersion,
        digest,
        IdentifierWordProductOracle.SourceCoordinate,
        IdentifierWordProductOracle.ReviewSetVersion,
        entries)).Oracle;

var witnessed = new HashSet<string>(StringComparer.Ordinal);
foreach (string name in EnumeratePublicNames(packDirectory))
{
    if (IdentifierWordBreaker.Break(name, oracle)
        is not IdentifierWordBreakOutcome.Succeeded succeeded)
    {
        throw new InvalidOperationException("The generation oracle was rejected.");
    }

    foreach (IdentifierWordSpan span in succeeded.Result.Spans)
    {
        if (span.Evidence.OracleEntry is { } entry)
            witnessed.Add(entry);
    }
}

string[] missing = entries
    .Select(static entry => entry.Text)
    .Where(entry => !witnessed.Contains(entry))
    .ToArray();
if (missing.Length > 0)
{
    throw new InvalidOperationException(
        $"Reviewed entries lack a public reference-pack witness: {string.Join(", ", missing)}");
}

var snapshot = new StringBuilder();
snapshot.Append("grammar\t").Append(IdentifierWordBreaker.GrammarVersion).Append('\n');
snapshot.Append("vocabulary\t").Append(IdentifierWordProductOracle.VocabularyVersion).Append('\n');
snapshot.Append("source\t").Append(IdentifierWordProductOracle.SourceCoordinate).Append('\n');
snapshot.Append("review\t").Append(IdentifierWordProductOracle.ReviewSetVersion).Append('\n');
snapshot.Append("digest\t").Append(digest).Append('\n');
foreach (IdentifierWordOracleEntry entry in entries)
{
    snapshot
        .Append(entry.Kind == IdentifierWordOracleEntryKind.Atom ? "atom" : "compound")
        .Append('\t')
        .Append(entry.Text)
        .Append('\n');
}

if (outputPath is null)
{
    Console.Write(snapshot);
}
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, snapshot.ToString(), new UTF8Encoding(false));
}
return 0;

static IdentifierWordOracleEntry ParseEntry(string line)
{
    string[] parts = line.Split('\t');
    if (parts.Length != 2)
        throw new InvalidDataException($"Invalid reviewed oracle entry: {line}");
    return new(
        parts[1],
        parts[0] switch
        {
            "atom" => IdentifierWordOracleEntryKind.Atom,
            "compound" => IdentifierWordOracleEntryKind.Compound,
            _ => throw new InvalidDataException($"Invalid oracle entry kind: {parts[0]}"),
        });
}

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null
        && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName
        ?? throw new DirectoryNotFoundException(
            "Run the generator from the dotnet-inspect repository.");
}

static IEnumerable<string> EnumeratePublicNames(string packDirectory)
{
    foreach (string path in Directory.EnumerateFiles(packDirectory, "*.dll"))
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            continue;

        MetadataReader reader = pe.GetMetadataReader();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!IsPublicType(reader, typeHandle))
                continue;

            yield return reader.GetString(type.Name);
            foreach (string segment in reader
                .GetString(type.Namespace)
                .Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return segment;
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask)
                    == MethodAttributes.Public)
                {
                    yield return reader.GetString(method.Name);
                }
            }

            static bool IsPublicType(MetadataReader reader, TypeDefinitionHandle handle)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
                TypeDefinitionHandle declaringType = type.GetDeclaringType();
                return declaringType.IsNil
                    ? visibility == TypeAttributes.Public
                    : visibility == TypeAttributes.NestedPublic
                        && IsPublicType(reader, declaringType);
            }
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask)
                    == FieldAttributes.Public)
                {
                    yield return reader.GetString(field.Name);
                }
            }
        }
    }
}
