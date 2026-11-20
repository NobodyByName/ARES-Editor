# ARES-Editor

Desktop tool for ARES server operators. It shows an ARES server's loot containers and loose item sockets as
points on the map and edits the server's loot rules.

- **Reads** (never writes) the server's game data: the folder ARES-GameDataExtract writes from an
  installed game (`manifest.json`, the loot container types, the item catalogue with its names (`usable-items.json`,
  `item-values.json`), and each map's loot sockets, placed loot
  containers, dynamic loot spawners and loose item sockets). The 3D view draws loose item sockets as small diamonds in
  their own colours; the View box shows or hides those on props and those on nature (`Socket.Loot.Nature.*`: trees,
  rocks; hidden by default), and clicking one shows its options, data layer, item chance and covering rules. The
  View box also lists the map's loot placement limiters (`loot-limiters.json`: zones that cap, per tag, how many sockets
  inside them get loot) and draws them on request; opening one shows its caps and how many sockets each covers, and the
  container and loose item socket inspectors list the limiters that cap the selected socket. The 3D
  view draws the map under the containers from the map's collision file (`maps/<map>/<map>.collision`, ARES's `ARESCOLL` format, version 3; the editor
  reads it as `Ares.Common.Maps.MapCollisionFile` writes it). Without that file the view shows only
  the containers.
- **Estimates contents.** The container and loose item socket inspectors show what the selection holds when it spawns:
  the pool or rule it draws from, the number of stacks, and each tier, pool entry or extra stack with its items and
  their chance. The editor draws the contents 10,000 times with a port of the server's roll (`Matching/ContainerContentRolls`
  mirrors `LootContentRolls`, `Matching/LooseItemRolls` mirrors `LooseItemRolls`) from the current, unsaved files included,
  reading the items' drop tags and placements (`usable-items.json`), the value loot tables and the container classes'
  slot counts from the game data, and the weighted stack counts (`targetStacks`) from `loot-contents.json`. What the
  game data leaves unset (a container type's per-group cap, a limiter's shape, radius and caps, a spawner's count)
  takes the defaults of `loot-spawn.json`, as on the server; the editor reads them when it opens the server.
- **Edits contents profiles** (`profiles` in `loot-contents.json`). Selecting a tag in the tag tree, a loose item
  socket's tag level in its inspector, or a container class in the container inspector opens that type's profile:
  create, edit or delete it. A profile keeps only some items, weights them (0 removes them), adds items the source does
  not offer whatever their placement lists say (the contents estimate warns when the list refuses the container and
  marks added items), scales the tiers and, for containers, sets the first stack's tier (or turns it off), the number of
  stacks and the extra stack. What a profile leaves out comes from the profiles of the tags above it (and for a class,
  of its container tag), shown greyed with the profile it comes from. The container and loose item socket inspectors
  list the profiles that apply, and the problems the server would refuse show in the panel and with the estimate.
- **Reads and writes** the loot files of the server's content folder: `loot-rules.json`,
  `loot-spawn.json` and `loot-contents.json`. Saving keeps the server's file layout, member order,
  members the editor does not know and the text of numbers it did not change, so an unchanged file is
  written back byte for byte. A rule's item fields suggest the items a loot container can hold (the server's
  `LoadoutDefinitions.ContainerItems`) as you type part of the technical or in-game name. Restart ARES-Host after saving; it loads the content when it starts.

ARES-Editor shares no code with ARES-Server; it depends on it only through these file formats.

## Requirements

- An ARES-Server install (the folder ARES-Host runs in) with its `content/` folder.
- That install's game data, extracted with ARES-GameDataExtract (see the ARES-Server README).

Pick the server folder and the editor finds both the way ARES-Host does: `content-dir` and
`game-data-dir` in its `ares-host.conf` (relative paths are relative to that file), else `content`
and `gamedata` next to it. The content and game data folders can also be given directly. The editor
remembers the last folders in its own settings file (`%APPDATA%\ARES-Editor\settings.json` on
Windows, `~/.config/ARES-Editor/settings.json` elsewhere; `XDG_CONFIG_HOME` overrides `~/.config`).

## Build and run

Requires the .NET 10 SDK (see `global.json`).

```sh
dotnet build ARES-Editor.slnx
dotnet run --project src/Ares.Editor.App
dotnet test --solution ARES-Editor.slnx
```

Linux needs WebKitGTK 4.1 for the window (often packaged as `webkit2gtk-4.1` or
`libwebkit2gtk-4.1-0`). The tests that read real data run when it is there: ARES-Server's `content/` next to this
repository (or `ARES_CONTENT_DIR`), and an extracted game data folder named by `ARES_GAMEDATA_DIR`;
otherwise they are skipped.

## License

GPL-3.0-or-later; see `LICENSE` and `THIRD-PARTY-NOTICES.txt`.
