#!/usr/bin/env bash
set -euo pipefail

version=${1:?usage: smoke-orchestration-package.sh <version> [local-feed]}
feed=${2:-}
repo=$(cd "$(dirname "$0")/../.." && pwd)
scratch=$(mktemp -d "${TMPDIR:-/tmp}/nami-orchestration-package.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

# A standalone consumer: no repository imports, project references or global package cache.
cp "$repo/eng/tools/NamiOrchestrationSmoke/Program.cs" "$scratch/Program.cs"
python3 - "$scratch" "$version" "$feed" <<'PY'
from pathlib import Path
import sys, xml.etree.ElementTree as ET
root, version, feed = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk.Web')
properties = ET.SubElement(project, 'PropertyGroup')
for name, value in [('TargetFramework', 'net10.0'), ('OutputType', 'Exe'),
                    ('ImplicitUsings', 'enable'), ('Nullable', 'enable')]:
    ET.SubElement(properties, name).text = value
items = ET.SubElement(project, 'ItemGroup')
# The copied program normally receives this global using from the repository SDK.
ET.SubElement(items, 'Using', Include='System.Diagnostics')
ET.SubElement(items, 'PackageReference', Include='Nami.Orchestration', Version=version)
ET.ElementTree(project).write(root / 'Smoke.csproj')
config = ET.Element('configuration')
sources = ET.SubElement(config, 'packageSources')
ET.SubElement(sources, 'clear')
if feed:
    ET.SubElement(sources, 'add', key='packed', value=feed)
ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
ET.ElementTree(config).write(root / 'NuGet.Config')
PY
NUGET_PACKAGES="$scratch/packages" dotnet run --project "$scratch/Smoke.csproj" --configuration Release \
  -p:RestoreConfigFile="$scratch/NuGet.Config" -p:RestoreNoCache=true -- --expected-version "$version"
