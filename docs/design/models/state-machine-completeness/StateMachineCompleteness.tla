---------------------- MODULE StateMachineCompleteness ----------------------
(***************************************************************************)
(* A model of the completeness invariants in                               *)
(* docs/design/state-machine-relationship-index.md.                        *)
(*                                                                         *)
(* This models the CONTRACT, not the implementation. It abstracts away     *)
(* metadata decoding, signature matching, and every specific budget. What  *)
(* it keeps is the part that is genuinely stateful and therefore hard to   *)
(* check by reading prose:                                                 *)
(*                                                                         *)
(*   - construction visits structural machines one at a time and may fail  *)
(*     for the whole module at any point;                                  *)
(*   - rejections merge into shared components as claims are discovered,   *)
(*     in an order the compiler chooses, not one we control; and           *)
(*   - a whole-module failure must reject every machine, while a per-claim *)
(*     refusal rejects only what its claim reaches.                        *)
(*                                                                         *)
(* The properties checked here are C1, C2, C3 and C5 of that document,     *)
(* plus absorption of failure. C4 is about what a consumer may infer and   *)
(* C6 is about an external observer; neither is a statement about system   *)
(* state, so neither is modeled directly -- though C1 is stated the way C6 *)
(* licenses, against an independently modeled population.                  *)
(***************************************************************************)

EXTENDS FiniteSets, Naturals

CONSTANTS
    MachineCount, \* how many structural state machines the image holds
    MaxRejections,\* how many rejection components may be published
    MaxBudget,    \* construction steps available before exhaustion
    FailureMode,  \* how whole-module failure rewrites results
    MergeMode,    \* how far a publication's merge reaches
    FinishMode    \* what construction requires before publishing

(***************************************************************************)
(* The three mode constants exist so that each deliberate counterexample is *)
(* a committed, re-runnable configuration rather than a described edit. A   *)
(* property no mutation can falsify is not checking anything, and a README  *)
(* table asserting that one was falsified is exactly the ungated prose      *)
(* claim this model was written to eliminate. See README.md.                *)
(*                                                                          *)
(*   FailureMode = "Total"              whole-module failure rejects all    *)
(*               = "PreserveClassified" leaves earlier results standing     *)
(*               = "ReportAbsent"       reports Absent instead of Rejected  *)
(*                                                                          *)
(*   MergeMode   = "Component"          publishing unions pre-existing      *)
(*                                      components, as MergeExisting does   *)
(*               = "NewClaimOnly"       publishing unions only the machines *)
(*                                      the new claim names                 *)
(*               = "NamedPairOnly"      evidence reaches only the new claim *)
(*               = "LatestClaim"        newest claim's kind wins, not the   *)
(*                                      earliest-published one              *)
(*                                                                          *)
(*   FinishMode  = "Guarded"            publish only once every machine is  *)
(*                                      classified                          *)
(*               = "AllowUnvisited"     publish with rows unclassified      *)
(*               = "DropAsAbsent"       publish a lost row as Absent        *)
(***************************************************************************)
ASSUME
    /\ MachineCount \in Nat \ {0}
    /\ MaxRejections \in Nat \ {0}
    /\ MaxBudget \in Nat
    /\ FailureMode \in {"Total", "PreserveClassified", "ReportAbsent"}
    /\ MergeMode \in
        {"Component", "NewClaimOnly", "NamedPairOnly", "LatestClaim"}
    /\ FinishMode \in {"Guarded", "AllowUnvisited", "DropAsAbsent"}

(***************************************************************************)
(* Machines are indices rather than opaque names purely so that component  *)
(* ids have a finite domain TLC can enumerate. Nothing below depends on    *)
(* their order.                                                           *)
(***************************************************************************)
Machines == 1..MachineCount

(***************************************************************************)
(* Results a query can return. `Unclassified` is not a result the index can *)
(* publish -- it is the modeling stand-in for a row construction has not    *)
(* reached yet.                                                            *)
(***************************************************************************)
Results == {"Resolved", "Rejected", "Absent", "Unclassified"}

(***************************************************************************)
(* Ground truth: what each machine actually is, independent of anything the *)
(* index does. This is the model's most important variable, and an earlier  *)
(* revision did not have it. Without it C1 was vacuous: it forbade only     *)
(* `Unclassified`, and a real index that loses a row does not publish       *)
(* `Unclassified` -- it publishes `Absent`, which is indistinguishable from *)
(* a machine that genuinely has no claim. Totality is only checkable        *)
(* against an independently known population, which is what C6 says and     *)
(* what `truth` supplies here.                                              *)
(***************************************************************************)
Truths == {"Resolvable", "Refused", "NoClaim"}

Expected(t) ==
    CASE t = "Resolvable" -> "Resolved"
      [] t = "Refused"    -> "Rejected"
      [] OTHER            -> "Absent"

\* The two ways construction can fail for the whole module.
FailureKinds == {"Malformed", "BudgetExceeded"}

(***************************************************************************)
(* Evidence a rejection carries, as small integers. Read them as distinct   *)
(* per-claim refusal kinds such as Unresolved, Ambiguous, or Duplicate.     *)
(*                                                                          *)
(* One value stands for the (Kind, Detail) pair. That is faithful because   *)
(* production captures both together from a single component in one         *)
(* expression -- `new(component.Kind, component.Detail)` -- so no reachable  *)
(* behavior takes them from different components. A model with two          *)
(* independent fields would add states without adding a checkable claim.     *)
(***************************************************************************)
RejectionKinds == 1..2
NoEvidence == 0

Phases == {"Building", "Built", "Failed"}

RejectionIds == 1..MaxRejections

VARIABLES
    truth,        \* Machines -> Truths; the population an external check recomputes
    phase,        \* Building, Built, or Failed
    kind,         \* failure kind once phase = "Failed", else "None"
    result,       \* Machines -> Results
    component,    \* Machines -> component id; equal ids merge as one rejection
    evidence,     \* Machines -> the kind the component settled on
    published,    \* how many rejection components have been published
    covers,       \* RejectionIds -> the machines a published rejection names
    claimKind,    \* RejectionIds -> that rejection's own kind
    visited,      \* machines construction has already classified
    budget        \* remaining construction steps

vars ==
    <<truth, phase, kind, result, component, evidence, published, covers,
      claimKind, visited, budget>>

(***************************************************************************)
(* The publication is the unit, not the machine. `RejectClaims` builds ONE  *)
(* `RejectionComponent` from a LIST of claims and appends it to             *)
(* `_rejectionComponents`, so a single publication can name several         *)
(* machines and carries one kind for all of them. A machine can also appear *)
(* in several publications, which is what `MergeExisting` exists to union.  *)
(*                                                                          *)
(* An earlier revision of this model gave each machine its own kind and its *)
(* own discovery position. That is the wrong unit: it cannot express "the   *)
(* first appended component wins", because in that model there is no such   *)
(* thing as a component that spans machines. Modeling publications directly *)
(* is what makes `C5_EvidenceIsFirstDiscovered` mean what the code does.    *)
(***************************************************************************)
Published == 1..published

Touches(p, S) == covers[p] \cap S # {}

ComponentOf(m) == {n \in Machines : component[n] = component[m]}

MinId(S) == CHOOSE i \in S : \A j \in S : i <= j

(***************************************************************************)
(* `FreezeRejections` walks the publication list in append order and seeds  *)
(* the merged kind and detail from the FIRST entry it meets whose component *)
(* root matches. The winner is therefore the lowest-numbered publication    *)
(* that touches the component -- not a minimum over kinds, and not the      *)
(* newest claim.                                                            *)
(***************************************************************************)
EarliestClaim(S) == MinId({p \in Published : Touches(p, S)})

(***************************************************************************)
(* Two machines must end up in one component when some published rejection  *)
(* names them both. This is derived from the claims themselves rather than  *)
(* configured, so the merging obligation is exactly the one the input       *)
(* creates -- and transitivity follows from applying it repeatedly.         *)
(***************************************************************************)
MustMerge(a, b) == \E p \in Published : {a, b} \subseteq covers[p]

TypeOK ==
    /\ truth \in [Machines -> Truths]
    /\ phase \in Phases
    /\ kind \in FailureKinds \cup {"None"}
    /\ result \in [Machines -> Results]
    /\ component \in [Machines -> Machines]
    /\ evidence \in [Machines -> RejectionKinds \cup {NoEvidence}]
    /\ published \in 0..MaxRejections
    /\ covers \in [RejectionIds -> SUBSET Machines]
    /\ claimKind \in [RejectionIds -> RejectionKinds \cup {NoEvidence}]
    /\ visited \subseteq Machines
    /\ budget \in 0..MaxBudget

(***************************************************************************)
(* Every machine starts in its own rejection component; publishing is the   *)
(* only thing that brings two together.                                     *)
(***************************************************************************)
Init ==
    \* Every population is explored, so no invariant can depend on a
    \* convenient one.
    /\ truth \in [Machines -> Truths]
    /\ phase = "Building"
    /\ kind = "None"
    /\ result = [m \in Machines |-> "Unclassified"]
    /\ component = [m \in Machines |-> m]
    /\ evidence = [m \in Machines |-> NoEvidence]
    /\ published = 0
    /\ covers = [p \in RejectionIds |-> {}]
    /\ claimKind = [p \in RejectionIds |-> NoEvidence]
    /\ visited = {}
    /\ budget = MaxBudget

-----------------------------------------------------------------------------
(***************************************************************************)
(* Construction steps. Each spends one unit of budget. The order is         *)
(* unconstrained: TLC explores every interleaving, which is what makes the  *)
(* first-discovered rule a real check rather than an assertion about one    *)
(* traversal.                                                              *)
(***************************************************************************)

\* The claim authenticates: this machine resolves.
ResolveOne(m) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ truth[m] = "Resolvable"
    /\ result' = [result EXCEPT ![m] = "Resolved"]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<truth, phase, kind, component, evidence, published, covers,
                   claimKind>>

\* No claim names this machine at all: it is absent, not refused.
AbsentOne(m) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ truth[m] = "NoClaim"
    /\ result' = [result EXCEPT ![m] = "Absent"]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<truth, phase, kind, component, evidence, published, covers,
                   claimKind>>

(***************************************************************************)
(* Publish one rejection naming the set S, and union it with whatever those *)
(* machines already belong to. This is `PublishRejection` followed by       *)
(* `MergeExisting`: the new component is appended first, then merged with   *)
(* every component that already covers one of its tokens.                   *)
(***************************************************************************)
PublishRejection(S, e) ==
    /\ phase = "Building"
    /\ published < MaxRejections
    /\ budget > 0
    /\ S # {}
    /\ \A m \in S : truth[m] = "Refused"
    \* A publication that names exactly what an existing one named would add
    \* no merging obligation, only states.
    /\ \A p \in Published : covers[p] # S
    /\ LET pid     == published + 1
           cov     == [covers EXCEPT ![pid] = S]
           kinds   == [claimKind EXCEPT ![pid] = e]
           \* MergeExisting reaches the components the named machines are
           \* already in; forgetting it is the NewClaimOnly counterexample.
           merged  == IF MergeMode = "NewClaimOnly"
                      THEN S
                      ELSE UNION {ComponentOf(x) : x \in S}
           id      == MinId({component[x] : x \in merged})
           winner  == MinId({p \in 1..pid : cov[p] \cap merged # {}})
           ev      == IF MergeMode = "LatestClaim" THEN e ELSE kinds[winner]
           touched == IF MergeMode = "NamedPairOnly" THEN S ELSE merged
       IN  /\ published' = pid
           /\ covers' = cov
           /\ claimKind' = kinds
           /\ component' = [x \in Machines |->
                              IF x \in merged THEN id ELSE component[x]]
           /\ evidence' = [x \in Machines |->
                              IF x \in touched THEN ev ELSE evidence[x]]
           /\ result' = [x \in Machines |->
                              IF x \in merged THEN "Rejected" ELSE result[x]]
           /\ visited' = visited \cup merged
    /\ budget' = budget - 1
    /\ UNCHANGED <<truth, phase, kind>>

(***************************************************************************)
(* Whole-module failure. This is C3's total case: it rejects every machine, *)
(* including ones construction had already resolved and ones it never       *)
(* reached. That total overwrite is the invariant -- an implementation that *)
(* left an earlier `Resolved` standing would produce the partial shape C3   *)
(* says a per-claim refusal may also have.                                  *)
(***************************************************************************)
FailModule(k) ==
    /\ phase = "Building"
    /\ phase' = "Failed"
    /\ kind' = k
    /\ result' = [m \in Machines |->
                    CASE FailureMode = "ReportAbsent" -> "Absent"
                      [] FailureMode = "PreserveClassified" ->
                            IF result[m] = "Unclassified" THEN "Rejected"
                            ELSE result[m]
                      [] OTHER -> "Rejected"]
    \* The whole module failed, so every machine carries the module-level
    \* kind rather than a per-claim one.
    /\ evidence' = [m \in Machines |-> NoEvidence]
    /\ visited' = Machines
    /\ UNCHANGED <<truth, component, published, covers, claimKind, budget>>

Malform == FailModule("Malformed")

\* Budget exhaustion is the same transition, reachable only when spent.
ExhaustBudget ==
    /\ budget = 0
    /\ visited # Machines
    /\ FailModule("BudgetExceeded")

(***************************************************************************)
(* Construction completes once every machine has been classified. There is  *)
(* deliberately no "everything is merged" precondition here: production     *)
(* merges eagerly when publishing and performs no such check before         *)
(* freezing, so requiring one would be an obligation the model invented.    *)
(***************************************************************************)
Finish ==
    /\ phase = "Building"
    /\ (FinishMode \in {"AllowUnvisited", "DropAsAbsent"} \/ visited = Machines)
    /\ phase' = "Built"
    \* A lost row is not published as a distinguished marker. It is published
    \* as Absent, which is what a machine with no claim looks like.
    /\ result' = IF FinishMode = "DropAsAbsent"
                 THEN [m \in Machines |->
                         IF result[m] = "Unclassified" THEN "Absent"
                         ELSE result[m]]
                 ELSE result
    /\ UNCHANGED <<truth, kind, component, evidence, published, covers,
                   claimKind, visited, budget>>

Next ==
    \/ \E m \in Machines : ResolveOne(m)
    \/ \E m \in Machines : AbsentOne(m)
    \/ \E S \in (SUBSET Machines) \ {{}}, e \in RejectionKinds :
            PublishRejection(S, e)
    \/ Malform
    \/ ExhaustBudget
    \/ Finish
    \* Terminal states stutter so the temporal properties are well formed.
    \/ /\ phase \in {"Built", "Failed"}
       /\ UNCHANGED vars

Spec == Init /\ [][Next]_vars /\ WF_vars(Next)

-----------------------------------------------------------------------------
(***************************************************************************)
(* Properties.                                                             *)
(***************************************************************************)

Terminal == phase \in {"Built", "Failed"}

(***************************************************************************)
(* C1 -- Totality. A published index classifies every structural machine    *)
(* the way an independent recount of the population would.                  *)
(*                                                                          *)
(* Stating it against `truth` rather than against the absence of an         *)
(* `Unclassified` marker is the whole point. A real index that drops a row  *)
(* does not publish a distinguished "not reached" value; it answers         *)
(* `Absent`, exactly as it would for a machine that genuinely has no claim. *)
(* An invariant that only forbade a marker would therefore be vacuous, and  *)
(* an earlier revision of this model contained precisely that mistake. This *)
(* is the modeled form of the external cross-check C6 requires.             *)
(***************************************************************************)
C1_Totality ==
    (phase = "Built") => \A m \in Machines : result[m] = Expected(truth[m])

(***************************************************************************)
(* C2 -- Failure is never success-shaped. A failed construction carries a   *)
(* typed kind, and does not present as a clean index that simply found      *)
(* nothing. The second conjunct is the sharp one: `Absent` everywhere is    *)
(* exactly what an empty successful index would look like.                  *)
(***************************************************************************)
C2_FailureIsTyped ==
    /\ (phase = "Failed") => kind \in FailureKinds
    /\ (phase = "Failed") => \A m \in Machines : result[m] # "Absent"

(***************************************************************************)
(* C3 -- Whole-module failure rejects the whole module. Total, not partial: *)
(* nothing resolves, and nothing is left absent.                            *)
(***************************************************************************)
C3_FailureRejectsAll ==
    (phase = "Failed") => \A m \in Machines : result[m] = "Rejected"

(***************************************************************************)
(* C5a -- Merged rejections agree. Every machine sharing a component        *)
(* carries the same result and the same evidence.                           *)
(*                                                                          *)
(* Note what this does NOT say. It does not say the merged evidence is      *)
(* independent of discovery order: production seeds it from the first       *)
(* contributing claim it meets, so it is not. An earlier revision of this   *)
(* model asserted order independence and combined evidence with a minimum,  *)
(* which made the claim true of the model and false of the implementation.  *)
(***************************************************************************)
C5_ComponentsAgree ==
    \A m, n \in Machines :
        (component[m] = component[n])
            => /\ result[m] = result[n]
               /\ evidence[m] = evidence[n]

(***************************************************************************)
(* C5b -- Merging happens. Two machines a published rejection names end up  *)
(* in one component, and transitively so through overlapping publications.  *)
(* C5a says a component agrees with itself; without this, an index that     *)
(* merged nothing would satisfy it vacuously.                               *)
(***************************************************************************)
C5_SharedRejectionsMerge ==
    (phase = "Built") =>
        \A a, b \in Machines : MustMerge(a, b) => component[a] = component[b]

(***************************************************************************)
(* C5c -- The merged evidence is the earliest-published contributing        *)
(* claim's. This specifies the merged value in terms of the component's     *)
(* members and publication order alone. It is deterministic, but it is      *)
(* derived from metadata row order, so it is a compiler artifact: consumers *)
(* may rely on it being stable for a given image, and not on which claim    *)
(* supplied it.                                                             *)
(***************************************************************************)
C5_EvidenceIsFirstDiscovered ==
    (phase = "Built") =>
        \A m \in Machines :
            (result[m] = "Rejected")
                => evidence[m] = claimKind[EarliestClaim(ComponentOf(m))]

(***************************************************************************)
(* Failure absorbs: once construction has failed it never reports success,  *)
(* and its kind never changes underneath a consumer.                        *)
(***************************************************************************)
FailureIsAbsorbing ==
    [][ (phase = "Failed") => (phase' = "Failed" /\ kind' = kind) ]_vars

(***************************************************************************)
(* Construction terminates. Not a safety property, but it is what makes the *)
(* invariants above non-vacuous: a spec that never reached a terminal state *)
(* would satisfy C1 trivially.                                              *)
(***************************************************************************)
EventuallyTerminal == <>Terminal

=============================================================================
