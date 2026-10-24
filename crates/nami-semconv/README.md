# nami-semconv

The generated, dependency-free `no_std` Rust constants crate for producers such as tomo.

From the repository root:

```sh
registry/generate.sh
registry/generate.sh --check
cargo build --manifest-path crates/nami-semconv/Cargo.toml --locked
```

Attribute keys are exposed by namespace (for example `http::REQUEST_METHOD`), enum values by
nested `*_values` modules, and metrics by `namespace::metrics`. Every generated definition
records its stability; deprecated definitions carry a Rust deprecation attribute.

This crate is generated from the same validated registry as Nami's C# library and Markdown
reference. It does not instrument or modify a consumer by itself.
