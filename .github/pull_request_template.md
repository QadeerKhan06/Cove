## Card

<!-- e.g. [KAN-6 / K-04] Code style and pull request template -->

## What changed

<!-- A few bullets: what you changed and why. -->

-

## How to test

<!-- The card's done-when steps, written so a teammate can follow them on their own PC. -->

1.

## Screenshots

<!-- Required for any UI change (pill, panels, settings). Delete this section if there's no UI. -->

## Reviewers

<!-- Pick the owner(s) whose area this touches (CLAUDE.md 5.2). -->

- [ ] UI → Lead (@QadeerKhan06)
- [ ] Jobs, files, conversion → File engine (@MuhammadMir786)
- [ ] Audio, devices, shortcuts, settings, installer → System modules (@SMK-BTW)
- [ ] Contracts in `Cove.Core/Contracts` → all three

## Checklist

- [ ] Branch is named `feature/<area>-<thing>` or `fix/<area>-<thing>`
- [ ] Builds with no new warnings
- [ ] Logic has unit tests, and all tests pass
- [ ] The card's done-when check passes on my PC
- [ ] New settings appear in Settings and survive a restart
- [ ] Works with the keyboard and has screen-reader names (UI only)
- [ ] Any decision made is one line in `docs/decisions.md`
