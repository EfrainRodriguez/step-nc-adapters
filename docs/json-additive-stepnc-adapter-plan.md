# JSON Additive STEP-NC Adapter: Mapping and Implementation Plan

## Goal

Create a third adapter that converts the Slic3r-derived JSON structure into a STEP-NC AP238 program using the same machining-oriented STEP-NC Machine DLL already used by the existing adapters.

The JSON represents FFF/FDM additive manufacturing: sliced model layers, deposition paths, non-deposition travels, and process parameters. Because the DLL mainly exposes machining concepts, the adapter must encode 3D printing as machining toolpaths, following the same approach used by the current additive XML adapter.

## Current Project State

The project already has these flows:

- `AdditiveXmlParser.cs`: parses additive XML into `AdditiveLayer` and `AdditivePolyline`.
- `AdditiveStepNcGenerator.cs`: generates STEP-NC from additive layers.
- `AptParser.cs`: parses Mastercam-style APT.
- `AptStepNcGenerator.cs`: generates STEP-NC from APT.
- `StepNcApi.cs`: public facade for parsing/converting additive XML and APT.
- `Program.cs`: CLI with `additive`, `parse-additive`, `apt`, and `parse-apt` commands.

The current additive XML pattern is:

- `NewProjectWithCCandWP(...)` creates the project and main workplan.
- `Millimeters()` sets units.
- `DefineTool(...)` and `LoadTool(1)` represent the extruder/nozzle as a machining tool.
- `NestWorkplan("Additive Layer-{n}")` creates one nested workplan per layer.
- `Workingstep(...)` creates one workingstep per polyline.
- The first point of a polyline is emitted as `Rapid()` + `GoToXYZ(...)`.
- Remaining points are emitted as `GoToXYZ(...)` with `Feedrate(...)` and `SpindleSpeed(...)`.
- `SaveAsP21(...)` is used by default.

## Observed Structure in `samples/additive/cube_new.json`

Top-level fields:

- `schema_version`: schema version, `1.0.0` in the sample.
- `generator`: generator metadata, Slic3r `1.3.1-dev` in the sample.
- `print_config`: global print configuration.
- `print_level_toolpaths`: print-level paths such as `skirt` and `brim`.
- `objects`: object/copy/raft metadata.
- `layers`: primary source of layer toolpaths.
- `summary`: global counts and material usage.

Sample summary:

- 110 layers.
- 1966 events in `layers[].events`.
- 1048 `extrusion_path` events.
- 918 `travel` events.
- 21135 points in events.
- 3 `is_raft_layer` layers.
- 44 `is_support_layer` layers.
- `print_level_toolpaths.skirt` contains 4 paths.
- `print_level_toolpaths.brim` is empty in the sample.

Observed `events` feature types:

- `travel`: non-deposition movement.
- `infill`: infill.
- `perimeter_internal`: internal perimeter.
- `perimeter_external`: external perimeter.
- `support`: support material.
- `support_interface`: support interface.
- `gap_fill`: gap fill.
- `infill_solid`: solid infill.
- `infill_top_solid`: top solid infill.
- `bridge`: bridge.

Relevant event fields:

- `type`: `extrusion_path` or `travel`.
- `feature_type`: functional classification.
- `role_name` and `role_id`: Slic3r role classification.
- `source`: logical source.
- `points`: list of `[x, y]` points.
- `layer_print_z`: absolute print Z.
- `height`: layer/path height.
- `width`: extrusion width.
- `mm3_per_mm`: extruded volume per millimeter of path.
- `is_bridge`: bridge flag.
- `is_solid_infill`: solid infill flag.
- `region_id`: associated region, when present.

Relevant layer fields:

- `layer_id`, `layer_seq_id`: layer identifiers.
- `print_z`: absolute layer Z.
- `height`: layer height.
- `copy_index`, `copy_offset`: copy and XY offset.
- `object_index`: associated object.
- `is_raft_layer`, `is_support_layer`: layer classification.
- `process`: layer-specific process speeds.
- `regions`: region configuration and classified paths.
- `support`: classified support paths.
- `events`: consolidated print sequence.

## Primary Toolpath Source

Use `layers[].events` as the primary source for STEP-NC generation.

Reasons:

- It preserves the actual execution order, mixing travels and extrusions.
- It contains both `travel` and `extrusion_path` events.
- It includes the needed classification metadata (`feature_type`, `role_name`, `source`).
- It avoids duplicating paths that also appear under `regions` or `support`.

Use `regions`, `support`, and `print_level_toolpaths` as auxiliary sources:

- `regions[].config`: resolve speeds and parameters when an event does not carry direct values.
- `support`: diagnostics or validation of support paths, not primary generation if the event already exists.
- `print_level_toolpaths.skirt` and `print_level_toolpaths.brim`: generate before layers if they are not included in `events`.

## JSON to STEP-NC Machine DLL Mapping

| JSON/Slic3r | STEP-NC/DLL | Decision |
| --- | --- | --- |
| Complete program | `NewProjectWithCCandWP(project, 1, mainWorkplan)` | Create AP238 with conformance class 1 because the output is primarily toolpath data. |
| XY/Z units | `Millimeters()` | Slic3r uses millimeters. |
| Extruder/nozzle | `DefineTool(...)`, `LoadTool(1)` | Represent the nozzle as a machining tool. Diameter = `print_config.nozzle_diameter`; fallback `0.5`. |
| Layer | `NestWorkplan("Layer-{seq}-Z{print_z}")` | Sequential workplan per layer. Close with `EndWorkplan()`. |
| Extrusion event | `Workingstep(...)` + `GoToXYZ(...)` | One workingstep per extrusion event, or per compatible consecutive group. Recommended initial behavior: one per event. |
| `travel` event | `Rapid()` + `GoToXYZ(...)` | `Rapid()` is modal until `Feedrate(...)`; restore feed before the next extrusion. |
| Point `[x,y]` | `GoToXYZ(label, x, y, z)` | Z comes from `event.layer_print_z` or `layer.print_z`. |
| `copy_offset` | Add to X/Y only if points are not already in global coordinates | In the sample, points already appear around 90-111 with offset 90; validate before applying to avoid double offset. Recommended default: do not apply. |
| `feature_type` / `role_name` | `Workingstep(...)`, geometry label, CAM properties | Use descriptive names, for example `L005 perimeter_external #123`. |
| Perimeters, infill, support, raft | `SetPathTypeTrajectory()` or `SetPathTypeContact()` | The DLL has no additive-specific path types. Use `Trajectory` for deposition; optionally `Contact` if contact with the part should be indicated. |
| Travel | `SetPathTypeConnect()` or `SetPathTypeNonContact()` | If applied before the path, classify travels as connection/non-contact paths. |
| Required paths | `SetPathPriorityRequired()` | Optional for deposition paths. |
| Feedrate | `Feedrate(mm/min)` with `FeedrateUnit("mmpm")` | Resolve from event/layer/config. Restore after each travel. |
| Extrusion | `SpindleSpeed(...)` with `SpindleSpeedUnit("rpm")` | Map extrusion intensity to spindle speed so it appears in the STEP-NC program. See spindle section. |
| Extruder/bed temperature | `WorkingstepAddPropertyDescriptiveMeasure(...)` and/or `PPrint(...)` | There is no temperature function in `AptStepMaker`. Store as property/comment, not as coolant/spindle. |
| `mm3_per_mm`, `width`, `height` | Workingstep CAM properties | Use `WorkingstepAddPropertyLengthMeasure` for `width`/`height`; descriptive property for `mm3_per_mm` because there is no direct volume-per-length unit. |
| Skirt/brim/raft/support | Workplans/workingsteps with specific names | Preserve categories: `skirt`, `brim`, `raft`, `support`, `support_interface`. |
| Toolpath color | Not directly supported by `AptStepMaker` according to `docs/dll.html` | Store `feature_type`/`role_name` as name or property. External visualization can color by this metadata. |

## Feedrate Mapping

Recommended priority for resolving feedrate in mm/min:

- For `travel`: `print_config.travel_speed`.
- For `perimeter_external`: `region.config.external_perimeter_speed`; if it is a percentage, resolve against `perimeter_speed` or `max_print_speed`.
- For `perimeter_internal`: `process.print_speed_perimeter`, then `region.config.perimeter_speed`.
- For `infill`: `process.print_speed_infill`, then `region.config.infill_speed`.
- For `infill_solid`: `process.print_speed_solid`, then `region.config.solid_infill_speed`.
- For `infill_top_solid`: `process.print_speed_top_solid`, then `region.config.top_solid_infill_speed`.
- For `support`: `process.support_speed`, then `print_config.support_speed` if present.
- For `support_interface`: `process.support_interface_speed`, then `process.support_speed`.
- For `bridge`: `region.config.bridge_speed`.
- For first layer: if `print_config.first_layer_speed` applies, resolve the percentage against the base speed.
- Fallback: `print_config.max_print_speed`, then `AdditiveJsonConversionOptions.DefaultFeedrate`.

Parsing rules:

- Numeric strings such as `"60"` are interpreted as mm/s in Slic3r and converted to mm/min by multiplying by 60, assuming the JSON preserves Slic3r's standard units.
- JSON numbers are treated the same as numeric strings.
- Percentages such as `"50%"` and `"100%"` are resolved against a base speed from the same feature family.
- `null` means fallback resolution is required.

## Extrusion to Spindle Speed Mapping

The requested behavior is to associate material extrusion with spindle speed so it appears in STEP-NC. Since `SpindleSpeed(...)` represents RPM or Hz rather than material flow, this mapping must be explicitly conventional.

Initial recommendation:

- Calculate volumetric flow per minute: `flow_mm3_per_min = mm3_per_mm * feedrate_mm_per_min`.
- Call `SpindleSpeed(flow_mm3_per_min)` with `SpindleSpeedUnit("rpm")`.
- Also store a descriptive property: `extrusion_mapping = "spindle_rpm_represents_mm3_per_min"`.

Advantages:

- It changes with both speed and extruded amount.
- It produces visible and useful magnitudes.
- It allows comparison between paths with different width/height/flow.

Fallbacks:

- If `mm3_per_mm` is missing, use `width * height` as an approximate extrusion cross-section and multiply by feedrate.
- If `width`/`height` are also missing, use `DefaultSpindleSpeed`.
- For `travel`, spindle should be `0` or left unchanged. Recommended: call `SpindleSpeed(0)` before travel only if the STEP-NC program must explicitly show no deposition. If it creates too many toolpaths, omit it and rely on `Rapid()` + `feature_type=travel`.

## Temperature and Other Process Parameters

Do not map temperature to coolant, spindle, or feedrate because that distorts machining semantics and can confuse simulation.

Recommended mapping:

- `print_config.temperature`: global descriptive property and/or per-workingstep property.
- `print_config.first_layer_temperature`: property on initial layers.
- `print_config.bed_temperature` and `first_layer_bed_temperature`: project-level descriptive property or initial `PPrint` comment.
- `fan_percentage`, `min_fan_speed`, `max_fan_speed`, `cooling`: descriptive property or `PPrint` comment.
- `filament_diameter`, `filament_density`, `filament_colour`: project-level descriptive property or first workingstep property.

If the wrapper allows access to `ws_id`:

- Use `WorkingstepAddPropertyDescriptiveMeasure(wsId, "extruder_temperature_c", value)`.
- Use `WorkingstepAddPropertyDescriptiveMeasure(wsId, "bed_temperature_c", value)`.
- Use `WorkingstepAddPropertyDescriptiveMeasure(wsId, "feature_type", event.FeatureType)`.
- Use `WorkingstepAddPropertyLengthMeasure(wsId, "path_width", event.Width)`.
- Use `WorkingstepAddPropertyLengthMeasure(wsId, "layer_height", event.Height)`.

If `ws_id` cannot be obtained from `Workingstep(...)`, call `GetCurrentWorkingstep()` immediately after creating the workingstep.

## Toolpath Classification

Recommended names:

- Main workplan: `JSON Additive Workplan`.
- Layer workplan: `Layer 005 Z1.650`.
- Event workingstep: `L005 E0123 perimeter_external`.
- Geometry label: `perimeter_external`, `infill`, `travel`, etc.

Use of `SetPathType...`:

- Before a deposition path: `SetPathTypeTrajectory()`.
- Before a travel path: `SetPathTypeConnect()` or `SetPathTypeNonContact()`.
- For explicit approach/lift paths, if they appear in future inputs: `SetPathTypeApproach()` and `SetPathTypeLift()`.

Note: the documentation says `SetPathType...` is assigned to the expected current or next path, and it can conceptually miss the intended path if that path has already been finished by a feed/speed/executable change. Therefore it should be called immediately before emitting the first `GoToXYZ` of the path being classified.

## Recommended Generation Algorithm

1. Parse JSON into an additive JSON intermediate model.
2. Validate that layers and events with points exist.
3. Create `AptStepMaker`.
4. Execute `NewProjectWithCCandWP(options.ProjectName, 1, options.MainWorkplanName)`.
5. Execute `Millimeters()`, `FeedrateUnit("mmpm")`, `SpindleSpeedUnit("rpm")`, `CamModeOn()`, `SetModeMill()`.
6. Define the extrusion tool with `DefineTool(nozzleDiameter, nozzleDiameter / 2, ...)`.
7. Load tool `LoadTool(1)`.
8. Emit `PPrint(...)` or initial properties with global metadata if useful.
9. Emit `print_level_toolpaths.skirt` and `brim` before the first layer when present and not already included in `events`.
10. For each layer ordered by `layer_seq_id` and `print_z`, create `NestWorkplan(...)`.
11. For each event in `layer.events`, create a workingstep if it has at least two points, or if preserving a single-point diagnostic is desired.
12. For `travel`, call `Rapid()`, classify the path as connect/non-contact, and emit all points with `GoToXYZ`.
13. For `extrusion_path`, resolve feedrate, calculate spindle from extrusion, call `Feedrate(...)` to cancel rapid, call `SpindleSpeed(...)`, classify the path as trajectory/contact, and emit points with `GoToXYZ`.
14. Add CAM properties to the workingstep when possible.
15. Close each layer with `EndWorkplan()`.
16. Save with `SaveAsP21(outputPath)` or `SaveFastAsModules(outputPath)` depending on options.
17. Release COM with `Marshal.FinalReleaseComObject(stnc)`.

## Proposed C# Model

Add new models instead of forcing the Slic3r JSON into `AdditiveLayer`, because the current additive XML model is much simpler.

```csharp
public sealed class AdditiveJsonParseResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public AdditiveJsonProgram Program { get; init; }
    public int LayerCount { get; init; }
    public int EventCount { get; init; }
    public int PointCount { get; init; }
}

public sealed class AdditiveJsonProgram
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string GeneratorName { get; init; } = string.Empty;
    public string GeneratorVersion { get; init; } = string.Empty;
    public AdditiveJsonPrintConfig PrintConfig { get; init; } = new();
    public IReadOnlyList<AdditiveJsonLayer> Layers { get; init; } = Array.Empty<AdditiveJsonLayer>();
    public IReadOnlyList<AdditiveJsonPath> SkirtPaths { get; init; } = Array.Empty<AdditiveJsonPath>();
    public IReadOnlyList<AdditiveJsonPath> BrimPaths { get; init; } = Array.Empty<AdditiveJsonPath>();
}

public sealed class AdditiveJsonLayer
{
    public int LayerId { get; init; }
    public int LayerSeqId { get; init; }
    public double PrintZ { get; init; }
    public double Height { get; init; }
    public bool IsRaftLayer { get; init; }
    public bool IsSupportLayer { get; init; }
    public AdditiveJsonProcess Process { get; init; } = new();
    public IReadOnlyList<AdditiveJsonEvent> Events { get; init; } = Array.Empty<AdditiveJsonEvent>();
}

public sealed class AdditiveJsonEvent
{
    public int EventIndex { get; init; }
    public string Type { get; init; } = string.Empty;
    public string FeatureType { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public double LayerPrintZ { get; init; }
    public double Height { get; init; }
    public double? Width { get; init; }
    public double? Mm3PerMm { get; init; }
    public bool IsBridge { get; init; }
    public bool IsSolidInfill { get; init; }
    public IReadOnlyList<ToolpathPoint> Points { get; init; } = Array.Empty<ToolpathPoint>();
}
```

## Suggested New Files

- `AdditiveJsonParser.cs`: Slic3r JSON parsing and validation.
- `AdditiveJsonStepNcGenerator.cs`: STEP-NC generation from the JSON model.
- `AdditiveJsonSpeedResolver.cs`, or private methods inside the generator/parser: speed and percentage resolution.
- `Models.cs`: add JSON models and options.
- `StepNcApi.cs`: expose `ParseAdditiveJson(...)` and `ConvertAdditiveJsonToStepNc(...)`.
- `Program.cs`: add `parse-additive-json` and `additive-json` commands.
- `README.md`: document the new flow.

## Proposed Conversion Options

```csharp
public sealed class AdditiveJsonConversionOptions
{
    public string ProjectName { get; init; } = "JSON Additive Manufacturing STEP-NC";
    public int ContextId { get; init; } = 1;
    public string MainWorkplanName { get; init; } = "Main JSON Additive Workplan";
    public bool SaveAsP21 { get; init; } = true;
    public double DefaultFeedrate { get; init; } = 1600.0;
    public double DefaultSpindleSpeed { get; init; } = 0.0;
    public double DefaultNozzleDiameter { get; init; } = 0.5;
    public bool EmitTravels { get; init; } = true;
    public bool EmitSkirtAndBrim { get; init; } = true;
    public bool AddWorkingstepProperties { get; init; } = true;
    public bool UseVolumetricFlowAsSpindle { get; init; } = true;
    public bool ApplyCopyOffset { get; init; } = false;
}
```

## Important Validations

- The file exists and is valid JSON.
- `layers` exists and is not empty.
- Each emitted event should have at least two points to create a valid line. If it has one point, repeat it only if preserving the diagnostic is desired, even though it adds no useful geometry.
- Each point must contain two numeric coordinates.
- Z comes from `event.layer_print_z`; if missing, use `layer.print_z`; if missing, use accumulated heights.
- Do not apply `copy_offset` automatically without validating coordinates, to avoid double offset.
- Avoid duplicating `support`/`regions` paths when they already exist in `events`.

## Recommended Tests

- `ParseAdditiveJson` on `samples/additive/cube_new.json` should report 110 layers, 1966 events, and 21135 points if all events are counted.
- Convert `cube_new.json` to STEP-NC and verify that a `.stpnc` file is generated without native errors.
- Verify that `travel` events generate rapid movements and that the next extrusion restores `Feedrate(...)`.
- Verify that workingsteps include names with `feature_type`.
- Verify that extrusion produces `SpindleSpeed(...)` changes when `mm3_per_mm` or feedrate changes.
- Verify that `skirt`/`brim` are not duplicated if they later appear in `events`.
- Compare generated counts against parser summary: layers, events, extrusions, travels, and points.

## Risks and Open Decisions

- The DLL does not natively model FFF additive manufacturing; the result is a machining-compatible AP238 toolpath representation, not a complete additive semantic model.
- Toolpath color does not appear as a direct `AptStepMaker` capability; it should be handled through names/properties and external visualization.
- Mapping extrusion to spindle is visually useful but not semantically exact. This must be documented in properties.
- Temperature has no equivalent machining mapping. Storing it as property or comment is the least misleading option.
- The exact meaning of Slic3r speeds in this JSON should be confirmed. Slic3r usually expresses speeds in mm/s, while STEP-NC `FeedrateUnit("mmpm")` expects mm/min.
- If `Workingstep(...)` does not return an ID in the available COM wrapper, use `GetCurrentWorkingstep()` to attach properties.

## Recommended Incremental Implementation

1. Implement JSON parser and models, without STEP-NC generation.
2. Add `parse-additive-json` command and validate counts with `cube_new.json`.
3. Implement minimal generation: project, tool, layer, events, `Rapid`, `Feedrate`, `SpindleSpeed`, `GoToXYZ`.
4. Add speed resolution by `feature_type`.
5. Add volumetric mapping `mm3_per_mm * feedrate` to spindle speed.
6. Add CAM properties and temperature/configuration comments.
7. Add `skirt`/`brim` if they are not already in events.
8. Document README and CLI examples.
