# DashX360 Source Code

DashX360 is a fan-made Windows recreation of the Xbox 360 Metro dashboard, with tile navigation, controller support, Guide overlays, local profile data, custom themes, boot media, and dashboard audio cues.

Original app credit: ZivvoZ  
https://youtube.com/@zivvoz

## Source Status

This repository contains recovered/decompiled C# source from the DashX360 V1.1 Windows build. It has been lightly cleaned so the project can build, but it is not the original hand-authored source tree.

Because this source was recovered from a published build, some files still reflect decompiler output, including generated helper classes, `.baml` resources instead of original `.xaml`, and compiler-style control flow in some methods.

## Features

- Xbox 360-inspired dashboard tabs for games, apps, music, video, social, and settings
- Controller-first navigation with keyboard and mouse support
- Xbox Guide overlay with Friends, Party, Profile, and search screens
- Local profile and friend data with cached gamer pictures
- Boot video, dashboard audio cues, and Metro-style tile presentation
- Custom theme support
- Steam library scanning with Steam-provided cover art
- Import/export support for user data transfer and version updates

## Building

### Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or the .NET CLI

### Command Line

```powershell
dotnet restore
dotnet build
```

The project targets `net8.0-windows` and uses WPF plus Windows Forms interop.

## Controls

- `A` / `Enter`: select
- `B` / `Escape`: back
- `X`: context actions where available
- `Y`: secondary actions where available
- Guide combo / hotkey: open the Xbox Guide overlay
- Mouse support is available for tiles, buttons, and popup menus

## Configuration Notes

- Launcher settings and cached profile data are stored in the app folder at runtime.
- Local user data, logs, and build output are ignored by git.
- If using Steam controller input, untick `Enable Guide Button Chords for controllers` to use the guide button with this launcher.

## Legal / Disclaimer

This is an unofficial, non-commercial fan project. Xbox, Xbox 360, Xbox LIVE, Microsoft, and related names, logos, and imagery are property of Microsoft. This project is not affiliated with, endorsed by, or sponsored by Microsoft.

Some bundled art, sounds, and reference assets may be derived from commercial software, media, or platform branding. Replace any assets you do not have the right to redistribute before publishing your own build or fork.

Only publish this repository if you have the rights or permission to redistribute the recovered source and bundled assets.
