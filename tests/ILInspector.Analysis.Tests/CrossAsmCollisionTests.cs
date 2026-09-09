extern alias collisionfix;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests
{
    public class CrossAsmCollisionTests
    {
        // #1804 review (High). An external REFERENCE type whose namespace+name collide with an
        // in-assembly STRUCT must remain a heap allocation. A display name omits assembly
        // identity, so a name-based value-type shortcut keyed on ToQualifiedDisplayString would
        // wrongly drop this `newobj`; the operand-token metadata path keeps it counted.
        // Non-vacuous: under the removed name-set shortcut this returned 0.
        [Fact]
        public void Allocations_ExternalRefTypeSharingNameWithInAssemblyStruct_StaysCounted()
        {
            var index = LibraryBodyIndex.Open(typeof(CrossAsmCollisionConsumer).Assembly.Location);
            int token = index.Methods
                .First(method => method.Name == nameof(CrossAsmCollisionConsumer.ConstructsExternalCollisionRefTypeInLoop))
                .MetadataToken;

            Assert.True(index.GetMethodSignals().GetValueOrDefault(token, MethodSignals.None).Allocations >= 1);
        }
    }

    // Constructs the EXTERNAL collisionfix::CrossAsmCollision.SharedShape (a reference type) in a
    // loop, while the in-assembly CrossAsmCollision.SharedShape is a struct with the same
    // fully-qualified display name.
    public static class CrossAsmCollisionConsumer
    {
        static int Sink(collisionfix::CrossAsmCollision.SharedShape shape) => shape.Value;

        public static int ConstructsExternalCollisionRefTypeInLoop(int n)
        {
            int total = 0;
            for (int i = 0; i < n; i++)
                total += Sink(new collisionfix::CrossAsmCollision.SharedShape(i));
            return total;
        }
    }
}

namespace CrossAsmCollision
{
    // In-assembly STRUCT sharing the exact fully-qualified name of the external reference type
    // collisionfix::CrossAsmCollision.SharedShape. Defining it (it need not be constructed) is
    // enough to populate any in-assembly value-type name set, exercising the collision.
    public struct SharedShape
    {
        public int Value;
        public SharedShape(int value) => Value = value;
    }
}

