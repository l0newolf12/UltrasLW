/*
name: Astral Empyrean LW
description: Seven-player CoreLoneWolf Army script for Astral Empyrean.
tags: ultra, astral empyrean, seven-player, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class AstralEmpyrean_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Fast,
    }

    private enum FightResult
    {
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Astral Empyrean LW";
    private const string SyncFileName = "AstralEmpyrean_LW.sync";
    private const string MapName = "astralshrine";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string StarfirePacketText = "starfire";
    private const string BossPacketText = "\"cInf\":\"m:1\"";
    private const string StardustBreathPacketText = "\"animStr\":\"Attack2\"";
    private const int UltraQuestId = 9803;
    private const int PrerequisiteQuestId = 9802;
    private const string PrerequisiteQuestName = "Hoshiyoru";
    private const int MinimumLevel = 95;
    private const int BossMapId = 1;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DeathResetThreshold = 3;
    private const int ZoneBTargetX = 240;
    private const int ZoneBTargetY = 200;
    private const int ZoneATargetX = 600;
    private const int ZoneATargetY = 430;

    private readonly Queue<string> pendingZones = new();
    private readonly object zoneLock = new();

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private bool isTaunter;
    private bool isTimedHealer;
    private int privateRoomNumber;

    private bool UsesStableRoleLayout => armyComposition == ArmyComposition.Stable;

    public string OptionsStorage = "AstralEmpyrean_LW";
    public bool DontPreconfigure = true;
    public static Option<string> player6 = new(
        "player6",
        "Player 6 (Optional)",
        "Player 6 (Optional) account name.",
        string.Empty
    );
    public static Option<string> player7 = new(
        "player7",
        "Player 7 (Optional)",
        "Player 7 (Optional) account name.",
        string.Empty
    );
    public List<IOption> Options = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        LoneWolf.player5,
        player6,
        player7,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: LR / SC / AP / LOO / VDK / Bard / Shaman\nStable: KE / SC / AP / LOO / VDK / Bard / AF\nFast: LR / SC / AP / LOO / VDK / Bard / AI",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<bool>(
            "UsePotions",
            "Use Potions",
            "Prepare and use the assigned potion loadout.",
            true
        ),
        new Option<bool>(
            "UseEnhancements",
            "Use Enhancements",
            "Prepare the assigned enhancement loadout.",
            true
        ),
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Config?.Configure();

        try
        {
            Run();
        }
        finally
        {
            LoneWolf.StopPacketDetector();
            StopZoneListener();
            LoneWolf.StopSkillEngine();
        }
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        if (armyPlayerCount < 6 && LoneWolf.IsArmyPlayer(1))
        {
            Core.Logger(
                "Bard is not configured. Astral Empyrean may fail without it.",
                "Run",
                messageBox: true
            );
        }

        ClassPreset preset = GetClassPreset();
        if (
            !LoneWolf.ValidateUltraAccess(
                UltraQuestId,
                PrerequisiteQuestId,
                PrerequisiteQuestName,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        playerAlias = GetPlayerAlias();
        isTaunter = UsesStableRoleLayout
            ? LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(5)
            : LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(5);
        isTimedHealer = LoneWolf.IsArmyPlayer(4);

        ApplyAstralOverrides(preset);
        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        LoneWolf.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Core.Jump(SafeCell, SafePad);
        LoneWolf.CompleteUltraQuest(UltraQuestId);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");

        string playerSix = Bot.Config.Get<string>("player6")?.Trim() ?? string.Empty;
        string playerSeven = Bot.Config.Get<string>("player7")?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(playerSix) && !string.IsNullOrEmpty(playerSeven))
        {
            Core.Logger(
                "Player 6 is required when Player 7 is configured.",
                "ValidateOptions",
                messageBox: true
            );
            return false;
        }

        armyPlayerCount = !string.IsNullOrEmpty(playerSeven)
            ? 7
            : !string.IsNullOrEmpty(playerSix)
                ? 6
                : 5;

        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (Bot.Config!.Get<bool>("UseEnhancements"))
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );

        if (Bot.Config.Get<bool>("UsePotions"))
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            LoneWolf.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        WarnIfVaingloryCapeIsEquipped();
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (Bot.Config!.Get<bool>("UsePotions"))
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (
            int fightAttempt = 1;
            fightAttempt <= MaxFightAttempts && !Bot.ShouldExit;
            fightAttempt++
        )
        {
            Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            StartZoneListener();
            if (!StartFightPacketDetector())
            {
                Core.Logger(
                    "The Astral fight packet detector could not be started.",
                    "RunFightAttempts",
                    messageBox: true,
                    stopBot: true
                );
                StopZoneListener();
                return false;
            }

            FightResult result;
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                result = Fight(preset, fightAttempt);
            }
            finally
            {
                LoneWolf.StopPacketDetector();
                StopZoneListener();
            }

            if (result == FightResult.Defeated)
                return true;

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                StopArmyAfterFailedAttempts();
                return false;
            }
        }

        return false;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            useSurvivalSkill: !UsesStableRoleLayout || !LoneWolf.IsArmyPlayer(1),
            maintainedPotion: !isTaunter && Bot.Config!.Get<bool>("UsePotions")
                ? preset.CombatPotion
                : null
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        bool bossObservedAlive = false;
        int nextStarfireCycle = 1;
        int nextStardustBreath = 1;
        DateTime openingTauntAt = DateTime.UtcNow.AddMilliseconds(1500);
        bool openingTauntRequested = !LoneWolf.IsArmyPlayer(5);

        while (!Bot.ShouldExit)
        {
            DrainZoneEvents(move: Bot.Player.Alive);

            if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
            {
                StopFightCombat();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    DrainZoneEvents(move: false);
                    ProcessStarfireDetections(ref nextStarfireCycle, requestTaunt: false);
                    ProcessStardustBreathDetections(
                        ref nextStardustBreath,
                        requestHeal: false
                    );

                    if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        StopFightCombat();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (
                    LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold)
                )
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            bool bossAlive = LoneWolf.IsMonsterAlive(BossMapId);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            if (
                !openingTauntRequested
                && bossAlive
                && DateTime.UtcNow >= openingTauntAt
            )
            {
                openingTauntRequested = true;
                LoneWolf.RequestAbsolutePriorityTaunt(BossMapId);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested the opening taunt."
                );
            }

            LoneWolf.MaintainTarget(BossMapId);
            ProcessStarfireDetections(ref nextStarfireCycle, requestTaunt: true);
            ProcessStardustBreathDetections(
                ref nextStardustBreath,
                requestHeal: true
            );
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Astral Empyrean defeated.");
        return FightResult.Defeated;
    }

    private void ProcessStarfireDetections(
        ref int nextStarfireCycle,
        bool requestTaunt
    )
    {
        if (!isTaunter)
            return;

        while (LoneWolf.HasPacketDetection(nextStarfireCycle))
        {
            bool ownsCycle = nextStarfireCycle % 2 == 1
                ? LoneWolf.IsArmyPlayer(
                    UsesStableRoleLayout ? 3 : 1
                )
                : LoneWolf.IsArmyPlayer(5);

            if (ownsCycle && requestTaunt)
            {
                LoneWolf.RequestAbsolutePriorityTaunt(BossMapId);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested absolute priority Starfire taunt {nextStarfireCycle}."
                );
            }

            nextStarfireCycle++;
        }
    }

    private bool StartFightPacketDetector()
    {
        if (isTaunter)
            return LoneWolf.StartPacketDetector(PacketCommand, StarfirePacketText);

        if (isTimedHealer)
        {
            return LoneWolf.StartPacketDetector(
                PacketCommand,
                new[] { BossPacketText, StardustBreathPacketText }
            );
        }

        return true;
    }

    private void ProcessStardustBreathDetections(
        ref int nextStardustBreath,
        bool requestHeal
    )
    {
        if (!isTimedHealer)
            return;

        while (LoneWolf.HasPacketDetection(nextStardustBreath))
        {
            if (requestHeal)
            {
                LoneWolf.RequestAbsolutePrioritySkill(2);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested timed Stardust Breath heal {nextStardustBreath}."
                );
            }

            nextStardustBreath++;
        }
    }

    private void StartZoneListener()
    {
        lock (zoneLock)
            pendingZones.Clear();

        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;
    }

    private void StopZoneListener()
    {
        Bot.Events.RunToArea -= OnRunToArea;

        lock (zoneLock)
            pendingZones.Clear();
    }

    private void OnRunToArea(string zone)
    {
        if (zone != "A" && zone != "B")
            return;

        lock (zoneLock)
            pendingZones.Enqueue(zone);
    }

    private void DrainZoneEvents(bool move)
    {
        while (true)
        {
            string zone;
            lock (zoneLock)
            {
                if (pendingZones.Count == 0)
                    return;

                zone = pendingZones.Dequeue();
            }

            if (!move)
                continue;

            if (zone == "B")
                Bot.Player.WalkTo(ZoneBTargetX, ZoneBTargetY);
            else
                Bot.Player.WalkTo(ZoneATargetX, ZoneATargetY);
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold);
        Core.Jump(SafeCell, SafePad);

        if (!IsInSafeRoom())
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not reach the safe room after reset.",
                "HandleFightReset",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopFightCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private void WarnIfVaingloryCapeIsEquipped()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                !item.Equipped
                || !string.Equals(
                    item.CategoryString,
                    "Cape",
                    StringComparison.OrdinalIgnoreCase
                )
                || item.EnhancementPatternID != (int)CapeSpecial.Vainglory
            )
                continue;

            Core.Logger(
                "Vainglory is enhanced on the cape. Astral Empyrean will fail.",
                "Prepare",
                messageBox: true
            );
            return;
        }
    }

    private void ApplyAstralOverrides(ClassPreset preset)
    {
        if (preset.CapeEnhancement == CapeSpecial.Vainglory)
            preset.CapeEnhancement = CapeSpecial.Lament;

        if (armyComposition == ArmyComposition.Fast && LoneWolf.IsArmyPlayer(7))
            preset.HelmEnhancement = HelmSpecial.Forge;

        if (isTimedHealer)
        {
            preset.CapeEnhancement = CapeSpecial.Absolution;
            preset.Skills = new[] { 3, 1, 4 };
        }

        if (isTaunter)
            preset.CombatPotion = null;
    }

    private ClassPreset GetClassPreset()
    {
        if (UsesStableRoleLayout)
        {
            if (LoneWolf.IsArmyPlayer(1))
                return LoneWolf.KingsEcho();

            if (LoneWolf.IsArmyPlayer(2))
                return LoneWolf.StoneCrusher();

            if (LoneWolf.IsArmyPlayer(3))
                return LoneWolf.ArchPaladin();

            if (LoneWolf.IsArmyPlayer(4))
                return LoneWolf.LordOfOrder();

            if (LoneWolf.IsArmyPlayer(5))
                return LoneWolf.VerusDoomKnight();

            if (LoneWolf.IsArmyPlayer(6))
                return LoneWolf.Bard();

            return LoneWolf.ArchFiend();
        }

        if (LoneWolf.IsArmyPlayer(1))
            return LoneWolf.LegionRevenant();

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        if (LoneWolf.IsArmyPlayer(4))
            return LoneWolf.LordOfOrder();

        if (LoneWolf.IsArmyPlayer(5))
            return LoneWolf.VerusDoomKnight();

        if (LoneWolf.IsArmyPlayer(6))
            return LoneWolf.Bard();

        return armyComposition == ArmyComposition.Fast
            ? LoneWolf.ArcanaInvoker()
            : LoneWolf.Shaman();
    }

    private string GetPlayerAlias()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return "playerOne";

        if (LoneWolf.IsArmyPlayer(2))
            return "playerTwo";

        if (LoneWolf.IsArmyPlayer(3))
            return "playerThree";

        if (LoneWolf.IsArmyPlayer(4))
            return "playerFour";

        if (LoneWolf.IsArmyPlayer(5))
            return "playerFive";

        if (LoneWolf.IsArmyPlayer(6))
            return "playerSix";

        return "playerSeven";
    }

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private void StopArmyAfterFailedAttempts()
    {
        if (LoneWolf.IsArmyPlayer(1))
            LoneWolf.StopArmySync("ATTEMPTS_EXHAUSTED");
        else
            LoneWolf.SyncArmy("STOP_CHECK");

        Core.Logger(
            $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
            "RunFightAttempts",
            messageBox: true,
            stopBot: true
        );
    }

    private void StopArmy()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            Bot.Sleep(2000);

            if (Bot.ShouldExit)
                return;

            if (LoneWolf.StopArmySync("COMPLETE"))
                Core.Logger($"{LogPrefix} playerOne published COMPLETE.");
            else
                Core.Logger($"{LogPrefix} playerOne could not publish COMPLETE.");

            return;
        }

        if (LoneWolf.SyncArmy("STOP_CHECK"))
            Core.Logger($"{LogPrefix} {playerAlias} unexpectedly passed STOP_CHECK.");
        else
            Core.Logger($"{LogPrefix} {playerAlias} detected COMPLETE.");
    }
}
