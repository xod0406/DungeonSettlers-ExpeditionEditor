using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace DungeonSettlers.ExpeditionEditor;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("DungeonSettlers.exe")]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.openai.dungeonsettlers.expeditioneditor";
    public const string PluginName = "Dungeon Settlers Expedition Editor";
    public const string PluginVersion = "2.1.3-0.4.18";

    internal static Plugin Instance { get; private set; }
    internal static readonly CharacterSettings[] Characters = new CharacterSettings[4];

    private Harmony _harmony;
    private ConfigEntry<bool> _nativeAllGenius;
    private ConfigEntry<bool> _diagnosticLogging;

    // DS_B.0.4.18 / supplied GameAssembly.dll.
    // DetermineEstablishTalents first chooses a target talent sum, then rolls six individual talents.
    // v1.1 can temporarily replace all seven RNG calls for ONE reroll, allowing exact per-slot talent profiles.
    private static readonly int[] TalentNativeRvas =
    {
        0xA48D5B, // target sum
        0xA48D8D, // Strength
        0xA48DBF, // Constitution
        0xA48DE8, // WillPower
        0xA48E11, // Intelligence
        0xA48E3A, // Agility
        0xA48E63, // Perception
    };

    private static readonly byte[][] TalentNativeExpected =
    {
        new byte[] { 0xE8, 0xC0, 0xBC, 0xEB, 0x02 },
        new byte[] { 0xE8, 0x8E, 0xBC, 0xEB, 0x02 },
        new byte[] { 0xE8, 0x5C, 0xBC, 0xEB, 0x02 },
        new byte[] { 0xE8, 0x33, 0xBC, 0xEB, 0x02 },
        new byte[] { 0xE8, 0x0A, 0xBC, 0xEB, 0x02 },
        new byte[] { 0xE8, 0xE1, 0xBB, 0xEB, 0x02 },
        new byte[] { 0xE8, 0xB8, 0xBB, 0xEB, 0x02 },
    };

    private const uint PageExecuteReadWrite = 0x40;
    private int _armedTalentSlot = -1;
    private DateTime _armedTalentAtUtc;
    private bool _customTalentPatchActive;

    // IMPORTANT: the establish screen is only a preview. The game serializes generation tokens
    // and regenerates the four founders when the campaign actually starts. Keep the final
    // portrait/profile identity of each visible slot so the regenerated candidate can be mapped
    // back to Character 1..4 and receive the same editor values again.
    private static readonly string[] FinalProfileKeyBySlot = new string[4];
    private readonly bool[] _persistenceApplied = new bool[4];
    private bool _persistenceWindowActive;
    private DateTime _persistenceWindowUntilUtc;
    private DateTime _persistenceWindowOpenedAtUtc;
    private readonly Dictionary<int, int> _persistenceSlotByGenerationSeed = new Dictionary<int, int>();

    // v1.6: the two Create Campaign UI pages do not call the presenter payload methods in the
    // actual DS_B.0.4.18 start path. The reliable runtime boundary is CampaignStartingSpawnHelper,
    // which consumes the establish tokens and immediately spawns the four founders. Keep a short
    // context window around that flow and patch the RecruitCandidateData objects produced inside it.
    private bool _campaignSpawnContextActive;
    private DateTime _campaignSpawnContextUntilUtc;
    private int _campaignSpawnSequentialCursor;
    private readonly HashSet<string> _campaignPatchedCandidateIds = new HashSet<string>(StringComparer.Ordinal);

    // v1.7: MajorStats / MajorStatTalents on RecruitCandidateData are preview/generation data.
    // The real campaign unit materializes them into StatusComponent generated-stat buckets.
    // Queue the matched founder slot when its final candidate is created, then patch the next
    // player StatusComponent.ApplyGeneratedStats call in the same campaign-start flow.
    private readonly Queue<int> _pendingFinalStatusSlots = new Queue<int>();
    private readonly bool[] _finalStatusApplied = new bool[4];
    private DateTime _pendingFinalStatusUntilUtc;
    private int _activeFounderSpawnSlot = -1;
    private int _activeFounderSpawnDepth;

    // v1.8: ApplyGeneratedStats is present in metadata but does not run on the actual founder-spawn
    // path in DS_B.0.4.17. Patch the UnitEntity returned by SpawnUnitFromCandidateData instead,
    // and reinforce the generated values for a short period in case later initialization overwrites them.
    private readonly List<PendingSpawnedFounder> _pendingSpawnedFounders = new List<PendingSpawnedFounder>();
    private bool _loggedUnitStatusDiscoveryFailure;

    // v2.0: the live founder path does call StatusComponent.SetGeneratedValue for the six major stats,
    // but mutating Harmony's object[] __args was not authoritative on IL2CPP. Capture the actual
    // StatusComponent instance during those calls and perform guarded direct writes after the game
    // writes its own values. Also force MajorStats/MajorStatTalents again at UnitSpawner.SetUnitStatus,
    // the exact transfer boundary from RecruitCandidateData to the real UnitEntity.
    private readonly HashSet<string>[] _inSpawnInterceptedStats =
    {
        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)
    };
    private readonly bool[] _unitSpawnerStatusObserved = new bool[4];
    private readonly object[] _capturedStatusComponentBySlot = new object[4];
    private readonly bool[] _talentsInjectedIntoStatus = new bool[4];
    private int _directGeneratedWriteDepth;

    internal static readonly string[] BackgroundOptions =
    {
        "Vanilla",
        "AFFECTER_Begger", "AFFECTER_Blacksmith", "AFFECTER_Butcher", "AFFECTER_Carpenter",
        "AFFECTER_Carter", "AFFECTER_ChiefBodyguard", "AFFECTER_Conscript", "AFFECTER_CursedChild",
        "AFFECTER_Deserter", "AFFECTER_Druid", "AFFECTER_ExiledLord", "AFFECTER_ForestKeeper",
        "AFFECTER_Gambler", "AFFECTER_Gardener", "AFFECTER_Hooligan", "AFFECTER_Hunter",
        "AFFECTER_Lumberjack", "AFFECTER_Mage", "AFFECTER_Mercenary", "AFFECTER_Messenger",
        "AFFECTER_Miner", "AFFECTER_NightWatch", "AFFECTER_NobleAdventurer", "AFFECTER_Peddler",
        "AFFECTER_Philosopher", "AFFECTER_Porter", "AFFECTER_Prodigy", "AFFECTER_ScaleCraftsman",
        "AFFECTER_Scout", "AFFECTER_Slave", "AFFECTER_SnakeCatcher", "AFFECTER_Squire",
        "AFFECTER_SwampKeeper", "AFFECTER_TavernRunner", "AFFECTER_Thief", "AFFECTER_Undertaker",
        "AFFECTER_Wanderer"
    };

    internal static readonly string[] TraitOptionsFirst =
    {
        "Vanilla", "None",
        "AFFECTER_HappyFool", "AFFECTER_Curious", "AFFECTER_BellyBeggar", "AFFECTER_LightEater",
        "AFFECTER_Optimistic", "AFFECTER_Pessimistic", "AFFECTER_CunningGetaway", "AFFECTER_Psychopath",
        "AFFECTER_DullTongue", "AFFECTER_Adventurous", "AFFECTER_Pacifist", "AFFECTER_RapidRecoverd",
        "AFFECTER_SlowLearner", "AFFECTER_SettlementPrefer", "AFFECTER_Townsfolk", "AFFECTER_Claustrophobic",
        "AFFECTER_NatureLover", "AFFECTER_Butterfingers", "AFFECTER_SimpleMinded", "AFFECTER_Perfectionist",
        "AFFECTER_IndoorPerson", "AFFECTER_Impatient", "AFFECTER_HateHuman", "AFFECTER_HateElf",
        "AFFECTER_HateLizard", "AFFECTER_HateLycan", "AFFECTER_HeavySnorer", "AFFECTER_Shooter",
        "AFFECTER_Blessed"
    };

    internal static readonly string[] TraitOptions = TraitOptionsFirst.Where(x => x != "Vanilla").ToArray();

    internal static readonly string[] MainSkillOptionsFirst =
    {
        "Vanilla", "Sword", "Greatsword", "Shield", "Blunt", "Spear", "Bow", "FireMagic", "WaterMagic", "NatureMagic"
    };
    internal static readonly string[] MainSkillOptions = MainSkillOptionsFirst.Where(x => x != "Vanilla").ToArray();

    // Only currently usable combat sub-skill trees are exposed in the editor.
    // The game data still contains unfinished production/life-skill enum values
    // (Logging, Mining, Cooking, Crafting, Construction, Unbreakable), but they are intentionally
    // hidden here so they cannot be selected accidentally.
    internal static readonly string[] SubSkillOptionsFirst =
    {
        "Vanilla", "Warrior", "Guardian", "Fortress", "Execution",
        "Berserker", "Burst", "Tide", "Harmony", "Blitz", "Stake"
    };
    internal static readonly string[] SubSkillOptions = SubSkillOptionsFirst.Where(x => x != "Vanilla").ToArray();

    internal static readonly string[] TalentOptions =
    {
        "Poor", "Moderate", "Outstanding", "Exceptional", "Genius"
    };

    internal void Diag(string message)
    {
        if (_diagnosticLogging?.Value == true)
            Log.LogInfo("[diag] " + message);
    }

    public override void Load()
    {
        Instance = this;
        try
        {
            BindConfig();
            SetGlobalAllGeniusPatch(_nativeAllGenius?.Value == true);

            _harmony = new Harmony(PluginGuid);
            Type ui = RequireType("Refactor.SubUI_EstablishMode");
            MethodInfo[] setSlots = ui.GetMethods(AccessTools.all)
                .Where(m => m.Name == "SetSlot" && m.GetParameters().Any(p => p.ParameterType.Name.Contains("RecruitCandidateData", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (setSlots.Length == 0)
                throw new MissingMethodException("Refactor.SubUI_EstablishMode.SetSlot not found.");

            foreach (MethodInfo m in setSlots)
            {
                _harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SetSlotPrefix)));
                Diag($"SetSlot writer patch: {m}");
            }

            InstallPersistenceHooks();
            InstallCampaignSpawnHooks();
            InstallFinalStatusHooks();

            MethodInfo[] rerollMethods = ui.GetMethods(AccessTools.all)
                .Where(m =>
                    (string.Equals(m.Name, "onRefreshSlotClicked", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(m.Name, "RefreshSlot", StringComparison.OrdinalIgnoreCase)) &&
                    m.GetParameters().Any(p => p.ParameterType == typeof(int)))
                .ToArray();
            if (rerollMethods.Length == 0)
            {
                string discovered = string.Join(" | ", ui.GetMethods(AccessTools.all)
                    .Where(m => m.Name.IndexOf("RefreshSlot", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => m.ToString()));
                Log.LogWarning($"No usable reroll-slot method was found. Manual ARM mode remains available. RefreshSlot-like methods: {discovered}");
            }
            else
            {
                foreach (MethodInfo m in rerollMethods)
                {
                    _harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(RerollSlotPrefix)));
                    Diag($"Best-effort reroll-slot native talent hook: {m}");
                }
            }

            AddComponent<EditorOverlay>();

            bool anyPerSlotTalents = Characters.Any(c => c.LockTalents.Value);
            if (anyPerSlotTalents && _nativeAllGenius.Value)
            {
                SetGlobalAllGeniusPatch(false);
                _nativeAllGenius.Value = false;
                Config.Save();
                Log.LogWarning("Per-slot talent lock is enabled, so the global native all-Genius patch was disabled to avoid conflicts.");
            }

            Log.LogInfo("Expedition Editor v2.1.3 (DS_B.0.4.18) loaded. Press F4 to open/close the editor.");
            Diag("Race/gender/profile remain vanilla. Campaign persistence uses the verified SetUnitStatus + direct StatusComponent write path.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patch/UI installation failed: {ex}");
        }
    }

    private void InstallPersistenceHooks()
    {
        // The v1.4 trigger (OnEstablishSelectionConfirmed) exists in metadata but is not actually
        // invoked by the Next/start path in DS_B.0.4.18. v1.5 therefore hooks the payload builder
        // itself. This is still on the UI/presenter side and avoids the unsafe TryGenerate bridge.
        try
        {
            Type presenter = RequireType("Refactor.UI.CreateCampaignPresenter");

            MethodInfo[] builders = presenter.GetMethods(AccessTools.all)
                .Where(m => string.Equals(m.Name, "BuildEstablishSelectionPayload", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo m in builders)
            {
                if (m.ReturnType == typeof(void))
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(EstablishPayloadBuiltVoidPostfix)));
                else
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(EstablishPayloadBuiltPostfix)));
                Diag($"Persistence payload-builder hook: {m}");
            }
            if (builders.Length == 0)
                Log.LogWarning("Persistence: BuildEstablishSelectionPayload was not found.");

            // Keep the old callback only as a harmless fallback. It did not fire in the user's v1.4
            // test, but if another game path uses it it can still open the same debounced window.
            MethodInfo[] confirms = presenter.GetMethods(AccessTools.all)
                .Where(m => string.Equals(m.Name, "OnEstablishSelectionConfirmed", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo m in confirms)
            {
                _harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(EstablishConfirmPrefix)));
                Diag($"Persistence fallback confirm hook: {m}");
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"Persistence presenter hook failed: {ex}");
        }

        // The campaign-start token path eventually rebuilds RecruitCandidateData in these safe
        // Create* helpers. We patch only their return values, never TryGenerate/TryGenerateFromToken.
        try
        {
            Type generator = RequireType("Refactor.EstablishUnitGenerator");
            string[] wanted =
            {
                "CreateRuleBasedCandidate",
                "CreateCandidateFromPreset",
                "CreateEstablishCandidateWithFallbackRecruitHelper",
                "CreateLegacyPresetWithFallbackRecruitHelper"
            };

            MethodInfo[] creators = generator.GetMethods(AccessTools.all)
                .Where(m => wanted.Contains(m.Name) && m.ReturnType != typeof(void) &&
                            m.ReturnType.Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            foreach (MethodInfo m in creators)
            {
                _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(FinalCandidatePostfix)));
                Diag($"Persistence final-candidate hook: {m}");
            }

            if (creators.Length == 0)
            {
                string discovered = string.Join(" | ", generator.GetMethods(AccessTools.all)
                    .Where(m => wanted.Contains(m.Name))
                    .Select(m => $"{m} -> {m.ReturnType.FullName}"));
                Log.LogWarning($"Persistence: no candidate-returning final creator was hookable. Discovered: {discovered}");
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"Persistence candidate hook failed: {ex}");
        }
    }

    private void InstallCampaignSpawnHooks()
    {
        // v1.4/v1.5 proved that the CreateCampaignPresenter callbacks are not part of the actual
        // button path used by this build. CampaignStartingSpawnHelper *is* the runtime component
        // responsible for consuming establish tokens and spawning the initial player units.
        try
        {
            Type spawnHelper = RequireType("Refactor.CampaignStartingSpawnHelper");
            string[] wanted =
            {
                "SpawnCampaignStartingEntities",
                "SpawnStartingTablePlayerUnits",
                "TrySpawnEstablishPlayerUnits"
            };

            MethodInfo[] methods = spawnHelper.GetMethods(AccessTools.all)
                .Where(m => wanted.Contains(m.Name))
                .ToArray();

            foreach (MethodInfo m in methods)
            {
                _harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(CampaignStartingSpawnPrefix)));
                Diag($"Campaign-start spawn hook: {m}");
            }

            if (methods.Length == 0)
                Log.LogWarning("CampaignStartingSpawnHelper was found, but no expected spawn methods were exposed.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Campaign-start spawn hook failed: {ex}");
        }

        // CampaignStartingSpawnHelper owns a RecruitHelper. The establish-token path may fall back
        // to RecruitHelper.CreateEstablishCandidate instead of the EstablishUnitGenerator Create*
        // helpers patched in v1.5, so patch candidate-returning RecruitHelper methods as well.
        try
        {
            Type recruit = RequireType("Refactor.Main.Event.RecruitHelper");
            MethodInfo[] candidateMethods = recruit.GetMethods(AccessTools.all)
                .Where(m => m.ReturnType != typeof(void) &&
                            m.ReturnType.Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (m.Name.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             m.Name.IndexOf("Establish", StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();

            foreach (MethodInfo m in candidateMethods)
            {
                _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(FinalCandidatePostfix)));
                Diag($"Campaign RecruitHelper candidate hook: {m}");
            }

            if (candidateMethods.Length == 0)
                Log.LogWarning("RecruitHelper was found, but no RecruitCandidateData-returning candidate method was hookable.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Campaign RecruitHelper hook failed: {ex}");
        }

        // Some token paths expose an EstablishCandidateFactory object. Patch only methods that
        // directly return RecruitCandidateData; no TryGenerate bridge is touched.
        try
        {
            Type factory = RequireType("Refactor.EstablishCandidateFactory");
            MethodInfo[] candidateMethods = factory.GetMethods(AccessTools.all)
                .Where(m => m.ReturnType != typeof(void) &&
                            m.ReturnType.Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            foreach (MethodInfo m in candidateMethods)
            {
                _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(FinalCandidatePostfix)));
                Diag($"Campaign EstablishCandidateFactory hook: {m}");
            }
        }
        catch (Exception ex)
        {
            // Factory type is optional on some generated interop layouts.
            Log.LogWarning($"Campaign EstablishCandidateFactory hook unavailable: {ex.Message}");
        }
    }

    private void InstallFinalStatusHooks()
    {
        try
        {
            Type status = RequireType("Refactor.Component.StatusComponent");

            // Authoritative path in DS_B.0.4.17: generated values are assigned through
            // SetGeneratedValue(StatType, Single). Mutate those arguments only while a mapped
            // founder is being spawned, so normal gameplay remains untouched.
            MethodInfo[] setters = status.GetMethods(AccessTools.all)
                .Where(m => string.Equals(m.Name, "SetGeneratedValue", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo m in setters)
            {
                _harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(GeneratedValueInterceptPrefix)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(GeneratedValueInterceptPostfix)));
                Diag($"IN-SPAWN generated-value capture/direct-write hook: {m}");
            }
            if (setters.Length == 0)
                Log.LogWarning("StatusComponent.SetGeneratedValue was not found; in-spawn stat/talent interception cannot run.");

            // Ensure our configured generated bucket is present before materialization and restored
            // after any default-reset pass that occurs inside founder spawning.
            foreach (MethodInfo m in status.GetMethods(AccessTools.all))
            {
                if (string.Equals(m.Name, "BuildRawStats", StringComparison.Ordinal) ||
                    string.Equals(m.Name, "ApplyGeneratedStats", StringComparison.Ordinal))
                {
                    _harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(StatusMaterializePrefix)));
                    Diag($"IN-SPAWN status materialization hook: {m}");
                }
                else if (string.Equals(m.Name, "ResetToDefaultStats", StringComparison.Ordinal))
                {
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(StatusResetPostfix)));
                    Diag($"IN-SPAWN status reset recovery hook: {m}");
                }
            }

            // Diagnostic/fallback boundary: UnitSpawner is the class that initializes playable-unit
            // status. If a StatusComponent is directly exposed in its arguments, apply there as well.
            try
            {
                Type unitSpawner = RequireType("Refactor.Main.UnitSpawner");
                foreach (MethodInfo m in unitSpawner.GetMethods(AccessTools.all)
                    .Where(m => m.Name == "SetUnitStatus" || m.Name == "AdjustStatus" || m.Name == "SpawnPlayableUnit"))
                {
                    _harmony.Patch(m,
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(UnitSpawnerStatusPrefix)),
                        postfix: new HarmonyMethod(typeof(Plugin), nameof(UnitSpawnerStatusPostfix)));
                    Diag($"UnitSpawner founder-status hook: {m}");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"UnitSpawner status hooks unavailable: {ex.Message}");
            }

            Type lifecycle = RequireType("Refactor.Main.EntityLifecycleHelper");
            MethodInfo[] spawnFromCandidate = lifecycle.GetMethods(AccessTools.all)
                .Where(m => string.Equals(m.Name, "SpawnUnitFromCandidateData", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo m in spawnFromCandidate)
            {
                _harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(FounderUnitSpawnPrefix)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(FounderUnitSpawnPostfix)));
                Diag($"Founder unit spawn boundary hook: {m}");
            }
            if (spawnFromCandidate.Length == 0)
                Log.LogWarning("EntityLifecycleHelper.SpawnUnitFromCandidateData was not found; founder generated-stat interception cannot map slots.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Final founder status hook failed: {ex}");
        }
    }

    private static void GeneratedValueInterceptPrefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || __args == null || __args.Length < 2) return;
        if (p._directGeneratedWriteDepth > 0) return;
        if (!p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;

        // Do not mutate __args here. On the IL2CPP bridge this looked successful in logs but the
        // game still materialized its original values. The postfix performs a real guarded call.
        p._capturedStatusComponentBySlot[slot] = __instance;
    }

    private static void GeneratedValueInterceptPostfix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || __instance == null || __args == null || __args.Length < 2) return;
        if (p._directGeneratedWriteDepth > 0) return;
        if (!p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;

        p._capturedStatusComponentBySlot[slot] = __instance;
        string statName = __args[0]?.ToString();
        if (string.IsNullOrEmpty(statName)) return;

        try
        {
            int? desired = p.GetConfiguredGeneratedStatusValue(slot, statName);
            if (desired.HasValue)
            {
                p._directGeneratedWriteDepth++;
                try { SetGeneratedStatusValue(__instance, statName, desired.Value); }
                finally { p._directGeneratedWriteDepth--; }

                if (p._inSpawnInterceptedStats[slot].Add(statName))
                    p.Diag($"IN-SPAWN STATUS Slot {slot + 1}: game wrote {statName}={__args[1]}; direct override committed -> {desired.Value}.");
            }

            // The founder path does not naturally call SetGeneratedValue for Talent* stats. Once
            // the major-stat status component is in hand, seed all six talent generated values
            // directly. Repeating at SetUnitStatus/spawn exit makes this resilient to later resets.
            if (!p._talentsInjectedIntoStatus[slot] &&
                ((p._nativeAllGenius != null && p._nativeAllGenius.Value) || Characters[slot].LockTalents.Value))
            {
                p._directGeneratedWriteDepth++;
                try
                {
                    int writes = p.ApplyConfiguredTalentValuesDirect(__instance, slot);
                    if (writes > 0)
                    {
                        p._talentsInjectedIntoStatus[slot] = true;
                        p.Diag($"IN-SPAWN TALENTS Slot {slot + 1}: directly seeded {writes} Talent* generated values on captured StatusComponent.");
                    }
                }
                finally { p._directGeneratedWriteDepth--; }
            }
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"IN-SPAWN STATUS Slot {slot + 1}: direct override failed for {statName}: {ex.Message}");
        }
    }

    private static void StatusMaterializePrefix(MethodBase __originalMethod, object __instance)
    {
        Plugin p = Instance;
        if (p == null || __instance == null || !p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;
        try
        {
            int writes = p.ApplyConfiguredGeneratedValuesDirect(__instance, slot);
            if (writes > 0 && p._inSpawnInterceptedStats[slot].Add("@" + __originalMethod.Name))
                p.Diag($"IN-SPAWN STATUS Slot {slot + 1}: seeded {writes} configured values before {__originalMethod.Name}.");
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"IN-SPAWN STATUS Slot {slot + 1}: materialize seed failed at {__originalMethod.Name}: {ex.Message}");
        }
    }

    private static void StatusResetPostfix(MethodBase __originalMethod, object __instance)
    {
        Plugin p = Instance;
        if (p == null || __instance == null || !p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;
        try
        {
            int writes = p.ApplyConfiguredGeneratedValuesDirect(__instance, slot);
            if (writes > 0)
                p.Diag($"IN-SPAWN STATUS Slot {slot + 1}: restored {writes} values after {__originalMethod.Name}.");
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"IN-SPAWN STATUS Slot {slot + 1}: reset recovery failed: {ex.Message}");
        }
    }

    private static void UnitSpawnerStatusPrefix(MethodBase __originalMethod, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || !p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;

        try
        {
            string args = __args == null ? "" : string.Join(", ", __args.Select((a, i) => $"{i}:{a?.GetType().FullName ?? "null"}"));
            p.Diag($"UNITSPAWNER STATUS Slot {slot + 1}: ENTER {__originalMethod.Name} args=[{args}]");

            // This is the last known boundary where the final RecruitCandidateData is handed to
            // status initialization. Force the candidate's major stats/talents here, not earlier.
            if (string.Equals(__originalMethod.Name, "SetUnitStatus", StringComparison.Ordinal) && __args != null)
            {
                object candidate = __args.Select(FindRecruitCandidate).FirstOrDefault(x => x != null);
                if (candidate != null)
                {
                    CharacterSettings c = Characters[slot];
                    if (c.Apply.Value)
                    {
                        if (c.LockMajorStats.Value) ApplyMajorStats(candidate, c, slot);
                        if ((p._nativeAllGenius != null && p._nativeAllGenius.Value) || c.LockTalents.Value) ApplyTalents(candidate, c, slot);
                        p.Diag($"STATUS TRANSFER Slot {slot + 1}: forced MajorStats/MajorStatTalents on the exact SetUnitStatus candidate.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"UNITSPAWNER STATUS Slot {slot + 1}: prefix failed at {__originalMethod.Name}: {ex.Message}");
        }
    }

    private static void UnitSpawnerStatusPostfix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || !p._campaignSpawnContextActive || p._activeFounderSpawnDepth <= 0) return;
        int slot = p._activeFounderSpawnSlot;
        if (slot < 0 || slot >= Characters.Length) return;

        try
        {
            object status = p._capturedStatusComponentBySlot[slot];
            if (status != null && (string.Equals(__originalMethod.Name, "SetUnitStatus", StringComparison.Ordinal) ||
                                   string.Equals(__originalMethod.Name, "SpawnPlayableUnit", StringComparison.Ordinal)))
            {
                p._directGeneratedWriteDepth++;
                int writes;
                try { writes = p.ApplyConfiguredGeneratedValuesDirect(status, slot); }
                finally { p._directGeneratedWriteDepth--; }
                p.Diag($"STATUS TRANSFER Slot {slot + 1}: re-committed {writes} generated stat/talent values after {__originalMethod.Name}.");
            }
            p.Diag($"UNITSPAWNER STATUS Slot {slot + 1}: EXIT {__originalMethod.Name}.");
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"UNITSPAWNER STATUS Slot {slot + 1}: postfix failed at {__originalMethod.Name}: {ex.Message}");
        }
    }

    private static object FindDirectStatusComponent(object root)
    {
        if (root == null) return null;
        Type target;
        try { target = RequireType("Refactor.Component.StatusComponent"); } catch { return null; }
        if (root is object[] arr)
        {
            foreach (object x in arr)
            {
                object hit = FindDirectStatusComponent(x);
                if (hit != null) return hit;
            }
            return null;
        }
        Type t = root.GetType();
        if (target.IsAssignableFrom(t) || string.Equals(t.FullName, target.FullName, StringComparison.Ordinal)) return root;
        foreach (FieldInfo f in t.GetFields(AccessTools.all))
        {
            if (f.FieldType == target || string.Equals(f.FieldType.FullName, target.FullName, StringComparison.Ordinal))
            {
                try { object v = f.GetValue(root); if (v != null) return v; } catch { }
            }
        }
        foreach (PropertyInfo prop in t.GetProperties(AccessTools.all))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            if (prop.PropertyType == target || string.Equals(prop.PropertyType.FullName, target.FullName, StringComparison.Ordinal))
            {
                try { object v = prop.GetValue(root); if (v != null) return v; } catch { }
            }
        }
        return null;
    }

    private int? GetConfiguredGeneratedStatusValue(int slot, string statName)
    {
        if (slot < 0 || slot >= Characters.Length || string.IsNullOrEmpty(statName)) return null;
        CharacterSettings c = Characters[slot];
        if (c == null || !c.Apply.Value) return null;
        switch (statName)
        {
            case "Strength": return c.LockMajorStats.Value ? c.Strength.Value : (int?)null;
            case "Constitution": return c.LockMajorStats.Value ? c.Constitution.Value : (int?)null;
            case "WillPower": return c.LockMajorStats.Value ? c.WillPower.Value : (int?)null;
            case "Intelligence": return c.LockMajorStats.Value ? c.Intelligence.Value : (int?)null;
            case "Agility": return c.LockMajorStats.Value ? c.Agility.Value : (int?)null;
            case "Perception": return c.LockMajorStats.Value ? c.Perception.Value : (int?)null;
        }

        bool allGenius = _nativeAllGenius != null && _nativeAllGenius.Value;
        if (!allGenius && !c.LockTalents.Value) return null;
        switch (statName)
        {
            case "TalentStrength": return allGenius ? 3 : TalentValue(c.StrengthTalent.Value);
            case "TalentConstitution": return allGenius ? 3 : TalentValue(c.ConstitutionTalent.Value);
            case "TalentWillPower": return allGenius ? 3 : TalentValue(c.WillPowerTalent.Value);
            case "TalentIntelligence": return allGenius ? 3 : TalentValue(c.IntelligenceTalent.Value);
            case "TalentAgility": return allGenius ? 3 : TalentValue(c.AgilityTalent.Value);
            case "TalentPerception": return allGenius ? 3 : TalentValue(c.PerceptionTalent.Value);
        }
        return null;
    }

    private int ApplyConfiguredTalentValuesDirect(object status, int slot)
    {
        if (status == null || slot < 0 || slot >= Characters.Length) return 0;
        string[] names =
        {
            "TalentStrength", "TalentConstitution", "TalentWillPower",
            "TalentIntelligence", "TalentAgility", "TalentPerception"
        };
        int writes = 0;
        foreach (string name in names)
        {
            int? v = GetConfiguredGeneratedStatusValue(slot, name);
            if (!v.HasValue) continue;
            writes += SetGeneratedStatusValue(status, name, v.Value);
        }
        object generated = ReadMember(status, "_generatedStats") ?? ReadMember(status, "GeneratedStats") ?? ReadMember(status, "generatedStats");
        if (generated != null)
        {
            CharacterSettings c = Characters[slot];
            WriteGeneratedStatusDictionary(generated, c, false, true, _nativeAllGenius != null && _nativeAllGenius.Value);
        }
        return writes;
    }

    private int ApplyConfiguredGeneratedValuesDirect(object status, int slot)
    {
        if (status == null || slot < 0 || slot >= Characters.Length) return 0;
        string[] names =
        {
            "Strength", "Constitution", "WillPower", "Intelligence", "Agility", "Perception",
            "TalentStrength", "TalentConstitution", "TalentWillPower", "TalentIntelligence", "TalentAgility", "TalentPerception"
        };
        int writes = 0;
        foreach (string name in names)
        {
            int? v = GetConfiguredGeneratedStatusValue(slot, name);
            if (!v.HasValue) continue;
            writes += SetGeneratedStatusValue(status, name, v.Value);
        }
        object generated = ReadMember(status, "_generatedStats") ?? ReadMember(status, "GeneratedStats") ?? ReadMember(status, "generatedStats");
        if (generated != null)
        {
            CharacterSettings c = Characters[slot];
            WriteGeneratedStatusDictionary(generated, c, c.LockMajorStats.Value,
                (_nativeAllGenius != null && _nativeAllGenius.Value) || c.LockTalents.Value,
                _nativeAllGenius != null && _nativeAllGenius.Value);
        }
        return writes;
    }

    private static void FounderUnitSpawnPrefix(MethodBase __originalMethod, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || !p._campaignSpawnContextActive) return;
        try
        {
            object candidate = null;
            if (__args != null)
            {
                foreach (object arg in __args)
                {
                    candidate = FindRecruitCandidate(arg);
                    if (candidate != null) break;
                }
            }

            int slot = p.MatchFounderSlot(candidate);
            if (slot < 0 && p._pendingFinalStatusSlots.Count > 0)
                slot = p._pendingFinalStatusSlots.Peek();

            p._activeFounderSpawnDepth++;
            p._activeFounderSpawnSlot = slot;
            if (slot >= 0 && slot < Characters.Length)
            {
                p._inSpawnInterceptedStats[slot].Clear();
                p._unitSpawnerStatusObserved[slot] = false;
                p._capturedStatusComponentBySlot[slot] = null;
                p._talentsInjectedIntoStatus[slot] = false;
            }
            string profile = candidate == null ? null : ReadStringProperty(candidate, "UnitProfileKey");
            p.Diag($"FOUNDER SPAWN ENTER -> slot={(slot >= 0 ? (slot + 1).ToString() : "?")}, profile={profile ?? "<none>"}, method={__originalMethod.Name}.");
        }
        catch (Exception ex)
        {
            p.Log.LogWarning($"Founder spawn prefix mapping failed: {ex.Message}");
        }
    }

    private static void FounderUnitSpawnPostfix(MethodBase __originalMethod, object __result)
    {
        Plugin p = Instance;
        if (p == null) return;

        int slot = p._activeFounderSpawnSlot;
        if (slot >= 0 && slot < Characters.Length)
        {
            object captured = p._capturedStatusComponentBySlot[slot];
            if (captured != null)
            {
                try
                {
                    p._directGeneratedWriteDepth++;
                    int finalWrites;
                    try { finalWrites = p.ApplyConfiguredGeneratedValuesDirect(captured, slot); }
                    finally { p._directGeneratedWriteDepth--; }
                    p.Diag($"STATUS TRANSFER Slot {slot + 1}: FINAL re-commit at founder spawn exit -> {finalWrites} writes.");
                    if (finalWrites > 0)
                        p.Log.LogInfo($"Expedition Editor: Slot {slot + 1} campaign stats/talents applied ({finalWrites} generated values).");
                }
                catch (Exception ex)
                {
                    p.Log.LogWarning($"STATUS TRANSFER Slot {slot + 1}: final re-commit failed: {ex.Message}");
                }
            }

            string seen = p._inSpawnInterceptedStats[slot].Count == 0
                ? "<none>"
                : string.Join(",", p._inSpawnInterceptedStats[slot].Where(x => !x.StartsWith("@", StringComparison.Ordinal)));
            p.Diag($"IN-SPAWN STATUS Slot {slot + 1}: spawn completed; observed/overridden=[{seen}], statusCaptured={captured != null}.");
            if (p._inSpawnInterceptedStats[slot].Count == 0)
                p.Log.LogWarning($"IN-SPAWN STATUS Slot {slot + 1}: no generated stat writes were observed inside {__originalMethod.Name}; inspect UNITSPAWNER STATUS diagnostics.");
        }

        if (p._activeFounderSpawnDepth > 0) p._activeFounderSpawnDepth--;
        if (p._activeFounderSpawnDepth == 0)
        {
            p.Diag($"FOUNDER SPAWN EXIT -> method={__originalMethod.Name}.");
            p._activeFounderSpawnSlot = -1;
        }
    }

    private void QueueSpawnedFounderForStatusReinforcement(object unitEntity, int slot, string source)
    {
        if (unitEntity == null || slot < 0 || slot >= Characters.Length) return;
        CharacterSettings c = Characters[slot];
        if (c == null || !c.Apply.Value) return;

        bool needsStats = c.LockMajorStats.Value;
        bool needsTalents = (_nativeAllGenius != null && _nativeAllGenius.Value) || c.LockTalents.Value;
        if (!needsStats && !needsTalents)
        {
            _finalStatusApplied[slot] = true;
            RemovePendingFinalStatusSlot(slot);
            TryFinishCampaignPersistence();
            return;
        }

        // Replace any stale retry entry for the same slot. Keep the UnitEntity alive by holding the
        // managed interop wrapper for only a few seconds.
        _pendingSpawnedFounders.RemoveAll(x => x.Slot == slot);
        var pending = new PendingSpawnedFounder
        {
            UnitEntity = unitEntity,
            Slot = slot,
            Source = source,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddSeconds(4),
            NextAttemptUtc = DateTime.UtcNow,
            Attempts = 0,
            SuccessfulWrites = 0
        };
        _pendingSpawnedFounders.Add(pending);

        Diag($"POST-SPAWN STATUS Slot {slot + 1}: captured returned {unitEntity.GetType().FullName}; reinforcing stats/talents for 4 seconds.");
        if (TryApplySpawnedFounderStatus(pending, finalAttempt: false))
        {
            pending.SuccessfulWrites = 1;
            pending.Attempts = 1;
            pending.NextAttemptUtc = DateTime.UtcNow.AddMilliseconds(100);
        }
    }

    internal void SpawnedFounderStatusTick()
    {
        if (_pendingSpawnedFounders.Count == 0) return;
        DateTime now = DateTime.UtcNow;
        for (int i = _pendingSpawnedFounders.Count - 1; i >= 0; i--)
        {
            PendingSpawnedFounder pending = _pendingSpawnedFounders[i];
            if (pending == null || pending.UnitEntity == null)
            {
                _pendingSpawnedFounders.RemoveAt(i);
                continue;
            }

            if (now < pending.NextAttemptUtc) continue;
            bool expiring = now >= pending.ExpiresUtc;
            bool success = TryApplySpawnedFounderStatus(pending, finalAttempt: expiring);

            if (success)
            {
                pending.SuccessfulWrites++;
                // Reinforce at ~0.1s, 0.35s, 0.85s, 1.6s and 2.6s after spawn.
                double[] delays = { 0.10, 0.25, 0.50, 0.75, 1.00 };
                int idx = Math.Min(pending.SuccessfulWrites - 1, delays.Length - 1);
                pending.NextAttemptUtc = now.AddSeconds(delays[idx]);
            }
            else
            {
                pending.NextAttemptUtc = now.AddMilliseconds(100);
            }

            pending.Attempts++;
            if (expiring || pending.SuccessfulWrites >= 6)
            {
                if (pending.SuccessfulWrites > 0)
                {
                    _finalStatusApplied[pending.Slot] = true;
                    RemovePendingFinalStatusSlot(pending.Slot);
                    Diag($"POST-SPAWN STATUS Slot {pending.Slot + 1}: reinforcement complete; successful writes={pending.SuccessfulWrites}, attempts={pending.Attempts}.");
                    TryFinishCampaignPersistence();
                }
                else
                {
                    Log.LogWarning($"POST-SPAWN STATUS Slot {pending.Slot + 1}: could not locate/use StatusComponent before timeout. See discovery diagnostics above.");
                }
                _pendingSpawnedFounders.RemoveAt(i);
            }
        }
    }

    private bool TryApplySpawnedFounderStatus(PendingSpawnedFounder pending, bool finalAttempt)
    {
        if (pending == null || pending.UnitEntity == null) return false;
        int slot = pending.Slot;
        if (slot < 0 || slot >= Characters.Length) return false;
        CharacterSettings c = Characters[slot];
        if (c == null || !c.Apply.Value) return false;

        string foundPath;
        object status = FindStatusComponentOnUnit(pending.UnitEntity, out foundPath);
        if (status == null)
        {
            if (!_loggedUnitStatusDiscoveryFailure || finalAttempt)
            {
                _loggedUnitStatusDiscoveryFailure = true;
                Log.LogWarning($"POST-SPAWN STATUS Slot {slot + 1}: StatusComponent not found on {pending.UnitEntity.GetType().FullName}. Relevant members: {DescribeStatusLikeMembers(pending.UnitEntity)}");
            }
            return false;
        }

        bool needStats = c.LockMajorStats.Value;
        bool needTalents = (_nativeAllGenius != null && _nativeAllGenius.Value) || c.LockTalents.Value;
        bool allGenius = _nativeAllGenius != null && _nativeAllGenius.Value;
        try
        {
            int writes = 0;
            if (needStats)
            {
                writes += SetGeneratedStatusValue(status, "Strength", c.Strength.Value);
                writes += SetGeneratedStatusValue(status, "Constitution", c.Constitution.Value);
                writes += SetGeneratedStatusValue(status, "WillPower", c.WillPower.Value);
                writes += SetGeneratedStatusValue(status, "Intelligence", c.Intelligence.Value);
                writes += SetGeneratedStatusValue(status, "Agility", c.Agility.Value);
                writes += SetGeneratedStatusValue(status, "Perception", c.Perception.Value);
            }
            if (needTalents)
            {
                writes += SetGeneratedStatusValue(status, "TalentStrength", allGenius ? 3 : TalentValue(c.StrengthTalent.Value));
                writes += SetGeneratedStatusValue(status, "TalentConstitution", allGenius ? 3 : TalentValue(c.ConstitutionTalent.Value));
                writes += SetGeneratedStatusValue(status, "TalentWillPower", allGenius ? 3 : TalentValue(c.WillPowerTalent.Value));
                writes += SetGeneratedStatusValue(status, "TalentIntelligence", allGenius ? 3 : TalentValue(c.IntelligenceTalent.Value));
                writes += SetGeneratedStatusValue(status, "TalentAgility", allGenius ? 3 : TalentValue(c.AgilityTalent.Value));
                writes += SetGeneratedStatusValue(status, "TalentPerception", allGenius ? 3 : TalentValue(c.PerceptionTalent.Value));
            }

            object generated = ReadMember(status, "_generatedStats") ?? ReadMember(status, "GeneratedStats") ??
                               ReadMember(status, "generatedStats");
            if (generated != null)
                WriteGeneratedStatusDictionary(generated, c, needStats, needTalents, allGenius);

            // Materialize the generated bucket into the live raw/final stats. BuildRawStats is the
            // safest idempotent path when exposed; ApplyGeneratedStats is used only as a fallback
            // and only on the first/final attempt to avoid accidental additive stacking.
            string refresh = TryRefreshLiveStatus(status, pending.SuccessfulWrites == 0 || finalAttempt);

            if (pending.SuccessfulWrites == 0)
                Diag($"POST-SPAWN STATUS Slot {slot + 1}: StatusComponent found via {foundPath}; writes={writes}; refresh={refresh}; stats={needStats}; talents={needTalents}.");
            if (finalAttempt)
                Diag($"POST-SPAWN STATUS Slot {slot + 1}: final values after reinforcement -> {DescribeFinalMajorValues(status)}");
            return writes > 0 || generated != null;
        }
        catch (Exception ex)
        {
            if (pending.Attempts == 0 || finalAttempt)
                Log.LogWarning($"POST-SPAWN STATUS Slot {slot + 1}: StatusComponent found via {foundPath} but write failed: {ex.Message}");
            return false;
        }
    }

    private static string TryRefreshLiveStatus(object status, bool allowApplyGeneratedFallback)
    {
        if (status == null) return "none";
        Type t = status.GetType();

        // Prefer a full rebuild from the component's source buckets. This should be idempotent and
        // keeps affecter/equipment/progression sources consistent with the newly written generated values.
        foreach (MethodInfo m in t.GetMethods(AccessTools.all).Where(m => string.Equals(m.Name, "BuildRawStats", StringComparison.Ordinal)))
        {
            try
            {
                object[] args = BuildDefaultArgs(m.GetParameters());
                if (args == null) continue;
                m.Invoke(status, args);
                return "BuildRawStats";
            }
            catch { }
        }

        // Some interop builds may not expose BuildRawStats as invokable. ApplyGeneratedStats is the
        // next closest materialization point, but may be additive, so don't spam it every retry.
        if (allowApplyGeneratedFallback)
        {
            foreach (MethodInfo m in t.GetMethods(AccessTools.all).Where(m => string.Equals(m.Name, "ApplyGeneratedStats", StringComparison.Ordinal)))
            {
                try
                {
                    object[] args = BuildDefaultArgs(m.GetParameters());
                    if (args == null) continue;
                    m.Invoke(status, args);
                    return "ApplyGeneratedStats";
                }
                catch { }
            }
        }
        return "SetGeneratedValue-only";
    }

    private static object[] BuildDefaultArgs(ParameterInfo[] ps)
    {
        if (ps == null || ps.Length == 0) return Array.Empty<object>();
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
            else if (ps[i].ParameterType == typeof(bool)) args[i] = false;
            else if (ps[i].ParameterType.IsValueType) args[i] = DefaultFor(ps[i].ParameterType);
            else return null; // do not invent required reference arguments
        }
        return args;
    }

    private static string DescribeFinalMajorValues(object status)
    {
        string[] names = { "Strength", "Constitution", "WillPower", "Intelligence", "Agility", "Perception",
                           "TalentStrength", "TalentConstitution", "TalentWillPower", "TalentIntelligence", "TalentAgility", "TalentPerception" };
        var parts = new List<string>();
        foreach (string name in names)
        {
            object v = TryGetFinalStatusValue(status, name);
            if (v != null) parts.Add(name + "=" + v);
        }
        return parts.Count == 0 ? "<GetFinalValue unavailable>" : string.Join(", ", parts);
    }

    private static object TryGetFinalStatusValue(object status, string statName)
    {
        if (status == null) return null;
        Type t = status.GetType();
        foreach (MethodInfo m in t.GetMethods(AccessTools.all).Where(m => string.Equals(m.Name, "GetFinalValue", StringComparison.Ordinal)))
        {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != 1 || !ps[0].ParameterType.IsEnum) continue;
            try
            {
                object stat = Enum.Parse(ps[0].ParameterType, statName, true);
                return m.Invoke(status, new[] { stat });
            }
            catch { }
        }
        return null;
    }

    private object FindStatusComponentOnUnit(object root, out string path)
    {
        path = null;
        if (root == null) return null;
        Type target;
        try { target = RequireType("Refactor.Component.StatusComponent"); } catch { return null; }

        var queue = new Queue<Tuple<object, string, int>>();
        var seen = new List<object>();
        queue.Enqueue(Tuple.Create(root, "UnitEntity", 0));

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            object obj = node.Item1;
            string nodePath = node.Item2;
            int depth = node.Item3;
            if (obj == null || SeenByReference(seen, obj)) continue;
            seen.Add(obj);

            Type t = obj.GetType();
            if (target.IsAssignableFrom(t) || string.Equals(t.FullName, target.FullName, StringComparison.Ordinal))
            {
                path = nodePath;
                return obj;
            }

            // First try obvious fields; IL2CPP wrappers often expose backing fields more reliably than properties.
            foreach (FieldInfo f in t.GetFields(AccessTools.all))
            {
                if (!ShouldInspectStatusMember(f.Name, f.FieldType, depth)) continue;
                object value = null;
                try { value = f.GetValue(obj); } catch { }
                if (value == null) continue;
                if (target.IsInstanceOfType(value)) { path = nodePath + "." + f.Name; return value; }
                if (depth < 3) queue.Enqueue(Tuple.Create(value, nodePath + "." + f.Name, depth + 1));
            }

            foreach (PropertyInfo prop in t.GetProperties(AccessTools.all))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                if (!ShouldInspectStatusMember(prop.Name, prop.PropertyType, depth)) continue;
                object value = null;
                try { value = prop.GetValue(obj); } catch { }
                if (value == null) continue;
                if (target.IsInstanceOfType(value)) { path = nodePath + "." + prop.Name; return value; }
                if (depth < 3) queue.Enqueue(Tuple.Create(value, nodePath + "." + prop.Name, depth + 1));
            }

            // Try explicit/non-generic GetComponent-like APIs without depending on compile-time game types.
            foreach (MethodInfo m in t.GetMethods(AccessTools.all))
            {
                try
                {
                    if (m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 &&
                        m.GetParameters().Length == 0 && m.Name.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object value = m.MakeGenericMethod(target).Invoke(obj, Array.Empty<object>());
                        if (value != null && target.IsInstanceOfType(value)) { path = nodePath + "." + m.Name + "<StatusComponent>()"; return value; }
                    }
                    else if (!m.IsGenericMethod && m.GetParameters().Length == 1 &&
                             m.GetParameters()[0].ParameterType == typeof(Type) &&
                             m.Name.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object value = m.Invoke(obj, new object[] { target });
                        if (value != null && target.IsInstanceOfType(value)) { path = nodePath + "." + m.Name + "(StatusComponent)"; return value; }
                    }
                    else if (!m.IsGenericMethod && m.GetParameters().Length == 0 &&
                             (target.IsAssignableFrom(m.ReturnType) || string.Equals(m.ReturnType.FullName, target.FullName, StringComparison.Ordinal)))
                    {
                        object value = m.Invoke(obj, Array.Empty<object>());
                        if (value != null) { path = nodePath + "." + m.Name + "()"; return value; }
                    }
                }
                catch { }
            }
        }
        return null;
    }

    private static bool SeenByReference(List<object> seen, object value)
    {
        foreach (object x in seen) if (ReferenceEquals(x, value)) return true;
        return false;
    }

    private static bool ShouldInspectStatusMember(string name, Type type, int depth)
    {
        if (type == null || type == typeof(string) || type.IsPrimitive || type.IsEnum) return false;
        string n = (name ?? "") + " " + (type.FullName ?? type.Name ?? "");
        if (n.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Reader", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (depth == 0 && n.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string DescribeStatusLikeMembers(object obj)
    {
        if (obj == null) return "<null>";
        try
        {
            Type t = obj.GetType();
            var parts = new List<string>();
            foreach (FieldInfo f in t.GetFields(AccessTools.all))
            {
                string text = f.Name + ":" + f.FieldType.Name;
                if (text.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Reader", StringComparison.OrdinalIgnoreCase) >= 0)
                    parts.Add("F " + text);
            }
            foreach (PropertyInfo p in t.GetProperties(AccessTools.all))
            {
                string text = p.Name + ":" + p.PropertyType.Name;
                if (text.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Reader", StringComparison.OrdinalIgnoreCase) >= 0)
                    parts.Add("P " + text);
            }
            foreach (MethodInfo m in t.GetMethods(AccessTools.all))
            {
                string text = m.Name + "->" + m.ReturnType.Name;
                if (text.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0)
                    parts.Add("M " + text);
            }
            return parts.Count == 0 ? "<none>" : string.Join(" | ", parts.Take(40));
        }
        catch (Exception ex) { return "<diagnostic failed: " + ex.Message + ">"; }
    }

    private int MatchFounderSlot(object candidate)
    {
        if (candidate != null)
        {
            string profile = ReadStringProperty(candidate, "UnitProfileKey");
            if (!string.IsNullOrWhiteSpace(profile))
            {
                for (int i = 0; i < FinalProfileKeyBySlot.Length; i++)
                    if (string.Equals(FinalProfileKeyBySlot[i], profile, StringComparison.Ordinal))
                        return i;
            }
        }
        return -1;
    }

    private static void FinalGeneratedStatsPostfix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        Plugin p = Instance;
        if (p == null || __instance == null || !p._campaignSpawnContextActive) return;
        if (!LooksLikeUnitGeneratedStats(__args)) return;
        p.ApplyPendingFinalStatus(__instance, __originalMethod);
    }

    private static bool LooksLikeUnitGeneratedStats(object[] args)
    {
        if (args == null || args.Length == 0) return true;
        bool sawStatDictionary = false;
        foreach (object arg in args)
        {
            if (arg == null) continue;
            Type t = arg.GetType();
            Type[] ga = t.GetGenericArguments();
            if (ga.Length != 2 || !ga[0].IsEnum) continue;
            string[] names;
            try { names = Enum.GetNames(ga[0]); } catch { continue; }
            if (!names.Contains("Strength") || !names.Contains("TalentStrength")) continue;
            sawStatDictionary = true;
            try
            {
                MethodInfo contains = t.GetMethods(AccessTools.all)
                    .FirstOrDefault(m => m.Name == "ContainsKey" && m.GetParameters().Length == 1);
                if (contains == null) return true;
                object str = Enum.Parse(ga[0], "Strength", true);
                object talent = Enum.Parse(ga[0], "TalentStrength", true);
                bool hasStr = Convert.ToBoolean(contains.Invoke(arg, new[] { str }));
                bool hasTalent = Convert.ToBoolean(contains.Invoke(arg, new[] { talent }));
                return hasStr || hasTalent;
            }
            catch { return true; }
        }
        return !sawStatDictionary;
    }

    private void ApplyPendingFinalStatus(object statusComponent, MethodBase source)
    {
        if (_pendingFinalStatusSlots.Count == 0) return;
        if (DateTime.UtcNow > _pendingFinalStatusUntilUtc)
        {
            Log.LogWarning("FINAL STATUS queue expired before StatusComponent.ApplyGeneratedStats was observed.");
            _pendingFinalStatusSlots.Clear();
            return;
        }

        int slot = _activeFounderSpawnSlot >= 0 ? _activeFounderSpawnSlot : _pendingFinalStatusSlots.Peek();
        if (slot < 0 || slot >= Characters.Length)
        {
            if (_pendingFinalStatusSlots.Count > 0) _pendingFinalStatusSlots.Dequeue();
            return;
        }

        CharacterSettings c = Characters[slot];
        if (c == null || !c.Apply.Value)
        {
            RemovePendingFinalStatusSlot(slot);
            _finalStatusApplied[slot] = true;
            TryFinishCampaignPersistence();
            return;
        }

        bool needStats = c.LockMajorStats.Value;
        bool needTalents = (_nativeAllGenius != null && _nativeAllGenius.Value) || c.LockTalents.Value;
        if (!needStats && !needTalents)
        {
            RemovePendingFinalStatusSlot(slot);
            _finalStatusApplied[slot] = true;
            TryFinishCampaignPersistence();
            return;
        }

        try
        {
            int writeCount = 0;
            if (needStats)
            {
                writeCount += SetGeneratedStatusValue(statusComponent, "Strength", c.Strength.Value);
                writeCount += SetGeneratedStatusValue(statusComponent, "Constitution", c.Constitution.Value);
                writeCount += SetGeneratedStatusValue(statusComponent, "WillPower", c.WillPower.Value);
                writeCount += SetGeneratedStatusValue(statusComponent, "Intelligence", c.Intelligence.Value);
                writeCount += SetGeneratedStatusValue(statusComponent, "Agility", c.Agility.Value);
                writeCount += SetGeneratedStatusValue(statusComponent, "Perception", c.Perception.Value);
            }

            if (needTalents)
            {
                bool allGenius = _nativeAllGenius != null && _nativeAllGenius.Value;
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentStrength", allGenius ? 3 : TalentValue(c.StrengthTalent.Value));
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentConstitution", allGenius ? 3 : TalentValue(c.ConstitutionTalent.Value));
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentWillPower", allGenius ? 3 : TalentValue(c.WillPowerTalent.Value));
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentIntelligence", allGenius ? 3 : TalentValue(c.IntelligenceTalent.Value));
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentAgility", allGenius ? 3 : TalentValue(c.AgilityTalent.Value));
                writeCount += SetGeneratedStatusValue(statusComponent, "TalentPerception", allGenius ? 3 : TalentValue(c.PerceptionTalent.Value));
            }

            // Also synchronize the backing generated-stat dictionary when exposed by Il2CppInterop.
            // SetGeneratedValue is the authoritative path; this is a persistence-oriented fallback.
            object generated = ReadMember(statusComponent, "_generatedStats") ?? ReadMember(statusComponent, "GeneratedStats");
            if (generated != null)
                WriteGeneratedStatusDictionary(generated, c, needStats, needTalents, _nativeAllGenius != null && _nativeAllGenius.Value);

            RemovePendingFinalStatusSlot(slot);
            _finalStatusApplied[slot] = true;
            Diag($"FINAL STATUS Slot {slot + 1}: applied via {source.Name}; generated writes={writeCount}; stats={needStats}; talents={needTalents}.");
            TryFinishCampaignPersistence();
        }
        catch (Exception ex)
        {
            // Do not consume the slot on failure; another ApplyGeneratedStats overload/call may be the usable one.
            Log.LogWarning($"FINAL STATUS Slot {slot + 1}: {source.Name} was not usable yet: {ex.Message}");
        }
    }

    private static int SetGeneratedStatusValue(object statusComponent, string statName, int rawValue)
    {
        Type t = statusComponent.GetType();
        Exception last = null;
        foreach (MethodInfo m in t.GetMethods(AccessTools.all).Where(m => string.Equals(m.Name, "SetGeneratedValue", StringComparison.Ordinal)))
        {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length < 2) continue;
            try
            {
                Type statType = ps[0].ParameterType;
                object stat = Enum.Parse(statType, statName, true);
                object value = ConvertNumeric(rawValue, ps[1].ParameterType);
                object[] args = new object[ps.Length];
                args[0] = stat;
                args[1] = value;
                for (int i = 2; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : DefaultFor(ps[i].ParameterType);
                m.Invoke(statusComponent, args);
                return 1;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new InvalidOperationException($"No usable SetGeneratedValue overload for {statName}. Last={last?.Message}");
    }

    private static object ConvertNumeric(int value, Type targetType)
    {
        Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsEnum) return Enum.ToObject(t, value);
        if (t == typeof(float)) return (float)value;
        if (t == typeof(double)) return (double)value;
        if (t == typeof(decimal)) return (decimal)value;
        if (t == typeof(long)) return (long)value;
        if (t == typeof(short)) return (short)value;
        if (t == typeof(byte)) return (byte)value;
        if (t == typeof(sbyte)) return (sbyte)value;
        if (t == typeof(uint)) return (uint)Math.Max(0, value);
        if (t == typeof(ulong)) return (ulong)Math.Max(0, value);
        if (t == typeof(ushort)) return (ushort)Math.Max(0, value);
        return Convert.ChangeType(value, t);
    }

    private static object DefaultFor(Type t)
    {
        if (!t.IsValueType) return null;
        try { return Activator.CreateInstance(t); } catch { return null; }
    }

    private static void WriteGeneratedStatusDictionary(object dict, CharacterSettings c, bool stats, bool talents, bool allGenius)
    {
        Type dictType = dict.GetType();
        Type[] ga = dictType.GetGenericArguments();
        if (ga.Length != 2) return;
        Type statType = ga[0];
        Type valueType = ga[1];
        MethodInfo setItem = dictType.GetMethods(AccessTools.all)
            .FirstOrDefault(m => m.Name == "set_Item" && m.GetParameters().Length == 2);
        if (setItem == null) return;

        void Put(string name, int value)
        {
            object key = Enum.Parse(statType, name, true);
            object val = ConvertNumeric(value, valueType);
            setItem.Invoke(dict, new[] { key, val });
        }

        if (stats)
        {
            Put("Strength", c.Strength.Value);
            Put("Constitution", c.Constitution.Value);
            Put("WillPower", c.WillPower.Value);
            Put("Intelligence", c.Intelligence.Value);
            Put("Agility", c.Agility.Value);
            Put("Perception", c.Perception.Value);
        }
        if (talents)
        {
            Put("TalentStrength", allGenius ? 3 : TalentValue(c.StrengthTalent.Value));
            Put("TalentConstitution", allGenius ? 3 : TalentValue(c.ConstitutionTalent.Value));
            Put("TalentWillPower", allGenius ? 3 : TalentValue(c.WillPowerTalent.Value));
            Put("TalentIntelligence", allGenius ? 3 : TalentValue(c.IntelligenceTalent.Value));
            Put("TalentAgility", allGenius ? 3 : TalentValue(c.AgilityTalent.Value));
            Put("TalentPerception", allGenius ? 3 : TalentValue(c.PerceptionTalent.Value));
        }
    }

    private void RemovePendingFinalStatusSlot(int slot)
    {
        if (_pendingFinalStatusSlots.Count == 0) return;
        if (_pendingFinalStatusSlots.Peek() == slot)
        {
            _pendingFinalStatusSlots.Dequeue();
            return;
        }

        int[] keep = _pendingFinalStatusSlots.Where(x => x != slot).ToArray();
        _pendingFinalStatusSlots.Clear();
        foreach (int x in keep) _pendingFinalStatusSlots.Enqueue(x);
    }

    private void QueueFinalStatusSlot(int slot)
    {
        if (slot < 0 || slot >= Characters.Length) return;
        CharacterSettings c = Characters[slot];
        if (c == null || !c.Apply.Value) return;

        bool needs = c.LockMajorStats.Value || c.LockTalents.Value || (_nativeAllGenius != null && _nativeAllGenius.Value);
        if (!needs)
        {
            _finalStatusApplied[slot] = true;
            return;
        }

        if (!_pendingFinalStatusSlots.Contains(slot) && !_finalStatusApplied[slot])
        {
            _pendingFinalStatusSlots.Enqueue(slot);
            _pendingFinalStatusUntilUtc = DateTime.UtcNow.AddSeconds(45);
            Diag($"FINAL STATUS Slot {slot + 1}: candidate matched; waiting for spawned UnitEntity status reinforcement.");
        }
    }

    private void TryFinishCampaignPersistence()
    {
        bool done = true;
        for (int i = 0; i < Characters.Length; i++)
        {
            CharacterSettings c = Characters[i];
            if (c == null || !c.Apply.Value) continue;
            if (!_persistenceApplied[i]) { done = false; break; }
            bool needsStatus = c.LockMajorStats.Value || c.LockTalents.Value || (_nativeAllGenius != null && _nativeAllGenius.Value);
            if (needsStatus && !_finalStatusApplied[i]) { done = false; break; }
        }
        if (!done) return;

        _persistenceWindowActive = false;
        _campaignSpawnContextActive = false;
        _pendingFinalStatusSlots.Clear();
        Diag("PERSISTENCE COMPLETE: candidate fields and final generated stats/talents are applied to all configured founders.");
    }

    private static void CampaignStartingSpawnPrefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        Plugin p = Instance;
        if (p == null) return;
        try
        {
            bool fresh = !p._campaignSpawnContextActive || DateTime.UtcNow > p._campaignSpawnContextUntilUtc;
            if (fresh)
            {
                p._campaignSpawnSequentialCursor = 0;
                p._campaignPatchedCandidateIds.Clear();
                p._pendingFinalStatusSlots.Clear();
                p._pendingSpawnedFounders.Clear();
                p._loggedUnitStatusDiscoveryFailure = false;
                p._activeFounderSpawnSlot = -1;
                p._activeFounderSpawnDepth = 0;
                p._directGeneratedWriteDepth = 0;
                Array.Clear(p._capturedStatusComponentBySlot, 0, p._capturedStatusComponentBySlot.Length);
                Array.Clear(p._talentsInjectedIntoStatus, 0, p._talentsInjectedIntoStatus.Length);
                Array.Clear(p._finalStatusApplied, 0, p._finalStatusApplied.Length);
                Array.Clear(p._persistenceApplied, 0, p._persistenceApplied.Length);
                p._persistenceSlotByGenerationSeed.Clear();
            }

            p._campaignSpawnContextActive = true;
            p._campaignSpawnContextUntilUtc = DateTime.UtcNow.AddMinutes(2);
            p._persistenceWindowActive = true;
            p._persistenceWindowUntilUtc = p._campaignSpawnContextUntilUtc;
            p._persistenceWindowOpenedAtUtc = DateTime.UtcNow;

            p.Diag($"CAMPAIGN START FLOW -> {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} args={DescribeArgs(__args)}");

            // The token list may be a direct argument, wrapped in EstablishSelectionPayload, or a
            // private member of CampaignStartingSpawnHelper/CampaignSetting. Search only token-ish
            // members to avoid invoking unrelated game properties.
            p.CaptureTokensFromUnknown(__args, "spawn args");
            p.CaptureTokensFromUnknown(__instance, "spawn helper");
        }
        catch (Exception ex)
        {
            p.Log.LogError($"Campaign-start prefix failed in {__originalMethod}: {ex}");
        }
    }

    private static string DescribeArgs(object[] args)
    {
        if (args == null || args.Length == 0) return "[]";
        try
        {
            return "[" + string.Join(", ", args.Select((a, i) =>
            {
                if (a == null) return $"{i}:null";
                string value = a is string || a.GetType().IsPrimitive || a.GetType().IsEnum ? $"={a}" : "";
                return $"{i}:{a.GetType().FullName}{value}";
            })) + "]";
        }
        catch { return "[unavailable]"; }
    }

    private void CaptureTokensFromUnknown(object source, string label, int depth = 0)
    {
        if (source == null || depth > 5) return;
        try
        {
            if (source is object[] arr)
            {
                for (int i = 0; i < arr.Length; i++)
                    CaptureTokensFromUnknown(arr[i], $"{label}[{i}]", depth + 1);
                return;
            }

            Type t = source.GetType();
            string typeName = t.FullName ?? t.Name;

            if (typeName.IndexOf("EstablishUnitSelectionToken", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                CaptureSingleToken(source, label);
                return;
            }

            // Direct payload/list shapes.
            object tokens = ReadMember(source, "Tokens") ?? ReadMember(source, "tokens");
            if (tokens != null)
            {
                CaptureTokenCollection(tokens, $"{label}.Tokens");
                return;
            }

            int count = ReadCollectionCount(source);
            if (count >= 0 && count <= 32 && typeName.IndexOf("String", StringComparison.OrdinalIgnoreCase) < 0)
            {
                bool foundToken = false;
                for (int i = 0; i < count; i++)
                {
                    object item = ReadCollectionItem(source, i);
                    if (item == null) continue;
                    string itemName = item.GetType().FullName ?? item.GetType().Name;
                    if (itemName.IndexOf("EstablishUnitSelectionToken", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CaptureSingleToken(item, $"{label}[{i}]");
                        foundToken = true;
                    }
                }
                if (foundToken) return;
            }

            // Inspect only members whose names strongly suggest establish/token/payload data.
            foreach (FieldInfo f in t.GetFields(AccessTools.all))
            {
                string n = f.Name ?? "";
                if (n.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("establish", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("payload", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                try
                {
                    object v = f.GetValue(source);
                    if (v != null && !ReferenceEquals(v, source))
                        CaptureTokensFromUnknown(v, $"{label}.{n}", depth + 1);
                }
                catch { }
            }

            foreach (PropertyInfo prop in t.GetProperties(AccessTools.all))
            {
                string n = prop.Name ?? "";
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                if (n.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("establish", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("payload", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                try
                {
                    object v = prop.GetValue(source);
                    if (v != null && !ReferenceEquals(v, source))
                        CaptureTokensFromUnknown(v, $"{label}.{n}", depth + 1);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Token scan skipped {label}: {ex.Message}");
        }
    }

    private void CaptureTokenCollection(object tokens, string label)
    {
        int count = ReadCollectionCount(tokens);
        if (count < 0) return;
        for (int i = 0; i < count; i++)
        {
            object token = ReadCollectionItem(tokens, i);
            if (token != null) CaptureSingleToken(token, $"{label}[{i}]");
        }
    }

    private void CaptureSingleToken(object token, string label)
    {
        try
        {
            int? slot = ReadIntMember(token, "SlotIndex");
            int? seed = ReadIntMember(token, "GenerationSeed");
            int? refresh = ReadIntMember(token, "RefreshCount");
            string preset = ReadStringMember(token, "PresetKey");
            if (slot.HasValue && seed.HasValue && slot.Value >= 0 && slot.Value < Characters.Length)
                _persistenceSlotByGenerationSeed[seed.Value] = slot.Value;
            Diag($"CAMPAIGN TOKEN [{label}] slot={slot?.ToString() ?? "?"} seed={seed?.ToString() ?? "?"} refresh={refresh?.ToString() ?? "?"} preset={preset ?? "<none>"}.");
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Campaign token read failed [{label}]: {ex.Message}");
        }
    }

    private static void OpenPersistenceWindow(string trigger, object payload)
    {
        Plugin p = Instance;
        if (p == null) return;
        try
        {
            if (p._customTalentPatchActive)
                p.RestoreTalentSitesToVanilla();

            // BuildEstablishSelectionPayload may be queried more than once in a very short span.
            // Do not erase already-applied state for duplicate calls from the same Start action.
            bool fresh = !p._persistenceWindowActive ||
                         (DateTime.UtcNow - p._persistenceWindowOpenedAtUtc).TotalSeconds > 3.0;
            if (fresh)
            {
                Array.Clear(p._persistenceApplied, 0, p._persistenceApplied.Length);
                p._persistenceSlotByGenerationSeed.Clear();
                p._persistenceWindowOpenedAtUtc = DateTime.UtcNow;
            }

            p._persistenceWindowActive = true;
            p._persistenceWindowUntilUtc = DateTime.UtcNow.AddMinutes(5);

            if (payload != null)
                p.CaptureEstablishTokens(payload);

            string slots = string.Join(", ", Enumerable.Range(0, Characters.Length)
                .Where(i => Characters[i] != null && Characters[i].Apply.Value)
                .Select(i => $"S{i + 1}={FinalProfileKeyBySlot[i] ?? "<missing-profile>"}"));
            p.Diag($"PERSISTENCE WINDOW OPEN [{trigger}] -> {slots}; tokenSeeds={p._persistenceSlotByGenerationSeed.Count}.");
        }
        catch (Exception ex)
        {
            p.Log.LogError($"Persistence window open failed [{trigger}]: {ex}");
        }
    }

    private static void EstablishPayloadBuiltPostfix(object __result)
    {
        OpenPersistenceWindow("BuildEstablishSelectionPayload", __result);
    }

    private static void EstablishPayloadBuiltVoidPostfix(object __instance)
    {
        object payload = null;
        try
        {
            payload = ReadMember(__instance, "_establishSelectionPayload") ??
                      ReadMember(__instance, "EstablishSelectionPayload");
            if (payload == null)
            {
                MethodInfo getter = __instance?.GetType().GetMethods(AccessTools.all)
                    .FirstOrDefault(m => m.Name == "GetEstablishSelectionPayload" && m.GetParameters().Length == 0 && m.ReturnType != typeof(void));
                if (getter != null) payload = getter.Invoke(__instance, null);
            }
        }
        catch { }
        OpenPersistenceWindow("BuildEstablishSelectionPayload(void)", payload);
    }

    private void CaptureEstablishTokens(object payload)
    {
        try
        {
            object tokens = ReadMember(payload, "Tokens") ?? ReadMember(payload, "tokens");
            if (tokens == null)
            {
                Log.LogWarning($"PERSIST TOKENS: payload type {payload.GetType().FullName} has no readable Tokens member.");
                return;
            }

            int count = ReadCollectionCount(tokens);
            if (count < 0)
            {
                Log.LogWarning($"PERSIST TOKENS: could not read token count from {tokens.GetType().FullName}.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                object token = ReadCollectionItem(tokens, i);
                if (token == null) continue;
                int? slot = ReadIntMember(token, "SlotIndex");
                int? seed = ReadIntMember(token, "GenerationSeed");
                int? refresh = ReadIntMember(token, "RefreshCount");
                string preset = ReadStringMember(token, "PresetKey");
                if (slot.HasValue && seed.HasValue && slot.Value >= 0 && slot.Value < Characters.Length)
                    _persistenceSlotByGenerationSeed[seed.Value] = slot.Value;
                Diag($"PERSIST TOKEN[{i}] type={token.GetType().Name} slot={slot?.ToString() ?? "?"} seed={seed?.ToString() ?? "?"} refresh={refresh?.ToString() ?? "?"} preset={preset ?? "<none>"}.");
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"PERSIST TOKENS capture failed: {ex.Message}");
        }
    }

    private static object ReadMember(object obj, string name)
    {
        if (obj == null) return null;
        Type t = obj.GetType();
        try
        {
            PropertyInfo p = t.GetProperty(name, AccessTools.all);
            if (p != null && p.CanRead) return p.GetValue(obj);
        }
        catch { }
        try
        {
            FieldInfo f = t.GetField(name, AccessTools.all);
            if (f != null) return f.GetValue(obj);
        }
        catch { }
        return null;
    }

    private static int? ReadIntMember(object obj, string name)
    {
        object v = ReadMember(obj, name);
        if (v == null) return null;
        try { return Convert.ToInt32(v); } catch { return null; }
    }

    private static string ReadStringMember(object obj, string name)
    {
        object v = ReadMember(obj, name);
        return v?.ToString();
    }

    private static int ReadCollectionCount(object collection)
    {
        if (collection == null) return -1;
        try
        {
            if (collection is Array a) return a.Length;
            object v = ReadMember(collection, "Count") ?? ReadMember(collection, "Length");
            if (v != null) return Convert.ToInt32(v);
        }
        catch { }
        return -1;
    }

    private static object ReadCollectionItem(object collection, int index)
    {
        if (collection == null) return null;
        try
        {
            if (collection is Array a) return a.GetValue(index);
            Type t = collection.GetType();
            PropertyInfo item = t.GetProperties(AccessTools.all)
                .FirstOrDefault(p => p.Name == "Item" && p.GetIndexParameters().Length == 1);
            if (item != null) return item.GetValue(collection, new object[] { index });
            MethodInfo getter = t.GetMethods(AccessTools.all)
                .FirstOrDefault(m => m.Name == "get_Item" && m.GetParameters().Length == 1);
            if (getter != null) return getter.Invoke(collection, new object[] { index });
        }
        catch { }
        return null;
    }

    private static void EstablishConfirmPrefix()
    {
        OpenPersistenceWindow("OnEstablishSelectionConfirmed", null);
    }

    private static void FinalCandidatePostfix(MethodBase __originalMethod, object[] __args, object __result)
    {
        Plugin p = Instance;
        if (p == null || !p._persistenceWindowActive || __result == null) return;
        try
        {
            if (DateTime.UtcNow > p._persistenceWindowUntilUtc)
            {
                p._persistenceWindowActive = false;
                p.Log.LogWarning("PERSISTENCE WINDOW expired before all configured founders were regenerated.");
                return;
            }

            object candidate = FindRecruitCandidate(__result);
            if (candidate == null) return;

            string profile = ReadStringProperty(candidate, "UnitProfileKey");
            int slot = -1;
            string match = "none";

            // Best match: generation seed captured directly from EstablishUnitSelectionToken.
            if (__args != null && p._persistenceSlotByGenerationSeed.Count > 0)
            {
                foreach (object arg in __args)
                {
                    if (arg == null) continue;
                    int value;
                    try
                    {
                        Type at = arg.GetType();
                        if (at != typeof(int) && !at.IsEnum) continue;
                        value = Convert.ToInt32(arg);
                    }
                    catch { continue; }

                    if (p._persistenceSlotByGenerationSeed.TryGetValue(value, out int exact))
                    {
                        slot = exact;
                        match = $"seed={value}";
                        break;
                    }

                    // TryGenerateFromToken may add a small retry offset to GenerationSeed.
                    foreach (var kv in p._persistenceSlotByGenerationSeed)
                    {
                        int delta = value - kv.Key;
                        if (delta >= 0 && delta <= 32)
                        {
                            slot = kv.Value;
                            match = $"seed={value} (base={kv.Key}, +{delta})";
                            break;
                        }
                    }
                    if (slot >= 0) break;
                }
            }

            // Fallback: deterministic regeneration normally preserves the visible portrait/profile.
            if (slot < 0 && !string.IsNullOrWhiteSpace(profile))
            {
                for (int i = 0; i < FinalProfileKeyBySlot.Length; i++)
                {
                    if (string.Equals(FinalProfileKeyBySlot[i], profile, StringComparison.Ordinal))
                    {
                        slot = i;
                        match = $"profile={profile}";
                        break;
                    }
                }
            }

            if (slot < 0 && p._campaignSpawnContextActive)
            {
                // Last-resort mapping for the actual CampaignStartingSpawnHelper path. It processes
                // the four establish slots in order. Prefer seed/profile above; use order only when
                // the build does not expose tokens to managed reflection.
                while (p._campaignSpawnSequentialCursor < Characters.Length)
                {
                    int candidateSlot = p._campaignSpawnSequentialCursor++;
                    CharacterSettings seq = Characters[candidateSlot];
                    if (seq != null && seq.Apply.Value && !p._persistenceApplied[candidateSlot])
                    {
                        slot = candidateSlot;
                        match = "campaign-spawn-order";
                        if (!string.IsNullOrWhiteSpace(profile))
                            FinalProfileKeyBySlot[candidateSlot] = profile;
                        break;
                    }
                }
            }

            if (slot < 0 || slot >= Characters.Length)
            {
                string ints = __args == null ? "" : string.Join(",", __args.Where(a => a != null && (a.GetType() == typeof(int) || a.GetType().IsEnum)).Select(a => a.ToString()));
                p.Diag($"PERSIST CANDIDATE unmatched via {__originalMethod.Name}: profile={profile ?? "<none>"}, intArgs=[{ints}], campaignSpawn={p._campaignSpawnContextActive}.");
                return;
            }

            CharacterSettings c = Characters[slot];
            if (c == null || !c.Apply.Value) return;

            ApplyBackground(candidate, c, slot);
            ApplyTraits(candidate, c, slot);
            ApplySkills(candidate, c, slot);
            ApplySubSkills(candidate, c, slot);

            // v1.8: do NOT rewrite MajorStats/MajorStatTalents on the campaign-start candidate.
            // The live unit materializes these values in a different representation, and v1.7
            // showed that candidate-level talent dictionaries could leak their -1..3 enum values
            // into the live stat display. Preview stats/talents are still handled by SetSlot/native ARM.
            // Campaign stats/talents are applied directly to the spawned UnitEntity below.

            bool first = !p._persistenceApplied[slot];
            p._persistenceApplied[slot] = true;
            if (first)
            {
                p.Diag($"PERSIST FINAL Slot {slot + 1}: applied via {__originalMethod.Name}, match={match}, profile={profile ?? "<none>"}.");
                if (p._campaignSpawnContextActive)
                    p.QueueFinalStatusSlot(slot);
            }

            p.TryFinishCampaignPersistence();
        }
        catch (Exception ex)
        {
            p.Log.LogError($"Persistence final candidate write failed in {__originalMethod}: {ex}");
        }
    }

    private static object FindRecruitCandidate(object value)
    {
        if (value == null) return null;
        Type t = value.GetType();
        if (t.Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0)
            return value;

        try
        {
            PropertyInfo p = t.GetProperty("Candidate", AccessTools.all);
            if (p != null && p.CanRead)
            {
                object nested = p.GetValue(value);
                if (nested != null && nested.GetType().Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0)
                    return nested;
            }
        }
        catch { }
        try
        {
            FieldInfo f = t.GetField("Candidate", AccessTools.all) ?? t.GetField("_candidate", AccessTools.all);
            if (f != null)
            {
                object nested = f.GetValue(value);
                if (nested != null && nested.GetType().Name.IndexOf("RecruitCandidateData", StringComparison.OrdinalIgnoreCase) >= 0)
                    return nested;
            }
        }
        catch { }
        return null;
    }

    private static string ReadStringProperty(object obj, string name)
    {
        try
        {
            PropertyInfo p = obj?.GetType().GetProperty(name, AccessTools.all);
            return p?.CanRead == true ? p.GetValue(obj)?.ToString() : null;
        }
        catch { return null; }
    }

    internal void PersistenceFailsafeTick()
    {
        if (_campaignSpawnContextActive && DateTime.UtcNow > _campaignSpawnContextUntilUtc)
        {
            _campaignSpawnContextActive = false;
            Log.LogWarning("CAMPAIGN START persistence context timed out after 2 minutes.");
        }
        if (!_persistenceWindowActive) return;
        if (DateTime.UtcNow <= _persistenceWindowUntilUtc) return;
        _persistenceWindowActive = false;
        Log.LogWarning("PERSISTENCE WINDOW timed out.");
    }

    private void BindConfig()
    {
        _nativeAllGenius = Config.Bind("General", "NativeAllGeniusInitialCandidates", true,
            "Verified native patch. true = all four initial candidates get six Genius talents; false = vanilla talent generation.");
        _diagnosticLogging = Config.Bind("General", "DiagnosticLogging", false,
            "Stable build diagnostics. Leave false for normal play; set true only when collecting LogOutput.log for troubleshooting.");

        for (int i = 0; i < 4; i++)
        {
            int n = i + 1;
            string sec = $"Character {n}";
            bool defaultApply = n == 1;
            Characters[i] = new CharacterSettings
            {
                Apply = Config.Bind(sec, "Apply", defaultApply, "Reapply configured gameplay values to this slot after every vanilla reroll."),
                Background = Config.Bind(sec, "Background", n == 1 ? "AFFECTER_Mercenary" : "Vanilla", "Background trait key, or Vanilla."),
                Trait1 = Config.Bind(sec, "Trait1", n == 1 ? "AFFECTER_SlowLearner" : "Vanilla", "First individual trait. Vanilla keeps generated list. None means custom empty list."),
                Trait2 = Config.Bind(sec, "Trait2", "None", "Second individual trait or None."),
                Trait3 = Config.Bind(sec, "Trait3", "None", "Third individual trait or None."),
                Skill1 = Config.Bind(sec, "Skill1", n == 1 ? "Blunt" : "Vanilla", "Main skill enum name; Vanilla keeps generated skills."),
                Skill2 = Config.Bind(sec, "Skill2", n == 1 ? "Shield" : "Vanilla", "Second main skill enum name."),
                Skill3 = Config.Bind(sec, "Skill3", n == 1 ? "FireMagic" : "Vanilla", "Third main skill enum name."),
                SubSkill1 = Config.Bind(sec, "SubSkill1", n == 1 ? "Fortress" : "Vanilla", "Sub skill enum name; Vanilla keeps generated sub skills."),
                SubSkill2 = Config.Bind(sec, "SubSkill2", n == 1 ? "Guardian" : "Vanilla", "Second sub skill enum name."),
                SubSkill3 = Config.Bind(sec, "SubSkill3", n == 1 ? "Execution" : "Vanilla", "Third sub skill enum name."),
                LockTalents = Config.Bind(sec, "LockTalents", false, "Per-slot native talent lock. On that slot's next vanilla reroll, its six selected talent values are injected into DetermineEstablishTalents."),
                StrengthTalent = Config.Bind(sec, "StrengthTalent", "Genius", "Talent for Strength: Poor/Moderate/Outstanding/Exceptional/Genius. LockTalents must be ON."),
                ConstitutionTalent = Config.Bind(sec, "ConstitutionTalent", "Genius", "TalentType for Constitution."),
                WillPowerTalent = Config.Bind(sec, "WillPowerTalent", "Genius", "TalentType for WillPower."),
                IntelligenceTalent = Config.Bind(sec, "IntelligenceTalent", "Genius", "TalentType for Intelligence."),
                AgilityTalent = Config.Bind(sec, "AgilityTalent", "Genius", "TalentType for Agility."),
                PerceptionTalent = Config.Bind(sec, "PerceptionTalent", "Genius", "TalentType for Perception."),
                LockMajorStats = Config.Bind(sec, "LockMajorStats", false, "If true, lock the six base MajorStats below on every reroll."),
                Strength = Config.Bind(sec, "Strength", 15, "Base Strength when LockMajorStats=true."),
                Constitution = Config.Bind(sec, "Constitution", 15, "Base Constitution when LockMajorStats=true."),
                WillPower = Config.Bind(sec, "WillPower", 15, "Base WillPower when LockMajorStats=true."),
                Intelligence = Config.Bind(sec, "Intelligence", 15, "Base Intelligence when LockMajorStats=true."),
                Agility = Config.Bind(sec, "Agility", 15, "Base Agility when LockMajorStats=true."),
                Perception = Config.Bind(sec, "Perception", 15, "Base Perception when LockMajorStats=true."),
            };
        }
    }

    internal static bool AllGeniusEnabled => Instance?._nativeAllGenius?.Value == true;

    internal static void SetAllGeniusFromUi(bool value)
    {
        Plugin p = Instance;
        if (p == null || p._nativeAllGenius == null) return;
        try
        {
            if (value)
            {
                foreach (CharacterSettings c in Characters)
                    c.LockTalents.Value = false;
            }
            p.SetGlobalAllGeniusPatch(value);
            p._nativeAllGenius.Value = value;
            p.Config.Save();
            p.Diag($"UI: NativeAllGeniusInitialCandidates -> {value}. Reroll candidates to regenerate talents.");
        }
        catch (Exception ex)
        {
            p.Log.LogError($"UI: talent patch toggle failed: {ex}");
        }
    }

    internal static void SetPerSlotTalentLockFromUi(int slot, bool value)
    {
        if (slot < 0 || slot >= Characters.Length || Instance == null) return;
        try
        {
            Characters[slot].LockTalents.Value = value;
            if (value && AllGeniusEnabled)
                SetAllGeniusFromUi(false);
            Instance.Config.Save();
            Instance.Diag($"UI: Slot {slot + 1} LockTalents -> {value}. Press that slot's vanilla reroll button to apply.");
        }
        catch (Exception ex)
        {
            Instance.Log.LogError($"UI: per-slot talent lock toggle failed: {ex}");
        }
    }

    internal static void ArmSlotTalentsFromUi(int slot)
    {
        if (slot < 0 || slot >= Characters.Length || Instance == null) return;
        try
        {
            CharacterSettings c = Characters[slot];
            c.Apply.Value = true;
            c.LockTalents.Value = true;
            if (AllGeniusEnabled)
                SetAllGeniusFromUi(false);
            Instance.ArmCustomTalentProfile(slot, c);
            Instance.Config.Save();
            Instance.Diag($"UI: MANUAL ARM -> Slot {slot + 1}. Close F4 and reroll ONLY that slot within 30 seconds.");
        }
        catch (Exception ex)
        {
            Instance.Log.LogError($"UI: manual talent ARM failed for Slot {slot + 1}: {ex}");
            try { Instance.RestoreTalentSitesToVanilla(); } catch { }
        }
    }

    internal static void DisarmTalentProfileFromUi()
    {
        if (Instance == null) return;
        try
        {
            if (Instance._customTalentPatchActive)
            {
                int slot = Instance._armedTalentSlot;
                Instance.RestoreTalentSitesToVanilla();
                Instance.Diag($"UI: manual talent profile DISARMED (was Slot {slot + 1}).");
            }
        }
        catch (Exception ex)
        {
            Instance.Log.LogError($"UI: manual talent DISARM failed: {ex}");
        }
    }

    internal static string TalentArmStatus
    {
        get
        {
            if (Instance == null || !Instance._customTalentPatchActive) return "Talent native patch: not armed";
            double left = Math.Max(0.0, 30.0 - (DateTime.UtcNow - Instance._armedTalentAtUtc).TotalSeconds);
            return $"ARMED for Slot {Instance._armedTalentSlot + 1} - reroll ONLY this slot ({left:0}s left)";
        }
    }

    internal static void SaveConfigFromUi()
    {
        Instance?.Config.Save();
        Instance?.Diag("UI: configuration saved.");
    }

    internal static void ResetSlotToVanilla(int slot)
    {
        if (slot < 0 || slot >= Characters.Length) return;
        CharacterSettings c = Characters[slot];
        c.Apply.Value = false;
        c.Background.Value = "Vanilla";
        c.Trait1.Value = "Vanilla";
        c.Trait2.Value = "None";
        c.Trait3.Value = "None";
        c.Skill1.Value = "Vanilla";
        c.Skill2.Value = "Vanilla";
        c.Skill3.Value = "Vanilla";
        c.SubSkill1.Value = "Vanilla";
        c.SubSkill2.Value = "Vanilla";
        c.SubSkill3.Value = "Vanilla";
        c.LockTalents.Value = false;
        c.StrengthTalent.Value = "Genius";
        c.ConstitutionTalent.Value = "Genius";
        c.WillPowerTalent.Value = "Genius";
        c.IntelligenceTalent.Value = "Genius";
        c.AgilityTalent.Value = "Genius";
        c.PerceptionTalent.Value = "Genius";
        c.LockMajorStats.Value = false;
        Instance?.Config.Save();
    }

    private static Type RequireType(string fullName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type t = asm.GetType(fullName, false, false);
                if (t != null) return t;
            }
            catch { }
        }
        throw new TypeLoadException($"Game interop type not found: {fullName}");
    }

    private static void RerollSlotPrefix(MethodBase __originalMethod, object[] __args)
    {
        int slot = ExtractSlotIndex(__originalMethod, __args);
        if (slot < 0 || slot >= Characters.Length || Instance == null)
        {
            Instance?.Log.LogWarning($"Talent reroll hook could not determine slot from {__originalMethod}.");
            return;
        }

        CharacterSettings c = Characters[slot];
        if (!c.Apply.Value || !c.LockTalents.Value || AllGeniusEnabled)
            return;

        try
        {
            Instance.ArmCustomTalentProfile(slot, c);
        }
        catch (Exception ex)
        {
            Instance.Log.LogError($"Slot {slot + 1}: failed to arm native talent profile: {ex}");
            try { Instance.RestoreTalentSitesToVanilla(); } catch { }
        }
    }

    private static int ExtractSlotIndex(MethodBase method, object[] args)
    {
        try
        {
            ParameterInfo[] ps = method.GetParameters();
            for (int i = 0; i < ps.Length && i < args.Length; i++)
            {
                if (args[i] == null) continue;
                if (string.Equals(ps[i].Name, "slotIndex", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt32(args[i]);
            }
            for (int i = 0; i < ps.Length && i < args.Length; i++)
            {
                if (args[i] != null && ps[i].ParameterType == typeof(int))
                    return Convert.ToInt32(args[i]);
            }
        }
        catch { }
        return -1;
    }

    internal void TalentPatchFailsafeTick()
    {
        if (!_customTalentPatchActive) return;
        if ((DateTime.UtcNow - _armedTalentAtUtc).TotalSeconds < 30.0) return;
        try
        {
            int slot = _armedTalentSlot;
            RestoreTalentSitesToVanilla();
            Log.LogWarning($"Slot {slot + 1}: native talent profile timed out after 30s and was restored for safety.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Talent patch failsafe restore failed: {ex}");
        }
    }

    private static void SetSlotPrefix(MethodBase __originalMethod, object[] __args)
    {
        int slot = -1;
        object candidate = null;
        try
        {
            ParameterInfo[] ps = __originalMethod.GetParameters();
            for (int i = 0; i < ps.Length && i < __args.Length; i++)
            {
                object arg = __args[i];
                if (string.Equals(ps[i].Name, "slotIndex", StringComparison.OrdinalIgnoreCase) && arg != null)
                    slot = Convert.ToInt32(arg);
                if (ps[i].ParameterType.Name.Contains("RecruitCandidateData", StringComparison.OrdinalIgnoreCase))
                    candidate = arg;
            }

            if (slot < 0 || slot >= Characters.Length || candidate == null)
                return;

            string currentProfile = ReadStringProperty(candidate, "UnitProfileKey");
            if (!string.IsNullOrWhiteSpace(currentProfile))
            {
                FinalProfileKeyBySlot[slot] = currentProfile;
                Instance?.Log.LogDebug($"Slot {slot + 1}: persistence identity -> {currentProfile}");
            }

            if (Instance != null && Instance._customTalentPatchActive)
            {
                int armed = Instance._armedTalentSlot;
                bool matched = armed == slot;
                Instance.RestoreTalentSitesToVanilla();
                if (matched)
                    Instance.Diag($"Slot {slot + 1}: armed native talent profile was consumed; original RNG calls restored.");
                else
                    Instance.Log.LogWarning($"Native talent profile was armed for Slot {armed + 1}, but Slot {slot + 1} completed generation. The temporary patch was restored. Re-arm the intended slot before rerolling it.");
            }

            CharacterSettings c = Characters[slot];
            if (!c.Apply.Value)
                return;

            ApplyBackground(candidate, c, slot);
            ApplyTraits(candidate, c, slot);
            ApplySkills(candidate, c, slot);
            ApplySubSkills(candidate, c, slot);
            if (c.LockMajorStats.Value)
                ApplyMajorStats(candidate, c, slot);
        }
        catch (Exception ex)
        {
            Instance?.Log.LogError($"Slot {slot + 1}: SetSlot write failed: {ex}");
        }
    }

    private static void ApplyBackground(object candidate, CharacterSettings c, int slot)
    {
        string value = c.Background.Value.Trim();
        if (IsVanilla(value)) return;
        RequireWritableProperty(candidate, "BackgroundTrait").SetValue(candidate, value);
    }

    private static void ApplyTraits(object candidate, CharacterSettings c, int slot)
    {
        string first = c.Trait1.Value.Trim();
        if (IsVanilla(first)) return;

        string[] raw = { first, c.Trait2.Value.Trim(), c.Trait3.Value.Trim() };
        List<string> wanted = raw
            .Where(s => !string.IsNullOrWhiteSpace(s) && !IsNone(s) && !IsVanilla(s))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();

        object list = RequireReadableProperty(candidate, "IndividualTraits").GetValue(candidate)
                      ?? throw new NullReferenceException("IndividualTraits returned null.");
        InvokeNoArg(list, "Clear");
        MethodInfo add = RequireMethod(list.GetType(), "Add", 1);
        foreach (string t in wanted) add.Invoke(list, new object[] { t });
        RequireWritableProperty(candidate, "IndividualTraits").SetValue(candidate, list);
    }

    private static void ApplySkills(object candidate, CharacterSettings c, int slot)
    {
        string first = c.Skill1.Value.Trim();
        if (IsVanilla(first)) return;

        string[] wanted = { first, c.Skill2.Value.Trim(), c.Skill3.Value.Trim() };
        if (wanted.Any(s => string.IsNullOrWhiteSpace(s) || IsNone(s) || IsVanilla(s)))
            throw new InvalidOperationException("When Skill1 is custom, Skill1/2/3 must all be valid skill enum names.");

        object list = RequireReadableProperty(candidate, "MainSkillTrees").GetValue(candidate)
                      ?? throw new NullReferenceException("MainSkillTrees returned null.");
        Type listType = list.GetType();
        Type enumType = FindSingleGenericArgument(listType)
                        ?? throw new InvalidOperationException($"Cannot determine MainSkillTrees enum type from {listType.FullName}");

        object[] enumValues = wanted.Select(s => Enum.Parse(enumType, s, true)).ToArray();
        if (enumValues.Select(v => Convert.ToInt32(v)).Distinct().Count() != 3)
            throw new InvalidOperationException("Main skills must be three different values.");

        InvokeNoArg(list, "Clear");
        MethodInfo add = RequireMethod(listType, "Add", 1);
        foreach (object v in enumValues) add.Invoke(list, new[] { v });
        RequireWritableProperty(candidate, "MainSkillTrees").SetValue(candidate, list);
    }

    private static void ApplySubSkills(object candidate, CharacterSettings c, int slot)
    {
        string first = c.SubSkill1.Value.Trim();
        if (IsVanilla(first)) return;

        string[] wanted = { first, c.SubSkill2.Value.Trim(), c.SubSkill3.Value.Trim() };
        if (wanted.Any(s => string.IsNullOrWhiteSpace(s) || IsNone(s) || IsVanilla(s)))
            throw new InvalidOperationException("When SubSkill1 is custom, SubSkill1/2/3 must all be valid sub-skill enum names.");

        object list = RequireReadableProperty(candidate, "SubSkillTrees").GetValue(candidate)
                      ?? throw new NullReferenceException("SubSkillTrees returned null.");
        Type listType = list.GetType();
        Type enumType = FindSingleGenericArgument(listType)
                        ?? throw new InvalidOperationException($"Cannot determine SubSkillTrees enum type from {listType.FullName}");

        object[] enumValues = wanted.Select(s => Enum.Parse(enumType, s, true)).ToArray();
        if (enumValues.Select(v => Convert.ToInt32(v)).Distinct().Count() != 3)
            throw new InvalidOperationException("Sub skills must be three different values.");

        InvokeNoArg(list, "Clear");
        MethodInfo add = RequireMethod(listType, "Add", 1);
        foreach (object v in enumValues) add.Invoke(list, new[] { v });
        RequireWritableProperty(candidate, "SubSkillTrees").SetValue(candidate, list);
    }

    private static void ApplyTalents(object candidate, CharacterSettings c, int slot)
    {
        PropertyInfo mainProp = RequireReadableProperty(candidate, "MajorStatTalents");
        object mainDict = mainProp.GetValue(candidate)
                          ?? throw new NullReferenceException("MajorStatTalents returned null.");
        WriteTalentDictionary(mainDict, c);
        if (mainProp.CanWrite) mainProp.SetValue(candidate, mainDict);

        // Il2CppInterop exposes the native backing field as a synthetic property on this game build.
        // Synchronize it explicitly because the normal MajorStatTalents setter alone did not update
        // the recruitment talent icons in v0.5.
        PropertyInfo backing = candidate.GetType().GetProperty("_MajorStatTalents_k__BackingField", AccessTools.all);
        if (backing != null && backing.CanRead)
        {
            object backingDict = backing.GetValue(candidate);
            if (backingDict != null)
            {
                WriteTalentDictionary(backingDict, c);
                if (backing.CanWrite) backing.SetValue(candidate, backingDict);
            }
        }

        MethodInfo setMajor = candidate.GetType().GetMethods(AccessTools.all)
            .FirstOrDefault(m => m.Name == "SetMajorStatTalents" && m.GetParameters().Length == 1);
        setMajor?.Invoke(candidate, new[] { mainDict });

        // Re-assert both views after the game's helper in case it swapped the dictionary instance.
        if (mainProp.CanWrite) mainProp.SetValue(candidate, mainDict);
        if (backing != null && backing.CanWrite) backing.SetValue(candidate, mainDict);

        Instance?.Diag($"Slot {slot + 1}: per-slot talents WRITE -> " +
            $"STR={c.StrengthTalent.Value}, CON={c.ConstitutionTalent.Value}, WIL={c.WillPowerTalent.Value}, " +
            $"INT={c.IntelligenceTalent.Value}, AGI={c.AgilityTalent.Value}, PER={c.PerceptionTalent.Value}");
    }

    private static void WriteTalentDictionary(object dict, CharacterSettings c)
    {
        Type dictType = dict.GetType();
        Type[] ga = dictType.GetGenericArguments();
        if (ga.Length != 2)
            throw new InvalidOperationException($"Cannot determine MajorStatTalents dictionary types from {dictType.FullName}");
        Type statType = ga[0];
        Type talentType = ga[1];
        MethodInfo setItem = RequireMethod(dictType, "set_Item", 2);

        (string stat, string talent)[] values =
        {
            ("Strength", c.StrengthTalent.Value),
            ("Constitution", c.ConstitutionTalent.Value),
            ("WillPower", c.WillPowerTalent.Value),
            ("Intelligence", c.IntelligenceTalent.Value),
            ("Agility", c.AgilityTalent.Value),
            ("Perception", c.PerceptionTalent.Value),
        };

        foreach (var item in values)
        {
            if (IsVanilla(item.talent)) continue;
            object key = Enum.Parse(statType, item.stat, true);
            object value = Enum.Parse(talentType, item.talent, true);
            setItem.Invoke(dict, new object[] { key, value });
        }
    }

    private static void ApplyMajorStats(object candidate, CharacterSettings c, int slot)
    {
        object dict = RequireReadableProperty(candidate, "MajorStats").GetValue(candidate)
                      ?? throw new NullReferenceException("MajorStats returned null.");
        Type dictType = dict.GetType();
        Type[] ga = dictType.GetGenericArguments();
        if (ga.Length != 2)
            throw new InvalidOperationException($"Cannot determine MajorStats dictionary types from {dictType.FullName}");
        Type statType = ga[0];
        MethodInfo setItem = RequireMethod(dictType, "set_Item", 2);

        (string name, int value)[] values =
        {
            ("Strength", c.Strength.Value),
            ("Constitution", c.Constitution.Value),
            ("WillPower", c.WillPower.Value),
            ("Intelligence", c.Intelligence.Value),
            ("Agility", c.Agility.Value),
            ("Perception", c.Perception.Value),
        };

        foreach (var item in values)
        {
            object key = Enum.Parse(statType, item.name, true);
            setItem.Invoke(dict, new object[] { key, item.value });
        }
        RequireWritableProperty(candidate, "MajorStats").SetValue(candidate, dict);
    }

    private void SetGlobalAllGeniusPatch(bool enabled)
    {
        RestoreTalentSitesToVanilla();
        if (enabled)
        {
            WriteTalentSite(0, MakeMovEax(18));
            Diag($"Native global all-Genius talent patch ACTIVE at RVA 0x{TalentNativeRvas[0]:X}.");
        }
        else
        {
            Diag("Native global all-Genius talent patch DISABLED; vanilla talent RNG restored.");
        }
    }

    private void ArmCustomTalentProfile(int slot, CharacterSettings c)
    {
        if (_nativeAllGenius != null && _nativeAllGenius.Value)
            throw new InvalidOperationException("Global all-Genius patch is enabled. Disable it before using per-slot talents.");

        int[] values =
        {
            TalentValue(c.StrengthTalent.Value),
            TalentValue(c.ConstitutionTalent.Value),
            TalentValue(c.WillPowerTalent.Value),
            TalentValue(c.IntelligenceTalent.Value),
            TalentValue(c.AgilityTalent.Value),
            TalentValue(c.PerceptionTalent.Value),
        };
        int sum = values.Sum();

        RestoreTalentSitesToVanilla();
        WriteTalentSite(0, MakeMovEax(sum));
        for (int i = 0; i < 6; i++)
            WriteTalentSite(i + 1, MakeMovEax(values[i]));

        _armedTalentSlot = slot;
        _armedTalentAtUtc = DateTime.UtcNow;
        _customTalentPatchActive = true;
        Diag($"Slot {slot + 1}: native talent profile ARMED -> " +
            $"STR={values[0]}, CON={values[1]}, WIL={values[2]}, INT={values[3]}, AGI={values[4]}, PER={values[5]}, SUM={sum}.");
    }

    private static int TalentValue(string name)
    {
        if (string.Equals(name, "Poor", StringComparison.OrdinalIgnoreCase)) return -1;
        if (string.Equals(name, "Moderate", StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(name, "Outstanding", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(name, "Exceptional", StringComparison.OrdinalIgnoreCase)) return 2;
        if (string.Equals(name, "Genius", StringComparison.OrdinalIgnoreCase)) return 3;
        throw new InvalidOperationException($"Unsupported talent value '{name}'. Turn LockTalents off for vanilla talents.");
    }

    private void RestoreTalentSitesToVanilla()
    {
        IntPtr gameAssembly = GetModuleHandle("GameAssembly.dll");
        if (gameAssembly == IntPtr.Zero)
            throw new InvalidOperationException("GameAssembly.dll is not loaded.");

        for (int i = 0; i < TalentNativeRvas.Length; i++)
        {
            IntPtr target = IntPtr.Add(gameAssembly, TalentNativeRvas[i]);
            byte[] actual = new byte[5];
            Marshal.Copy(target, actual, 0, actual.Length);
            if (actual.SequenceEqual(TalentNativeExpected[i]))
                continue;

            // Our patches are always exactly 'mov eax, imm32' (B8 xx xx xx xx).
            if (actual.Length == 5 && actual[0] == 0xB8)
            {
                WriteBytes(target, TalentNativeExpected[i]);
                continue;
            }

            throw new InvalidOperationException($"Game build mismatch at talent RVA 0x{TalentNativeRvas[i]:X}; restore refused. Got {ToHex(actual)}.");
        }

        _customTalentPatchActive = false;
        _armedTalentSlot = -1;
    }

    private void WriteTalentSite(int siteIndex, byte[] bytes)
    {
        IntPtr gameAssembly = GetModuleHandle("GameAssembly.dll");
        if (gameAssembly == IntPtr.Zero)
            throw new InvalidOperationException("GameAssembly.dll is not loaded.");
        IntPtr target = IntPtr.Add(gameAssembly, TalentNativeRvas[siteIndex]);
        byte[] actual = new byte[5];
        Marshal.Copy(target, actual, 0, 5);
        if (!actual.SequenceEqual(TalentNativeExpected[siteIndex]) && actual[0] != 0xB8)
            throw new InvalidOperationException($"Game build mismatch at talent RVA 0x{TalentNativeRvas[siteIndex]:X}; patch refused. Got {ToHex(actual)}.");
        WriteBytes(target, bytes);
    }

    private static byte[] MakeMovEax(int value)
    {
        byte[] b = new byte[5];
        b[0] = 0xB8;
        byte[] imm = BitConverter.GetBytes(value);
        Buffer.BlockCopy(imm, 0, b, 1, 4);
        return b;
    }

    private static void WriteBytes(IntPtr target, byte[] bytes)
    {
        if (!VirtualProtect(target, (UIntPtr)bytes.Length, PageExecuteReadWrite, out uint oldProtection))
            throw new InvalidOperationException($"VirtualProtect failed for talent patch: {Marshal.GetLastWin32Error()}.");
        Marshal.Copy(bytes, 0, target, bytes.Length);
        VirtualProtect(target, (UIntPtr)bytes.Length, oldProtection, out _);
        FlushInstructionCache(GetCurrentProcess(), target, (UIntPtr)bytes.Length);
    }

    private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", " ");

    private static PropertyInfo RequireWritableProperty(object obj, string name)
    {
        PropertyInfo p = obj.GetType().GetProperty(name, AccessTools.all)
                         ?? throw new MissingMemberException(obj.GetType().FullName, name);
        if (!p.CanWrite) throw new InvalidOperationException($"{name} is not writable.");
        return p;
    }

    private static PropertyInfo RequireReadableProperty(object obj, string name)
    {
        PropertyInfo p = obj.GetType().GetProperty(name, AccessTools.all)
                         ?? throw new MissingMemberException(obj.GetType().FullName, name);
        if (!p.CanRead) throw new InvalidOperationException($"{name} is not readable.");
        return p;
    }

    private static MethodInfo RequireMethod(Type t, string name, int parameterCount)
        => t.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount)
           ?? throw new MissingMethodException(t.FullName, name);

    private static void InvokeNoArg(object obj, string name)
        => RequireMethod(obj.GetType(), name, 0).Invoke(obj, null);

    private static Type FindSingleGenericArgument(Type t)
    {
        Type[] args = t.GetGenericArguments();
        if (args.Length == 1) return args[0];
        for (Type cur = t.BaseType; cur != null; cur = cur.BaseType)
        {
            args = cur.GetGenericArguments();
            if (args.Length == 1) return args[0];
        }
        return null;
    }

    internal static string DisplayValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "None";
        if (value.StartsWith("AFFECTER_", StringComparison.Ordinal)) return value.Substring("AFFECTER_".Length);
        return value;
    }

    internal static bool IsVanilla(string s) => string.Equals(s?.Trim(), "Vanilla", StringComparison.OrdinalIgnoreCase);
    internal static bool IsNone(string s) => string.Equals(s?.Trim(), "None", StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtection, out uint oldProtection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private sealed class PendingSpawnedFounder
    {
        public object UnitEntity;
        public int Slot;
        public string Source;
        public DateTime CreatedUtc;
        public DateTime ExpiresUtc;
        public DateTime NextAttemptUtc;
        public int Attempts;
        public int SuccessfulWrites;
    }

    internal sealed class CharacterSettings
    {
        public ConfigEntry<bool> Apply = null;
        public ConfigEntry<string> Background = null;
        public ConfigEntry<string> Trait1 = null;
        public ConfigEntry<string> Trait2 = null;
        public ConfigEntry<string> Trait3 = null;
        public ConfigEntry<string> Skill1 = null;
        public ConfigEntry<string> Skill2 = null;
        public ConfigEntry<string> Skill3 = null;
        public ConfigEntry<string> SubSkill1 = null;
        public ConfigEntry<string> SubSkill2 = null;
        public ConfigEntry<string> SubSkill3 = null;
        public ConfigEntry<bool> LockTalents = null;
        public ConfigEntry<string> StrengthTalent = null;
        public ConfigEntry<string> ConstitutionTalent = null;
        public ConfigEntry<string> WillPowerTalent = null;
        public ConfigEntry<string> IntelligenceTalent = null;
        public ConfigEntry<string> AgilityTalent = null;
        public ConfigEntry<string> PerceptionTalent = null;
        public ConfigEntry<bool> LockMajorStats = null;
        public ConfigEntry<int> Strength = null;
        public ConfigEntry<int> Constitution = null;
        public ConfigEntry<int> WillPower = null;
        public ConfigEntry<int> Intelligence = null;
        public ConfigEntry<int> Agility = null;
        public ConfigEntry<int> Perception = null;
    }
}

public sealed class EditorOverlay : MonoBehaviour
{
    private bool _open;
    private bool _f4WasDown;
    private int _slot;
    private int _page; // 0=Build, 1=Talents/Stats
    private PickerKind _picker = PickerKind.None;
    private int _pickerPage;
    private const int VkF4 = 0x73;

    private GUIStyle _titleStyle;
    private GUIStyle _subtitleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _hintStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _centerStyle;

    public EditorOverlay(IntPtr ptr) : base(ptr) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void Update()
    {
        bool down = (GetAsyncKeyState(VkF4) & 0x8000) != 0;
        if (down && !_f4WasDown)
        {
            _open = !_open;
            _picker = PickerKind.None;
        }
        _f4WasDown = down;
        Plugin.Instance?.TalentPatchFailsafeTick();
        Plugin.Instance?.PersistenceFailsafeTick();
    }

    public void OnGUI()
    {
        try
        {
            if (!_open) return;
            EnsureStyles();

            float panelW = Math.Min(720f, Screen.width - 30f);
            float panelH = Math.Min(770f, Screen.height - 48f);
            float px = Math.Max(12f, Screen.width - panelW - 18f);
            float py = Math.Max(18f, (Screen.height - panelH) * 0.5f);
            Rect panel = new Rect(px, py, panelW, panelH);

            GUI.Box(new Rect(panel.x + 4f, panel.y + 4f, panel.width, panel.height), "");
            GUI.Box(panel, "");
            DrawPanel(panel);

            if (_picker != PickerKind.None)
                DrawPicker(panel);
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.LogError($"Editor UI exception: {ex}");
        }
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _titleStyle.normal.textColor = Color.white;

        _subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft
        };
        _subtitleStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f, 1f);

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _sectionStyle.normal.textColor = new Color(0.95f, 0.86f, 0.68f, 1f);

        _hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };
        _hintStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1f);

        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _centerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
    }

    private void DrawPanel(Rect p)
    {
        float margin = 16f;
        float x = p.x + margin;
        float y = p.y + 10f;
        float w = p.width - margin * 2f;

        GUI.Label(new Rect(x, y, w - 110f, 26f), "Expedition Editor", _titleStyle);
        GUI.Label(new Rect(x + w - 110f, y + 2f, 110f, 22f), "F4  Close", _subtitleStyle);
        y += 27f;
        GUI.Label(new Rect(x, y, w, 20f), "Keep race / gender locked in the vanilla recruit UI. Reroll there after editing values here.", _subtitleStyle);
        y += 27f;

        // Slot tabs + per-slot enable in a single compact header row.
        float tabGap = 6f;
        float tabW = (w - 128f - tabGap * 4f) / 4f;
        for (int i = 0; i < 4; i++)
        {
            string label = i == _slot ? $"[ Slot {i + 1} ]" : $"Slot {i + 1}";
            if (GUI.Button(new Rect(x + i * (tabW + tabGap), y, tabW, 30f), label))
            {
                _slot = i;
                _picker = PickerKind.None;
            }
        }

        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        bool apply = c.Apply.Value;
        float applyX = x + 4f * (tabW + tabGap);
        if (GUI.Button(new Rect(applyX, y, w - (applyX - x), 30f), apply ? "Slot ON" : "Slot OFF"))
        {
            c.Apply.Value = !apply;
            Plugin.SaveConfigFromUi();
        }
        y += 38f;

        // Page tabs.
        float pageW = (w - 8f) * 0.5f;
        if (GUI.Button(new Rect(x, y, pageW, 31f), _page == 0 ? "BUILD  -  selected" : "BUILD"))
        {
            _page = 0;
            _picker = PickerKind.None;
        }
        if (GUI.Button(new Rect(x + pageW + 8f, y, pageW, 31f), _page == 1 ? "TALENTS / STATS  -  selected" : "TALENTS / STATS"))
        {
            _page = 1;
            _picker = PickerKind.None;
        }
        y += 41f;

        if (_page == 0)
            y = DrawBuildPage(x, y, w);
        else
            y = DrawTalentsStatsPage(x, y, w);

        DrawFooter(x, p.y + p.height - 47f, w);
    }

    private float DrawBuildPage(float x, float y, float w)
    {
        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        Rect bgBox = new Rect(x, y, w, 161f);
        GUI.Box(bgBox, "");
        GUI.Label(new Rect(x + 12f, y + 6f, w - 24f, 22f), "BACKGROUND & TRAITS", _sectionStyle);
        float ry = y + 31f;
        ry = SelectorFull(x + 12f, ry, w - 24f, "Background", PickerKind.Background, c.Background.Value);
        ry = SelectorFull(x + 12f, ry, w - 24f, "Trait 1", PickerKind.Trait1, c.Trait1.Value);
        ry = SelectorFull(x + 12f, ry, w - 24f, "Trait 2", PickerKind.Trait2, c.Trait2.Value);
        ry = SelectorFull(x + 12f, ry, w - 24f, "Trait 3", PickerKind.Trait3, c.Trait3.Value);
        y += 171f;

        Rect skillBox = new Rect(x, y, w, 157f);
        GUI.Box(skillBox, "");
        GUI.Label(new Rect(x + 12f, y + 6f, w - 24f, 22f), "SKILLS", _sectionStyle);

        float gap = 16f;
        float colW = (w - 24f - gap) * 0.5f;
        float left = x + 12f;
        float right = left + colW + gap;
        GUI.Label(new Rect(left, y + 30f, colW, 20f), "Main skills", _subtitleStyle);
        GUI.Label(new Rect(right, y + 30f, colW, 20f), "Sub skills", _subtitleStyle);

        float sy = y + 52f;
        SelectorMini(left, sy, colW, "1", PickerKind.MainSkill1, c.Skill1.Value);
        SelectorMini(right, sy, colW, "1", PickerKind.SubSkill1, c.SubSkill1.Value);
        sy += 31f;
        SelectorMini(left, sy, colW, "2", PickerKind.MainSkill2, c.Skill2.Value);
        SelectorMini(right, sy, colW, "2", PickerKind.SubSkill2, c.SubSkill2.Value);
        sy += 31f;
        SelectorMini(left, sy, colW, "3", PickerKind.MainSkill3, c.Skill3.Value);
        SelectorMini(right, sy, colW, "3", PickerKind.SubSkill3, c.SubSkill3.Value);
        y += 167f;

        Rect tip = new Rect(x, y, w, 55f);
        GUI.Box(tip, "");
        GUI.Label(new Rect(x + 12f, y + 7f, w - 24f, 40f),
            "Changes are auto-saved. Close with F4, then use the game's reroll button for this slot. Name / portrait remain vanilla.", _hintStyle);
        return y + 65f;
    }

    private float DrawTalentsStatsPage(float x, float y, float w)
    {
        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        // Global talent mode.
        Rect globalBox = new Rect(x, y, w, 58f);
        GUI.Box(globalBox, "");
        GUI.Label(new Rect(x + 12f, y + 6f, 230f, 20f), "GLOBAL TALENT MODE", _sectionStyle);
        bool allGenius = Plugin.AllGeniusEnabled;
        if (GUI.Button(new Rect(x + 12f, y + 29f, 250f, 25f), allGenius ? "All 4 candidates: 6x Genius  ON" : "All 4 candidates: 6x Genius  OFF"))
            Plugin.SetAllGeniusFromUi(!allGenius);
        GUI.Label(new Rect(x + 276f, y + 31f, w - 288f, 22f), "Turn OFF to use the slot-specific grades below.", _hintStyle);
        y += 68f;

        // Slot-specific talents.
        Rect talentBox = new Rect(x, y, w, 263f);
        GUI.Box(talentBox, "");
        GUI.Label(new Rect(x + 12f, y + 6f, 220f, 22f), $"SLOT {_slot + 1} TALENTS", _sectionStyle);
        bool lockTalents = c.LockTalents.Value;
        if (GUI.Button(new Rect(x + w - 190f, y + 5f, 178f, 25f), lockTalents ? "Talent lock  ON" : "Talent lock  OFF"))
            Plugin.SetPerSlotTalentLockFromUi(_slot, !lockTalents);

        float gap = 14f;
        float colW = (w - 24f - gap) * 0.5f;
        float left = x + 12f;
        float right = left + colW + gap;
        float ty = y + 39f;
        SelectorMini(left, ty, colW, "Strength", PickerKind.StrengthTalent, c.StrengthTalent.Value);
        SelectorMini(right, ty, colW, "Constitution", PickerKind.ConstitutionTalent, c.ConstitutionTalent.Value);
        ty += 32f;
        SelectorMini(left, ty, colW, "WillPower", PickerKind.WillPowerTalent, c.WillPowerTalent.Value);
        SelectorMini(right, ty, colW, "Intelligence", PickerKind.IntelligenceTalent, c.IntelligenceTalent.Value);
        ty += 32f;
        SelectorMini(left, ty, colW, "Agility", PickerKind.AgilityTalent, c.AgilityTalent.Value);
        SelectorMini(right, ty, colW, "Perception", PickerKind.PerceptionTalent, c.PerceptionTalent.Value);
        ty += 39f;

        bool armed = Plugin.TalentArmStatus.StartsWith("ARMED", StringComparison.OrdinalIgnoreCase);
        if (GUI.Button(new Rect(left, ty, w - 24f - 118f, 31f), armed ? $"ARMED - Slot {_slot + 1} next reroll" : $"ARM Slot {_slot + 1} for NEXT reroll"))
            Plugin.ArmSlotTalentsFromUi(_slot);
        if (GUI.Button(new Rect(x + w - 118f, ty, 106f, 31f), "Disarm"))
            Plugin.DisarmTalentProfileFromUi();
        ty += 35f;

        _statusStyle.normal.textColor = armed ? new Color(0.55f, 1f, 0.60f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
        GUI.Label(new Rect(left, ty, w - 24f, 24f), FriendlyArmStatus(Plugin.TalentArmStatus), _statusStyle);
        ty += 25f;
        GUI.Label(new Rect(left, ty, w - 24f, 34f),
            "Manual ARM is intentionally one-shot: arm this slot, close F4, then reroll ONLY that slot within 30 seconds.", _hintStyle);
        y += 273f;

        // Base stats.
        Rect statBox = new Rect(x, y, w, 194f);
        GUI.Box(statBox, "");
        GUI.Label(new Rect(x + 12f, y + 6f, 220f, 22f), "BASE STATS", _sectionStyle);
        bool lockStats = c.LockMajorStats.Value;
        if (GUI.Button(new Rect(x + w - 190f, y + 5f, 178f, 25f), lockStats ? "Base stat lock  ON" : "Base stat lock  OFF"))
        {
            c.LockMajorStats.Value = !lockStats;
            Plugin.SaveConfigFromUi();
        }

        float stY = y + 39f;
        StatMini(left, stY, colW, "Strength", 0);
        StatMini(right, stY, colW, "Constitution", 1);
        stY += 38f;
        StatMini(left, stY, colW, "WillPower", 2);
        StatMini(right, stY, colW, "Intelligence", 3);
        stY += 38f;
        StatMini(left, stY, colW, "Agility", 4);
        StatMini(right, stY, colW, "Perception", 5);
        GUI.Label(new Rect(x + 12f, y + 158f, w - 24f, 26f), "Background / trait bonuses are applied after these base values.", _hintStyle);
        return y + 204f;
    }

    private void DrawFooter(float x, float y, float w)
    {
        GUI.Label(new Rect(x, y + 5f, 230f, 22f), "Auto-save enabled", _subtitleStyle);
        if (GUI.Button(new Rect(x + w - 330f, y, 190f, 30f), "Reset selected slot to Vanilla"))
        {
            Plugin.ResetSlotToVanilla(_slot);
            _picker = PickerKind.None;
        }
        if (GUI.Button(new Rect(x + w - 130f, y, 130f, 30f), "Close  [F4]"))
        {
            _open = false;
            _picker = PickerKind.None;
        }
    }

    private float SelectorFull(float x, float y, float w, string label, PickerKind kind, string current)
    {
        GUI.Label(new Rect(x, y + 3f, 116f, 22f), label);
        float valueX = x + 120f;
        float valueW = w - 120f;
        if (GUI.Button(new Rect(valueX, y, 28f, 26f), "<"))
            SetValue(kind, Cycle(GetOptions(kind), current, -1));
        if (GUI.Button(new Rect(valueX + 33f, y, valueW - 66f, 26f), Pretty(current)))
        {
            _picker = kind;
            int index = IndexOf(GetOptions(kind), current);
            _pickerPage = Math.Max(0, index / 12);
        }
        if (GUI.Button(new Rect(valueX + valueW - 28f, y, 28f, 26f), ">"))
            SetValue(kind, Cycle(GetOptions(kind), current, +1));
        return y + 30f;
    }

    private void SelectorMini(float x, float y, float w, string label, PickerKind kind, string current)
    {
        float labelW = Math.Min(102f, w * 0.34f);
        GUI.Label(new Rect(x, y + 3f, labelW, 22f), label);
        float bx = x + labelW + 4f;
        float bw = w - labelW - 4f;
        if (GUI.Button(new Rect(bx, y, 24f, 26f), "<"))
            SetValue(kind, Cycle(GetOptions(kind), current, -1));
        if (GUI.Button(new Rect(bx + 28f, y, bw - 56f, 26f), Pretty(current)))
        {
            _picker = kind;
            int index = IndexOf(GetOptions(kind), current);
            _pickerPage = Math.Max(0, index / 12);
        }
        if (GUI.Button(new Rect(bx + bw - 24f, y, 24f, 26f), ">"))
            SetValue(kind, Cycle(GetOptions(kind), current, +1));
    }

    private void StatMini(float x, float y, float w, string label, int statIndex)
    {
        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        ConfigEntry<int> entry = statIndex switch
        {
            0 => c.Strength,
            1 => c.Constitution,
            2 => c.WillPower,
            3 => c.Intelligence,
            4 => c.Agility,
            5 => c.Perception,
            _ => c.Strength
        };
        GUI.Label(new Rect(x, y + 3f, 96f, 22f), label);
        float bx = x + 100f;
        if (GUI.Button(new Rect(bx, y, 33f, 27f), "-5")) { entry.Value = Math.Max(0, entry.Value - 5); Plugin.SaveConfigFromUi(); }
        if (GUI.Button(new Rect(bx + 37f, y, 28f, 27f), "-")) { entry.Value = Math.Max(0, entry.Value - 1); Plugin.SaveConfigFromUi(); }
        GUI.Box(new Rect(bx + 69f, y, 52f, 27f), entry.Value.ToString());
        if (GUI.Button(new Rect(bx + 125f, y, 28f, 27f), "+")) { entry.Value = Math.Min(99, entry.Value + 1); Plugin.SaveConfigFromUi(); }
        if (GUI.Button(new Rect(bx + 157f, y, 33f, 27f), "+5")) { entry.Value = Math.Min(99, entry.Value + 5); Plugin.SaveConfigFromUi(); }
    }

    private void DrawPicker(Rect panel)
    {
        string[] options = GetOptions(_picker);
        const int pageSize = 12;
        int pageCount = Math.Max(1, (options.Length + pageSize - 1) / pageSize);
        _pickerPage = Math.Max(0, Math.Min(pageCount - 1, _pickerPage));

        float pw = Math.Min(570f, panel.width - 54f);
        float ph = 300f;
        float px = panel.x + (panel.width - pw) * 0.5f;
        float py = panel.y + (panel.height - ph) * 0.5f;
        Rect box = new Rect(px, py, pw, ph);
        GUI.Box(box, "");

        GUI.Label(new Rect(px + 14f, py + 10f, pw - 90f, 24f), $"Select  •  {PickerLabel(_picker)}", _sectionStyle);
        if (GUI.Button(new Rect(px + pw - 48f, py + 8f, 34f, 26f), "X"))
        {
            _picker = PickerKind.None;
            return;
        }

        int start = _pickerPage * pageSize;
        int end = Math.Min(options.Length, start + pageSize);
        int localCount = end - start;
        int rows = 6;
        float gap = 8f;
        float colW = (pw - 28f - gap) * 0.5f;
        string current = CurrentValue(_picker);

        for (int j = 0; j < localCount; j++)
        {
            int i = start + j;
            int col = j / rows;
            int row = j % rows;
            float bx = px + 14f + col * (colW + gap);
            float by = py + 44f + row * 32f;
            bool selected = string.Equals(options[i], current, StringComparison.OrdinalIgnoreCase);
            string label = selected ? $"* {Pretty(options[i])}" : Pretty(options[i]);
            if (GUI.Button(new Rect(bx, by, colW, 27f), label))
            {
                SetValue(_picker, options[i]);
                _picker = PickerKind.None;
                return;
            }
        }

        float navY = py + ph - 39f;
        if (GUI.Button(new Rect(px + 14f, navY, 75f, 27f), "Prev"))
            _pickerPage = Math.Max(0, _pickerPage - 1);
        GUI.Label(new Rect(px + pw * 0.5f - 50f, navY + 3f, 100f, 22f), $"{_pickerPage + 1} / {pageCount}", _centerStyle);
        if (GUI.Button(new Rect(px + pw - 89f, navY, 75f, 27f), "Next"))
            _pickerPage = Math.Min(pageCount - 1, _pickerPage + 1);
    }

    private string CurrentValue(PickerKind kind)
    {
        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        return kind switch
        {
            PickerKind.Background => c.Background.Value,
            PickerKind.Trait1 => c.Trait1.Value,
            PickerKind.Trait2 => c.Trait2.Value,
            PickerKind.Trait3 => c.Trait3.Value,
            PickerKind.MainSkill1 => c.Skill1.Value,
            PickerKind.MainSkill2 => c.Skill2.Value,
            PickerKind.MainSkill3 => c.Skill3.Value,
            PickerKind.SubSkill1 => c.SubSkill1.Value,
            PickerKind.SubSkill2 => c.SubSkill2.Value,
            PickerKind.SubSkill3 => c.SubSkill3.Value,
            PickerKind.StrengthTalent => c.StrengthTalent.Value,
            PickerKind.ConstitutionTalent => c.ConstitutionTalent.Value,
            PickerKind.WillPowerTalent => c.WillPowerTalent.Value,
            PickerKind.IntelligenceTalent => c.IntelligenceTalent.Value,
            PickerKind.AgilityTalent => c.AgilityTalent.Value,
            PickerKind.PerceptionTalent => c.PerceptionTalent.Value,
            _ => ""
        };
    }

    private static string FriendlyArmStatus(string raw)
    {
        if (raw.StartsWith("ARMED", StringComparison.OrdinalIgnoreCase))
            return raw.Replace("reroll ONLY this slot", "ready for next reroll");
        return "Not armed — normal talent generation is active.";
    }

    private static string Pretty(string value)
    {
        string s = Plugin.DisplayValue(value);
        if (s == "WillPower") return "Will Power";
        if (s == "FireMagic") return "Fire Magic";
        if (s == "WaterMagic") return "Water Magic";
        if (s == "NatureMagic") return "Nature Magic";

        var chars = new List<char>(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                chars.Add(' ');
            chars.Add(ch);
        }
        return new string(chars.ToArray());
    }

    private void SetValue(PickerKind kind, string value)
    {
        Plugin.CharacterSettings c = Plugin.Characters[_slot];
        switch (kind)
        {
            case PickerKind.Background: c.Background.Value = value; break;
            case PickerKind.Trait1: c.Trait1.Value = value; break;
            case PickerKind.Trait2: c.Trait2.Value = value; break;
            case PickerKind.Trait3: c.Trait3.Value = value; break;
            case PickerKind.MainSkill1:
                c.Skill1.Value = value;
                NormalizeMainSkills(c);
                break;
            case PickerKind.MainSkill2:
                c.Skill2.Value = value;
                NormalizeMainSkills(c);
                break;
            case PickerKind.MainSkill3:
                c.Skill3.Value = value;
                NormalizeMainSkills(c);
                break;
            case PickerKind.SubSkill1:
                c.SubSkill1.Value = value;
                NormalizeSubSkills(c);
                break;
            case PickerKind.SubSkill2:
                c.SubSkill2.Value = value;
                NormalizeSubSkills(c);
                break;
            case PickerKind.SubSkill3:
                c.SubSkill3.Value = value;
                NormalizeSubSkills(c);
                break;
            case PickerKind.StrengthTalent: c.StrengthTalent.Value = value; break;
            case PickerKind.ConstitutionTalent: c.ConstitutionTalent.Value = value; break;
            case PickerKind.WillPowerTalent: c.WillPowerTalent.Value = value; break;
            case PickerKind.IntelligenceTalent: c.IntelligenceTalent.Value = value; break;
            case PickerKind.AgilityTalent: c.AgilityTalent.Value = value; break;
            case PickerKind.PerceptionTalent: c.PerceptionTalent.Value = value; break;
        }
        Plugin.SaveConfigFromUi();
    }

    private static void NormalizeMainSkills(Plugin.CharacterSettings c)
    {
        if (Plugin.IsVanilla(c.Skill1.Value)) return;
        string[] values = { c.Skill1.Value, c.Skill2.Value, c.Skill3.Value };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Length; i++)
        {
            string v = values[i];
            if (Plugin.IsVanilla(v) || Plugin.IsNone(v) || !Plugin.MainSkillOptions.Contains(v, StringComparer.OrdinalIgnoreCase) || !used.Add(v))
            {
                v = Plugin.MainSkillOptions.First(x => !used.Contains(x));
                values[i] = v;
                used.Add(v);
            }
        }
        c.Skill1.Value = values[0]; c.Skill2.Value = values[1]; c.Skill3.Value = values[2];
    }

    private static void NormalizeSubSkills(Plugin.CharacterSettings c)
    {
        if (Plugin.IsVanilla(c.SubSkill1.Value)) return;
        string[] values = { c.SubSkill1.Value, c.SubSkill2.Value, c.SubSkill3.Value };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Length; i++)
        {
            string v = values[i];
            if (Plugin.IsVanilla(v) || Plugin.IsNone(v) || !Plugin.SubSkillOptions.Contains(v, StringComparer.OrdinalIgnoreCase) || !used.Add(v))
            {
                v = Plugin.SubSkillOptions.First(x => !used.Contains(x));
                values[i] = v;
                used.Add(v);
            }
        }
        c.SubSkill1.Value = values[0]; c.SubSkill2.Value = values[1]; c.SubSkill3.Value = values[2];
    }

    private static string[] GetOptions(PickerKind kind)
    {
        return kind switch
        {
            PickerKind.Background => Plugin.BackgroundOptions,
            PickerKind.Trait1 => Plugin.TraitOptionsFirst,
            PickerKind.Trait2 or PickerKind.Trait3 => Plugin.TraitOptions,
            PickerKind.MainSkill1 => Plugin.MainSkillOptionsFirst,
            PickerKind.MainSkill2 or PickerKind.MainSkill3 => Plugin.MainSkillOptions,
            PickerKind.SubSkill1 => Plugin.SubSkillOptionsFirst,
            PickerKind.SubSkill2 or PickerKind.SubSkill3 => Plugin.SubSkillOptions,
            PickerKind.StrengthTalent or PickerKind.ConstitutionTalent or PickerKind.WillPowerTalent or
            PickerKind.IntelligenceTalent or PickerKind.AgilityTalent or PickerKind.PerceptionTalent => Plugin.TalentOptions,
            _ => Array.Empty<string>()
        };
    }

    private static string PickerLabel(PickerKind kind)
    {
        return kind switch
        {
            PickerKind.Background => "Background",
            PickerKind.Trait1 => "Trait 1",
            PickerKind.Trait2 => "Trait 2",
            PickerKind.Trait3 => "Trait 3",
            PickerKind.MainSkill1 => "Main skill 1",
            PickerKind.MainSkill2 => "Main skill 2",
            PickerKind.MainSkill3 => "Main skill 3",
            PickerKind.SubSkill1 => "Sub skill 1",
            PickerKind.SubSkill2 => "Sub skill 2",
            PickerKind.SubSkill3 => "Sub skill 3",
            PickerKind.StrengthTalent => "Strength talent",
            PickerKind.ConstitutionTalent => "Constitution talent",
            PickerKind.WillPowerTalent => "WillPower talent",
            PickerKind.IntelligenceTalent => "Intelligence talent",
            PickerKind.AgilityTalent => "Agility talent",
            PickerKind.PerceptionTalent => "Perception talent",
            _ => "value"
        };
    }

    private static string Cycle(string[] options, string current, int delta)
    {
        if (options.Length == 0) return current;
        int i = IndexOf(options, current);
        if (i < 0) i = 0;
        i = (i + delta) % options.Length;
        if (i < 0) i += options.Length;
        return options[i];
    }

    private static int IndexOf(string[] options, string value)
    {
        for (int i = 0; i < options.Length; i++)
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private enum PickerKind
    {
        None,
        Background,
        Trait1,
        Trait2,
        Trait3,
        MainSkill1,
        MainSkill2,
        MainSkill3,
        SubSkill1,
        SubSkill2,
        SubSkill3,
        StrengthTalent,
        ConstitutionTalent,
        WillPowerTalent,
        IntelligenceTalent,
        AgilityTalent,
        PerceptionTalent
    }
}

