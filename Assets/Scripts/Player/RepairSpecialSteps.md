# Repair preparation prototype

`Assets/Resources/RepairSpecialStepSettings.asset` enables the prototype via **enableSpecialRepairs**. Turn it off to restore normal repair seating, including an unfinished current job; existing hammer progress is mapped back to the normal three-hit requirement. No scene, robot prefab, spare quantities or order rewards are replaced.

Each accepted ordinary order chooses one actually missing slot. Preference cycles wheel, connector, casing; unavailable kinds fall back to an existing missing module. All other slots retain their normal behavior. Final AURINKO dismantling is excluded. Console instant repair still bypasses preparation.

- **Wheel:** fit the module normally, then use empty hands and hold LMB over it. Move the mouse left/right until the broad guide zone catches the marks; then move the mouse up while holding LMB to seat the wheel. This replaces that wheel's usual hammer seating.
- **Connector:** fit the module, then hold LMB and move left/right to match the guide. The module automatically seats inside the generous snap zone.
- **Casing:** fit the panel and straighten its dent with two hammer hits. A real vertex blend shape smooths from dented to half-straight to flat. This replaces the selected panel's normal three-hit seating.

The amber task point, guide and English/Russian instructions use the existing post-processing-safe repair HUD. Mouse work temporarily consumes camera look while leaving movement available. A toolbox wrench/hammer can be stowed automatically on the first LMB press over a wheel/connector; other carried objects and firearms do not start the task. Release LMB or use RMB to leave; alignment and seating progress persist. Menus, PC/rover focus, toolbox, held objects, noclip and leaving reach release the interaction. Fasteners and diagnostics remain gated until preparation completes. No timer, damage, cost or audio is added. Alignment respects tool sensitivity, and combined long running-gear meshes use a small fitting tilt instead of rotating like a round wheel.

`Tools > Moon Game > Repair > Build special repair meshes` rebuilds the blend meshes from the existing body-panel prefabs in the open Operator scene. It reads non-readable imported FBXs through Unity's editor mesh API, subdivides their surfaces to support a visible central dent and preserves materials/UVs. Generated files are limited to `Resources/RepairSpecialStepMeshes` and the settings catalogue. Runtime dent copies restore original renderers on completion, reset, feature disable or destruction.
