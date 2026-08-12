/*
name: Ultra Gramiel LW
description: Four-player CoreLoneWolf Ultra Gramiel script.
tags: ultra, gramiel, weekly, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraGramiel_LW
{
    public enum ArmyComposition
    {
        Default,
        Optimized,
    }

    private enum PhaseResult
    {
        Completed,
        Reset,
        Stopped,
    }

    private enum PhaseTwoDetectorState
    {
        Attacks,
        Liberator,
        ChargeTwo,
        ChargeAttackTwo,
        WaitingForInvulnerableEnd,
        Finished,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Ultra Gramiel LW";
    private const string SyncFileName = "UltraGramiel_LW.sync";
    private const string MapName = "ultragramiel";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string FightCell = "r2";
    private const string FightPad = "Down";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string CrystalChargeMessage =
        "The Grace Crystal prepares a defense shattering attack!";
    private const string ChargeAnimationMarker = "\"animStr\":\"Charge\"";
    private const string SafeguardAuraMarker = "\"nam\":\"Safeguard\"";
    private const string GraceGivenAuraMarker = "\"nam\":\"Grace Given\"";
    private const string GramielCasterMarker = "\"cInf\":\"m:1\"";
    private const string AttackAnimationMarker = "\"animStr\":\"Attack";
    private const string ChargeTwoAnimationMarker = "\"animStr\":\"Charge2\"";
    private const string ChargeAttackTwoAnimationMarker =
        "\"animStr\":\"ChargeAttack2\"";
    private const string LiberatorMarker = "Liberator";
    private const string InvulnerableAura = "Invulnerable";
    private const int UltraQuestId = 10301;
    private const int PrerequisiteQuestId = 9986;
    private const string PrerequisiteQuestName = "Isa, Reversed - Realized";
    private const int MinimumLevel = 80;
    private const int GramielMapId = 1;
    private const int CrystalAMapId = 2;
    private const int CrystalBMapId = 3;
    private const int BalanceStartDifference = 20;
    private const int BalanceStopDifference = 2;
    private const int TauntTargetHold = 1500;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int GramielNukeCount = 3;

    private string playerAlias = string.Empty;
    private int privateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraGramiel_LW";
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
            "Default: LR / SC / AP / LOO\nOptimized: Shaman / SC / AP / LOO",
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
            "Prepare and use the assigned tonic and elixir.",
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
        ClassPreset preset = GetClassPreset();
        preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias}.");
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

            if (
                !PrepareSafeRoom(preset)
                || !StartCrystalPacketDetector()
            )
                return false;

            bool usePhaseTwoSlowdown = ShouldUsePhaseTwoSlowdown();
            if (!usePhaseTwoSlowdown && IsPhaseTwoSlowdownOwner())
                Core.Logger(
                    $"{LogPrefix} {playerAlias} disabled Phase 2 slowdown because the equipped weapon is below 40% damage boost."
                );

            if (!Sync("FIGHT_READY"))
                return false;

            Core.Jump(FightCell, FightPad);

            PhaseResult result = FightPhaseOne(preset, fightAttempt);
            if (result == PhaseResult.Completed)
            {
                result = FightPhaseTwo(
                    preset,
                    fightAttempt,
                    usePhaseTwoSlowdown
                );
                if (result == PhaseResult.Completed)
                    return true;
            }

            if (
                result != PhaseResult.Reset
                || !HandleFightReset(fightAttempt)
            )
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
            "UltraGramielComposition",
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
                warnForElysiumUnlock: IsOptimizedShaman()
            );

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        LoneWolf.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        LoneWolf.EquipScroll(EnrageScroll);
        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool StartCrystalPacketDetector()
    {
        if (
            LoneWolf.StartPacketDetector(
                PacketCommand,
                new[] { CrystalChargeMessage, ChargeAnimationMarker }
            )
        )
            return true;

        Core.Logger(
            "The crystal packet detector could not be started.",
            "RunFightAttempts",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private PhaseResult FightPhaseOne(ClassPreset preset, int fightAttempt)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            true,
            LogPrefix,
            IsOptimizedShaman()
                ? SkillEngineMode.Simple
                : preset.SkillMode
        );
        Core.Logger($"{LogPrefix} {playerAlias} started Phase 1.");

        int nextDetection = 1;
        int emergencyTarget = 0;
        bool crystalBalancing = false;
        bool crystalsObserved = false;
        bool safeguardActive = false;
        DateTimeOffset tauntTargetUntil = DateTimeOffset.MinValue;
        bool shamanOpeningSkillFourQueued = false;
        bool shamanSafeguardSkillFourQueued = false;

        while (!Bot.ShouldExit)
        {
            int crystalAHealth = LoneWolf.GetMonsterHP(CrystalAMapId);
            int crystalBHealth = LoneWolf.GetMonsterHP(CrystalBMapId);

            if (LoneWolf.ShouldResetFight(fightAttempt))
                return FinishPhaseOne(PhaseResult.Reset);

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");
                crystalBalancing = false;
                safeguardActive = false;
                tauntTargetUntil = DateTimeOffset.MinValue;

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (LoneWolf.ShouldResetFight(fightAttempt))
                        return FinishPhaseOne(PhaseResult.Reset);

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                crystalAHealth = LoneWolf.GetMonsterHP(CrystalAMapId);
                crystalBHealth = LoneWolf.GetMonsterHP(CrystalBMapId);

                if (LoneWolf.ShouldResetFight(fightAttempt))
                    return FinishPhaseOne(PhaseResult.Reset);

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (!IsInFightRoom())
                    Core.Jump(FightCell, FightPad);

                AdvanceDetections(ref nextDetection);
                continue;
            }

            if (!crystalsObserved)
            {
                if (crystalAHealth > 0 && crystalBHealth > 0)
                {
                    crystalsObserved = true;
                    Core.Logger($"{LogPrefix} {playerAlias} observed both Grace Crystals.");
                }
                else
                {
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            if (crystalAHealth <= 0 && crystalBHealth <= 0)
                break;

            bool bothCrystalsAlive = crystalAHealth > 0
                && crystalBHealth > 0;

            HandleCrystalDetections(
                bothCrystalsAlive,
                ref nextDetection,
                ref tauntTargetUntil
            );

            bool shamanSkillFourPending = IsOptimizedShaman()
                && LoneWolf.HasPendingTargetedPrioritySkill();
            if (!shamanSkillFourPending)
            {
                MaintainPhaseOneTarget(
                    crystalAHealth,
                    crystalBHealth,
                    ref crystalBalancing,
                    ref safeguardActive,
                    tauntTargetUntil,
                    ref emergencyTarget
                );
            }

            MaintainPhaseOneShamanSkillFour(
                bothCrystalsAlive,
                safeguardActive,
                tauntTargetUntil,
                ref shamanOpeningSkillFourQueued,
                ref shamanSafeguardSkillFourQueued
            );

            Bot.Sleep(FightPollDelay);
        }

        if (Bot.ShouldExit)
            return FinishPhaseOne(PhaseResult.Stopped);

        Core.Logger($"{LogPrefix} {playerAlias} confirmed both Grace Crystals defeated.");
        return FinishPhaseOne(PhaseResult.Completed);
    }

    private PhaseResult FightPhaseTwo(
        ClassPreset preset,
        int fightAttempt,
        bool usePhaseTwoSlowdown
    )
    {
        int[] normalSkills = GetPhaseTwoSkills(preset);
        LoneWolf.StartSkillEngine(
            normalSkills,
            playerAlias,
            true,
            LogPrefix,
            preset.SkillMode
        );

        Core.Logger($"{LogPrefix} {playerAlias} started Phase 2.");

        bool graceGivenObserved = false;
        bool gramielObserved = false;
        int nukeCycle = 1;
        int nextAttack = 1;
        int ownedAttack = GetPhaseTwoTauntAttack();
        PhaseTwoDetectorState detectorState = PhaseTwoDetectorState.Attacks;
        bool rotationRestricted = false;

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.ShouldResetFight(fightAttempt))
                return FinishPhaseTwo(PhaseResult.Reset);

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died during Phase 2.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (LoneWolf.ShouldResetFight(fightAttempt))
                        return FinishPhaseTwo(PhaseResult.Reset);

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt))
                    return FinishPhaseTwo(PhaseResult.Reset);

                Core.Logger($"{LogPrefix} {playerAlias} respawned during Phase 2.");

                if (!IsInFightRoom())
                    Core.Jump(FightCell, FightPad);
            }

            int gramielHealth = LoneWolf.GetMonsterHP(GramielMapId);
            if (gramielHealth > 0)
                gramielObserved = true;
            else if (gramielObserved)
            {
                Core.Logger($"{LogPrefix} {playerAlias} confirmed Gramiel defeated.");
                return FinishPhaseTwo(PhaseResult.Completed);
            }

            LoneWolf.MaintainTarget(GramielMapId);

            if (!graceGivenObserved)
            {
                if (
                    !Bot.Target
                        .GetMonsterAura(GramielMapId)
                        .Contains(
                            GraceGivenAuraMarker,
                            StringComparison.Ordinal
                        )
                )
                {
                    Bot.Sleep(FightPollDelay);
                    continue;
                }

                graceGivenObserved = true;
                if (!StartPhaseTwoAttackDetector())
                    return FinishPhaseTwo(PhaseResult.Stopped);

                Core.Logger($"{LogPrefix} {playerAlias} detected Grace Given and armed nuke cycle 1.");
            }

            switch (detectorState)
            {
                case PhaseTwoDetectorState.Attacks:
                    while (LoneWolf.HasPacketDetection(nextAttack))
                    {
                        int attack = nextAttack++;

                        if (
                            usePhaseTwoSlowdown
                            && armyComposition == ArmyComposition.Default
                            && LoneWolf.IsArmyPlayer(3)
                            && attack == 1
                        )
                        {
                            LoneWolf.SetSkillEngineSkills(new[] { 2 });
                            rotationRestricted = true;
                            Core.Logger($"{LogPrefix} playerThree restricted its rotation to {{2}} on playerOne's taunt attack.");
                        }

                        if (IsOptimizedShaman())
                        {
                            if (attack == 3 && usePhaseTwoSlowdown)
                            {
                                LoneWolf.SetShamanSkillThreeEnabled(false);
                                Core.Logger($"{LogPrefix} playerOne disabled skill 3 on nuke cycle {nukeCycle}, attack 3.");
                            }

                            if (attack == 7)
                            {
                                if (usePhaseTwoSlowdown)
                                {
                                    LoneWolf.SetShamanSkillThreeEnabled(true);
                                    Core.Logger($"{LogPrefix} playerOne restored skill 3 on nuke cycle {nukeCycle}, attack 7.");
                                }

                                if (!StartChargeTwoPacketDetector())
                                    return FinishPhaseTwo(PhaseResult.Stopped);

                                detectorState = PhaseTwoDetectorState.ChargeTwo;
                                break;
                            }
                        }

                        if (attack != ownedAttack)
                            continue;

                        LoneWolf.MaintainTarget(GramielMapId);
                        LoneWolf.RequestTaunt(GramielMapId);
                        Core.Logger($"{LogPrefix} {playerAlias} requested Gramiel taunt on nuke cycle {nukeCycle}, attack {attack}.");

                        if (IsOptimizedShaman())
                            continue;

                        if (
                            LoneWolf.IsArmyPlayer(1)
                            || LoneWolf.IsArmyPlayer(3)
                        )
                        {
                            if (
                                LoneWolf.IsArmyPlayer(1)
                                && usePhaseTwoSlowdown
                            )
                            {
                                LoneWolf.SetSkillEngineSkills(new[] { 3 });
                                rotationRestricted = true;
                            }

                            if (!StartLiberatorPacketDetector())
                                return FinishPhaseTwo(PhaseResult.Stopped);

                            detectorState = PhaseTwoDetectorState.Liberator;
                            Core.Logger($"{LogPrefix} {playerAlias} started waiting for Liberator.");
                        }
                        else
                        {
                            if (!StartChargeTwoPacketDetector())
                                return FinishPhaseTwo(PhaseResult.Stopped);

                            detectorState = PhaseTwoDetectorState.ChargeTwo;
                        }

                        break;
                    }
                    break;

                case PhaseTwoDetectorState.Liberator:
                    if (!LoneWolf.HasPacketDetection(1))
                        break;

                    if (rotationRestricted)
                    {
                        LoneWolf.SetSkillEngineSkills(normalSkills);
                        rotationRestricted = false;
                        Core.Logger($"{LogPrefix} {playerAlias} detected Liberator in a {LoneWolf.GetPacketDetectorCommand()} packet and restored its normal rotation.");
                    }
                    else
                        Core.Logger($"{LogPrefix} {playerAlias} detected Liberator in a {LoneWolf.GetPacketDetectorCommand()} packet.");

                    if (!StartChargeTwoPacketDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.ChargeTwo;
                    break;

                case PhaseTwoDetectorState.ChargeTwo:
                    if (!LoneWolf.HasPacketDetection(1))
                        break;

                    LogGramielProtection(nukeCycle);

                    if (!StartChargeAttackTwoPacketDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.ChargeAttackTwo;
                    break;

                case PhaseTwoDetectorState.ChargeAttackTwo:
                    if (!LoneWolf.HasPacketDetection(1))
                        break;

                    Bot.Sleep(FightPollDelay);
                    LoneWolf.StopPacketDetector();
                    Core.Logger($"{LogPrefix} {playerAlias} handled ChargeAttack2 for nuke cycle {nukeCycle}.");
                    detectorState = PhaseTwoDetectorState.WaitingForInvulnerableEnd;
                    break;

                case PhaseTwoDetectorState.WaitingForInvulnerableEnd:
                    if (Bot.Self.HasActiveAura(InvulnerableAura))
                        break;

                    Core.Logger($"{LogPrefix} {playerAlias} detected Invulnerable ended after nuke cycle {nukeCycle}.");

                    if (nukeCycle >= GramielNukeCount)
                    {
                        LoneWolf.SetSkillEngineSkills(normalSkills);
                        detectorState = PhaseTwoDetectorState.Finished;
                        Core.Logger($"{LogPrefix} {playerAlias} completed all three Gramiel nuke cycles.");
                        break;
                    }

                    nukeCycle++;
                    nextAttack = 1;
                    LoneWolf.SetSkillEngineSkills(normalSkills);
                    rotationRestricted = false;

                    if (!StartPhaseTwoAttackDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.Attacks;
                    Core.Logger($"{LogPrefix} {playerAlias} armed Gramiel nuke cycle {nukeCycle}.");
                    break;

                case PhaseTwoDetectorState.Finished:
                    break;
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishPhaseTwo(PhaseResult.Stopped);
    }

    private bool StartPhaseTwoAttackDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, AttackAnimationMarker },
            "Attack2/Attack3"
        );

    private bool StartLiberatorPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { LiberatorMarker },
            "Liberator"
        );

    private bool StartChargeTwoPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, ChargeTwoAnimationMarker },
            "Charge2"
        );

    private bool StartChargeAttackTwoPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, ChargeAttackTwoAnimationMarker },
            "ChargeAttack2"
        );

    private bool StartPhaseTwoPacketDetector(
        string[] commands,
        string[] markers,
        string detectorName
    )
    {
        if (LoneWolf.StartPacketDetector(commands, markers))
            return true;

        Core.Logger(
            $"The Phase 2 {detectorName} packet detector could not be started.",
            "FightPhaseTwo",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private void LogGramielProtection(int nukeCycle)
    {
        Core.Logger(
            Bot.Self.HasActiveAura(InvulnerableAura)
                ? $"{LogPrefix} {playerAlias} entered nuke cycle {nukeCycle} protected by Invulnerable."
                : $"{LogPrefix} {playerAlias} entered nuke cycle {nukeCycle} without Invulnerable."
        );
    }

    private int[] GetPhaseTwoSkills(ClassPreset preset) =>
        LoneWolf.IsArmyPlayer(1)
        && armyComposition == ArmyComposition.Default
            ? new[] { 3, 4, 2, 1 }
            : preset.Skills;

    private bool ShouldUsePhaseTwoSlowdown()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                item.Equipped
                && !Core.NoneEnhancableFilter(item)
            )
                return Core.GetBoostFloat(item, "dmgAll") >= 1.40f;
        }

        return false;
    }

    private bool IsPhaseTwoSlowdownOwner() =>
        IsOptimizedShaman()
        || (
            armyComposition == ArmyComposition.Default
            && (
                LoneWolf.IsArmyPlayer(1)
                || LoneWolf.IsArmyPlayer(3)
            )
        );

    private int GetPhaseTwoTauntAttack()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return 1;

        if (LoneWolf.IsArmyPlayer(2))
            return 3;

        if (LoneWolf.IsArmyPlayer(3))
            return 5;

        return 7;
    }

    private PhaseResult FinishPhaseTwo(PhaseResult result)
    {
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        return Bot.ShouldExit ? PhaseResult.Stopped : result;
    }

    private void HandleCrystalDetections(
        bool bothCrystalsAlive,
        ref int nextDetection,
        ref DateTimeOffset tauntTargetUntil
    )
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
        {
            int cycle = nextDetection++;
            if (!bothCrystalsAlive || !OwnsTauntCycle(cycle))
                continue;

            int crystalMapId = GetAssignedCrystalMapId();
            LoneWolf.MaintainTarget(crystalMapId);
            LoneWolf.RequestTaunt(crystalMapId);

            if (
                LoneWolf.IsArmyPlayer(2)
                || LoneWolf.IsArmyPlayer(3)
                || LoneWolf.IsArmyPlayer(4)
                || IsOptimizedShaman()
            )
                tauntTargetUntil = DateTimeOffset.Now.AddMilliseconds(
                    TauntTargetHold
                );

            Core.Logger($"{LogPrefix} {playerAlias} requested crystal taunt cycle {cycle} on MapID {crystalMapId}.");
        }
    }

    private void MaintainPhaseOneShamanSkillFour(
        bool bothCrystalsAlive,
        bool safeguardActive,
        DateTimeOffset tauntTargetUntil,
        ref bool openingSkillFourQueued,
        ref bool safeguardSkillFourQueued
    )
    {
        if (!IsOptimizedShaman())
            return;

        if (!safeguardActive)
            safeguardSkillFourQueued = false;

        if (
            !bothCrystalsAlive
            || DateTimeOffset.Now < tauntTargetUntil
            || LoneWolf.HasPendingTargetedPrioritySkill()
            || !Bot.Skills.CanUseSkill(4)
        )
            return;

        bool openingCast = !openingSkillFourQueued;
        bool safeguardCast = openingSkillFourQueued
            && safeguardActive
            && !safeguardSkillFourQueued;
        if (!openingCast && !safeguardCast)
            return;

        if (
            !LoneWolf.RequestTargetedPrioritySkill(
                4,
                GramielMapId,
                GramielMapId
            )
        )
            return;

        if (openingCast)
            openingSkillFourQueued = true;
        else
            safeguardSkillFourQueued = true;

        Core.Logger(
            openingCast
                ? $"{LogPrefix} playerOne queued opening Phase 1 skill 4 on Gramiel."
                : $"{LogPrefix} playerOne queued Safeguard Phase 1 skill 4 on Gramiel."
        );
    }

    private void MaintainPhaseOneTarget(
        int crystalAHealth,
        int crystalBHealth,
        ref bool crystalBalancing,
        ref bool safeguardActive,
        DateTimeOffset tauntTargetUntil,
        ref int emergencyTarget
    )
    {
        if (crystalAHealth <= 0 || crystalBHealth <= 0)
        {
            int survivingCrystal = crystalAHealth > 0
                ? CrystalAMapId
                : CrystalBMapId;

            if (emergencyTarget != survivingCrystal)
            {
                emergencyTarget = survivingCrystal;
                Core.Logger($"{LogPrefix} {playerAlias} focusing surviving Crystal MapID {survivingCrystal}.");
            }

            crystalBalancing = false;
            safeguardActive = false;
            LoneWolf.MaintainTarget(survivingCrystal);
            return;
        }

        emergencyTarget = 0;
        int assignedCrystal = GetAssignedCrystalMapId();
        bool safeguardAttacker = armyComposition == ArmyComposition.Default
            ? LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(3)
            : true;

        if (safeguardAttacker)
        {
            bool currentSafeguard = Bot.Target
                .GetMonsterAura(GramielMapId)
                .Contains(
                    SafeguardAuraMarker,
                    StringComparison.Ordinal
                );

            if (currentSafeguard != safeguardActive)
            {
                safeguardActive = currentSafeguard;
                Core.Logger(
                    currentSafeguard
                        ? $"{LogPrefix} {playerAlias} detected Safeguard on Gramiel."
                        : $"{LogPrefix} {playerAlias} detected Safeguard ended."
                );
            }

            if (DateTimeOffset.Now < tauntTargetUntil || currentSafeguard)
            {
                LoneWolf.MaintainTarget(
                    DateTimeOffset.Now < tauntTargetUntil
                        ? assignedCrystal
                        : GramielMapId
                );
                return;
            }
        }

        bool crystalBalancer = armyComposition == ArmyComposition.Default
            ? LoneWolf.IsArmyPlayer(4)
            : LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(2);
        if (!crystalBalancer)
        {
            LoneWolf.MaintainTarget(assignedCrystal);
            return;
        }

        if (DateTimeOffset.Now < tauntTargetUntil)
        {
            LoneWolf.MaintainTarget(assignedCrystal);
            return;
        }

        int difference = Math.Abs(crystalAHealth - crystalBHealth);
        if (!crystalBalancing && difference > BalanceStartDifference)
        {
            crystalBalancing = true;
            Core.Logger($"{LogPrefix} {playerAlias} started crystal balancing at {crystalAHealth}/{crystalBHealth} HP.");
        }

        if (!crystalBalancing)
        {
            LoneWolf.MaintainTarget(assignedCrystal);
            return;
        }

        if (difference <= BalanceStopDifference)
        {
            crystalBalancing = false;
            Core.Logger($"{LogPrefix} {playerAlias} finished crystal balancing at {crystalAHealth}/{crystalBHealth} HP.");
            LoneWolf.MaintainTarget(assignedCrystal);
            return;
        }

        LoneWolf.MaintainTarget(
            crystalAHealth >= crystalBHealth
                ? CrystalAMapId
                : CrystalBMapId
        );
    }

    private bool OwnsTauntCycle(int cycle)
    {
        bool openingOwner = LoneWolf.IsArmyPlayer(1)
            || LoneWolf.IsArmyPlayer(3);
        return openingOwner ? cycle % 2 == 1 : cycle % 2 == 0;
    }

    private int GetAssignedCrystalMapId() =>
        LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(2)
            ? CrystalAMapId
            : CrystalBMapId;

    private void AdvanceDetections(ref int nextDetection)
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
            nextDetection++;
    }

    private PhaseResult FinishPhaseOne(PhaseResult result)
    {
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        return Bot.ShouldExit ? PhaseResult.Stopped : result;
    }

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
                $"{LogPrefix} fight failed after {MaxFightAttempts} attempts.",
                "RunFightAttempts"
            );
            return;
        }

        if (LoneWolf.IsArmyPlayer(1))
            LoneWolf.StopArmySync("ATTEMPTS_EXHAUSTED");
        else
            LoneWolf.SyncArmy("STOP_CHECK");

        Core.Logger(
            $"{LogPrefix} fight failed after {MaxFightAttempts} attempts.",
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

    private bool IsInFightRoom() =>
        Bot.Player.Cell == FightCell && Bot.Player.Pad == FightPad;

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Optimized)
            {
                ClassPreset shaman = LoneWolf.Shaman();
                shaman.BaseEnhancement = EnhancementType.Wizard;
                shaman.WeaponEnhancement = WeaponSpecial.Elysium;
                shaman.HelmEnhancement = HelmSpecial.None;
                shaman.CapeEnhancement = CapeSpecial.Absolution;
                shaman.Tonic = "Sage Tonic";
                shaman.Elixir = "Potent Malevolence Elixir";
                return shaman;
            }

            ClassPreset preset = LoneWolf.LegionRevenant();
            preset.Skills = new[] { 3, 4, 2 };
            preset.CapeEnhancement = CapeSpecial.Penitence;
            return preset;
        }

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
        {
            ClassPreset preset = LoneWolf.ArchPaladin();
            preset.CapeEnhancement = CapeSpecial.Penitence;
            return preset;
        }

        ClassPreset lordOfOrder = LoneWolf.LordOfOrder();
        lordOfOrder.WeaponEnhancement = WeaponSpecial.Valiance;
        return lordOfOrder;
    }

    private bool IsOptimizedShaman() =>
        armyComposition == ArmyComposition.Optimized
        && LoneWolf.IsArmyPlayer(1);

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
