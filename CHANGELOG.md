# Changelog

## ver-1.4.4.1 - 2026-09-19

- Page settings (rename, delete, visibility) used to be reachable only by holding a tab down for half a second, with nothing on screen hinting it was there. Turning on edit mode now shows a small gear on every tab - and on a folder's own name - that opens the same sheet with a click. The hold still works too.

## ver-1.4.4.0 - 2026-09-19

- Fixed a real crash: if something else on the machine briefly held the diagnostics log file at the exact moment LunaPanel tried to write to it, the whole app could go down. It now waits a beat and tries again, and if that still fails, just skips that one log line rather than taking the process with it. Found from an actual crash report.

## ver-1.4.3.1 - 2026-09-19

- Fixed: the pairing window's countdown label was sized for its short countdown text, so the longer "code's expired" message clipped at the bottom once it wrapped to two lines. Same fix as the label beside it got earlier tonight - size it for the longest text it'll ever show. Confirmed live.

## ver-1.4.3.0 - 2026-09-19

- Fixed the disembark macro's planet-surface path (docking at a station was already fine) - it was pressing the wrong radar key and landing on the wrong menu item for a commander with real keybinds. Its final confirm press could also fire without the game registering it, the same issue fixed elsewhere tonight; it now waits for confirmation and retries. Found from a real user's report.

## ver-1.4.2.1 - 2026-09-19

- Renamed the "About" button, on the tray menu and the Status window both, to "General" - that's what it actually opens.

## ver-1.4.2.0 - 2026-09-19

- Self-updates used to take thirty to fifty seconds because the app didn't answer Windows' shutdown request properly. It now answers in under a tenth of a second, and a full update takes eight or nine.
- Fixed a rare post-update crash: if the new files were still locked at the exact instant of relaunch, LunaPanel could fail to come back on its own. It now hands off to a small launcher that waits for the files and retries.
- Windows Installer was only checking the first three numbers of the version, so a fix-only release - moving just the last number - could get skipped as "already installed," leaving old and new files mixed together. Every real version bump is now recognized and installed.

## ver-1.4.1.1 - 2026-09-19

- Caught the README up to the first-launch Elite Dangerous setup screen added earlier tonight - it had shipped without ever making it into the Getting started steps.

## ver-1.4.1.0 - 2026-09-19

- Fixed a serious upgrade bug: only the tray program was stamped with its real version at build time, so Windows Installer treated Core and Server as unchanged and left the old copies in place - a mismatched install that crashed on relaunch, confirmed on a real upgrade tonight. Every part of LunaPanel now gets the same real version number, and the installer build refuses to ship if any file's version is stale. If you upgraded to an earlier ver-1.4.x build and hit a crash on launch, reinstalling with this version or later fixes it.

## ver-1.4.0.4 - 2026-09-19

- Fixed the manual Elite Dangerous folder picker opening three folders too deep - right inside "Products" or a specific product folder - instead of the actual Elite Dangerous folder you're meant to pick. It now opens right where you'd expect, whether starting from what LunaPanel found on its own or a path you'd already set. Applies everywhere the picker shows up: the new first-launch screen below and the About window.

## ver-1.4.0.3 - 2026-09-19

- Reworded the "Minimize to system tray" checkbox in the About window to say what it actually does: "Closing the window minimizes to the system tray instead of exiting." The old label named the setting, not the effect.

## ver-1.4.0.2 - 2026-09-19

- Long button labels like "Colonisation Module" and "Nomad Dock/Launch" could render with their text jammed right up against the button's edge. Buttons now keep a little breathing room around their text no matter how long the label is.

## ver-1.4.0.1 - 2026-09-19

- The new first-launch setup screen (see below) now resizes properly - drag it wider or narrower and the explanation text rewraps to fit, instead of staying locked to one size.

## ver-1.4.0.0 - 2026-09-19

- Added a one-time setup screen that shows up the very first time LunaPanel runs, before the tray icon appears, checking whether it found Elite Dangerous. If not, you point it at the right folder or tell it plainly you'll deal with it later - no silent warning buried in a menu. The About window's manual override is still there if you want to revisit it.

## ver-1.3.2.1 - 2026-09-19

- Added a countdown bar and an "Expires in m:ss" caption to the pairing code in the Add a Device window, so you can actually see the two minutes running out instead of guessing. Once it hits zero it says so plainly: "This code has expired - close and try again."

## ver-1.3.2.0 - 2026-09-19

- Fixed a first-launch bug: opening the panel on a phone in portrait right after a cold start could show button text overlapping and garbled, because the browser hadn't settled on its real screen size yet. It now waits for things to settle, with a quiet automatic follow-up just in case - the same fix a manual rotation already triggered, just running on its own now.

## ver-1.3.1.0 - 2026-09-19

- Fixed Elite Dangerous and EDHM-UI detection for anyone whose Steam (or Elite) isn't sitting in the default folder, from a real user's report:
  - LunaPanel now checks the Windows registry for Steam's actual install location, instead of only the two standard Program Files paths - so it can find Elite even when Steam itself was moved.
  - The manual "point me at your Elite Dangerous install" folder picker now checks what you chose and explains clearly when it's wrong (picking the Products folder itself, or something inside it, instead of its parent), instead of silently offering a pointless restart.
  - "EDHM theme found" now means an actual usable HUD colour theme was located, not just that EDHM's installer file exists - so it won't show green when there's really nothing there to read.

## ver-1.3.0.0 - 2026-09-19

- EDHM colour changes now reach the panel the moment you make them. Previously you had to switch pages or nudge a layout before a HUD colour change in EDHM-UI would show up - now LunaPanel watches the theme files directly and pushes the update to every open panel instantly.

## ver-1.2.0.0 - 2026-09-19

- Added a "Minimize to system tray" option to the About window, on by default so nothing changes unless you go looking for it. Turn it off and LunaPanel keeps a normal taskbar window open all the time instead of living in the tray, and its close button quits the app outright instead of hiding it.

## ver-1.1.2.0 - 2026-09-19

- Fixed the self-update going dark for 15-30 seconds during the actual install, with nothing on screen to say it hadn't just hung. It now shows Windows' own plain progress bar for that stretch instead of silence - nothing to click, nothing you can interfere with, just proof it's still working. The "update now?" prompt beforehand and the "you've been updated" note after your next relaunch are unchanged.

## ver-1.1.1.0 - 2026-09-18

- Moved the "follow me between ship, SRV, and on foot" toggle to where you'd actually look for it: your device's own Settings → Panels tab, right under "Merging and expanding panels." It used to live in the tray's Devices window on the PC, which made no sense if you were holding your phone.
- Nothing about how the toggle works changed - still per device, still on by default.

## ver-1.1.0.0 - 2026-09-18

- Added a checkbox next to each device in the tray's Devices window, so you can turn off automatic page-following per device. Useful if you run more than one and want one to stay put while the other keeps following you between ship, SRV and on foot.
- It's on by default, so nothing changes for anyone who doesn't touch it.

## ver-1.0.1.1 - 2026-09-18

- Added tooltips to the About window and the Status window, so hovering anything - a status row, a button, the address box - tells you what it's for.

## ver-1.0.1.0 - 2026-09-18

- Fixed a real bug in Check for Updates, found the same night 1.0 shipped: it could say an update installed successfully without actually replacing anything, because every build looked identical to Windows under the hood. Each version now genuinely looks newer, so updates actually take effect.

## ver-1.0.0.0 - 2026-09-18

- LunaPanel is 1.0. Everything built up to here — buttons that know your keybindings, macros, folders, live editing from the PC, colours from your HUD, and now checking for and installing its own updates — is what ships as the real, public release.
</content>
</invoke>
