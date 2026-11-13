#!/usr/bin/env python3
"""Reproduce proposed reading shapes; no installed command or network calls."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
SOURCE_HASH = '291faaf34918bb9150312009e70a4ecdb372118e80a4938f64bfeb8eda8927df'
FIELDS = ('summary', 'operators', 'value_kind', 'examples', 'effects', 'input_rules', 'values', 'requires')
ARRAYS = FIELDS[1:2] + FIELDS[3:]
PROFILES = {
    'catalog': None,
    'direct': ['depends', 'dependency-target'],
    'depth': ['depends', 'dependency-depth'],
    'literal': ['library-literal'],
}

def compact(value):
    return (json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n').encode()

def project(source, requested):
    if source['data_scope'] != {'completeness': 'Complete'}:
        raise ValueError('This experiment requires complete available source data')
    facets = {f['key']: f for f in source['_embedded']['facets']}
    if len(facets) != len(source['_embedded']['facets']):
        raise ValueError('duplicate facet key')
    selected = set(facets if requested is None else requested)
    # Only declared required-context edges close automatically. Conditional prose
    # is retained verbatim; it is not parsed into invented dependency edges.
    pending = list(selected)
    while pending:
        key = pending.pop()
        for target in facets[key]['requires']:
            if target not in selected:
                selected.add(target)
                pending.append(target)
    records = {}
    for key, original in facets.items():
        if key not in selected:
            continue
        if original.get('data_states'):
            raise ValueError('non-available source facts require a separate outcome design')
        record = {field: original[field] for field in FIELDS}
        # Fixed reading profile: omitted optional arrays mean known empty only
        # inside complete selected records, never for an unselected facet.
        records[key] = {field: value for field, value in record.items() if value != [] or field == 'operators'}
    binding, = source['_embedded']['bindings']
    if binding.get('data_states'):
        raise ValueError('non-available binding facts')
    selected_binding = {field: binding[field] for field in ('gesture', 'input_rules')}
    selected_binding['exposed_facets'] = [key for key in binding['exposed_facets'] if key in selected]
    scope = {'resource': 'package-query/query', 'population': 'selected-facets',
             'completeness': 'Complete', 'catalog_facets': len(facets)}
    direct = {'scope': scope, 'facets': records, 'bindings': {'cli': selected_binding}}
    # Generated selection has no implemented self address. The one advertised
    # link reaches the existing full HAL dataset when missing knowledge is needed.
    hal = {'_links': {'collection': {'href': source['_links']['self']['href'],
                                   'type': 'application/hal+json'}},
           '_embedded': {'facets': [{'key': key, **record} for key, record in records.items()],
                         'bindings': [{'key': 'cli', **selected_binding}]}, 'scope': scope}
    # Recover the entire selected semantic profile, independently of jq tasks.
    for key, record in records.items():
        recovered = {field: record.get(field, [] if field in ARRAYS else None) for field in FIELDS}
        assert recovered == {field: facets[key][field] for field in FIELDS}, key
    assert selected_binding['input_rules'] == binding['input_rules']
    return direct, hal

def run_queries(document, form):
    answers = {}
    tasks = ['inventory', 'preparation', 'context', 'binding', 'missing']
    for task in tasks:
        result = subprocess.run(['jq', '-ce', '--arg', 'form', form, '--arg', 'task', task,
                                 '-f', str(HERE / 'read.jq')], input=compact(document),
                                capture_output=True, check=True)
        answers[task] = json.loads(result.stdout)
    return answers

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source_bytes = (HERE / 'source-hal.json').read_bytes()
assert hashlib.sha256(source_bytes).hexdigest() == SOURCE_HASH, 'source pin changed'
source = json.loads(source_bytes)
args.output.mkdir(parents=True, exist_ok=True)
report = {'source_sha256': SOURCE_HASH, 'source_bytes': len(source_bytes),
          'source_compact_bytes': len(compact(source)), 'profiles': {}}
for name, selected in PROFILES.items():
    direct, hal = project(source, selected)
    baseline_answers = run_queries(source, 'source')
    answers = run_queries(direct, 'direct')
    assert answers == run_queries(hal, 'hal'), name
    # Compare every retained semantic field against the unmodified source.
    expected = {key: baseline_answers['preparation'][key] for key in direct['facets']}
    assert answers['preparation'] == expected, name
    assert answers['context'] == {key: baseline_answers['context'][key] for key in direct['facets']}, name
    assert answers['inventory'] == [entry for entry in baseline_answers['inventory']
                                    if entry['key'] in direct['facets']], name
    assert answers['binding'] == {**baseline_answers['binding'],
        'exposed_facets': [key for key in baseline_answers['binding']['exposed_facets']
                           if key in direct['facets']]}, name
    assert answers['missing']['status'] == ('known' if name == 'catalog' else 'unselected'), name
    for form, document in [('direct', direct), ('hal', hal)]:
        (args.output / f'{name}-{form}.json').write_bytes(compact(document))
    (args.output / f'{name}-answers.json').write_bytes(compact(answers))
    report['profiles'][name] = {'facets': list(direct['facets']),
                               'direct_bytes': len(compact(direct)),
                               'hal_bytes': len(compact(hal)),
                               'answers': len(answers)}
# A required target absent from the source must fail rather than become empty.
broken = json.loads(source_bytes)
broken['_embedded']['facets'] = [f for f in broken['_embedded']['facets'] if f['key'] != 'library-target']
try:
    project(broken, ['library-literal'])
except KeyError:
    pass
else:
    raise AssertionError('unresolved required context admitted')
assert report['profiles']['literal']['facets'] == ['library-literal', 'library-target']
assert report['profiles']['depth']['facets'] == ['dependency-target', 'depends', 'dependency-depth']
assert 'dependency-depth' not in report['profiles']['direct']['facets']
report['gates'] = ['all selected semantic fields recovered', 'five jq answers agree per profile',
                   'source semantic answers preserved', 'required-context closure',
                   'unresolved context rejected', 'unselected facets remain unknown']
(args.output / 'report.json').write_bytes(compact(report))
print(json.dumps(report, indent=2))
