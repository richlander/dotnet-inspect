---------------- MODULE TypeSourcePdbLatencyHedge ----------------
EXTENDS TLC

VARIABLES
    pdb,
    authored,
    decompilation,
    decompilationUsedPdb,
    initialWindowElapsed,
    selection,
    cleanup,
    published

vars ==
    <<pdb,
      authored,
      decompilation,
      decompilationUsedPdb,
      initialWindowElapsed,
      selection,
      cleanup,
      published>>

Init ==
    /\ pdb = "pending"
    /\ authored = "not-started"
    /\ decompilation = "not-started"
    /\ decompilationUsedPdb = FALSE
    /\ initialWindowElapsed = FALSE
    /\ selection = "none"
    /\ cleanup = "live"
    /\ published = FALSE

PdbReady ==
    /\ selection = "none"
    /\ pdb = "pending"
    /\ pdb' = "ready"
    /\ UNCHANGED
        <<authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

PdbUnavailable ==
    /\ selection = "none"
    /\ pdb = "pending"
    /\ pdb' = "unavailable"
    /\ UNCHANGED
        <<authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

InitialWindowExpires ==
    /\ selection = "none"
    /\ ~initialWindowElapsed
    /\ initialWindowElapsed' = TRUE
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilation,
          decompilationUsedPdb,
          selection,
          cleanup,
          published>>

StartAuthored ==
    /\ selection = "none"
    /\ pdb = "ready"
    /\ authored = "not-started"
    /\ decompilation # "available"
    /\ authored' = "running"
    /\ UNCHANGED
        <<pdb,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

StartDecompilation ==
    /\ selection = "none"
    /\ decompilation = "not-started"
    /\ (pdb = "unavailable"
        \/ (pdb = "ready" /\ authored = "unavailable")
        \/ (pdb = "pending" /\ initialWindowElapsed))
    /\ decompilation' = "running"
    /\ decompilationUsedPdb' = (pdb = "ready")
    /\ UNCHANGED
        <<pdb,
          authored,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

AuthoredAvailable ==
    /\ selection = "none"
    /\ authored = "running"
    /\ authored' = "available"
    /\ UNCHANGED
        <<pdb,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

AuthoredUnavailable ==
    /\ selection = "none"
    /\ authored = "running"
    /\ authored' = "unavailable"
    /\ UNCHANGED
        <<pdb,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

DecompilationAvailable ==
    /\ selection = "none"
    /\ decompilation = "running"
    /\ decompilation' = "available"
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

DecompilationUnavailable ==
    /\ selection = "none"
    /\ decompilation = "running"
    /\ decompilation' = "unavailable"
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup,
          published>>

SelectAuthored ==
    /\ selection = "none"
    /\ authored = "available"
    /\ decompilation # "running"
    /\ decompilation # "available"
    /\ selection' = "authored"
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          cleanup,
          published>>

SelectDecompiled ==
    /\ selection = "none"
    /\ decompilation = "available"
    /\ (decompilationUsedPdb
        \/ pdb = "unavailable"
        \/ initialWindowElapsed)
    /\ selection' = "decompiled"
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          cleanup,
          published>>

SelectUnavailable ==
    /\ selection = "none"
    /\ decompilation = "unavailable"
    /\ (pdb = "unavailable" \/ authored = "unavailable")
    /\ selection' = "unavailable"
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          cleanup,
          published>>

SettleLosers ==
    /\ selection # "none"
    /\ cleanup = "live"
    /\ pdb' = IF pdb = "pending" THEN "cancelled" ELSE pdb
    /\ authored' =
        IF authored = "running" THEN "cancelled" ELSE authored
    /\ cleanup' = "settled"
    /\ UNCHANGED
        <<decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          published>>

Publish ==
    /\ selection # "none"
    /\ cleanup = "settled"
    /\ ~published
    /\ published' = TRUE
    /\ UNCHANGED
        <<pdb,
          authored,
          decompilation,
          decompilationUsedPdb,
          initialWindowElapsed,
          selection,
          cleanup>>

Next ==
    \/ PdbReady
    \/ PdbUnavailable
    \/ InitialWindowExpires
    \/ StartAuthored
    \/ StartDecompilation
    \/ AuthoredAvailable
    \/ AuthoredUnavailable
    \/ DecompilationAvailable
    \/ DecompilationUnavailable
    \/ SelectAuthored
    \/ SelectDecompiled
    \/ SelectUnavailable
    \/ SettleLosers
    \/ Publish

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ pdb \in {"pending", "ready", "unavailable", "cancelled"}
    /\ authored \in
        {"not-started", "running", "available", "unavailable", "cancelled"}
    /\ decompilation \in
        {"not-started", "running", "available", "unavailable"}
    /\ decompilationUsedPdb \in BOOLEAN
    /\ initialWindowElapsed \in BOOLEAN
    /\ selection \in {"none", "authored", "decompiled", "unavailable"}
    /\ cleanup \in {"live", "settled"}
    /\ published \in BOOLEAN

PublicationRequiresSettlement ==
    published => cleanup = "settled"

PublishedWorkIsSettled ==
    published =>
        /\ pdb # "pending"
        /\ authored # "running"
        /\ decompilation # "running"

SelectedResultExists ==
    /\ (selection = "authored" => authored = "available")
    /\ (selection = "decompiled" => decompilation = "available")
    /\ (selection = "unavailable" => decompilation = "unavailable")

PdbAssistedDecompilationFollowsAuthoredFailure ==
    decompilationUsedPdb =>
        /\ pdb = "ready"
        /\ authored = "unavailable"

NoPdbDecompilationRequiresBoundary ==
    decompilation = "running" /\ ~decompilationUsedPdb =>
        pdb = "unavailable" \/ initialWindowElapsed

UnavailableWaitsForRemainingSource ==
    selection = "unavailable" =>
        pdb = "unavailable" \/ authored = "unavailable"

ExpiredPdbPathDoesNotPreferLateAuthored ==
    selection = "authored" /\ initialWindowElapsed =>
        decompilation # "available"

=================================================================
