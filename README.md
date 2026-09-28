# bioMass

**bioMass** is a 3D reverse-horror game built in Unity 6.6 (6000.6.3f1). The player controls the *intent* of a sentient biological mass rather than directly animating limbs. Locomotion is generated from a small physics simulation, surface sensing, adhesion, asynchronous anchor tendrils, momentum and deformation.

## Current milestone — Movement Foundation v0.1.1

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

The visible node spheres and LineRenderer tendrils are development/debug representations only. They are intentionally isolated from the simulation architecture so later procedural flesh and spline-mesh tendrils can replace them without rewriting locomotion.

## Open it

1. Install Unity 6000.6.3f1 in Unity Hub.
2. Add this folder as an existing project.
3. Allow Unity to resolve packages. The project pins `com.unity.inputsystem` to **1.20.0**, the Unity 6000.6-compatible release.
4. In Unity choose **bioMass > Build / Rebuild Movement Lab**.
5. Open `Assets/BioMass/Scenes/MovementLab.unity` if it is not opened automatically.
6. Enter Play Mode.

## Controls

- `WASD` — movement intent
- Gamepad left stick — movement intent
- Mouse / gamepad right stick — orbit camera
- `F1` — toggle debug visuals
- `R` — reset creature to the lab spawn

## Architecture rule

Simulation and presentation stay separate. A limited number of Rigidbody nodes and important tendrils perform gameplay physics. Later visual flesh, veins, secondary strands and mesh deformation will be driven from those nodes rather than becoming hundreds of extra physics bodies.
