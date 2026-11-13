{identity: .facts.identity, name: .facts.name,
 listed_count: .facts.members, accepted_by: .facts["accepted-by"],
 values: ._links["vocabulary-value"],
 target_completeness: .relationships["vocabulary-value"].completeness,
 expansion_completeness: .traversal.completeness,
 truncation_reasons: .traversal.truncation_reasons}
# Links at depth zero provide navigation, not the values' facts or labels.
