------------------------- MODULE CompileBackAdmission -------------------------
EXTENDS Integers, Naturals, TLC

CONSTANT MaxIterations

ASSUME MaxIterations \in Nat

Phases ==
    {"Planning", "ProductAttempt", "LegacyPlanning", "LegacyAttempt", "Done"}

ProductAdmissions == {"None", "Declined", "Failed", "Admitted"}
LegacyAdmissions == {"None", "Declined", "Failed", "Admitted"}
Verdicts == {"None", "Exact", "Different", "Unavailable"}

VARIABLES
    phase,
    iteration,
    productAdmission,
    legacyAdmission,
    productEvidenceAttempt,
    productCoverageReceiptAttempt,
    productReceiptAttempt,
    legacyReceipt,
    verdict,
    suppliedBody

vars ==
    <<phase, iteration, productAdmission, legacyAdmission,
      productEvidenceAttempt, productCoverageReceiptAttempt,
      productReceiptAttempt, legacyReceipt, verdict, suppliedBody>>

Init ==
    /\ phase = "Planning"
    /\ iteration = 0
    /\ productAdmission = "None"
    /\ legacyAdmission = "None"
    /\ productEvidenceAttempt = -1
    /\ productCoverageReceiptAttempt = -1
    /\ productReceiptAttempt = -1
    /\ legacyReceipt = FALSE
    /\ verdict = "None"
    /\ suppliedBody \in BOOLEAN

SuppliedBodyComparison ==
    /\ phase = "Planning"
    /\ suppliedBody
    /\ phase' = "Done"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, productAdmission, legacyAdmission,
          productEvidenceAttempt, productCoverageReceiptAttempt,
          productReceiptAttempt, legacyReceipt, suppliedBody>>

PlanningDeclineWithoutLegacy ==
    /\ phase = "Planning"
    /\ ~suppliedBody
    /\ phase' = "Done"
    /\ productAdmission' = "Declined"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, suppliedBody>>

PlanningDeclineToLegacy ==
    /\ phase = "Planning"
    /\ ~suppliedBody
    /\ phase' = "LegacyPlanning"
    /\ productAdmission' = "Declined"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, verdict, suppliedBody>>

ProductCommit ==
    /\ phase = "Planning"
    /\ ~suppliedBody
    /\ phase' = "ProductAttempt"
    /\ productEvidenceAttempt' = iteration
    /\ UNCHANGED
        <<iteration, productAdmission, legacyAdmission,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, verdict, suppliedBody>>

ProductCoverageReceipt ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = -1
    /\ productCoverageReceiptAttempt' = iteration
    /\ UNCHANGED
        <<phase, iteration, productAdmission, legacyAdmission,
          productEvidenceAttempt, productReceiptAttempt,
          legacyReceipt, verdict, suppliedBody>>

ProductExpand ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = iteration
    /\ iteration < MaxIterations
    /\ phase' = "Planning"
    /\ iteration' = iteration + 1
    /\ productEvidenceAttempt' = -1
    /\ productCoverageReceiptAttempt' = -1
    /\ legacyReceipt' = FALSE
    /\ UNCHANGED
        <<productAdmission, legacyAdmission, productReceiptAttempt,
          verdict, suppliedBody>>

ProductBudgetFail ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = iteration
    /\ iteration = MaxIterations
    /\ phase' = "Done"
    /\ productAdmission' = "Failed"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, suppliedBody>>

ProductFail ==
    /\ phase = "ProductAttempt"
    /\ phase' = "Done"
    /\ productAdmission' = "Failed"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, suppliedBody>>

ProductAdmitExact ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = iteration
    /\ phase' = "Done"
    /\ productAdmission' = "Admitted"
    /\ productReceiptAttempt' = iteration
    /\ verdict' = "Exact"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, legacyReceipt, suppliedBody>>

ProductAdmitDifferent ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = iteration
    /\ phase' = "Done"
    /\ productAdmission' = "Admitted"
    /\ productReceiptAttempt' = iteration
    /\ verdict' = "Different"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, legacyReceipt, suppliedBody>>

ProductAdmitUnavailable ==
    /\ phase = "ProductAttempt"
    /\ productCoverageReceiptAttempt = iteration
    /\ phase' = "Done"
    /\ productAdmission' = "Admitted"
    /\ productReceiptAttempt' = iteration
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, legacyAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, legacyReceipt, suppliedBody>>

LegacyDecline ==
    /\ phase = "LegacyPlanning"
    /\ phase' = "Done"
    /\ legacyAdmission' = "Declined"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, productAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, suppliedBody>>

LegacyCommit ==
    /\ phase = "LegacyPlanning"
    /\ phase' = "LegacyAttempt"
    /\ UNCHANGED
        <<iteration, productAdmission, legacyAdmission,
          productEvidenceAttempt, productCoverageReceiptAttempt,
          productReceiptAttempt, legacyReceipt, verdict, suppliedBody>>

LegacyFail ==
    /\ phase = "LegacyAttempt"
    /\ phase' = "Done"
    /\ legacyAdmission' = "Failed"
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, productAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt,
          legacyReceipt, suppliedBody>>

LegacyAdmitExact ==
    /\ phase = "LegacyAttempt"
    /\ phase' = "Done"
    /\ legacyAdmission' = "Admitted"
    /\ legacyReceipt' = TRUE
    /\ verdict' = "Exact"
    /\ UNCHANGED
        <<iteration, productAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt, suppliedBody>>

LegacyAdmitDifferent ==
    /\ phase = "LegacyAttempt"
    /\ phase' = "Done"
    /\ legacyAdmission' = "Admitted"
    /\ legacyReceipt' = TRUE
    /\ verdict' = "Different"
    /\ UNCHANGED
        <<iteration, productAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt, suppliedBody>>

LegacyAdmitUnavailable ==
    /\ phase = "LegacyAttempt"
    /\ phase' = "Done"
    /\ legacyAdmission' = "Admitted"
    /\ legacyReceipt' = TRUE
    /\ verdict' = "Unavailable"
    /\ UNCHANGED
        <<iteration, productAdmission, productEvidenceAttempt,
          productCoverageReceiptAttempt, productReceiptAttempt, suppliedBody>>

DoneStutter ==
    /\ phase = "Done"
    /\ UNCHANGED vars

Next ==
    \/ SuppliedBodyComparison
    \/ PlanningDeclineWithoutLegacy
    \/ PlanningDeclineToLegacy
    \/ ProductCommit
    \/ ProductCoverageReceipt
    \/ ProductExpand
    \/ ProductBudgetFail
    \/ ProductFail
    \/ ProductAdmitExact
    \/ ProductAdmitDifferent
    \/ ProductAdmitUnavailable
    \/ LegacyDecline
    \/ LegacyCommit
    \/ LegacyFail
    \/ LegacyAdmitExact
    \/ LegacyAdmitDifferent
    \/ LegacyAdmitUnavailable
    \/ DoneStutter

TypeOK ==
    /\ phase \in Phases
    /\ iteration \in 0..MaxIterations
    /\ productAdmission \in ProductAdmissions
    /\ legacyAdmission \in LegacyAdmissions
    /\ productEvidenceAttempt \in -1..MaxIterations
    /\ productCoverageReceiptAttempt \in -1..MaxIterations
    /\ productReceiptAttempt \in -1..MaxIterations
    /\ legacyReceipt \in BOOLEAN
    /\ verdict \in Verdicts
    /\ suppliedBody \in BOOLEAN

ExactRequiresMatchingReceipt ==
    verdict # "Exact"
    \/ /\ productAdmission = "Admitted"
       /\ productReceiptAttempt = iteration
    \/ /\ productAdmission = "Declined"
       /\ legacyAdmission = "Admitted"
       /\ legacyReceipt

ProductFailureCannotUseLegacy ==
    productAdmission # "Failed"
    \/ legacyAdmission = "None"

LegacyRequiresProductDecline ==
    legacyAdmission = "None"
    \/ productAdmission = "Declined"

SuppliedBodyNeverExact ==
    ~suppliedBody
    \/ verdict # "Exact"

ReceiptRequiresAdmission ==
    /\ (productReceiptAttempt = -1 \/ productAdmission = "Admitted")
    /\ (~legacyReceipt \/ legacyAdmission = "Admitted")

CoverageReceiptMatchesAttempt ==
    productCoverageReceiptAttempt = -1
    \/ productCoverageReceiptAttempt = iteration

ProductAdmissionRequiresCoverage ==
    productAdmission # "Admitted"
    \/ productCoverageReceiptAttempt = iteration

AttemptEvidenceMatchesPhase ==
    /\ (phase # "ProductAttempt" \/ productEvidenceAttempt = iteration)
    /\ (phase # "Planning" \/ productEvidenceAttempt = -1)

SupersededAttemptClearsCoverage ==
    phase # "Planning"
    \/ iteration = 0
    \/ productCoverageReceiptAttempt = -1

SupersededAttemptClearsEvidence ==
    phase # "Planning"
    \/ iteration = 0
    \/ productEvidenceAttempt = -1

TerminalFailureIsUnavailable ==
    /\ (productAdmission # "Failed" \/ verdict = "Unavailable")
    /\ (legacyAdmission # "Failed" \/ verdict = "Unavailable")

Termination == <>(phase = "Done")

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Next)

=============================================================================
