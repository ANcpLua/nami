# Registry view shared by nami's rust and markdown targets (weaver `--v2` context).
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

def nami_keywords:
  ["abstract", "as", "async", "await", "become", "box", "break", "const", "continue",
   "crate", "do", "dyn", "else", "enum", "extern", "false", "final", "fn", "for", "gen",
   "if", "impl", "in", "let", "loop", "macro", "match", "mod", "move", "mut",
   "override", "priv", "pub", "ref", "return", "self", "static", "struct", "super",
   "trait", "true", "try", "type", "typeof", "unsafe", "unsized", "use", "virtual",
   "where", "while", "yield"];

def nami_height($graph; $url):
  if (($graph[$url] // []) | length) == 0 then 0
  else ([$graph[$url][] as $dep | nami_height($graph; $dep)] | max) + 1
  end;

# Schema urls in precedence order: this registry, then its dependencies.
def nami_sources:
  . as $root
  | ($root.dependency_graph // {}) as $graph
  | [$root.schema_url]
    + ([($root.dependencies // {}) | keys | .[]
        | {url: ., height: nami_height($graph; .)}]
       | sort_by(-.height, .url)
       | map(.url));

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

def nami_rust_string: gsub("\\\\"; "\\\\") | gsub("\""; "\\\"");

def nami_md_cell: nami_text | gsub("\\|"; "\\|");

def nami_root: split(".") | .[0];

def nami_rest: . as $id | split(".") | if length > 1 then .[1:] | join(".") else $id end;

def nami_const_ident:
  ascii_upcase | gsub("[^A-Z0-9_]"; "_") | if test("^[0-9]") then "_" + . else . end;

def nami_mod_ident:
  ascii_downcase | gsub("[^a-z0-9_]"; "_")
  | if test("^[0-9]") then "_" + .
    elif . == "self" or . == "super" or . == "crate" or . == "Self" then . + "_"
    elif (. as $m | any(nami_keywords[]; . == $m)) then "r#" + .
    else . end;

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
  | ($v | type) as $t
  | {id: .id,
     const: (.id | nami_const_ident),
     value: $v,
     rust_type: (if $t == "string" then "&str"
                 elif $t == "boolean" then "bool"
                 elif $t == "number" and ($v == ($v | floor)) then "i64"
                 else "f64" end),
     rust_value: (if $t == "string" then "\"" + ($v | nami_rust_string) + "\"" else ($v | tostring) end),
     brief: (((.brief // "") | nami_text) as $b | if $b == "" then "`\($v | tostring)`." else $b end),
     stability: (.stability // $attribute_stability),
     deprecated: nami_deprecation,
     codegen: (nami_excluded_from_codegen | not)};

def nami_attribute_view:
  .stability as $st
  | {key: .key,
     namespace: (.key | nami_root),
     const: (.key | nami_rest | nami_const_ident),
     module: ((.key | nami_rest | ascii_downcase | gsub("[^a-z0-9_]"; "_")) + "_values"
              | if test("^[0-9]") then "_" + . else . end),
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
   const: (.name | nami_rest | nami_const_ident),
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

# [{name, module, attributes, metrics}] sorted by namespace.
def nami_namespaces($view):
  ([$view.attributes[] | .namespace] + [$view.metrics[] | .namespace]) | unique
  | map(. as $ns
        | {name: $ns,
           module: ($ns | nami_mod_ident),
           attributes: [$view.attributes[] | select(.namespace == $ns)],
           metrics: [$view.metrics[] | select(.namespace == $ns)]});

def nami_require_unique($what; $names):
  ($names | group_by(.) | map(select(length > 1) | .[0])) as $dups
  | if ($dups | length) > 0 then error("nami: duplicate \($what): \($dups | join(", "))") else . end;

def nami_doc_lines($indent; $lines):
  $lines | map(if . == "" then $indent + "///" else $indent + "/// " + . end);

def nami_deprecated_attr($indent):
  if .deprecated == null then []
  else [$indent + "#[deprecated(note = \"" + (.deprecated.note | nami_rust_string) + "\")]"] end;

def nami_deprecated_doc:
  if .deprecated == null then [] else ["", "Deprecated: \(.deprecated.note)"] end;

def nami_rust_attribute:
  (nami_doc_lines("    "; [.brief, "",
     "Stability: \(.stability). Type: `\(.type)`."
     + (if .is_template then " Template: the key is a prefix; append `.<name>`." else "" end)]
     + nami_deprecated_doc)
   + nami_deprecated_attr("    ")
   + ["    pub const \(.const): &str = \"\(.key | nami_rust_string)\";"])
  + (if (.members | length) == 0 then []
     else
       [""]
       + nami_doc_lines("    "; ["Values of `\(.key)` (`\(.const)`).", "", "Stability: \(.stability)."])
       + nami_deprecated_attr("    ")
       + ["    pub mod \(.module) {"]
       + ([.members[]
           | [""]
             + nami_doc_lines("        "; [.brief, "", "Stability: \(.stability)."] + nami_deprecated_doc)
             + nami_deprecated_attr("        ")
             + ["        pub const \(.const): \(.rust_type) = \(.rust_value);"]]
          | add // [] | .[1:])
       + ["    }"]
     end);

def nami_rust_metric:
  nami_doc_lines("        "; [.brief, "",
    "Stability: \(.stability). Instrument: \(.instrument). Unit: `\(.unit)`."] + nami_deprecated_doc)
  + nami_deprecated_attr("        ")
  + ["        pub const \(.const): Metric = Metric {",
     "            name: \"\(.name | nami_rust_string)\",",
     "            unit: \"\(.unit | nami_rust_string)\",",
     "            instrument: Instrument::\(.instrument | nami_instrument_variant),",
     "            stability: Stability::\(.stability | nami_stability_variant),",
     "        };"];

# Context of templates/registry/rust/lib.rs.j2. Every line is computed here so the
# template only joins lines; blank lines between items are explicit.
def nami_rust:
  nami_view as $view
  | {schema_url: $view.schema_url,
     sources: $view.sources,
     namespaces: [nami_namespaces($view)[]
       | .attributes |= map(select(.codegen) | .members |= map(select(.codegen)))
       | .metrics |= map(select(.codegen))
       | nami_require_unique("constants in `\(.name)`"; [.attributes[] | .const])
       | nami_require_unique("modules in `\(.name)`";
           [.attributes[] | select((.members | length) > 0) | .module] + ["metrics"])
       | nami_require_unique("metric constants in `\(.name)`"; [.metrics[] | .const])
       | {name: .name,
          module: .module,
          lines: ([.attributes[]
                   | nami_require_unique("value constants of `\(.key)`"; [.members[] | .const])
                   | [""] + nami_rust_attribute]
                  + (if (.metrics | length) == 0 then []
                     else [[""]
                           + nami_doc_lines("    "; ["Metrics of the `\(.name)` namespace."])
                           + ["    pub mod metrics {",
                              "        use crate::{Instrument, Metric, Stability};"]
                           + ([.metrics[] | [""] + nami_rust_metric] | add)
                           + ["    }"]]
                     end)
                  | add // [] | .[1:])}],
     keys: [$view.attributes[] | "    \"\(.key | nami_rust_string)\","]};

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
