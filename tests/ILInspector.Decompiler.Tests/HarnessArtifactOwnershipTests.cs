using System.Text;

using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class HarnessArtifactOwnershipTests
{
    [Fact]
    public void ProductWholeMemberSplice_PreservesConstructorArtifact()
    {
        const string member = """
                private Fixture()
                {
                    Consume(,);
                }
            """;

        var emitted = new StringBuilder();
        FidelityCheck.EmitPrerenderedMember(member, emitted, "        ");
        string source = emitted.ToString();

        Assert.Contains("private Fixture()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Fixture()", source, StringComparison.Ordinal);
        Assert.Contains("Consume(,);", source, StringComparison.Ordinal);
    }
}
