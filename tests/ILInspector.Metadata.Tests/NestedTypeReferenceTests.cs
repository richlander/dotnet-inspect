using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Pins nested-type qualification in metadata signature rendering (#1324). A
/// nested type reached through a <see cref="TypeReference"/> (a nested type from
/// a referenced assembly) must be qualified through its declaring type, the same
/// as a <see cref="TypeDefinition"/> — otherwise <c>ImmutableArray`1+Builder</c>
/// rendered as a bare <c>Builder&lt;T&gt;</c> (the enclosing arguments attached to
/// the leaf), which is CS0246 and poisons changed-method compile-back.
/// </summary>
public class NestedTypeReferenceTests
{
    static string ImmutableArrayPath => typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location;

    [Fact]
    public void SignatureDecoder_QualifiesNestedTypeReferenceThroughDeclaringType()
    {
        using var pe = new PEReader(File.OpenRead(ImmutableArrayPath));
        var reader = pe.GetMetadataReader();

        string? returnType = null;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            // The non-generic static `ImmutableArray` factory class (the generic
            // value type is `ImmutableArray`1`), which declares CreateBuilder<T>.
            if (reader.GetString(typeDef.Name) != "ImmutableArray")
                continue;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != "CreateBuilder")
                    continue;
                // ImmutableArray.CreateBuilder<T>() returns ImmutableArray<T>.Builder,
                // a nested type of the referenced generic ImmutableArray<T>.
                var signature = method.DecodeSignature(SignatureDecoder.Instance, new GenericContext(["T"], ["T"]));
                returnType = signature.ReturnType;
                break;
            }
            if (returnType is not null)
                break;
        }

        Assert.NotNull(returnType);
        Assert.Equal("System.Collections.Immutable.ImmutableArray<T>.Builder", returnType);
    }
}
