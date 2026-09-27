using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Computes the shared per-unit key of a fused pass once, with the pass's type
/// scope. A struct, so the fused kernel calls it directly.
/// </summary>
internal interface IUnitClassifier<TKey>
{
    bool TypeInScope(MetadataReader reader, TypeDefinition type);

    TKey Classify(ref MethodDefinitionUnit unit);
}

/// <summary>
/// One closed query in a fused pass: it consumes each unit's shared key and
/// folds its own terminal. <see cref="IsDone"/> reports a settled Exists.
/// </summary>
internal interface IFusedSink<TKey>
{
    bool IsDone { get; }

    void Accept(ref MethodDefinitionUnit unit, TKey key);
}

/// <summary>Two sinks composed into one type, so a whole constant request is one nested type.</summary>
internal struct FusedPair<TFirst, TSecond, TKey> : IFusedSink<TKey>
    where TFirst : struct, IFusedSink<TKey>
    where TSecond : struct, IFusedSink<TKey>
{
    public TFirst First;
    public TSecond Second;

    public readonly bool IsDone => First.IsDone && Second.IsDone;

    public void Accept(ref MethodDefinitionUnit unit, TKey key)
    {
        if (!First.IsDone)
            First.Accept(ref unit, key);
        if (!Second.IsDone)
            Second.Accept(ref unit, key);
    }
}

/// <summary>
/// A typed fused kernel: one traversal specialized to a classifier and a
/// nested sink type, with no per-unit dispatch. It stops before advancing the
/// enumerators once every sink is done, as the reference executor does.
/// </summary>
internal static class FusedKernel
{
    public static int Run<TClassifier, TKey, TSinks>(
        PEReader peReader,
        ref TSinks sinks)
        where TClassifier : struct, IUnitClassifier<TKey>
        where TSinks : struct, IFusedSink<TKey>
    {
        MetadataReader reader = peReader.GetMetadataReader();
        TClassifier classifier = default;
        var unit = new MethodDefinitionUnit(reader, peReader, lookup: null);
        int visited = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
            if (!classifier.TypeInScope(reader, typeDefinition))
                continue;

            foreach (MethodDefinitionHandle methodHandle in typeDefinition.GetMethods())
            {
                unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                visited++;
                try
                {
                    sinks.Accept(ref unit, classifier.Classify(ref unit));
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                {
                    throw new InvalidDataException(
                        $"{unit.Label} could not be analyzed: {ex.Message}", ex);
                }

                if (sinks.IsDone)
                    return visited;
            }
        }

        return visited;
    }
}
