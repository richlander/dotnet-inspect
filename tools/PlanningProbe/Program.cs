using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis.Planning.Experiments;
using NLinq;

// args: <budget-ms> <variant> <dll>...
int budgetMs = int.Parse(args[0]);
string variant = args[1];
foreach (string path in args.Skip(2))
{
    var image = ImmutableArray.Create(File.ReadAllBytes(path));
    string answer = Run(variant, path, image);
    for (int i = 0; i < 5; i++)
        Run(variant, path, image);

    var samples = new List<double>();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var total = Stopwatch.StartNew();
    while ((total.ElapsedMilliseconds < budgetMs || samples.Count < 20) && samples.Count < 5000)
    {
        long start = Stopwatch.GetTimestamp();
        Run(variant, path, image);
        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    long allocatedPerCall = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / samples.Count;
    samples.Sort();
    double Q(double q) => samples[(int)Math.Clamp(Math.Round(q * (samples.Count - 1)), 0, samples.Count - 1)];
    Console.WriteLine($"{Path.GetFileName(path)}\t{answer}\t{samples.Count}\t{Q(0.5):F4}\t{Q(0.1):F4}\t{Q(0.9):F4}\t{allocatedPerCall}");
}

static string Run(string variant, string path, ImmutableArray<byte> image)
{
    using var peReader = new PEReader(image);
    return variant switch
    {
        "legacy" => AsyncMethodCount.LegacyRows(peReader).ToString(),
        "hand" => AsyncMethodCount.HandRolled(peReader).ToString(),
        "hand-flat" => HandFlat(peReader.GetMetadataReader()).ToString(),
        "nlinq" => new MethodRows(peReader.GetMetadataReader())
            .Where<MethodRows, MethodRow, IsCountedAsync>(default)
            .CountFold<Filter<MethodRow, MethodRows, IsCountedAsync>, MethodRow>().ToString(),
        "planned" => AsyncMethodCount.Planned(path, peReader).ToString(),
        "hand-exists" => HandExists(peReader.GetMetadataReader()) ? "true" : "false",
        "nlinq-exists" => new MethodRows(peReader.GetMetadataReader())
            .Any<MethodRows, MethodRow, IsCountedAsync>(default) ? "true" : "false",
        "planned-exists" => AsyncMethodCount.PlannedExists(path, peReader) ? "true" : "false",
        "unsafe-planned" => ILInspector.Analysis.Planning.UnsafeEvidencePresence.HasEvidence(path, image) ? "true" : "false",
        _ => throw new ArgumentException(variant),
    };
}

// The hand-rolled loop without hoisting the type filter: the same per-method work the
// planned and NLinq variants do, so hoisting and planning overhead separate.
static int HandFlat(MetadataReader reader)
{
    int count = 0;
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (AsyncMethodScope.IsCounted(reader, type, reader.GetMethodDefinition(methodHandle)))
                count++;
        }
    }

    return count;
}

static bool HandExists(MetadataReader reader)
{
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (AsyncMethodScope.IsCounted(reader, type, reader.GetMethodDefinition(methodHandle)))
                return true;
        }
    }

    return false;
}

readonly struct MethodRow(MetadataReader reader, TypeDefinition type, MethodDefinition method)
{
    public MetadataReader Reader => reader;
    public TypeDefinition Type => type;
    public MethodDefinition Method => method;
}

struct IsCountedAsync : IFunc<MethodRow, bool>
{
    public bool Invoke(MethodRow row) => AsyncMethodScope.IsCounted(row.Reader, row.Type, row.Method);
}

/// <summary>An NLinq source over every method definition, type by type.</summary>
struct MethodRows : NLinq.IEnumerator<MethodRows, MethodRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    MethodDefinitionHandleCollection.Enumerator _methods;
    TypeDefinition _type;
    bool _inType;

    public MethodRows(MetadataReader reader)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
    }

    public MethodRow TryGetNext(out bool hasMore)
    {
        while (true)
        {
            if (_inType && _methods.MoveNext())
            {
                hasMore = true;
                return new(_reader, _type, _reader.GetMethodDefinition(_methods.Current));
            }

            if (!_types.MoveNext())
            {
                hasMore = false;
                return default;
            }

            _type = _reader.GetTypeDefinition(_types.Current);
            _methods = _type.GetMethods().GetEnumerator();
            _inType = true;
        }
    }

    static TAcc NLinq.IEnumerator<MethodRows, MethodRow>.Fold<TAcc, TFunc>(scoped ref MethodRows @this, TAcc accumulator, TFunc func)
    {
        MetadataReader reader = @this._reader;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                accumulator = func.Invoke(accumulator, new MethodRow(reader, type, reader.GetMethodDefinition(methodHandle)));
        }

        return accumulator;
    }
}
