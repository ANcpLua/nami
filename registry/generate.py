#!/usr/bin/env python3
"""Validate immutable upstream schemas, materialize them, and run Weaver targets."""
from __future__ import annotations

import argparse
import io
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile

from materialize import materialize

ROOT = Path(__file__).resolve().parent.parent
CORE_COMMIT = 'e10a930844c6951757a43b849d364f7d056ac32b'
GENAI_COMMIT = '3fe84604a581b3d50dd5b4bd29383b462d2a5018'
WEAVER_VERSION = '0.28.0'
CORE_URL = 'https://github.com/open-telemetry/semantic-conventions.git'
GENAI_URL = 'https://github.com/open-telemetry/semantic-conventions-genai.git'


def run(args, **kwargs):
    result = subprocess.run(args, cwd=ROOT, text=True, capture_output=True, **kwargs)
    if result.returncode:
        raise RuntimeError(f"{' '.join(map(str,args))}\n{result.stdout}\n{result.stderr}")
    return result


def pinned_model(url, ref, sibling, cache, destination):
    """Read git objects, never a possibly modified developer worktree."""
    source = ROOT.parent / sibling
    if not (source / '.git').exists() or subprocess.run(['git','-C',str(source),'cat-file','-e',ref+'^{commit}'],capture_output=True).returncode:
        source = cache / sibling
        if not (source / '.git').exists():
            source.parent.mkdir(parents=True, exist_ok=True)
            run(['git','clone','--no-checkout','--filter=blob:none',url,str(source)])
        run(['git','-C',str(source),'fetch','--depth','1','origin',ref])
    commit = run(['git','-C',str(source),'rev-parse',ref+'^{commit}']).stdout.strip()
    data = subprocess.run(['git','-C',str(source),'archive',commit,'model'],check=True,capture_output=True).stdout
    destination.mkdir(parents=True,exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(data)) as archive:
        archive.extractall(destination,filter='data')
    return commit


def leaf_errors(error):
    for key,value in error.items():
        if key in ('Resolver','FailToResolveDefinition','CompoundError'):
            if isinstance(value,list):
                for item in value: yield from leaf_errors(item)
            else: yield from leaf_errors(value)
        else: yield key,value


def migration_diagnostic(kind, value, workspace):
    # These are immutable, known v1/v2 migration issues in Weaver 0.28 and the
    # pinned dependencies. Validation of the normalized result remains strict.
    if kind == 'UnstableFileFormat':
        return value.get('file_format') == 'definition/2' and str(workspace) in value.get('provenance','')
    if kind == 'DeprecatedIncludeUnreferencedWarning':
        return True
    if kind == 'MissingManifestFileFormat':
        return value.get('expected_format') == 'definition_manifest/2.0' and any(value.get('path','').endswith('/'+p+'/model/manifest.yaml') for p in ('core','genai'))
    if kind == 'MissingRequirementLevelWarning':
        if value.get('group_id') in ('span.a2a.client','span.a2a.server'):
            # This immutable GenAI revision omits the signal requirement level.
            # Do not accept this exception for arbitrary revisions or source files.
            authored=Path(workspace)/'genai/model/a2a/spans.yaml'
            return GENAI_COMMIT == '3fe84604a581b3d50dd5b4bd29383b462d2a5018' and value.get('path_or_url') in (str(authored),str(authored.resolve()))
        return value.get('group_id') in ('span.gen_ai.fetch_response.client','entity.gen_ai.main_agent','metric.gen_ai.invoke_agent.inference_calls','metric.gen_ai.invoke_agent.tool_calls') and '/genai/model/' in value.get('path_or_url','')
    if kind == 'InvalidAttributeWarning':
        return value.get('attribute_id') == 'gen_ai.main_agent.id' and value.get('group_id') == 'entity.gen_ai.main_agent' and value.get('error') == "Identity attributes are always required. Setting 'requirement_level' on an identity attribute is deprecated and has no effect." and '/genai/model/' in value.get('path_or_url','')
    if kind == 'DuplicateAttributeId':
        # The GenAI v2 files retain excluded legacy definitions beside their
        # replacements. The materializer selects only nonexcluded definitions.
        groups=set(value.get('group_ids',[]))
        known_pairs={
            'registry.gen_ai':'gen-ai/registry.yaml',
            'registry.mcp':'mcp/registry.yaml',
            'registry.aws.bedrock':'aws-bedrock/registry.yaml',
            'registry.openai':'openai/registry.yaml',
        }
        for legacy,relative in known_pairs.items():
            authored=Path(workspace)/'genai/model'/relative
            # Accept the spelled and OS-canonical path only; never a suffix
            # that an authored Nami file could accidentally share.
            for path in (authored,authored.resolve()):
                generated='registry.'+str(path.with_suffix('')).lstrip('/').replace('/','.')
                if groups=={legacy,generated}:
                    return True
        return False
    return False



def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='regenerate in a temporary directory and compare without editing tracked outputs')
    args=parser.parse_args()
    weaver=os.environ.get('WEAVER') or shutil.which('weaver')
    if not weaver:
        candidates=[Path.home()/f'.local/share/mise/installs/github-open-telemetry-weaver/{WEAVER_VERSION}/weaver']
        weaver=next((str(p) for p in candidates if p.is_file()),None)
    if not weaver: raise RuntimeError(f'Weaver {WEAVER_VERSION} is required; run mise install.')
    version=run([weaver,'--version']).stdout.strip()
    if version != 'weaver '+WEAVER_VERSION: raise RuntimeError('Expected weaver '+WEAVER_VERSION+', got '+version)
    manifest=(ROOT/'registry/source/manifest.yaml').read_text()
    expected=GENAI_URL+'@'+GENAI_COMMIT+'[model]'
    paths=re.findall(r'^\s*registry_path:\s*(\S+)\s*$',manifest,re.M)
    if paths != [expected]:
        raise RuntimeError('Registry dependencies must match the current pinned GenAI commit; core is transitive.')
    changelog=(ROOT/'registry/CHANGELOG.md').read_text()
    current_release=changelog.split('\n## ',1)[-1].split('\n## ',1)[0]
    if CORE_COMMIT not in current_release or GENAI_COMMIT not in current_release or f'Weaver: `{WEAVER_VERSION}`' not in current_release:
        raise RuntimeError('The current registry CHANGELOG entry must record both exact pinned commits and the Weaver version.')
    cache=ROOT/'artifacts/registry-source-cache'
    with tempfile.TemporaryDirectory(prefix='nami-registry-') as temp:
        temp=Path(temp)
        core=pinned_model(CORE_URL,CORE_COMMIT,'semantic-conventions',cache,temp/'core')
        genai=pinned_model(GENAI_URL,GENAI_COMMIT,'semantic-conventions-genai',cache,temp/'genai')
        genai_manifest=(temp/'genai/model/manifest.yaml').read_text()
        if CORE_URL+'@v1.44.0[model]' not in genai_manifest:
            raise RuntimeError('The pinned GenAI registry no longer refers to the expected core v1.44.0.')
        authoring=temp/'nami';shutil.copytree(ROOT/'registry/source',authoring,ignore=shutil.ignore_patterns('__pycache__'))
        # Redirect exact pinned source paths only inside this disposable copy.
        replacements={CORE_URL+'@v1.44.0[model]':str(temp/'core/model'),GENAI_URL+'@'+GENAI_COMMIT+'[model]':str(temp/'genai/model')}
        for path in (authoring/'manifest.yaml',temp/'genai/model/manifest.yaml'):
            text=path.read_text()
            for before,after in replacements.items():text=text.replace(before,after)
            path.write_text(text)
        raw=temp/'raw.json'
        resolved=subprocess.run([weaver,'registry','resolve','--v2','--include-unreferenced','-r',str(authoring),'-p',str(ROOT/'registry/policies'),'-f','json','-o',str(raw),'--diagnostic-format','json'],cwd=ROOT,text=True,capture_output=True)
        diagnostics=[]
        pos=resolved.stderr.find('[\n')
        if pos>=0: diagnostics,_=json.JSONDecoder().raw_decode(resolved.stderr[pos:])
        if resolved.returncode not in (0,1):
            raise RuntimeError('Weaver ended abnormally:\n'+resolved.stderr)
        if resolved.returncode and not diagnostics:
            raise RuntimeError('Weaver failed without structured diagnostics:\n'+resolved.stderr)
        for diagnostic in diagnostics:
            for kind,value in leaf_errors(diagnostic['error']):
                if not migration_diagnostic(kind,value,temp):
                    raise RuntimeError('Registry diagnostic is not an approved immutable format migration: '+json.dumps(diagnostic))
        if not raw.exists():raise RuntimeError('Weaver did not resolve the registry:\n'+resolved.stderr)
        source=json.loads(raw.read_text());normalized=materialize(source)
        materialized=temp/'materialized';materialized.mkdir()
        (materialized/'resolved.json').write_text(json.dumps(normalized,ensure_ascii=False,sort_keys=True,indent=2)+'\n')
        (materialized/'manifest.yaml').write_text('file_format: manifest/2.0\nschema_url: '+normalized['schema_url']+'\nstability: development\nresolved_registry_uri: resolved.json\n')
        run([weaver,'--future','registry','check','--v2','-r',str(materialized),'-p',str(ROOT/'registry/policies')])
        (materialized/'source-pins.json').write_text(json.dumps({'core':core,'genai':genai,'weaver':WEAVER_VERSION},sort_keys=True,indent=2)+'\n')
        # Publish the checked self-contained schema at the canonical registry root.
        # Source authoring remains below source/ and is not loaded by this manifest.
        for name in ('manifest.yaml','resolved.json','source-pins.json'):
            expected_bytes=(materialized/name).read_bytes()
            canonical=ROOT/'registry'/name
            if args.check:
                if not canonical.is_file() or canonical.read_bytes()!=expected_bytes:
                    raise RuntimeError('Canonical registry differs; run registry/generate.sh: '+str(canonical))
            else:
                canonical.write_bytes(expected_bytes)
        run([weaver,'--future','registry','check','--v2','-r',str(ROOT/'registry'),'-p',str(ROOT/'registry/policies')])
        destinations=[('csharp','internal/nami.semconv/Generated'),('markdown','docs/semconv')]
        for target,destination in destinations:
            output=temp/target
            run([weaver,'registry','generate','--v2','-r',str(materialized),'-p',str(ROOT/'registry/policies'),'-t',str(ROOT/'templates/registry'),'-D','core_commit='+core,'-D','genai_commit='+genai,target,str(output)])
            actual=ROOT/destination
            files={p.relative_to(output):p.read_bytes() for p in output.rglob('*') if p.is_file()}
            if target == 'csharp':
                attributes=[normalized['attribute_catalog'][i] for i in normalized['registry']['attributes']]
                counts={}
                for kind in ('attributes','metrics','spans','events','entities'):
                    values=attributes if kind=='attributes' else normalized['registry'][kind]
                    counts[kind]={'total':len(values),'stable':sum(v.get('stability')=='stable' and not v.get('deprecated') for v in values)}
                    shapes=normalized.get('refinements',{}).get(kind,[])
                    counts[kind]['stable_refinements']=sum(v.get('stability')=='stable' and not v.get('deprecated') for v in shapes)
                inventory={'core_commit':core,'genai_commit':genai,'weaver':WEAVER_VERSION,'schema_url':normalized['schema_url'],'counts':counts,'attributes':[{'key':v['key'],'stability':v.get('stability'),'deprecated':v.get('deprecated'),'source_schema':v.get('annotations',{}).get('nami.source_schema')} for v in attributes]}
                files[Path('registry-inventory.json')]=(json.dumps(inventory,sort_keys=True,ensure_ascii=False,indent=2)+'\n').encode()
            if args.check:
                differences=[str(actual/name) for name,data in files.items() if not (actual/name).exists() or (actual/name).read_bytes()!=data]
                existing={p.relative_to(actual) for p in actual.rglob('*') if p.is_file()} if actual.exists() else set()
                differences += [str(actual/name)+' (stale)' for name in existing-files.keys()]
                if differences: raise RuntimeError('Generated files differ; run registry/generate.sh:\n'+'\n'.join(differences))
            else:
                actual.mkdir(parents=True,exist_ok=True)
                for name,data in files.items():
                    path=actual/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
                for path in actual.rglob('*'):
                    if path.is_file() and path.relative_to(actual) not in files:path.unlink()
        print(f'Registry verified; core={core}, genai={genai}; {len(diagnostics)} known format migration diagnostics; generated outputs '+('match.' if args.check else 'updated.'))

if __name__=='__main__':
    try:main()
    except (RuntimeError,subprocess.CalledProcessError) as error:
        print(error,file=sys.stderr);sys.exit(1)
