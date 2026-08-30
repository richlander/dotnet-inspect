-------------------------- MODULE ViewerSession --------------------------
(***************************************************************************)
(* Design model of one Annotated Source embedded reader and modal viewer.  *)
(*                                                                         *)
(* The model checks viewer-local selection, Finding detail, active versus  *)
(* rendered annotations, exact opener restoration, non-empty media, modal  *)
(* opening and dismissal, and layered Escape. Shell modal composition,     *)
(* browser history, navigation authority, and packet state are not modeled. *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals

CONSTANTS
  Dual,
  IlOnly,
  Unanchored,
  Structural,
  DualCs,
  DualCsSibling,
  DualIl,
  IlOnlyIl,
  StructuralIl,
  Node

Findings == {Dual, IlOnly, Unanchored}
StructuralAnnotations == {Structural}
Annotations == Findings \cup StructuralAnnotations
FindingTargetIds == {DualCs, DualCsSibling, DualIl, IlOnlyIl}
Targets == FindingTargetIds \cup {StructuralIl}
TargetPairs ==
  {<<DualCs, Dual>>, <<DualCsSibling, Dual>>, <<DualIl, Dual>>,
   <<IlOnlyIl, IlOnly>>}
AnnotationTargetPairs == TargetPairs \cup {<<StructuralIl, Structural>>}
CSharpTargets == {DualCs, DualCsSibling}
IlTargets == {DualIl, IlOnlyIl}
IlAnnotationTargets == IlTargets \cup {StructuralIl}
Nodes == {Node}

NoValue == "none"

ASSUME /\ Cardinality(Findings) = 3
       /\ Cardinality(Annotations) = 4
       /\ Cardinality(FindingTargetIds) = 4
       /\ Cardinality(Targets) = 5
       /\ Cardinality(Nodes) = 1
       /\ NoValue \notin Annotations \cup Targets \cup Nodes
       /\ TargetPairs \subseteq FindingTargetIds \X Findings
       /\ AnnotationTargetPairs \subseteq Targets \X Annotations
       /\ \A target \in FindingTargetIds :
            Cardinality(
              {finding \in Findings :
                 <<target, finding>> \in TargetPairs}) = 1
       /\ \A target \in Targets :
            Cardinality(
              {annotation \in Annotations :
                 <<target, annotation>> \in AnnotationTargetPairs}) = 1
       /\ CSharpTargets \cup IlTargets = FindingTargetIds
       /\ CSharpTargets \cap IlTargets = {}
       /\ CSharpTargets \cup IlAnnotationTargets = Targets
       /\ CSharpTargets \cap IlAnnotationTargets = {}
       /\ CSharpTargets # {}
       /\ IlTargets # {}
       /\ StructuralIl \in IlAnnotationTargets
       /\ StructuralIl \notin FindingTargetIds
       /\ \E finding \in Findings :
            /\ \E target \in CSharpTargets :
                 <<target, finding>> \in TargetPairs
            /\ \E target \in IlTargets :
                 <<target, finding>> \in TargetPairs
       /\ \E finding \in Findings :
            Cardinality(
              {target \in CSharpTargets :
                 <<target, finding>> \in TargetPairs}) = 2
       /\ \E finding \in Findings :
            ~\E target \in FindingTargetIds :
                <<target, finding>> \in TargetPairs

Surfaces == {"Embedded", "Modal"}
Media == {"CSharp", "IL"}
SupportedMediaSets == {{"CSharp"}, Media}
PrimaryKinds == {"None", "Finding", "Node"}
OpenerKinds == {"None", "Chip", "Inspector"}
FocusKinds ==
  {"None", "Detail", "Explore", "ModalHeading", "Chip", "Inspector", "Node",
   "AnnotationSetControl", "AnnotationToggle", "MediumToggle",
   "CoordinateToggle"}
AnnotationSetControls == {"DefaultControl", "AllControl", "ClearControl"}
CoordinateControl == "CoordinatesControl"
ActionKinds ==
  {"Init", "OpenEmbeddedChip", "OpenModal", "DismissEscape",
   "DismissPointer", "OpenModalFinding", "SelectModalNode", "CloseDetail",
   "EmbeddedEscape", "SetDefault", "SetAll", "ClearAll",
   "ToggleAnnotation", "ToggleMedium", "ToggleCoordinates"}
ReportedStates == {"Default", "All", "Clear", "Custom"}
Values == Annotations \cup Targets \cup Nodes \cup {NoValue}
FocusValues ==
  Values \cup Media \cup AnnotationSetControls \cup {CoordinateControl}
ControlValues ==
  Media \cup AnnotationSetControls \cup {CoordinateControl, NoValue}

FindingOf(target) ==
  CHOOSE finding \in Findings :
    <<target, finding>> \in TargetPairs

FindingTargets(finding) ==
  {target \in FindingTargetIds : <<target, finding>> \in TargetPairs}

AnnotationOf(target) ==
  CHOOSE annotation \in Annotations :
    <<target, annotation>> \in AnnotationTargetPairs

AnnotationTargets(annotation) ==
  {target \in Targets :
     <<target, annotation>> \in AnnotationTargetPairs}

TargetMedium(target) ==
  IF target \in CSharpTargets THEN "CSharp" ELSE "IL"

AnnotatableFor(mediaSet) ==
  {annotation \in Annotations :
     \E target \in AnnotationTargets(annotation) :
       TargetMedium(target) \in mediaSet}

NoPrimary == [kind |-> "None", value |-> NoValue]
FindingPrimary(finding) == [kind |-> "Finding", value |-> finding]
NodePrimary(node) == [kind |-> "Node", value |-> node]

NoDetail ==
  [finding |-> NoValue, opener |-> "None", target |-> NoValue]
ChipDetail(finding, target) ==
  [finding |-> finding, opener |-> "Chip", target |-> target]
InspectorDetail(finding) ==
  [finding |-> finding, opener |-> "Inspector", target |-> NoValue]

NoFocus == [kind |-> "None", value |-> NoValue]
DetailFocus(finding) == [kind |-> "Detail", value |-> finding]
ExploreFocus == [kind |-> "Explore", value |-> NoValue]
ModalHeadingFocus == [kind |-> "ModalHeading", value |-> NoValue]
ChipFocus(target) == [kind |-> "Chip", value |-> target]
InspectorFocus(finding) == [kind |-> "Inspector", value |-> finding]
NodeFocus(node) == [kind |-> "Node", value |-> node]
AnnotationSetFocus(control) ==
  [kind |-> "AnnotationSetControl", value |-> control]
AnnotationToggleFocus(finding) ==
  [kind |-> "AnnotationToggle", value |-> finding]
MediumToggleFocus(medium) ==
  [kind |-> "MediumToggle", value |-> medium]
CoordinateToggleFocus ==
  [kind |-> "CoordinateToggle", value |-> CoordinateControl]

VisibleTargets(activeSet, shownMedia) ==
  {target \in Targets :
      /\ AnnotationOf(target) \in activeSet
      /\ IF target \in CSharpTargets
         THEN "CSharp" \in shownMedia
         ELSE "IL" \in shownMedia}

ModalOpeningFocuses(primary) ==
  IF primary.kind = "Finding"
  THEN {ModalHeadingFocus, InspectorFocus(primary.value)}
  ELSE {ModalHeadingFocus}

EmbeddedEscapeSnapshot(embeddedSelection, embeddedTransient,
                       modalSelection, modalTransient, activeSet,
                       shownMedia, shownCoordinates, reportedState,
                       currentFocus) ==
  [embeddedPrimary |-> embeddedSelection,
   embeddedDetail |-> embeddedTransient,
   modalPrimary |-> modalSelection,
   modalDetail |-> modalTransient,
   active |-> activeSet,
   media |-> shownMedia,
   coordinates |-> shownCoordinates,
   reported |-> reportedState,
   focus |-> currentFocus]

EmptyEmbeddedEscapeSnapshot(defaultSet) ==
  EmbeddedEscapeSnapshot(
    NoPrimary, NoDetail, NoPrimary, NoDetail, defaultSet,
    {"CSharp"}, FALSE, "Default", NoFocus)

Transferable(primary, defaultSet) ==
  /\ primary.kind = "Finding"
  /\ primary.value \in defaultSet
  /\ FindingTargets(primary.value) \cap CSharpTargets # {}

VARIABLES
  defaults,
  surface,
  embeddedPrimary,
  embeddedDetail,
  modalPrimary,
  modalDetail,
  active,
  supportedMedia,
  visibleMedia,
  coordinatesVisible,
  reported,
  focus,
  eventPulse,
  lastAction,
  escapeLayered,
  closedDetail,
  closedSurface,
  closedPrimary,
  closedActive,
  closedMedia,
  closedCoordinates,
  dismissedPrimary,
  openedFinding,
  openedOpener,
  openedTarget,
  openingEmbeddedPrimary,
  openingPriorActive,
  openingPriorMedia,
  openingPriorCoordinates,
  selectedNode,
  nodePriorActive,
  nodePriorMedia,
  nodePriorCoordinates,
  toggledFinding,
  togglePriorActive,
  togglePriorPrimary,
  togglePriorDetail,
  togglePriorMedia,
  togglePriorCoordinates,
  activatedControl,
  controlPriorActive,
  controlPriorPrimary,
  controlPriorDetail,
  controlPriorMedia,
  controlPriorCoordinates,
  embeddedEscapePrior

vars ==
  <<defaults, surface, embeddedPrimary, embeddedDetail, modalPrimary,
    modalDetail, active, supportedMedia, visibleMedia, coordinatesVisible,
    reported, focus, eventPulse, lastAction, escapeLayered, closedDetail,
    closedSurface, closedPrimary, closedActive, closedMedia,
    closedCoordinates, dismissedPrimary, openedFinding, openedOpener,
    openedTarget, openingEmbeddedPrimary, openingPriorActive,
    openingPriorMedia, openingPriorCoordinates, selectedNode,
    nodePriorActive, nodePriorMedia, nodePriorCoordinates, toggledFinding,
    togglePriorActive, togglePriorPrimary, togglePriorDetail,
    togglePriorMedia, togglePriorCoordinates,
    activatedControl, controlPriorActive, controlPriorPrimary,
    controlPriorDetail, controlPriorMedia, controlPriorCoordinates,
    embeddedEscapePrior>>

Annotatable ==
  AnnotatableFor(supportedMedia)

Reported(activeSet, defaultSet) ==
  IF activeSet = defaultSet
  THEN "Default"
  ELSE IF activeSet = Annotatable
       THEN "All"
       ELSE IF activeSet = {}
            THEN "Clear"
            ELSE "Custom"

CurrentDetail ==
  IF surface = "Embedded" THEN embeddedDetail ELSE modalDetail

ExactChipAvailable(target, currentSurface, activeSet, shownMedia) ==
  IF currentSurface = "Embedded"
  THEN /\ target \in CSharpTargets
       /\ FindingOf(target) \in defaults
  ELSE /\ target \in FindingTargetIds
       /\ target \in VisibleTargets(activeSet, shownMedia)

RestoredDetailFocus(detail, currentSurface, activeSet, shownMedia) ==
  IF detail.opener = "Chip"
     /\ ExactChipAvailable(detail.target, currentSurface,
                           activeSet, shownMedia)
  THEN ChipFocus(detail.target)
  ELSE InspectorFocus(detail.finding)

FocusIsValid ==
  CASE focus.kind = "None" -> focus = NoFocus
    [] focus.kind = "Detail" ->
         /\ CurrentDetail # NoDetail
         /\ focus.value = CurrentDetail.finding
    [] focus.kind = "Explore" ->
         /\ surface = "Embedded"
         /\ focus = ExploreFocus
    [] focus.kind = "ModalHeading" ->
         /\ surface = "Modal"
         /\ focus = ModalHeadingFocus
    [] focus.kind = "Chip" ->
         /\ focus.value \in FindingTargetIds
         /\ IF surface = "Embedded"
            THEN /\ focus.value \in CSharpTargets
                 /\ FindingOf(focus.value) \in defaults
            ELSE focus.value \in VisibleTargets(active, visibleMedia)
    [] focus.kind = "Inspector" ->
         /\ surface = "Modal"
         /\ focus.value \in Findings
    [] focus.kind = "Node" ->
         /\ surface = "Modal"
         /\ focus.value \in Nodes
         /\ modalPrimary = NodePrimary(focus.value)
    [] focus.kind = "AnnotationSetControl" ->
         /\ surface = "Modal"
         /\ focus.value \in AnnotationSetControls
    [] focus.kind = "AnnotationToggle" ->
         /\ surface = "Modal"
         /\ focus.value \in Findings
         /\ focus.value \in Annotatable
    [] focus.kind = "MediumToggle" ->
         /\ surface = "Modal"
         /\ focus.value \in supportedMedia
    [] focus.kind = "CoordinateToggle" ->
         /\ surface = "Modal"
         /\ focus = CoordinateToggleFocus
    [] OTHER -> FALSE

ClearCloseHistory ==
  /\ closedDetail' = NoDetail
  /\ closedSurface' = "Embedded"
  /\ closedPrimary' = NoPrimary
  /\ closedActive' = defaults
  /\ closedMedia' = {"CSharp"}
  /\ closedCoordinates' = FALSE

ClearDismissHistory ==
  dismissedPrimary' = NoPrimary

ClearOpenHistory ==
  /\ openedFinding' = NoValue
  /\ openedOpener' = "None"
  /\ openedTarget' = NoValue
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = defaults
  /\ openingPriorMedia' = {"CSharp"}
  /\ openingPriorCoordinates' = FALSE

ClearNodeHistory ==
  /\ selectedNode' = NoValue
  /\ nodePriorActive' = defaults
  /\ nodePriorMedia' = {"CSharp"}
  /\ nodePriorCoordinates' = FALSE

ClearToggleHistory ==
  /\ toggledFinding' = NoValue
  /\ togglePriorActive' = defaults
  /\ togglePriorPrimary' = NoPrimary
  /\ togglePriorDetail' = NoDetail
  /\ togglePriorMedia' = {"CSharp"}
  /\ togglePriorCoordinates' = FALSE

ClearControlHistory ==
  /\ activatedControl' = NoValue
  /\ controlPriorActive' = defaults
  /\ controlPriorPrimary' = NoPrimary
  /\ controlPriorDetail' = NoDetail
  /\ controlPriorMedia' = {"CSharp"}
  /\ controlPriorCoordinates' = FALSE

RecordControlHistory(control) ==
  /\ activatedControl' = control
  /\ controlPriorActive' = active
  /\ controlPriorPrimary' = modalPrimary
  /\ controlPriorDetail' = modalDetail
  /\ controlPriorMedia' = visibleMedia
  /\ controlPriorCoordinates' = coordinatesVisible

ClearEmbeddedEscapeHistory ==
  embeddedEscapePrior' = EmptyEmbeddedEscapeSnapshot(defaults)

ClearHistory ==
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory

Init ==
  /\ supportedMedia \in SupportedMediaSets
  /\ defaults \in SUBSET (Annotatable \cap Findings)
  /\ surface = "Embedded"
  /\ embeddedPrimary = NoPrimary
  /\ embeddedDetail = NoDetail
  /\ modalPrimary = NoPrimary
  /\ modalDetail = NoDetail
  /\ active = defaults
  /\ visibleMedia = {"CSharp"}
  /\ coordinatesVisible = FALSE
  /\ reported = Reported(active, defaults)
  /\ focus = NoFocus
  /\ eventPulse = FALSE
  /\ lastAction = "Init"
  /\ escapeLayered = TRUE
  /\ closedDetail = NoDetail
  /\ closedSurface = "Embedded"
  /\ closedPrimary = NoPrimary
  /\ closedActive = defaults
  /\ closedMedia = {"CSharp"}
  /\ closedCoordinates = FALSE
  /\ dismissedPrimary = NoPrimary
  /\ openedFinding = NoValue
  /\ openedOpener = "None"
  /\ openedTarget = NoValue
  /\ openingEmbeddedPrimary = NoPrimary
  /\ openingPriorActive = defaults
  /\ openingPriorMedia = {"CSharp"}
  /\ openingPriorCoordinates = FALSE
  /\ selectedNode = NoValue
  /\ nodePriorActive = defaults
  /\ nodePriorMedia = {"CSharp"}
  /\ nodePriorCoordinates = FALSE
  /\ toggledFinding = NoValue
  /\ togglePriorActive = defaults
  /\ togglePriorPrimary = NoPrimary
  /\ togglePriorDetail = NoDetail
  /\ togglePriorMedia = {"CSharp"}
  /\ togglePriorCoordinates = FALSE
  /\ activatedControl = NoValue
  /\ controlPriorActive = defaults
  /\ controlPriorPrimary = NoPrimary
  /\ controlPriorDetail = NoDetail
  /\ controlPriorMedia = {"CSharp"}
  /\ controlPriorCoordinates = FALSE
  /\ embeddedEscapePrior = EmptyEmbeddedEscapeSnapshot(defaults)

OpenEmbeddedChip(target) ==
  /\ surface = "Embedded"
  /\ target \in CSharpTargets
  /\ FindingOf(target) \in defaults
  /\ embeddedPrimary' = FindingPrimary(FindingOf(target))
  /\ embeddedDetail' = ChipDetail(FindingOf(target), target)
  /\ focus' = DetailFocus(FindingOf(target))
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenEmbeddedChip"
  /\ openedFinding' = FindingOf(target)
  /\ openedOpener' = "Chip"
  /\ openedTarget' = target
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, modalPrimary, modalDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

OpenModal ==
  /\ surface = "Embedded"
  /\ surface' = "Modal"
  /\ embeddedDetail' = NoDetail
  /\ modalPrimary' =
       IF Transferable(embeddedPrimary, defaults)
       THEN embeddedPrimary
       ELSE NoPrimary
  /\ modalDetail' = NoDetail
  /\ active' = defaults
  /\ visibleMedia' = {"CSharp"}
  /\ coordinatesVisible' = FALSE
  /\ reported' = Reported(active', defaults)
  /\ focus' \in
       ModalOpeningFocuses(
         IF Transferable(embeddedPrimary, defaults)
         THEN embeddedPrimary
         ELSE NoPrimary)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModal"
  /\ openedFinding' = NoValue
  /\ openedOpener' = "None"
  /\ openedTarget' = NoValue
  /\ openingEmbeddedPrimary' = embeddedPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, embeddedPrimary, supportedMedia, escapeLayered>>

DismissModal ==
  /\ surface = "Modal"
  /\ surface' = "Embedded"
  /\ embeddedPrimary' =
       IF Transferable(modalPrimary, defaults)
       THEN modalPrimary
       ELSE NoPrimary
  /\ embeddedDetail' = NoDetail
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ active' = defaults
  /\ visibleMedia' = {"CSharp"}
  /\ coordinatesVisible' = FALSE
  /\ reported' = Reported(active', defaults)
  /\ focus' = ExploreFocus
  /\ eventPulse' = ~eventPulse
  /\ dismissedPrimary' = modalPrimary
  /\ ClearCloseHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, supportedMedia>>

DismissModalByEscape ==
  /\ surface = "Modal"
  /\ modalDetail = NoDetail
  /\ DismissModal
  /\ lastAction' = "DismissEscape"
  /\ escapeLayered' = (escapeLayered /\ modalDetail = NoDetail)

DismissModalByPointer ==
  /\ surface = "Modal"
  /\ DismissModal
  /\ lastAction' = "DismissPointer"
  /\ UNCHANGED escapeLayered

OpenModalFinding(finding, opener, target) ==
  /\ surface = "Modal"
  /\ finding \in Findings
  /\ opener \in {"Chip", "Inspector"}
  /\ IF opener = "Chip"
     THEN /\ target \in FindingTargetIds
          /\ target \in VisibleTargets(active, visibleMedia)
          /\ FindingOf(target) = finding
     ELSE target = NoValue
  /\ modalPrimary' = FindingPrimary(finding)
  /\ modalDetail' =
       IF opener = "Chip"
       THEN ChipDetail(finding, target)
       ELSE InspectorDetail(finding)
  /\ focus' = DetailFocus(finding)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModalFinding"
  /\ openedFinding' = finding
  /\ openedOpener' = opener
  /\ openedTarget' = target
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

SelectModalNode(node) ==
  /\ surface = "Modal"
  /\ node \in Nodes
  /\ modalPrimary' = NodePrimary(node)
  /\ modalDetail' = NoDetail
  /\ focus' = NodeFocus(node)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SelectModalNode"
  /\ selectedNode' = node
  /\ nodePriorActive' = active
  /\ nodePriorMedia' = visibleMedia
  /\ nodePriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

CloseCurrentDetail ==
  /\ CurrentDetail # NoDetail
  /\ LET restored ==
           RestoredDetailFocus(CurrentDetail, surface, active, visibleMedia)
     IN /\ IF surface = "Embedded"
           THEN /\ embeddedDetail' = NoDetail
                /\ modalDetail' = modalDetail
           ELSE /\ modalDetail' = NoDetail
                /\ embeddedDetail' = embeddedDetail
        /\ focus' = restored
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "CloseDetail"
  /\ closedDetail' = CurrentDetail
  /\ closedSurface' = surface
  /\ closedPrimary' =
       IF surface = "Embedded" THEN embeddedPrimary ELSE modalPrimary
  /\ closedActive' = active
  /\ closedMedia' = visibleMedia
  /\ closedCoordinates' = coordinatesVisible
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, modalPrimary, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

EmbeddedEscapeFallsThrough ==
  /\ surface = "Embedded"
  /\ embeddedDetail = NoDetail
  /\ embeddedEscapePrior' =
       EmbeddedEscapeSnapshot(
         embeddedPrimary, embeddedDetail, modalPrimary, modalDetail,
         active, visibleMedia, coordinatesVisible, reported, focus)
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 modalPrimary, modalDetail, active, supportedMedia, visibleMedia,
                 coordinatesVisible, reported, focus>>
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "EmbeddedEscape"
  /\ ClearHistory
  /\ escapeLayered' = (escapeLayered /\ embeddedDetail = NoDetail)

SetDefault ==
  /\ surface = "Modal"
  /\ active' = defaults
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("DefaultControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetDefault"
  /\ RecordControlHistory("DefaultControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

SetAll ==
  /\ surface = "Modal"
  /\ active' = Annotatable
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = modalPrimary
  /\ modalDetail' = modalDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("AllControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetAll"
  /\ RecordControlHistory("AllControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

ClearAll ==
  /\ surface = "Modal"
  /\ active' = {}
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("ClearControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ClearAll"
  /\ RecordControlHistory("ClearControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

ToggleAnnotation(finding) ==
  /\ surface = "Modal"
  /\ finding \in Findings \cap Annotatable
  /\ LET removing == finding \in active
         nextActive ==
           IF removing THEN active \ {finding} ELSE active \cup {finding}
         removesPrimary ==
           /\ removing
           /\ modalPrimary = FindingPrimary(finding)
         nextPrimary ==
           IF removesPrimary THEN NoPrimary ELSE modalPrimary
         nextDetail ==
           IF removesPrimary THEN NoDetail ELSE modalDetail
     IN /\ active' = nextActive
        /\ reported' = Reported(nextActive, defaults)
        /\ modalPrimary' = nextPrimary
        /\ modalDetail' = nextDetail
        /\ focus' = AnnotationToggleFocus(finding)
        /\ toggledFinding' = finding
        /\ togglePriorActive' = active
        /\ togglePriorPrimary' = modalPrimary
        /\ togglePriorDetail' = modalDetail
        /\ togglePriorMedia' = visibleMedia
        /\ togglePriorCoordinates' = coordinatesVisible
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleAnnotation"
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearControlHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, visibleMedia, coordinatesVisible,
                 escapeLayered>>

ToggleMedium(medium) ==
  /\ surface = "Modal"
  /\ medium \in supportedMedia
  /\ LET nextMedia ==
           IF medium \in visibleMedia
              /\ Cardinality(visibleMedia) = 1
           THEN visibleMedia
           ELSE IF medium \in visibleMedia
                THEN visibleMedia \ {medium}
                ELSE visibleMedia \cup {medium}
     IN /\ visibleMedia' = nextMedia
        /\ active' = active
        /\ coordinatesVisible' = coordinatesVisible
        /\ reported' = reported
        /\ modalPrimary' = modalPrimary
        /\ modalDetail' = modalDetail
        /\ focus' = MediumToggleFocus(medium)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleMedium"
  /\ RecordControlHistory(medium)
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
          supportedMedia, escapeLayered>>

ToggleCoordinates ==
  /\ surface = "Modal"
  /\ coordinatesVisible' = ~coordinatesVisible
  /\ active' = active
  /\ visibleMedia' = visibleMedia
  /\ reported' = reported
  /\ modalPrimary' = modalPrimary
  /\ modalDetail' = modalDetail
  /\ focus' = CoordinateToggleFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleCoordinates"
  /\ RecordControlHistory(CoordinateControl)
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearEmbeddedEscapeHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
          supportedMedia, escapeLayered>>

Next ==
  \/ \E target \in FindingTargetIds : OpenEmbeddedChip(target)
  \/ OpenModal
  \/ DismissModalByEscape
  \/ DismissModalByPointer
  \/ \E finding \in Findings,
        opener \in {"Chip", "Inspector"},
        target \in FindingTargetIds \cup {NoValue} :
       OpenModalFinding(finding, opener, target)
  \/ \E node \in Nodes : SelectModalNode(node)
  \/ CloseCurrentDetail
  \/ EmbeddedEscapeFallsThrough
  \/ SetDefault
  \/ SetAll
  \/ ClearAll
  \/ \E finding \in Findings \cap Annotatable : ToggleAnnotation(finding)
  \/ \E medium \in supportedMedia : ToggleMedium(medium)
  \/ ToggleCoordinates

Spec == Init /\ [][Next]_vars

NextActionEnabled(action) ==
  ENABLED (Next /\ lastAction' = action)

NextFindingOpeningEnabled(action, finding, opener, target) ==
  ENABLED
    (/\ Next
     /\ lastAction' = action
     /\ openedFinding' = finding
     /\ openedOpener' = opener
     /\ openedTarget' = target)

NextNodeSelectionEnabled(node) ==
  ENABLED
    (/\ Next
     /\ lastAction' = "SelectModalNode"
     /\ selectedNode' = node)

NextAnnotationToggleEnabled(finding) ==
  ENABLED
    (/\ Next
     /\ lastAction' = "ToggleAnnotation"
     /\ toggledFinding' = finding)

NextControlEnabled(action, control) ==
  ENABLED
    (/\ Next
     /\ lastAction' = action
     /\ activatedControl' = control)

TypeOK ==
  /\ defaults \in SUBSET (Annotatable \cap Findings)
  /\ surface \in Surfaces
  /\ embeddedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ embeddedDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : FindingTargetIds \cup {NoValue}]
  /\ modalPrimary \in [kind : PrimaryKinds, value : Values]
  /\ modalDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : FindingTargetIds \cup {NoValue}]
  /\ active \in SUBSET Annotatable
  /\ supportedMedia \in SupportedMediaSets
  /\ visibleMedia \in SUBSET Media
  /\ visibleMedia \subseteq supportedMedia
  /\ coordinatesVisible \in BOOLEAN
  /\ reported \in ReportedStates
  /\ focus \in [kind : FocusKinds, value : FocusValues]
  /\ eventPulse \in BOOLEAN
  /\ lastAction \in ActionKinds
  /\ escapeLayered \in BOOLEAN
  /\ closedDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : FindingTargetIds \cup {NoValue}]
  /\ closedSurface \in Surfaces
  /\ closedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ closedActive \in SUBSET Annotatable
  /\ closedMedia \in SUBSET Media
  /\ closedCoordinates \in BOOLEAN
  /\ dismissedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ openedFinding \in Findings \cup {NoValue}
  /\ openedOpener \in OpenerKinds
  /\ openedTarget \in FindingTargetIds \cup {NoValue}
  /\ openingEmbeddedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ openingPriorActive \in SUBSET Annotatable
  /\ openingPriorMedia \in SUBSET Media
  /\ openingPriorCoordinates \in BOOLEAN
  /\ selectedNode \in Nodes \cup {NoValue}
  /\ nodePriorActive \in SUBSET Annotatable
  /\ nodePriorMedia \in SUBSET Media
  /\ nodePriorCoordinates \in BOOLEAN
  /\ toggledFinding \in Findings \cup {NoValue}
  /\ togglePriorActive \in SUBSET Annotatable
  /\ togglePriorPrimary \in [kind : PrimaryKinds, value : Values]
  /\ togglePriorDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : FindingTargetIds \cup {NoValue}]
  /\ togglePriorMedia \in SUBSET Media
  /\ togglePriorCoordinates \in BOOLEAN
  /\ activatedControl \in ControlValues
  /\ controlPriorActive \in SUBSET Annotatable
  /\ controlPriorPrimary \in [kind : PrimaryKinds, value : Values]
  /\ controlPriorDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : FindingTargetIds \cup {NoValue}]
  /\ controlPriorMedia \in SUBSET Media
  /\ controlPriorCoordinates \in BOOLEAN
  /\ embeddedEscapePrior \in
       [embeddedPrimary :
          [kind : PrimaryKinds, value : Values],
        embeddedDetail :
          [finding : Findings \cup {NoValue},
           opener  : OpenerKinds,
           target  : FindingTargetIds \cup {NoValue}],
        modalPrimary :
          [kind : PrimaryKinds, value : Values],
        modalDetail :
          [finding : Findings \cup {NoValue},
           opener  : OpenerKinds,
           target  : FindingTargetIds \cup {NoValue}],
        active : SUBSET Annotatable,
        media : SUBSET Media,
        coordinates : BOOLEAN,
        reported : ReportedStates,
        focus : [kind : FocusKinds, value : FocusValues]]

PrimaryShapes ==
  /\ (embeddedPrimary.kind = "None") = (embeddedPrimary = NoPrimary)
  /\ (embeddedPrimary.kind = "Finding" =>
        embeddedPrimary.value \in Findings)
  /\ embeddedPrimary.kind # "Node"
  /\ (modalPrimary.kind = "None") = (modalPrimary = NoPrimary)
  /\ (modalPrimary.kind = "Finding" => modalPrimary.value \in Findings)
  /\ (modalPrimary.kind = "Node" => modalPrimary.value \in Nodes)

DetailShapes ==
  /\ (embeddedDetail = NoDetail)
     \/ /\ embeddedDetail.opener = "Chip"
        /\ embeddedDetail.target \in CSharpTargets
        /\ FindingOf(embeddedDetail.target) = embeddedDetail.finding
        /\ embeddedPrimary = FindingPrimary(embeddedDetail.finding)
  /\ (modalDetail = NoDetail)
     \/ /\ modalDetail.finding \in Findings
        /\ modalPrimary = FindingPrimary(modalDetail.finding)
        /\ IF modalDetail.opener = "Chip"
           THEN /\ modalDetail.target \in FindingTargetIds
                /\ FindingOf(modalDetail.target) = modalDetail.finding
           ELSE /\ modalDetail.opener = "Inspector"
                /\ modalDetail.target = NoValue

EmbeddedStateIsConstrained ==
  /\ (embeddedPrimary = NoPrimary)
     \/ /\ embeddedPrimary.kind = "Finding"
        /\ embeddedPrimary.value \in defaults
        /\ FindingTargets(embeddedPrimary.value) \cap CSharpTargets # {}
  /\ (embeddedDetail = NoDetail)
     \/ embeddedDetail.finding \in defaults

ModalDetailExistsOnlyWhileOpen ==
  surface = "Embedded" => modalDetail = NoDetail

EmbeddedDetailExistsOnlyWhileEmbedded ==
  surface = "Modal" => embeddedDetail = NoDetail

CoordinatesExistOnlyWhileModal ==
  surface = "Embedded" => ~coordinatesVisible

EmbeddedPresentationIsFixed ==
  surface = "Embedded" =>
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}

AtLeastOneMediumIsVisible ==
  /\ visibleMedia # {}
  /\ visibleMedia \subseteq supportedMedia

AnnotatableUniverseIsSupported ==
  \A annotation \in Annotations :
    (annotation \in Annotatable) =
      (\E target \in AnnotationTargets(annotation) :
         TargetMedium(target) \in supportedMedia)

InspectorActionsAreAvailable ==
  \A finding \in Findings :
    (surface = "Modal") =
      NextFindingOpeningEnabled(
        "OpenModalFinding", finding, "Inspector", NoValue)

EmbeddedChipActionsAreExact ==
  \A target \in FindingTargetIds :
    (/\ surface = "Embedded"
     /\ target \in CSharpTargets
     /\ FindingOf(target) \in defaults) =
      NextFindingOpeningEnabled(
        "OpenEmbeddedChip", FindingOf(target), "Chip", target)

ModalChipActionsAreExact ==
  \A target \in FindingTargetIds :
    (/\ surface = "Modal"
     /\ target \in VisibleTargets(active, visibleMedia)) =
      NextFindingOpeningEnabled(
        "OpenModalFinding", FindingOf(target), "Chip", target)

AnnotationToggleActionsAreExact ==
  \A finding \in Findings :
    (/\ surface = "Modal"
     /\ finding \in Annotatable) =
      NextAnnotationToggleEnabled(finding)

MediumToggleActionsAreExact ==
  \A medium \in Media :
    (/\ surface = "Modal"
     /\ medium \in supportedMedia) =
      NextControlEnabled("ToggleMedium", medium)

FixedActionAvailabilityIsExact ==
  /\ (surface = "Embedded") = NextActionEnabled("OpenModal")
  /\ (/\ surface = "Modal"
      /\ modalDetail = NoDetail) =
       NextActionEnabled("DismissEscape")
  /\ (surface = "Modal") = NextActionEnabled("DismissPointer")
  /\ (CurrentDetail # NoDetail) = NextActionEnabled("CloseDetail")
  /\ (/\ surface = "Embedded"
      /\ embeddedDetail = NoDetail) =
       NextActionEnabled("EmbeddedEscape")
  /\ (surface = "Modal") = NextActionEnabled("SetDefault")
  /\ (surface = "Modal") = NextActionEnabled("SetAll")
  /\ (surface = "Modal") = NextActionEnabled("ClearAll")
  /\ (surface = "Modal") = NextActionEnabled("ToggleCoordinates")

NodeActionsAreExact ==
  \A node \in Nodes :
    (surface = "Modal") = NextNodeSelectionEnabled(node)

RenderedTargetsAreExact ==
  \A target \in Targets :
    (target \in VisibleTargets(active, visibleMedia)) =
      (/\ AnnotationOf(target) \in active
       /\ TargetMedium(target) \in visibleMedia)

ReportedStateIsDerived ==
  CASE active = defaults ->
         reported = "Default"
    [] active # defaults /\ active = Annotatable ->
         reported = "All"
    [] active # defaults /\ active # Annotatable /\ active = {} ->
         reported = "Clear"
    [] OTHER ->
         reported = "Custom"

ModalAlwaysHasFocus ==
  surface = "Modal" => focus # NoFocus

DetailClosureOutcomeIsExact ==
  lastAction = "CloseDetail" =>
    LET exactChipWasAvailable ==
          /\ closedDetail.opener = "Chip"
          /\ IF closedSurface = "Embedded"
             THEN /\ closedDetail.target \in CSharpTargets
                  /\ FindingOf(closedDetail.target) \in defaults
             ELSE closedDetail.target \in
                    VisibleTargets(closedActive, closedMedia)
        expectedFocus ==
          IF exactChipWasAvailable
          THEN ChipFocus(closedDetail.target)
          ELSE InspectorFocus(closedDetail.finding)
    IN /\ surface = closedSurface
       /\ CurrentDetail = NoDetail
       /\ active = closedActive
       /\ visibleMedia = closedMedia
       /\ coordinatesVisible = closedCoordinates
       /\ IF closedSurface = "Embedded"
          THEN embeddedPrimary = closedPrimary
          ELSE modalPrimary = closedPrimary
       /\ focus = expectedFocus

FindingOpeningIsExact ==
  lastAction \in {"OpenEmbeddedChip", "OpenModalFinding"} =>
    IF lastAction = "OpenEmbeddedChip"
    THEN /\ surface = "Embedded"
         /\ openedOpener = "Chip"
         /\ embeddedPrimary = FindingPrimary(openedFinding)
         /\ embeddedDetail = ChipDetail(openedFinding, openedTarget)
         /\ active = openingPriorActive
         /\ visibleMedia = openingPriorMedia
         /\ coordinatesVisible = openingPriorCoordinates
         /\ focus = DetailFocus(openedFinding)
    ELSE /\ surface = "Modal"
         /\ modalPrimary = FindingPrimary(openedFinding)
         /\ modalDetail =
              IF openedOpener = "Chip"
              THEN ChipDetail(openedFinding, openedTarget)
              ELSE InspectorDetail(openedFinding)
         /\ active = openingPriorActive
         /\ visibleMedia = openingPriorMedia
         /\ coordinatesVisible = openingPriorCoordinates
         /\ focus = DetailFocus(openedFinding)

NodeSelectionOutcomeIsExact ==
  lastAction = "SelectModalNode" =>
    /\ surface = "Modal"
    /\ modalPrimary = NodePrimary(selectedNode)
    /\ modalDetail = NoDetail
    /\ active = nodePriorActive
    /\ visibleMedia = nodePriorMedia
    /\ coordinatesVisible = nodePriorCoordinates
    /\ focus = NodeFocus(selectedNode)

AnnotationToggleOutcomeIsExact ==
  lastAction = "ToggleAnnotation" =>
    LET wasActive == toggledFinding \in togglePriorActive
        expectedActive ==
          IF wasActive
          THEN togglePriorActive \ {toggledFinding}
          ELSE togglePriorActive \cup {toggledFinding}
        removedPrimary ==
          /\ wasActive
          /\ togglePriorPrimary = FindingPrimary(toggledFinding)
    IN /\ active = expectedActive
       /\ modalPrimary =
            IF removedPrimary THEN NoPrimary ELSE togglePriorPrimary
       /\ modalDetail =
            IF removedPrimary THEN NoDetail ELSE togglePriorDetail
       /\ visibleMedia = togglePriorMedia
       /\ coordinatesVisible = togglePriorCoordinates
       /\ focus = AnnotationToggleFocus(toggledFinding)

ControlOutcomeIsExact ==
  lastAction \in
    {"SetDefault", "SetAll", "ClearAll",
     "ToggleMedium", "ToggleCoordinates"} =>
    CASE lastAction = "SetDefault" ->
           /\ activatedControl = "DefaultControl"
           /\ active = defaults
           /\ modalPrimary = NoPrimary
           /\ modalDetail = NoDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("DefaultControl")
      [] lastAction = "SetAll" ->
           /\ activatedControl = "AllControl"
           /\ active = Annotatable
           /\ modalPrimary = controlPriorPrimary
           /\ modalDetail = controlPriorDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("AllControl")
      [] lastAction = "ClearAll" ->
           /\ activatedControl = "ClearControl"
           /\ active = {}
           /\ modalPrimary = NoPrimary
           /\ modalDetail = NoDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("ClearControl")
      [] lastAction = "ToggleMedium" ->
           LET medium == activatedControl
               expectedMedia ==
                 IF medium \in controlPriorMedia
                    /\ Cardinality(controlPriorMedia) = 1
                 THEN controlPriorMedia
                 ELSE IF medium \in controlPriorMedia
                      THEN controlPriorMedia \ {medium}
                      ELSE controlPriorMedia \cup {medium}
           IN /\ medium \in supportedMedia
              /\ active = controlPriorActive
              /\ modalPrimary = controlPriorPrimary
              /\ modalDetail = controlPriorDetail
              /\ visibleMedia = expectedMedia
              /\ coordinatesVisible = controlPriorCoordinates
              /\ focus = MediumToggleFocus(medium)
      [] lastAction = "ToggleCoordinates" ->
           /\ activatedControl = CoordinateControl
           /\ active = controlPriorActive
           /\ modalPrimary = controlPriorPrimary
           /\ modalDetail = controlPriorDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = ~controlPriorCoordinates
           /\ focus = CoordinateToggleFocus

ModalOpeningIsFresh ==
  lastAction = "OpenModal" =>
    /\ surface = "Modal"
    /\ modalPrimary =
         IF /\ openingEmbeddedPrimary.kind = "Finding"
            /\ openingEmbeddedPrimary.value \in defaults
            /\ FindingTargets(openingEmbeddedPrimary.value)
                 \cap CSharpTargets # {}
         THEN openingEmbeddedPrimary
         ELSE NoPrimary
    /\ modalDetail = NoDetail
    /\ embeddedDetail = NoDetail
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}
    /\ coordinatesVisible = FALSE
    /\ focus \in ModalOpeningFocuses(modalPrimary)

ModalDismissalIsExact ==
  lastAction \in {"DismissEscape", "DismissPointer"} =>
    /\ surface = "Embedded"
    /\ embeddedPrimary =
         IF /\ dismissedPrimary.kind = "Finding"
            /\ dismissedPrimary.value \in defaults
            /\ FindingTargets(dismissedPrimary.value)
                 \cap CSharpTargets # {}
         THEN dismissedPrimary
         ELSE NoPrimary
    /\ embeddedDetail = NoDetail
    /\ modalPrimary = NoPrimary
    /\ modalDetail = NoDetail
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}
    /\ coordinatesVisible = FALSE
    /\ focus = ExploreFocus

EscapeCannotBypassDetail ==
  escapeLayered

EmbeddedEscapeOutcomeIsExact ==
  lastAction = "EmbeddedEscape" =>
    /\ surface = "Embedded"
    /\ embeddedPrimary = embeddedEscapePrior.embeddedPrimary
    /\ embeddedDetail = embeddedEscapePrior.embeddedDetail
    /\ modalPrimary = embeddedEscapePrior.modalPrimary
    /\ modalDetail = embeddedEscapePrior.modalDetail
    /\ active = embeddedEscapePrior.active
    /\ visibleMedia = embeddedEscapePrior.media
    /\ coordinatesVisible = embeddedEscapePrior.coordinates
    /\ reported = embeddedEscapePrior.reported
    /\ focus = embeddedEscapePrior.focus

=============================================================================
