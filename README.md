# LootChatReaderLU4

Current version: **1.18**

LootChatReaderLU4 is a local Windows application that reads the LU4 system chat from a selected screen area and keeps loot statistics.

## Recognized messages

- yellow `You have obtained ...` and `You have earned ...` — received-item events (the bundled catalog type decides the summary table);
- white `You have acquired ... XP and ... SP` — experience and SP;
- Adena is counted separately from item drops.

## How it works

The application periodically captures only the window rectangle selected by the user. Yellow and white chat rows are extracted by color and recognized locally with Tesseract OCR. Item names are matched against the bundled `mw2.wiki` item index to correct common OCR mistakes. The catalog also contains the wiki item type and subtype, such as `Quest Item`, `Other`, or `Other / Material`. Summary placement is based strictly on that catalog type rather than on whether the chat used `obtained` or `earned`. The item catalog and every icon currently available from the wiki are embedded in the EXE, so recognition and icon display work without an internet connection.

Each top-level tracker tab has independent window/area settings, monitoring controls, statistics, timer, OCR state, and tracking history. Use the **+** tab to create another tracker. Double-click a tracker name, or right-click it and choose **Rename**, to change its name. Right-click and choose **Delete** to remove it.

The **Summary** tab shows aggregated drops and quest items. **Full Logs** keeps every accepted event. XP, SP, Adena, and active session time are displayed separately. **Share** copies the currently displayed summary to the clipboard.

The **Overlay Settings** section configures all in-game panels for the current tracker. Adena, XP, and SP can be placed to the left, above, to the right, or below the selected chat area. The obtained-items and quest-items panels each have an independent visually selected rectangle inside the game window; that rectangle controls both position and size. Their visibility is controlled by the two checkboxes on the tracker main page.

All overlay panels are permanently transparent to mouse input. There is no `More` button, Shift mode, in-game menu, dragging, resizing, or overlay interaction hook. Position and size are changed only through **Overlay Settings**, so the overlay cannot interfere with L2 controls. Each overlay is natively owned by its selected game window: only the foreground game's panels are raised, so multiple tracked clients do not cover unrelated applications or each other.

A session remains active across Stop/Start cycles. **Clear All** closes the current session and begins a new one immediately if monitoring is running. Closing the application also closes every started session. Completed sessions are saved per tracker and listed newest-first in **Tracking history**; selecting one restores its totals, item lists, full logs, and elapsed time in read-only mode.

OCR differences alone are never counted as loot. The application first confirms that the chat text layer moved upward, segments the frame into physical chat-row bands, and accepts only bands that actually appeared at the bottom of the moving list. Event identity and value are not used as duplicate protection: six separate identical XP/SP or Adena rows are six separate events. Both colored item lines and neutral-gray XP/SP/system lines participate in motion detection, so XP/SP does not require a yellow drop beside it. A newly detected row is retried for several frames when its first OCR pass fails, while a stationary chat adds nothing even if the game background, spell effects, or text antialiasing change. Original pixels and cleaned color masks are both tried for difficult lines, and common small OCR errors in message verbs and catalog item names are corrected. Mouse-wheel activity inside the selected chat starts a short baseline resynchronization so scrolling history cannot replay old loot.

Screenshots are never written to disk. Tracker settings and completed tracking histories are stored in `%LOCALAPPDATA%\LU4LootChatReader`. OCR data is extracted there on first use. No icon download or catalog refresh is performed at runtime.

## Usage

1. Start `LootChatReader.exe` and the LU4 client.
2. Rename the initial tracker if needed, or click **+** to create trackers for additional game windows.
3. In each tracker, click **Select Window / Area**, select its LU4 window, and mark only the system chat message area.
4. Click **Start** independently in every tracker that should be monitored.
5. Use **Stop** to pause a tracker and **Clear All** to archive and reset its current statistics.
6. Optionally open **Overlay Settings** to select panel placement and size, then enable the item and/or quest-item overlay checkboxes on the tracker page.

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
