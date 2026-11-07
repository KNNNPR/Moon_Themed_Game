# Salvage routes

The editable layout is **Assets/Resources/SalvageSpawnLayout.prefab**. The Operator hierarchy installs it as **Salvage Spawn Points**, with twelve four-part routes plus **13 SHIP - interior hold**, containing a battery and a wheel on the internal Cube.064 cargo floor (50 points total). The ship points use its existing MeshCollider, require no BoxCollider, and remain optional loot behind the ship gates. Each point exposes its Part and route notes in the Inspector. Scene-instance position overrides are respected. Runtime instantiates the prefab if the scene has not been saved with an instance yet.

Each successful repair advances the requested supply route. The six story jobs request: 1 — access road (41–61m), 2 — roadside wrecks (93–115m), 3 — northern junction (126–143m), 4 — western relay detour (192–214m), 5 — outpost approach (269–324m), 6 — eastern service road (384–440m). Distances are driving routes from the base, not straight-line HUD readings. Earlier points are physically cleared and cannot refill; this changes actual loot, rather than only hiding HUD markers. The selected route supplies the missing part types and quantities, including extra wheels when required. Cargo, parts in the bay and parts travelling through the tube count toward supply and are preserved.

The selected tier is capped by normal-weather endurance: capacity / drain, adjusted for the purchased speed upgrade, with a 7% reserve against the route's measured loop time. Stock power supports the first three routes. One battery upgrade supports the western detour; one battery plus one regulator upgrade supports the outpost approach; a second battery or regulator upgrade enables the eastern road. Those three power upgrades cost 38 credits, affordable from the initial balance and the first five story rewards. Buying other items may delay these routes. Upgrades take effect on route selection when the next job is accepted, so existing targets do not move mid-run. Aborted orders do not advance distance. Endless orders retain the farthest affordable safe tier.

The six story repairs pay 15, 20, 25, 30, 40 and 50 credits (180 total). The displayed reward and actual payment use the same fixed amount for each story order. Endless repairs retain the existing 2 credits per missing part. Each successful order pays once; aborting does not pay or advance progression.

Story orders unlock sequentially. Initially only order 1 is visible; completing it reveals and selects order 2, continuing through order 6. Completed rows remain visible and crossed out. Future rows, previews and acceptance are unavailable until all previous story repairs are complete. Aborting leaves the same order available for retry. Endless mode unlocks after all six story orders.

Points expose **Progression Tier**, **Estimated Loop Seconds** and **Order Route** in the Inspector. Only routes safe without unopened shortcuts carry guaranteed order supplies; gated and bridge caches remain optional bonuses. Required markers prefer the selected safe supply route. Four-point routes can refill a collected point with the next missing part after the rover has moved at least 4m away, supporting orders larger than the four-slot starting cargo capacity.

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

Station 2 recharges the rover. Cargo still returns to the main base for unloading through the scrap tube. There are no artificial level locks on the caches: gates, bridge geometry and the rover's current power budget determine access. Collection and respawning use these authored points instead of random map positions. Re-running setup does not duplicate live salvage.

**Tools > Moon Game > Salvage > Install Balanced Spawn Layout** adds the prefab to Operator. **Rebuild Default Spawn Prefab** restores the default design; it intentionally replaces prefab edits. The existing mouse-placement tool can add points beneath the layout root. After changing level geometry or moving distant caches, re-check routes and battery reserve.

The far southern and eastern routes are populated at startup, even on the stock rover. HUD order markers prefer required supplies on the active order route, then other live supplies; the marker list is not the full list of live spawns. **Tools > Moon Game > Salvage > Prepare Order Progression** adds the default progression metadata without moving points. Completed Orders on RepairMessagesApp counts successful repairs, independently of the displayed inbox ID; aborted deliveries do not advance it.
