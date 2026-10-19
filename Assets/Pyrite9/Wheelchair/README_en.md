# Pyrite9 Wheelchair

Version 1.0.2 (2026-10-19)

A wheelchair gimmick for VRChat avatars. Drop the prefab under any humanoid avatar — it fits itself to the avatar when you build.

日本語: `README_jp.md` / 한국어: `README_ko.md` / License: `LICENSE.txt`

## Requirements

- Unity 2022.3 (VRChat), **PC only**
- VRChat SDK – Avatars 3.x
- Modular Avatar
- NDMF (Non-Destructive Modular Framework)
- lilToon

Tested with: Unity 2022.3.22f1, VRChat SDK Avatars 3.10.5, Modular Avatar 1.18.7 (stable, in a fresh project) and 1.19.0-alpha.0, NDMF 1.14.8, lilToon 2.3.4.
Older versions have not been checked.

## Installation

1. **Add the required packages.** In VCC / ALCOM, open your avatar project with "Manage Project" and add **Modular Avatar** and **lilToon**.
   NDMF is installed automatically together with Modular Avatar. Use the stable (non-preview) versions.
2. **Import this package.** Double-click the `.unitypackage` file (or in Unity: `Assets > Import Package > Custom Package…`),
   leave everything checked and press **Import**. The files go to `Assets/Pyrite9/Wheelchair/`.

## How to use

1. In the Project window, open `Assets/Pyrite9/Wheelchair/` and drag **`Wheelchair.prefab`** onto your avatar
   (the object with the VRC Avatar Descriptor) in the Hierarchy. It becomes a child of the avatar.
   - Any place inside the avatar is fine. **If it ends up outside the avatar (e.g. at the scene root), nothing happens.**
   - Do not unpack the prefab or rename the objects inside it.
2. (Optional) Change the settings on the **Wheelchair Root** component (see "Settings" below).
3. Open `VRChat SDK > Show Control Panel > Builder`. Try **Build & Test** first, then **Build & Publish**.
4. Check the Console. Messages that start with **`[Pyrite9 Wheelchair]`** are warnings — see "Troubleshooting".
   No message means the wheelchair was fitted normally. Details of the last build are written to `Logs/Pyrite9_Wheelchair_build.txt` in your project folder.

At build time the wheelchair measures your avatar in its sitting pose and adjusts:
size and position, seat width, sitting height (if the hips would go through the seat, the body is raised), footrests,
full-body-tracking sensors and wheel spin speed.
Nothing in your scene is changed — only the build copy.

## Expression Menu

A single submenu **Wheelchair** is added:

| Item | Parameter | Default |
|---|---|---|
| On/Off | `Wheelchair/On` | off |
| Full Body Tracking | `Wheelchair/FBT` | off |
| Rear Seat | `Wheelchair/RearSeat` | on |

Synced parameter cost: **3 bits**. Other parameters are internal (renamed automatically by Modular Avatar).

## Features

- Sitting pose for desktop and VR, fingers keep following your gestures
- VR (headset + controllers): if you raise yourself with a playspace mover (e.g. OVR Advanced Settings "Space Drag"), your whole seated body and the chair rise together — up to about 2.5 m above the normal seated height.
  It also follows down (up to 1 m) when your real head goes lower than the seated height; the chair then sinks into the floor, so raise yourself back with the playspace mover.
  Works at any VRChat avatar scale (changing your height in the Quick Menu)
- VR with VRChat full-body tracking on, Full Body Tracking menu off: your hips follow your hip tracker and the seated legs and feet follow your hips (up to about 2.5 m up, 1 m down); the chair follows your hips, so a playspace mover raises everything together. The seated height is where your real hip tracker is, so standing up in real life lifts you and the chair
- Wheels and casters spin with your movement
- Full Body Tracking mode: legs follow your trackers, the chair follows your hips, and slides back when your back or shins push into it
- Rear Seat: a standing spot behind the chair that other players can use
- AFK while seated: with the wheelchair on, you stay seated instead of playing your avatar's own AFK animation, and sit back properly when you return. With the wheelchair off, your avatar's own AFK plays as usual

## Settings (Wheelchair Root component on the prefab root)

| Field | Meaning |
|---|---|
| Menu Language | English / Japanese / Korean (menu labels, applied at build) |
| Auto Fit | Turn off to keep the prefab's own size and position |
| Seat Width Offset | Per side, meters. + wider |
| Seat Height Offset | Meters, added to the automatic sitting height. + raises the body, - lowers it |
| Foot Height Offset | Meters. + up |
| Foot Forward Offset | Meters. + forward |

The inspector also has (its text follows Menu Language):

- **Preview sitting**: builds a copy of the avatar with the current settings and seats it in place (takes a few seconds). Scene view only, never saved to the scene; turning it off puts everything back. Press **Refresh** after changing the offsets.
- **Last build**: the chair scale, body raise and warnings decided at the last Play or upload, with buttons for the build log and the README.
- **Version**: once a day it asks GitHub for the latest version and tells you when a newer one is out (see Notes).

## Troubleshooting

| Console message (`[Pyrite9 Wheelchair] …`) or symptom | What to do |
|---|---|
| `the avatar is not humanoid` | Only humanoid avatars are supported (Rig: Humanoid). |
| `sitting animation not found`, `wheelchair bone WC_Side_L not found`, `wheelchair model object not found`, `WC_HipRef/Seat not found`, `wheel spin state not found` | Parts of the prefab are missing or renamed. Delete the wheelchair from the avatar and drag `Wheelchair.prefab` in again. If that does not help, import the package again. |
| `the hips go … m into the seat — raised only …` | The automatic raise hit its limit (10 cm). Raise the body more with **Seat Height Offset**. |
| `chair scale … is far from the reference` | Your avatar is much smaller or larger than usual. Check it with Build & Test and use the offsets if needed. |
| `full-body sensor …`, `knee or foot bone …`, `contact sender or its bone …` not found | Full Body Tracking push-back may not work. Check the avatar's humanoid bone mapping (chest, legs, feet). |
| `WC_FBTOffset not found under Seat` | Full Body Tracking push-back keeps a fixed distance. The prefab was probably unpacked or edited — delete the `Wheelchair` object and drag the prefab in again. |
| `… bone or 'Wheelchair Sit Height …' blend tree not found`, `unexpected 'Wheelchair Sit Height …' blend tree`, `sitting animation has no RootT.y curve` | VR height follow (by the head, or by the hips with full-body tracking) is skipped; everything else works. Import the package again. |
| `FX controller not found — height follow may break when the avatar is resized` | VR height following may stop working when the avatar is resized. Assign an FX layer controller to the avatar (an empty one is fine). |
| `Gesture controller not found` | Fingers may stop following gestures while seated. Set a Gesture layer on the avatar descriptor (or leave it as default). |
| `no hand mask found for the gesture layers` | A Gesture layer of your avatar may override the sitting pose while seated (e.g. when crouching). Give the first layer of the avatar's Gesture controller a hand mask. |
| `… WheelchairRoot components found` | Use only one wheelchair per avatar. Remove the extra one. |
| The Wheelchair menu does not appear | The prefab is not inside the avatar. Move it under the avatar object. |
| The sitting pose is broken or overridden | Another gimmick is using the Action layer at the same time. Turn it off while seated. |
| Feet are off the footrests / the seat is too narrow | Adjust Foot Height / Foot Forward / Seat Width Offset. |
| The hips still go through the seat, or the body floats above it | Adjust **Seat Height Offset** (+ up / - down) by 0.01 m at a time and check with Build & Test. The footrests follow automatically. |

## Updating / removing

- **Update:** import the new package over the old one (leave everything checked and press Import).
- **Remove from an avatar:** delete the `Wheelchair` object from the avatar.
- **Remove from the project:** delete the `Assets/Pyrite9/Wheelchair/` folder (remove it from your avatars first).

## Changelog

- **1.0.2** (2026-10-19): VR height following (Space Drag) now works at any VRChat avatar scale (before, scaling the avatar up straightened the legs). AFK while seated keeps you seated and sits you back down when you return. Fixed your avatar's own AFK and emotes being covered while the wheelchair is off. New inspector (sitting preview, last build, update notice).
- **1.0.1** (2026-10-18): Full Body Tracking push-back now scales with VRChat avatar scaling (changing your height in the Quick Menu). Before, a smaller avatar pushed the chair the same distance and it moved too far.
- **1.0.0** (2026-10-17): First release.

## Notes

- Humanoid avatars only. Very unusual proportions may need the offsets above.
- Uses the **Action** layer for the sitting pose. Other gimmicks that also use the Action layer may conflict.
- While seated, your avatar's Gesture layers are copied into the Action layer so the fingers keep following your gestures. Like in the Gesture layer, they only move the hands.
- If GoGo Loco is installed, `Go/Action` is set while seated.
- When you open the wheelchair inspector in the Unity Editor, it asks GitHub (`api.github.com`) for this package's latest version once a day. Nothing is sent; click the version text to check again right away.
- Performance: 1 mesh, 12,196 triangles, 1 material (lilToon), 4 menu icons (256×256), 0 PhysBones, 11 Contacts (6 are added at build time to measure head and hip height in VR), 1 Station.

## License (summary — the full text is in `LICENSE.txt`)

- Free. You may use and modify it, and upload avatars that use it (public or private).
- You may use those avatars in videos, streams and screenshots, including monetized ones.
- You may install it on someone else's avatar (e.g. commissions) only if that person downloaded this package themselves.
- You may not redistribute, share or sell the files.
- Credit is not required, but appreciated.
- The package is also on GitHub: https://github.com/Pyrite9/vrchat-wheelchair (viewing and forking there are fine).

© Pyrite9
