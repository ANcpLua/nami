"""Canonicalize Weaver's expanded v2 view without changing effective definitions.

The pinned mixed-format upstream trees contain excluded legacy shadows. Select
nonexcluded definitions in dependency order, retain attribute refinements as
separate catalog entries, and carry origin schema URLs as explicit annotations.
"""
import copy,json
from pathlib import Path

def materialize(raw):
 root=raw['schema_url'];deps=raw.get('dependencies',{});graph=raw.get('dependency_graph',{})
 def height(s):return 1+max((height(t) for t in graph.get(s,[])),default=-1)
 sources=[(root,raw['registry'])]+[(s,v['registry']) for s,v in sorted(deps.items(),key=lambda x:(-height(x[0]),x[0]))]
 def merge(field,idfield):
  all=[]
  for fallback,registry in sources:
   for v in registry.get(field,[]):
    if v.get('annotations',{}).get('dependency_resolution',{}).get('exclude'):continue
    src=v.get('provenance',{}).get('source',fallback)
    rank=(0 if src==root else 1,-height(src),src)
    all.append((rank,v[idfield],v,src))
  seen={}
  for rank,k,v,src in sorted(all,key=lambda x:(x[0],x[1])):
   if k not in seen:seen[k]=(copy.deepcopy(v),src)
  return [seen[k] for k in sorted(seen)]
 # Preserve per-reference policy annotations in the materialized catalog.
 policies={}
 for v,src in merge('attribute_groups','id'):
  if v['id']=='nami.persistence':
   policies={a['key']:a.get('annotations',{}) for a in v.get('attributes',[])}
 cat=[]; cat_idx={}
 attr_keys={'key','type','examples','brief','note','stability','deprecated','annotations'}
 def origin(v,src):
  v=copy.deepcopy(v);actual=v.pop('provenance',{}).get('source',src)
  v.setdefault('annotations',{})['nami.source_schema']=actual
  return v
 def intern(a,src):
  a=origin({k:v for k,v in a.items() if k in attr_keys or k=='provenance'},src)
  a.setdefault('annotations',{}).update(policies.get(a['key'],{}))
  s=json.dumps(a,sort_keys=True,separators=(',',':'))
  if s not in cat_idx:cat_idx[s]=len(cat);cat.append(a)
  return cat_idx[s]
 def ar(a,src,span=False):
  r={'base':intern(a,src),'requirement_level':a.get('requirement_level','recommended')}
  if span and 'sampling_relevant' in a:r['sampling_relevant']=a['sampling_relevant']
  return r
 def associations(xs):
  result=[]
  for a in xs:
   a=copy.deepcopy(a);a.pop('provenance',None)
   if 'one_of' in a:a['one_of']=associations(a['one_of'])
   if 'all_of' in a:a['all_of']=associations(a['all_of'])
   result.append(a)
  return result
 def signal(v,src,kind):
  out=origin(v,src)
  if kind=='entities':out.setdefault('identity',[])
  if kind=='attribute_groups':out.setdefault('attributes',[])
  if 'attributes' in out:out['attributes']=[ar(a,src,kind=='spans') for a in out['attributes']]
  if 'identity' in out:out['identity']=[intern(a,src) for a in out['identity']]
  if 'description' in out and isinstance(out['description'],list):out['description']=[ar(a,src) for a in out['description']]
  if 'entity_associations' in out:out['entity_associations']=associations(out['entity_associations'])
  # Visibility is authored-only; only public groups appear in the Forge view.
  out.pop('visibility',None)
  return out
 registry={'attributes':[intern(v,src) for v,src in merge('attributes','key')]}
 for field,idfield in [('attribute_groups','id'),('metrics','name'),('spans','type'),('events','name'),('entities','type')]:
  registry[field]=[signal(v,src,field) for v,src in merge(field,idfield)]
 refs={}
 for kind,entries in raw.get('refinements',{}).items():
  refs[kind]=[signal(v,root,kind) for v in sorted(entries,key=lambda x:x['id'])]
 return {'file_format':'resolved/2.0','schema_url':root,'attribute_catalog':cat,'registry':registry,'refinements':refs}

if __name__=='__main__':
 import sys
 Path(sys.argv[2]).write_text(json.dumps(materialize(json.loads(Path(sys.argv[1]).read_text())),ensure_ascii=False,sort_keys=True,indent=2)+'\n')
