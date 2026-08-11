---
name: use-blender
description: Use when task creates, edits, exports, validates, or troubleshoots Blender 3D assets for this Unity project.
---

# Blender -> Unity asset workflow

## Source choice

- simple prototype mesh: generate with `bpy`.
- repeatable procedural asset: source in `Tools/Blender/generate_<asset>.py`; output in `Assets/_Game/Models/`.
- hand-authored state hard to express in script: preserve `.blend` source under `Tools/Blender/`; add thin deterministic export script.
- external model pack: require user approval for source, license, attribution, visual fit, asset cost. Record source and license beside imported assets.
- Blender owns mesh, UVs, normals, vertex colors, and optional animation. Unity integration follows [`AGENTS.md`](../../../AGENTS.md) architecture.

## Asset contract

Choose branch before geometry. Record target Unity owner, dimensions, origin, forward axis, triangle budget, material slots, UV needs, animation needs in generator or export script. Missing brief -> match nearby project assets; use smallest geometry satisfying silhouette.

- static gameplay prop or projectile: applied mesh transforms; stable material slots; asymmetric forward marker when orientation matters; Unity root owns collider, `Rigidbody`, gameplay scripts.
- modular environment piece: explicit module dimensions, snap grid, edge connections, pivot placement; static render mesh; Unity owns collision and scene placement.
- rigged or animated character, only when requested: preserve `.blend`; one named armature; stable bone hierarchy; explicit exported actions; normalized weights and project-agreed influence cap; Unity owns Avatar, Animator, root motion policy, gameplay collision.

## Run

1. Read repository `AGENTS.md`, relevant builder code, prefab contract, and clean/dirty Git status. Completion: authoritative owner and pre-existing changes identified.
2. Resolve Blender executable with `Get-Command blender`; fallback to installed `Blender Foundation` directories. Run `blender --version`. Completion: exact executable and version captured.
3. Write idempotent `bpy` generator:
   - start with `bpy.ops.wm.read_factory_settings(use_empty=True)`
   - derive repository root from `__file__`; avoid machine-specific output paths
   - use explicit names, dimensions, transforms, shading, selected export objects
   - multipart mesh: write connection map before geometry; list connected parts, contact axis/faces, required overlap; default minimum overlap `0.005m` unless asset scale requires tighter value
   - cubes: use `size=2`; treat scale as half-extents; apply scale immediately while created object remains active
   - spanning part: measure neighbour world bounds; derive span from measured endpoints; use `bmesh`, vectors, or quaternion alignment instead of guessed Euler rotations
   - axis-authored part: explicit rotation allowed; apply it and prove imported Unity orientation
   - structural hard surface: prefer bevel over subdivision; if subdivision required, validate evaluated post-modifier bounds
   - apply required transforms before export
4. Run mesh audit. Fail with `RuntimeError` on violated required invariant:
   - finite vertex coordinates; no zero-area faces, zero-length edges, or unintended loose geometry
   - no non-manifold edges for closed solids; intentional open surfaces declared in asset contract
   - evaluated normals and winding consistent; no inverted or missing faces in preview
   - exported object rotation near zero and scale near one; origin and world bounds match asset contract
   - unique stable object, mesh, material-slot, armature, bone, and action names as applicable
   - UV layers and material slots match branch contract; no unused slots
   - evaluated vertex and triangle counts within budget
   - every connection-map joint meets required world-space overlap
   - print audit results, world bounds, evaluated vertex/triangle counts, output path
5. Render automated previews from final evaluated geometry before export:
   - output: `Temp/BlenderPreviews/<asset>/`; never commit
   - views: front, rear, left, right, top, three-quarter
   - frame camera from measured world bounds; neutral background and lighting; fixed resolution
   - isolate render setup from export objects; preview generation must not change exported transforms, materials, modifiers, or selection
   - confirm every image exists and is non-empty; inspect silhouette, connections, forward direction, normals, clipping, pivots, scale cues
6. Export FBX with explicit settings: selected required objects only, `axis_forward="-Z"`, `axis_up="Y"`, unit scale applied, modifiers applied. Animation disabled for static branches; rigged branch exports only declared armature and actions.
7. Run generator from repository root:

```powershell
& $blenderExe --background --factory-startup --python Tools/Blender/generate_<asset>.py
```

Completion: Blender exit `0`; FBX exists and is non-empty; audit passes; six previews exist and were inspected; counts and bounds fit asset contract.

## Unity contract

- Unity axes: `+Y` up, gameplay forward `+Z`.
- FBX axis conversion: verify imported geometry, never infer success from Blender transform values.
- current projectile convention: model nose authored toward Blender `-Y` with export settings above -> imported Unity local `+Z`.
- prefab layout: physics/gameplay root -> imported model nested as `Visual` -> Unity materials assigned to renderers.
- Builder ownership, generated-output authority, and `.meta`/GUID safety -> [`AGENTS.md`](../../../AGENTS.md). Let Unity create metadata for new FBX files.

## Validate

1. When Unity integration changed, run current [`AGENTS.md`](../../../AGENTS.md) builder protocol through current facade entry points. This includes harness, bake-current, build, separate-process semantic validation, and process/lock gates.
2. Validator proves every applicable invariant:
   - source model asset exists
   - prefab `Visual` contains imported FBX mesh and renderer
   - imported mesh provenance path matches expected FBX
   - no missing components
   - material assignment valid
   - bounds and scale fit gameplay root
   - asymmetric model nose points Unity local `+Z`
   - rigged branch: expected Avatar, bones, clips, loop settings, root motion policy
   - environment branch: expected module dimensions, pivot, seams, static flags
   - root collider, Rigidbody, and gameplay settings unchanged
3. Follow `AGENTS.md` final-diff hygiene; additionally run `git diff --check`. Completion: Blender exit `0`; required Unity checks pass; expected diff only.

## Failure routing

- model imports backward: change source geometry orientation, regenerate, rebuild, rerun forward validator. Avoid hidden compensating prefab rotation.
- mesh missing: verify selected export objects, active object, `object_types={"MESH"}`, joined hierarchy, output path.
- size wrong: fix source units/dimensions or declared visual scale; keep gameplay collider dimensions explicit.
- multipart gaps: inspect connection map and printed world bounds; derive replacement span from measured neighbours; regenerate.
- wrong directional part: replace guessed Euler rotation with endpoint-derived vector/quaternion or `bmesh` geometry; rerun bounds and overlap audit.
- preview differs from FBX import: compare evaluated Blender bounds with Unity imported bounds; inspect unapplied modifiers, export selection, axis conversion, material reassignment.
- audit rejects intentional open geometry: declare allowed open surface in asset contract; keep closed-solid checks for every other mesh.
- materials wrong: keep mesh material slots stable; assign project URP materials in builder.
- FBX hash changes after identical runs: Blender metadata can change while geometry stays equal. Avoid needless regeneration; apply `AGENTS.md` semantic-validation rule.
- batch import fails: inspect full Blender/Unity logs first, then confirm executable version, FBX existence, `.meta` health, Editor process, and lock state.
