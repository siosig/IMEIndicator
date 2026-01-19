# IMEIndicatorClockW

[日本語](README_ja.md) | [繁體中文](README_zh-Hant.md) | [简体中文](README_zh-Hans.md) | [한국어](README_ko.md)

A Windows utility app that visually displays IME (Input Method Editor) status with a customizable desktop clock.

This is the Windows port of the macOS version [IMEIndicatorClock](https://github.com/obott9/IMEIndicatorClock).

## Screenshots

### Desktop Overview
| IME ON (Japanese) | IME OFF (English) |
|:-----------------:|:-----------------:|
| ![IME ON](docs/images/desktop-ime-on.png) | ![IME OFF](docs/images/desktop-ime-off.png) |

### Settings
| IME Indicator | Clock | Mouse Cursor | Version |
|:-------------:|:-----:|:------------:|:-------:|
| ![Indicator](docs/images/settings-indicator.png) | ![Clock](docs/images/settings-clock.png) | ![Cursor](docs/images/settings-cursor.png) | ![Version](docs/images/settings-version.png) |

## Vision

**Our goal is to support IMEs from around the world.**

We aim to help IME users see their current input mode at a glance.

## Features

### IME Indicator
- Visually displays the current input method status on screen
- Japanese: Red circle with "あ"
- English: Blue circle with "A"
- Customizable position, size, and opacity
- Multi-display support

### Desktop Clock
- Floating clock supporting both analog and digital modes
- Date display with Japanese calendar (Wareki) support
- Background color changes based on IME status
- Fully customizable window size, font size, and colors

### Mouse Cursor Indicator
- Displays IME status near the mouse cursor
- Convenient for text input

## Language Support

### Full Support (IME Detection + UI)
| Language | IME Detection | UI Localization |
|----------|:-------------:|:---------------:|
| Japanese | ✅ | ✅ |
| English | ✅ | ✅ |
| Chinese (Simplified) | ✅ | ✅ |
| Chinese (Traditional) | ✅ | ✅ |
| Korean | ✅ | ✅ |

### IME Detection + Basic UI
| Language | IME Detection | UI Localization |
|----------|:-------------:|:---------------:|
| Thai | ✅ | ✅ |
| Vietnamese | ✅ | ✅ |
| Arabic | ✅ | ✅ |
| Hebrew | ✅ | ✅ |
| Hindi | ✅ | ✅ |
| Russian | ✅ | ✅ |
| Greek | ✅ | ✅ |
| Bengali | ✅ | ✅ |
| Tamil | ✅ | ✅ |
| Telugu | ✅ | ✅ |
| Nepali | ✅ | ✅ |
| Sinhala | ✅ | ✅ |
| Myanmar | ✅ | ✅ |
| Khmer | ✅ | ✅ |
| Lao | ✅ | ✅ |
| Mongolian | ✅ | ✅ |
| Persian | ✅ | ✅ |
| Ukrainian | ✅ | ✅ |

*UI translations for these languages are machine-translated and may need improvement. Contributions welcome!*

## System Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Installation

1. Download the latest release from [Releases](https://github.com/obott9/IMEIndicatorClockW/releases)
2. Extract to any folder
3. Run `IMEIndicatorW.exe`

## Build from Source

### Requirements
- Visual Studio 2022 or VS Code
- .NET 8.0 SDK

### Build Steps
```bash
git clone https://github.com/obott9/IMEIndicatorClockW.git
cd IMEIndicatorClockW
dotnet build
```

## Usage

1. Launch the app - an icon appears in the system tray
2. Right-click the tray icon to access settings
3. Drag the clock or indicator to your preferred position

## Security & Privacy

### About Keyboard Hook Usage

This application uses a **low-level keyboard hook** (`SetWindowsHookEx` API) for accurate IME status detection.

**Why is a keyboard hook necessary?**
- Standard Windows IME APIs cannot accurately detect IME status in some applications (terminals, games, etc.)
- The keyboard hook detects IME toggle keys (Hankaku/Zenkaku, Henkan, etc.) for more accurate status display

**Safety:**
- Key input content is **never recorded or transmitted**
- Only IME-related keys are detected (Hankaku/Zenkaku, Ctrl+Space, etc.)
- No internet communication features
- Settings are stored locally only (`%AppData%\IMEIndicatorW`)
- Source code is open and verifiable

**About antivirus warnings:**
Applications using keyboard hooks may trigger antivirus warnings. This is because the same technology is used by keyloggers, but this application performs no malicious operations. Please review the source code if you have concerns.

## Development

This project was developed in collaboration with [Claude AI](https://claude.ai/) by Anthropic.

Claude assisted with:
- Architecture design and code implementation
- Multi-language localization
- Documentation and README creation

## Support

If you find this app useful, consider buying me a coffee!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/obott9)

## Contributing

We welcome contributions! Especially:
- UI translations for additional languages
- Support for more IME types
- Bug reports and feature requests

## License

MIT License - See [LICENSE](LICENSE) file for details.
