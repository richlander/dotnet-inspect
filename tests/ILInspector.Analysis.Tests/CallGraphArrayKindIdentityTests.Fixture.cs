using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Tests;

public sealed partial class CallGraphArrayKindIdentityTests
{
    internal static byte[] BuildProjectionFlowImage(string specimen)
    {
        var metadata = CreateMetadata();
        byte[] signatureType = specimen switch
        {
            "Vector" => Sz(Int32),
            "RankOneNonSz" => MdArray(Int32, rank: 1),
            "RankTwo" => MdArray(Int32, rank: 2),
            "NestedRankOneNonSz" => GenericInstance(
                isValueType: false,
                AddListReference(metadata),
                MdArray(Int32, rank: 1)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(specimen),
                specimen,
                "Unknown call-graph projection specimen."),
        };

        return BuildImage(
            [
                new("M", signatureType, signatureType, IsGeneric: false),
            ],
            metadata);
    }

    static TypeReferenceHandle AddListReference(MetadataBuilder metadata)
    {
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        return metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
    }
}
