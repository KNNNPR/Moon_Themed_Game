# Salvage routes

The editable layout is **Assets/Resources/SalvageSpawnLayout.prefab**. The Operator hierarchy installs it as **Salvage Spawn Points**. There are **74 fixed positions**: the original 50 plus six four-point alternative groups (14–19). Each story distance tier now has eight safe positions spread across roads, side paths and wrecks. **13 SHIP - interior hold** retains its battery and wheel on the internal Cube.064 cargo floor. The ship uses its existing MeshCollider, without a BoxCollider. Scene-instance position overrides are respected; runtime instantiates the prefab if no layout instance exists.

These are candidate positions, not ambient loot. Without an active repair order, there are no outdoor salvage parts. Each accepted order creates a stable, randomized supply plan at its current distance and power tier. A spacing score prefers different areas, discourages the previous order's positions and avoids immediately refilling a collected position. Required part types are shuffled independently of their default Inspector part. The plan supplies exactly the unstocked missing quantities for the current robot family and prioritizes its assigned pieces on the HUD. Parts in cargo, in the repair bay and in transit through the tube count toward supply, so collecting a part does not spawn a replacement. A purchase does not relocate an existing target. When a tier spans several branches, use separate return trips rather than assuming its whole spread fits one battery.

The stock cargo bay holds **2 parts**. Its three upgrades increase capacity to **3, 4 and 5**, for the existing prices of 8, 15 and 25 credits. A third part remains on the ground until there is room; unloading still sends cargo through the scrap tube. Completing or aborting an order removes its remaining outdoor pieces immediately. Collected cargo, tube deliveries and loose repair-bay parts are preserved. The final dismantling order does not spawn outdoor salvage, and its removed modules remain ordinary repair-bay parts.

Manipulator caches are eligible only after buying the arm; purchasing it does not populate the map. With the arm installed, an accepted order has a 50% chance to put **at most one** required part in a harder cache: outpost yards, gated depots, beyond the bridge, or inside the ship. The cache's Minimum Order Tier and Estimated Loop Seconds must fit the current distance tier and the power budget captured when accepting the order; otherwise a safe point is used. This never raises the mandatory distance tier. Gates, the bridge and the ship lift still have to be operated normally. Pickup checks height and a clear line to the part, so parts cannot be collected through a fence or from the surface above the ship hold.

Each successful repair advances the requested supply distance. Including alternatives, the six safe tiers cover roughly 40–75m, 85–115m, 115–150m, 170–220m, 255–330m and 355–445m of driving from the base. Distances are driving routes, not straight-line HUD readings. Earlier tiers are physically cleared and cannot refill; this changes actual loot, rather than only hiding HUD markers. Each selected set supplies the missing part types and quantities, including extra wheels when required. Cargo, parts in the bay and parts travelling through the tube count toward supply and are preserved.

The selected tier is capped by normal-weather endurance: capacity / drain, adjusted for the purchased speed upgrade, with a 7% reserve against the route's measured loop time. Stock power supports the first three routes. One battery upgrade supports the western detour; one battery plus one regulator upgrade supports the outpost approach; a second battery or regulator upgrade enables the eastern road. Those three power upgrades cost 38 credits, affordable from the initial balance and the first five story rewards. Buying other items may delay these routes. Upgrades take effect on route selection when the next job is accepted, so existing targets do not move mid-run. Aborted orders do not advance distance. Endless orders retain the farthest affordable safe tier.

The six story repairs pay 15, 20, 25, 30, 40 and 50 credits (180 total). The displayed reward and actual payment use the same fixed amount for each story order. Endless repairs retain the existing 2 credits per missing part. Each successful order pays once; aborting does not pay or advance progression.

Story orders unlock sequentially. Initially only order 1 is visible; completing it reveals and selects order 2, continuing through the six ordinary repair jobs. The seventh order, SCAV_BOT_AURINKO, reverses the repair process and needs no salvage; the rover is absent during that job. Endless mode unlocks after AURINKO is dismantled and collected by the crane. See **Assets/Scripts/Computer/StoryFinale.md**. Future orders remain unavailable until predecessors are complete; aborting leaves the same order available for retry.

Points expose **Progression Tier**, **Estimated Loop Seconds**, **Order Route**, **Requires Manipulator** and **Minimum Order Tier** in the Inspector. Safe routes provide the fallback; a manipulable cache can replace one target when eligible. Only outstanding required quantities can respawn after losing a piece; unused spaced positions are preferred. SalvageField exposes **Target Spacing** (18m preference) and **Manipulator Cache Chance** (0.5).

Routes were measured against the current Operator colliders with a 1.1m navigation radius, both with gates shut and with gates open / bridge lowered. The planning speed is 4.55m/s (32% of the stock top speed), plus 14 seconds per four-part loop for pickup and manoeuvring. Stock energy is 100 units at 1 unit/second. These are conservative normal-weather estimates, not speedrun targets; cold and radiation cost additional energy.

| Group | Anchor | Full loop estimate | Progression |
|---|---|---:|---|
| 01 Access road | Main base | 42s | Stock battery; about 58% reserve |
| 02 Roadside wrecks | Main base | 68s | Stock battery; about 32% reserve |
| 03 Northern junction | Main base | 79s | Stock battery; about 21% reserve; can split into shorter trips |
| 04 Relay detour | Main base | 108s | Capacity or regulator upgrade |
| 05 Outpost approach | Main base / station 2 | 159s / 64s | A transport route toward the second charger, then local loops |
| 06 Charging yard | Station 2 | 33s | Outpost gate / manipulator unlock |
| 07 Southern rubble | Station 2 | 50s | Short collection trips after opening the outpost |
| 08 Far-side cache | Station 2 | 96s | Lowered bridge; power upgrades recommended for reserve |
| 09 Eastern service road | Main base | 208s | Larger battery plus regulator |
| 10 Gated eastern depot | Main base | 230s | Eastern gate; larger battery plus regulator |
| 11 Far southern basin | Station 2 | 218-251m outward | Lowered bridge, recharge, larger battery and regulator |
| 12 Eastern perimeter | Main base | 456-536m outward | Eastern gate, advanced power upgrades |

Station 2 recharges the rover. Cargo still returns to the main base for unloading through the scrap tube. Physical mechanisms determine access, while order selection also checks the arm, distance tier and power budget. Collection and respawning use authored points instead of random map positions. Re-running setup does not duplicate live salvage.

**Tools > Moon Game > Salvage > Install Balanced Spawn Layout** adds the prefab to Operator. **Rebuild Default Spawn Prefab** restores the default design; it intentionally replaces prefab edits. The existing mouse-placement tool can add points beneath the layout root. After changing level geometry or moving distant caches, re-check routes and battery reserve.

**Tools > Moon Game > Salvage > Add Varied Order Spawns** adds groups 14–19 without moving the existing 50 positions, then annotates all route metadata. Rebuilding the default prefab also includes these groups. Alternative locations were sampled from the closed-gate navigation survey; their ground is checked again when the tool runs.

Startup, timed refreshes and manipulator purchases never populate unused points. All live outdoor parts belong to the accepted order, and its HUD markers identify those parts. Choosing a new order randomizes locations within the same eligible difficulty tier; its plan stays fixed while travelling. **Tools > Moon Game > Salvage > Prepare Order Progression** adds default progression metadata without moving points. Completed Orders on RepairMessagesApp counts successful repairs, independently of the displayed inbox ID; aborted deliveries do not advance it.
