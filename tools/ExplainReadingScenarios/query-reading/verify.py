"""Capture/check production query reading and compare the prior presentation shape.

Usage: python3 verify.py HEAD_BINARY [--write] [--baseline BASE_BINARY]
No package query is executed; all requests read installed explanations.
"""
import argparse
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
CASES = {
    'catalog': 'package-query/query',
    'depends': 'package-query/query/facets/depends',
    'literal': 'package-query/query/facets/library-literal',
}
PRESENTATION = {
    'presentation_order', 'control_kind', 'presentation_kind', 'selection_group',
    'combines_within_selection_group', 'replacement_group', 'options',
}


def capture(binary, name, projection):
    if name == 'dependency':
        args = ['package', 'query', '--explain', '-Q', 'depends,dependency-target']
    else:
        args = ['explain', CASES[name], '.' + projection, '--json']
    result = subprocess.run([str(binary), *args], check=True, capture_output=True)
    assert not result.stderr, result.stderr.decode()
    return result.stdout


def compact_baseline(value, projection):
    """Comparison oracle for the declared profile boundary, not a product writer."""
    value['scope']['profile'] = 'query-preparation/v2'
    facets = (value['_embedded']['facets'] if projection == 'hal'
              else value['facets'].values())
    for facet in facets:
        options = facet['options']
        if options:
            facet['value_descriptions'] = {option['value']: option['summary'] for option in options}
        for field in PRESENTATION:
            del facet[field]
        for field in ['execution_class', 'option_source']:
            if facet[field] is None:
                del facet[field]
    return value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('binary', type=Path)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    for name in CASES:
        pair = {}
        for projection in ['data', 'hal']:
            raw = capture(args.binary.resolve(), name, projection)
            value = json.loads(raw)
            assert value['scope']['profile'] == 'query-preparation/v2'
            assert value['scope']['completeness'] == 'Complete'
            path = HERE / 'examples' / f'{name}-{projection}.json'
            if args.write:
                path.write_bytes(raw)
            else:
                assert raw == path.read_bytes(), f'stale example: {path}'
            pair[projection] = value
            measurement = {'case': name, 'projection': projection, 'bytes': len(raw)}
            if args.baseline:
                before = capture(args.baseline.resolve(), name, projection)
                assert json.loads(before)['scope']['profile'] == 'query-preparation/v1'
                assert compact_baseline(json.loads(before), projection) == value
                measurement['baseline_bytes'] = len(before)
            print(json.dumps(measurement))
        hal = pair['hal']['_embedded']
        for relation in ['facets', 'bindings']:
            keyed = {record['key']: {k: v for k, v in record.items() if k != 'key'}
                     for record in hal[relation]}
            assert keyed == pair['data'][relation]
        assert pair['data']['facet_order'] == [facet['key'] for facet in hal['facets']]
        assert pair['data']['scope'] == pair['hal']['scope']
    raw = capture(args.binary.resolve(), 'dependency', 'hal')
    path = HERE / 'examples' / 'dependency-hal.json'
    if args.write:
        path.write_bytes(raw)
    else:
        assert raw == path.read_bytes(), f'stale example: {path}'
    measurement = {'case': 'dependency', 'projection': 'hal', 'bytes': len(raw)}
    if args.baseline:
        before = capture(args.baseline.resolve(), 'dependency', 'hal')
        assert compact_baseline(json.loads(before), 'hal') == json.loads(raw)
        measurement['baseline_bytes'] = len(before)
    print(json.dumps(measurement))



if __name__ == '__main__':
    main()
