package after_resolution

import rego.v1

# Keys nami defines itself: its own `nami.*` keys.
nami_defined(attr) if startswith(attr.key, "nami.")

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
