# SmoothScroll

[🇷🇺 Русская версия](README.ru.md)

A lightweight background utility for Windows that replaces the mouse wheel's
abrupt 3-line jumps with smooth, animated, inertia-based scrolling —
an open analog of the well-known SmoothScroll app.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet) ![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4) ![single exe](https://img.shields.io/badge/build-single%20exe-success)

## Features

- **Inertia scrolling** — three motion profiles: exponential glide (default),
  constant speed, and cubic ease-out.
- **Adjustable step** — scroll 0.25×–4× farther per wheel detent.
- **Adjustable animation length** — 100–1200 ms.
- **Flick acceleration** — rapid successive detents speed the scroll up (up to 3×).
- **Horizontal wheel** — smooth horizontal scrolling when tilting the wheel.
- **Per-app blacklist** — games and other apps keep their native scroll behavior.
- **Autostart with Windows** — one checkbox.
- High-precision touchpads and wheel events injected by other software are
  passed through untouched — they are already smooth.

Everything applies instantly (no restart) and is saved to
`%APPDATA%\SmoothScroll\settings.json`. UI is available in Russian.

## Run

Grab the ready single-file build (no .NET installation needed):

```
publish\SmoothScroll.exe
```

The app lives in the notification area (system tray):

- **double-click** the tray icon — settings window;
- **right-click** — menu: Enabled / Settings / Run at startup / Exit.

## Build from source

Requires the .NET 8 SDK.

```bat
dotnet build -c Release

rem single-file, self-contained executable:
dotnet publish -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```

## How it works

1. A low-level mouse hook (`WH_MOUSE_LL`) running on its own dedicated,
   above-normal-priority thread intercepts `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL`.
2. Classic wheel detents (|Δ| ≥ 100) are swallowed. Every impulse is pushed
   into a lock-free queue — **the hook thread never blocks**. This matters:
   a blocked low-level hook hits the system timeout (300 ms) and makes the
   whole mouse lag.
3. A dedicated animator thread (also above-normal priority) drains the queue
   and, paced by a high-resolution waitable timer (~125 Hz, Windows 10 1803+,
   `timeBeginPeriod(1)` fallback), injects fractional scroll steps via
   `SendInput` following the selected curve. Fractional remainders accumulate,
   so the total distance lands exactly on target.
4. Injected events are tagged with a marker in `dwExtraInfo` — the hook lets
   them through, preventing recursion.
5. Slow work (resolving the window under the cursor into a process name for
   the blacklist) never happens on the hook thread — it is offloaded to the
   animator thread, and until a window is identified it keeps native scrolling.

### Tuning tips

- Row-based lists (Explorer, Task Manager) snap to whole rows, so the
  animation is most visible with **longer duration** (500–700 ms) and the
  **"soft braking" (ease-out)** profile.
- Pixel-scrolled apps (browsers, editors) look great with the default
  exponential glide.

## Notes

- If scrolling does not work in a window running **elevated (as
  administrator)**, that is Windows UIPI: run SmoothScroll elevated too.
- Games that read raw input usually bypass wheel hooks anyway; add them to
  the blacklist if needed.
