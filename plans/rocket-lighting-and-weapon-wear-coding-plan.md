# Rocket Lighting And Weapon Wear Coding Plan

Status: complete; weapon revision accepted; integration complete
Source: direct user request
Run ID: direct-rocket-lighting-weapon-wear
Plan ID: rocket-lighting-weapon-wear
Attempt ID: direct-20260823-01
Covered Requirements: accepted brighter lighting; contact-driven launcher and shotgun PBR wear; Quake-2 khaki-grey/black/red-glass palette; no periodic shotgun stripe; deterministic Blender proofs; Unity Fast rebuild; persisted screenshots; arena texture replacement excluded
Baseline: `fba1ec91ce33994d43713817238d6b9d8ff6af2e`
Dependencies: None

## Objective

Make launcher and shotgun readable under accepted brighter warm-neutral, Quake-inspired lighting. Replace generic/random wear with deterministic model-specific contact wear and fine metal detail. Finalize through bake-free Fast rebuild, persisted semantic validation, and visual comparison.

## Scope

- in: launcher UV0; shotgun FPS/world UV0; launcher and shotgun five-map PBR atlases; contact-driven wear; importer/material/validator wiring; accepted sun/ambient/post/SSAO; stage/fingerprint/comparator closure; Fast/Persisted capture; bake-free Fast proof
- out: arena texture/mesh replacement; gameplay, collision, camera, animation, placement, weapon silhouette, fog, bloom, or manual Unity UI edits

## Completion Record

- precedence: final accepted outcome. Overrides pending Development/Production bake steps below.
- final generated head: `5b006d5ae8df98e10290c1c528b86870683b36d6`
- launcher: five-map `2048²` atlas; khaki metal, black structure, red glass; contact-biased edge/corner/muzzle wear; irregular scratches; left-side first-person framing retained.
- shotgun: shared FPS/world five-map `2048²` atlas; launcher-matched material system; brown legacy surface removed; right-side first-person framing retained.
- stripe fix: analytic `directional_grain` sine wave removed from smoothness and height/normal. `WeaponSurfaceReference.png` supplies irregular non-periodic scratch carrier. Directional brushing remains subtle and smoothness-only.
- preserved: colour blocking, geometry, UV zones, material groups, FBX GUIDs, wear placement, weapon positions, launcher silhouette/appearance.
- arena: no arena texture, material, mesh, tiling, or composition changes. Previously accepted lighting retained.
- Blender proofs: launcher and shotgun two-run semantic/UV/texture identity passed; exact geometry/bounds/group audits passed; scratch coverage near `1.8%`; zero glass wear.
- Unity Fast workflow: complete; `bakeCount=0`; changed generated outputs limited to build manifest, player prefab, and MovementLab scene.
- generated comparator: `3/3` authoritative paths covered; dangling `0`; GUID churn `0`; broken pairs `0`; unsupported `0`.
- semantic validation: separate Unity process reopened generated scene -> `Rocket Fooxball Movement Lab validation succeeded`.
- capture: six persisted `1920x1080` High/Low images under `C:\wt\rlwb2987aece\direct-20260823-b2987aec\WMR5-persisted-5b006d5-exec088`.
- visual comparison: fresh `sol_medium` adjudicator passed all checks. Shotgun periodic stripe absent; launcher not regressed; scratches visible; colours, geometry, wear placement, and positions preserved.
- bake decision: Development and Production bakes skipped by user direction. No lighting bake run for final weapon revision.
- final state: weapon work complete. Arena remains out of scope. T6/T7/production-final checks not required.

## Decisions

- assumption: launcher geometry and its four material groups remain correct.
- decision: execution pins this plan, creates unique branch `codex/rocket-lighting-weapon-wear-<attempt8>`, short worktree `C:\wt\rlw<attempt8>`, private Library, and evidence root `C:\wt\rlw<attempt8>e`; every invocation receives a monotonically increasing `executionId`, and every evidence directory is `<root>\<attemptId>\<checkId>-<sha8>-<executionId>` so same-SHA retries never collide.
- decision: T1 lands pipeline/capture support first; T2 generates raw art; T3 alone generates its Unity metas. CP2 groups T2+T3 and freezes a source/raw commit followed by a generated-meta commit, preventing a reviewed one-sided asset pair.
- decision: atlas is `2048²`; zones in pixels are Metal `(32,864)-(2016,2016)`, Dark `(32,352)-(1312,832)`, Accent `(1344,352)-(2016,832)`, AccentCore `(32,32)-(2016,320)`; UV smart-project angle `66°`; island padding and gutter dilation `16 px`.
- decision: preserve four group names/slot order/shell-core pairs, `1120` vertices, `2156` triangles, Blender bounds `(-.16,-.55,-.1313)..(.16,.20,.11)`, Blender `-Y`/Unity `+Z`, and FBX GUID.
- decision: wear uses metric distance to actual mesh edges with dihedral `>=30°` and vertices incident to `>=3` sharp edges. UV-island borders never create wear. Edge contact=`1-smoothstep(.0015,.006,edgeDistanceMeters)`; corner contact=`1-smoothstep(.002,.010,cornerDistanceMeters)`; muzzle wear uses each model profile's forward lip and edge contact.
- decision: chips occur on Metal contact zones only. Dark receives subtle grey edge scuffs/soot. Accent and AccentCore receive zero chips/scratches and remain clean red glass. Fine directional scratches: width `.25-.8 mm`, length `4-22 mm`, tangent-aligned, contact-biased, height about `-.015`; chips height about `-.035`. Micro-brushed roughness/normal detail remains below silhouette scale. No random interior speckles.
- decision: handling polish stays restricted to stock/grip/pump/rear handling zones. Channel order: base -> contact chip/scuff -> muzzle soot -> cavity grime -> handling polish -> micro-normal/roughness. Proof and bake share identical masks.
- decision: proof schema `2`; require chip-contact precision `>=.90`, scratch-contact precision `>=.85`, Metal scratch precision `1`, glass wear pixels `0`, muzzle-soot precision `>=.90`, physical scratch width/length audit, zero non-adjacent UV interior overlaps, each zone `>=15%` utilization, two-run UV/texture hash identity.
- decision: Base Color is RGBA8 sRGB; Normal/MetallicSmoothness/Occlusion/Emission are linear RGBA8; metallic=R, smoothness=A; AO=`clamp(1-.35*cavity-.15*grime,.45,1)`; exact core emission map value `.85`.
- decision: Metal clean/exposed `(.50,.49,.43)/(.66,.67,.65)`; Dark clean/scuff `(.025,.028,.03)/(.15,.16,.17)`; Accent glass `(.55,0,0)`; AccentCore `(.16,0,0)`. Pure-red core emission `(1,0,0)*2`; no orange/yellow component. Existing PBR scalar ranges remain unless atlas owns per-pixel value.
- decision: Unity BaseColor RGB multipliers are white; Accent alpha `.42`; metallic/smoothness/occlusion/bump multipliers `1`; launcher and shotgun use no legacy detail normal. Atlases own material variation.
- decision: shotgun shared atlas is `2048²`. FPS zones: Metal `(32,1056)-(1344,2016)`, Dark `(1376,1056)-(2016,1344)`, Accent `(1376,1376)-(2016,1664)`, Core `(1376,1696)-(2016,2016)`. World zones: Metal `(32,32)-(1344,992)`, Dark `(1376,32)-(2016,320)`, Accent `(1376,352)-(2016,640)`, Core `(1376,672)-(2016,992)`. Padding/dilation `16 px`.
- decision: atlas importer uses Clamp U/V, mipmaps, Trilinear, aniso `8`, max `2048`; Normal uses NormalMap; only Base Color is sRGB.
- decision: six Blender previews are `640²`; object coverage `15%-85%`, clipped foreground `<.5%`, luminance SD `>.025`, chroma SD `>.015`.
- decision: sun `(50,330,0)`, intensity `2.4`, shadows `.65`; ambient sky `(.42,.40,.36,1)`, equator `(.28,.25,.22,1)`, ground `(.16,.14,.12,1)`, persisted intensity `.85`, Fast intensity `1.05`; ACES, exposure `+.35 EV`, contrast `+2`, saturation `+2`; SSAO `.90`, direct `.15`; fog/bloom unchanged.
- decision: capture adds explicit `Fast|Persisted`. `MovementLabFastModeSession.EnterForCapture(int qualityIndex)` snapshots state, applies Fast scene/rendering simplifications while retaining the requested High or Low quality index, and asserts that parameterized contract; each of the six images enters its requested quality, asserts Fast, captures, and restores in `finally`. Existing interactive `Enter()` continues to require Iteration. Persisted capture asserts no Fast session. Capture never authors/saves assets.
- decision: VISUAL2 acceptance includes authority for one Production bake. Valid reuse consumes zero. Failed/rejected execution exhausts authority; a replacement needs new user authority.
- question: None

## Execution Graph

`START -> T1 -> CP1 -> T2 -> T3 -> CP2 -> T4 -> CP3 -> JOIN1 -> T5 -> CP4 -> VISUAL1 -> T6 -> CP5 -> VISUAL2 -> T7 -> CP6 -> FINAL`

- notation: `->` sequential; `+` requires every named predecessor
- gates: START = pinned snapshot, clean launch HEAD, baseline ancestor, unique branch/worktree/evidence, private Library, no owning Unity; JOIN1 = CP1-3 accepted at clean committed HEAD; VISUAL1 = user accepts Fast wear/readability/direct light; VISUAL2 = user accepts Development lighting and authorizes one Production bake; FINAL = CP6, full EditMode, separate ProductionValidate, clean commit

## VISUAL1 Revision

- trigger: user rejected weapon visuals at `bf1a7eaee10292dcbc02b06b8f92c728da92e9b8`; Fast lighting accepted and frozen
- precedence: R1-R4 override earlier shotgun exclusion, legacy shotgun-material contract, random/UV-seam wear formulas, warm metal/red palette, and T5 VISUAL1 result
- graph: `VISUAL1_REJECTED -> (R1 + R2) -> CPR1 -> R3 -> CPR2 -> R4 -> CPR3 -> VISUAL1_REV2 -> T6`
- arena: no texture, material, mesh, tiling, or composition change in this plan; prior visible change came from lighting only

## Tasks

### T1: Close integration, proof, and capture contracts

- objective: make new launcher assets importable, launcher-only, validator/comparator-covered, and truthfully capturable in Fast mode
- covered_requirements: launcher integration; cheap real Fast capture; generated safety
- owner: `worker-T1 (luna_max)`
- dependencies: `START -> pinned environment`
- parallel_contract: None
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/{MovementLabContract.cs,MovementLabContractCatalog.cs,MovementLabImportPipeline.cs,MovementLabMaterialPipeline.cs,MovementLabPrefabPipeline.cs,MovementLabValidator.cs,MovementLabStageGraph.cs}`; `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; `Tools/Validation/Compare-GeneratedYaml.ps1`; `Tools/Tests/MovementLabHarness.Tests.ps1`; `Tools/Tests/Fixtures/{red-workflow.ps1.txt,red-generated-yaml-comparator.ps1.txt}`
- protected: `Tools/Blender/**`; models/textures; lighting-author sources; generated inventory; runtime
- read_paths: `MovementLabBuilder.cs -> facade`; `MovementLabFastModeSession.cs -> transient lifecycle`; `BrightArenaVisualCapture.cs -> six views`; `MovementLabContract.cs -> inventories`; `MovementLabStageGraph.cs -> ownership`; workflow/comparator/harness -> schema and guards
- validation_environment: source-only worker; execution-orchestrator holds CP1 compile Unity lease
- unity_mutation: `false`
- expensive_proof_owner: `execution-orchestrator`
- expensive_proof_execution: `orchestrator_phase`
- implementation: add five launcher texture paths/metas; add narrow `ImportLauncherVisualAssets` facade/import method affecting only launcher FBX and five maps; use an explicit PBR spec at launcher/shotgun call sites; bind four launcher materials to new atlas, shotgun to legacy; validate maps/importers/material multipliers/slots/no-detail-normal/UV zones; remove VolumeProfile from Lighting inputs but retain output, add LightingProfiles source input, bump affected stage versions, derive fingerprints from every QualityOutput; replace broad `GeneratedRoots` and `AuthoritativeInventory` classification with one exact Appendix A-derived path set, and derive BuilderOutputContract, generated hashing, dirty-path classification, authoritative inventory, and comparator selection from that same set; include exact closed metas; support `.physicMaterial` as Unity text; update harness and both red fixtures
- done when: CP1 compile and harness pass; red fixtures fail; absent new assets are not loaded during compile; shotgun contract unchanged; every generated classifier/hash/comparator consumer uses the single exact set and supports all Appendix A types
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -EvidenceRoot <root>\<attemptId>\T1-harness-<sha8>-<executionId>` -> exit 0 under 90s, HEAD green, fixtures red, no Unity/lock; proof: waited hidden Unity `-batchmode -nographics -quit -projectPath <project> -logFile <root>\<attemptId>\T1-compile-<sha8>-<executionId>\unity.log` -> exit 0, no compile errors, release
- review_focus: launcher/shotgun leakage, importer-meta escape, stage self-dependency, missing Iteration fingerprint, unsupported type, destructive lock handling, false Fast state, stale fixture
- review_checkpoint: `CP1`

### T2: Generate UV-aware launcher wear in Blender

- objective: produce FBX, five PBR maps, deterministic proof, and six previews with geometry-derived irregular wear
- covered_requirements: real wear; Blender assigned `sol_high`
- owner: `worker-T2 (sol_high)`
- dependencies: `CP1 -> path/import/proof contract frozen`
- parallel_contract: None
- owns: `Tools/Blender/generate_fps_rocket_launcher.py`; `Assets/_Game/Models/FpsRocketLauncher.fbx`; `Assets/_Game/Textures/FpsRocketLauncher_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png`; `Temp/BlenderPreviews/FpsRocketLauncher/{proof.json,front.png,rear.png,left.png,right.png,top.png,three-quarter.png}`; `<root>/<attemptId>/T2-blender-<sha8>-<executionId>/{proof.json,front.png,rear.png,left.png,right.png,top.png,three-quarter.png}`
- protected: all metas; retro generator/maps; shotguns; Unity/PowerShell source; generated Unity assets
- read_paths: launcher generator -> geometry/export; contract/importer -> bounds/UV contract; retro generator -> forbidden motifs
- validation_environment: Blender 4.5.10 LTS; project Temp staging; no Unity
- unity_mutation: `false`
- expensive_proof_owner: `worker-T2`
- expensive_proof_execution: `same_dispatch`
- implementation: factory reset; preserve geometry/groups; project/remap UVs; barycentrically rasterize the fully specified fields; synthesize channels with the fixed precedence and no ellipse/circle/segment/pane/grid primitives; dilate; bind final maps to Principled previews; stage, audit, two-run compare, then atomic promote; signature includes UV loops and five hashes; proof schema `{schemaVersion:1,seed,versions,geometry,bounds,uvZones,masks,periodicity,channels,outputSha256,previewSha256,twoRunIdentical}`; after success copy proof and six previews byte-for-byte from required Temp locations into the unique immutable T2 evidence directory and record both path sets/hashes
- done when: proof schemaVersion 1, exact geometry/zones/thresholds, five identical output hashes across two runs, six passing previews, generic/shotgun bytes unchanged
- checks: proof: `& 'C:\Program Files\Blender Foundation\Blender 4.5\blender.exe' --background --factory-startup --python Tools/Blender/generate_fps_rocket_launcher.py -- --proof-two-run` -> exit 0 and all proof fields/audits pass
- review_focus: geometry/slot drift, fake motifs, non-model wear, nondeterminism, channel mismatch, clipping, flat preview, unrelated art change
- review_checkpoint: `CP2` grouped with T3 because raw assets require generated metas

### T3: Import launcher assets and generate exact metas

- objective: configure only launcher FBX plus five atlas maps and close their Unity pairs
- covered_requirements: importer provenance
- owner: `worker-T3 (luna_max)`
- dependencies: `T2 -> raw assets exist`; `CP1 -> narrow importer exists`
- parallel_contract: None
- owns: `Assets/_Game/Models/FpsRocketLauncher.fbx.meta`; `Assets/_Game/Textures/FpsRocketLauncher_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png.meta`
- protected: all source/raw art and every other generated path
- read_paths: builder narrow facade; importer exact contract; comparator metadata/pair/GUID logic
- validation_environment: exclusive short-path Unity lease/private Library
- unity_mutation: `true`
- expensive_proof_owner: `worker-T3`
- expensive_proof_execution: `same_dispatch`
- implementation: harness; waited hidden Unity executeMethod `RocketFooxball.Editor.MovementLabBuilder.ImportLauncherVisualAssets`; require exactly six meta changes; comparator CP1 frozen SHA to WORKTREE; preserve FBX GUID, create five unique nonzero GUIDs, zero unsupported/dangling/broken pairs. Barrier commits T2 source/raw then T3 metas separately before grouped review.
- done when: CP2 reviews frozen paired commits and clean tree
- checks: proof: harness command with unique T3 evidence -> exit 0 under 90s; proof: waited hidden Unity `-batchmode -nographics -quit -projectPath <project> -executeMethod RocketFooxball.Editor.MovementLabBuilder.ImportLauncherVisualAssets -logFile <...>` -> exit 0, six metas only, release; proof: comparator `-Base <CP1FrozenSha> -Head WORKTREE -FailOnDangling` -> six metas, full headers, no failures
- review_focus: broad mutation, seventh path, GUID churn, wrong importer, raw-byte mutation, one-sided pair
- review_checkpoint: `CP2`

### T4: Author deterministic brighter lighting

- objective: code-own and idempotently author the decided sun/ambient/post/SSAO/Fast values
- covered_requirements: overall brightness; launcher readability
- owner: `worker-T4 (luna_max)`
- dependencies: `CP2 -> launcher response frozen`
- parallel_contract: None
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; `Assets/_Game/Editor/MovementLab/{MovementLabLightingPipeline.cs,MovementLabLightingProfiles.cs,MovementLabFastModeSession.cs}`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; `Tools/Validation/Capture-BrightArenaVisuals.ps1`; `Tools/Tests/MovementLabHarness.Tests.ps1`
- protected: every T1-T3 path except the explicitly re-owned `MovementLabBuilder.cs` and harness file; raw art; generated assets except the declared VolumeProfile pair; runtime
- read_paths: lighting pipeline/profile/Fast/quality sources; existing volume asset read-only for subasset identities
- validation_environment: source-only worker; orchestrator CP3 compile lease
- unity_mutation: `false`
- expensive_proof_owner: `execution-orchestrator`
- expensive_proof_execution: `orchestrator_phase`
- implementation: named constants; idempotent author reuses exactly one Tonemapping/Bloom/ColorAdjustments subasset, writes overrides/values, saves/imports/reloads, verifies GUID/local IDs; `BuildMovementLabFast` calls author before entering transient mode, while capture never calls it; both bake preparations call it; add `ValidateLightingAuthorIdempotency` facade which records profile GUID, component local IDs/counts, calls author, hashes persisted bytes, calls author again, reimports/reloads, then requires identical first/second hashes and identities and writes schemaVersion 1 JSON; implement `EnterForCapture(High|Low)` and capture VisualMode lifecycle; preserve six images; harden capture lock/process release; apply decided sun/ambient/SSAO/Fast values; keep Low policy/fog/bloom; validators share constants
- done when: compile passes; two-call proof is byte-no-op after first call, exactly one expected component per type, GUID/local IDs stable; Fast builder authors before transient state; High/Low Fast capture is read-only and asserted; no duplicate/recreated subasset; exact values validated
- checks: proof: harness with unique T4 evidence -> exit 0 under 90s; proof: waited hidden compile-only Unity -> exit 0, no errors, release; proof: fresh harness then waited hidden Unity `-batchmode -nographics -quit -projectPath <project> -executeMethod RocketFooxball.Editor.MovementLabBuilder.ValidateLightingAuthorIdempotency -movementLabEvidencePath <root>\<attemptId>\T4-idempotency-<sha8>-<executionId>\result.json -logFile <...>\unity.log` -> exit 0, schema 1, firstHash=secondHash, one component/type, stable profile GUID/local IDs, only `pair(Assets/_Game/Lighting/MovementLabVolumeProfile.asset)` changed, release; generated barrier commits that pair separately before CP3
- review_focus: asset-only values, destructive subassets, wrong values, Fast/Persisted confusion, fog/bloom drift
- review_checkpoint: `CP3`

### T5: Build and capture Fast candidate

- objective: integrated generated candidate plus six images with transient Fast state proven active
- covered_requirements: cheap approval
- owner: `worker-T5 (luna_max)`
- dependencies: `JOIN1 -> reviewed clean sourceFreezeSha`
- parallel_contract: None
- owns: Appendix A; unique T5 evidence
- protected: source/raw art; other evidence
- read_paths: workflow Fast; capture Fast; Appendix A
- validation_environment: exclusive Unity; one waited process per workflow step and capture; harness immediately before workflow; capture internal harness
- unity_mutation: `true`
- expensive_proof_owner: `worker-T5`
- expensive_proof_execution: `same_dispatch`
- implementation: harness; Fast workflow (existing probe then build processes); validate schemaVersion 1 result/ledger/release; comparator sourceFreezeSha to WORKTREE; generated barrier commits selected Appendix A paths only; capture exact commit in Fast mode and require per-image requested High/Low quality, enter/assert/capture/restore, six successful assertions, and no asset write
- done when: CP4 accepts; VISUAL1 shows High rocket first with commit/fingerprint/hash and label `Fast transient preview; baked GI/probes/HDR/post/SSAO disabled`
- checks: proof: harness unique T5 -> pass; proof: workflow `-Mode Fast -ProjectPath <project> -EvidenceRoot <root>\<attemptId> -AttemptId T5-fast-<sha8>-<executionId>` -> schema 1/top complete/ledger/release; proof: comparator `-Base <sourceFreezeSha> -Head WORKTREE -FailOnDangling` -> exact Appendix A, zero failures; proof: capture `-AttemptId T5-capture-<sha8>-<executionId> -VisualMode Fast` -> six 1920x1080 images, state assertions/hashes/release
- review_focus: inactive Fast capture, source drift, out-of-inventory path, missing ledger/comparator/image binding
- review_checkpoint: `CP4`

### R1: Integrate model-specific shotgun atlas

- objective: replace shotgun legacy tile materials with one five-map FPS/world atlas while preserving model/material GUIDs and gameplay wiring
- owner: `worker-R1 (luna_max)`
- dependencies: `VISUAL1_REJECTED`; parallel with R2 through bound disjoint ownership
- owns: `Assets/_Game/Editor/MovementLab/{MovementLabContract.cs,MovementLabContractCatalog.cs,MovementLabImportPipeline.cs,MovementLabMaterialPipeline.cs,MovementLabPrefabPipeline.cs,MovementLabValidator.cs,MovementLabStageGraph.cs}`; `Assets/_Game/Editor/MovementLabBuilder.cs`; `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; `Tools/Tests/MovementLabHarness.Tests.ps1`; `Tools/Tests/Fixtures/red-workflow.ps1.txt`
- protected: Blender scripts; FBX/PNG/meta assets; generated outputs; lighting/capture sources
- implementation: add `Shotgun_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png` contracts; narrow importer entry point covers both weapon FBXs and ten atlas maps; configure/validate Clamp, mipmaps, Trilinear, aniso `8`, max `2048`, BaseColor-only sRGB, NormalMap-only normal; validate eight UV zones across FPS/world FBXs; bind existing four `Shotgun*.mat` GUIDs to shotgun atlas with neutral multipliers and no detail normal; validate pure-red glass/emission; close exact generated inventory; bump invalidated stages
- checks: harness and hidden Unity compile; no asset load required for compile; red fixture remains red
- review_checkpoint: `CPR1`; fresh `sol_medium`; Critical/High only

### R2: Reauthor launcher and shotgun contact wear

- objective: generate two deterministic weapon surfaces matching revised wear/palette contract
- owner: `worker-R2 (sol_high)`
- dependencies: `VISUAL1_REJECTED`; parallel with R1 through bound disjoint ownership
- owns: `Tools/Blender/generate_fps_rocket_launcher.py`; `Tools/Blender/generate_fps_shotgun.py`; `Assets/_Game/Models/{FpsRocketLauncher,FpsShotgun,Shotgun}.fbx`; `Assets/_Game/Textures/FpsRocketLauncher_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png`; `Assets/_Game/Textures/Shotgun_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png`; unique R2 proof/previews
- protected: all metas; Unity/PowerShell source; retro textures; generated Unity outputs; lighting
- implementation: preserve geometry/groups/slots/bounds/axes; repack shotgun eight zones; raster actual sharp-edge/corner/muzzle/handling fields; generate contact-only chips/scuffs, fine directional scratches, micro-brushed normal/roughness, muzzle soot, cavity grime, handling polish; enforce khaki-grey/black/pure-red-glass palette; preserve glass from wear; stage/audit/two-run compare/atomic promote; launcher six previews plus shotgun FPS/world previews
- checks: Blender 4.5.10 factory-startup proofs; schema `2`; exact geometry/bounds/groups; UV zones/overlap; contact precision; glass zero-wear; scratch dimensions; five hashes per weapon identical across two runs; unrelated art unchanged
- review_checkpoint: `CPR2` grouped with R3 because raw/meta pairs are indivisible

### R3: Import revised weapon atlases

- objective: generate only new shotgun PNG metas and validate both weapon atlas import contracts without raw-byte mutation
- owner: `worker-R3 (luna_max)`
- dependencies: `CPR1 + R2`
- owns: `Assets/_Game/Models/{FpsRocketLauncher,FpsShotgun,Shotgun}.fbx.meta`; `Assets/_Game/Textures/FpsRocketLauncher_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png.meta`; `Assets/_Game/Textures/Shotgun_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png.meta`
- protected: source/raw art/generated Unity outputs
- implementation: harness; waited hidden narrow weapon import; preserve three FBX GUIDs and five launcher-map GUIDs; create five unique shotgun-map GUIDs; comparator from R2 frozen source/raw commit to WORKTREE; require exact five additions, intact pairs, zero dangling/GUID churn/unsupported
- review_checkpoint: `CPR2`; fresh `sol_medium`; Critical/High only

### R4: Rebuild and capture revised Fast candidate

- objective: rebuild generated outputs and capture six images with accepted lighting plus revised launcher/shotgun surfaces
- owner: `worker-R4 (luna_max)`
- dependencies: `CPR1 + CPR2`
- owns: Appendix A; unique R4 workflow/comparator/capture evidence
- protected: source/raw/meta assets; lighting values; prior evidence
- implementation: harness; Fast workflow; generated comparator; separate generated commit; exact-SHA Fast capture; no bake; no asset write during capture
- done when: CPR3 accepts and VISUAL1_REV2 shows High launcher and shotgun first
- checks: schema `1` workflow complete; `bakeCount=0`; comparator full actual coverage/zero failures; six `1920x1080` images; source clean; Unity/lock release
- review_checkpoint: `CPR3`; fresh `sol_medium`; Critical/High only

### T6: Bake and capture Development candidate

- objective: representative GI/reflection evidence after Fast approval
- covered_requirements: representative lighting approval
- owner: `worker-T6 (luna_max)`
- dependencies: `VISUAL1 -> explicit acceptance`
- parallel_contract: None
- owns: Appendix A; unique T6 evidence
- protected: source/raw art; prior evidence
- read_paths: workflow Development; capture Persisted; Appendix A
- validation_environment: exclusive Unity; one waited process per workflow step/capture; pre-workflow and capture-internal harnesses
- unity_mutation: `true`
- expensive_proof_owner: `worker-T6`
- expensive_proof_execution: `same_dispatch`
- implementation: harness; Development workflow; require schema 1/current/development; comparator accepted Fast SHA to WORKTREE; commit selected outputs; Persisted capture exact commit with Fast inactive
- done when: CP5 accepts; VISUAL2 shows High rocket/external first with exact SHA/fingerprint/profile/hashes and asks for look acceptance plus one-bake authority
- checks: proof: harness unique T6 -> pass; proof: workflow `-Mode Development -ProjectPath <project> -EvidenceRoot <root>\<attemptId> -AttemptId T6-development-<sha8>-<executionId>` -> top complete/current/development/ledger/release; proof: comparator from accepted Fast SHA -> exact Appendix A/no failures; proof: capture `-AttemptId T6-capture-<sha8>-<executionId> -VisualMode Persisted` -> six images/Fast inactive/hashes/release
- review_focus: Fast leakage, wrong profile, source drift, out-of-scope generation, unbound images
- review_checkpoint: `CP5`

### T7: Prepare Production outputs

- objective: consume authorized single-bake budget or valid reuse, commit and review authoritative outputs
- covered_requirements: production finalization
- owner: `worker-T7 (luna_max)`
- dependencies: `VISUAL2 -> accepted look and explicit one-bake authority`
- parallel_contract: None
- owns: Appendix A; unique T7 evidence
- protected: source/raw art; accepted images
- read_paths: ProductionPrepare workflow/result ledger; comparator; lighting manifest; Appendix A
- validation_environment: exclusive Unity; harness before compile and separately immediately before workflow; one waited process per step; max one actual production bake
- unity_mutation: `true`
- expensive_proof_owner: `worker-T7`
- expensive_proof_execution: `same_dispatch`
- implementation: harness+compile; fresh harness; run exact waited hidden pre-probe Unity command `-batchmode -nographics -quit -projectPath <project> -executeMethod RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState -movementLabProbePath <root>\<attemptId>\T7-preprobe-<sha8>-<executionId>\probe.json -logFile <...>\unity.log`; require schemaVersion 1 and release; resolve `reused` iff production profile and Lighting+BakedOutput current, else `executed`; run another fresh harness immediately before ProductionPrepare; run it; parse top complete and `production-bake` ledger row whose internal owner remains literal `workflow-orchestrator`, while this plan check owner remains worker-T7; zero bakes for reused/exactly one for executed; comparator accepted Development SHA to WORKTREE; commit selected outputs; CP6 review. Failed/rejected executed bake blocks replacement pending new authority.
- done when: CP6 accepts final generated commit/status/bake count/comparator/manifest/persisted references and tree clean
- checks: proof: precompile harness then waited hidden compile-only -> pass/release; proof: fresh pre-probe harness then exact waited pre-probe command above -> schema 1/current fields/release; proof: another fresh pre-workflow harness -> pass; check_id=movementlab-production-prepare; tier=production-final; owner=worker-T7; expected_status=selector: `reused` iff schemaVersion 1 pre-probe reports production and current Lighting+BakedOutput, otherwise `executed`; command=`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare -ProjectPath <project> -EvidenceRoot <root>\<attemptId> -AttemptId T7-production-prepare-<sha8>-<executionId>`; mutates_project=true; input_paths=[`Assets/_Game/Editor/MovementLabBuilder.cs`,`Assets/_Game/Editor/BrightArenaVisualCapture.cs`,`Assets/_Game/Editor/GraphicsQualityConfigurator.cs`,`Assets/_Game/Editor/MovementLab/MovementLabContract.cs`,`Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs`,`Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs`,`Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs`,`Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`,`Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`,`Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`,`Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs`,`Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs`,`Assets/_Game/Editor/MovementLab/MovementLabFastModeSession.cs`,`Assets/_Game/Models/FpsRocketLauncher.fbx`,`Assets/_Game/Textures/FpsRocketLauncher_BaseColor.png`,`Assets/_Game/Textures/FpsRocketLauncher_Normal.png`,`Assets/_Game/Textures/FpsRocketLauncher_MetallicSmoothness.png`,`Assets/_Game/Textures/FpsRocketLauncher_Occlusion.png`,`Assets/_Game/Textures/FpsRocketLauncher_Emission.png`]; run_point=VISUAL2; evidence=schema 1, top complete, internal ledger owner `workflow-orchestrator`, production-bake status equals selector, current production probe, valid marker, bakeCount 0/1, manifest digest, lockReleaseProof; proof: comparator accepted Development SHA to WORKTREE -> exact Appendix A/no failures
- review_focus: ambiguous/wrong ledger status, multiple bake, malformed marker, absent authority, drift, scope/comparator gap
- review_checkpoint: `CP6`

## Execution Assignments

- workers: `T1 worker-T1(luna_max) foundation`; `T2 worker-T2(sol_high) Blender`; `T3 worker-T3(luna_max) importer metas`; `T4 worker-T4(luna_max) lighting`; `T5 worker-T5(luna_max) Fast`; `T6 worker-T6(luna_max) Development`; `T7 worker-T7(luna_max) ProductionPrepare`
- review_checkpoints: CP1 T1 per-worker frozen; CP2 T2+T3 grouped because raw/meta pairing is indivisible; CP3 T4 per-worker frozen; CP4 T5 generated/capture; CP5 T6 generated/capture; CP6 T7 generated/production proof
- reviewers: fresh `sol_medium`; Critical/High only; fixes to new exact-scope workers; Blender fixes always `sol_high`; freeze and rerun invalidated checks before re-review

## Final Verification

- exact head: clean committed accepted source/raw, generated launcher metas, and final generated-output commit when comparator selected paths
- checks: proof: harness immediately before Test Runner -> pass under 90s; proof: waited hidden Unity `-batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode -testResults <root>\<attemptId>\FINAL-editmode-<sha8>-<executionId>\results.xml -logFile <...>\unity.log` with no `-quit` -> exit 0, XML parses total>0/failed=0/inconclusive=0, release; proof: fresh harness immediately before validator -> pass; check_id=movementlab-production-validate; tier=production-final; owner=execution-orchestrator; expected_status=executed; command=`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath <project> -EvidenceRoot <root>\<attemptId> -AttemptId FINAL-production-validate-<sha8>-<executionId>`; mutates_project=false; input_paths=[`Assets/_Game/Scenes/MovementLab.unity`,`Assets/_Game/Generated/MovementLabBuildManifest.json`,`Assets/_Game/Lighting/MovementLabLightingManifest.json`,`Assets/_Game/Lighting/MovementLabVolumeProfile.asset`,`Assets/_Game/Materials/WeaponMetal.mat`,`Assets/_Game/Materials/WeaponDark.mat`,`Assets/_Game/Materials/WeaponAccent.mat`,`Assets/_Game/Materials/WeaponAccentCore.mat`,`Assets/_Game/Models/FpsRocketLauncher.fbx`,`Assets/_Game/Textures/FpsRocketLauncher_BaseColor.png`,`Assets/_Game/Textures/FpsRocketLauncher_Normal.png`,`Assets/_Game/Textures/FpsRocketLauncher_MetallicSmoothness.png`,`Assets/_Game/Textures/FpsRocketLauncher_Occlusion.png`,`Assets/_Game/Textures/FpsRocketLauncher_Emission.png`]; run_point=CP6 plus full EditMode; evidence=schema 1, top complete, internal ledger owner `workflow-orchestrator`, production-validator ledger executed, validated_sha=final SHA, current production probe, persisted launcher/import/material/UV/lighting contracts, manifest digest, lockReleaseProof
- revision-final-inputs: add `Assets/_Game/Models/{FpsShotgun,Shotgun}.fbx`; `Assets/_Game/Textures/Shotgun_{BaseColor,Normal,MetallicSmoothness,Occlusion,Emission}.png`; `Assets/_Game/Materials/Shotgun{Metal,Dark,Accent,AccentCore}.mat` to production validation input surface
- inspect: `git diff --check`; clean status; Blender proof/hashes; exact meta GUIDs; model-specific launcher/shotgun bindings; accepted Fast/Development image hashes; ProductionPrepare probe/manifest/comparator; XML; separate validator evidence
- invalidation: accepted fix/merge reruns local and all downstream checks; lighting edit invalidates Development/Production; launcher or shotgun edit invalidates weapon import through final; capture-only edit invalidates affected capture/review; proof-tool edit invalidates proofs using it

## Handoff

- residual risks: final visual quality requires user judgment; Fast deliberately cannot approve final baked lighting
- authority: execution orchestrator owns commits/reviews/integration; user owns VISUAL1, VISUAL2, and one-bake authorization

## Appendix A: Exact Generated Inventory

Definitions are finite literal expansion, not globs: `pair(p)` means exactly `p` and `p.meta`; `items(root,[a,b])` means exactly `pair(root/a)` and `pair(root/b)`.

- importer metas exactly: `Assets/_Game/Models/{LowPolyRocket,ArenaKit,LowPolyCharacter,FpsKickRig,FpsRocketLauncher,FpsShotgun,Shotgun}.fbx.meta`; `Assets/_Game/Textures/{RetroGrass,RetroGrass_Normal,RetroGrass_MetallicSmoothness,RetroGrass_Occlusion,RetroWall,RetroWall_Normal,RetroWall_MetallicSmoothness,RetroWall_Occlusion,RetroTrim,RetroTrim_Normal,RetroTrim_MetallicSmoothness,RetroTrim_Occlusion,RetroHazard,RetroHazard_Normal,RetroHazard_MetallicSmoothness,RetroHazard_Occlusion,RetroDetailNormal,RetroShield,RetroBall,RetroBall_Normal,RetroBall_MetallicSmoothness,RetroBall_Occlusion,RetroWeaponMetal,RetroWeaponMetal_Normal,RetroWeaponMetal_MetallicSmoothness,RetroWeaponMetal_Occlusion,RetroWeaponDark,RetroWeaponDark_Normal,RetroWeaponDark_MetallicSmoothness,RetroWeaponDark_Occlusion,RetroWeaponAccent,RetroWeaponAccent_Normal,RetroWeaponAccent_MetallicSmoothness,RetroWeaponAccent_Occlusion,RetroWeaponAccent_Emission,RetroRocket,RetroRocket_Normal,RetroRocket_MetallicSmoothness,RetroRocket_Occlusion,RetroRocket_Emission,RetroRocketGlow,RetroExplosion,RetroSmoke,RetroSunnySky,FpsRocketLauncher_BaseColor,FpsRocketLauncher_Normal,FpsRocketLauncher_MetallicSmoothness,FpsRocketLauncher_Occlusion,FpsRocketLauncher_Emission,Shotgun_BaseColor,Shotgun_Normal,Shotgun_MetallicSmoothness,Shotgun_Occlusion,Shotgun_Emission}.png.meta`
- prefab/controller pairs: `items(Assets/_Game/Prefabs,[Player.prefab,Ball.prefab,Rocket.prefab,ExplosionVfx.prefab,HealthPickup.prefab,ShotgunPickup.prefab,AmmoPickup.prefab])`; `items(Assets/_Game/Animations,[WorldCharacter.controller,FpsKick.controller])`
- material pairs: `items(Assets/_Game/Materials,[Floor.mat,Wall.mat,Trim.mat,Hazard.mat,Marking.mat,Ball.mat,Rocket.mat,RocketHot.mat,ProjectileGlow.mat,GoalFrame.mat,Shield.mat,ShieldBlue.mat,ShieldRed.mat,ArenaPrimary.mat,ArenaTrim.mat,ArenaHazard.mat,ArenaGlow.mat,BallSurface.physicMaterial,Explosion.mat,ExplosionAdditive.mat,ExplosionSparks.mat,Smoke.mat,ContainmentGridCeiling.mat,ContainmentGridLongWall.mat,ContainmentGridEndWall.mat,RetroSunnySky.mat,CharacterRed.mat,CharacterBlack.mat,CharacterCream.mat,CharacterEye.mat,WeaponMetal.mat,WeaponDark.mat,WeaponAccentCore.mat,WeaponAccent.mat,ShotgunMetal.mat,ShotgunDark.mat,ShotgunAccentCore.mat,ShotgunAccent.mat,TeamBlue.mat,TeamRed.mat,TeamBlueShield.mat,TeamRedShield.mat,TeamBlueTrail.mat,TeamRedTrail.mat,HealthPickup.mat,AmmoShell.mat])`
- generated pairs: `items(Assets/_Game/Generated,[BlueCircleCueMesh.asset,RedTriangleCueMesh.asset,MovementLabBuildManifest.json])`
- gameplay: `pair(Assets/_Game/Scenes/MovementLab.unity)`; `ProjectSettings/{EditorBuildSettings,DynamicsManager,TimeManager,TagManager}.asset`
- quality: `items(Assets/Settings,[PC_RPAsset.asset,PC_Renderer.asset,PC_Low_RPAsset.asset,PC_Low_Renderer.asset,PC_Iteration_RPAsset.asset,PC_Iteration_Renderer.asset])`; `ProjectSettings/{QualitySettings,ProjectSettings}.asset`
- lighting: `items(Assets/_Game/Lighting,[MovementLabLightingSettings.asset,MovementLabLightingSettings_Development.asset,MovementLabVolumeProfile.asset,MovementLabLightingManifest.json])`
- baked: `pair(Assets/_Game/Scenes/MovementLab/LightingData.asset)`; for each literal `i` in `[0,1,2,3]`, exactly `pair(Assets/_Game/Scenes/MovementLab/Lightmap-i_comp_dir.png)`, `pair(.../Lightmap-i_comp_light.exr)`, `pair(.../Lightmap-i_comp_shadowmask.png)`; for each literal `i` in `[0,1,2,3]`, exactly `pair(Assets/_Game/Scenes/MovementLab/ReflectionProbe-i.exr)`
