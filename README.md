# MiniThemeStudio

A desktop editor for the menu themes (the Clover UI) of the Nintendo Classic Mini consoles, packaged for
[hakchi](https://github.com/TeamShinkansen/Hakchi2-CE).

A theme only holds what it changes. Everything else comes from a **Base**: the UI files of the stock
firmware the theme is made for. The editor shows the theme over its Base, writes every change to a draft
copy, and exports the result as a package hakchi installs.

## Supported consoles

| Base | Firmware |
| --- | --- |
| NES Classic Mini | 1.0.2 |
| Famicom Classic Mini (Japan) | 1.0.5 |
| Famicom Mini Shonen Jump 50th (Japan) | 3.0.1 |
| SNES Classic Mini (Europe) | 2.0.7, 2.0.13 |
| SNES Classic Edition (USA) | 2.0.7, 2.0.13 |
| Super Famicom Classic Mini (Japan) | 2.0.12 |

A Base is imported once, from a firmware image (`.hsqs`, `.tar`, `.tar.gz`) or from a folder that holds
its UI files. The editor checks the files against the known firmwares before it uses them.

## What it edits

| Tab | What it changes |
| --- | --- |
| Scenes | The nodes of each `.scn` scene: position, layer, size, visibility, the image they draw. Images can be added from a PNG, nodes duplicated or deleted, and moved by dragging them in the preview. |
| Sprites | The sprites packed in the atlases (`sprites/packed.png` and the like): replace or export each one. |
| Wallpapers | The background layers (`wall`, `wall_near`, `wall_far`...). |
| Characters | The characters running in the menu: their frames, frame time and direction. |
| Fonts | The console fonts: convert a TrueType or OpenType font, fill the characters a font lacks (Japanese, Cyrillic, Greek), replace or export the font and its texture. |
| Colors | The colour palettes of each region (the compiled `colors_*.lua` scripts) and where the scenes use each colour. |
| Audio | The home and boot music, and how it is packaged (converted with FFmpeg, mono or stereo, or left out). |
| Validation | What would break the theme on the console, images no scene draws, textures registered without an image. |

A theme that has validation errors cannot be saved or exported.

### Exporting

**Export package** writes `<SYSTEM>.<name>.tar.gz`. It holds the theme's own files and, when the theme
adds files the Base does not have, a `needs_overlay` marker for hakchi.

## Where it keeps its files

On Windows, under `%AppData%\MiniThemeStudio`; on Linux and macOS, under the matching application data
folder of the user.

| Folder | Holds |
| --- | --- |
| `Bases` | The imported Bases, one folder per console, region and version. |
| `Drafts` | Saved drafts of themes in progress. |
| `Backups` | The copies made before a theme is overwritten. |
| `Logs` | The logs (`minithemestudio-<date>.log`), kept for 14 days. Attach the latest one when reporting a problem. |
| `settings.json` | The settings: the console address, FFmpeg, the audio options, the fill font. |

Only one Mini Theme Studio runs at a time; starting it again brings the open window to the front.

## Building

It needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build MiniThemeStudio.slnx
dotnet run --project src/MiniThemeStudio.App
dotnet test MiniThemeStudio.slnx
```

The CI builds and tests on Windows and Linux for every pull request, and on macOS too when work reaches
`development` or `main`.

## Project layout

| Project | Role |
| --- | --- |
| `src/MiniThemeStudio.Core` | The models and the interfaces of the services. No file access, no UI. |
| `src/MiniThemeStudio.Infrastructure` | The services: Bases and firmware images, theme files (`dependencies.json`, scenes, atlases, animations, fonts, palettes, audio), validation and export. |
| `src/MiniThemeStudio.App` | The Avalonia app, in MVVM: a view model per tab over a shared `ThemeSession`. `Composition/ServiceRegistration.cs` is the only place that picks the implementations. |
| `tests/*` | xUnit tests for each project. The app tests use the real composition with the dialogs faked, and every folder in a temporary directory. |

## Contributing

- Branch from `development`; pull requests target `development`, and `main` holds the releases.
- Code, comments and UI text are in English, following the
  [.NET coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions).
  `.editorconfig` holds the rules and the build treats every warning as an error.
- Files use LF line endings.
- Install the [pre-commit](https://pre-commit.com/) hooks with `pre-commit install`. They check line
  endings, whitespace, JSON and YAML, and run `dotnet format whitespace`. The CI runs the same hooks.
