# `hw` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `hw.battery.capacity` | `string` | development |  | Design capacity in Watts-hours or Ampere-hours | `"9.3Ah"`, `"50Wh"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.battery.chemistry` | `string` | development |  | Battery [chemistry](https://schemas.dmtf.org/wbem/cim-html/2.31.0/CIM_Battery.html), e.g. Lithium-Ion, Nickel-Cadmium, etc. | `"Li-ion"`, `"NiMH"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.battery.state` | `enum` | development |  | The current state of the battery |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.bios_version` | `string` | development |  | BIOS version of the hardware component | `"1.2.3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.driver_version` | `string` | development |  | Driver version for the hardware component | `"10.2.1-3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.enclosure.type` | `string` | development |  | Type of the enclosure (useful for modular systems) | `"Computer"`, `"Storage"`, `"Switch"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.firmware_version` | `string` | development |  | Firmware version of the hardware component | `"2.0.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.gpu.task` | `enum` | development |  | Type of task the GPU is performing |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.id` | `string` | development |  | An identifier for the hardware component, unique within the monitored host | `"win32battery_battery_testsysa33_1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.limit_type` | `enum` | development |  | Type of limit for hardware components |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.logical_disk.raid_level` | `string` | development |  | RAID Level of the logical disk | `"RAID0+1"`, `"RAID5"`, `"RAID10"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.logical_disk.state` | `enum` | development |  | State of the logical disk space usage |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.memory.type` | `string` | development |  | Type of the memory module | `"DDR4"`, `"DDR5"`, `"LPDDR5"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.model` | `string` | development |  | Descriptive model name of the hardware component | `"PERC H740P"`, `"Intel(R) Core(TM) i7-10700K"`, `"Dell XPS 15 Battery"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.name` | `string` | development |  | An easily-recognizable name for the hardware component | `"eth0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.network.logical_addresses` | `string[]` | development |  | Logical addresses of the adapter (e.g. IP address, or WWPN) | `["172.16.8.21","57.11.193.42"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.network.physical_address` | `string` | development |  | Physical address of the adapter (e.g. MAC address, or WWNN) | `"00-90-F5-E9-7B-36"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.parent` | `string` | development |  | Unique identifier of the parent component (typically the `hw.id` attribute of the enclosure, or disk controller) | `"dellStorage_perc_0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.physical_disk.smart_attribute` | `string` | development |  | [S.M.A.R.T.](https://wikipedia.org/wiki/S.M.A.R.T.) (Self-Monitoring, Analysis, and Reporting Technology) attribute of the physical disk | `"Spin Retry Count"`, `"Seek Error Rate"`, `"Raw Read Error Rate"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.physical_disk.state` | `enum` | development |  | State of the physical disk endurance utilization |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.physical_disk.type` | `string` | development |  | Type of the physical disk | `"HDD"`, `"SSD"`, `"10K"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.sensor_location` | `string` | development |  | Location of the sensor | `"cpu0"`, `"ps1"`, `"INLET"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.serial_number` | `string` | development |  | Serial number of the hardware component | `"CNFCP0123456789"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.state` | `enum` | development |  | The current state of the component |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.tape_drive.operation_type` | `enum` | development |  | Type of tape drive operation |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.type` | `enum` | development |  | Type of the component |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `hw.vendor` | `string` | development |  | Vendor name of the hardware component | `"Dell"`, `"HP"`, `"Intel"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `hw.battery.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `charging` | development |  | Charging |
| `discharging` | development |  | Discharging |

### `hw.gpu.task` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `decoder` | development |  | Decoder |
| `encoder` | development |  | Encoder |
| `general` | development |  | General |

### `hw.limit_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `critical` | development |  | Critical |
| `degraded` | development |  | Degraded |
| `high.critical` | development |  | High Critical |
| `high.degraded` | development |  | High Degraded |
| `low.critical` | development |  | Low Critical |
| `low.degraded` | development |  | Low Degraded |
| `max` | development |  | Maximum |
| `throttled` | development |  | Throttled |
| `turbo` | development |  | Turbo |

### `hw.logical_disk.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `free` | development |  | Free |
| `used` | development |  | Used |

### `hw.physical_disk.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `remaining` | development |  | Remaining |

### `hw.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `degraded` | development |  | Degraded |
| `failed` | development |  | Failed |
| `needs_cleaning` | development |  | Needs Cleaning |
| `ok` | development |  | OK |
| `predicted_failure` | development |  | Predicted Failure |

### `hw.tape_drive.operation_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `clean` | development |  | Clean |
| `mount` | development |  | Mount |
| `unmount` | development |  | Unmount |

### `hw.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `battery` | development |  | Battery |
| `cpu` | development |  | CPU |
| `disk_controller` | development |  | Disk controller |
| `enclosure` | development |  | Enclosure |
| `fan` | development |  | Fan |
| `gpu` | development |  | GPU |
| `logical_disk` | development |  | Logical disk |
| `memory` | development |  | Memory |
| `network` | development |  | Network |
| `physical_disk` | development |  | Physical disk |
| `power_supply` | development |  | Power supply |
| `tape_drive` | development |  | Tape drive |
| `temperature` | development |  | Temperature |
| `voltage` | development |  | Voltage |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `hw.battery.charge` | gauge | `1` | development |  | Remaining fraction of battery charge. |
| `hw.battery.charge.limit` | gauge | `1` | development |  | Lower limit of battery charge fraction to ensure proper operation. |
| `hw.battery.time_left` | gauge | `s` | development |  | Time left before battery is completely charged or discharged. |
| `hw.cpu.speed` | gauge | `Hz` | development |  | CPU current frequency. |
| `hw.cpu.speed.limit` | gauge | `Hz` | development |  | CPU maximum frequency. |
| `hw.energy` | counter | `J` | development |  | Energy consumed by the component. |
| `hw.errors` | counter | `{error}` | development |  | Number of errors encountered by the component. |
| `hw.fan.speed` | gauge | `rpm` | development |  | Fan speed in revolutions per minute. |
| `hw.fan.speed.limit` | gauge | `rpm` | development |  | Speed limit in rpm. |
| `hw.fan.speed_ratio` | gauge | `1` | development |  | Fan speed expressed as a fraction of its maximum speed. |
| `hw.gpu.io` | counter | `By` | development |  | Received and transmitted bytes by the GPU. |
| `hw.gpu.memory.limit` | updowncounter | `By` | development |  | Size of the GPU memory. |
| `hw.gpu.memory.usage` | updowncounter | `By` | development |  | GPU memory used. |
| `hw.gpu.memory.utilization` | gauge | `1` | development |  | Fraction of GPU memory used. |
| `hw.gpu.utilization` | gauge | `1` | development |  | Fraction of time spent in a specific task. |
| `hw.host.ambient_temperature` | gauge | `Cel` | development |  | Ambient (external) temperature of the physical host. |
| `hw.host.energy` | counter | `J` | development |  | Total energy consumed by the entire physical host, in joules. |
| `hw.host.heating_margin` | gauge | `Cel` | development |  | By how many degrees Celsius the temperature of the physical host can be increased, before reaching a warning threshold on one of the internal sensors. |
| `hw.host.power` | gauge | `W` | development |  | Instantaneous power consumed by the entire physical host in Watts (`hw.host.energy` is preferred). |
| `hw.logical_disk.limit` | updowncounter | `By` | development |  | Size of the logical disk. |
| `hw.logical_disk.usage` | updowncounter | `By` | development |  | Logical disk space usage. |
| `hw.logical_disk.utilization` | gauge | `1` | development |  | Logical disk space utilization as a fraction. |
| `hw.memory.size` | updowncounter | `By` | development |  | Size of the memory module. |
| `hw.network.bandwidth.limit` | updowncounter | `By/s` | development |  | Link speed. |
| `hw.network.bandwidth.utilization` | gauge | `1` | development |  | Utilization of the network bandwidth as a fraction. |
| `hw.network.io` | counter | `By` | development |  | Received and transmitted network traffic in bytes. |
| `hw.network.packets` | counter | `{packet}` | development |  | Received and transmitted network traffic in packets (or frames). |
| `hw.network.up` | updowncounter | `1` | development |  | Link status: `1` (up) or `0` (down). |
| `hw.physical_disk.endurance_utilization` | gauge | `1` | development |  | Endurance remaining for this SSD disk. |
| `hw.physical_disk.size` | updowncounter | `By` | development |  | Size of the disk. |
| `hw.physical_disk.smart` | gauge | `1` | development |  | Value of the corresponding [S.M.A.R.T.](https://wikipedia.org/wiki/S.M.A.R.T.) (Self-Monitoring, Analysis, and Reporting Technology) attribute. |
| `hw.power` | gauge | `W` | development |  | Instantaneous power consumed by the component. |
| `hw.power_supply.limit` | updowncounter | `W` | development |  | Maximum power output of the power supply. |
| `hw.power_supply.usage` | updowncounter | `W` | development |  | Current power output of the power supply. |
| `hw.power_supply.utilization` | gauge | `1` | development |  | Utilization of the power supply as a fraction of its maximum output. |
| `hw.status` | updowncounter | `1` | development |  | Operational status: `1` (true) or `0` (false) for each of the possible states. |
| `hw.tape_drive.operations` | counter | `{operation}` | development |  | Operations performed by the tape drive. |
| `hw.temperature` | gauge | `Cel` | development |  | Temperature in degrees Celsius. |
| `hw.temperature.limit` | gauge | `Cel` | development |  | Temperature limit in degrees Celsius. |
| `hw.voltage` | gauge | `V` | development |  | Voltage measured by the sensor. |
| `hw.voltage.limit` | gauge | `V` | development |  | Voltage limit in Volts. |
| `hw.voltage.nominal` | gauge | `V` | development |  | Nominal (expected) voltage. |
