# Minimal settings terminal

Escape opens a paused, full-screen black menu with a compact text column, inspired by
the station login screen. Home contains master volume, Controls, Resume and Exit Game.
PT Mono is rendered upright, without synthetic bold, italics or text shadows. The
station caption is 14 px and the footer 13 px at the reference resolution for readability.
There are no decorative frames or mini-games. The master volume controls all sounds,
including the spatial radio. Exit Game has a text-only confirmation; the build quits
to desktop and the Editor exits Play Mode.

Controls opens the scrollable key list. Select a key, then press its replacement.
Conflicting assignments are rejected. Escape cancels capture, then returns to Home,
then resumes the game. Escape always remains a back/menu fallback, even after changing
Open Settings. Restore Defaults resets all assignments. The current bindings are saved
in PlayerPrefs under `controls.<command>`.

Bindings apply to operator/robot movement, interaction, throwing, repair, visual style,
radio controls, satellite selection and the robot tuner. Mouse look/gamepad movement
retain their existing Input Actions.

Escape first returns from an active PC, weather display, note or robot connection.
A later press opens settings. The resume frame still blocks gameplay input so the
same click/key cannot reactivate another interaction or unlock the cursor.

UI styles: `SettingsMenu.uss`. Font, theme and style:
`Assets/Resources/SettingsMenuAssets.asset`. The native UI Toolkit document is created
at runtime. `SettingsMenuSetup.ApplyAndValidate` checks menu navigation, master volume,
bindings, pause and back priorities in an isolated Unity project.
Reports and screenshots: `Logs/SettingsMenuCaptures`.
