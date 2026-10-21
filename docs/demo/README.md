# Foam DA demo

[Download the Windows installer](https://github.com/bradmdesign/Foam-DA-public/releases/latest) · [Project source](../../README.md)

## Expanded game canvas

Choose a window size that suits your display, including ultrawide, 1920 × 1080, and smaller windows. The game view scales inside the host, with the original controls and the Library available together.

![Measured play-area comparison: 162.5% more map area](screen-area.png)

In this example, the play area is **162.5% larger**: **72.7% wider** and **51.9% taller**. The gold outline shows the original view; green shows the expanded view. The original HUD and Library remain available.

<details>
<summary>How the percentages are calculated</summary>

The native full-HUD MAP control grows from 616 × 308 to 1064 × 468 logical pixels at 100% play zoom. Area gain = `(1064 × 468 ÷ (616 × 308) − 1) × 100 = 162.46%`. These percentages measure additional rendered map space, excluding display scaling; server visibility limits still apply. Other chosen layouts have different gains.

</details>

![Game with skill planning in the bottom-right Library](game-library.png)

## You no longer need to Alt+Tab

Nearly every piece of Dark Ages information you would otherwise look up on a website is now right in your client. Search quests, skills, items, spells, and maps in the lightning-fast bottom-right Library. Check requirements, item costs, experience, ability points, icons, sprites, and related records while you play—or open the Library on its own.

![Library with ability icons and item requirements](library.png)

## Built-in macros and hotkeys

Keep skill training and key combinations in the same client, reducing the need for extra programs. The Macro panel lets you choose skills or spells to train, or use Simple mode to repeat a key sequence at your chosen interval. Enable Assail to press Space every half-second; macros can continue in other game tabs while you do something else.

The Hotkeys panel turns shortcuts such as **Shift+1** or **Alt+2** into a sequence of keypresses, including timed pauses. For example, `s1234 space (pause:500) 5 6` opens the skill panel, presses 1–4 and Space, waits half a second, then presses 5 and 6. Shortcuts run with the game focused and chat or dialogue closed.

## Automatic updates

Updates run automatically. A pending app update must finish before the host opens. Library records update independently, and the host reattaches running games after updating. Offline use remains available when no update is pending.

![Signed update settings](updates.png)

Install the separately obtained Dark Ages 7.41 client, run the Foam DA installer, choose that game folder, then use **Launch new tab**. The installer includes the offline Library, its local runtime and updater. It contains no full Dark Ages game or saved accounts.
