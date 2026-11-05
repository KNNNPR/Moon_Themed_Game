# SCAV_BOT rally handling

W/S accelerate, brake and reverse; A/D steer along a car-like arc. Speed builds over
roughly one second, with a short coast when releasing the throttle. Normal steering
has some tyre slip; Space (gamepad south/A button) with A/D loosens rear grip and
boosts yaw to initiate a handbrake turn. Braking is mild during the slide so momentum
is preserved. The turn retains its entry direction past 90 degrees of sideslip.
Countersteer controls the slide and rear grip returns gradually after
releasing the handbrake. Extra speed-dependent downforce is applied only while the
bot is supported on its wheels.

Stability: wheel contacts steeper than `maxGroundSteepness` (wall faces) are not ground, so the bot
cannot drive up vertical surfaces; traction fades as the body tilts past `tractionTilt`. Drive and
brake forces act near the centre of mass (`pitchFreeDrive`, grip `rollFreeGrip`) so hard braking
does not tip the bot onto its nose, and suspension force is capped so a nose-dive cannot launch it.

There is no automatic recovery. R (gamepad north/Y button) rights the bot only when
it has overturned, slowed down near a real surface and has room to stand upright.
The feed shows the recovery key only when the action is available. The action stops
motion, preserves heading and lifts the bot slightly before it settles on its wheels.
Upright and airborne recovery is rejected. Repair-delivery robots do not auto-flip.

Mouse movement and the right stick orbit the camera independently of steering.
The parked camera retains the chosen angle. After 0.9 seconds without look input,
a moving bot gradually brings the view back behind it. The chase angle blends the
body heading with its direction of travel, making a drift easier to see. C, the middle
mouse button or a right-stick click resets the view. Keyboard camera reset, handbrake
and recovery can be rebound in Settings / Controls; older custom bindings are preserved.

The camera ignores rover roll/pitch and adds a small cornering bank of at most 1.25 degrees.
At maximum speed the field of view grows by 12 degrees, chase distance by 12%, and the
chase angle lowers by 3 degrees. These effects blend with speed and settle back at rest.
The camera clamps vertical look, pulls in at obstacles and smoothly extends again.
Tuner, pause and disconnected states suppress look input;
the first mouse delta after UI handoff is discarded to prevent a camera jump.

Tuning lives on `ScavengerDrive` and `ScavengerOrbitCamera`. The link installs both
at runtime. Drive speed, acceleration and braking are measured in bot lengths so the
existing robot scale and drive upgrades keep their proportions. `driftRearGrip` sets
the looseness of the rear axle, `handbrakeYawBoost` the extra turning response,
`handbrakeDeceleration` the loss of speed, `gripRecovery` the return of grip, and `speedDownforce`
the high-speed load on the suspension. Camera speed effects are controlled by
`speedFieldOfView`, `speedPullback`, `speedPitchDrop` and `maximumCornerBank`.

The start location contains only the `Robot Start` transform. Startup does not generate
a test floor, base pad, ramps or rocks. Existing authored terrain supplies the geometry;
the saved marker still controls spawning and evacuation.

`ScavengerHandlingValidation.Run` uses the actual bot mesh and PhysX in an isolated
project copy. It checks progressive acceleration, coast, reversing, drift initiation,
handbrake yaw/speed retention, a sustained U-turn, countersteer, grip recovery,
a low kerb, 50/100 Hz simulation, manual-only recovery,
camera orbit and drift tracking, 30/120 fps input, UI handoff and obstruction recovery.
It also checks speed-based FOV/distance/pitch, bounded banking and return to the rest view.
The live Operator scene checks connection, tuner, resume and disconnection.
`RunStartArea` checks that startup and connection do not generate a test surface.
Reports: `Logs/ScavengerHandling`.

Landscape prop collision is identified by meshes imported from `LandScape.fbx` and
`second_lan.fbx`. `LandscapeColliderSetup` adds missing static MeshColliders to the
loaded Operator scene with Undo support. All fence meshes/objects and objects with
the exact lowercase material `cargo` use BoxColliders instead. These fit the local
mesh bounds and follow object rotation/scale; existing authored boxes keep their
settings. Replaced fence/cargo shapes transfer material, enabled/trigger state and
contact/layer settings. Uppercase `CARGO` does not select an object for box conversion.
Other existing colliders (including disabled, trigger and compound setups), dynamic
props and the LAND terrain are preserved.
It marks the scene dirty without saving other edits. The manual command is
Tools / Moon Game / Level / Add Missing Landscape Colliders.
The `LandscapeCollisionSet` resource also supplies missing collision on scene load
in Play mode and builds. Repeated installs do not add duplicates.
`LandscapeColliderSetup.RunValidation` checks preservation of all existing collider
properties, fence/cargo boxes, migration from MeshColliders, case-sensitive material
selection, terrain exclusion, duplicate prevention and scene save/reload in an
isolated copy. Report: `Logs/LandscapeColliders/validation.txt`.
