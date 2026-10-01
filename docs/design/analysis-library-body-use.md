# Analysis Library body use

## Status and ownership

This focused `ILInspector.Analysis` design is tracked by
[#8864](https://github.com/richlander/dotnet-inspect/issues/8864). It supplies
the body-use producer available to the future depth mode of the
[Library Metrics Type structural-leverage extension](library-structural-report.md#type-structural-leverage).

**Analysis Library Body Use** is the single normative owner established here.
Its exact claim is:

> Given one exact supported ECMA-335 assembly image and one explicit
> whole-Library body-use request, safely examine every admitted managed IL
> body, resolve typed operands that name Types defined in that exact image,
> attribute Roslyn-generated physical bodies to their declared owners by
> Roslyn's emission shape, and publish detached source-to-target occurrence
> evidence with exact image identity, coverage, work, fidelity
> qualification, and visible failures.

This owner defines Analysis construction, logical ownership, binding, and
qualification only. It does not define Metadata signature use, Graph degree,
Research rankings or roles, CLI rendering, Browser presentation, or
provider-backed acquisition.

## Fidelity and security

The fidelity claim covers Roslyn-produced assemblies, C# and Visual Basic,
including SDK-trimmed output. Logical ownership follows Roslyn's emission shape directly, as
[Logical ownership](#logical-ownership) states. For output from other tools
(rewriters, custom trimming, hand-authored or adversarial metadata), an
ownership answer may be wrong but is never insecure. Exists, Count, and Rows
over this population share that one fidelity.

Security covers every supported ECMA-335 input, regardless of compiler. The
image remains untrusted: instruction decode, token resolution, relationship
construction, retained evidence, and diagnostics are bounded and fail
visibly. A compiler shape cannot cause unbounded work, process failure, or
partial success-shaped publication.

Ownership attribution decodes no body other than the one it attributes, so
the per-body instruction limit bounds each body's decode alone.

The result distinguishes:

- **logical fidelity**, where the body's Roslyn emission shape names its
  declared owner;
- **physical only**, where the body is safe to inspect but logical ownership
  is outside the fidelity contract; and
- **unavailable**, where malformed or bounded evidence prevents trustworthy
  physical publication.

Physical-only evidence is not malformed evidence. It retains its physical
owner and exact qualification but cannot silently participate as a logical
owner.

## Logical ownership

Attribution reads Roslyn's emission shape. It does not authenticate it.
Authenticated substrates such as `StateMachineRelationshipIndex` remain the
owners of explicitly requested relationship facts; body use asks only which
declared Type a body's uses belong to, and Roslyn's shape answers that exactly.

For one physical MethodDef body:

1. **State-machine role.** When the body's declaring Type is a nested Type
   with `MethodImpl` rows, its host is the Type that declares it. No
   state-machine name is assumed, because the lowering names differ by
   language (`<M>d__N` in C#, `VB$StateMachine_N_M` in Visual Basic). A host
   method carrying `AsyncStateMachineAttribute`,
   `AsyncIteratorStateMachineAttribute`, or `IteratorStateMachineAttribute`
   is a kickoff; the attribute's serialized `System.Type` argument names the
   state machine by its innermost nested segment. A body is a role
   implementation when an explicit `MethodImpl` of that state machine binds
   it to a role declaration: `MoveNext` and `SetStateMachine` for async;
   those plus `MoveNextAsync` and `DisposeAsync` for an async iterator; and
   `MoveNext` and `Dispose` for an iterator. A role body continues as its
   kickoff. A Type claimed by two kickoffs, or a name two nested Types share,
   maps no role.
2. **Lifted method.** A method whose name is a canonical lifted lambda or
   local-function name belongs to the innermost declaring Type whose name
   does not start with `<>`. Roslyn places closure Types (`<>c`,
   `<>c__DisplayClass…`) inside the Type that declares the source method, so
   this also covers a lifted kickoff, such as an async lambda's. A name with a
   lifted marker that is not canonical is rejected. The canonical names are
   C#'s. Visual Basic lambdas (`_Lambda$__N-M` in `_Closure$__…`) fall to
   rule 3 and stay physical only, as authenticated ownership also left them.
3. **Otherwise** the method belongs to its declaring Type, unless the method
   or that Type carries `CompilerGeneratedAttribute`; then the body is
   physical only. Closure constructors, a state machine's non-role members,
   auto-property accessors, and record-synthesized members are physical only.

The rule is linear in metadata: each host's methods and attributes are read
once, attribute constructors, value blobs, and Type answers are memoized, and
a declaring chain walk is bounded by
`MetadataSafetyPolicy.MaxRelationshipNodes`. A state-machine name longer than
`MetadataSafetyPolicy.MaxTypeNameCharacters` fails its host. A host whose
scan fails fails the body of every nested Type with `MethodImpl` rows that it
hosts as malformed, including bodies unrelated to state machines. This breadth is intended: the scan
cannot tell which of the host's generated bodies it would have attributed, so
none is published as if the host were readable.

Each distinct lifted-method name is classified once per execution. A name
longer than `MetadataSafetyPolicy.MaxTypeNameCharacters` rejects generated
ownership visibly without repeated materialization.

Attribution changes only which Type a body's uses belong to. Body use owns the
facts it consumes, and failures of those facts remain visible. These make the
body malformed or limited, or the operand unavailable, with a typed diagnostic:

- an unreadable or over-limit IL body, and an instruction or occurrence limit;
- an operand token that does not resolve, or resolves to the wrong kind;
- a malformed or structurally over-limit method signature, whether the body's
  own or an operand's;
- an unreadable method operand's declaring Type;
- a malformed TypeSpec, and a MethodSpec invalid for its target or its
  caller's generic scope; and
- a current-image reference that cannot be bound.

For a method operand, body use reads only the declaring Type, the method
signature's structural validity and generic arity, and any MethodSpec
instantiation and its validity for the target and caller. It does not read the
callee name, generic-parameter names, parameter rows, parameter Types, or
return Type. Those unconsumed facts and full member resolution's identity and
decode accounting do not affect body-use availability.

This is a deliberate fidelity boundary. Roslyn-produced output has exact
answers. On other output, an unconsumed malformed callee fact may make full
member resolution fail while body use remains available; the body-use answer
may therefore be wrong but remains bounded and inert, as
[Fidelity and security](#fidelity-and-security) allows.

Each method signature's structural outcome, each MethodSpec instantiation's
decode (per blob) and validation (per blob, target generic arity, and caller
generic arities), and each operand's binding is retained per execution, a
recoverable failure included as its diagnostic description. A malformed blob
or operand therefore fails every use visibly, with the same detail, without
repeating its decode or rethrowing a shared exception.

The producer charges each uniquely inspected method-signature or MethodSpec
blob once against its method-signature byte budget. Exhausting that
producer-global budget aborts the whole execution and publishes no body-use
result; it never turns the remaining operands into a partial success-shaped
population.

Attribution never requires the owner's body to reference the lifted body. A
local function whose calls Roslyn elided, such as a `[Conditional("DEBUG")]`
helper, still belongs to the Type that declares it.

## Identity and population

One result is bound to the assembly identity and non-empty module MVID read
from the same image. Its Type inventory contains every TypeDef except the
metadata `<Module>` pseudo-type, independent of accessibility. The pseudo-type
is the top-level empty-namespace `<Module>` definition; an authored nested Type
whose leaf name is `<Module>` remains in the population. Bodies physically
declared on the pseudo-type are therefore outside the admitted body population:
they have no Type endpoint and do not enter coverage, physical evidence, or
occurrence projection. Typed operands that resolve to it likewise publish no
relationship.

MVID plus TypeDef token is the endpoint currency. Structured names support
display, deterministic ordering, and exact same-image binding; they do not
replace physical identity. Duplicate exact definition names reject the image
instead of selecting one.

For logical source Type `A` and local target Type `B`, one physical typed
operand produces `A -> B` when:

- the operand occurs in one admitted managed IL MethodDef body;
- Analysis resolves its named Type definitions;
- `B` is defined in the exact inspected image; and
- the physical body has logical-fidelity ownership for `A`.

A physical-only body retains qualified operand evidence but does not
manufacture a logical source relationship.

A state-machine role body continues as its kickoff before lifted-method
attribution, so an async lambda's generated kickoff Type never becomes a
logical endpoint.

Typed method, field, Type, and method-instantiation operands participate.
Constructed shapes contribute every contained named definition. Intrinsic
primitives and foreign definitions do not enter the Library-local population.
A named reference whose exact scope identifies the current image but whose
definition cannot be bound is unavailable evidence, not a foreign omission.

Each occurrence retains:

- exact logical-source and target TypeDef addresses and structured names;
- the physical MethodDef address;
- IL offset and operand kind;
- the physical operand token; and
- the zero-based ordinal when one operand contributes several named Types.

Parallel and self occurrences remain distinct. Analysis does not deduplicate
peers, remove self relationships, rank Types, or aggregate evidence.

## Producer Planning contract

The work is declared through
[Producer Planning](producer-planning.md), not another
`LibraryBodyAnalysisFeatures` path or `LibraryBodyIndex` projection.

The body-use producer is a method-definition producer. Its shipping consumer
closes it with the Rows terminal. The scorecard also exercises its internal
Exists and Complete terminals as performance oracles; exposing those questions
to additional product consumers requires QuerySpace adoption rather than a
parallel public query API. The producer declares the smallest applicable
combination of:

- declaration metadata;
- managed body;
- execution-scoped module lookup; and
- identity text needed only by retained rows.

The producer visits units handed to it; it never iterates the Library.
Producer Planning closes dependencies and records participation. QuerySpace
and source delegation own scheduling, collapse, and later acquisition
pushdown. Graph owns structural execution over the admitted rows.

Research's later Graph composition is a higher-tier completion requiring the
Metadata signature-use and Analysis body-use results. It does not reopen
either image or repeat operand resolution.

## Completion and result

Coverage accounts for bodies and typed operands:

```text
body considered = examined + physical-only + unavailable + limited
operand considered = examined + unavailable + limited
```

One available result contains:

- exact image identity and Analysis participation receipt;
- canonical Type inventory;
- canonical logical body-use occurrences;
- qualified physical-only evidence;
- body and operand coverage;
- `Complete`, `Qualified`, or `Partial` disposition; and
- typed diagnostics for unavailable or limiting evidence.

`Complete` requires every admitted body and typed operand to complete with
logical fidelity. `Qualified` retains safe physical-only bodies without
claiming Roslyn fidelity. `Partial` means malformed, unavailable, or bounded
evidence prevented trustworthy examination. An empty complete occurrence
collection proves no admitted local body-use occurrences.

A failure that prevents exact image identity or a trustworthy Type inventory
rejects the operation and publishes no population. Cancellation propagates.
An operand's rows commit atomically after classification, resolution, nested
shape validation, and same-image binding. A global occurrence rejection keeps
only the body's fixed-size physical summary; rejected occurrence arrays do not
remain retained.

## Shared and specialized work

The producer reuses:

- `LibraryBodyAnalysisService` for exact-image execution;
- `MethodDefinitionProducer` and its typed outcome and receipt;
- `ILInspector.Instructions` for bounded decode and physical offsets;
- the execution-scoped module lookup for
  [logical ownership](#logical-ownership);
- existing Analysis generic-scope and same-image token resolution; and
- `MetadataTypeDefinitionAddress` and structured Type names.

The specialized stage retains Library-local named Type occurrences. It does
not require complete call, allocation, value-flow, implementation-profile, or
optimization evidence when typed operand resolution alone is sufficient. It
does not add a second token decoder where an existing Analysis resolver owns
the same semantics.

## Consumer boundary

The future Research depth mode may consume the detached result and:

- maps canonical Types to Graph nodes;
- maps complete logical occurrences to typed Graph relationships;
- selects body incoming and body outgoing distinct-neighbor degree;
- applies its explicit self-loop policy;
- keeps body evidence distinct from the Metadata signature surface mode; and
- preserves Analysis qualification in the report.

Research does not turn physical-only evidence into logical relationships,
repair incomplete evidence, infer identity from display text, or treat a
missing relationship as an examined zero.

The current Type structural-leverage surface mode does not acquire this
body-use population. Adopting the depth mode requires separate cost and
qualification evidence and must not silently change the meaning of the
signature-only surface rankings.

The later QuerySpace adoption may push selected relationship demand into
acquisition. It must preserve this closed-document result's identities,
ordering, qualification, coverage, work, and failures.

## Before/after evidence

Stage 4 in [#8825](https://github.com/richlander/dotnet-inspect/issues/8825)
compares:

- **Before:** a frozen #8732-derived direct SRM/IL reference extractor in the
  Analysis performance-oracle harness.
- **After:** this Analysis-owned qualified body-use operation.

Both routes run at one frozen candidate head over System.Text.Json 10.0.0 and
the .NET 11 RC1 System.Private.CoreLib. On their shared Roslyn-produced scope,
the gate compares exact canonical logical-source, physical-body, target,
operand-kind, IL-offset, and occurrence-ordinal rows and completion before
reporting warm elapsed time and allocation. The product route additionally
records Analysis work and participation.

This is a same-head implementation comparison and a richer-producer cost
baseline. Provider-backed acquisition remains Stage 8.

### LINQ and NLinq scorecard

The producer's semantic row unit is one retained logical body-use occurrence:
one declared source Type, one local target Type, and one typed operand
occurrence with its physical coordinates. The scorecard exercises three
Producer closings over that exact row population:

- **Exists** stops after the first retained logical occurrence. A settled true
  answer carries prefix coverage and diagnostics with `Settled` disposition.
  A false answer completes the population and retains its complete,
  qualified, or partial disposition.
- **Count** completes the population and returns the exact retained logical
  occurrence count with disposition, coverage, and diagnostics, without
  materializing occurrence, canonical-Type, or physical-evidence rows.
- **Rows** preserves the complete product result: disposition, full canonical
  Type inventory, named ordered occurrences, physical-only evidence, body and
  operand coverage, and typed diagnostics.

Every closing performs the same bounded root Type-inventory admission and the
same per-body fidelity and containment work it reaches. Count and false Exists
therefore cannot turn unavailable, qualified, limited, or malformed evidence
into a success-shaped scalar. These scalar closings are internal scorecard
questions, not a public product query surface. QuerySpace adoption owns their
future product exposure plus Head, Tail, Window, selection, and provider-backed
demand.

The performance scorecard asks all three internal terminal questions. Every
column constructs the same product-owned answer for the selected closing. The
scorecard excludes only the Planner execution receipt: an oracle cannot
truthfully manufacture Producer Planning participation and work without
invoking the Planner itself.

The scorecard runs four columns:

- **Direct** is the existing explicit MethodDef traversal.
- **LINQ** is an idiomatic streaming `System.Linq` traversal: `Any` for Exists,
  `Count` over admitted occurrence rows, and collection materialization for
  Rows.
- **NLinq** uses the pinned NLinq MethodDef source and closes the admitted
  occurrence rows with `Any`, `CountFold`, or `ToList`.
- **Planner** executes the product-owned producer under Producer Planning with
  the selected terminal.

Direct, LINQ, and NLinq independently traverse the MethodDef population. They
share the Analysis-owned per-method body-use kernel because its instruction
decode, logical ownership, operand binding, fidelity, and containment are the
question being composed, not alternative query machinery. They do not invoke
Producer Planning or consume Planner output. Every column performs bounded
Type-inventory admission, uses the same body, occurrence, and
method-signature limits, and feeds the product-owned terminal-aware
accumulator for admission, qualification, and diagnostics. Each comparator
then owns its terminal: Exists stops at the first settling fact, Count counts
the admitted occurrence-row stream, and Rows materializes that stream before
calling the product-owned public projection. The scorecard checks every public
scalar, disposition, coverage field, diagnostic, and, for Rows, projected
array in order before timing.
Comparator traversal also preserves the production completion boundary:
cancellation is observed before and during population traversal, recoverable
body-acquisition failure becomes per-method unavailable evidence, and a
producer-global critical abort becomes the same atomic rejection as Planner.
Every rejected execution retains and compares the public rejection kind and
exact detail, including bounded or unsupported Type-inventory admission.

NativeAOT is the only accepted timing. The report identifies the exact
candidate, assets, source locations, pinned NLinq provenance, invocation,
per-closing answer hashes, absolute medians, allocation, and ratios to NLinq.
It runs the Roslyn fidelity assets and the body-use ECMA safety fixtures. A
faster fair oracle is evidence for Planner improvement; the oracle is not
burdened with unconsumed Planner work.

The four implementations and report live in
[`BodyUseScorecard.cs`](../../tools/AnalysisHarness/BodyUseScorecard.cs).
NLinq traverses the shared
[`MethodDefinitionRows`](../../tests/DotnetInspector.PerformanceOracles/MethodDefinitionRows.cs)
source. Its exact upstream pin and checksums remain in
[`PROVENANCE.md`](../../tests/NLinq.Oracle/PROVENANCE.md).

Publish and run the scorecard for the target RID:

```bash
dotnet publish tools/BodyUseScorecard -c Release -r <rid> \
  -o artifacts/body-use-scorecard
artifacts/body-use-scorecard/analysis-harness check <assembly>...
artifacts/body-use-scorecard/analysis-harness time \
  --rounds 6 --budget-ms 2000 --tsv <path> <assembly>...
```

## Required evidence

Release gates prove:

- typed method, field, Type, and method-instantiation operand participation;
- constructed shapes, parallel occurrences, and self relationships;
- exact local binding and foreign-target omission;
- Roslyn async, iterator, local-function, lambda, and top-level ownership,
  with only state-machine role bodies continuing as their kickoff;
- compiler-generated non-lifted members kept physical only, and bounded,
  visibly failing handling of non-Roslyn or unrecognized lowering;
- malformed bodies and tokens, unresolved operands, duplicate identities, and
  exact work limits;
- atomic operand publication and retained healthy evidence; and
- detached deterministic results.

System.Text.Json 10.0.0 proves representative Roslyn package behavior.
System.Private.CoreLib from SDK `11.0.100-rc.1.26425.128` proves scale,
all-accessibility participation, and deterministic completion.
