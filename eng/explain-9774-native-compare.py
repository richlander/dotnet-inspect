import argparse, hashlib, json, math, os, platform, statistics, subprocess, time
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--base',required=True,type=Path);p.add_argument('--head',required=True,type=Path)
p.add_argument('--base-sha',required=True);p.add_argument('--head-sha',required=True)
p.add_argument('--output',required=True,type=Path);p.add_argument('--samples',type=int,default=30)
a=p.parse_args(); cases=[]; checks=[]
def call(binary,args):
 start=time.perf_counter_ns(); r=subprocess.run([str(binary),*args],capture_output=True,check=True)
 assert not r.stderr, r.stderr.decode()
 return (time.perf_counter_ns()-start)/1e6,r.stdout
roots=[('empty-context','package-query/query/facets/library-target',0),
 ('small-facet','package-query/query/facets/library-literal',0),
 ('style-population','vocabularies/csharp.style-choices',1),
 ('facet-population','package-query/query',1),
 ('body-population','vocabularies/csharp.body-kinds',1)]
for name,path,depth in roots:
 original=['explain',path,'--depth',str(depth)]
 old=json.loads(call(a.base,original+['--json'])[1])
 new=json.loads(call(a.head,['explain',path,'.contract','--depth',str(depth),'--json'])[1])
 assert len(old['resources'])==len(new['resources']),path
 for before,after in zip(old['resources'],new['resources']):
  filtered=dict(after);filtered['facts']=[fact for fact in after['facts'] if fact['fact']['value']!='input-rules']
  assert before==filtered,path
 checks.append({'case':name,'resources':len(old['resources']),
   'root_identity_sha256':hashlib.sha256(json.dumps(old['resources'][0]['key'],sort_keys=True).encode()).hexdigest(),
   'common_snapshots_equal':True})
 for mode,flags in [('json',['--json']),('markdown',[]),('plaintext',['--plaintext']),('contract',['--json'])]:
  headargs=original+flags if mode!='contract' else ['explain',path,'.contract','--depth',str(depth),'--json']
  cases.append((name,mode,original+flags,headargs))
 for mode in ['data','hal']:
  # Baseline complete contract and candidate selected reading views have distinct wire shapes.
  # Snapshot equality above qualifies common data; reading-task comparisons qualify selection.
  cases.append((name,mode,original+['--json'],['explain',path,'.'+mode,'--json']))
cases.append(('search-small','search',['explain','literal','--json'],['explain','literal','--json']))
rows=[]; load_before=os.getloadavg()
for name,mode,ba,ha in cases:
 for _ in range(3):call(a.base,ba);call(a.head,ha)
 data={'base':[],'head':[]};sizes={}
 for i in range(a.samples):
  order=[('base',a.base,ba),('head',a.head,ha)]
  if i%2:order.reverse()
  for key,binary,args in order:
   ms,out=call(binary,args);data[key].append(ms);sizes[key]=len(out)
 row={'case':name,'terminal':mode,'base_command':ba,'head_command':ha,'bytes':sizes}
 for key,v in data.items():row[key]={'median_ms':round(statistics.median(v),3),'p95_ms':round(sorted(v)[math.ceil(.95*len(v))-1],3)}
 row['median_ratio']=round(row['head']['median_ms']/row['base']['median_ms'],3)
 rows.append(row)
report={'base_sha':a.base_sha,'head_sha':a.head_sha,'host':platform.platform(),'machine':platform.machine(),
 'warmups':3,'samples':a.samples,'ordering':'alternating interleaved; stdout/stderr captured to completion',
 'load_before':load_before,'load_after':os.getloadavg(),'functional_checks':checks,'rows':rows}
for key,binary in [('base',a.base),('head',a.head)]:
 report[key]={'version':call(binary,['--version'])[1].decode().strip(),
  'flavor':call(binary,['--flavor'])[1].decode().strip(),
  'binary_sha256':hashlib.sha256(binary.read_bytes()).hexdigest()}
 assert report[key]['flavor'].startswith('NativeAOT;'),key
 assert report[key]['version'].endswith(report[key+'_sha'][:7]),key
 assert report[key]['version'].startswith('0.27.0+'),key
a.output.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
