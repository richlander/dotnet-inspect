# Counts equal the records, and the outcome follows the section outcomes.
.counts.types == (.types | length) and .counts.members == (.members | length) and (.outcome == (if all(.section_outcomes[]; .status | IN("complete", "not_requested", "offline")) then "complete" else "partial" end))
