#!/usr/bin/env python3
"""Offline vocabulary reading proposal, not an installed host selection API."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
PROFILES = {
    'accessibility': ('accessibility', None),
    'body-catalog': ('body', None),
    'body-one': ('body', ['ObjectCreationExpression']),
    'style-catalog': ('style', None),
    'slot': ('style', ['slot-local-names']),
    'conflicts': ('style', ['guarded-boolean-return-style:conditional-expression',
                            'guarded-boolean-return-style:branchless']),
}


def compact(value):
    return (json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n').encode()


def source_table(source):
    return {v['id']: v for v in [source] + source['_embedded'].get('vocabularies', [])}


def values(vocabulary):
    rows = vocabulary['_embedded']['values']
    indexed = {v['id']: v for v in rows}
    if len(indexed) != len(rows):
        raise ValueError('duplicate value identity')
    return indexed


def project(source, requested):
    if source['data_scope'] != {'completeness': 'Complete'}:
        raise ValueError('complete source required')
    original = source_table(source)
    root = source['id']
    selected = {root: set(values(source) if requested is None else requested)}
    if not selected[root]:
        raise ValueError('empty selection')
    pending = [(root, key) for key in selected[root]]
    while pending:
        vid, key = pending.pop()
        vocabulary = original[vid]
        if vocabulary.get('data_states'):
            raise ValueError('non-available vocabulary facts')
        row = values(vocabulary)[key]
        if row.get('data_states'):
            raise ValueError('non-available value facts')
        for m in vocabulary['maps']:
            if m['coverage'] != 'complete':
                raise ValueError('partial map coverage needs a separate outcome')
            if m['value-kind'] != 'term':
                continue
            targets = row.get(m['identity'], [])
            targets = targets if isinstance(targets, list) else [targets]
            target = m['target-vocabulary']
            for term in targets:
                if term not in selected.setdefault(target, set()):
                    selected[target].add(term)
                    pending.append((target, term))
    records = {}
    for vid, vocabulary in original.items():
        if vid not in selected:
            continue
        ids = selected[vid]
        rows = {}
        for key, row in values(vocabulary).items():
            if key not in ids:
                continue
            record = {k: v for k, v in row.items() if k not in ('id', 'kind', '_links')}
            # Profile default is exact identity, not a generated human label.
            if record['name'] == key:
                del record['name']
            rows[key] = record
        maps = {}
        for m in vocabulary['maps']:
            maps[m['identity']] = {k: v for k, v in m.items()
                                   if k != 'identity' and v is not None}
        records[vid] = {
            **{k: vocabulary[k] for k in ('name', 'summary') if k in vocabulary},
            'catalog_values': len(values(vocabulary)),
            'accepted_by': vocabulary['accepted_by'],
            'maps': maps,
            'map_order': list(maps),
            'values': rows,
            'value_order': list(rows),
            'defaults': [k for k in vocabulary['defaults'] if k in ids],
            'property_sets': {k: [v for v in members if v in ids]
                              for k, members in vocabulary['property_sets'].items()},
            'property_groups': {k: {group: [v for v in members if v in ids]
                                   for group, members in groups.items() if ids.intersection(members)}
                                for k, groups in vocabulary['property_groups'].items()},
        }
    scope = {'profile': 'vocabulary-reading/proposal', 'vocabulary': root,
             'completeness': 'Complete', 'population': 'selected-values'}
    direct = {'scope': scope, 'vocabularies': records}
    embedded = []
    for vid, record in records.items():
        embedded.append({'id': vid, **{k: v for k, v in record.items()
                                      if k not in ('values', 'value_order')},
                         '_embedded': {'values': [{'id': key, **row}
                                                  for key, row in record['values'].items()]}})
    hal = {'_links': {'collection': {'href': source['_links']['self']['href'],
                                   'type': 'application/hal+json'}},
           '_embedded': {'vocabularies': embedded}, 'scope': scope}
    # Recover every selected reading field, independent of the chosen questions.
    for vid, record in records.items():
        vocabulary = original[vid]
        for field in ('name', 'summary', 'accepted_by'):
            assert record.get(field) == vocabulary.get(field)
        assert record['value_order'] == [key for key in values(vocabulary) if key in selected[vid]]
        assert record['map_order'] == [m['identity'] for m in vocabulary['maps']]
        for key, row in record['values'].items():
            assert {'name': key, **row} == {k: v for k, v in values(vocabulary)[key].items()
                                          if k not in ('id', 'kind', '_links')}
        for key, m in record['maps'].items():
            recovered = {'identity': key, 'target-vocabulary': None, **m}
            assert recovered == next(m for m in vocabulary['maps'] if m['identity'] == key)
    return direct, hal


def query(document, form, vid, key, task):
    result = subprocess.run(['jq', '-ce', '--arg', 'form', form, '--arg', 'vocabulary', vid,
                             '--arg', 'value', key, '--arg', 'task', task,
                             '-f', str(HERE / 'read.jq')], input=compact(document),
                            capture_output=True, check=True)
    return json.loads(result.stdout)


def verify(source, direct, hal):
    recovered = {}
    for vocabulary in hal['_embedded']['vocabularies']:
        rows = vocabulary['_embedded']['values']
        recovered[vocabulary['id']] = {
            **{k: v for k, v in vocabulary.items() if k not in ('id', '_embedded')},
            'values': {row['id']: {k: v for k, v in row.items() if k != 'id'} for row in rows},
            'value_order': [row['id'] for row in rows],
        }
    assert recovered == direct['vocabularies']
    assert hal['scope'] == direct['scope']
    answers = {}
    for vid, record in direct['vocabularies'].items():
        answers[vid] = {}
        for key in record['values']:
            answer = query(direct, 'direct', vid, key, 'value')
            assert answer == query(hal, 'hal', vid, key, 'value')
            assert answer == query(source, 'source', vid, key, 'value')
            answers[vid][key] = answer
        for task in ('inventory', 'defaults', 'sets', 'groups'):
            assert query(direct, 'direct', vid, '', task) == query(hal, 'hal', vid, '', task)
        missing = '__unselected_probe__'
        assert query(direct, 'direct', vid, missing, 'value')['status'] == 'unselected'
        assert query(hal, 'hal', vid, missing, 'value')['status'] == 'unselected'
    return answers


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    provenance = json.loads((HERE / 'sources/provenance.json').read_bytes())
    sources = {}
    for name, pin in provenance['sources'].items():
        raw = (HERE / 'sources' / name).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == pin['sha256'], name
        sources[name.removesuffix('-hal.json')] = json.loads(raw)
    args.output.mkdir(parents=True, exist_ok=True)
    report = {'sources': {}, 'profiles': {}}
    for name, source in sources.items():
        report['sources'][name] = {
            'wire_bytes': provenance['sources'][name + '-hal.json']['bytes'],
            'compact_bytes': len(compact(source)),
            'without_links_bytes': len(compact(strip_links(source))),
            'map_declarations_bytes': len(compact([v['maps'] for v in source_table(source).values()])),
            'referenced_vocabularies_bytes': len(compact(source['_embedded'].get('vocabularies', []))),
            'values': len(values(source)),
        }
    for name, (source_name, requested) in PROFILES.items():
        source = sources[source_name]
        direct, hal = project(source, requested)
        answers = verify(source, direct, hal)
        for form, document in [('direct', direct), ('hal', hal), ('answers', answers)]:
            (args.output / f'{name}-{form}.json').write_bytes(compact(document))
        report['profiles'][name] = {'source': source_name,
                                  'values': {vid: list(v['values']) for vid, v in direct['vocabularies'].items()},
                                  'direct_bytes': len(compact(direct)), 'hal_bytes': len(compact(hal))}
    # Plausible unavailable/partial/missing-context regressions must not silently
    # turn an incomplete observation into known false or an empty value.
    for mutation in ('scope', 'coverage', 'state', 'context', 'unknown'):
        broken = copy.deepcopy(sources['style'])
        requested = ['slot-local-names']
        if mutation == 'scope':
            broken['data_scope']['completeness'] = 'Partial'
        elif mutation == 'coverage':
            broken['maps'][3]['coverage'] = 'partial'
        elif mutation == 'state':
            broken['_embedded']['values'][0]['data_states'] = {'name': 'Unavailable'}
        elif mutation == 'context':
            broken['_embedded']['vocabularies'] = []
        else:
            requested = ['missing-value']
        try:
            project(broken, requested)
        except (ValueError, KeyError):
            pass
        else:
            raise AssertionError('unsupported source admitted: ' + mutation)
    slot, _ = project(sources['style'], ['slot-local-names'])
    assert list(slot['vocabularies']['csharp.style-tiers']['values']) == ['Synthesis']
    assert query(slot, 'direct', 'csharp.style-choices', 'qualify-field-access', 'value')['status'] == 'unselected'
    _, slot_hal = project(sources['style'], ['slot-local-names'])
    assert query(slot_hal, 'hal', 'csharp.style-choices', 'qualify-field-access', 'value')['status'] == 'unselected'
    report['gates'] = ['source hashes pinned', 'all selected fields recovered',
                       'jq value answers equal unmodified source', 'direct/HAL inventories and indices agree',
                       'exact term closure', 'unselected is unknown',
                       'partial/unavailable/missing-context/unknown selections rejected']
    (args.output / 'report.json').write_bytes(compact(report))
    print(json.dumps(report, indent=2))


def strip_links(value):
    if isinstance(value, dict):
        return {k: strip_links(v) for k, v in value.items() if k != '_links'}
    if isinstance(value, list):
        return [strip_links(v) for v in value]
    return value


if __name__ == '__main__':
    main()
