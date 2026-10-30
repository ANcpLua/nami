# Registry views for Nami's C# and Markdown targets (Weaver `--v2` context).
#
# The view is the union of this registry and every registry it depends on, directly
# or indirectly (`dependencies` in the v2 context), with exactly one definition per
# id. A definition belongs to the registry named by its `provenance.source` (or to
# the registry it is listed in when that is empty). Precedence per id: this registry
# first, then the dependencies from most derived to least derived (height in
# `dependency_graph`), ties broken by schema url; so with nami -> genai -> core a
# key that genai and core both define comes from genai. A dependency's definition
# annotated `dependency_resolution: {exclude: true}` is invisible to dependents and
# skipped. Everything is sorted explicitly, so the same registry gives the same bytes.

def nami_height($graph; $url):
  if (($graph[$url] // []) | length) == 0 then 0
  else ([$graph[$url][] as $dep | nami_height($graph; $dep)] | max) + 1
  end;

# Schema urls in precedence order: this registry, then its dependencies.
def nami_sources:
  . as $root
  | ($root.dependency_graph // {}) as $graph
  | [$root.schema_url]
    + ([$root.registry.attributes[]? | .annotations."nami.source_schema" // empty] | unique)
    + ([($root.dependencies // {}) | keys | .[]
        | {url: ., height: nami_height($graph; .)}]
       | sort_by(-.height, .url)
       | map(.url)) | unique;

def nami_excluded_from_resolution:
  (.annotations.dependency_resolution.exclude // false) == true;

def nami_excluded_from_codegen:
  (.annotations.code_generation.exclude // false) == true;

# One definition per id, sorted by id; each carries `nami_source`, the schema url
# of the registry that defined it.
def nami_merge($field; idf):
  . as $root
  | ($root.dependency_graph // {}) as $graph
  | [({url: $root.schema_url, registry: $root.registry},
      (($root.dependencies // {}) | to_entries | .[] | {url: .key, registry: .value.registry}))
     as $s
     | ($s.registry[$field] // []) | .[]
     | (.provenance.source // $s.url) as $src
     | select($src == $root.schema_url or (nami_excluded_from_resolution | not))
     | . + {nami_source: $src,
            nami_rank: [(if $src == $root.schema_url then 0 else 1 end), -nami_height($graph; $src), $src]}]
  | sort_by(idf, .nami_rank)
  | reduce .[] as $d ({seen: {}, out: []};
      ($d | idf) as $id
      | if .seen[$id] then . else (.seen[$id] = true) | (.out += [$d | del(.nami_rank)]) end)
  | .out;

def nami_text: tostring | gsub("\\s+"; " ") | ltrimstr(" ") | rtrimstr(" ");

def nami_md_cell: nami_text | gsub("\\|"; "\\|");

def nami_root: split(".") | .[0];

def nami_rest: . as $id | split(".") | if length > 1 then .[1:] | join(".") else $id end;

def nami_stability_variant:
  . as $s
  | {"stable": "Stable", "release_candidate": "ReleaseCandidate", "beta": "Beta",
     "alpha": "Alpha", "development": "Development"}[$s]
    // error("nami: unknown stability \($s | tojson)");

def nami_instrument_variant:
  . as $i
  | {"counter": "Counter", "updowncounter": "UpDownCounter", "gauge": "Gauge",
     "histogram": "Histogram"}[$i]
    // error("nami: unknown instrument \($i | tojson)");

# null, or {reason, renamed_to, note} with a one-line note.
def nami_deprecation:
  if .deprecated == null then null
  else
    .deprecated as $d
    | (($d.note // "") | nami_text) as $note
    | {reason: $d.reason,
       renamed_to: ($d.renamed_to // null),
       note: (if $note != "" then $note
              elif $d.renamed_to then "Replaced by `\($d.renamed_to)`."
              else "Deprecated." end)}
  end;

def nami_member($attribute_stability):
  (if .value == null then .id else .value end) as $v
  | {id: .id,
     value: $v,
     brief: (((.brief // "") | nami_text) as $b | if $b == "" then "`\($v | tostring)`." else $b end),
     stability: (.stability // $attribute_stability),
     deprecated: nami_deprecation,
     codegen: (nami_excluded_from_codegen | not)};

def nami_attribute_view:
  .stability as $st
  | {key: .key,
     namespace: (.key | nami_root),
     type: (if (.type | type) == "object" then "enum" else .type end),
     is_template: ((.type | type) == "string" and (.type | startswith("template["))),
     brief: (.brief | nami_text),
     stability: .stability,
     deprecated: nami_deprecation,
     codegen: (nami_excluded_from_codegen | not),
     examples: ([if .examples == null then empty
                 elif (.examples | type) == "array" then .examples[]
                 else .examples end]
                | .[0:3]
                | map("`" + (tojson | gsub("`"; "'")) + "`") | join(", ")),
     annotations: ((.annotations // {}) | to_entries
                   | map(select(.key | startswith("nami.")))
                   | sort_by(.key)
                   | map("`\(.key)`: `\(.value | tojson)`") | join(", ")),
     members: (if (.type | type) == "object"
               then [.type.members[] | nami_member($st)] | sort_by(.id)
               else [] end),
     source: .nami_source};

def nami_metric_view:
  {name: .name,
   namespace: (.name | nami_root),
   instrument: .instrument,
   unit: (.unit // ""),
   brief: (.brief | nami_text),
   stability: .stability,
   deprecated: nami_deprecation,
   codegen: (nami_excluded_from_codegen | not),
   source: .nami_source};

# {schema_url, sources, attributes, metrics}: the merged, sorted catalog.
def nami_view:
  {schema_url: .schema_url,
   sources: nami_sources,
   attributes: [nami_merge("attributes"; .key)[] | nami_attribute_view],
   metrics: [nami_merge("metrics"; .name)[] | nami_metric_view]};

# [{name, attributes, metrics}] sorted by namespace.
def nami_namespaces($view):
  ([$view.attributes[] | .namespace] + [$view.metrics[] | .namespace]) | unique
  | map(. as $ns
        | {name: $ns,
           attributes: [$view.attributes[] | select(.namespace == $ns)],
           metrics: [$view.metrics[] | select(.namespace == $ns)]});

def nami_md_attribute_row($with_annotations):
  "| `\(.key)` | `\(.type)` | \(.stability) | "
  + (if .deprecated then (.deprecated.note | nami_md_cell) else "" end)
  + " | \(.brief | nami_md_cell) | \(.examples | nami_md_cell) |"
  + (if $with_annotations then " \(.annotations | nami_md_cell) |" else "" end);

# Context of templates/registry/markdown/namespace.md.j2 (one page per namespace).
def nami_markdown_pages:
  nami_view as $view
  | [nami_namespaces($view)[]
     | ([.attributes[] | select(.annotations != "")] | length > 0) as $ann
     | {name: .name,
        schema_url: $view.schema_url,
        sources: $view.sources,
        attribute_header: (if $ann
          then ["| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |",
                "| --- | --- | --- | --- | --- | --- | --- |"]
          else ["| Key | Type | Stability | Deprecated | Brief | Examples |",
                "| --- | --- | --- | --- | --- | --- |"] end),
        attribute_rows: [.attributes[] | nami_md_attribute_row($ann)],
        enums: [.attributes[] | select((.members | length) > 0)
          | {key: .key,
             rows: [.members[]
               | "| `\(.value | tostring | nami_md_cell)` | \(.stability) | "
                 + (if .deprecated then (.deprecated.note | nami_md_cell) else "" end)
                 + " | \(.brief | nami_md_cell) |"]}],
        metric_rows: [.metrics[]
          | "| `\(.name)` | \(.instrument) | `\(.unit)` | \(.stability) | "
            + (if .deprecated then (.deprecated.note | nami_md_cell) else "" end)
            + " | \(.brief | nami_md_cell) |"]}];

# Context of templates/registry/markdown/README.md.j2 (the index page).
def nami_markdown_index:
  nami_view as $view
  | {schema_url: $view.schema_url,
     sources: $view.sources,
     totals: {attributes: ($view.attributes | length),
              stable_attributes: ([$view.attributes[] | select(.stability == "stable" and .deprecated == null)] | length),
              metrics: ($view.metrics | length),
              stable_metrics: ([$view.metrics[] | select(.stability == "stable" and .deprecated == null)] | length)},
     rows: [nami_namespaces($view)[]
       | "| [`\(.name)`](\(.name).md) | \(.attributes | length) | "
         + "\([.attributes[] | select(.stability == "stable" and .deprecated == null)] | length) | "
         + "\(.metrics | length) | "
         + "\([.metrics[] | select(.stability == "stable" and .deprecated == null)] | length) |"]};


def nami_pascal_name: gsub("[^A-Za-z0-9]+"; "_") | split("_") | map(. as $p | ($p[0:1] | ascii_upcase) + $p[1:]) | join("") | if test("^[0-9]") then "_" + . else . end;
def nami_cs_stability: if .deprecated != null then "Deprecated" else (.stability // "development" | nami_stability_variant) end;
def nami_cs_attr:
  . as $attribute
  | . + {identifier: (.key | nami_rest | nami_pascal_name),
       json: (.key | tojson),
       cs_stability: nami_cs_stability,
       persisted: ((if .annotations | has("nami.persisted") then .annotations."nami.persisted" else true end) and (.deprecated == null)),
       token_exempt: (.annotations."nami.token_exempt" // false),
       successor: (.deprecated.renamed_to // null | tojson),
       source_json: (.annotations."nami.source_schema" // .nami_source // "" | tojson),
       type_json: (if (.type|type) == "object" then "enum" else .type end | tojson),
       values: [if (.type|type) == "object" then .type.members[] else empty end
                | select(.deprecated == null)
                | (if .value == null then .id else .value end) as $value
                | {name:(.id | nami_pascal_name), json:($value|tojson), type:(if ($value|type)=="string" then "string" elif ($value|type)=="boolean" then "bool" else "double" end)}],
       enum_values: [if (.type|type)=="object" then .type.members[] else empty end
                     | {json:((if .value == null then .id else .value end)|tostring|tojson),stability:(if .deprecated != null or $attribute.deprecated != null then "Deprecated" else (.stability // $attribute.stability | nami_stability_variant) end)}]};
def nami_cs:
  . as $root
  | [nami_merge("attributes"; .key)[] | nami_cs_attr] as $attrs
  | {schema_url:$root.schema_url,attributes:$attrs,
     namespaces:([$attrs[].key | nami_root]|unique|map(. as $ns | {name:($ns|nami_pascal_name), attributes:[$attrs[]|select((.key|nami_root)==$ns)|select(nami_excluded_from_codegen|not)]})),
     metrics:[nami_merge("metrics"; .name)[] | select(nami_excluded_from_codegen|not) | {name:(.name|nami_pascal_name),json:(.name|tojson),unit:(.unit//""|tojson),instrument:(.instrument|nami_instrument_variant),stability:nami_cs_stability}],
     spans:[(([($root.refinements.spans // [])[] | . + {shape_id:.id}] + [nami_merge("spans"; .type)[] | . + {shape_id:.type}]) | unique_by(.shape_id))[] | select(nami_excluded_from_codegen|not) | {name:(.shape_id|nami_pascal_name),id_json:(.shape_id|tojson),json:(.type|tojson),stability:nami_cs_stability,kind:(.kind|nami_pascal_name),required:[.attributes[]|select(.requirement_level=="required")|.key|tojson]}]};
