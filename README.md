# AIM Player Ammo Hit Recorder

## v0.1.10 — Experimental

Records accepted impacts from ammunition fired by the player-controlled vehicle. Each original shot produces one console message containing a staged contact table and one local CSV record. Shots with no accepted contact do not produce a record.

**Experimental build. May cause stuttering or reduced game performance. Full gameplay compatibility has not been verified.**

### Features

- First-contact range, impact angle and speed.
- Observed native armor decisions: perforated, not perforated, ricochet or unconfirmed.
- Native capability before comparison, contact resistance and remaining capability when available.
- Chinese console output by default; English and automatic language selection are supported.
- Original component names and readings retained in CSV.

Readings are observations of the game's native decisions, not a guaranteed maximum penetration measurement or an independent physical armor model. Missing observations remain unknown. Terrain contacts are not armor perforations.

### Installation

Audited against GHPC `0.1.0-alpha+20261002` with MelonLoader `0.7.3` (`net35`). Other game builds have not been verified. Existing MelonLoader/Harmony is required;

1. Close the game normally.
2. Back up any existing `AIMAmmoHitRecorder.dll` and place this DLL in `Bin/Mods`.
3. Keep only one copy of this recorder in the active Mods folder.
4. Launch the game. Results appear in the MelonLoader console/log and a local CSV file; the console startup message shows the CSV path.

### Language

After the recorder has initialized once, close the game and edit `Bin/UserData/MelonPreferences.cfg`:

```toml
[AIMAmmoHitRecorder]
Language = "zh"
```

Use `zh` for Chinese (default), `en` for English, or `auto` to follow the current UI culture (Chinese for `zh`, English otherwise). Restart the game after editing. A previously saved language choice is respected. Language selection affects the console only; CSV columns and existing Chinese detail labels remain unchanged.

### File verification

`AIMAmmoHitRecorder.dll` SHA256:

```text
27FCAD987E9ABED3812CE641B20BE58DF703E5D192BB5C12B4954925D54C2CBF
```
