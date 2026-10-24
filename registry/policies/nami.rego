package after_resolution

import rego.v1

deny contains finding if {
    some attr in input.registry.attributes
    startswith(attr.key, "nami.")
    object.get(attr, "brief", "") == ""
    finding := {"id": "nami_brief_required", "level": "violation", "message": sprintf("Nami attribute %s requires a brief", [attr.key])}
}

deny contains finding if {
    some attr in input.registry.attributes
    startswith(attr.key, "nami.")
    object.get(attr, "stability", "") == ""
    finding := {"id": "nami_stability_required", "level": "violation", "message": sprintf("Nami attribute %s requires stability", [attr.key])}
}

deny contains finding if {
    some attr in input.registry.attributes
    startswith(attr.key, "nami.")
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
