# VRC Scene Backup

**Automatic scene backup before every VRChat build.**

Have you ever had a power outage, a PC shutdown, or a crash in the middle of a build, and then your scene file was corrupted and everything you worked on was gone? Nothing left but thin air.

This asset was born from the day that happened to me.

Before every VRChat build, it saves a copy of your scene first. So if something goes wrong during the build, you still have the scene exactly as it was right before it started.

## Features

- Saves a backup automatically before every VRChat **Build & Test** and **Publish**
- Separate folder for each platform (Windows, Android, iOS)
- Timestamped file names, so older backups are never overwritten
- Automatically deletes the oldest backups beyond a limit you choose
- Never blocks or cancels your build, even if the backup fails
- Does not touch the scene you have open (the backup is saved as a copy)
- Backup Now button and menu items for manual backups

## Installation

### Option 1: VRChat Creator Companion (recommended)

1. Open **https://sweetforest.github.io/VPM/** and click **Add to VCC**
   (or copy the listing URL and add it manually in VCC under **Settings → Packages → Add Repository**)
2. Open your project in VCC
3. Find **VRC Scene Backup** in the package list and click **+** to add it

That's it. No setup is needed.

### Option 2: Manual

Download the `.zip` from the [Releases](../../releases) page and extract it into your project's `Packages` folder.

## How it works

Every time you start a VRChat build, the active scene is saved as a copy before the build begins:

```
Assets/
└── Scene Backup/
    ├── windows/
    │   └── bak_MyWorld_2026-11-13_15-55-44.unity
    ├── android/
    │   └── bak_MyWorld_2026-11-13_16-20-10.unity
    └── ios/
```

File names follow the pattern `bak_<scene name>_<yyyy-MM-dd_HH-mm-ss>.unity`, saved inside a folder named after the build platform.

### Restoring a scene

1. Close or save your current scene
2. Open the backup file from `Assets/Scene Backup/<platform>/`
3. Use **File → Save As...** to save it back to your original scene path

> Do not open a backup and your original scene at the same time.

## Settings

Open **Edit → Preferences → VRC Scene Backup** (or **Tools → VRC Scene Backup → Settings...**).

| Setting | Description | Default |
|---|---|---|
| Backup before build | Turns the automatic backup on or off | On |
| Backups to keep | How many backups to keep **per scene and per platform**. Older ones are deleted | 5 |
| Backup folder | Where backups are stored (must be inside `Assets/`) | `Assets/Scene Backup` |
| Log to Console | Prints a message when a backup is saved | On |

## Menu

**Tools → VRC Scene Backup**

- **Enabled**: toggle automatic backups
- **Backup Now**: save a backup of the active scene right away
- **Open Backup Folder**: open the backup folder in the Project window
- **Settings...**: open the preferences page

## Notes

- Scenes that have never been saved (Untitled) are skipped, because there is no file path to name the backup after.
- Backups are stored inside `Assets/`, so Unity imports them. They are normal scene files.
- If you use Git, add the backup folder to your `.gitignore` so backups are not committed:

  ```
  /Assets/Scene Backup/
  /Assets/Scene Backup.meta
  ```

- A backup only covers the scene file. Assets and scripts outside the scene are not backed up, so keep using version control for those.

## Requirements

- Unity 2022.3 (the version used by the VRChat SDK)
- VRChat SDK 3 (`com.vrchat.base`)

## Links

- Package listing: https://sweetforest.github.io/VPM/
- Sweet Forest on Gumroad: https://sweetforest.gumroad.com/
