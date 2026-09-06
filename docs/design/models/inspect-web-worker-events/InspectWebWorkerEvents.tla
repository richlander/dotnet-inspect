------------------------ MODULE InspectWebWorkerEvents ------------------------
EXTENDS Naturals, Sequences, FiniteSets, TLC

CONSTANT Mutation

References ==
    {[epoch |-> 7, operationId |-> "A", operationSequence |-> 1],
     [epoch |-> 7, operationId |-> "B", operationSequence |-> 2]}
MaxEvents == 3
BatchBound == 2
EmptyReference == [epoch |-> 0, operationId |-> "", operationSequence |-> 0]

ASSUME Mutation \in {"None", "OvertakeBatch", "DropEvent", "ReverseBatch"}

VARIABLES posted, delivered, wire, pending, pendingReference,
          terminalPosted, terminalReceived

vars == <<posted, delivered, wire, pending, pendingReference,
          terminalPosted, terminalReceived>>

Init ==
    /\ posted = [r \in References |-> <<>>]
    /\ delivered = [r \in References |-> <<>>]
    /\ wire = <<>>
    /\ pending = <<>>
    /\ pendingReference = EmptyReference
    /\ terminalPosted = {}
    /\ terminalReceived = {}

PostBatch(r, count) ==
    /\ r \in References
    /\ r \notin terminalPosted
    /\ count \in 1..BatchBound
    /\ Len(posted[r]) + count <= MaxEvents
    /\ LET batch == [i \in 1..count |-> Len(posted[r]) + i]
       IN /\ posted' = [posted EXCEPT ![r] = @ \o batch]
          /\ wire' = Append(wire,
                 [kind |-> "events", reference |-> r, events |-> batch])
    /\ UNCHANGED <<delivered, pending, pendingReference,
                   terminalPosted, terminalReceived>>

PostTerminal(r) ==
    /\ r \in References
    /\ r \notin terminalPosted
    /\ terminalPosted' = terminalPosted \cup {r}
    /\ wire' = Append(wire,
           [kind |-> "settled", reference |-> r, events |-> <<>>])
    /\ UNCHANGED <<posted, delivered, pending, pendingReference,
                   terminalReceived>>

ReceiveBatch ==
    /\ pending = <<>>
    /\ wire # <<>>
    /\ Head(wire).kind = "events"
    /\ LET message == Head(wire)
           batch == message.events
       IN /\ pending' =
                CASE Mutation = "DropEvent" -> Tail(batch)
                  [] Mutation = "ReverseBatch" ->
                       [i \in 1..Len(batch) |-> batch[Len(batch) - i + 1]]
                  [] OTHER -> batch
          /\ pendingReference' = message.reference
    /\ wire' = Tail(wire)
    /\ UNCHANGED <<posted, delivered, terminalPosted, terminalReceived>>

DeliverEntry ==
    /\ pending # <<>>
    /\ delivered' =
          [delivered EXCEPT ![pendingReference] = Append(@, Head(pending))]
    /\ pending' = Tail(pending)
    /\ UNCHANGED <<posted, wire, pendingReference,
                   terminalPosted, terminalReceived>>

ReceiveTerminal ==
    /\ pending = <<>> \/ Mutation = "OvertakeBatch"
    /\ wire # <<>>
    /\ Head(wire).kind = "settled"
    /\ terminalReceived' = terminalReceived \cup {Head(wire).reference}
    /\ wire' = Tail(wire)
    /\ UNCHANGED <<posted, delivered, pending, pendingReference,
                   terminalPosted>>

Next ==
    \/ \E r \in References, count \in 1..BatchBound : PostBatch(r, count)
    \/ \E r \in References : PostTerminal(r)
    \/ ReceiveBatch
    \/ DeliverEntry
    \/ ReceiveTerminal

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ posted \in [References -> Seq(1..MaxEvents)]
    /\ delivered \in [References -> Seq(1..MaxEvents)]
    /\ pending \in Seq(1..MaxEvents)
    /\ pendingReference \in References \cup {EmptyReference}
    /\ terminalPosted \subseteq References
    /\ terminalReceived \subseteq terminalPosted

DeliveryIsOrderedPrefix ==
    \A r \in References :
        delivered[r] = SubSeq(posted[r], 1, Len(delivered[r]))

TerminalFollowsEveryEntry ==
    \A r \in terminalReceived : delivered[r] = posted[r]

BatchesAreBounded ==
    /\ Len(pending) <= BatchBound
    /\ \A i \in 1..Len(wire) : Len(wire[i].events) <= BatchBound

CompleteDeliveryReachable ==
    ~(terminalReceived = References /\
      \A r \in References : Len(posted[r]) = MaxEvents)
=============================================================================
