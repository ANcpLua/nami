package after_resolution

import rego.v1

# Keys nami defines itself: its own `nami.*` keys and the keys of a producer it describes,
# such as Claude Code's (annotation `nami.producer`).
nami_defined(attr) if startswith(attr.key, "nami.")

nami_defined(attr) if object.get(object.get(attr, "annotations", {}), "nami.producer", "") != ""

deny contains finding if {
    some attr in input.registry.attributes
    nami_defined(attr)
    object.get(attr, "brief", "") == ""
    finding := {"id": "nami_brief_required", "level": "violation", "message": sprintf("Nami attribute %s requires a brief", [attr.key])}
}

deny contains finding if {
    some attr in input.registry.attributes
    nami_defined(attr)
    object.get(attr, "stability", "") == ""
    finding := {"id": "nami_stability_required", "level": "violation", "message": sprintf("Nami attribute %s requires stability", [attr.key])}
}

deny contains finding if {
    some attr in input.registry.attributes
    nami_defined(attr)
    not is_boolean(object.get(object.get(attr, "annotations", {}), "nami.persisted", null))
    finding := {"id": "nami_persistence_required", "level": "violation", "message": sprintf("Nami attribute %s requires a persistence annotation", [attr.key])}
}

deny contains finding if {
    some attr in input.registry.attributes
    annotations := object.get(attr, "annotations", {})
    object.get(annotations, "nami.token_exempt", false) == true
    object.get(annotations, "nami.persisted", true) == false
    finding := {"id": "nami_token_exemption_requires_persistence", "level": "violation", "message": sprintf("Attribute %s cannot be token-exempt when it is not persisted", [attr.key])}
}

# The attributes of every signal, with the annotations of the signal's own reference.
signal_attribute contains [kind, name, attr] if {
    some kind in ["events", "metrics"]
    some signal in object.get(input.registry, kind, [])
    some attr in object.get(signal, "attributes", [])
    name := signal.name
}

signal_attribute contains ["spans", signal.type, attr] if {
    some signal in object.get(input.registry, "spans", [])
    some attr in object.get(signal, "attributes", [])
}

wire_types := {"string", "int", "double", "boolean"}

# `nami.wire_type` names the primitive OTLP type a producer sends where it differs from the
# type nami stores, which ingest converts from.
deny contains finding if {
    some [kind, name, attr] in signal_attribute
    wire := object.get(object.get(attr, "annotations", {}), "nami.wire_type", null)
    wire != null
    not wire in wire_types
    finding := {"id": "nami_wire_type_invalid", "level": "violation", "message": sprintf("%s %s: wire type %v of %s is not one of %v", [kind, name, wire, attr.key, wire_types])}
}

deny contains finding if {
    some [kind, name, attr] in signal_attribute
    object.get(object.get(attr, "annotations", {}), "nami.wire_type", null) == attr.type
    finding := {"id": "nami_wire_type_redundant", "level": "violation", "message": sprintf("%s %s: %s already has type %v; drop its wire type", [kind, name, attr.key, attr.type])}
}

# `nami.wire_container` names the JSON string attribute a key arrives in. The container itself
# is never persisted: ingest lifts the registered members out of it and drops the rest.
deny contains finding if {
    some [kind, name, attr] in signal_attribute
    container := object.get(object.get(attr, "annotations", {}), "nami.wire_container", null)
    container != null
    not unpersisted_string_attribute(container)
    finding := {"id": "nami_wire_container_invalid", "level": "violation", "message": sprintf("%s %s: container %v of %s must be a defined, unpersisted string attribute", [kind, name, container, attr.key])}
}

unpersisted_string_attribute(key) if {
    some attr in input.registry.attributes
    attr.key == key
    attr.type == "string"
    object.get(object.get(attr, "annotations", {}), "nami.persisted", true) == false
}

# A producer's signal names the instrumentation scope it arrives under, so ingest can tell the
# producer's keys from the same names sent by others.
deny contains finding if {
    some kind in ["events", "metrics", "spans"]
    some signal in object.get(input.registry, kind, [])
    annotations := object.get(signal, "annotations", {})
    object.get(annotations, "nami.producer", "") != ""
    object.get(annotations, "nami.instrumentation_scope", "") == ""
    finding := {"id": "nami_producer_scope_required", "level": "violation", "message": sprintf("%s %v of producer %v requires nami.instrumentation_scope", [kind, object.get(signal, "name", object.get(signal, "type", "")), annotations["nami.producer"]])}
}

# `nami.max_utf8_bytes` bounds, in UTF-8 bytes, the value ingest keeps of a persisted string key.
deny contains finding if {
    some attr in input.registry.attributes
    annotations := object.get(attr, "annotations", {})
    bound := object.get(annotations, "nami.max_utf8_bytes", null)
    bound != null
    not valid_bound(attr, bound)
    finding := {"id": "nami_max_utf8_bytes_invalid", "level": "violation", "message": sprintf("Attribute %s: nami.max_utf8_bytes %v must be a positive integer on a persisted string attribute", [attr.key, bound])}
}

valid_bound(attr, bound) if {
    is_number(bound)
    bound == floor(bound)
    bound > 0
    attr.type == "string"
    object.get(object.get(attr, "annotations", {}), "nami.persisted", true) == true
}
