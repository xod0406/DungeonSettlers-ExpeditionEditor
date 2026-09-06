# Expedition Editor v0.9 values

The in-game picker already includes these values.

## Main skills
Sword, Greatsword, Shield, Blunt, Spear, Bow, FireMagic, WaterMagic, NatureMagic

## Sub skills
Warrior, Guardian, Fortress, Execution, Berserker, Burst, Tide, Harmony, Blitz, Stake

## General recruit traits
HappyFool, Curious, BellyBeggar, LightEater, Optimistic, Pessimistic, CunningGetaway, Psychopath, DullTongue, Adventurous, Pacifist, RapidRecoverd, SlowLearner, SettlementPrefer, Townsfolk, Claustrophobic, NatureLover, Butterfingers, SimpleMinded, Perfectionist, IndoorPerson, Impatient, HateHuman, HateElf, HateLizard, HateLycan, HeavySnorer, Shooter, Blessed

## General recruit backgrounds
Begger, Blacksmith, Butcher, Carpenter, Conscript, Deserter, Hunter, Messenger, Miner, Thief, Undertaker, ChiefBodyguard, CursedChild, Druid, ExiledLord, Gardener, Mage, Mercenary, NobleAdventurer, Philosopher, Prodigy, ScaleCraftsman, Scout, Squire, SwampKeeper, TavernRunner, Carter, NightWatch

## Notes
- `Vanilla` on Background keeps the generated background.
- `Vanilla` on Trait 1 keeps the entire generated trait list.
- `None` on Trait 1 makes a custom empty trait list (unless Trait 2/3 are selected).
- `Vanilla` on Main skill 1 keeps the generated main-skill list.
- `Vanilla` on Sub skill 1 keeps the generated sub-skill list.


## Talent grades (v1.0)

- Poor (-1)
- Moderate (0)
- Outstanding (1)
- Exceptional (2)
- Genius (3)

Use `LockTalents=true` per character for individual grades. Disable the global native all-Genius option when testing individual grades.


## v1.1 per-slot talent note
Per-slot talents are now applied natively during the selected slot's reroll. Disable the global all-Genius switch before using them.


## v1.3.2 hidden unfinished sub skills
These enum values still exist in the game data but are intentionally not shown in the editor picker because they are unfinished: Logging, Mining, Cooking, Crafting, Construction, Unbreakable.

## v1.4 persistence note
The create-campaign UI candidate is only a preview. v1.4 records each final slot's `UnitProfileKey` and re-applies editor values to the token-regenerated candidate during campaign start. This is required for edited values to survive the Next button.


## v1.9 final status persistence
Major stats and talent grades are additionally re-applied to the spawned founder StatusComponent generated-stat values during campaign start.
