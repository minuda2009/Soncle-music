# Task 01 — Android: know the output, switch sound profiles per output

_First task of ROADMAP.md "Android next: sound per output". Branch: `openhands/phone-output`.
Read `AGENTS.md` first. Keep the change small; the "Phone speaker" preset and virtual bass are
task 02, not this one._

## Goal
On the phone, Soncle knows what it is playing through (phone speaker, wired headphones, USB,
Bluetooth device by name, car) and switches the saved sound profile (EQ + sound settings + volume)
automatically, exactly as the desktop app already does.

## How desktop does it today (reuse, don't rewrite)
- `renderer/app.js`, section "audio output devices": `refreshDevices()` reads the output list,
  `classifyOutput()` (from `renderer/devices.js`) names it, `onDeviceChanged()` pauses on
  disconnect and applies `DB.deviceProfiles[OUT.key]`, `saveDeviceProfile()` stores it.
- On Android `navigator.mediaDevices` doesn't report outputs and `api.btDevices()` in
  `mobile/src/backend.js` returns `[]`, so none of this runs. The settings rows are hidden by
  `MOBILE_HIDDEN` in `app.js`.

## What to build
1. **Native (`SonclePlugin.java`).** Register an `AudioDeviceCallback`. Work out the active media
   output (on API 33+ `AudioManager.getDevicesForAttributes` for media usage; older: prefer a
   connected Bluetooth A2DP/LE, then USB, then wired, else the built-in speaker). Expose
   `getOutput()` returning `{ id, type, name }` (`type`: `speaker`, `wired`, `usb`, `bluetooth`,
   `car`, `hdmi`, `other`; `name`: the product name, e.g. "Galaxy Buds2"), and fire an
   `outputChanged` event when it changes. Also listen for
   `AudioManager.ACTION_AUDIO_BECOMING_NOISY` and fire `outputChanged` with `noisy: true`.
2. **Backend (`mobile/src/native.js`, `backend.js`).** Implement the hook the UI needs: a
   `api.audioOutput()` (current output) and `api.onAudioOutput(fn)`. Keep `btDevices` returning the
   Bluetooth names it can see.
3. **UI (`renderer/app.js`).** When `api.audioOutput` exists, `refreshDevices()` uses it instead of
   `enumerateDevices()`: build a label `classifyOutput()` understands (e.g. the product name, plus
   "(Bluetooth)" for Bluetooth), keep the same `OUT.key` scheme, then the existing
   `onDeviceChanged()` path does the rest. A `noisy` event pauses (if "Pause when headphones
   disconnect" is on). Desktop behaviour must not change.
4. **Settings.** Remove "Per-device sound profiles", "Auto-tune new devices" and "Saved device
   profiles" from `MOBILE_HIDDEN`. "Output device" stays hidden on the phone.

## Acceptance
- `npm test` and `npm run lint` pass. Add a unit test for the label → `classifyOutput()` mapping
  of the Android output types (speaker, wired, a few real Bluetooth product names, car).
- Preview APK from the branch: with earbuds connected, change the EQ; disconnect → playback pauses
  and the phone-speaker profile comes back; reconnect → the earbuds' EQ comes back within ~1 s.
- No new permission prompts beyond what Bluetooth names need (if `BLUETOOTH_CONNECT` is required on
  Android 12+, ask for it only when the user opens the device profiles setting, and work without it
  using the type only).
- CHANGELOG entry under "Unreleased". PR description lists what minuda2009 should test.

## Out of scope
The "Phone speaker" preset, virtual bass, AutoEq match by name (tasks 02–03), any desktop change.
