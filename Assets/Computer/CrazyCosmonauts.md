# Crazy Cosmonauts / Безумные Космонавты

Buy/download the game in SHOP for 10 credits. The zombie icon then appears on the PC desktop. Installation lasts for the current game run, like the other paid applications and the credit balance; closing and reopening the PC retains it. A new run starts without the application, even if an older version saved an installation in PlayerPrefs. The game runs through a dedicated camera/render texture inside the PC application; the operator stays at the computer.

Controls: movement uses the saved movement bindings; Shift runs, Space jumps, LMB fires, RMB aims, R reloads the eight-round pistol. Ammo magazines are unlimited. Zombies have the existing body-part damage, hit reactions and death ragdolls. Their attacks remove 20 HP from the game's 100 HP, without damaging or moving the operator in the base.

Wave 1 has three zombies, each next wave adds two (up to 40 per wave, eight alive at once). After all pending and living enemies are gone, the game pays exactly 3 credits and waits for Continue. Health carries between waves. The application closes without a penalty; dying blocks a new run for 180 real seconds. The UTC deadline is saved and survives closing the app, pausing, leaving the computer, and restarting the game.

Escape first pauses the arcade game. Another Escape closes the application; the next returns from the PC to the operator. The pause menu offers Resume and Back to desktop. World weather/ASCII passes do not process the game's camera. The application adds thin CRT scanlines and a slowly rolling scan band; health/ammo labels remain clear.

Configuration: Assets/Resources/CrazyCosmonautsContent.asset contains price, reward, retry duration, health, enemy damage, wave growth, spawn rate and referenced models/icons. Assets/Computer/CrazyCosmonauts/ZombieShooter.prefab uses Assets/Models/zombie shooter.fbx, exact mesh colliders, connected spawn markers and baked navigation. Use Tools > Moon Game > Crazy Cosmonauts > Build Arena and Application to rebuild generated arena assets after updating the source model. Custom changes to generated prefab spawn markers should be backed up before rebuilding.

The shop card shows the complete Cosmonaut model in the same rotating white wireframe style as other shop items. Its full skin is baked once into readable geometry without changing the source FBX or the headless first-person body. The desktop zombie and health HUD icons derive from the supplied SVGs, with light fills for the dark PC interface. Original SVG files are unchanged. The start screen contains the title and buttons; the introductory equipment paragraph is hidden.

# Repair fasteners

Unfinished bolts and screws have a persistent crisp green contour while viewing the repair area. A single shared contour mask covers unfinished fasteners, including ones outside the crosshair. A brighter hover contour additionally identifies the targeted unfinished fastener. Both use live meshes and follow tightening; the persistent contour remains when looking away and hides when opening settings or using the PC. Once the last tightening turn finishes, both outlines disappear. Backing the fastener out restores its highlight; wrench targeting still supports unscrewing a tight fastener.
