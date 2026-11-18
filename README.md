# KeePassGroupViews

**Per-group column layouts for KeePass 2.x.** Tested on 2.62 (Windows, Linux/Mono).

Choose which columns to show, in what order, and at what width for each group. Your layout is restored when you return to that group.

## Installation

Copy `KeePassGroupViews.dll` from the [latest release](https://github.com/BlackF1re/KeePassGroupViews/releases/latest) to `Plugins/` and restart KeePass.

## Usage

- **View -> Configure Columns** - edits save automatically for the selected group.
- **Tools -> Group Views**
  - **Save columns for this group** - pin the current layout.
  - **Use inherited columns** - use the nearest parent layout or the default.
  - **Set current columns as default** - change the fallback.

> [!NOTE]
> Layouts stay in your local KeePass configuration.

> [!WARNING]
> **Use inherited columns** removes the group's saved layout.

## Build

```powershell
dotnet build src/KeePassGroupViews -c Release -p:KeePassReferencePath="C:\Path\To\KeePass.exe"
```
