# Console Manager

A mod for Spaceflight Simulator that improves the built-in console with per-source log filtering.

## Features

- Filter logs by source (mod, game, ModLoader, Harmony) and by level (Log, Warning, Error, Exception)
- Toggle all levels for a source on or off with one click
- Live count badge showing how many logs each source has produced
- Filter state is saved and restored between sessions
- The Copy button only copies logs that are currently visible

## How It Works

When a log arrives, the mod traces the call stack to figure out which mod or system produced it. It checks the assembly and namespace against the list of loaded mods to find a display name. If it can't match anything specific, the log is grouped under **Game**, **ModLoader**, **Harmony**, or **Other**.

Each source gets a row in the filter panel with toggle buttons for each log level. Turning a level off hides those logs from the console immediately.

## Known Issues

- **Wrong source** — logs from mods that route all their output through a shared logging utility, or through Harmony-transpiled methods, may show up under `Game` instead of the correct mod.
- **Raw assembly names** — logs that arrive very early at startup, before the mod list has finished loading, may show the internal assembly name instead of the mod's display name. These are corrected automatically once loading completes.