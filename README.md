# LootChatReaderLU4

Current version: **1.10**

LootChatReaderLU4 is a local Windows application that reads the LU4 system chat from a selected screen area and keeps loot statistics.

## Recognized messages

- yellow `You have obtained ...` and `You have earned ...` — received-item events (the bundled catalog type decides the summary table);
- white `You have acquired ... XP and ... SP` — experience and SP;
- Adena is counted separately from item drops.

## How it works

The application periodically captures only the window rectangle selected by the user. Yellow and white chat rows are extracted by color and recognized locally with Tesseract OCR. Item names are matched against the bundled `mw2.wiki` item index to correct common OCR mistakes. The catalog also contains the wiki item type and subtype, such as `Quest Item`, `Other`, or `Other / Material`. Summary placement is based strictly on that catalog type rather than on whether the chat used `obtained` or `earned`. The item catalog and every icon currently available from the wiki are embedded in the EXE, so recognition and icon display work without an internet connection.

Each top-level tracker tab has independent window/area settings, monitoring controls, statistics, timer, OCR state, and tracking history. Use the **+** tab to create another tracker. Double-click a tracker name, or right-click it and choose **Rename**, to change its name. Right-click and choose **Delete** to remove it.

The **Summary** tab shows aggregated drops and quest items. **Full Logs** keeps every accepted event. XP, SP, Adena, and active session time are displayed separately. **Share** copies the currently displayed summary to the clipboard.

The arrow buttons around the capture preview place a transparent in-game overlay on the selected side of the chat area. The overlay is **Off** by default; clicking the currently selected arrow again turns it off. Adena, XP, SP, and **More** are arranged horizontally above/below the chat and vertically to its left/right. The main panel exactly matches the selected chat width when placed above/below and its height when placed left/right. **More** opens either the normal-item or quest-item list for the current session. Hold **Shift** to interact with every overlay control: open a menu, choose a list, close it, scroll it, drag the detail header to move it, or drag its right/bottom edges to resize it. Without Shift, all overlay windows are click-through and game input passes through them. Detail position and size are saved independently for each tracker.

A session remains active across Stop/Start cycles. **Clear All** closes the current session and begins a new one immediately if monitoring is running. Closing the application also closes every started session. Completed sessions are saved per tracker and listed newest-first in **Tracking history**; selecting one restores its totals, item lists, full logs, and elapsed time in read-only mode.

Mouse-wheel activity inside the selected chat area starts a resynchronization. Rows displayed again after scrolling become a new baseline and are not added to the statistics a second time. Scrolling outside the selected area has no effect.

Screenshots are never written to disk. Tracker settings and completed tracking histories are stored in `%LOCALAPPDATA%\LU4LootChatReader`. OCR data is extracted there on first use. No icon download or catalog refresh is performed at runtime.

## Usage

1. Start `LootChatReader.exe` and the LU4 client.
2. Rename the initial tracker if needed, or click **+** to create trackers for additional game windows.
3. In each tracker, click **Select Window / Area**, select its LU4 window, and mark only the system chat message area.
4. Click **Start** independently in every tracker that should be monitored.
5. Use **Stop** to pause a tracker and **Clear All** to archive and reset its current statistics.
6. Optionally click an arrow around the preview to enable the in-game overlay; hold **Shift** while using it.

Windowed or borderless-windowed game mode is recommended. Exclusive fullscreen can prevent Windows screen capture from reading the game frame.

The capture is bound to the selected game window through Windows Graphics Capture. Other applications may cover the game without affecting recognition. If the game is minimized, closed, restarting, or temporarily stops producing frames, OCR pauses silently while monitoring and elapsed time continue. Recognition resumes automatically after the selected window becomes available again.

## Build

Requirements:

- Windows 10 or Windows 11 x64;
- .NET 9 SDK.

Build and publish a self-contained single-file EXE:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

The resulting application is `publish\LootChatReader.exe`.
