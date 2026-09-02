---------------- MODULE AssemblyBindingPolicyVersionLifecycle ----------------
(***************************************************************************)
(* Owner-issued lifecycle for one modeled replacement of the composed      *)
(* AssemblyBindingPolicyVersion. Consumers instantiate this module with     *)
(* their own state variables and recheck its safety specification inside    *)
(* the consuming behavior.                                                  *)
(* Owned by docs/design/type-forwarding-resolution.md.                      *)
(***************************************************************************)

CONSTANTS
    InitialVersion,
    ReplacementVersion

ASSUME InitialVersion # ReplacementVersion

VARIABLES
    version,
    advanced

vars == <<version, advanced>>

Versions == {InitialVersion, ReplacementVersion}

Init ==
    /\ version = InitialVersion
    /\ advanced = FALSE

Advance ==
    /\ ~advanced
    /\ version = InitialVersion
    /\ version' = ReplacementVersion
    /\ advanced' = TRUE

SafetySpec ==
    /\ Init
    /\ [][Advance]_vars

Spec ==
    /\ SafetySpec
    /\ WF_vars(Advance)

TypeOK ==
    /\ version \in Versions
    /\ advanced \in BOOLEAN

AdvancedVersionIsFresh ==
    advanced => version = ReplacementVersion

VersionEventuallyAdvances ==
    <>advanced

=============================================================================
