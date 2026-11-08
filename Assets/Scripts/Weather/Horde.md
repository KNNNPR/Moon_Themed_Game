# The Horde

Clearing wave 2 of Crazy Cosmonauts starts The Horde once and unlocks it in the ordinary surface forecast for the current game run. Starting/reaching wave 2 is insufficient: the existing wave-clear/reward path must complete. Subsequent waves do not reset its timer. Normal software price, per-wave payout and retry lockout are unchanged.

MoonWeatherSystem exposes Horde Duration (90 seconds), Maximum Horde Zombies (12), Horde Spawn Interval (2.5 seconds), Horde Contact Damage (8 battery units per contact interval) and Horde Ram Speed (5 world units/second). `weather horde` or `weather zombie` starts the event directly for debugging without requiring the PC game.

HordeWeatherEvent spawns the existing Zombie prefab around the rover on clear exterior ground. The sealed base is excluded. Zombies rise from the ground, pursue the rover and drain charge only on physical contact, with an attack cooldown. The living capsule stays solid but yields to the rover at every speed: low-speed contact pushes the suit back with a brief stagger, trying either side if a wall blocks the first direction. This also works while pursuit AI is stopped. The controller respects world collisions, and pushing does not deal lethal damage. Bone hit shapes are ignored while the suit is alive. Lethal ram detection includes the next physics step and crossed positions. Recovery/evacuation teleports are excluded.

A high-speed hit transfers about 95% of planar rover speed to the ragdoll (capped at 24 suit units/second), adds upward velocity and tumble, and removes a small share of rover momentum according to its mass. Existing non-blood impact particles remain. The initial ragdoll/chassis overlap is ignored for at least 0.15 seconds; physical corpse collision returns once all bones clear the chassis. A landed corpse can then be pushed again. Pistol hit zones and ordinary shot-death behavior remain, including during emergence.

The event uses warm red/amber exterior haze and a slowly pulsing point light above the rover; existing atmosphere rendering preserves the haze in both Retro and ASCII. On any change to another weather condition, living zombies and corpses stop attacking, disable collisions and sink for two seconds before removal. The glow fades out. Forecast name/effect and the assigned zombie icon appear on the weather monitor in English and Russian.

Runtime objects are named `Horde / Cosmonaut` and `Horde / amber surface glow`. No world scene, lighting asset or zombie prefab is replaced.
