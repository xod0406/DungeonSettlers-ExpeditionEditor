# Technical Notes - v2.1.4 Save-only Native Persistence Fix

This release keeps the v2.1.3-derived founder creation and talent application path intact.

The final persistence fix does not leave talent compensation in the live `StatusComponent`. Instead, only while `CampaignSaveData` is being serialized, it temporarily calls the game's own `StatusComponent.SetGeneratedValue(...)` method with a serialization target that compensates for the numeric contribution of the selected talent grade.

Conceptually:

```text
serialization target = configured final major stat - talent delta
```

Examples:

- Final 10 with Poor (-1): serialize 11, then load applies -1 -> 10.
- Final 11 with Moderate (0): serialize 11 -> 11.
- Final 12 with Outstanding (+1): serialize 11, then load applies +1 -> 12.
- Final 13 with Exceptional (+2): serialize 11, then load applies +2 -> 13.
- Final 14 with Genius (+3): serialize 11, then load applies +3 -> 14.

Immediately after serialization, the configured live major-stat targets are restored through the same game-native method. A Harmony finalizer also attempts restoration if serialization throws. Talent values themselves are not suppressed or rewritten.
