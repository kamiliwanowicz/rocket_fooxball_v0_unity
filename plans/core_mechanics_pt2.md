# Core Mechanics Part 2

## Automated MovementLab Smoke Test [IDEA]

Goal: add deterministic Unity Play Mode smoke coverage for integrated core mechanics.

Proposed flow:

1. Load `Assets/_Game/Scenes/MovementLab.unity`.
2. Resolve player, ball, launcher, goals, match controller, and HUD by component type.
3. Drive gameplay intent through `PlayerInputReader` test seam. Never poll legacy input or inject device-specific events.
4. Advance fixed steps with bounded timeouts.
5. Fail on missing components, violated invariants, timeout, or unexpected error/exception log.

Required scenarios:

- scene wiring: required components exist; player and ball start inside arena
- movement: forward intent changes player position; horizontal speed stays under hard cap
- jump: grounded jump raises player; one double-jump consumed per grounded sequence; held jump does not repeat
- rocket: fire creates projectile; impact removes projectile; direct and nearby blasts add player/ball impulse
- kick: valid fresh press near ball changes ball velocity; cooldown blocks immediate repeat
- goal: ball center crossing goal plane increments opposing score once; celebration freezes gameplay; reset restores player, ball, score-state inputs, and active-rocket count
- containment: player and ball remain inside failsafe bounds during bounded simulation
- health: no unexpected Unity error or exception logs

Assertion policy:

- Assert invariants, bounds, state transitions, and event counts.
- Avoid exact positions, exact physics trajectories, subjective feel, visual readability, and rendered HUD appearance.
- Keep target-device FPS measurement optional and non-blocking.
- Seed randomness. Use `Time.fixedDeltaTime`; preserve `60 Hz` physics contract.

Suggested location: `Assets/_Game/Tests/PlayMode/MovementLabSmokeTests.cs` with separate Play Mode test asmdef referencing `RocketFooxball.Runtime`.

Suggested batch entry: Unity Test Framework Play Mode run producing NUnit XML and Editor log. Test implementation requires explicit test-strategy approval because current repository policy defers automated tests.
