# bioMass

**bioMass** is a 3D reverse-horror game built in Unity 6.6 (6000.6.3f1). The player controls the *intent* of a sentient biological mass rather than directly animating limbs. Locomotion is generated from a small physics simulation, surface sensing, adhesion, asynchronous anchor tendrils, momentum and deformation.

## Current milestone — Movement Foundation v0.1.4.1

Implemented:

- Camera-relative keyboard/gamepad movement intent
- Connected multi-Rigidbody biomass body
- Centre-of-mass tracking
- Surface sensing using sphere/ray queries
- Floor / wall / ceiling adhesion without separate climb modes
- Automatic locomotion-tentacle state machines
- Scored anchor selection biased toward desired movement
- Physics pulling from anchors
- Organic body lag through spring connections
- Custom gravity when detached from surfaces
- Smoothed follow camera based on biomass centre
- Toggleable debug overlays / gizmos
- Editor command that creates a repeatable movement test chamber

### v0.1.2 play-test fixes

- Makes `LineRenderer` mandatory on locomotion tendrils and creates it before the tentacle script.
- Enlarges surface-query buffers so the creature's own colliders cannot crowd out floor and wall hits.
- Makes tendril anchor searches look past the creature's own colliders instead of failing on the first self-hit.
- Makes Rigidbody access lazy-safe so initialization no longer depends on Unity's Awake ordering.
- Makes debug materials match the actually active render pipeline, preventing magenta geometry when URP is installed but not active.
- Adds raw movement input to the debug HUD.

The visible node spheres and LineRenderer tendrils are development/debug representations only. They are intentionally isolated from the simulation architecture so later procedural flesh and spline-mesh tendrils can replace them without rewriting locomotion.

## Open it

1. Install Unity 6000.6.3f1 in Unity Hub.
2. Add this folder as an existing project.
3. Allow Unity to resolve packages. The project pins `com.unity.inputsystem` to **1.20.0**.
4. Enable the new Input System if Unity prompts, then restart the editor.
5. In Unity choose **bioMass > Build / Rebuild Movement Lab**.
6. Enter Play Mode and click the Game view once before testing keyboard input.

## Controls

- `WASD` — movement intent
- Gamepad left stick — movement intent
- Mouse / gamepad right stick — orbit camera
- `F1` — toggle debug visuals
- `R` — reset creature to the lab spawn

The debug HUD shows raw movement input. Pressing W should display approximately `Input: 0.00, 1.00`.

## Architecture rule

Simulation and presentation stay separate. A limited number of Rigidbody nodes and important tendrils perform gameplay physics. Later visual flesh, veins, secondary strands and mesh deformation will be driven from those nodes rather than becoming hundreds of extra physics bodies.

### v0.1.3 traversal tuning

- Raises Stage 1 target speed and acceleration so movement feels more predatory.
- Adds explicit floor → wall → ceiling transition detection instead of relying only on an averaged nearby-surface normal.
- Makes locomotion tendrils prefer useful surfaces ahead during a transition and release anchors left behind on the old surface.
- Tightens spring distances and adds a compacting force around the core so Stage 1 stays clustered while still gaining stretch at speed.
- Starts the debug biomass nodes in a tighter formation.
- Adds a `Traversal: stable/transitioning` line to the movement HUD.

After updating these scripts, run **bioMass > Build / Rebuild Movement Lab** again so the scene picks up the new defaults and tighter spawn layout.


### v0.1.4 tendrils, connective tissue and recovery

- Raises Stage 1 locomotion tendrils to 10 and gives each one its own territory around the body, so anchors distribute around the biomass instead of bunching mainly in the movement direction.
- Gives locomotion tendrils multiple deterministic curve/width profiles so they do not all render with the same shape.
- Adds visible connective tendrils between physical biomass nodes. These visual strands follow the same node pairs used by the spring cohesion network.
- Adds a soft leash that aggressively pulls stray biomass back before it becomes a problem.
- If a non-core node remains beyond the hard break-off distance, it detaches, vanishes briefly, and reforms beside the core rather than leaving the player snagged on geometry.
- Locomotion tendrils attached to a reforming node are released automatically.
- Inbound springs are temporarily disabled during reformation to prevent the rest of the creature being yanked by the recovering node.
- Safe reform placement probes nearby space around the core and avoids reforming directly inside environment geometry when possible.
- The debug HUD now reports how many nodes are currently reforming.

After updating, run **bioMass > Build / Rebuild Movement Lab** again so the generated test scene receives the latest serialized defaults.


### v0.1.4.1 compile fix

- Fixes Unity 6.6 compilation errors caused by treating `SpringJoint` as if it had an `enabled` property.
- Biomass reformation now suspends spring forces by caching and temporarily neutralising each affected spring's force/range settings.
- The exact spring settings are restored when the detached biomass node reforms.
