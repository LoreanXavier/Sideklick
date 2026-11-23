<p align="center">
  <img src="assets/icon.png" alt="Sideklick icon" width="96" />
</p>

<h1 align="center">Sideklick</h1>

<p align="center">Push to talk. Stay on the page.</p>

<p align="center">
  <a href="https://github.com/LoreanXavier/Sideklick/releases/latest"><img src="https://img.shields.io/github/v/release/LoreanXavier/Sideklick?color=2667E0&amp;cacheSeconds=300" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D4" alt="Windows" />
  <img src="https://img.shields.io/badge/language-C%23-68217A" alt="C#" />
  <img src="https://img.shields.io/badge/portable-no_install-2667E0" alt="Portable" />
  <img src="https://img.shields.io/badge/tray_icon-yes-25B5A5" alt="Tray icon" />
</p>

<p align="center">
  <a href="https://github.com/LoreanXavier/Sideklick/releases/latest/download/Sideklick.exe"><strong>Download for Windows (.exe)</strong></a>
  &nbsp;&bull;&nbsp;
  <a href="Sideklick.cs"><strong>Source code</strong></a>
</p>

## Why Sideklick?

Your mouse side button is a handy Discord push-to-talk bind. It's less handy when every press also sends your browser **Back** or **Forward**.

Sideklick disables the button's normal Windows behaviour so you can keep using it for Discord push to talk without accidentally leaving the page. If your Discord setup doesn't pick up the disabled button, the optional **F13 / F14** replacement gives you a key to bind instead.

## Set it once

1. [Download Sideklick.exe](https://github.com/LoreanXavier/Sideklick/releases/latest/download/Sideklick.exe) and open it. No building or installation needed.
2. Pick **Block** (or **F13 / F14 / F15**) for the buttons you want to change. Changes apply and save instantly.
3. Close the window. Sideklick keeps running in the tray next to the clock.

**Start with Windows** is on by default, so it comes back after every restart with the same settings. Turn it off in the window or the tray menu.

## Tray

- Left-click the icon to open the window.
- Right-click for quick access: change each button's mode, **Pause / Resume** everything, toggle **Start with Windows**, or **Exit**.
- A grey icon means Sideklick is paused and all buttons behave normally.

While the window is open, pressing a mouse button highlights its row so you can tell which is which.

## Where things are kept

Settings are saved in `%APPDATA%\Sideklick\settings.txt`, so they survive restarts no matter where the exe lives. An old `settings.txt` next to the exe is picked up automatically. If you move the exe, open it once from its new place and the startup entry follows it. The UI is in Turkish when Windows is set to Turkish, English otherwise.

Disabling a button applies across Windows. F-key mode maps side buttons to **F13/F14** and wheel click to **F15**; rebind Discord PTT if you choose this mode. Left and right click are never touched.

**Upgrading from the original Sideklick:** just open the new exe. It notices the old one running and offers to replace it, carrying your button settings over.

## Build

Run `Build.cmd` on Windows with .NET Framework 4.x installed. It uses the Windows C# compiler and produces `Sideklick.exe`. Source, the Windows icon, and its PNG image are included.

Forked from [deklol/Sideklick](https://github.com/deklol/Sideklick).
