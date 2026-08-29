---------------------- MODULE RejectionComponentMerge ----------------------
(***************************************************************************)
(* C5 of docs/design/state-machine-relationship-index.md.                  *)
(*                                                                         *)
(* Production creates one union-find node per published rejection. A node  *)
(* connects through tagged identities in four domains: kickoff MethodDef,  *)
(* state-machine TypeDef, implementation MethodDef, and a claimed type name *)
(* registered for reuse by RejectKickoffCandidates. A claimed name carried  *)
(* only as diagnostic evidence is not thereby a link.                      *)
(* It separately contributes diagnostic evidence. This model preserves     *)
(* that separation: `links` determine connectivity; `payload` determines   *)
(* accumulated evidence.                                                   *)
(*                                                                         *)
(* MergeKeys collapses the four dictionaries into one finite set. Read     *)
(* every value as domain-tagged: the same numeric token in two domains is   *)
(* two different keys. EvidenceItems is tagged the same way across kickoff  *)
(* candidates, state-machine candidates, and claimed types. Claimed-name   *)
(* links have the same connectivity semantics even though production stores *)
(* their representative in a separate dictionary. Which representative a   *)
(* dictionary retains cannot change the connected components, so one       *)
(* `owner` function is sufficient.                                         *)
(***************************************************************************)

EXTENDS FiniteSets, Naturals

CONSTANTS
    KeyCount,
    EvidenceCount,
    MaxPublications,
    MergeMode,
    FreezeMode

(***************************************************************************)
(*   MergeMode  = "Component"       merge every existing component reached *)
(*              = "NewOnly"         publish without MergeExisting           *)
(*              = "Representatives" merge only current representatives      *)
(*                                                                         *)
(*   FreezeMode = "Aggregate"       one union payload and kind per component *)
(*              = "LocalPayload"    retain only each publication's payload  *)
(*              = "LocalKind"       retain each publication's own kind      *)
(***************************************************************************)
ASSUME
    /\ KeyCount \in Nat \ {0}
    /\ EvidenceCount \in Nat \ {0}
    /\ MaxPublications \in Nat \ {0}
    /\ MergeMode \in {"Component", "NewOnly", "Representatives"}
    /\ FreezeMode \in {"Aggregate", "LocalPayload", "LocalKind"}

MergeKeys == 1..KeyCount
EvidenceItems == 1..EvidenceCount
PublicationIds == 1..MaxPublications
Kinds == 1..2
NoPublication == 0
NoKind == 0
Phases == {"Building", "Frozen"}

VARIABLES
    phase,
    published,
    links,          \* publication -> tagged identities used for merging
    payload,        \* publication -> contributed diagnostic evidence
    claimKind,      \* publication -> its own (Kind, Detail) abstraction
    component,      \* publication -> union-find component id
    owner,          \* merge key -> current publication representative
    frozenEvidence, \* publication -> component evidence membership
    frozenKind      \* publication -> selected component kind/detail

vars ==
    <<phase, published, links, payload, claimKind, component, owner,
      frozenEvidence, frozenKind>>

Published == 1..published

ComponentOf(p) ==
    {q \in Published : component[q] = component[p]}

MinId(S) == CHOOSE i \in S : \A j \in S : i <= j

ComponentPayload(p) ==
    UNION {payload[q] : q \in ComponentOf(p)}

ComponentKinds(p) ==
    {claimKind[q] : q \in ComponentOf(p)}

FirstPublication(p) == MinId(ComponentOf(p))

Overlap(p, q) == links[p] \cap links[q] # {}

TypeOK ==
    /\ phase \in Phases
    /\ published \in 0..MaxPublications
    /\ links \in [PublicationIds -> SUBSET MergeKeys]
    /\ payload \in [PublicationIds -> SUBSET EvidenceItems]
    /\ claimKind \in [PublicationIds -> Kinds \cup {NoKind}]
    /\ component \in [PublicationIds -> PublicationIds]
    /\ owner \in [MergeKeys -> PublicationIds \cup {NoPublication}]
    /\ frozenEvidence \in
        [PublicationIds -> SUBSET EvidenceItems]
    /\ frozenKind \in [PublicationIds -> Kinds \cup {NoKind}]

Init ==
    /\ phase = "Building"
    /\ published = 0
    /\ links = [p \in PublicationIds |-> {}]
    /\ payload = [p \in PublicationIds |-> {}]
    /\ claimKind = [p \in PublicationIds |-> NoKind]
    /\ component = [p \in PublicationIds |-> p]
    /\ owner = [k \in MergeKeys |-> NoPublication]
    /\ frozenEvidence = [p \in PublicationIds |-> {}]
    /\ frozenKind = [p \in PublicationIds |-> NoKind]

Publish(linkSet, evidenceSet, kind) ==
    /\ phase = "Building"
    /\ published < MaxPublications
    /\ linkSet # {}
    /\ evidenceSet # {}
    \* Duplicate nodes add no graph shape and only multiply states.
    /\ \A p \in Published :
        \/ links[p] # linkSet
        \/ payload[p] # evidenceSet
        \/ claimKind[p] # kind
    /\ LET pid == published + 1
           representatives ==
               {owner[k] : k \in linkSet}
                   \ {NoPublication}
           reached ==
               UNION {ComponentOf(p) : p \in representatives}
           merged ==
               CASE MergeMode = "NewOnly" -> {pid}
                 [] MergeMode = "Representatives" ->
                        {pid} \cup representatives
                 [] OTHER -> {pid} \cup reached
           id == MinId(merged)
       IN  /\ published' = pid
           /\ links' = [links EXCEPT ![pid] = linkSet]
           /\ payload' = [payload EXCEPT ![pid] = evidenceSet]
           /\ claimKind' = [claimKind EXCEPT ![pid] = kind]
           /\ component' = [p \in PublicationIds |->
                                IF p \in merged THEN id
                                ELSE component[p]]
           /\ owner' = [k \in MergeKeys |->
                            IF k \in linkSet THEN pid ELSE owner[k]]
    /\ UNCHANGED <<phase, frozenEvidence, frozenKind>>

Freeze ==
    /\ phase = "Building"
    /\ published > 0
    /\ phase' = "Frozen"
    /\ frozenEvidence' =
        [p \in PublicationIds |->
            IF p \notin Published THEN {}
            ELSE IF FreezeMode = "LocalPayload" THEN payload[p]
            ELSE ComponentPayload(p)]
    /\ frozenKind' =
        [p \in PublicationIds |->
            IF p \notin Published THEN NoKind
            ELSE IF FreezeMode = "LocalKind" THEN claimKind[p]
            ELSE claimKind[FirstPublication(p)]]
    /\ UNCHANGED
        <<published, links, payload, claimKind, component, owner>>

Next ==
    \/ \E linkSet \in (SUBSET MergeKeys) \ {{}},
          evidenceSet \in (SUBSET EvidenceItems) \ {{}},
          kind \in Kinds :
            Publish(linkSet, evidenceSet, kind)
    \/ Freeze
    \/ /\ phase = "Frozen"
       /\ UNCHANGED vars

Spec == Init /\ [][Next]_vars /\ WF_vars(Next)

C5_OverlapClosesTransitively ==
    (phase = "Frozen") =>
        \A p, q \in Published :
            Overlap(p, q) => component[p] = component[q]

C5_ComponentProjectionAgrees ==
    (phase = "Frozen") =>
        \A p, q \in Published :
            (component[p] = component[q])
                => /\ frozenEvidence[p] = frozenEvidence[q]
                   /\ frozenKind[p] = frozenKind[q]

C5_EvidenceMembershipIsComplete ==
    (phase = "Frozen") =>
        \A p \in Published :
            frozenEvidence[p] = ComponentPayload(p)

C5_KindComesFromComponent ==
    (phase = "Frozen") =>
        \A p \in Published :
            frozenKind[p] \in ComponentKinds(p)

EventuallyFrozen == <> (phase = "Frozen")

=============================================================================
