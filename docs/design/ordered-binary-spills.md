# Ordered binary operand spills

## Owner and claim

Expression inlining owns this contract: two exclusive, adjacent synthetic
spills can become the direct operands of a binary expression when the whole
replacement preserves their evaluation order and operand types. The printer
receives the resulting expression, not a request to rediscover temporary
ownership.

This is a focused continuation of [value-typed emission](value-typed-emission.md)
and issue #7611. It does not change scalar-update recognition, numeric binding,
or overflow-context spelling.

## Admission and boundaries

The consumer is a direct binary value stored to a local or argument. Both
operands must be direct loads of the two immediately preceding stack-slot
stores in the same block. Each spill has exactly one definition and one value
use in the existing ownership inventory; each stored value has the same known
type as its load. Both loads remain unconditional and their order agrees with
the order of the stores.

The run folds atomically. Inlining either spill alone can require a purity
claim that the original ordered sequence does not need: the left operand may
read mutable storage and the right operand may call, mutate, or throw.
The shared ordered-spill proof already used for returned call arguments is
the analogous implementation; a binary evaluates both operands left to right
before performing its operation, just as a call evaluates its arguments before
invocation.

User locals are consumers, not removable spill storage. Extra uses or
definitions, unknown or changed operand types, intervening statements,
different blocks, nested operand loads, and await-bearing spill values decline
this fold. Field, property, array, and indirect destinations retain their
existing owners because evaluating their destination can precede the RHS.
Declining this fold does not prohibit an existing independent inlining rule
from proving a smaller replacement.
The compiler-produced nested conditional in #7686 remains a cross-block
continuation case, not an admitted two-spill run; its unchanged opcode
difference is not certified by this slice.

## Raise obligations

**Lowering shell:** Roslyn Release leaves the first operand on the evaluation
stack while a conditional selects the second. Import and diamond raising
represent these values as two stack-slot stores followed by their binary
consumer. The compiler-produced conditional-update fixture and the real
`System.Data.RBTree<K>.RecomputeSize` in .NET 11 RC1 pin this shape.

**Consumed ownership:** remove only the two exclusive spill stores and replace
their direct loads with the original value nodes. No source local, destination,
branch, or expression inside either value is consumed. Moving the original
nodes retains their IL-origin evidence.

**Control flow:** both stores and the consumer are in one block. The fold
changes no successors, exception regions, or structured transfers. Conditional
evaluation within either operand remains within that same operand.

**Replacement:** preserve the binary kind, signedness, checkedness, and each
operand's type and internal evaluation. Changed evaluation order or count,
changed overflow context, a lost type witness, newly invalid C#, or a native
compile-back regression falsifies the claim.

## Adoption and gates

The existing expression-inlining runs adopt the fold in the shared Default and
Lowered pipelines, including separately raised nested bodies. CLI and
Browser/Wasm consume the same decided IR without host-specific matching.
No printer logic or new representation is required.

Release `OrderedBinarySpillTests` covers the compiler-produced shape,
exclusive ownership and type boundaries, mutation and exception ordering,
idempotence, and the existing IR/coercion invariants. Its focused native
compile-back cases use product-owned artifacts without a repair floor.
The fixed real-library witness receives separate before/after native evidence;
fixture results do not substitute for its outcome.
