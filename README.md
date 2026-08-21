# LootChatReaderLU4

Current version: **1.03**

LootChatReaderLU4 is a local Windows application that reads the LU4 system chat from a selected screen area and keeps loot statistics.

## Recognized messages

- yellow `You have obtained ...` — regular drops;
- yellow `You have earned ...` — quest items;
- white `You have acquired ... XP and ... SP` — experience and SP;
- Adena is counted separately from item drops.

## How it works

The application periodically captures only the screen rectangle selected by the user. Yellow and white chat rows are extracted by color and recognized locally with Tesseract OCR. Item names are matched against the bundled `mw2.wiki` item index to correct common OCR mistakes and load item icons.

The **Summary** tab shows aggregated drops and quest items. **Full Logs** keeps every accepted event. XP, SP, Adena, and active session time are displayed separately.

Mouse-wheel activity inside the selected chat area starts a resynchronization. Rows displayed again after scrolling become a new baseline and are not added to the statistics a second time. Scrolling outside the selected area has no effect.

Screenshots and recognized chat messages are not written to disk. Settings, the extracted OCR model, the refreshed item index, and downloaded icon cache are stored in `%LOCALAPPDATA%\LU4LootChatReader`.

## Usage

1. Start `LootChatReader.exe`.
2. Click **Select Area** and select only the system chat message area.
3. Click **Start**.
4. Use **Stop** to pause monitoring and **Clear All** to reset the current statistics.

Windowed or borderless-windowed game mode is recommended. Exclusive fullscreen can prevent Windows screen capture from reading the game frame.

## Build

Requirements:

- Windows 10 or Windows 11 x64;
- .NET 9 SDK.

Build and publish a self-contained single-file EXE:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

The resulting application is `publish\LootChatReader.exe`.
