# LiquidFlow — a fork of OpenWhispr

LiquidFlow (Electron edition) is a fork of **OpenWhispr**
(<https://github.com/OpenWhispr/openwhispr>), used under the **MIT License**.

The original copyright and license are preserved in [`LICENSE`](./LICENSE)
(Copyright (c) 2024 OpenWhispr Team). This fork retains that notice as the MIT
License requires.

## What this fork changes
- Rebrands the application to **LiquidFlow** (name, icons, window titles).
- Applies LiquidFlow's green‑black visual theme.
- Uses LiquidFlow's bottom dictation‑island overlay style.
- Runs in **local / free** mode: account, billing, and subscription surfaces are
  disabled so all on‑device features work without signing in.
- Targets Windows x64 (runs under x64 emulation on Windows‑on‑ARM).

The separate native C#/.NET LiquidFlow app lives on the `windows-port` branch and
tag `liquidflow-native-1.6.2`; this Electron fork is developed on the
`liquidflow-electron` branch.
