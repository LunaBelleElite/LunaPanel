# COMING SOON

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

**Three ways to press**

- **Tap** for a normal press.
- **Hold** for a second action on the same button, if you've given it one.
- **Latch** to hold a key down until you tap it again — handy for keeping
  secondary fire down to dispatch a ton of limpets during a mining run. It
  lets go by itself if the device sleeps, if you leave the game, or after two
  minutes.

**Buttons that do more than one thing**

- Some things in Elite have no key at all. Requesting docking means opening a
  panel and finding your way to it. LunaPanel does that part for you, so it's
  one button.
- **Press a running one again to stop it.** No waiting, no "already busy".
- **Build your own!** Pick steps from a list. Each one's written in plain
  English with an explanation, and you can see how long the whole thing will
  take before you save it. You build them on the PC, where there's a real
  keyboard — the tray icon has a **Build a macro** item that opens it. Your
  phone or tablet picks them up and puts them on buttons like anything else.

**A panel that follows you**

- The page changes when you change vessel. Climb into the SRV and the SRV
  controls come up. Dock it again and you're back on the ship's page.
- Pages ship for the ship, the SRV, the Nomad, a fighter, and on foot.
- It won't switch while your thumb is already moving, and it won't drag you
  back if you've tapped somewhere else yourself.

**Set up the way you want it**

- **Edit on the device.** Tap a slot, search for the control, place it, rename
  it. No files, nothing to restart.
- **Drag a button somewhere else.** In edit mode, pick one up with your finger
  and drop it on another square. Drop it on an occupied one and the two swap.
- The picker shows every control Elite has, not just the ones you've bound.
  Place an unbound one and the button says so rather than failing quietly —
  and it starts working the moment you bind the key in the game.
- **Choose your own colours**, from your HUD's palette or a standard one, with
  one tap back to automatic. That's the border, the text, the glow on a lit
  button, and the background behind them all. If a colour ends up invisible
  against the background, the panel says so and still lets you have it.
- **Choose how many buttons you want**, from six up to thirty. Buttons that no
  longer fit move to a second page rather than disappearing, and a tab row
  appears to switch between them.
- **Tune macro timing** if a press isn't registering — how long a key is held,
  and the gap between presses. It's one setting for the whole PC, and you can
  reach it from the tray icon or from any device. Change it in one place and
  every device that's connected sees it straight away.
- **Start over** with one button, if you've edited a panel into a mess.

**More than one device**

- Pair as many as you like. Each one gets a name, and the tray app lists them
  all.
- **Copy your buttons from another device** rather than laying them out twice.
- **Save a whole panel to a file, and load one back.** The tray icon's
  **Import or export** item does both. A saved panel takes the macros its
  buttons use along with it, so you can hand one to another commander or carry
  it to a new PC. You can send single macros the same way.
- Pair a device again and LunaPanel offers your old arrangement back rather
  than making you rebuild it.
- Pairing stays closed unless you open it, and only for two minutes at a time.

## What you need

- **A Windows PC** running Elite Dangerous. Windows only, and that won't
  change — the part that presses keys is Windows-specific, and it's
  essentially the whole product.
- **A phone or tablet with a web browser**, on the same home network. Any size
  works.

## Getting started

1. Run LunaPanel on your PC. It sits in the system tray, next to the clock.
2. Right-click the tray icon and choose **Add a device**. A window shows a
   six-digit code and the address to open.
3. On your phone or tablet, open that address and type in the code.
4. The panel appears, already filled with a starter set of buttons.

That's the setup. After that the device remembers. Tap the page once to go
fullscreen and lose the browser's address bar, and you can add it to your home
screen too.
