extern alias lookalike;

using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata.SourceProvenanceFixtures;

public sealed class OrdinaryType
{
    public int Value() => 42;

    [CompilerGenerated]
    public int LoweredMemberMarkerDoesNotClassifyType() => Value();

    public async Task<int> AsyncValue()
    {
        await Task.Yield();
        return Value();
    }
}

public abstract class MixedMethodMarkerType
{
    public int Ordinary() => 1;

    [GeneratedCode("FixtureGenerator", "1.0")]
    public abstract int Generated();
}

[GeneratedCode("FixtureGenerator", "1.0")]
public partial class MixedTypeMarkerType
{
    public int Ordinary() => 2;
}

[CompilerGenerated]
public sealed class CompilerSynthesizedType
{
    public int Value() => 3;

    public sealed class InheritedCompilerSynthesizedType
    {
        public int Value() => 4;

        public sealed class DeepInheritedCompilerSynthesizedType
        {
            public int Value() => 5;
        }
    }
}

public interface NoDocumentType
{
}

[GeneratedCode(null, null)]
public interface NullGeneratedCodeArguments
{
}

[lookalike::System.CodeDom.Compiler.GeneratedCode(
    "UntrustedLookalike",
    "1.0")]
[lookalike::System.Runtime.CompilerServices.CompilerGenerated]
public sealed class LookalikeMarkedType
{
    public int Value() => 6;
}

public sealed record GeneratedModel(int Value);

[JsonSerializable(typeof(GeneratedModel))]
public partial class FixtureJsonContext : JsonSerializerContext;
