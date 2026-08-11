/*
name: Core LoneWolf
description: Shared combat mechanics for LoneWolf Ultra scripts.
tags: core, lonewolf, ultra
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Models.Shops;
using Skua.Core.Options;

#nullable enable

public class CoreLoneWolf
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;

    private const int SkillPollDelay = 100;
    private const string ArmyProtocolVersion = "1";
    private const int ArmyPollDelay = 500;
    private const int ArmyFileRetryDelay = 100;
    private const string PotionShopMap = "alchemyacademy";
    private const int PotionShopId = 2036;
    private const int PotionAuraCheckDelay = 1500;
    private const int PotionSuccessDelay = 2000;
    private const string EnrageScroll = "Scroll of Enrage";
    private const string DecayScroll = "Scroll of Decay";
    private const string MystifyScroll = "Scroll of Mystify";
    private const string SpellcraftMap = "spellcraft";
    private const int ArcaneQuillShopId = 693;
    private const int ArcaneQuillShopItemId = 7686;
    private const int SpellInkShopId = 549;
    private const int EnrageQuestId = 2330;
    private const int EnrageThreshold = 80;
    private const int EnrageMaxStack = 1000;
    private const int EnrageRewardQuantity = 40;
    private const int DecayQuestId = 2331;
    private const int MystifyQuestId = 2344;
    private const int OptionalScrollThreshold = 10;
    private const string ArcanaInvokerFoolAura = "0 - The Fool";
    private const string ArcanaInvokerJudgementAura = "Judgement Day";
    private const string ArcanaInvokerWorldAura = "XXI - The World";
    private const string ShamanElementalEmbraceAura = "Elemental Embrace";
    private const string ChronoShadowHunterRoundsEmptyAura = "Rounds Empty";
    private const string ChronoShadowHunterGunslingerAura = "Gunslinger Stance";

    private enum PotionCategory
    {
        Tonic,
        Elixir,
        CombatPotion,
    }

    private enum EnhancementSlot
    {
        Class,
        Cape,
        Helm,
        Weapon,
    }

    private sealed class TargetedPrioritySkillRequest
    {
        public TargetedPrioritySkillRequest(
            int skill,
            int targetMapId,
            int returnMapId
        )
        {
            Skill = skill;
            TargetMapId = targetMapId;
            ReturnMapId = returnMapId;
        }

        public int Skill { get; }
        public int TargetMapId { get; }
        public int ReturnMapId { get; }
    }

    private sealed class LimitedPrioritySkillRequest
    {
        public LimitedPrioritySkillRequest(int skill, int remainingUses)
        {
            Skill = skill;
            RemainingUses = remainingUses;
        }

        public int Skill { get; }
        public int RemainingUses { get; }
    }

    private static readonly string[] ArmyAliases =
    {
        "playerOne",
        "playerTwo",
        "playerThree",
        "playerFour",
        "playerFive",
        "playerSix",
        "playerSeven",
    };

    private Thread? skillThread;
    private volatile bool skillEngineRunning;
    private int skillEnginePaused;
    private int pendingAbsolutePriorityTauntMapId;
    private int pendingTauntMapId;
    private int pendingImmediateTauntMapId;
    private int pendingSkillFiveMapId;
    private int pendingImmediateSkillFiveMapId;
    private int pendingAbsolutePrioritySkill;
    private int pendingPrioritySkill;
    private TargetedPrioritySkillRequest? pendingTargetedPrioritySkill;
    private LimitedPrioritySkillRequest? pendingLimitedPrioritySkill;
    private int skillIndex;
    private int[] skillList = Array.Empty<int>();
    private string role = string.Empty;
    private bool isTaunter;
    private string LogPrefix = string.Empty;
    private SkillEngineMode skillEngineMode;
    private bool useSurvivalSkill = true;
    private string? maintainedPotion;
    private int kingsEchoManaThreshold = 12;
    private bool shamanSkillThreeEnabled = true;
    private bool cssNormalInitialManaCheck;
    private bool cssNormalNeedsRegeneration;
    private bool cssGunslingerInitialManaCheck;
    private bool cssGunslingerWaitingForStance;
    private bool cssGunslingerFiring;
    private bool cssGunslingerNeedsRegeneration;
    private volatile bool packetDetectorRunning;
    private string[] packetCommands = Array.Empty<string>();
    private string packetSelectedCommand = string.Empty;
    private string[] packetTexts = Array.Empty<string>();
    private bool packetChoiceMode;
    private string packetSelectedChoice = string.Empty;
    private string packetSkillPauseChoice = string.Empty;
    private int packetDetectionCount;

    private string[] armyPlayers = Array.Empty<string>();
    private string armyUsername = string.Empty;
    private string armySyncPath = string.Empty;
    private string armyLaunchToken = string.Empty;
    private string armySessionId = string.Empty;
    private int armyPlayerIndex = -1;
    private long nextArmyStepId = 1;
    private long lastArmyStepId;
    private bool armyInitialized;
    private bool armySessionStarted;
    private bool armySessionFailureLogged;
    private bool armyStopLogged;
    private bool armyTransportFailed;
    private bool armyTransportFailureLogged;
    private int reportedFightAttempt;
    private bool? reportedFightAlive;

    public Option<string> player1 = new(
        "player1",
        "Player 1",
        "Player 1 is the sync boss.",
        string.Empty
    );

    public Option<string> player2 = new(
        "player2",
        "Player 2",
        "Player 2 account name.",
        string.Empty
    );

    public Option<string> player3 = new(
        "player3",
        "Player 3",
        "Player 3 account name.",
        string.Empty
    );

    public Option<string> player4 = new(
        "player4",
        "Player 4",
        "Player 4 account name.",
        string.Empty
    );

    public Option<string> player5 = new(
        "player5",
        "Player 5",
        "Player 5 account name.",
        string.Empty
    );

    public Option<string> player6 = new(
        "player6",
        "Player 6",
        "Player 6 account name.",
        string.Empty
    );

    public Option<string> player7 = new(
        "player7",
        "Player 7",
        "Player 7 account name.",
        string.Empty
    );

    public void ScriptMain(IScriptInterface Bot)
    {
        Core.RunCore();
    }

    public ClassPreset LegionRevenant() =>
        new()
        {
            ClassName = "Legion Revenant",
            Skills = new[] { 3, 4, 2, 1 },
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Arcanas_Concerto,
            Tonic = "Sage Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset LightCaster(bool healingMode = false) =>
        new()
        {
            ClassName = "LightCaster",
            Skills = healingMode
                ? new[] { 2, 1, 4 }
                : new[] { 2, 1, 3, 4 },
            SkillMode = healingMode
                ? SkillEngineMode.LightCasterHealing
                : SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ArchFiend() =>
        new()
        {
            ClassName = "ArchFiend",
            Skills = new[] { 3, 4, 1, 2 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset VoidHighlord() =>
        new()
        {
            ClassName = "Void Highlord",
            Skills = new[] { 1, 2, 4 },
            SkillMode = SkillEngineMode.VoidHighlord,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset HollowbornVindicator() =>
        new()
        {
            ClassName = "Hollowborn Vindicator",
            Skills = new[] { 3, 4, 1, 4, 1, 3, 1, 3, 1, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset VerusDoomKnight() =>
        new()
        {
            ClassName = "Verus DoomKnight",
            Skills = new[]
            {
                1, 2, 3, 4,
                1, 2, 3,
                1, 2, 3,
                4, 1, 2, 3,
                1, 2, 4, 3,
                1, 2, 3,
            },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset DragonOfTime() =>
        new()
        {
            ClassName = "Dragon of Time",
            Skills = new[] { 3, 2, 1, 2, 4, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            Tonic = "Sage Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Bard() =>
        new()
        {
            ClassName = "Bard",
            Skills = new[] { 1, 4, 2, 3, 1, 2, 3, 4, 1, 3, 4, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Sage Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset KingsEcho() =>
        new()
        {
            ClassName = "King's Echo",
            Skills = new[] { 1, 2 },
            SkillMode = SkillEngineMode.KingsEcho,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ArcanaInvoker() =>
        new()
        {
            ClassName = "Arcana Invoker",
            Skills = new[] { 2, 3, 4 },
            SkillMode = SkillEngineMode.ArcanaInvoker,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ChronoShadowHunter(bool gunslingerMode = false) =>
        new()
        {
            ClassName = "Chrono ShadowHunter",
            AlternateClassNames = new[] { "Chrono ShadowSlayer" },
            SkillMode = gunslingerMode
                ? SkillEngineMode.ChronoShadowHunterGunslinger
                : SkillEngineMode.ChronoShadowHunterStable,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Arcanas_Concerto,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ChaosSlayer() =>
        new()
        {
            ClassName = "Chaos Slayer Berserker",
            AlternateClassNames = new[]
            {
                "Chaos Slayer Mystic",
                "Chaos Slayer Cleric",
                "Chaos Slayer Thief",
            },
            Skills = new[] { 3, 2, 4, 1 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset Shaman(bool farmMode = false) =>
        new()
        {
            ClassName = "Shaman",
            Skills = new[] { 1, 2 },
            SkillMode = farmMode ? SkillEngineMode.Simple : SkillEngineMode.Shaman,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset StoneCrusher() =>
        new()
        {
            ClassName = "StoneCrusher",
            Skills = new[] { 3, 2, 4, 1 },
            BaseEnhancement = EnhancementType.Fighter,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.None,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Might Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset ArchPaladin() =>
        new()
        {
            ClassName = "ArchPaladin",
            Skills = new[] { 3, 2, 1, 4 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset LordOfOrder() =>
        new()
        {
            ClassName = "Lord of Order",
            AlternateClassNames = new[] { "Lord Of Order" },
            Skills = new[] { 2, 3, 1, 4 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.None,
            WeaponEnhancement = WeaponSpecial.Awe_Blast,
            Tonic = "Fate Tonic",
            Elixir = "Potent Destruction Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Oracle() =>
        new()
        {
            ClassName = "Oracle",
            Skills = new[] { 4, 2, 3, 1 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public void EquipClass(ClassPreset preset)
    {
        if (Bot.ShouldExit)
            return;

        if (preset == null || string.IsNullOrWhiteSpace(preset.ClassName))
        {
            Core.Logger(
                "Class preset is invalid.",
                "EquipClass",
                messageBox: true,
                stopBot: true
            );
            return;
        }

        string? className = ResolveClassName(preset);
        if (className == null)
        {
            Core.Logger(
                $"{string.Join(" or ", new[] { preset.ClassName }.Concat(preset.AlternateClassNames))} is not in inventory or bank.",
                "EquipClass",
                messageBox: true,
                stopBot: true
            );
            return;
        }

        if (
            string.Equals(
                Bot.Player.CurrentClass?.Name,
                className,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Core.Logger($"{className} already equipped.", "EquipClass");
            return;
        }

        if (!Bot.Inventory.Contains(className))
        {
            if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                Bot.Bank.Open();

            Bot.Bank.Load(waitForLoad: false);
            Bot.Wait.ForTrue(() => Bot.Bank.Contains(className), 20);

            if (!Bot.Bank.Contains(className))
            {
                Core.Logger(
                    $"{className} is not in inventory or bank.",
                    "EquipClass",
                    messageBox: true,
                    stopBot: true
                );
                return;
            }

            if (!Core.HasSpace)
            {
                Core.Logger(
                    $"{className} could not be moved from bank because no inventory slot is available.",
                    "EquipClass",
                    messageBox: true,
                    stopBot: true
                );
                return;
            }

            Bot.Bank.EnsureToInventory(className);
            Bot.Wait.ForTrue(() => Bot.Inventory.Contains(className), 14);

            if (!Bot.Inventory.Contains(className))
            {
                Core.Logger(
                    $"{className} could not be moved from bank.",
                    "EquipClass",
                    messageBox: true,
                    stopBot: true
                );
                return;
            }
        }

        Core.Equip(className);

        if (
            string.Equals(
                Bot.Player.CurrentClass?.Name,
                className,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Core.Logger($"{className} equipped.", "EquipClass");
            return;
        }

        Core.Logger(
            $"{className} could not be equipped.",
            "EquipClass",
            messageBox: true,
            stopBot: true
        );
    }

    private string? ResolveClassName(ClassPreset preset)
    {
        string primaryClassName = preset.ClassName;
        string[] alternateClassNames = preset.AlternateClassNames;

        if (alternateClassNames.Length == 0)
            return primaryClassName;

        if (
            string.Equals(
                Bot.Player.CurrentClass?.Name,
                primaryClassName,
                StringComparison.OrdinalIgnoreCase
            )
            || Bot.Inventory.Contains(primaryClassName)
        )
            return primaryClassName;

        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForTrue(
            () =>
                Bot.Bank.Contains(primaryClassName)
                || alternateClassNames.Any(name => Bot.Bank.Contains(name)),
            20
        );

        if (Bot.Bank.Contains(primaryClassName))
            return primaryClassName;

        foreach (string alternateClassName in alternateClassNames)
        {
            if (
                string.Equals(
                    Bot.Player.CurrentClass?.Name,
                    alternateClassName,
                    StringComparison.OrdinalIgnoreCase
                )
                || Bot.Inventory.Contains(alternateClassName)
                || Bot.Bank.Contains(alternateClassName)
            )
                return alternateClassName;
        }

        return null;
    }

    public void StartSkillEngine(
        int[] skills,
        string roleName,
        bool taunter,
        string logPrefix,
        SkillEngineMode mode = SkillEngineMode.Simple,
        bool useSurvivalSkill = true,
        string? maintainedPotion = null,
        int kingsEchoManaThreshold = 12
    )
    {
        if (skillEngineRunning)
            return;

        skillList = skills;
        role = roleName;
        isTaunter = taunter;
        LogPrefix = logPrefix;
        skillEngineMode = mode;
        this.useSurvivalSkill = useSurvivalSkill;
        this.maintainedPotion = maintainedPotion;
        this.kingsEchoManaThreshold = kingsEchoManaThreshold;
        skillIndex = 0;
        if (mode == SkillEngineMode.ChronoShadowHunterStable)
            ResetCSSNormalMode();
        if (mode == SkillEngineMode.ChronoShadowHunterGunslinger)
            ResetCSSGunslingerMode();
        skillEnginePaused = 0;
        pendingAbsolutePriorityTauntMapId = 0;
        pendingTauntMapId = 0;
        pendingImmediateTauntMapId = 0;
        pendingSkillFiveMapId = 0;
        pendingImmediateSkillFiveMapId = 0;
        pendingAbsolutePrioritySkill = 0;
        pendingPrioritySkill = 0;
        pendingTargetedPrioritySkill = null;
        pendingLimitedPrioritySkill = null;
        shamanSkillThreeEnabled = true;
        skillEngineRunning = true;

        Bot.Events.ScriptStopping -= OnScriptStopping;
        Bot.Events.ScriptStopping += OnScriptStopping;

        skillThread = new Thread(SkillEngineLoop)
        {
            Name = "LoneWolf Skill Engine",
            IsBackground = true,
        };
        skillThread.Start();
    }

    public void StopSkillEngine()
    {
        skillEngineRunning = false;
        Bot.Events.ScriptStopping -= OnScriptStopping;

        Thread? thread = skillThread;
        if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
            thread.Join(2000);

        skillThread = null;
        skillEnginePaused = 0;
        pendingAbsolutePriorityTauntMapId = 0;
        pendingTauntMapId = 0;
        pendingImmediateTauntMapId = 0;
        pendingSkillFiveMapId = 0;
        pendingImmediateSkillFiveMapId = 0;
        pendingAbsolutePrioritySkill = 0;
        pendingPrioritySkill = 0;
        pendingTargetedPrioritySkill = null;
        pendingLimitedPrioritySkill = null;
        shamanSkillThreeEnabled = true;
        maintainedPotion = null;
    }

    public void SetSkillEngineSkills(int[] skills)
    {
        if (skills.Length == 0)
            return;

        skillList = skills;
        skillIndex = 0;
    }

    public bool StartPacketDetector(string command, string text)
        => StartPacketDetector(new[] { command }, new[] { text });

    public bool StartPacketDetector(string[] commands, string text)
        => StartPacketDetector(commands, new[] { text });

    public bool StartPacketDetector(string command, string[] texts)
        => StartPacketDetector(new[] { command }, texts);

    public bool StartPacketDetector(string[] commands, string[] texts)
        => StartPacketDetector(commands, texts, choiceMode: false);

    public bool StartPacketChoiceDetector(
        string command,
        string[] choices,
        string pauseSkillChoice = ""
    ) => StartPacketDetector(
        new[] { command },
        choices,
        choiceMode: true,
        pauseSkillChoice: pauseSkillChoice
    );

    private bool StartPacketDetector(
        string[] commands,
        string[] texts,
        bool choiceMode,
        string pauseSkillChoice = ""
    )
    {
        StopPacketDetector();

        string[] validCommands = commands?
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        string[] validTexts = texts?
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        if (validCommands.Length == 0 || validTexts.Length == 0)
        {
            Core.Logger(
                "Packet detector command and text are required.",
                "CoreLoneWolf"
            );
            return false;
        }

        packetCommands = validCommands;
        packetSelectedCommand = string.Empty;
        packetTexts = validTexts;
        packetChoiceMode = choiceMode;
        packetSelectedChoice = string.Empty;
        packetSkillPauseChoice = choiceMode
            && validTexts.Contains(pauseSkillChoice, StringComparer.Ordinal)
                ? pauseSkillChoice
                : string.Empty;
        Interlocked.Exchange(ref packetDetectionCount, 0);
        packetDetectorRunning = true;

        Bot.Flash.FlashCall += PacketDetectorFlashCall;
        Bot.Events.ScriptStopping -= OnPacketDetectorStopping;
        Bot.Events.ScriptStopping += OnPacketDetectorStopping;
        return true;
    }

    public bool HasPacketDetection(int detectionNumber) =>
        packetDetectorRunning
        && detectionNumber > 0
        && Volatile.Read(ref packetDetectionCount) >= detectionNumber;

    public string GetPacketDetectorCommand() =>
        Volatile.Read(ref packetSelectedCommand);

    public string GetPacketDetectorChoice() =>
        packetChoiceMode
            ? Volatile.Read(ref packetSelectedChoice)
            : string.Empty;

    public void StopPacketDetector()
    {
        packetDetectorRunning = false;
        Bot.Flash.FlashCall -= PacketDetectorFlashCall;
        Bot.Events.ScriptStopping -= OnPacketDetectorStopping;
        packetCommands = Array.Empty<string>();
        packetSelectedCommand = string.Empty;
        packetTexts = Array.Empty<string>();
        packetChoiceMode = false;
        packetSelectedChoice = string.Empty;
        packetSkillPauseChoice = string.Empty;
        Volatile.Write(ref skillEnginePaused, 0);
        Interlocked.Exchange(ref packetDetectionCount, 0);
    }

    public void ResumeSkillEngine() =>
        Volatile.Write(ref skillEnginePaused, 0);

    public void RequestTaunt(int mapId)
    {
        if (mapId > 0)
            Volatile.Write(ref pendingTauntMapId, mapId);
    }

    public void RequestAbsolutePriorityTaunt(int mapId)
    {
        if (mapId > 0)
            Volatile.Write(ref pendingAbsolutePriorityTauntMapId, mapId);
    }

    public bool RequestImmediateTaunt(int mapId)
    {
        if (
            !Bot.Player.Alive
            || !IsTaunterRole()
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Volatile.Write(ref pendingImmediateTauntMapId, mapId);
        return true;
    }

    public void RequestSkillFive(int mapId)
    {
        if (mapId > 0)
            Volatile.Write(ref pendingSkillFiveMapId, mapId);
    }

    public void RequestAbsolutePrioritySkill(int skill)
    {
        if (skill is >= 1 and <= 4)
            Volatile.Write(ref pendingAbsolutePrioritySkill, skill);
    }

    public bool HasPendingAbsolutePrioritySkill() =>
        Volatile.Read(ref pendingAbsolutePrioritySkill) > 0;

    public void RequestPrioritySkill(int skill)
    {
        if (skill is >= 1 and <= 4)
            Volatile.Write(ref pendingPrioritySkill, skill);
    }

    public bool HasPendingPrioritySkill() =>
        Volatile.Read(ref pendingPrioritySkill) > 0;

    public void RequestLimitedPrioritySkill(int skill, int uses)
    {
        if (skill is < 1 or > 4 || uses <= 0)
            return;

        Volatile.Write(
            ref pendingLimitedPrioritySkill,
            new LimitedPrioritySkillRequest(skill, uses)
        );
    }

    public bool RequestTargetedPrioritySkill(
        int skill,
        int targetMapId,
        int returnMapId
    )
    {
        if (
            skill is < 1 or > 4
            || targetMapId <= 0
            || returnMapId <= 0
        )
            return false;

        TargetedPrioritySkillRequest request = new(
            skill,
            targetMapId,
            returnMapId
        );
        return Interlocked.CompareExchange(
            ref pendingTargetedPrioritySkill,
            request,
            null
        ) == null;
    }

    public bool HasPendingTargetedPrioritySkill() =>
        Volatile.Read(ref pendingTargetedPrioritySkill) != null;

    public void SetShamanSkillThreeEnabled(bool enabled) =>
        Volatile.Write(ref shamanSkillThreeEnabled, enabled);

    public bool RequestImmediateSkillFive(int mapId)
    {
        if (
            !Bot.Player.Alive
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Volatile.Write(ref pendingImmediateSkillFiveMapId, mapId);
        return true;
    }

    private bool OnScriptStopping(Exception? exception)
    {
        StopSkillEngine();
        return true;
    }

    private bool OnPacketDetectorStopping(Exception? exception)
    {
        StopPacketDetector();
        return true;
    }

    private void PacketDetectorFlashCall(string function, object[] args)
    {
        if (
            !packetDetectorRunning
            || !string.Equals(function, "pext", StringComparison.Ordinal)
            || args.Length == 0
            || args[0] is not string packet
        )
            return;

        string[] texts = packetTexts;
        bool choiceMode = packetChoiceMode;
        string selectedChoice = string.Empty;

        if (texts.Length == 0)
            return;

        if (choiceMode)
        {
            foreach (string text in texts)
            {
                if (!packet.Contains(text, StringComparison.Ordinal))
                    continue;

                selectedChoice = text;
                break;
            }

            if (selectedChoice.Length == 0)
                return;
        }
        else if (
            texts.Any(text =>
                !packet.Contains(text, StringComparison.Ordinal)
            )
        )
            return;

        if (!packetDetectorRunning)
            return;

        string selectedCommand = Volatile.Read(ref packetSelectedCommand);
        if (selectedCommand.Length == 0)
        {
            foreach (string command in packetCommands)
            {
                string commandMarker = $"\"cmd\":\"{command}\"";
                if (!packet.Contains(commandMarker, StringComparison.Ordinal))
                    continue;

                Interlocked.CompareExchange(
                    ref packetSelectedCommand,
                    command,
                    string.Empty
                );
                selectedCommand = Volatile.Read(ref packetSelectedCommand);
                break;
            }
        }

        if (
            selectedCommand.Length == 0
            || !packet.Contains(
                $"\"cmd\":\"{selectedCommand}\"",
                StringComparison.Ordinal
            )
            || !packetDetectorRunning
        )
            return;

        if (choiceMode)
        {
            if (string.Equals(
                selectedChoice,
                Volatile.Read(ref packetSkillPauseChoice),
                StringComparison.Ordinal
            ))
                Volatile.Write(ref skillEnginePaused, 1);

            Volatile.Write(ref packetSelectedChoice, selectedChoice);
        }

        Interlocked.Increment(ref packetDetectionCount);
    }

    private void SkillEngineLoop()
    {
        try
        {
            while (skillEngineRunning && !Bot.ShouldExit)
            {
                if (!Bot.Player.Alive)
                {
                    if (
                        skillEngineMode is SkillEngineMode.Simple
                            or SkillEngineMode.LightCasterHealing
                            or SkillEngineMode.VoidHighlord
                    )
                        skillIndex = 0;

                    Volatile.Write(ref pendingAbsolutePrioritySkill, 0);
                    Volatile.Write(ref pendingPrioritySkill, 0);
                    Volatile.Write(ref pendingLimitedPrioritySkill, null);
                }

                int absolutePriorityTauntMapId = Volatile.Read(
                    ref pendingAbsolutePriorityTauntMapId
                );
                if (absolutePriorityTauntMapId > 0)
                {
                    if (
                        !Bot.Player.Alive
                        || !IsTaunterRole()
                        || GetMonsterHP(absolutePriorityTauntMapId) <= 0
                    )
                    {
                        Interlocked.CompareExchange(
                            ref pendingAbsolutePriorityTauntMapId,
                            0,
                            absolutePriorityTauntMapId
                        );
                        Volatile.Write(ref skillEnginePaused, 0);
                    }
                    else if (
                        Bot.Skills.CanUseSkill(5)
                        && Bot.Skills.UseSkill(5)
                    )
                    {
                        Interlocked.CompareExchange(
                            ref pendingAbsolutePriorityTauntMapId,
                            0,
                            absolutePriorityTauntMapId
                        );
                        Volatile.Write(ref skillEnginePaused, 0);
                        Core.Logger(
                            $"{LogPrefix} {role} used absolute priority taunt."
                        );
                    }

                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                if (Volatile.Read(ref skillEnginePaused) > 0)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                if (Bot.Combat.StopAttacking)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                int absolutePrioritySkill = Volatile.Read(
                    ref pendingAbsolutePrioritySkill
                );
                bool absolutePrioritySkillPending = absolutePrioritySkill > 0;
                bool absolutePrioritySkillUsed = absolutePrioritySkillPending
                    && Bot.Skills.CanUseSkill(absolutePrioritySkill)
                    && Bot.Skills.UseSkill(absolutePrioritySkill);

                if (absolutePrioritySkillUsed)
                {
                    Interlocked.CompareExchange(
                        ref pendingAbsolutePrioritySkill,
                        0,
                        absolutePrioritySkill
                    );
                    Core.Logger($"{LogPrefix} {role} used absolute priority skill {absolutePrioritySkill}.");
                }

                if (absolutePrioritySkillPending)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                int tauntMapId = Interlocked.Exchange(ref pendingTauntMapId, 0);

                if (tauntMapId > 0)
                    TauntMonster(tauntMapId);
                else
                {
                    int immediateTauntMapId = Interlocked.Exchange(
                        ref pendingImmediateTauntMapId,
                        0
                    );
                    bool immediateTauntUsed = immediateTauntMapId > 0
                        && ImmediateTaunt(immediateTauntMapId);

                    int skillFiveMapId = immediateTauntUsed
                        ? 0
                        : Interlocked.Exchange(ref pendingSkillFiveMapId, 0);
                    bool skillFiveUsed = !immediateTauntUsed
                        && skillFiveMapId > 0
                        && UseSkillFive(skillFiveMapId);

                    int immediateSkillFiveMapId = immediateTauntUsed || skillFiveUsed
                        ? 0
                        : Interlocked.Exchange(
                            ref pendingImmediateSkillFiveMapId,
                            0
                        );
                    bool immediateSkillFiveUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && immediateSkillFiveMapId > 0
                        && ImmediateSkillFive(immediateSkillFiveMapId);

                    TargetedPrioritySkillRequest? targetedPrioritySkill =
                        Volatile.Read(ref pendingTargetedPrioritySkill);
                    bool targetedPrioritySkillPending =
                        targetedPrioritySkill != null;
                    bool targetedPrioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && targetedPrioritySkill != null
                        && UseTargetedPrioritySkill(targetedPrioritySkill);

                    if (targetedPrioritySkillUsed)
                    {
                        Interlocked.CompareExchange(
                            ref pendingTargetedPrioritySkill,
                            null,
                            targetedPrioritySkill
                        );
                    }

                    int prioritySkill = Volatile.Read(ref pendingPrioritySkill);
                    bool prioritySkillPending = prioritySkill > 0;
                    bool prioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && prioritySkillPending
                        && Bot.Skills.CanUseSkill(prioritySkill)
                        && Bot.Skills.UseSkill(prioritySkill);

                    if (prioritySkillUsed)
                    {
                        Interlocked.CompareExchange(
                            ref pendingPrioritySkill,
                            0,
                            prioritySkill
                        );
                        Core.Logger($"{LogPrefix} {role} used priority skill {prioritySkill}.");
                    }

                    LimitedPrioritySkillRequest? limitedPrioritySkill =
                        Volatile.Read(ref pendingLimitedPrioritySkill);
                    bool limitedPrioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && limitedPrioritySkill != null
                        && Bot.Skills.CanUseSkill(limitedPrioritySkill.Skill)
                        && Bot.Skills.UseSkill(limitedPrioritySkill.Skill);

                    if (limitedPrioritySkillUsed && limitedPrioritySkill != null)
                    {
                        LimitedPrioritySkillRequest? replacement =
                            limitedPrioritySkill.RemainingUses > 1
                                ? new LimitedPrioritySkillRequest(
                                    limitedPrioritySkill.Skill,
                                    limitedPrioritySkill.RemainingUses - 1
                                )
                                : null;
                        LimitedPrioritySkillRequest? observed =
                            Interlocked.CompareExchange(
                                ref pendingLimitedPrioritySkill,
                                replacement,
                                limitedPrioritySkill
                            );
                        int remainingUses = observed == limitedPrioritySkill
                            ? replacement?.RemainingUses ?? 0
                            : Volatile.Read(ref pendingLimitedPrioritySkill)
                                ?.RemainingUses ?? 0;
                        Core.Logger(
                            $"{LogPrefix} {role} used limited priority skill {limitedPrioritySkill.Skill}. {remainingUses} uses remain."
                        );
                    }

                    bool potionUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && !limitedPrioritySkillUsed
                        && !string.IsNullOrWhiteSpace(maintainedPotion)
                        && MaintainPotion(maintainedPotion);

                    if (
                        !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && !limitedPrioritySkillUsed
                        && !potionUsed
                    )
                    {
                        if (skillEngineMode == SkillEngineMode.Strict)
                            StrictSkillEngine(skillList, ref skillIndex);
                        else if (skillEngineMode == SkillEngineMode.KingsEcho)
                            KingsEchoSkillEngine(useSurvivalSkill);
                        else if (skillEngineMode == SkillEngineMode.ArcanaInvoker)
                            ArcanaInvokerSkillEngine();
                        else if (skillEngineMode == SkillEngineMode.ChronoShadowHunterStable)
                            CSSNormalMode();
                        else if (
                            skillEngineMode == SkillEngineMode.ChronoShadowHunterGunslinger
                        )
                            CSSGunslingerMode();
                        else if (skillEngineMode == SkillEngineMode.Shaman)
                            ShamanSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.LightCasterHealing
                        )
                            LightCasterHealingSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.VoidHighlord
                        )
                            VoidHighlordSkillEngine();
                        else
                            CustomSkillEngine();
                    }
                }

                Bot.Sleep(SkillPollDelay);
            }
        }
        catch (Exception ex)
        {
            Core.Logger($"{LogPrefix} skill engine stopped: {ex.Message}");
        }
        finally
        {
            skillEngineRunning = false;
            skillEnginePaused = 0;
            pendingAbsolutePriorityTauntMapId = 0;
            pendingTauntMapId = 0;
            pendingImmediateTauntMapId = 0;
            pendingSkillFiveMapId = 0;
            pendingImmediateSkillFiveMapId = 0;
            pendingAbsolutePrioritySkill = 0;
            pendingPrioritySkill = 0;
            pendingTargetedPrioritySkill = null;
            pendingLimitedPrioritySkill = null;
            shamanSkillThreeEnabled = true;
            maintainedPotion = null;
        }
    }

    private bool UseTargetedPrioritySkill(
        TargetedPrioritySkillRequest request
    )
    {
        if (
            !Bot.Player.Alive
            || request.TargetMapId <= 0
            || GetMonsterHP(request.TargetMapId) <= 0
        )
            return false;

        Bot.Combat.Attack(request.TargetMapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.Alive
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != request.TargetMapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(request.Skill)
            || !Bot.Skills.UseSkill(request.Skill)
        )
            return false;

        Core.Logger(
            $"{LogPrefix} {role} used targeted priority skill {request.Skill} on MapID {request.TargetMapId}."
        );

        if (GetMonsterHP(request.ReturnMapId) > 0)
            Bot.Combat.Attack(request.ReturnMapId);

        return true;
    }

    private bool ImmediateTaunt(int mapId)
    {
        if (
            !Bot.Player.Alive
            || !IsTaunterRole()
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != mapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        if (!Bot.Skills.UseSkill(5))
            return false;

        Core.Logger($"{LogPrefix} {role} used immediate taunt.");
        return true;
    }

    private bool UseSkillFive(int mapId)
    {
        if (!Bot.Player.Alive || mapId <= 0 || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used skill 5 immediately.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} waiting 750ms for skill 5.");
        Bot.Sleep(750);

        if (!Bot.Player.Alive || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used skill 5 after 750ms.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} could not use skill 5 after 750ms.");
        Bot.Combat.Attack(mapId);
        return false;
    }

    private bool ImmediateSkillFive(int mapId)
    {
        if (
            !Bot.Player.Alive
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != mapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        if (!Bot.Skills.UseSkill(5))
            return false;

        Core.Logger($"{LogPrefix} {role} used immediate skill 5.");
        return true;
    }

    public bool TauntMonster(int mapId)
    {
        if (!Bot.Player.Alive || !IsTaunterRole() || mapId <= 0 || GetMonsterHP(mapId) <= 0)
        {
            return false;
        }

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used taunt immediately.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} waiting 750ms for skill 5.");
        Bot.Sleep(750);

        if (!Bot.Player.Alive || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used taunt after 750ms.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} could not use skill 5 after 750ms.");
        Bot.Combat.Attack(mapId);
        return false;
    }

    public void CustomSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        int[] skillList = GetSkillList();

        if (skillList.Length == 0)
            return;

        for (int offset = 0; offset < skillList.Length; offset++)
        {
            int index = (skillIndex + offset) % skillList.Length;
            int skill = skillList[index];

            if (!Bot.Skills.CanUseSkill(skill))
                continue;

            Bot.Skills.UseSkill(skill);
            skillIndex = (index + 1) % skillList.Length;
            return;
        }
    }

    private void LightCasterHealingSkillEngine()
    {
        if (Bot.Skills.CanUseSkill(3))
        {
            Bot.Skills.UseSkill(3);
            return;
        }

        CustomSkillEngine();
    }

    private void VoidHighlordSkillEngine()
    {
        double healthPercentage =
            Bot.Player.MaxHealth > 0
                ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
                : 0;

        if (
            healthPercentage > 90
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    public void DirectSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        int[] skillList = GetSkillList();

        if (skillList.Length == 0)
            return;

        for (int offset = 0; offset < skillList.Length; offset++)
        {
            int index = (skillIndex + offset) % skillList.Length;

            if (!Bot.Skills.UseSkill(skillList[index]))
                continue;

            skillIndex = (index + 1) % skillList.Length;
            return;
        }
    }

    public void StrictSkillEngine(int[] skills, ref int index)
    {
        if (!Bot.Player.Alive)
        {
            index = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (skills.Length == 0)
            return;

        int skill = skills[index];

        if (!Bot.Skills.CanUseSkill(skill))
            return;

        if (!Bot.Skills.UseSkill(skill))
            return;

        index = (index + 1) % skills.Length;
    }

    public void KingsEchoSkillEngine(bool useSurvivalSkill)
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        float residualEnergy = Bot.Self.GetAura("Residual Energy")?.Value ?? 0;

        if (
            (residualEnergy >= 24 || Bot.Player.Mana < kingsEchoManaThreshold)
            && Bot.Skills.CanUseSkill(4)
            && Bot.Skills.UseSkill(4)
        )
            return;

        double healthPercentage =
            Bot.Player.MaxHealth > 0
                ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
                : 0;
        double manaPercentage =
            Bot.Player.MaxMana > 0 ? (double)Bot.Player.Mana / Bot.Player.MaxMana * 100 : 0;

        if (
            useSurvivalSkill
            && healthPercentage < 50
            && manaPercentage > 24
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    private void ArcanaInvokerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var worldAura = Bot.Self.GetAura(ArcanaInvokerWorldAura);
        if (worldAura != null)
        {
            if (worldAura.RemainingTime > 1)
                DirectSkillEngine();

            else
            {
                skillIndex = 0;
                if (Bot.Skills.CanUseSkill(1))
                    Bot.Skills.UseSkill(1);
            }

            return;
        }

        if (
            Bot.Self.HasActiveAura(ArcanaInvokerFoolAura)
            && !Bot.Self.HasActiveAura(ArcanaInvokerJudgementAura)
        )
        {
            DirectSkillEngine();
            return;
        }

        skillIndex = 0;
        if (Bot.Skills.CanUseSkill(1))
            Bot.Skills.UseSkill(1);
    }

    private void ShamanSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var elementalEmbrace = Bot.Target.GetAura(ShamanElementalEmbraceAura);
        if (
            (elementalEmbrace == null || elementalEmbrace.RemainingTime <= 5)
            && Bot.Skills.CanUseSkill(4)
            && Bot.Skills.UseSkill(4)
        )
            return;

        if (
            Volatile.Read(ref shamanSkillThreeEnabled)
            && Bot.Player.Mana > 80
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    private void CSSNormalMode()
    {
        if (!Bot.Player.Alive)
        {
            ResetCSSNormalMode();
            return;
        }

        if (cssNormalInitialManaCheck)
        {
            if (Bot.Player.Mana < 100)
            {
                if (Bot.Skills.UseSkill(1))
                    cssNormalInitialManaCheck = false;
                return;
            }

            cssNormalInitialManaCheck = false;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (cssNormalNeedsRegeneration)
        {
            if (Bot.Skills.UseSkill(1))
                cssNormalNeedsRegeneration = false;
            return;
        }

        if (
            Bot.Player.Mana < 5
            || Bot.Self.HasActiveAura(ChronoShadowHunterRoundsEmptyAura)
        )
        {
            if (Bot.Skills.UseSkill(4))
                cssNormalNeedsRegeneration = true;
            return;
        }

        Bot.Skills.UseSkill(3);
    }

    private void ResetCSSNormalMode()
    {
        cssNormalInitialManaCheck = true;
        cssNormalNeedsRegeneration = false;
    }

    private void CSSGunslingerMode()
    {
        if (!Bot.Player.Alive)
        {
            ResetCSSGunslingerMode();
            return;
        }

        if (cssGunslingerInitialManaCheck)
        {
            if (Bot.Player.Mana < 100)
            {
                if (Bot.Skills.UseSkill(1))
                    cssGunslingerInitialManaCheck = false;
                return;
            }

            cssGunslingerInitialManaCheck = false;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (cssGunslingerNeedsRegeneration)
        {
            if (Bot.Skills.UseSkill(1))
                cssGunslingerNeedsRegeneration = false;
            return;
        }

        if (cssGunslingerWaitingForStance)
        {
            if (!Bot.Self.HasActiveAura(ChronoShadowHunterGunslingerAura))
                return;

            cssGunslingerWaitingForStance = false;
            cssGunslingerFiring = true;
        }

        if (cssGunslingerFiring)
        {
            if (Bot.Player.Mana < 10)
            {
                if (Bot.Skills.UseSkill(4))
                {
                    cssGunslingerFiring = false;
                    cssGunslingerNeedsRegeneration = true;
                }
                return;
            }

            Bot.Skills.UseSkill(0);
            return;
        }

        if (
            Bot.Player.Mana < 5
            || Bot.Self.HasActiveAura(ChronoShadowHunterRoundsEmptyAura)
        )
        {
            if (Bot.Skills.UseSkill(1))
                cssGunslingerWaitingForStance = true;
            return;
        }

        Bot.Skills.UseSkill(3);
    }

    private void ResetCSSGunslingerMode()
    {
        cssGunslingerInitialManaCheck = true;
        cssGunslingerWaitingForStance = false;
        cssGunslingerFiring = false;
        cssGunslingerNeedsRegeneration = false;
    }

    private bool IsTaunterRole() => isTaunter;

    private int[] GetSkillList() => skillList;

    public int GetMonsterHP(int mapId)
    {
        var monsters = Bot.Monsters?.MapMonsters;
        if (mapId <= 0 || monsters == null)
            return 0;

        foreach (var monster in monsters)
        {
            if (monster != null && monster.MapID == mapId)
                return monster.HP;
        }

        return 0;
    }

    public bool IsMonsterAlive(int mapId) => GetMonsterHP(mapId) > 0;

    public void MaintainTarget(int mapId)
    {
        if (!Bot.Player.Alive || mapId <= 0 || !IsMonsterAlive(mapId))
            return;

        var target = Bot.Player.Target;
        if (
            !Bot.Player.HasTarget
            || target?.MapID != mapId
            || target?.HP <= 0
        )
            Bot.Combat.Attack(mapId);
    }

    public void GenericPrebuff()
    {
        Bot.Skills.UseSkill(3);
        Bot.Sleep(750);
        Bot.Skills.UseSkill(2);
        Bot.Sleep(750);
        Bot.Skills.UseSkill(1);
        Bot.Sleep(750);
    }

    public string GetDivineElixir()
    {
        const string divineElixir = "Divine Elixir";
        const string unstableDivineElixir = "Unstable Divine Elixir";

        if (!Bot.Inventory.Contains(unstableDivineElixir))
        {
            if (!Bot.Bank.Loaded)
            {
                if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                    Bot.Bank.Open();

                Bot.Bank.Load(waitForLoad: false);
                Bot.Wait.ForBankLoad(20);
            }

            if (Bot.Bank.Contains(unstableDivineElixir))
            {
                if (MoveBankItemToInventory(unstableDivineElixir))
                    Core.Logger(
                        $"{unstableDivineElixir} moved from bank.",
                        "GetDivineElixir"
                    );
                else
                    Core.Logger(
                        $"{unstableDivineElixir} could not be moved from bank.",
                        "GetDivineElixir"
                    );
            }
        }

        if (Bot.Inventory.Contains(unstableDivineElixir))
        {
            Core.Logger(
                $"{unstableDivineElixir} already available.",
                "GetDivineElixir"
            );
            return unstableDivineElixir;
        }

        if (!Bot.Inventory.Contains(divineElixir) && !Core.HasSpace)
        {
            Core.Logger(
                $"{divineElixir} skipped because no free inventory slot is available.",
                "GetDivineElixir"
            );
            return divineElixir;
        }

        Core.KillMonster(
            "poisonforest",
            "r15",
            "Left",
            41,
            divineElixir,
            10,
            isTemp: false
        );
        return divineElixir;
    }

    public void PreparePotions(
        string tonicName,
        string elixirName,
        string? potionName = null
    )
    {
        PreparePotion(tonicName, PotionCategory.Tonic);
        PreparePotion(elixirName, PotionCategory.Elixir);

        if (!string.IsNullOrWhiteSpace(potionName))
            PreparePotion(potionName, PotionCategory.CombatPotion);
    }

    public void UsePotions(
        string tonicName,
        string elixirName,
        string? potionName = null
    )
    {
        UsePotion(tonicName, PotionCategory.Tonic);
        UsePotion(elixirName, PotionCategory.Elixir);

        if (!string.IsNullOrWhiteSpace(potionName))
            UsePotion(potionName, PotionCategory.CombatPotion);
    }

    public bool MaintainPotion(string potionName)
    {
        if (
            !Bot.Player.Alive
            || !IsSupportedPotion(potionName, PotionCategory.CombatPotion)
            || !Bot.Inventory.IsEquipped(potionName)
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        var aura = Bot.Self.GetAura(GetPotionAuraName(potionName));
        if (
            aura != null
            && aura.ExpiresAt - DateTimeOffset.Now > TimeSpan.FromSeconds(5)
        )
            return false;

        return Bot.Skills.UseSkill(5);
    }

    public void EquipScroll(string scrollName)
    {
        if (Bot.ShouldExit)
            return;

        if (string.IsNullOrWhiteSpace(scrollName))
        {
            Core.Logger(
                "Scroll equipping skipped because no scroll was specified.",
                "EquipScroll"
            );
            return;
        }

        bool required = string.Equals(
            scrollName,
            EnrageScroll,
            StringComparison.OrdinalIgnoreCase
        );

        try
        {
            if (!Bot.Inventory.Contains(scrollName))
            {
                Core.Logger(
                    $"{scrollName} is not in inventory.",
                    "EquipScroll",
                    messageBox: required,
                    stopBot: required
                );
                return;
            }

            if (Bot.Inventory.IsEquipped(scrollName))
            {
                Core.Logger($"{scrollName} already equipped.", "EquipScroll");
                return;
            }

            Bot.Inventory.EquipUsableItem(scrollName);
            Bot.Wait.ForItemEquip(scrollName);

            if (!Bot.Inventory.IsEquipped(scrollName))
            {
                Core.Logger(
                    $"{scrollName} could not be equipped.",
                    "EquipScroll",
                    messageBox: required,
                    stopBot: required
                );
                return;
            }

            Bot.Sleep(2000);
            Core.Logger($"{scrollName} equipped.", "EquipScroll");
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreLoneWolf scroll equip failed for {scrollName}: {ex}");
            Core.Logger(
                $"{scrollName} could not be equipped.",
                "EquipScroll",
                messageBox: required,
                stopBot: required
            );
        }
    }

    private void PreparePotion(string itemName, PotionCategory category)
    {
        if (Bot.ShouldExit)
            return;

        if (!IsSupportedPotion(itemName, category))
        {
            Core.Logger(
                $"{itemName} is not supported as a {GetPotionCategoryName(category)}.",
                "PreparePotions"
            );
            return;
        }

        try
        {
            if (Bot.Inventory.Contains(itemName))
            {
                Core.Logger($"{itemName} already available.", "PreparePotions");
                return;
            }

            if (Bot.Bank.Contains(itemName))
            {
                if (!MoveBankItemToInventory(itemName))
                    Core.Logger($"{itemName} could not be moved from bank.", "PreparePotions");
                else
                    Core.Logger($"{itemName} moved from bank.", "PreparePotions");
                return;
            }

            if (
                !TryGetPotionRecipe(
                    itemName,
                    out string voucherName,
                    out int voucherQuantity,
                    out int voucherCost,
                    out int potionQuantity,
                    out string? factionName,
                    out int factionRank
                )
            )
            {
                Core.Logger(
                    $"{itemName} skipped because no purchase recipe is configured.",
                    "PreparePotions"
                );
                return;
            }

            if (
                factionName != null
                && !Bot.Reputation.HasRank(factionName, factionRank)
            )
            {
                Core.Logger(
                    $"{itemName} skipped because {factionName} rank {factionRank} is required.",
                    "PreparePotions"
                );
                return;
            }

            int requiredSlots = Bot.Inventory.Contains(voucherName) ? 1 : 2;
            if (Bot.Inventory.FreeSlots < requiredSlots)
            {
                Core.Logger(
                    $"{itemName} skipped because {requiredSlots} free inventory slots are required.",
                    "PreparePotions"
                );
                return;
            }

            if (Bot.Bank.Contains(voucherName) && !MoveBankItemToInventory(voucherName))
            {
                Core.Logger(
                    $"{itemName} skipped because {voucherName} could not be moved from bank.",
                    "PreparePotions"
                );
                return;
            }

            int missingVouchers = Math.Max(
                0,
                voucherQuantity - Bot.Inventory.GetQuantity(voucherName)
            );
            int requiredGold = missingVouchers * voucherCost;

            if (Bot.Player.Gold < requiredGold)
            {
                Core.Logger(
                    $"{itemName} skipped because {requiredGold} gold is required.",
                    "PreparePotions"
                );
                return;
            }

            if (missingVouchers > 0)
            {
                if (!Core.HasSpace)
                {
                    Core.Logger(
                        $"{itemName} skipped because no free inventory slot is available.",
                        "PreparePotions"
                    );
                    return;
                }

                Core.BuyItem(
                    PotionShopMap,
                    PotionShopId,
                    voucherName,
                    voucherQuantity
                );

                if (Bot.Inventory.GetQuantity(voucherName) < voucherQuantity)
                {
                    Core.Logger(
                        $"{itemName} skipped because the required vouchers could not be obtained.",
                        "PreparePotions"
                    );
                    return;
                }
            }

            if (!Core.HasSpace)
            {
                Core.Logger(
                    $"{itemName} skipped because no free inventory slot is available.",
                    "PreparePotions"
                );
                return;
            }

            Core.BuyItem(
                PotionShopMap,
                PotionShopId,
                itemName,
                potionQuantity
            );

            if (!Bot.Inventory.Contains(itemName))
            {
                Core.Logger($"{itemName} could not be purchased.", "PreparePotions");
                return;
            }

            Core.Logger($"{itemName} purchased.", "PreparePotions");
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreLoneWolf potion preparation failed for {itemName}: {ex}");
            Core.Logger($"{itemName} preparation failed.", "PreparePotions");
        }
    }

    private void UsePotion(string itemName, PotionCategory category)
    {
        if (Bot.ShouldExit)
            return;

        if (!IsSupportedPotion(itemName, category))
        {
            Core.Logger(
                $"{itemName} is not supported as a {GetPotionCategoryName(category)}.",
                "UsePotions"
            );
            return;
        }

        try
        {
            if (!Bot.Inventory.Contains(itemName))
            {
                Core.Logger(
                    $"{itemName} skipped because it is not in inventory.",
                    "UsePotions"
                );
                return;
            }

            string auraName = GetPotionAuraName(itemName);
            bool auraActive = Bot.Self.HasActiveAura(auraName);

            if (auraActive && category != PotionCategory.CombatPotion)
            {
                Core.Logger($"{itemName} already active.", "UsePotions");
                return;
            }

            if (!EquipPotion(itemName))
                return;

            Bot.Sleep(PotionSuccessDelay);

            if (auraActive)
            {
                Core.Logger(
                    $"{itemName} already active and equipped.",
                    "UsePotions"
                );
                return;
            }

            Bot.Skills.UseSkill(5);
            Bot.Sleep(PotionAuraCheckDelay);

            if (!Bot.Self.HasActiveAura(auraName))
            {
                Core.Logger(
                    $"{itemName} was not verified after use.",
                    "UsePotions"
                );
                return;
            }

            Core.Logger($"{itemName} applied.", "UsePotions");
            Bot.Sleep(PotionSuccessDelay);
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreLoneWolf potion use failed for {itemName}: {ex}");
            Core.Logger($"{itemName} use failed.", "UsePotions");
        }
    }

    private bool EquipPotion(string itemName)
    {
        if (Bot.Inventory.IsEquipped(itemName))
            return true;

        Bot.Inventory.EquipUsableItem(itemName);
        Bot.Wait.ForItemEquip(itemName);

        if (Bot.Inventory.IsEquipped(itemName))
            return true;

        Core.Logger($"{itemName} could not be equipped.", "UsePotions");
        return false;
    }

    private bool MoveBankItemToInventory(string itemName)
    {
        if (!Bot.Bank.Contains(itemName))
            return false;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
            return false;

        int inventoryQuantity = Bot.Inventory.GetQuantity(itemName);
        Bot.Bank.EnsureToInventory(itemName);
        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(itemName) > inventoryQuantity,
            14
        );
        return Bot.Inventory.GetQuantity(itemName) > inventoryQuantity;
    }

    private static bool IsSupportedPotion(
        string itemName,
        PotionCategory category
    ) =>
        category switch
        {
            PotionCategory.Tonic => itemName is "Might Tonic" or "Sage Tonic" or "Fate Tonic",
            PotionCategory.Elixir =>
                itemName
                    is "Potent Battle Elixir"
                    or "Potent Malevolence Elixir"
                    or "Potent Destruction Elixir"
                    or "Divine Elixir"
                    or "Unstable Divine Elixir",
            PotionCategory.CombatPotion =>
                itemName is "Potent Honor Potion" or "Felicitous Philtre",
            _ => false,
        };

    private static string GetPotionCategoryName(PotionCategory category) =>
        category switch
        {
            PotionCategory.Tonic => "tonic",
            PotionCategory.Elixir => "elixir",
            PotionCategory.CombatPotion => "combat potion",
            _ => "potion",
        };

    private static string GetPotionAuraName(string itemName) =>
        itemName switch
        {
            "Sage Tonic" => "Sage",
            "Might Tonic" => "Might",
            "Fate Tonic" => "Fate",
            "Potent Honor Potion" => "Potent Honor Malice",
            "Felicitous Philtre" => "Felicitous Philtre",
            _ => itemName,
        };

    private static bool TryGetPotionRecipe(
        string itemName,
        out string voucherName,
        out int voucherQuantity,
        out int voucherCost,
        out int potionQuantity,
        out string? factionName,
        out int factionRank
    )
    {
        voucherName = "Gold Voucher 500k";
        voucherQuantity = 0;
        voucherCost = 500_000;
        potionQuantity = 0;
        factionName = null;
        factionRank = 0;

        switch (itemName)
        {
            case "Sage Tonic":
            case "Might Tonic":
                voucherQuantity = 2;
                potionQuantity = 10;
                factionName = "Alchemy";
                factionRank = 8;
                return true;

            case "Fate Tonic":
                voucherQuantity = 4;
                potionQuantity = 10;
                factionName = "Alchemy";
                factionRank = 8;
                return true;

            case "Potent Battle Elixir":
            case "Potent Malevolence Elixir":
                voucherQuantity = 4;
                potionQuantity = 8;
                return true;

            case "Potent Destruction Elixir":
                voucherQuantity = 2;
                potionQuantity = 8;
                return true;

            case "Potent Honor Potion":
                voucherQuantity = 1;
                potionQuantity = 5;
                factionName = "Good";
                factionRank = 10;
                return true;

            case "Felicitous Philtre":
                voucherName = "Gold Voucher 100k";
                voucherQuantity = 2;
                voucherCost = 100_000;
                potionQuantity = 25;
                return true;

            default:
                return false;
        }
    }

    public void PrepareScrolls(string scrollName)
    {
        if (Bot.ShouldExit)
            return;

        if (string.IsNullOrWhiteSpace(scrollName))
        {
            Core.Logger(
                "Scroll preparation skipped because no scroll was specified.",
                "PrepareScrolls"
            );
            return;
        }

        if (
            string.Equals(scrollName, DecayScroll, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scrollName, MystifyScroll, StringComparison.OrdinalIgnoreCase)
        )
        {
            PrepareOptionalScroll(
                string.Equals(scrollName, DecayScroll, StringComparison.OrdinalIgnoreCase)
                    ? DecayScroll
                    : MystifyScroll
            );
            return;
        }

        if (!string.Equals(scrollName, EnrageScroll, StringComparison.OrdinalIgnoreCase))
        {
            Core.Logger(
                $"{scrollName} preparation skipped because it is not supported.",
                "PrepareScrolls"
            );
            return;
        }

        scrollName = EnrageScroll;

        try
        {
            if (!Bot.Reputation.HasRank("SpellCrafting", 5))
            {
                ScrollPreparationFailed(
                    scrollName,
                    "SpellCrafting rank 5 is required"
                );
                return;
            }

            if (Bot.Inventory.GetQuantity(scrollName) >= EnrageThreshold)
            {
                Core.Logger($"{scrollName} already available.", "PrepareScrolls");
                return;
            }

            bool scrollWasBanked = Bot.Bank.Contains(scrollName);
            if (
                scrollWasBanked
                && !MoveBankedScrollItem(
                    scrollName,
                    scrollName,
                    EnrageThreshold
                )
            )
                return;

            if (Bot.Inventory.GetQuantity(scrollName) >= EnrageThreshold)
            {
                Core.Logger($"{scrollName} moved from bank.", "PrepareScrolls");
                return;
            }

            bool useGold = Bot.Player.Gold >= 1_000_000;
            bool prepared = useGold
                ? PrepareEnrageWithGold()
                : PrepareEnrageByFarming();

            if (!prepared || Bot.ShouldExit)
                return;

            Core.Logger(
                $"{scrollName} prepared {(useGold ? "with gold" : "by farming")}.",
                "PrepareScrolls"
            );
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreLoneWolf scroll preparation failed for {scrollName}: {ex}");
            ScrollPreparationFailed(scrollName, "an unexpected error occurred");
        }
    }

    private bool PrepareEnrageWithGold()
    {
        const string voucher = "Gold Voucher 500k";
        const string quill = "Arcane Quill";
        const string ink = "Zealous Ink";

        int requiredTurnIns =
            (
                EnrageMaxStack
                - Bot.Inventory.GetQuantity(EnrageScroll)
                + EnrageRewardQuantity
                - 1
            ) / EnrageRewardQuantity;

        Core.Join(SpellcraftMap);

        if (Bot.ShouldExit)
            return false;

        if (
            !MoveBankedScrollItem(EnrageScroll, voucher, 2)
            || !MoveBankedScrollItem(EnrageScroll, quill, 10)
            || !MoveBankedScrollItem(EnrageScroll, ink, requiredTurnIns)
        )
            return false;

        if (
            !PrepareGoldScrollMaterial(voucher, 2)
            || !PrepareGoldScrollMaterial(
                quill,
                10,
                ArcaneQuillShopItemId,
                index: 1
            )
            || !PrepareGoldScrollMaterial(ink, requiredTurnIns)
        )
            return false;

        if (!CompleteEnrageQuest(requiredTurnIns))
            return false;

        return !Bot.ShouldExit
            && Bot.Inventory.GetQuantity(EnrageScroll) >= EnrageMaxStack;
    }

    private bool PrepareEnrageByFarming()
    {
        const string parchment = "Mystic Parchment";
        const string ink = "Zealous Ink";

        Core.Join(SpellcraftMap);

        if (Bot.ShouldExit || !MoveBankedScrollItem(EnrageScroll, ink, 1))
            return false;

        if (Bot.Inventory.GetQuantity(ink) < 1)
        {
            if (!MoveBankedScrollItem(EnrageScroll, parchment, 2))
                return false;

            if (Bot.Inventory.GetQuantity(parchment) < 2)
            {
                if (!EnsureScrollOutputSpace(EnrageScroll, parchment))
                    return false;

                Core.AddDrop(parchment);
                Core.Join("underworld");
                Core.Jump("r2", "Up");
                Bot.Kill.ForItem("*", parchment, 2, false);

                if (Bot.ShouldExit)
                    return false;

                if (Bot.Inventory.GetQuantity(parchment) < 2)
                    return ScrollPreparationFailed(
                        EnrageScroll,
                        $"2 {parchment} could not be obtained"
                    );
            }

            Core.Join(SpellcraftMap);

            if (!EnsureScrollOutputSpace(EnrageScroll, ink))
                return false;

            Core.BuyItem(SpellcraftMap, SpellInkShopId, ink, 5);

            if (Bot.ShouldExit)
                return false;

            if (Bot.Inventory.GetQuantity(ink) < 5)
                return ScrollPreparationFailed(
                    EnrageScroll,
                    $"{ink} could not be crafted"
                );
        }

        return CompleteEnrageQuest(1);
    }

    private bool PrepareGoldScrollMaterial(
        string itemName,
        int quantity,
        int shopItemID = 0,
        int index = 0
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!EnsureScrollOutputSpace(EnrageScroll, itemName))
            return false;

        Core.BuyItem(
            SpellcraftMap,
            ArcaneQuillShopId,
            itemName,
            quantity,
            shopItemID: shopItemID,
            index: index
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(itemName) < quantity)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"{itemName} could not be obtained"
            );

        return true;
    }

    private bool CompleteEnrageQuest(int amount)
    {
        if (Bot.ShouldExit)
            return false;

        if (!EnsureScrollOutputSpace(EnrageScroll, EnrageScroll))
            return false;

        int previousQuantity = Bot.Inventory.GetQuantity(EnrageScroll);
        int expectedQuantity = Math.Min(
            EnrageMaxStack,
            previousQuantity + amount * EnrageRewardQuantity
        );

        if (previousQuantity == 0)
            Core.AddDrop(EnrageScroll);

        int completed = Core.EnsureCompleteMulti(EnrageQuestId, amount);

        if (Bot.ShouldExit)
            return false;

        if (completed < amount)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"the quest completed {completed} of {amount} requested turn-ins"
            );

        if (previousQuantity == 0)
        {
            Bot.Wait.ForDrop(EnrageScroll, 10);
            Bot.Drops.Pickup(EnrageScroll);
            Bot.Wait.ForPickup(EnrageScroll, 10);
        }

        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(EnrageScroll) >= expectedQuantity,
            10
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(EnrageScroll) < expectedQuantity)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"the quest produced fewer than {expectedQuantity} total scrolls"
            );

        return true;
    }

    private void PrepareOptionalScroll(string scrollName)
    {
        int questId = string.Equals(
            scrollName,
            DecayScroll,
            StringComparison.OrdinalIgnoreCase
        )
            ? DecayQuestId
            : MystifyQuestId;
        int requiredRank = questId == DecayQuestId ? 5 : 8;
        int startingQuantity = Bot.Inventory.GetQuantity(scrollName);

        try
        {
            if (startingQuantity >= OptionalScrollThreshold)
            {
                Core.Logger($"{scrollName} already available.", "PrepareScrolls");
                return;
            }

            bool scrollWasBanked = Bot.Bank.Contains(scrollName);
            if (
                scrollWasBanked
                && !MoveBankedScrollItem(
                    scrollName,
                    scrollName,
                    OptionalScrollThreshold
                )
            )
                return;

            if (Bot.Inventory.GetQuantity(scrollName) >= OptionalScrollThreshold)
            {
                Core.Logger($"{scrollName} moved from bank.", "PrepareScrolls");
                return;
            }

            if (!Bot.Reputation.HasRank("SpellCrafting", requiredRank))
            {
                ScrollPreparationFailed(
                    scrollName,
                    $"SpellCrafting rank {requiredRank} is required"
                );
                return;
            }

            bool useGold = Bot.Player.Gold >= 1_000_000;
            if (!useGold && questId == MystifyQuestId)
            {
                ScrollPreparationFailed(
                    scrollName,
                    "the gold route requires at least 1,000,000 gold"
                );
                return;
            }

            Core.Join(SpellcraftMap);
            if (
                Bot.ShouldExit
                || !string.Equals(
                    Bot.Map.Name,
                    SpellcraftMap,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ScrollPreparationFailed(scrollName, $"{SpellcraftMap} could not be joined");
                return;
            }

            Quest? quest = Core.InitializeWithRetries(() => Core.EnsureLoad(questId));
            ItemBase? requirement = quest?.Requirements.FirstOrDefault();
            ItemBase? reward = quest?.Rewards.FirstOrDefault();

            if (
                quest == null
                || requirement == null
                || reward == null
                || requirement.Quantity <= 0
                || reward.Quantity <= 0
                || reward.MaxStack <= 0
                || !string.Equals(
                    reward.Name,
                    scrollName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ScrollPreparationFailed(
                    scrollName,
                    $"quest {questId} did not provide valid recipe data"
                );
                return;
            }

            int targetQuantity = useGold
                ? reward.MaxStack
                : OptionalScrollThreshold;
            int turnIns = (
                targetQuantity
                - Bot.Inventory.GetQuantity(scrollName)
                + reward.Quantity
                - 1
            ) / reward.Quantity;

            bool prepared = useGold
                ? PrepareOptionalScrollWithGold(
                    scrollName,
                    quest,
                    requirement,
                    reward,
                    turnIns
                )
                : PrepareDecayByFarming(
                    scrollName,
                    quest,
                    requirement,
                    reward,
                    turnIns
                );

            if (!prepared || Bot.ShouldExit)
                return;

            Core.Logger(
                $"{scrollName} prepared {(useGold ? "with gold" : "by farming")} ({startingQuantity} -> {Bot.Inventory.GetQuantity(scrollName)}).",
                "PrepareScrolls"
            );
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreLoneWolf scroll preparation failed for {scrollName}: {ex}");
            ScrollPreparationFailed(scrollName, "an unexpected error occurred");
        }
    }

    private bool PrepareOptionalScrollWithGold(
        string scrollName,
        Quest quest,
        ItemBase requirement,
        ItemBase reward,
        int turnIns
    )
    {
        const string voucher = "Gold Voucher 500k";
        const string quill = "Arcane Quill";
        int requiredInk = turnIns * requirement.Quantity;

        if (
            !MoveBankedScrollItem(scrollName, voucher, 2)
            || !MoveBankedScrollItem(scrollName, quill, 10)
            || !MoveBankedScrollItem(scrollName, requirement.Name, requiredInk)
        )
            return false;

        if (
            !PrepareOptionalGoldScrollMaterial(scrollName, voucher, 2)
            || !PrepareOptionalGoldScrollMaterial(
                scrollName,
                quill,
                10,
                ArcaneQuillShopItemId,
                index: 1
            )
            || !PrepareOptionalGoldScrollMaterial(
                scrollName,
                requirement.Name,
                requiredInk
            )
        )
            return false;

        return CompleteOptionalScrollQuest(
            scrollName,
            quest,
            reward,
            turnIns,
            reward.MaxStack
        );
    }

    private bool PrepareDecayByFarming(
        string scrollName,
        Quest quest,
        ItemBase requirement,
        ItemBase reward,
        int turnIns
    )
    {
        const string parchment = "Mystic Parchment";
        int requiredInk = turnIns * requirement.Quantity;

        if (!MoveBankedScrollItem(scrollName, requirement.Name, requiredInk))
            return false;

        if (Bot.Inventory.GetQuantity(requirement.Name) < requiredInk)
        {
            if (!MoveBankedScrollItem(scrollName, parchment, 2))
                return false;

            if (Bot.Inventory.GetQuantity(parchment) < 2)
            {
                if (!EnsureScrollOutputSpace(scrollName, parchment))
                    return false;

                Core.AddDrop(parchment);
                Core.Join("underworld");
                Core.Jump("r2", "Up");
                Bot.Kill.ForItem("*", parchment, 2, false);

                if (Bot.ShouldExit)
                    return false;

                if (Bot.Inventory.GetQuantity(parchment) < 2)
                    return ScrollPreparationFailed(
                        scrollName,
                        $"2 {parchment} could not be obtained"
                    );
            }

            Core.Join(SpellcraftMap);
            if (
                Bot.ShouldExit
                || !string.Equals(
                    Bot.Map.Name,
                    SpellcraftMap,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return ScrollPreparationFailed(
                    scrollName,
                    $"{SpellcraftMap} could not be joined"
                );

            if (!EnsureScrollOutputSpace(scrollName, requirement.Name))
                return false;

            Core.BuyItem(SpellcraftMap, SpellInkShopId, requirement.Name, 5);

            if (Bot.ShouldExit)
                return false;

            if (Bot.Inventory.GetQuantity(requirement.Name) < requiredInk)
                return ScrollPreparationFailed(
                    scrollName,
                    $"{requirement.Name} could not be crafted"
                );
        }

        return CompleteOptionalScrollQuest(
            scrollName,
            quest,
            reward,
            turnIns,
            OptionalScrollThreshold
        );
    }

    private bool PrepareOptionalGoldScrollMaterial(
        string scrollName,
        string itemName,
        int quantity,
        int shopItemID = 0,
        int index = 0
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!EnsureScrollOutputSpace(scrollName, itemName))
            return false;

        Core.BuyItem(
            SpellcraftMap,
            ArcaneQuillShopId,
            itemName,
            quantity,
            shopItemID: shopItemID,
            index: index
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(itemName) < quantity)
            return ScrollPreparationFailed(
                scrollName,
                $"{itemName} could not be obtained"
            );

        return true;
    }

    private bool CompleteOptionalScrollQuest(
        string scrollName,
        Quest quest,
        ItemBase reward,
        int amount,
        int targetQuantity
    )
    {
        if (Bot.ShouldExit || amount <= 0)
            return false;

        Core.Join(SpellcraftMap);
        if (
            Bot.ShouldExit
            || !string.Equals(
                Bot.Map.Name,
                SpellcraftMap,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return ScrollPreparationFailed(
                scrollName,
                $"{SpellcraftMap} could not be joined"
            );

        if (!EnsureScrollOutputSpace(scrollName, scrollName))
            return false;

        int previousQuantity = Bot.Inventory.GetQuantity(scrollName);
        int expectedQuantity = Math.Min(
            reward.MaxStack,
            previousQuantity + amount * reward.Quantity
        );

        if (previousQuantity == 0)
            Core.AddDrop(scrollName);

        int completed = Core.EnsureCompleteMulti(quest.ID, amount);
        if (Bot.ShouldExit)
            return false;

        if (completed < amount)
            return ScrollPreparationFailed(
                scrollName,
                $"the quest completed {completed} of {amount} requested turn-ins"
            );

        if (previousQuantity == 0)
        {
            Bot.Wait.ForDrop(scrollName, 10);
            Bot.Drops.Pickup(scrollName);
            Bot.Wait.ForPickup(scrollName, 10);
        }

        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(scrollName) >= expectedQuantity,
            10
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(scrollName) < targetQuantity)
            return ScrollPreparationFailed(
                scrollName,
                $"the quest produced fewer than {targetQuantity} total scrolls"
            );

        return true;
    }

    private bool MoveBankedScrollItem(
        string scrollName,
        string itemName,
        int quantity
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!Bot.Bank.Contains(itemName))
            return true;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
            return ScrollPreparationFailed(
                scrollName,
                $"no free inventory slot is available for {itemName}"
            );

        if (MoveBankItemToInventory(itemName))
            return true;

        if (Bot.ShouldExit)
            return false;

        return ScrollPreparationFailed(
            scrollName,
            $"{itemName} could not be moved from bank"
        );
    }

    private bool EnsureScrollOutputSpace(string scrollName, string itemName)
    {
        if (Bot.Inventory.Contains(itemName) || Core.HasSpace)
            return true;

        return ScrollPreparationFailed(
            scrollName,
            $"no free inventory slot is available for {itemName}"
        );
    }

    private bool ScrollPreparationFailed(string scrollName, string reason)
    {
        bool required = string.Equals(
            scrollName,
            EnrageScroll,
            StringComparison.OrdinalIgnoreCase
        );

        Core.Logger(
            $"{scrollName} preparation failed: {reason}.",
            "PrepareScrolls",
            messageBox: required,
            stopBot: required
        );
        return false;
    }

    public void PrepareEnhancements(
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    )
    {
        if (Bot.ShouldExit)
            return;

        bool checkEquipped = Bot.Player.Level == 100;
        bool classPrepared = checkEquipped
            && IsEquippedEnhancementPrepared(EnhancementSlot.Class, type);
        bool capePrepared = checkEquipped
            && IsEquippedEnhancementPrepared(
                EnhancementSlot.Cape,
                type,
                capeSpecial: capeSpecial
            );
        bool helmPrepared = checkEquipped
            && IsEquippedEnhancementPrepared(
                EnhancementSlot.Helm,
                type,
                helmSpecial: helmSpecial
            );
        bool weaponPrepared = checkEquipped
            && IsEquippedEnhancementPrepared(
                EnhancementSlot.Weapon,
                type,
                weaponSpecial: weaponSpecial
            );

        if (classPrepared && capePrepared && helmPrepared && weaponPrepared)
        {
            Core.Logger(
                "All enhancements already prepared.",
                "PrepareEnhancements"
            );
            return;
        }

        if (classPrepared)
            LogEnhancementResult(
                EnhancementSlot.Class,
                GetRequestedEnhancementName(
                    EnhancementSlot.Class,
                    type,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(EnhancementSlot.Class, type);

        if (Bot.ShouldExit)
            return;

        if (capePrepared)
            LogEnhancementResult(
                EnhancementSlot.Cape,
                GetRequestedEnhancementName(
                    EnhancementSlot.Cape,
                    type,
                    capeSpecial,
                    HelmSpecial.None,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Cape,
                type,
                capeSpecial: capeSpecial
            );

        if (Bot.ShouldExit)
            return;

        if (helmPrepared)
            LogEnhancementResult(
                EnhancementSlot.Helm,
                GetRequestedEnhancementName(
                    EnhancementSlot.Helm,
                    type,
                    CapeSpecial.None,
                    helmSpecial,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Helm,
                type,
                helmSpecial: helmSpecial
            );

        if (Bot.ShouldExit)
            return;

        if (weaponPrepared)
            LogEnhancementResult(
                EnhancementSlot.Weapon,
                GetRequestedEnhancementName(
                    EnhancementSlot.Weapon,
                    type,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    weaponSpecial
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Weapon,
                type,
                weaponSpecial: weaponSpecial
            );
    }

    private void PrepareEnhancementSlot(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial = CapeSpecial.None,
        HelmSpecial helmSpecial = HelmSpecial.None,
        WeaponSpecial weaponSpecial = WeaponSpecial.None
    )
    {
        if (Bot.ShouldExit)
            return;

        string requestedEnhancement = GetRequestedEnhancementName(
            slot,
            type,
            capeSpecial,
            helmSpecial,
            weaponSpecial
        );

        try
        {
            if (
                !TryGetEnhancementShop(
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    out int shopId,
                    out string enhancementName,
                    out bool forgeShop
                )
            )
            {
                Core.Logger(
                    $"{slot}: requested enhancement is invalid.",
                    "PrepareEnhancements"
                );
                return;
            }

            if (forgeShop)
            {
                Core.Join("forge");
                if (Bot.ShouldExit)
                    return;
            }

            string mapName = Bot.Map?.Name ?? "whitemap";
            ShopItem? enhancement = Core.GetShopItems(mapName, shopId)
                .Where(item =>
                    item.Category == ItemCategory.Enhancement
                    && item.Level <= Bot.Player.Level
                    && (!item.Upgrade || Bot.Player.IsMember)
                    && NormalizeEnhancementName(item.Name).Contains(enhancementName)
                )
                .OrderByDescending(item => item.Level)
                .ThenByDescending(item => item.Upgrade ? 1 : 0)
                .FirstOrDefault();

            if (enhancement == null)
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "skipped because no usable enhancement was found"
                );
                return;
            }

            InventoryItem? candidate = FindEnhancedCandidate(
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                enhancement.Level
            );

            if (candidate != null)
            {
                if (Bot.Inventory.IsEquipped(candidate.ID))
                {
                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        "already prepared"
                    );
                    return;
                }

                InventoryItem? equippedWeapon = slot == EnhancementSlot.Weapon
                    ? GetEquippedEnhancementItem(EnhancementSlot.Weapon)
                    : null;
                bool candidateIsWeaker = equippedWeapon != null
                    && Core.GetBoostFloat(candidate, "dmgAll")
                    < Core.GetBoostFloat(equippedWeapon, "dmgAll");

                if (candidateIsWeaker)
                {
                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        "will be applied to the stronger equipped weapon"
                    );
                }
                else
                {
                    Core.Equip(candidate.ID);
                    if (Bot.Inventory.IsEquipped(candidate.ID))
                    {
                        LogEnhancementResult(
                            slot,
                            requestedEnhancement,
                            "equipped from inventory"
                        );
                        return;
                    }

                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        $"could not be equipped from inventory. Using the current {GetEnhancementSlotName(slot)}"
                    );
                }
            }

            InventoryItem? equippedItem = GetEquippedEnhancementItem(slot);
            if (equippedItem == null)
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    $"skipped because no {GetEnhancementSlotName(slot)} is equipped"
                );
                return;
            }

            if (
                IsRequestedEnhancement(
                    equippedItem,
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    enhancement.Level
                )
            )
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "already prepared"
                );
                return;
            }

            if (!IsEnhancementUnlocked(slot, capeSpecial, helmSpecial, weaponSpecial))
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "skipped because it is not unlocked"
                );
                return;
            }

            int roomId = Bot.Map?.RoomID ?? 1;
            Bot.Send.Packet(
                $"%xt%zm%enhanceItemShop%{roomId}%{equippedItem.ID}%{enhancement.ID}%{shopId}%"
            );
            Core.Sleep();

            InventoryItem? updatedItem = Bot.Inventory.Items.FirstOrDefault(item =>
                item.ID == equippedItem.ID && item.Equipped
            );

            if (
                updatedItem == null
                || !IsRequestedEnhancement(
                    updatedItem,
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    enhancement.Level
                )
            )
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "was not verified after application"
                );
                return;
            }

            LogEnhancementResult(slot, requestedEnhancement, "applied");
        }
        catch (Exception ex)
        {
            Bot.Log(
                $"CoreLoneWolf enhancement preparation failed for {GetEnhancementSlotName(slot)}: {ex}"
            );
            LogEnhancementResult(slot, requestedEnhancement, "preparation failed");
        }
    }

    private bool TryGetEnhancementShop(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        out int shopId,
        out string enhancementName,
        out bool forgeShop
    )
    {
        shopId = 0;
        enhancementName = string.Empty;
        forgeShop = false;

        if (!Enum.IsDefined(typeof(EnhancementType), type))
            return false;

        switch (slot)
        {
            case EnhancementSlot.Class:
                shopId = GetNormalEnhancementShop(type);
                enhancementName = "armor";
                return shopId > 0;

            case EnhancementSlot.Cape:
                if (!Enum.IsDefined(typeof(CapeSpecial), capeSpecial))
                    return false;
                shopId = capeSpecial == CapeSpecial.None
                    ? GetNormalEnhancementShop(type)
                    : 2143;
                enhancementName = capeSpecial == CapeSpecial.None
                    ? "cape"
                    : NormalizeEnhancementName(capeSpecial.ToString());
                forgeShop = capeSpecial != CapeSpecial.None;
                return shopId > 0;

            case EnhancementSlot.Helm:
                if (!Enum.IsDefined(typeof(HelmSpecial), helmSpecial))
                    return false;
                shopId = helmSpecial == HelmSpecial.None
                    ? GetNormalEnhancementShop(type)
                    : 2164;
                enhancementName = helmSpecial == HelmSpecial.None
                    ? "helm"
                    : NormalizeEnhancementName(helmSpecial.ToString());
                forgeShop = helmSpecial != HelmSpecial.None;
                return shopId > 0;

            case EnhancementSlot.Weapon:
                if (!Enum.IsDefined(typeof(WeaponSpecial), weaponSpecial))
                    return false;

                if (weaponSpecial == WeaponSpecial.None)
                {
                    shopId = GetNormalEnhancementShop(type);
                    enhancementName = "weapon";
                }
                else if ((int)weaponSpecial >= 2 && (int)weaponSpecial <= 6)
                {
                    shopId = GetAweEnhancementShop(type);
                    enhancementName = NormalizeEnhancementName(weaponSpecial.ToString());
                }
                else
                {
                    shopId = 2142;
                    enhancementName = NormalizeEnhancementName(weaponSpecial.ToString());
                    forgeShop = true;
                }
                return shopId > 0;

            default:
                return false;
        }
    }

    private int GetNormalEnhancementShop(EnhancementType type)
    {
        bool levelFifty = Bot.Player.Level >= 50;
        return type switch
        {
            EnhancementType.Fighter => levelFifty ? 768 : 141,
            EnhancementType.Thief => levelFifty ? 767 : 142,
            EnhancementType.Hybrid => levelFifty ? 766 : 143,
            EnhancementType.Wizard => levelFifty ? 765 : 144,
            EnhancementType.Healer => levelFifty ? 762 : 145,
            EnhancementType.SpellBreaker => levelFifty ? 764 : 146,
            EnhancementType.Lucky => levelFifty ? 763 : 147,
            _ => 0,
        };
    }

    private static int GetAweEnhancementShop(EnhancementType type) =>
        type switch
        {
            EnhancementType.Fighter => 635,
            EnhancementType.Thief => 637,
            EnhancementType.Hybrid => 633,
            EnhancementType.Wizard => 636,
            EnhancementType.SpellBreaker => 636,
            EnhancementType.Healer => 638,
            EnhancementType.Lucky => 639,
            _ => 0,
        };

    private InventoryItem? FindEnhancedCandidate(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        int enhancementLevel
    )
    {
        if (slot == EnhancementSlot.Class)
            return null;

        IEnumerable<InventoryItem> candidates = Bot.Inventory.Items.Where(item =>
            (!item.Upgrade || Bot.Player.IsMember)
            && IsItemInEnhancementSlot(item, slot)
            && IsRequestedEnhancement(
                item,
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                enhancementLevel
            )
        );

        return slot == EnhancementSlot.Weapon
            ? candidates
                .OrderByDescending(item => Core.GetBoostFloat(item, "dmgAll"))
                .FirstOrDefault()
            : candidates.FirstOrDefault();
    }

    private InventoryItem? GetEquippedEnhancementItem(EnhancementSlot slot) =>
        Bot.Inventory.Items.FirstOrDefault(item =>
            item.Equipped && IsItemInEnhancementSlot(item, slot)
        );

    private bool IsEquippedEnhancementPrepared(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial = CapeSpecial.None,
        HelmSpecial helmSpecial = HelmSpecial.None,
        WeaponSpecial weaponSpecial = WeaponSpecial.None
    )
    {
        InventoryItem? item = GetEquippedEnhancementItem(slot);
        return item != null
            && IsRequestedEnhancement(
                item,
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                100
            );
    }

    private static bool IsItemInEnhancementSlot(
        InventoryItem item,
        EnhancementSlot slot
    ) =>
        slot switch
        {
            EnhancementSlot.Class => item.Category == ItemCategory.Class,
            EnhancementSlot.Cape => item.Category == ItemCategory.Cape,
            EnhancementSlot.Helm => item.Category == ItemCategory.Helm,
            EnhancementSlot.Weapon => string.Equals(
                item.ItemGroup,
                "Weapon",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };

    private static bool IsRequestedEnhancement(
        InventoryItem item,
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        int enhancementLevel
    )
    {
        if (item.EnhancementLevel != enhancementLevel)
            return false;

        return slot switch
        {
            EnhancementSlot.Class => item.EnhancementPatternID == (int)type,
            EnhancementSlot.Cape =>
                item.EnhancementPatternID
                == (capeSpecial == CapeSpecial.None
                    ? (int)type
                    : (int)capeSpecial),
            EnhancementSlot.Helm =>
                item.EnhancementPatternID
                == (helmSpecial == HelmSpecial.None
                    ? (int)type
                    : (int)helmSpecial),
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None =>
                item.EnhancementPatternID == (int)type,
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.Forge =>
                item.ProcID == 0
                && (item.EnhancementPatternID == (int)type
                    || item.EnhancementPatternID == 10),
            EnhancementSlot.Weapon when (int)weaponSpecial <= 6 =>
                item.EnhancementPatternID == (int)type
                && item.ProcID == (int)weaponSpecial,
            EnhancementSlot.Weapon => item.ProcID == (int)weaponSpecial,
            _ => false,
        };
    }

    private bool IsEnhancementUnlocked(
        EnhancementSlot slot,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    )
    {
        int questId = slot switch
        {
            EnhancementSlot.Cape => capeSpecial switch
            {
                CapeSpecial.None => 0,
                CapeSpecial.Forge => 8758,
                CapeSpecial.Absolution => 8743,
                CapeSpecial.Avarice => 8745,
                CapeSpecial.Vainglory => 8744,
                CapeSpecial.Penitence => 8822,
                CapeSpecial.Lament => 8823,
                _ => -1,
            },
            EnhancementSlot.Helm => helmSpecial switch
            {
                HelmSpecial.None => 0,
                HelmSpecial.Forge => 8828,
                HelmSpecial.Vim => 8824,
                HelmSpecial.Examen => 8825,
                HelmSpecial.Anima => 8826,
                HelmSpecial.Pneuma => 8827,
                HelmSpecial.Hearty => 9466,
                _ => -1,
            },
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None => 0,
            EnhancementSlot.Weapon when (int)weaponSpecial >= 2 && (int)weaponSpecial <= 6 => 2937,
            EnhancementSlot.Weapon => weaponSpecial switch
            {
                WeaponSpecial.Forge => 8738,
                WeaponSpecial.Lacerate => 8739,
                WeaponSpecial.Smite => 8740,
                WeaponSpecial.Valiance => 8741,
                WeaponSpecial.Arcanas_Concerto => 8742,
                WeaponSpecial.Acheron => 8820,
                WeaponSpecial.Elysium => 8821,
                WeaponSpecial.Praxis => 9171,
                WeaponSpecial.Dauntless => 9172,
                WeaponSpecial.Ravenous => 9560,
                _ => -1,
            },
            _ => 0,
        };

        if (questId < 0)
            return false;
        if (questId == 0)
            return true;
        if (!Core.isCompletedBefore(questId))
            return false;

        return slot != EnhancementSlot.Helm
            || helmSpecial != HelmSpecial.Hearty
            || Bot.Reputation.HasRank("Grimskull Trolling", 7);
    }

    private static string NormalizeEnhancementName(string name) =>
        name.Replace(" ", string.Empty)
            .Replace("'", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();

    private static string GetEnhancementSlotName(EnhancementSlot slot) =>
        slot.ToString().ToLowerInvariant();

    private static string GetRequestedEnhancementName(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    )
    {
        string name = slot switch
        {
            EnhancementSlot.Class => type.ToString(),
            EnhancementSlot.Cape when capeSpecial == CapeSpecial.None =>
                type.ToString(),
            EnhancementSlot.Cape => capeSpecial.ToString(),
            EnhancementSlot.Helm when helmSpecial == HelmSpecial.None =>
                type.ToString(),
            EnhancementSlot.Helm => helmSpecial.ToString(),
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None =>
                type.ToString(),
            EnhancementSlot.Weapon
                when (int)weaponSpecial >= 2 && (int)weaponSpecial <= 6 =>
                $"{type} {weaponSpecial}",
            EnhancementSlot.Weapon => weaponSpecial.ToString(),
            _ => type.ToString(),
        };

        return name.Replace("_", " ");
    }

    private void LogEnhancementResult(
        EnhancementSlot slot,
        string requestedEnhancement,
        string result
    ) =>
        Core.Logger(
            $"{slot}: {requestedEnhancement} {result}.",
            "PrepareEnhancements"
        );

    public bool ValidatePrivateRoomNumber(int roomNumber)
    {
        if (roomNumber >= 1001 && roomNumber <= 99999)
            return true;

        Core.Logger(
            $"Private room number {roomNumber} is invalid. Use 1001 through 99999.",
            "ValidatePrivateRoomNumber",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    public bool AcceptUltraQuest(int questID)
    {
        if (questID <= 0)
        {
            Core.Logger(
                $"Warning: Ultra quest ID {questID} is invalid. Continuing.",
                "AcceptUltraQuest"
            );
            return false;
        }

        if (Bot.Quests.IsInProgress(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already accepted.",
                "AcceptUltraQuest"
            );
            return true;
        }

        if (Bot.Quests.IsDailyComplete(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already completed for the current reset.",
                "AcceptUltraQuest"
            );
            return false;
        }

        if (!Bot.Quests.IsAvailable(questID))
        {
            Core.Logger(
                $"Warning: Ultra quest {questID} is unavailable. Continuing.",
                "AcceptUltraQuest"
            );
            return false;
        }

        Core.EnsureAccept(questID);

        if (Bot.Quests.IsInProgress(questID))
        {
            Core.Logger($"Ultra quest {questID} accepted.", "AcceptUltraQuest");
            return true;
        }

        Core.Logger(
            $"Warning: Ultra quest {questID} could not be accepted. Continuing.",
            "AcceptUltraQuest"
        );
        return false;
    }

    public bool CompleteUltraQuest(int questID)
    {
        if (questID <= 0)
        {
            Core.Logger(
                $"Warning: Ultra quest ID {questID} is invalid. Continuing.",
                "CompleteUltraQuest"
            );
            return false;
        }

        if (Bot.Quests.IsDailyComplete(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already completed for the current reset.",
                "CompleteUltraQuest"
            );
            return true;
        }

        if (!Bot.Quests.IsInProgress(questID))
        {
            Core.Logger(
                $"Warning: Ultra quest {questID} is not accepted. Completion skipped.",
                "CompleteUltraQuest"
            );
            return false;
        }

        Core.EnsureComplete(questID);

        if (
            Bot.Quests.IsDailyComplete(questID)
            && !Bot.Quests.IsInProgress(questID)
        )
        {
            Core.Logger($"Ultra quest {questID} completed.", "CompleteUltraQuest");
            return true;
        }

        Core.Logger(
            $"Warning: Ultra quest {questID} could not be completed. Continuing.",
            "CompleteUltraQuest"
        );
        return false;
    }

    public bool ValidateUltraAccess(
        int ultraQuestID,
        int prerequisiteQuestID,
        string prerequisiteQuestName,
        int minimumLevel,
        string ultraName
    )
    {
        if (
            Bot.Player.Level < minimumLevel
            && !SendArmySignal("ULTRA_LEVEL_INVALID")
        )
            return false;

        if (!SyncArmy("ULTRA_LEVEL_CHECK"))
            return false;

        List<string> playersBelowLevel = new();
        for (int playerNumber = 1; playerNumber <= armyPlayers.Length; playerNumber++)
        {
            if (HasArmySignal("ULTRA_LEVEL_INVALID", playerNumber))
                playersBelowLevel.Add($"Player {playerNumber}");
        }

        if (playersBelowLevel.Count > 0)
        {
            Core.Logger(
                $"{ultraName} requires every player to be at least level {minimumLevel}. Players below the requirement: {string.Join(", ", playersBelowLevel)}.",
                "ValidateUltraAccess",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (
            prerequisiteQuestID > 0
            && (
                !Bot.Quests.IsUnlocked(ultraQuestID)
                || !Bot.Quests.HasBeenCompleted(prerequisiteQuestID)
            )
        )
        {
            Core.Logger(
                $"{ultraName} requires {prerequisiteQuestName} to be completed. The questline will be updated so this account can continue.",
                "ValidateUltraAccess",
                messageBox: true
            );
            Bot.Quests.UpdateQuest(prerequisiteQuestID);
        }

        return !Bot.ShouldExit;
    }

    public bool StartArmySync(
        string syncFileName,
        int playerCount,
        string? optionCategory = null
    )
    {
        ResetArmyState();

        if (
            string.IsNullOrWhiteSpace(syncFileName)
            || !string.Equals(
                Path.GetFileName(syncFileName),
                syncFileName,
                StringComparison.Ordinal
            )
            || !string.Equals(
                Path.GetExtension(syncFileName),
                ".sync",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return ArmyInitializationFailure("Sync file must be a filename ending in .sync.");

        if (playerCount < 2 || playerCount > 7)
            return ArmyInitializationFailure("Army player count must be from 2 through 7.");

        if (Bot.Config == null)
            return ArmyInitializationFailure("Army player configuration is unavailable.");

        string[] players = new string[playerCount];
        for (int index = 0; index < playerCount; index++)
        {
            string optionName = $"player{index + 1}";
            players[index] = NormalizeArmyUsername(
                string.IsNullOrEmpty(optionCategory)
                    ? Bot.Config.Get<string>(optionName)
                    : Bot.Config.Get<string>(optionCategory, optionName)
            );
        }

        if (players.Any(string.IsNullOrEmpty))
            return ArmyInitializationFailure("Every requested Army player name is required.");

        if (players.Distinct(StringComparer.OrdinalIgnoreCase).Count() != players.Length)
            return ArmyInitializationFailure("Army player names must be unique.");

        string username = NormalizeArmyUsername(Core.Username());
        int playerIndex = Array.FindIndex(
            players,
            player => string.Equals(player, username, StringComparison.OrdinalIgnoreCase)
        );
        if (playerIndex < 0)
            return ArmyInitializationFailure("Current account is not in the Army roster.");

        armyPlayers = players;
        armyUsername = username;
        armyPlayerIndex = playerIndex;
        armyLaunchToken = Guid.NewGuid().ToString("N");
        armySessionId = armyPlayerIndex == 0 ? Guid.NewGuid().ToString("N") : string.Empty;
        armySyncPath = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
        armyInitialized = true;

        bool started = armyPlayerIndex == 0
            ? StartLeaderArmySession()
            : JoinFollowerArmySession();
        armySessionStarted = started;
        return started;
    }

    public bool SyncArmy(string step)
    {
        if (!RequireArmySession())
            return false;

        if (!CanContinueArmySync(ReadArmyLines()))
            return false;

        string stepName = NormalizeArmyRecordName(step);
        if (string.IsNullOrEmpty(stepName))
        {
            Core.Logger("Army sync step name is invalid.", "CoreLoneWolf");
            return false;
        }

        return armyPlayerIndex == 0
            ? RunLeaderArmySync(stepName)
            : RunFollowerArmySync(stepName);
    }

    public bool SendArmySignal(string signal)
    {
        if (!RequireArmySession())
            return false;

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army signal name is invalid.", "CoreLoneWolf");
            return false;
        }

        return AppendArmyRecord(
            new[]
            {
                "SIGNAL",
                ArmyProtocolVersion,
                armySessionId,
                signalName,
                armyUsername,
            }
        );
    }

    public bool HasArmySignal(string signal, int senderPlayerNumber)
    {
        if (!RequireArmySession())
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army signal name is invalid.", "CoreLoneWolf");
            return false;
        }

        if (senderPlayerNumber < 1 || senderPlayerNumber > armyPlayers.Length)
        {
            Core.Logger("Army signal sender player number is invalid.", "CoreLoneWolf");
            return false;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string expectedSender = armyPlayers[senderPlayerNumber - 1];
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 5
                && parts[0] == "SIGNAL"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && IsArmyRecordName(parts[3])
                && string.Equals(parts[3], signalName, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    expectedSender,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        return false;
    }

    public bool ShouldResetFight(int fightAttempt)
    {
        if (!RequireArmySession())
            return false;

        if (fightAttempt <= 0)
        {
            Core.Logger("Fight attempt must be greater than zero.", "CoreLoneWolf");
            return false;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string deadSignal = $"FIGHT_DEAD_{fightAttempt}";
        string aliveSignal = $"FIGHT_ALIVE_{fightAttempt}";
        string resetSignal = $"FIGHT_RESET_{fightAttempt}";
        bool playerAlive = Bot.Player.Alive;

        if (reportedFightAttempt != fightAttempt)
        {
            reportedFightAttempt = fightAttempt;
            reportedFightAlive = true;
        }

        if (reportedFightAlive != playerAlive)
        {
            string stateSignal = playerAlive ? aliveSignal : deadSignal;
            if (
                !AppendArmyRecord(
                    new[]
                    {
                        "SIGNAL",
                        ArmyProtocolVersion,
                        armySessionId,
                        stateSignal,
                        armyUsername,
                    }
                )
            )
                return false;

            reportedFightAlive = playerAlive;
        }

        string playerOne = armyPlayers[0];
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 5
                && parts[0] == "SIGNAL"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && string.Equals(parts[3], resetSignal, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    playerOne,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        Dictionary<string, bool> latestAlive = new(StringComparer.OrdinalIgnoreCase);
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length != 5
                || parts[0] != "SIGNAL"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || (
                    !string.Equals(parts[3], deadSignal, StringComparison.Ordinal)
                    && !string.Equals(parts[3], aliveSignal, StringComparison.Ordinal)
                )
            )
                continue;

            string sender = NormalizeArmyUsername(parts[4]);
            if (
                !armyPlayers.Contains(sender, StringComparer.OrdinalIgnoreCase)
                || latestAlive.ContainsKey(sender)
            )
                continue;

            latestAlive[sender] = string.Equals(
                parts[3],
                aliveSignal,
                StringComparison.Ordinal
            );
        }

        latestAlive[armyUsername] = playerAlive;
        if (latestAlive.Count(state => !state.Value) < 2 || armyPlayerIndex != 0)
            return false;

        return AppendArmyRecord(
            new[]
            {
                "SIGNAL",
                ArmyProtocolVersion,
                armySessionId,
                resetSignal,
                armyUsername,
            }
        );
    }

    public bool SendArmyTimestamp(string signal, long unixMilliseconds)
    {
        if (!RequireArmySession())
            return false;

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army timestamp name is invalid.", "CoreLoneWolf");
            return false;
        }

        if (unixMilliseconds <= 0)
        {
            Core.Logger("Army timestamp value is invalid.", "CoreLoneWolf");
            return false;
        }

        return AppendArmyRecord(
            new[]
            {
                "TIMESTAMP",
                ArmyProtocolVersion,
                armySessionId,
                signalName,
                armyUsername,
                unixMilliseconds.ToString(),
            }
        );
    }

    public long GetArmyTimestamp(string signal, int senderPlayerNumber)
    {
        if (!RequireArmySession())
            return 0;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army timestamp name is invalid.", "CoreLoneWolf");
            return 0;
        }

        if (senderPlayerNumber < 1 || senderPlayerNumber > armyPlayers.Length)
        {
            Core.Logger("Army timestamp sender player number is invalid.", "CoreLoneWolf");
            return 0;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return 0;

        string expectedSender = armyPlayers[senderPlayerNumber - 1];
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length == 6
                && parts[0] == "TIMESTAMP"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && IsArmyRecordName(parts[3])
                && string.Equals(parts[3], signalName, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    expectedSender,
                    StringComparison.OrdinalIgnoreCase
                )
                && long.TryParse(parts[5], out long unixMilliseconds)
                && unixMilliseconds > 0
            )
                return unixMilliseconds;
        }

        return 0;
    }

    public bool IsArmyPlayer(int playerNumber) =>
        armyInitialized
        && playerNumber >= 1
        && playerNumber <= armyPlayers.Length
        && armyPlayerIndex == playerNumber - 1;

    public bool StopArmySync(string reason = "")
    {
        if (!RequireArmySession())
            return false;

        if (!CanContinueArmySync(ReadArmyLines()))
            return false;

        if (armyPlayerIndex != 0)
        {
            Core.Logger("Only playerOne may stop Army sync.", "CoreLoneWolf");
            return false;
        }

        if (!IsSafeArmyField(reason))
        {
            Core.Logger("Army stop reason contains invalid data.", "CoreLoneWolf");
            return false;
        }

        return AppendArmyRecord(
            new[] { "STOP", ArmyProtocolVersion, armySessionId, reason }
        );
    }

    private bool StartLeaderArmySession()
    {
        if (!ClearArmyFile())
            return false;

        if (
            !AppendArmyRecord(new[] { "RESET", ArmyProtocolVersion, armySessionId })
            || !WriteArmyReady(armySessionId)
        )
            return false;

        Dictionary<string, string>? launchTokens = WaitForArmyReady(armySessionId);
        if (launchTokens == null)
            return false;

        List<string> start = new() { "START", ArmyProtocolVersion, armySessionId };
        start.AddRange(armyPlayers.Select(player => launchTokens[player]));
        return AppendArmyRecord(start);
    }

    private bool JoinFollowerArmySession()
    {
        Core.Logger("Waiting for playerOne to start Army sync.", "CoreLoneWolf");
        string readySession = string.Empty;

        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            string? sessionId = FindLatestArmyReset(lines);
            if (sessionId == null)
            {
                Bot.Sleep(ArmyPollDelay);
                continue;
            }

            if (!string.Equals(readySession, sessionId, StringComparison.Ordinal))
            {
                if (!WriteArmyReady(sessionId))
                    return false;

                readySession = sessionId;
            }

            if (HasMatchingArmyStart(lines, sessionId))
            {
                if (HasArmyStop(lines, sessionId))
                    return false;

                armySessionId = sessionId;
                return true;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return false;
    }

    private bool WriteArmyReady(string sessionId) =>
        AppendArmyRecord(
            new[] { "READY", ArmyProtocolVersion, sessionId, armyUsername, armyLaunchToken }
        );

    private Dictionary<string, string>? WaitForArmyReady(string sessionId)
    {
        string previousMissing = string.Empty;
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in ReadArmyLines())
            {
                string[] parts = line.Split('|');
                if (
                    parts.Length != 5
                    || parts[0] != "READY"
                    || parts[1] != ArmyProtocolVersion
                    || !string.Equals(parts[2], sessionId, StringComparison.Ordinal)
                )
                    continue;

                string username = NormalizeArmyUsername(parts[3]);
                if (
                    armyPlayers.Contains(username, StringComparer.OrdinalIgnoreCase)
                    && Guid.TryParseExact(parts[4], "N", out _)
                )
                    tokens[username] = parts[4];
            }

            if (armyPlayers.All(tokens.ContainsKey))
                return tokens;

            string missing = LogArmyNames(
                armyPlayers.Where(player => !tokens.ContainsKey(player))
            );
            if (!string.Equals(missing, previousMissing, StringComparison.Ordinal))
            {
                Core.Logger($"Waiting for Army players: {missing}.", "CoreLoneWolf");
                previousMissing = missing;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return null;
    }

    private bool HasMatchingArmyStart(string[] lines, string sessionId)
    {
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length != 3 + armyPlayers.Length
                || parts[0] != "START"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], sessionId, StringComparison.Ordinal)
            )
                continue;

            string[] tokens = parts.Skip(3).ToArray();
            return tokens.All(token => Guid.TryParseExact(token, "N", out _))
                && string.Equals(
                    tokens[armyPlayerIndex],
                    armyLaunchToken,
                    StringComparison.Ordinal
                );
        }

        return false;
    }

    private bool RunLeaderArmySync(string stepName)
    {
        long stepId = nextArmyStepId;
        if (
            !AppendArmyRecord(
                new[]
                {
                    "STEP",
                    ArmyProtocolVersion,
                    armySessionId,
                    stepId.ToString(),
                    stepName,
                }
            )
        )
            return false;

        nextArmyStepId++;
        if (!WriteArmyArrived(stepId))
            return false;

        string previousMissing = string.Empty;
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            if (!CanContinueArmySync(lines))
                return false;

            HashSet<string> arrived = GetArmyArrivals(lines, stepId);
            if (armyPlayers.All(arrived.Contains))
            {
                if (
                    !AppendArmyRecord(
                        new[]
                        {
                            "CONTINUE",
                            ArmyProtocolVersion,
                            armySessionId,
                            stepId.ToString(),
                        }
                    )
                )
                    return false;

                lastArmyStepId = stepId;
                return true;
            }

            string missing = LogArmyNames(
                armyPlayers.Where(player => !arrived.Contains(player))
            );
            if (!string.Equals(missing, previousMissing, StringComparison.Ordinal))
            {
                Core.Logger($"Waiting for Army players: {missing}.", "CoreLoneWolf");
                previousMissing = missing;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return false;
    }

    private bool RunFollowerArmySync(string expectedStepName)
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            if (!CanContinueArmySync(lines))
                return false;

            if (!TryFindNextArmyStep(lines, out long stepId, out string stepName))
            {
                Bot.Sleep(ArmyPollDelay);
                continue;
            }

            if (!string.Equals(stepName, expectedStepName, StringComparison.Ordinal))
            {
                Core.Logger("Army sync step does not match playerOne.", "CoreLoneWolf");
                return false;
            }

            if (!WriteArmyArrived(stepId))
                return false;

            while (!Bot.ShouldExit && !armyTransportFailed)
            {
                string[] continueLines = ReadArmyLines();
                if (!CanContinueArmySync(continueLines))
                    return false;

                if (HasArmyContinue(continueLines, stepId))
                {
                    lastArmyStepId = stepId;
                    return true;
                }

                Bot.Sleep(ArmyPollDelay);
            }
        }

        return false;
    }

    private bool WriteArmyArrived(long stepId) =>
        AppendArmyRecord(
            new[]
            {
                "ARRIVED",
                ArmyProtocolVersion,
                armySessionId,
                stepId.ToString(),
                armyUsername,
            }
        );

    private HashSet<string> GetArmyArrivals(string[] lines, long stepId)
    {
        HashSet<string> arrived = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length != 5
                || parts[0] != "ARRIVED"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || !long.TryParse(parts[3], out long recordStepId)
                || recordStepId != stepId
            )
                continue;

            string username = NormalizeArmyUsername(parts[4]);
            if (armyPlayers.Contains(username, StringComparer.OrdinalIgnoreCase))
                arrived.Add(username);
        }

        return arrived;
    }

    private bool TryFindNextArmyStep(
        string[] lines,
        out long nextStepId,
        out string nextStepName
    )
    {
        nextStepId = long.MaxValue;
        nextStepName = string.Empty;

        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length != 5
                || parts[0] != "STEP"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || !long.TryParse(parts[3], out long stepId)
                || stepId <= lastArmyStepId
                || stepId >= nextStepId
                || !IsArmyRecordName(parts[4])
            )
                continue;

            nextStepId = stepId;
            nextStepName = parts[4];
        }

        if (nextStepId != long.MaxValue)
            return true;

        nextStepId = 0;
        return false;
    }

    private bool HasArmyContinue(string[] lines, long stepId)
    {
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 4
                && parts[0] == "CONTINUE"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && long.TryParse(parts[3], out long recordStepId)
                && recordStepId == stepId
            )
                return true;
        }

        return false;
    }

    private bool CanContinueArmySync(string[] lines)
    {
        string? latestSessionId = FindLatestArmyReset(lines);
        if (
            latestSessionId != null
            && !string.Equals(latestSessionId, armySessionId, StringComparison.Ordinal)
        )
        {
            if (!armySessionFailureLogged)
            {
                Core.Logger(
                    "Army sync session was replaced.",
                    "CoreLoneWolf",
                    messageBox: true,
                    stopBot: true
                );
                armySessionFailureLogged = true;
            }

            return false;
        }

        if (HasArmyStop(lines, armySessionId))
        {
            if (!armyStopLogged)
            {
                Core.Logger("Army sync was stopped by playerOne.", "CoreLoneWolf");
                armyStopLogged = true;
            }

            return false;
        }

        return true;
    }

    private static string? FindLatestArmyReset(string[] lines)
    {
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length == 3
                && parts[0] == "RESET"
                && parts[1] == ArmyProtocolVersion
                && Guid.TryParseExact(parts[2], "N", out _)
            )
                return parts[2];
        }

        return null;
    }

    private static bool HasArmyStop(string[] lines, string sessionId)
    {
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 4
                && parts[0] == "STOP"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], sessionId, StringComparison.Ordinal)
                && IsSafeArmyField(parts[3])
            )
                return true;
        }

        return false;
    }

    private bool AppendArmyRecord(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0 || fields.Any(field => !IsSafeArmyField(field)))
        {
            Core.Logger("Army sync record contains invalid data.", "CoreLoneWolf");
            return false;
        }

        string line = string.Join("|", fields);
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                using FileStream stream = new(
                    armySyncPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read
                );
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
                LogArmyRecord(fields);
                return true;
            }
            catch (IOException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return false;
    }

    private string[] ReadArmyLines()
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                return File.Exists(armySyncPath)
                    ? File.ReadAllLines(armySyncPath)
                    : Array.Empty<string>();
            }
            catch (IOException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return Array.Empty<string>();
    }

    private bool ClearArmyFile()
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                using FileStream stream = new(
                    armySyncPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read
                );
                return true;
            }
            catch (IOException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return false;
    }

    private void LogArmyRecord(IReadOnlyList<string> fields)
    {
        switch (fields[0])
        {
            case "RESET":
                Core.Logger("Army sync reset.", "CoreLoneWolf");
                break;
            case "START":
                Core.Logger("Army sync started.", "CoreLoneWolf");
                break;
            case "STEP" when fields.Count > 4:
                Core.Logger($"Army sync step {fields[4]}.", "CoreLoneWolf");
                break;
            case "CONTINUE":
                Core.Logger("Army sync continuing.", "CoreLoneWolf");
                break;
            case "STOP":
                Core.Logger("Army sync stop sent.", "CoreLoneWolf");
                break;
        }
    }

    private void SetArmyTransportFailure(Exception exception)
    {
        armyTransportFailed = true;
        Bot.Log($"CoreLoneWolf Army sync file access failed: {exception}");
        if (!armyTransportFailureLogged)
        {
            Core.Logger(
                "Army sync file access failed.",
                "CoreLoneWolf",
                messageBox: true,
                stopBot: true
            );
            armyTransportFailureLogged = true;
        }
    }

    private bool RequireArmySession()
    {
        if (!armyInitialized)
        {
            Core.Logger(
                "Army sync has not been initialized.",
                "CoreLoneWolf",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (armySessionStarted && !string.IsNullOrEmpty(armySessionId))
            return true;

        Core.Logger(
            "Army sync session has not started.",
            "CoreLoneWolf",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool ArmyInitializationFailure(string message)
    {
        Core.Logger(
            message,
            "CoreLoneWolf",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private string LogArmyNames(IEnumerable<string> usernames) =>
        string.Join(", ", usernames.Select(LogArmyName));

    private string LogArmyName(string username)
    {
        string normalized = NormalizeArmyUsername(username);
        int index = Array.FindIndex(
            armyPlayers,
            player => string.Equals(player, normalized, StringComparison.OrdinalIgnoreCase)
        );
        return index >= 0 && index < ArmyAliases.Length ? ArmyAliases[index] : "unknownPlayer";
    }

    private void ResetArmyState()
    {
        armyPlayers = Array.Empty<string>();
        armyUsername = string.Empty;
        armySyncPath = string.Empty;
        armyLaunchToken = string.Empty;
        armySessionId = string.Empty;
        armyPlayerIndex = -1;
        nextArmyStepId = 1;
        lastArmyStepId = 0;
        armyInitialized = false;
        armySessionStarted = false;
        armySessionFailureLogged = false;
        armyStopLogged = false;
        armyTransportFailed = false;
        armyTransportFailureLogged = false;
        reportedFightAttempt = 0;
        reportedFightAlive = null;
    }

    private static string NormalizeArmyUsername(string? username) =>
        username?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeArmyRecordName(string? name)
    {
        string normalized = name?.Trim().ToUpperInvariant() ?? string.Empty;
        return IsArmyRecordName(normalized) ? normalized : string.Empty;
    }

    private static bool IsArmyRecordName(string name) =>
        name.Length > 0
        && name.All(character =>
            character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character == '_'
        );

    private static bool IsSafeArmyField(string? value) =>
        value != null
        && !value.Contains('|')
        && !value.Contains('\r')
        && !value.Contains('\n')
        && !value.Contains('\0');
}

public enum UltraRunResult
{
    Completed,
    AttemptsExhausted,
    Failed,
}

public enum SkillEngineMode
{
    Simple,
    Strict,
    KingsEcho,
    ArcanaInvoker,
    Shaman,
    LightCasterHealing,
    VoidHighlord,
    ChronoShadowHunterStable,
    ChronoShadowHunterGunslinger,
}

public class ClassPreset
{
    public string ClassName { get; set; } = string.Empty;
    public string[] AlternateClassNames { get; set; } = Array.Empty<string>();
    public int[] Skills { get; set; } = Array.Empty<int>();
    public SkillEngineMode SkillMode { get; set; } = SkillEngineMode.Simple;
    public EnhancementType BaseEnhancement { get; set; } = EnhancementType.Lucky;
    public CapeSpecial CapeEnhancement { get; set; } = CapeSpecial.None;
    public HelmSpecial HelmEnhancement { get; set; } = HelmSpecial.None;
    public WeaponSpecial WeaponEnhancement { get; set; } = WeaponSpecial.None;
    public string Tonic { get; set; } = string.Empty;
    public string Elixir { get; set; } = string.Empty;
    public string? CombatPotion { get; set; }
}
