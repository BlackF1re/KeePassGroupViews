# KeePassGroupViews

Per-group column layouts for **KeePass 2.x**. Tested on KeePass 2.62 (Windows and Linux/Mono).

## Features

- Automatically switch visible columns, order, and widths when selecting groups.
- Show standard and custom entry fields using KeePass's native columns.
- Inherit layouts from parent groups; use a default layout otherwise.
- Keep layout settings in local KeePass configuration, without modifying KDBX entries.

## Installation

Download [KeePassGroupViews.dll](../../releases/latest) from the latest release, copy it into the KeePass `Plugins` directory, and restart KeePass. A ZIP package with the license and README is also available.

## Usage

1. Select a group and edit columns using **View → Configure Columns**.
2. Switch groups freely: changes to columns, widths, and order are saved automatically for the edited group.
3. Choose **Tools → Group Views → Use inherited columns** to discard a group's override and inherit from its parent.
4. Choose **Set current columns as default** to define the fallback for groups without overrides.
5. Optionally choose **Save columns for this group** to pin an inherited layout without changing it.

Save and inherit commands are also available in the group context menu. Settings are local to KeePass and never modify the KDBX database.

## Build

Requires .NET SDK and an official KeePass 2.x executable.

```powershell
dotnet build src/KeePassGroupViews -c Release -p:KeePassReferencePath="C:\Path\To\KeePass.exe"
```

License: GPL-2.0-or-later.
