# pid-unity

An open-source engine reimplementation of **Pathways Into Darkness**
(Bungie Software, 1993, Macintosh), built in Unity.

> ## ?? Very early work in progress
>
> This does not run yet. There is no playable build, no release, and no
> meaningful functionality. The repository exists so the work is public from
> the start.
>
> Nothing here is stable. Architecture, data schema, and scene structure will
> all change without warning. Do not build anything on top of this.

---

## What this is

Pathways Into Darkness was Bungie's 1993 first-person adventure — the game that
led directly to Marathon and, eventually, to Halo. Its source code was never
released.

The data formats have been reverse-engineered from the shipped binaries. That
work lives in a separate repository, **[pid-re](../../pid-re)**, which contains
the format specification, the parsers, and the JSON exports this project
consumes.

This repository is only the engine. It knows nothing about resource forks,
compressed sprites, or 68k Macintosh file formats — it reads JSON and renders
a world.

**No game data is included here.** You need your own copy of Pathways Into
Darkness. The original has been freely available since Bungie released it as
freeware.

---

## Status

| Area | State |
|---|---|
| Level geometry import | in progress |
| First-person movement and collision | in progress |
| Doors and level transitions | not started |
| Items and inventory | not started |
| Corpse dialogue | not started |
| Monsters and combat | not started |
| Textures | blocked — sprite compression unsolved |
| Audio | data ready, not wired up |
| Save / load | blocked — format partially understood |

### Current milestone

Walk Ground Floor. Import the level, extrude walls and floors, and move through
it in first person with collision. Flat shading, no textures, nothing else.

The test is simple: spawn at the south end of the level, walk north up the
corridor, and reach every room. If that works, the format research is validated
by the only measure that really counts.

---

## What's known and what isn't

The map format is fully decoded and validated — geometry, walls, collision
semantics, level connections, item placement, corpses and their dialogue.
All 25 levels parse and render correctly.

The art is not. Pathways stores its sprites and textures in a compressed
format that has resisted every decoder attempted so far. Until that falls,
everything renders in flat colour.

This turns out not to block much. Movement, collision, doors, transitions,
items, and dialogue can all be built and tested against untextured geometry,
and textures become a swap in a single importer when they're available.

Full detail — including the parts of the older fan documentation that turned
out to be wrong — is in `pid-re/docs/FORMAT.md`.

---

## Setup

- **Unity 6.3 LTS** (6000.3.x), Universal Render Pipeline
- Keep the project path short. Windows' 260-character path limit interacts
  badly with Unity's deep package directories.

You will also need level data exported from `pid-re`:

```bash
python tools/export_level.py        # exports all 25 levels as JSON
```

Place the output where the importer expects it (see `Assets/` — this will be
documented properly once it stabilises).

---

## Credits

The format research this engine depends on builds on work done by the Pathways
community, much of it in the late 1990s with a hex editor and a lot of
patience: **Loren Petrich**, **Ben Semmler**, **Chuck Gray**, **Alan Earhart**,
and **Alain Roy**. Details and specifics are credited in `pid-re`.

The approach — an open engine that requires the player's own game files — is
modelled on **Daggerfall Unity**.

Pathways Into Darkness is © Bungie Software. This repository contains no
original game code or assets.