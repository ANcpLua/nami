# Rust and Markdown integration

`registry/generate.sh` generates `crates/nami-semconv/src/lib.rs` and one reference page per
namespace in `docs/semconv`. `--check` regenerates all outputs in a temporary directory and
compares bytes, including the embedded C# inventory. The generated crate is `no_std` and
has no dependencies. No tomo source is changed by this workstream.
