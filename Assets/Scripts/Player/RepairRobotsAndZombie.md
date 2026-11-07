# Repair robots

`Assets/Prefabs/RepairRobots/FinderBot` and `SkateBot` contain the complete robot prefab, its repair definition, and its own `Parts`, `Meshes`, and `Materials` folders. The five part names are unchanged: Wheel, Battery, Headlights, Back_Body_Robot, Window_Robot. Each family has different prefab references, so parts cannot be installed on another robot family. HUD labels include the family name. Wireframe previews include view-dependent silhouettes for smooth panels and distribute their line budget between meshes, so dense wheel rims cannot erase other parts.

`Assets/Resources/RepairRobotCatalog.asset` adds these robots to the existing SCAV_BOT repair dropper without modifying the scene. Six main orders cycle through the three families; endless orders choose a family randomly. Repair bots are delivered through orders; the old F/manual-spawn action is retired and hidden from Controls without changing saved binding indices. Rewards, the completed-order counter, salvage progression, pipe delivery, and repair tools use the selected robot's parts.

In the developer console, `parts` lists all IDs. Examples: `spawn finderbot_wheel`, `deliver skatebot_battery 2`. Original IDs such as `wheel` still refer to SCAV_BOT parts.

To rebuild the generated assets after changing the FBX models, use Tools > Moon Game > Repair > Rebuild FinderBot, SkateBot and Zombie. Rebuilding replaces generated prefabs and meshes; save any custom changes separately first.

# Zombie

`Assets/Resources/Zombie.prefab` contains the complete Cosmonaut model, a CharacterController, procedural movement and pose, and ten bone-following hit zones. Use `spawn zombie` in the F3+F4 console. It requires a clear floor near the human operator; it does not spawn enemies automatically.

The zombie detects a visible human, follows the last seen position, steers around nearby obstacles, and strikes/shoves at close range. It has no dependency on a baked NavMesh. It does not introduce a player health or death system.

The prefab's ZombieActor component exposes health, speed, detection/attack range, attack interval, and corpse lifetime. Each ZombieHitZone exposes its body part and damage multiplier. Default health is 100; PlayerPistol bulletDamage is 34. Head x3, torso x1, arms x0.55, legs x0.7. Leg hits slow movement. Hits briefly interrupt walking and produce a directional flinch; reactions fade over 0.65 seconds. Death stops the AI and locomotion capsule, enables a ten-body physical ragdoll with constrained joints, transfers movement and the shot impulse to the struck bone, and removes the corpse after 35 seconds. Bone colliders continue to collide with the environment and receive physical impulses from later shots. The ZombieRagdoll component exposes deathImpulse; bone Rigidbody masses and CharacterJoint limits are editable in the prefab. Impact particles are pale suit debris and dust, without blood. Gunshot audio stays unchanged.

# Lamps

BreakableLamp discovers the existing emissive `lamp` glass in each loaded scene and adds a thin hit collider. Each nearby point light is assigned to one glass by position, including lights parented under a different lamp. A pistol shot hides the struck glass, emits pale glass fragments, and switches off its light. Other lamps stay on. Metal housing is retained; this is runtime damage and resets when the scene reloads.
