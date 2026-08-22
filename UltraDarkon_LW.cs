/*
name: Ultra Darkon LW
description: Four-player CoreLoneWolf Army script for Ultra Darkon.
tags: ultra, darkon, weekly, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDarkon_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Optimized,
        Test,
        Test2,
        Test3,
        Test4,
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

    private const string LogPrefix = "Ultra Darkon LW";
    private const string SyncFileName = "UltraDarkon_LW.sync";
    private const string MapName = "ultradarkon";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string BossCasterMarker = "\"cInf\":\"m:1\"";
    private const string Attack2Marker = "\"animStr\":\"Attack2\"";
    private const string Attack3Marker = "\"animStr\":\"Attack3\"";
    private const string AriaAura = "Aria";
    private const string AMajorAura = "A Major";
    private const string RighteousSealAura = "Righteous Seal";
    private const int UltraQuestId = 8746;
    private const int PrerequisiteQuestId = 8733;
    private const string PrerequisiteQuestName = "The World";
    private const int MinimumLevel = 90;
    private const int DarkonMapId = 1;
    private const int LooHoldHealth = 10_000_000;
    private const int LooReleaseHealth = 4_500_000;
    private const int TauntDelay = 1250;
    private const int LooExtraHealWindowStartDelay = 6000;
    private const int LooExtraHealWindowDuration = 500;
    private const int ApExtraHealWindowStartDelay = 5000;
    private const int ApExtraHealWindowDuration = 1500;
    private const int RequiredHealDelay = 250;
    private const int RighteousSealSkillFourWindow = 1500;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private bool isTaunter;
    private int privateRoomNumber;

    public string OptionsStorage = "UltraDarkon_LW";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nOptimized: LC / SC / AP / LOO\nTest: LR / SC / AP / LOO\nTest2: VDK / SC / AP / LOO\nTest3: Guardian / AP / LR / LOO\nTest4: Guardian / SC / AP / LOO",
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
            LoneWolf.StopSkillEngine();
        }
    }

    public UltraRunResult RunFromMaster()
    {
        masterMode = true;
        runResult = UltraRunResult.Failed;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, 4, masterMode ? "Setup" : null))
            return;

        if (
            !LoneWolf.ValidateUltraAccess(
                UltraQuestId,
                PrerequisiteQuestId,
                PrerequisiteQuestName,
                MinimumLevel,
                LogPrefix,
                GetClassPreset().ClassName
            )
        )
            return;

        playerAlias = GetPlayerAlias();
        isTaunter = IsTaunter();

        ClassPreset preset = GetClassPreset();
        if (isTaunter)
            preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias} using {armyComposition} composition.");
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
        runResult = UltraRunResult.Completed;
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

            if (!PrepareSafeRoom(preset) || !StartDarkonPacketDetector())
                return false;

            if (!Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            FightResult result = Fight(preset, fightAttempt);
            LoneWolf.StopPacketDetector();

            if (result == FightResult.Defeated)
                return true;

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                StopArmyAfterFailedAttempts();
                runResult = UltraRunResult.AttemptsExhausted;
                return false;
            }
        }

        return false;
    }

    private bool ValidateOptions()
    {
        armyComposition = GetUltraOption<ArmyComposition>(
            "UltraDarkonComposition",
            "ArmyComposition"
        );
        privateRoomNumber = GetSetupOption<int>("PrivateRoomNumber");
        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (GetSetupOption<bool>("UseEnhancements"))
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (isTaunter)
            LoneWolf.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        WarnIfVaingloryCapeIsEquipped();
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
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
                "Vainglory is enhanced on the cape. Ultra Darkon will fail.",
                "Prepare",
                messageBox: true
            );
            return;
        }
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool StartDarkonPacketDetector()
    {
        bool detectsAttack2 = armyComposition == ArmyComposition.Test3
            ? LoneWolf.IsArmyPlayer(2)
            : UsesTestFightBehavior()
                ? LoneWolf.IsArmyPlayer(3)
                : UsesDefaultFightRoles()
                    ? LoneWolf.IsArmyPlayer(4)
                    : LoneWolf.IsArmyPlayer(1);
        string animationMarker = detectsAttack2
            ? Attack2Marker
            : Attack3Marker;

        if (
            LoneWolf.StartPacketDetector(
                PacketCommand,
                new[] { BossCasterMarker, animationMarker }
            )
        )
            return true;

        Core.Logger(
            "The Darkon packet detector could not be started.",
            "RunFightAttempts",
            messageBox: true,
            stopBot: true
        );
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
            useSurvivalSkill: !(
                armyComposition == ArmyComposition.Stable
                && LoneWolf.IsArmyPlayer(1)
            ),
            maintainedPotion: !isTaunter
                && GetSetupOption<bool>("UsePotions")
                    ? preset.CombatPotion
                    : null,
            kingsEchoManaThreshold:
                armyComposition == ArmyComposition.Stable ? 25 : 12,
            blockedSimpleSkill:
                (
                    armyComposition == ArmyComposition.Test3
                    || armyComposition == ArmyComposition.Test4
                )
                && LoneWolf.IsArmyPlayer(1)
                    ? 4
                    : 0,
            blockedSimpleSkillTargetAura:
                (
                    armyComposition == ArmyComposition.Test3
                    || armyComposition == ArmyComposition.Test4
                )
                && LoneWolf.IsArmyPlayer(1)
                    ? AMajorAura
                    : string.Empty
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool bossObserved = LoneWolf.IsMonsterAlive(DarkonMapId);
        bool tauntScheduled = false;
        bool looHoldLogged = false;
        bool looSkillFourRequested = false;
        bool looSkillFourReleased = false;
        int nextDetection = 1;
        int nextAttack2Signal = 1;
        int scheduledTauntCycle = 0;
        int looExtraHealCycle = 0;
        int apExtraHealCycle = 0;
        int delayedHealCycle = 0;
        string delayedHealAttack = string.Empty;
        DateTimeOffset tauntAt = DateTimeOffset.MinValue;
        DateTimeOffset looExtraHealWindowStart = DateTimeOffset.MinValue;
        DateTimeOffset apExtraHealWindowStart = DateTimeOffset.MinValue;
        DateTimeOffset delayedHealAt = DateTimeOffset.MinValue;
        bool apPhaseThreeConfigured = false;
        bool apSkillThreeBlocked = false;
        bool handlesAttack2Signals = !UsesDefaultFightRoles()
            && !UsesTestFightBehavior()
            && LoneWolf.IsArmyPlayer(4);

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.IsMonsterAlive(DarkonMapId))
                bossObserved = true;
            else if (bossObserved)
                break;

            if (LoneWolf.ShouldResetFight(fightAttempt))
            {
                LoneWolf.StopSkillEngine();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");
                tauntScheduled = false;
                looSkillFourRequested = false;
                looExtraHealCycle = 0;
                looExtraHealWindowStart = DateTimeOffset.MinValue;
                apExtraHealCycle = 0;
                apExtraHealWindowStart = DateTimeOffset.MinValue;
                delayedHealCycle = 0;
                delayedHealAttack = string.Empty;
                delayedHealAt = DateTimeOffset.MinValue;
                LoneWolf.SetOrdinarySkillsSuppressed(false);

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (LoneWolf.ShouldResetFight(fightAttempt))
                    {
                        LoneWolf.StopSkillEngine();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (!LoneWolf.IsMonsterAlive(DarkonMapId) && bossObserved)
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt))
                {
                    LoneWolf.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (!IsInBossRoom())
                    Core.Jump(BossCell, BossPad);

                LoneWolf.MaintainTarget(DarkonMapId);
                AdvanceDetections(ref nextDetection);
                if (handlesAttack2Signals)
                    AdvanceAttack2Signals(
                        fightAttempt,
                        ref nextAttack2Signal
                    );
                continue;
            }

            LoneWolf.MaintainTarget(DarkonMapId);

            if (LoneWolf.IsArmyPlayer(4))
                ReleaseLooSkillFour(
                    ref looHoldLogged,
                    ref looSkillFourRequested,
                    ref looSkillFourReleased
                );

            if (!HandlePacketDetections(
                fightAttempt,
                ref nextDetection,
                ref tauntScheduled,
                ref scheduledTauntCycle,
                ref tauntAt,
                ref looExtraHealCycle,
                ref looExtraHealWindowStart,
                ref apExtraHealCycle,
                ref apExtraHealWindowStart,
                ref delayedHealCycle,
                ref delayedHealAttack,
                ref delayedHealAt
            ))
            {
                LoneWolf.StopSkillEngine();
                return FightResult.Stopped;
            }

            if (handlesAttack2Signals)
                HandleAttack2Signals(
                    fightAttempt,
                    ref nextAttack2Signal,
                    ref looExtraHealCycle,
                    ref looExtraHealWindowStart,
                    ref delayedHealCycle,
                    ref delayedHealAttack,
                    ref delayedHealAt
                );

            TryQueueDelayedPacketHeal(
                ref delayedHealCycle,
                ref delayedHealAttack,
                ref delayedHealAt
            );

            HandleArchPaladin(
                ref apPhaseThreeConfigured,
                ref apSkillThreeBlocked
            );

            if (
                UsesTestFightBehavior()
                && IsArchPaladinPlayer()
            )
                TryQueueApExtraHeal(
                    apPhaseThreeConfigured,
                    ref apExtraHealCycle,
                    ref apExtraHealWindowStart
                );

            if (tauntScheduled && DateTimeOffset.Now >= tauntAt)
            {
                LoneWolf.RequestTaunt(DarkonMapId);
                Core.Logger($"{LogPrefix} {playerAlias} requested Darkon taunt cycle {scheduledTauntCycle}.");
                tauntScheduled = false;
            }

            if (LoneWolf.IsArmyPlayer(4))
                TryQueueLooExtraHeal(
                    ref looExtraHealCycle,
                    ref looExtraHealWindowStart
                );

            if (LoneWolf.IsArmyPlayer(4))
                UseLooSkillFourNormally(looSkillFourReleased);

            Bot.Sleep(FightPollDelay);
        }

        LoneWolf.StopSkillEngine();

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Darkon defeated.");
        return FightResult.Defeated;
    }

    private bool HandlePacketDetections(
        int fightAttempt,
        ref int nextDetection,
        ref bool tauntScheduled,
        ref int scheduledTauntCycle,
        ref DateTimeOffset tauntAt,
        ref int looExtraHealCycle,
        ref DateTimeOffset looExtraHealWindowStart,
        ref int apExtraHealCycle,
        ref DateTimeOffset apExtraHealWindowStart,
        ref int delayedHealCycle,
        ref string delayedHealAttack,
        ref DateTimeOffset delayedHealAt
    )
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
        {
            int cycle = nextDetection++;

            if (
                UsesTestFightBehavior()
                && IsArchPaladinPlayer()
            )
            {
                SchedulePacketHeal(
                    "Attack2",
                    cycle,
                    ref delayedHealCycle,
                    ref delayedHealAttack,
                    ref delayedHealAt
                );
                ScheduleApExtraHeal(
                    cycle,
                    ref apExtraHealCycle,
                    ref apExtraHealWindowStart
                );
                continue;
            }

            if (
                !UsesDefaultFightRoles()
                && !UsesTestFightBehavior()
                && LoneWolf.IsArmyPlayer(1)
            )
            {
                string signal = GetAttack2Signal(fightAttempt, cycle);
                if (!LoneWolf.SendArmySignal(signal))
                {
                    Core.Logger(
                        $"{LogPrefix} playerOne could not send {signal}.",
                        "HandlePacketDetections",
                        messageBox: true,
                        stopBot: true
                    );
                    return false;
                }

                Core.Logger($"{LogPrefix} playerOne sent {signal}.");
                continue;
            }

            if (IsArchPaladinPlayer())
                SchedulePacketHeal(
                    "Attack3",
                    cycle,
                    ref delayedHealCycle,
                    ref delayedHealAttack,
                    ref delayedHealAt
                );

            if (isTaunter)
            {
                bool openingOwner = IsOpeningTauntOwner();
                bool ownsCycle = openingOwner
                    ? cycle % 2 == 1
                    : cycle % 2 == 0;

                if (ownsCycle)
                {
                    scheduledTauntCycle = cycle;
                    tauntAt = DateTimeOffset.Now.AddMilliseconds(TauntDelay);
                    tauntScheduled = true;
                    Core.Logger($"{LogPrefix} {playerAlias} detected owned Attack3 cycle {cycle}.");
                }
            }
            if (UsesTestFightBehavior() && LoneWolf.IsArmyPlayer(4))
            {
                if (cycle == 1 || cycle % 2 == 0)
                    SchedulePacketHeal(
                        "Attack3",
                        cycle,
                        ref delayedHealCycle,
                        ref delayedHealAttack,
                        ref delayedHealAt
                    );
            }
            else if (
                UsesDefaultFightRoles()
                && LoneWolf.IsArmyPlayer(4)
            )
            {
                SchedulePacketHeal(
                    "Attack2",
                    cycle,
                    ref delayedHealCycle,
                    ref delayedHealAttack,
                    ref delayedHealAt
                );
                ScheduleLooExtraHeal(
                    cycle,
                    ref looExtraHealCycle,
                    ref looExtraHealWindowStart
                );
            }
        }

        return true;
    }

    private void HandleAttack2Signals(
        int fightAttempt,
        ref int nextSignal,
        ref int extraHealCycle,
        ref DateTimeOffset extraHealWindowStart,
        ref int delayedHealCycle,
        ref string delayedHealAttack,
        ref DateTimeOffset delayedHealAt
    )
    {
        while (
            LoneWolf.HasArmySignal(
                GetAttack2Signal(fightAttempt, nextSignal),
                1
            )
        )
        {
            SchedulePacketHeal(
                "Attack2",
                nextSignal,
                ref delayedHealCycle,
                ref delayedHealAttack,
                ref delayedHealAt
            );
            ScheduleLooExtraHeal(
                nextSignal,
                ref extraHealCycle,
                ref extraHealWindowStart
            );

            nextSignal++;
        }
    }

    private void ScheduleApExtraHeal(
        int cycle,
        ref int extraHealCycle,
        ref DateTimeOffset extraHealWindowStart
    )
    {
        extraHealCycle = cycle;
        extraHealWindowStart = DateTimeOffset.Now.AddMilliseconds(
            ApExtraHealWindowStartDelay
        );
    }

    private void TryQueueApExtraHeal(
        bool phaseThreeConfigured,
        ref int cycle,
        ref DateTimeOffset windowStart
    )
    {
        if (cycle <= 0)
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        if (now < windowStart)
            return;

        if (phaseThreeConfigured)
        {
            int phaseThreeCycle = cycle;
            cycle = 0;
            windowStart = DateTimeOffset.MinValue;
            LoneWolf.RequestPrioritySkill(2);
            Core.Logger($"{LogPrefix} {playerAlias} queued its mandatory Phase 3 midpoint heal for cycle {phaseThreeCycle}.");
            return;
        }

        if (
            now >= windowStart.AddMilliseconds(
                ApExtraHealWindowDuration
            )
        )
        {
            cycle = 0;
            windowStart = DateTimeOffset.MinValue;
            return;
        }

        if (
            !Bot.Player.Alive
            || LoneWolf.HasPendingPrioritySkill()
            || !Bot.Skills.CanUseSkill(2)
        )
            return;

        int scheduledCycle = cycle;
        cycle = 0;
        windowStart = DateTimeOffset.MinValue;
        LoneWolf.RequestPrioritySkill(2);
        Core.Logger($"{LogPrefix} {playerAlias} queued its optional Attack2 heal for cycle {scheduledCycle}.");
    }

    private void ScheduleLooExtraHeal(
        int cycle,
        ref int extraHealCycle,
        ref DateTimeOffset extraHealWindowStart
    )
    {
        extraHealCycle = cycle;
        extraHealWindowStart = DateTimeOffset.Now.AddMilliseconds(
            LooExtraHealWindowStartDelay
        );
    }

    private void TryQueueLooExtraHeal(
        ref int cycle,
        ref DateTimeOffset windowStart
    )
    {
        if (cycle <= 0)
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        if (now < windowStart)
            return;

        if (
            now >= windowStart.AddMilliseconds(
                LooExtraHealWindowDuration
            )
        )
        {
            cycle = 0;
            windowStart = DateTimeOffset.MinValue;
            return;
        }

        if (
            !Bot.Player.Alive
            || LoneWolf.HasPendingPrioritySkill()
            || !Bot.Skills.CanUseSkill(2)
        )
            return;

        int scheduledCycle = cycle;
        cycle = 0;
        windowStart = DateTimeOffset.MinValue;
        LoneWolf.RequestPrioritySkill(2);
        Core.Logger($"{LogPrefix} {playerAlias} queued its extra Attack2 heal for cycle {scheduledCycle}.");
    }

    private void SchedulePacketHeal(
        string attack,
        int cycle,
        ref int delayedCycle,
        ref string delayedAttack,
        ref DateTimeOffset delayedAt
    )
    {
        delayedCycle = cycle;
        delayedAttack = attack;
        delayedAt = DateTimeOffset.Now.AddMilliseconds(RequiredHealDelay);
        LoneWolf.SetOrdinarySkillsSuppressed(true);
        Core.Logger($"{LogPrefix} {playerAlias} scheduled its {attack} heal for cycle {cycle}.");
    }

    private void TryQueueDelayedPacketHeal(
        ref int cycle,
        ref string attack,
        ref DateTimeOffset dueAt
    )
    {
        if (cycle <= 0 || DateTimeOffset.Now < dueAt)
            return;

        int scheduledCycle = cycle;
        string scheduledAttack = attack;
        cycle = 0;
        attack = string.Empty;
        dueAt = DateTimeOffset.MinValue;
        LoneWolf.RequestPrioritySkill(2);
        LoneWolf.SetOrdinarySkillsSuppressed(false);
        Core.Logger($"{LogPrefix} {playerAlias} queued its delayed {scheduledAttack} heal for cycle {scheduledCycle}.");
    }

    private void HandleArchPaladin(
        ref bool phaseThreeConfigured,
        ref bool skillThreeBlocked
    )
    {
        if (
            !IsArchPaladinPlayer()
            || !Bot.Player.Alive
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != DarkonMapId
            || Bot.Player.Target?.HP <= 0
        )
            return;

        bool phaseThreeChanged = false;
        if (
            !phaseThreeConfigured
            && Bot.Target.GetAura(AriaAura) != null
        )
        {
            phaseThreeConfigured = true;
            phaseThreeChanged = true;
            Core.Logger($"{LogPrefix} {playerAlias} switched to its Phase 3 skill array.");
        }

        bool shouldBlockSkillThree = Bot.Target.GetAura(AMajorAura) != null;
        bool skillThreeBlockChanged = shouldBlockSkillThree != skillThreeBlocked;
        if (phaseThreeChanged || skillThreeBlockChanged)
        {
            int[] skills = phaseThreeConfigured
                ? shouldBlockSkillThree
                    ? new[] { 1 }
                    : new[] { 1, 3 }
                : shouldBlockSkillThree
                    ? new[] { 1, 4 }
                    : new[] { 3, 1, 4 };

            LoneWolf.SetSkillEngineSkills(skills);
            skillThreeBlocked = shouldBlockSkillThree;
            if (skillThreeBlockChanged)
            {
                Core.Logger(
                    shouldBlockSkillThree
                        ? $"{LogPrefix} {playerAlias} blocked skill 3 for A Major."
                        : $"{LogPrefix} {playerAlias} restored skill 3 after A Major."
                );
            }
        }

        if (!phaseThreeConfigured)
            return;

        var righteousSeal = Bot.Target.GetAura(RighteousSealAura);
        TimeSpan righteousSealRemaining = righteousSeal?.ExpiresAt
            - DateTimeOffset.Now ?? TimeSpan.Zero;
        if (
            righteousSeal == null
            || righteousSealRemaining <= TimeSpan.Zero
            || righteousSealRemaining
                > TimeSpan.FromMilliseconds(RighteousSealSkillFourWindow)
            || LoneWolf.HasPendingPrioritySkill()
            || !Bot.Skills.CanUseSkill(4)
        )
            return;

        LoneWolf.RequestPrioritySkill(4);
        Core.Logger($"{LogPrefix} {playerAlias} queued skill 4 for Righteous Seal.");
    }

    private void ReleaseLooSkillFour(
        ref bool holdLogged,
        ref bool skillFourRequested,
        ref bool skillFourReleased
    )
    {
        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != DarkonMapId
            || Bot.Player.Target?.HP <= 0
            || skillFourReleased
        )
            return;

        if (skillFourRequested)
        {
            if (LoneWolf.HasPendingAbsolutePrioritySkill())
                return;

            skillFourRequested = false;
            skillFourReleased = true;
            Core.Logger($"{LogPrefix} playerFour released held skill 4.");
            return;
        }

        if (Bot.Target.GetAura(AriaAura) != null)
            return;

        int health = Bot.Player.Target?.HP ?? 0;
        if (health <= LooHoldHealth && !holdLogged)
        {
            holdLogged = true;
            Core.Logger($"{LogPrefix} playerFour is holding skill 4.");
        }

        if (health <= LooReleaseHealth)
        {
            LoneWolf.RequestAbsolutePrioritySkill(4);
            skillFourRequested = true;
            Core.Logger($"{LogPrefix} playerFour queued held skill 4 with absolute priority.");
        }
    }

    private void UseLooSkillFourNormally(bool skillFourReleased)
    {
        if (
            LoneWolf.HasPendingPrioritySkill()
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != DarkonMapId
            || Bot.Player.Target?.HP <= 0
        )
            return;

        bool aria = Bot.Target.GetAura(AriaAura) != null;
        int health = Bot.Player.Target?.HP ?? 0;

        if (
            !aria
            && (health <= LooHoldHealth || skillFourReleased)
        )
            return;

        if (Bot.Skills.CanUseSkill(4))
            Bot.Skills.UseSkill(4);
    }

    private void AdvanceDetections(ref int nextDetection)
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
            nextDetection++;
    }

    private void AdvanceAttack2Signals(
        int fightAttempt,
        ref int nextSignal
    )
    {
        while (
            LoneWolf.HasArmySignal(
                GetAttack2Signal(fightAttempt, nextSignal),
                1
            )
        )
            nextSignal++;
    }

    private static string GetAttack2Signal(
        int fightAttempt,
        int cycle
    ) =>
        $"DARKON_ATTACK2_{fightAttempt}_{cycle}";

    private bool HandleFightReset(int fightAttempt)
    {
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightAttempt);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        LoneWolf.ShouldResetFight(fightAttempt);
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

    private void StopArmyAfterFailedAttempts()
    {
        if (masterMode)
        {
            if (LoneWolf.IsArmyPlayer(1))
                LoneWolf.StopArmySync("ATTEMPTS_EXHAUSTED");
            else
                LoneWolf.SyncArmy("STOP_CHECK");

            Core.Logger(
                $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
                "RunFightAttempts"
            );
            return;
        }

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

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private T GetSetupOption<T>(string optionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Setup", optionName)
            : Bot.Config!.Get<T>(optionName))!;

    private T GetUltraOption<T>(string masterOptionName, string standaloneOptionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Weekly_Ultras", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool UsesDefaultFightRoles() =>
        armyComposition == ArmyComposition.Default
        || armyComposition == ArmyComposition.Test
        || armyComposition == ArmyComposition.Test2;

    private bool UsesTestFightBehavior() =>
        armyComposition == ArmyComposition.Test
        || armyComposition == ArmyComposition.Test2
        || armyComposition == ArmyComposition.Test3
        || armyComposition == ArmyComposition.Test4;

    private bool IsTaunter() =>
        armyComposition == ArmyComposition.Test3
            ? LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(4)
            : armyComposition == ArmyComposition.Test4
                ? LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(4)
                : UsesDefaultFightRoles()
                    ? LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(2)
                    : LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(3);

    private bool IsArchPaladinPlayer() =>
        armyComposition == ArmyComposition.Test3
            ? LoneWolf.IsArmyPlayer(2)
            : LoneWolf.IsArmyPlayer(3);

    private bool IsOpeningTauntOwner() =>
        armyComposition == ArmyComposition.Test3
            ? LoneWolf.IsArmyPlayer(3)
            : UsesDefaultFightRoles()
                ? LoneWolf.IsArmyPlayer(1)
                : LoneWolf.IsArmyPlayer(2);

    private ClassPreset GetClassPreset()
    {
        ClassPreset preset;

        if (LoneWolf.IsArmyPlayer(1))
        {
            if (
                armyComposition == ArmyComposition.Test3
                || armyComposition == ArmyComposition.Test4
            )
                preset = LoneWolf.Guardian();
            else if (armyComposition == ArmyComposition.Test2)
            {
                preset = LoneWolf.VerusDoomKnight();
                preset.CapeEnhancement = CapeSpecial.Penitence;
            }
            else if (armyComposition == ArmyComposition.Stable)
            {
                preset = LoneWolf.KingsEcho();
                preset.CapeEnhancement = CapeSpecial.Penitence;
                preset.HelmEnhancement = HelmSpecial.Forge;
            }
            else if (armyComposition == ArmyComposition.Optimized)
                preset = LoneWolf.LightCaster();
            else
            {
                preset = LoneWolf.LegionRevenant();
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
        }
        else if (LoneWolf.IsArmyPlayer(2))
        {
            if (armyComposition == ArmyComposition.Test3)
            {
                preset = LoneWolf.ArchPaladin();
                preset.Skills = new[] { 3, 1, 4 };
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
            else
            {
                preset = LoneWolf.StoneCrusher();
                preset.CapeEnhancement = CapeSpecial.Absolution;
            }
        }
        else if (LoneWolf.IsArmyPlayer(3))
        {
            if (armyComposition == ArmyComposition.Test3)
            {
                preset = LoneWolf.LegionRevenant();
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
            else
            {
                preset = LoneWolf.ArchPaladin();
                preset.Skills = new[] { 3, 1, 4 };
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
        }
        else
        {
            preset = LoneWolf.LordOfOrder();
            preset.Skills = new[] { 3, 1 };
            preset.CapeEnhancement = CapeSpecial.Absolution;
        }

        if (preset.CapeEnhancement == CapeSpecial.Lament)
            preset.CapeEnhancement = CapeSpecial.Penitence;

        return preset;
    }

    private string GetPlayerAlias()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return "playerOne";

        if (LoneWolf.IsArmyPlayer(2))
            return "playerTwo";

        if (LoneWolf.IsArmyPlayer(3))
            return "playerThree";

        return "playerFour";
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
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
