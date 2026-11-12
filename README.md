# KeePassGroupViews

Per-group column layouts for **KeePass 2.x**. Tested on KeePass 2.62 (Windows and Linux/Mono).

## Features

- Automatically switch visible columns, order, and widths when selecting groups.
- Show standard and custom entry fields using KeePass's native columns.
- Inherit layouts from parent groups; use a default layout otherwise.
- Keep layout settings in local KeePass configuration, without modifying KDBX entries.

## Installation

Download the ZIP from [Releases](../../releases), extract `KeePassGroupViews.dll` into the KeePass `Plugins` directory, and restart KeePass.

## Usage

1. Configure columns with **View → Configure Columns**.
2. Select a group, then choose **Tools → Group Views → Save columns for this group**.
3. Switch groups to activate their saved or inherited layouts.
4. Choose **Use inherited columns** to remove a group override.
5. Choose **Set current columns as default** for groups without overrides.

Group context menus also offer save and inherit commands. Changes to column widths and order in saved group layouts are retained when switching groups.

## Build

Requires .NET SDK and an official KeePass 2.x executable.

```powershell
dotnet build src/KeePassGroupViews -c Release -p:KeePassReferencePath="C:\Path\To\KeePass.exe"
```

License: GPL-2.0-or-later.
