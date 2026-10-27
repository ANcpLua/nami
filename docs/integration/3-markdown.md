# Markdown integration

`registry/generate.sh` generates one reference page per namespace in `docs/semconv`
alongside the C# semantic-convention catalog. `--check` regenerates both outputs in
a temporary directory and compares bytes, including the embedded C# inventory.

Nami's registry generation and build require no Rust toolchain or Rust consumer.
