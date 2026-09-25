# LunaPanel

**Turn a phone or tablet into a control panel for Elite Dangerous.**

Open a web page on your phone or tablet, tap a button, and the command fires
in the game on your PC. No cables, no extra hardware, nothing to install on
the device. The two just need to be on the same home network.

It's somewhere to put the switches you keep forgetting the key for — landing
gear, lights, cargo scoop, pips. Big enough to hit without looking.

## How this was built

LunaPanel is a **vibe coded** project — around 95% written by Claude Code, and
around 5% by hand by the developer behind it.

It sits on **Luna-Core**, a starter kit for running a project this way, and is
built alongside **Astrid**, an AI personality.

---

## What you get

**Buttons that know your game**

- Every button uses *your* keybindings, read from your own Elite settings.
  Rebind a key in the game and the button follows.
- Buttons light up to show what's on in the ship right now — gear down, lights
  on — and they keep up as you fly.
- The panel takes its colours from your own HUD if you use the EDHM-UI mod. If
  you don't, it uses Elite's orange, or whatever you pick.

**Four ways to press**

- **Tap** for a normal press.
- **Long-press** for a second action on the same button, if you've given it
  one.
- **Latch** to hold a key down until you tap it again — handy for keeping
  secondary fire down to dispatch a ton of limpets during a mining run. It
  lets go by itself if the device sleeps, if you leave the game, or after two
  minutes.
- **Hold** to keep a key down for as long as your finger is actually on the
  button, and let go the moment it lifts — for a control like thrust, where
  the game needs to see the key physically held rather than tapped.

**Buttons that do more than one thing**

- Some things in Elite have no key at all. Requesting docking means opening a
  panel and finding your way to it. LunaPanel does that part for you, so it's
  one button.
- **Press a running one again to stop it.** No waiting, no "already busy".
- **A macro can check what's actually going on first.** Disembark used to only
  work docked at a station - now it looks at whether you're docked or sitting
  on a planet's surface and takes the right steps either way, all from the
  same button.
- **Pressing a macro doesn't freeze the panel while it runs.** The button
  fires straight away, then tells you how it went - which step, and anything
  that went wrong - the moment it's actually done, instead of leaving the
  screen sitting there wondering.
- Six ready-made macros cover every two-system pip priority (Shields +
  Engines, Engines + Weapons, and so on) - pick one and it pushes your first
  choice to four pips and the second to two. **Prepare to Dock** is built in
  too: it requests docking, waits for the game to actually grant it, refuels,
  and stows or deploys your hardware to match.
- **Build your own!** Pick steps from a list. Each one's written in plain
  English with an explanation, and you can see how long the whole thing will
  take before you save it. You build them on the PC, where there's a real
  keyboard — the tray icon has a **Build a macro** item that opens it. Your
  phone or tablet picks them up and puts them on buttons like anything else.
  Start a step from either direction: pick the control you want, or press the
  physical key you want and LunaPanel checks your real Elite bindings on the
  spot — offering to use that control instead, use the key as-is, or back out
  and pick again, so a step keeps working even if you rebind that key later.

**A panel that follows you**

- The page changes when you change vessel. Climb into the SRV and the SRV
  controls come up. Dock it again and you're back on the ship's page.
- Pages ship for the ship, the SRV, the Nomad, a fighter, and on foot. The
  ship page groups buttons by what they're for - navigation together, power
  together, docking together - rather than scattered wherever there was
  room.
- It won't switch while your thumb is already moving, and it won't drag you
  back if you've tapped somewhere else yourself.
- Running more than one device? Turn following off for just one of them from
  that device's own Settings → Panels tab - handy if you want one panel to
  stay put while the other keeps tracking you around.

**Set up the way you want it**

- **Edit on the device.** Tap a slot, search for the control, place it, rename
  it. No files, nothing to restart. (You can also edit from the PC instead -
  see "Edit a paired device's layout live" below.)
- **Drag a button somewhere else.** In edit mode, pick one up with your finger
  and drop it on another square. Drop it on an occupied one and the two swap.
- The picker shows every control Elite has, not just the ones you've bound.
  Place an unbound one and the button says so rather than failing quietly —
  and it starts working the moment you bind the key in the game. Confirm
  (Elite's Space bar) is in that list too, for the times you just need to
  accept a prompt.
- **Choose your own colours**, from your HUD's palette or a standard one, with
  one tap back to automatic. That's the border, the text, the glow on a lit
  button, and the background behind them all. If a colour ends up invisible
  against the background, the panel says so and still lets you have it.
- **Choose how many buttons you want**, from six up to sixty-four. Buttons
  that no longer fit move to a second page rather than disappearing, and a
  tab row appears to switch between them.
- **Add, delete, rename, and reorder your own pages, and choose which vessel
  brings each one up.** Tap the tab row's "+" to add one and pick its size;
  hold a tab down to rename it, delete it, or set (or clear) which vessel
  context - main ship, fighter, SRV, a specific ship type, on foot - switches
  to it on its own. Drag a tab sideways to put it wherever you want in the
  row.
- **If it can't find your game (or your EDHM-UI settings) on its own, tell
  it where to look.** The tray icon's **About** window shows both paths and
  lets you pick the right folder or file by hand - it also shows which
  version you're running and gets you straight to the logs folder.
- **Check for updates from the tray, any time you like.** It looks for a
  newer version, asks before doing anything, and if you say yes it
  downloads and installs it and starts itself back up - no need to keep
  track of new versions yourself.
- **Turn a button into a folder.** It opens its own small page of buttons
  instead of firing anything - handy for grouping controls by role on a
  smaller screen. One level deep, with a clear way back up; clearing or
  reassigning the button never deletes what's inside.
- **Tune macro timing** if a press isn't registering — how long a key is held,
  and the gap between presses. It's one setting for the whole PC, and you can
  reach it from the tray icon or from any device. Change it in one place and
  every device that's connected sees it straight away.
- **Start over** with one button, if you've edited a panel into a mess.

**More than one device**

- Pair as many as you like. Each one gets a name, and the tray app lists them
  all.
- **Copy your buttons from another device** rather than laying them out twice.
- **Edit a paired device's layout live, from the PC's own browser.** Open
  Settings → Devices on the PC and choose "Edit this device live" next to any
  paired phone or tablet - you're now editing its real arrangement directly,
  and if that device's own screen is open at the same time it updates
  instantly. A banner shows which device you're editing, with a button to
  stop and go back to your own. This is separate from saving to a file below
  - use it when the device is right there on the network and you just want to
  fix its layout from a keyboard.
- **Save a whole panel to a file, and load one back.** The tray icon's
  **Import or export** item does both. A saved panel takes the macros its
  buttons use along with it, so you can hand one to another commander or carry
  it to a new PC. You can send single macros the same way.
- Pair a device again and LunaPanel offers your old arrangement back rather
  than making you rebuild it. Don't need an old device's saved layout
  anymore? Delete it from that same list in Settings.
- Pairing stays closed unless you open it, and only for two minutes at a time.

## What you need

- **A Windows PC** running Elite Dangerous. It's Windows-only for now.
- **A phone or tablet with a web browser**, on the same home network. Any size
  works.

## Getting started

1. Run LunaPanel on your PC. The first time, before the tray icon even shows
   up, it checks whether it found Elite Dangerous on its own - point it at
   the right folder yourself, or tell it you'll deal with that later. After
   that it sits in the system tray, next to the clock.
2. Right-click the tray icon and choose **Add a device**. A window shows a
   six-digit code and the address to open.
3. On your phone or tablet, open that address and type in the code.
4. The panel appears, already filled with a starter set of buttons.

That's the setup. After that the device remembers. Tap the page once to go
fullscreen and lose the browser's address bar, and you can add it to your home
screen too.

## Something not working right?

[Open an issue](https://github.com/LunaBelleElite/LunaPanel/issues) and tell
me what happened. I can't promise a fix by tomorrow, but I read every one and
I'll do what I can to sort it out. If you've got them, attach the log
file(s) from `%LocalAppData%\LunaPanel\Logs\` (a new one's written each day,
named `lunapanel-YYYYMMDD.log`, and the tray's About window has a button
straight there) and whatever error text you saw — that's usually what turns
"it broke" into something I can actually chase down.

Would rather talk it through? Come find me on the
[LunaPrograms Discord](https://discord.gg/64Gg9qdgsT) — the support channel is
for how-do-I questions, and the bug-reports channel is for things that are
actually broken.
