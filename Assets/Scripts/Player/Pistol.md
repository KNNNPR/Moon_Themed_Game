# Operator pistol

Open the developer console with F3 + F4 and enter `pistol`. The command adds one pistol to the operator's toolbox for this play session. Repeating it does not duplicate the weapon. It is not part of the normal starting inventory.

Hold MMB, move the mouse vertically to the far PISTOL cradle, then release MMB to take it. LMB fires one shot per click, holding RMB aims through the sights, R reloads, and G drops it. With the pistol out, hold Q/E to lean left/right, including hip fire and reloading; RMB is not required. When the interaction binding shares a lean key, use Alt plus that key (Alt+E by default) to interact with the pistol in hand. Without the pistol, normal E interaction is unchanged. Reload and lean can be rebound in Settings; R is shared with rover recovery because the two actions have separate contexts.

The magazine contains 8 rounds. Spare magazines are unlimited for this console-granted weapon. Default reloads take 2.8 seconds with rounds remaining, or 3.05 seconds from empty. Runtime duration follows the assigned clip length and Reload Playback Speed. Changing tools or dropping the pistol cancels an unfinished reload without granting ammunition. Pause/console suspend the action, and screens/toolbox block shooting.

## Editing the reload in Unity

1. Open **Assets/Resources/PlayerPistol.prefab** in Prefab Mode. This is the actual prefab used by the console command, so saved changes apply to new instances.
2. Select its root and open **Window > Animation > Animation**. Choose **PistolReload** or **PistolReloadEmpty** from the clip list. The editable files are in **Assets/Weapons/Animations**.
3. Animate **Reload Motion** for the whole gun, **mag** for the magazine, and **Slide** for the slide. **Right grip** controls the right palm; **Magazine hand target**, beneath mag, controls where the left palm meets the magazine. Hands follow these transforms through the existing player IK.
4. Save the clip and prefab. The empty reload holds the slide back before releasing it; the tactical reload leaves it closed. Both clips return all animated transforms to their initial pose. Starting a reload smoothly exits aiming.

The prefab's Animation component provides edit-time preview. During gameplay the pistol samples the selected clip at its own reload clock, so animation and ammunition cannot get out of sync. Use the component's Magazine Out Point / Magazine In Point / Slide Point for normalized audio cue times; clip Animation Events are not the gameplay timing source. Sound slots remain empty. **Tools > Moon Game > Weapons > Prepare Editable Pistol Reload** upgrades an existing prefab without replacing existing clips.

Camera kick, muzzle kick and recoil return time are exposed in the prefab's Recoil section. View pitch has lasting recoil that the player compensates with the mouse; weapon rotation and camera roll settle smoothly. Lean uses a sphere cast to limit camera displacement near walls, and clears when stowing the pistol or focusing a terminal.

The supplied `Assets/Models/makarov.fbx` and `Assets/Textures/pistol.png` are used in `Assets/Resources/PlayerPistol.prefab`. The magazine and upper slide animate separately. The prefab exposes muzzle, sight and hand sockets, firing interval, range and effect materials for editing in Unity. The rebuild command under **Tools > Moon Game > Weapons > Rebuild Pistol Prefab** recreates the defaults, so it replaces prefab tuning.

Shots use a camera aim ray and a second ray from the actual muzzle, including obstruction between the eye and muzzle. Recoil changes the player's view angles and can be compensated with the mouse. The weapon has additional recoil recovery, turn lag, movement sway, a slide lock on an empty magazine, muzzle flash/smoke, spent casings and surface impacts. Dynamic rigidbodies receive a physical impulse. No enemy health/damage system is added by this feature. The pistol fires and aims with the right hand; the left hand only assists with swapping the magazine. Wrist orientation is fixed to the grip, with a lowered elbow and an arm anchor that follows the grip when crouching or looking upward. All audio slots are empty: assign Shot Sound, Empty Sound, Magazine Out Sound, Magazine In Sound and Slide Sound on the PlayerPistol component of the prefab. Assigned clips follow the existing master-volume setting.

Gun and part models pass through the existing visual shader. The ammunition/control HUD is drawn after the scene effects and remains readable in Retro and ASCII.

## Hits on handheld props

Shots destroy ordinary and Suspicious Apples in a burst of red skin and pale flesh mesh particles. Breaking fruit does not apply the edible effect; fragments fade and clean themselves up. Existing apple prefabs inherit this through EdibleProp, whose Shot Particle Count and Lifetime tune the burst.

Radio hits advance to the next station and start playback if the radio was off. On Assets/Prefabs/Radio.prefab, HeldProp's Radio Track is the first station; Radio Stations contains additional AudioClips. Empty/duplicate entries are skipped, and the playlist wraps. With only the existing radio11 clip, a hit restarts that clip. Arrow controls use the same playlist; spatial audio and master volume still apply.
