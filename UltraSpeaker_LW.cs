/*
name: Ultra Speaker LW
description: Four-player Ultra Speaker Army script using CoreLoneWolf.
tags: ultra, speaker, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraSpeaker_LW
{
    private enum FightResult
    {
        Defeated,
        Reset,
        Stopped,
    }

    public enum ArmyComposition
    {
        Default,
        Stable,
        Pay2Win,
        Test,
        Test2,
        Test3,
    }

    private sealed class SpeakerStep
    {
        public string Warning { get; }
        public int Owner { get; }
        public bool Fresh { get; }
        public int SkillOwner { get; }
        public int Skill { get; }

        public SpeakerStep(
            string warning,
            int owner,
            bool fresh,
            int skillOwner = 0,
            int skill = 0
        )
        {
            Warning = warning;
            Owner = owner;
            Fresh = fresh;
            SkillOwner = skillOwner;
            Skill = skill;
        }
    }

    private sealed class ZoneState
    {
        public bool Moving;
        public int TargetX;
        public int TargetY;
        public string ArrivalSignal = string.Empty;
        public DateTimeOffset ConfirmMovementAt;
        public int WaitingForSanctityCycle;
        public int WaitingForClearCycle;
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Ultra Speaker LW";
    private const string SyncFileName = "UltraSpeaker_LW.sync";
    private const string MapName = "ultraspeaker";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string SanctityAura = "Sanctity";
    private const string RighteousSealAura = "Righteous Seal";
    private const string TruthMessage = "I will make you see the truth.";
    private const string ListenMessage = "You shall listen.";
    private const int UltraQuestId = 9173;
    private const int PrerequisiteQuestId = 9125;
    private const string PrerequisiteQuestName = "Your Hero";
    private const int MinimumLevel = 90;
    private const int SpeakerMapId = 1;
    private const int SafeX = 890;
    private const int SafeY = 395;
    private const int RedX = 731;
    private const int RedY = 380;
    private const int CoordinateTolerance = 20;
    private const int WalkSpeed = 8;
    private const int MovementRetryDelay = 500;
    private const int RighteousSealSkillFourWindow = 1000;
    private const int FightPollDelay = 100;
    private const int MaxFightAttempts = 3;

    private static readonly SpeakerStep[] OpeningSteps =
    {
        new(TruthMessage, 1, fresh: true),
        new(ListenMessage, 4, fresh: true),
    };

    private static readonly SpeakerStep[] SectionOneSteps =
    {
        new(TruthMessage, 3, fresh: true, skillOwner: 3, skill: 3),
        new(ListenMessage, 2, fresh: true),
        new(TruthMessage, 2, fresh: false, skillOwner: 4, skill: 4),
    };

    private static readonly SpeakerStep[] SectionTwoSteps =
    {
        new(ListenMessage, 1, fresh: true),
        new(TruthMessage, 1, fresh: false, skillOwner: 3, skill: 3),
        new(TruthMessage, 4, fresh: true),
        new(ListenMessage, 4, fresh: false),
    };

    private static readonly SpeakerStep[] Pay2WinOpeningSteps =
    {
        new(TruthMessage, 3, fresh: true),
        new(ListenMessage, 2, fresh: true),
    };

    private static readonly SpeakerStep[] Pay2WinSectionOneSteps =
    {
        new(TruthMessage, 4, fresh: true, skillOwner: 4, skill: 3),
        new(ListenMessage, 2, fresh: true),
        new(TruthMessage, 2, fresh: false),
    };

    private static readonly SpeakerStep[] Pay2WinSectionTwoSteps =
    {
        new(ListenMessage, 3, fresh: true),
        new(TruthMessage, 3, fresh: false, skillOwner: 4, skill: 3),
        new(TruthMessage, 2, fresh: true),
        new(ListenMessage, 2, fresh: false),
    };

    private static readonly SpeakerStep[] Pay2WinSectionThreeSteps =
    {
        new(TruthMessage, 4, fresh: true, skillOwner: 4, skill: 3),
        new(ListenMessage, 3, fresh: true),
        new(TruthMessage, 3, fresh: false),
    };

    private static readonly SpeakerStep[] Pay2WinSectionFourSteps =
    {
        new(ListenMessage, 2, fresh: true),
        new(TruthMessage, 2, fresh: false, skillOwner: 4, skill: 3),
        new(TruthMessage, 3, fresh: true),
        new(ListenMessage, 2, fresh: true),
    };

    private static readonly SpeakerStep[] Test2OpeningSteps =
    {
        new(TruthMessage, 4, fresh: true),
        new(ListenMessage, 4, fresh: false),
    };

    private static readonly SpeakerStep[] Test2SectionOneSteps =
    {
        new(TruthMessage, 2, fresh: true, skillOwner: 2, skill: 3),
        new(ListenMessage, 3, fresh: true),
        new(TruthMessage, 4, fresh: true),
    };

    private static readonly SpeakerStep[] Test2SectionTwoSteps =
    {
        new(ListenMessage, 3, fresh: true),
        new(TruthMessage, 3, fresh: false, skillOwner: 2, skill: 3),
        new(TruthMessage, 4, fresh: true, skillOwner: 4, skill: 4),
        new(ListenMessage, 4, fresh: false),
    };

    private static readonly SpeakerStep[] Test2SectionThreeSteps =
    {
        new(TruthMessage, 2, fresh: true, skillOwner: 2, skill: 3),
        new(ListenMessage, 3, fresh: true),
        new(TruthMessage, 3, fresh: false),
    };

    private static readonly SpeakerStep[] Test2SectionFourSteps =
    {
        new(ListenMessage, 3, fresh: true, skillOwner: 2, skill: 3),
        new(TruthMessage, 3, fresh: false, skillOwner: 4, skill: 4),
        new(TruthMessage, 4, fresh: true),
        new(ListenMessage, 4, fresh: false),
    };

    private static readonly int[] ZoneOwners = { 2, 1, 3, 4 };
    private static readonly int[] Pay2WinZoneOwners = { 1, 3, 4, 2 };
    private static readonly int[] Test2ZoneOwners = { 1, 3, 2, 4 };

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition = ArmyComposition.Default;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private bool bruteForceMethod;
    private int privateRoomNumber;
    private int equalizeDetectionCount;

    public string OptionsStorage = "UltraSpeaker_LW";
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
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO\nPay2Win: Guardian / SC / LR / AP\nTest: LR / SC / AP / LOO\nTest2: CSS / AP / LR / LOO\nTest3: LR / SC / AP / LOO",
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
        new Option<bool>(
            "BruteForceMethod",
            "Brute Force Method",
            "Keep every player in the safe zone and ignore Equalize movement.",
            false
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
            StopFightSystems();
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
            StopFightSystems();
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

        if (IsTaunter())
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

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            if (!StartFightSystems(preset))
                return false;

            FightResult result;
            try
            {
                Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                result = Fight(fightAttempt);
            }
            finally
            {
                StopFightSystems();
            }

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
            "UltraSpeakerComposition",
            "ArmyComposition"
        );
        bruteForceMethod = GetUltraOption<bool>(
            "UltraSpeakerBruteForceMethod",
            "BruteForceMethod"
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
        {
            if (preset.ClassName == "StoneCrusher")
                preset.Elixir = LoneWolf.GetDivineElixir();

            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        if (IsTaunter())
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

        if (IsTaunter())
            LoneWolf.EquipScroll(EnrageScroll);

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool StartFightSystems(ClassPreset preset)
    {
        if (
            !LoneWolf.StartPacketChoiceDetector(
                "ct",
                new[] { TruthMessage, ListenMessage },
                pauseSkillChoice: ListenMessage,
                pauseEveryChoice: armyComposition == ArmyComposition.Test3
            )
        )
        {
            Core.Logger(
                "Speaker packet detector could not be started.",
                "StartFightSystems",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        Interlocked.Exchange(ref equalizeDetectionCount, 0);
        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;

        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            IsTaunter(),
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: GetSetupOption<bool>("UsePotions")
                ? preset.CombatPotion
                : null
        );
        return true;
    }

    private void StopFightSystems()
    {
        Bot.Events.RunToArea -= OnRunToArea;
        Interlocked.Exchange(ref equalizeDetectionCount, 0);
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
    }

    private void OnRunToArea(string zone)
    {
        if (string.Equals(zone, "A", StringComparison.Ordinal))
            Interlocked.Increment(ref equalizeDetectionCount);
    }

    private FightResult Fight(int fightAttempt)
    {
        int currentSection = 0;
        int currentStep = 0;
        int nextWarningDetection = 1;
        int nextEqualizeDetection = 1;
        int equalizeCycle = 0;
        bool bossObserved = false;
        bool playerWasDead = false;
        bool localChartInvalid = false;
        bool chartFailureSent = false;
        bool chartResetSent = false;
        int nextChartFailureSender = 1;
        bool righteousSealSkillFourQueued = false;
        int nextTestHealClearCycle = 1;
        ZoneState zoneState = new();

        StartInitialMovement(zoneState);

        if (
            armyComposition == ArmyComposition.Test
            && !bruteForceMethod
            && LoneWolf.IsArmyPlayer(GetArchPaladinPlayer())
        )
        {
            LoneWolf.RequestLimitedPrioritySkill(2, 2);
            Core.Logger(
                $"{LogPrefix} {playerAlias} started with 2 unlocked heals."
            );
        }

        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        while (!Bot.ShouldExit)
        {
            if (IsInBossRoom())
            {
                if (LoneWolf.IsMonsterAlive(SpeakerMapId))
                    bossObserved = true;
                else if (bossObserved)
                    return FightResult.Defeated;
            }

            if (
                !bruteForceMethod
                && LoneWolf.ShouldResetFight(fightAttempt)
            )
                return FightResult.Reset;

            if (!localChartInvalid)
            {
                bool warningChartValid = ProcessWarningDetections(
                    ref currentSection,
                    ref currentStep,
                    ref nextWarningDetection,
                    ref righteousSealSkillFourQueued,
                    out string warningMismatch
                );

                if (
                    !warningChartValid
                    && !ReportChartFailure(
                        fightAttempt,
                        warningMismatch,
                        ref localChartInvalid,
                        ref chartFailureSent
                    )
                )
                    return FightResult.Stopped;

                if (
                    !localChartInvalid
                    && !ProcessEqualizeDetections(
                        fightAttempt,
                        zoneState,
                        ref currentSection,
                        ref currentStep,
                        ref nextEqualizeDetection,
                        ref equalizeCycle,
                        out string equalizeMismatch
                    )
                    && !ReportChartFailure(
                        fightAttempt,
                        equalizeMismatch,
                        ref localChartInvalid,
                        ref chartFailureSent
                    )
                )
                    return FightResult.Stopped;
            }

            if (
                !PublishChartResetIfRequested(
                    fightAttempt,
                    ref chartResetSent,
                    ref nextChartFailureSender
                )
            )
                return FightResult.Stopped;

            if (HasChartResetSignal(fightAttempt))
                return FightResult.Reset;

            ProcessTestHealClearSignals(
                fightAttempt,
                ref nextTestHealClearCycle
            );

            if (!Bot.Player.Alive)
            {
                if (!playerWasDead)
                {
                    playerWasDead = true;
                    Core.Logger($"{LogPrefix} {playerAlias} died.");
                }

                Bot.Sleep(FightPollDelay);
                continue;
            }

            if (playerWasDead)
            {
                playerWasDead = false;
                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (!IsInBossRoom())
                    Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit)
                    break;

                RestoreZonePositionAfterDeath(
                    zoneState,
                    fightAttempt,
                    equalizeCycle
                );
            }

            if (!ProcessZoneState(zoneState, fightAttempt))
                return FightResult.Stopped;

            if (IsInBossRoom())
            {
                LoneWolf.MaintainTarget(SpeakerMapId);

                if (!bruteForceMethod)
                {
                    MaintainArchPaladinRighteousSeal(
                        ref righteousSealSkillFourQueued
                    );
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private bool ProcessWarningDetections(
        ref int currentSection,
        ref int currentStep,
        ref int nextDetection,
        ref bool righteousSealSkillFourQueued,
        out string mismatch
    )
    {
        mismatch = string.Empty;

        while (LoneWolf.HasPacketDetection(nextDetection))
        {
            string detectedWarning = LoneWolf.GetPacketDetectorChoice();
            SpeakerStep[] steps = GetSectionSteps(currentSection);

            if (currentStep >= steps.Length)
            {
                mismatch = $"Unexpected {GetWarningName(detectedWarning)} before the next Equalize.";
                return false;
            }

            SpeakerStep expected = steps[currentStep];
            if (!string.Equals(
                detectedWarning,
                expected.Warning,
                StringComparison.Ordinal
            ))
            {
                mismatch = $"Expected {GetWarningName(expected.Warning)} but detected {GetWarningName(detectedWarning)}.";
                return false;
            }

            nextDetection++;
            currentStep++;

            bool listenDetected = string.Equals(
                expected.Warning,
                ListenMessage,
                StringComparison.Ordinal
            );
            bool absolutePriorityWarning = listenDetected
                || (
                    armyComposition == ArmyComposition.Test3
                    && string.Equals(
                        expected.Warning,
                        TruthMessage,
                        StringComparison.Ordinal
                    )
                );
            bool localFreshOwner = expected.Fresh
                && LoneWolf.IsArmyPlayer(expected.Owner);

            if (localFreshOwner)
            {
                if (absolutePriorityWarning)
                    LoneWolf.RequestAbsolutePriorityTaunt(SpeakerMapId);
                else
                    LoneWolf.RequestTaunt(SpeakerMapId);

                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested Fresh {GetWarningName(expected.Warning)} taunt."
                );
            }
            else if (!expected.Fresh)
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} processed Covered {GetWarningName(expected.Warning)}."
                );
            }

            if (absolutePriorityWarning && !localFreshOwner)
                LoneWolf.ResumeSkillEngine();

            if (
                !bruteForceMethod
                && expected.Skill > 0
                && LoneWolf.IsArmyPlayer(expected.SkillOwner)
            )
            {
                LoneWolf.RequestPrioritySkill(expected.Skill);

                if (
                    expected.SkillOwner == GetArchPaladinPlayer()
                    && expected.Skill == 3
                )
                    righteousSealSkillFourQueued = false;

                Core.Logger(
                    $"{LogPrefix} {playerAlias} queued chart skill {expected.Skill}."
                );
            }
        }

        return true;
    }

    private bool ProcessEqualizeDetections(
        int fightAttempt,
        ZoneState zoneState,
        ref int currentSection,
        ref int currentStep,
        ref int nextDetection,
        ref int equalizeCycle,
        out string mismatch
    )
    {
        mismatch = string.Empty;

        while (Volatile.Read(ref equalizeDetectionCount) >= nextDetection)
        {
            if (currentStep != GetSectionStepCount(currentSection))
            {
                mismatch = $"Equalize arrived before {GetPortionName(currentSection)} completed.";
                return false;
            }

            int cycle = nextDetection++;
            int owner = GetZoneOwner(cycle);
            if (!bruteForceMethod)
            {
                if (
                    LoneWolf.IsArmyPlayer(owner)
                    && !IsAtCoordinate(RedX, RedY)
                )
                {
                    mismatch = $"Equalize cycle {cycle} owner was not in the red zone.";
                    return false;
                }
            }

            equalizeCycle = cycle;
            currentSection = ((cycle - 1) % 4) + 1;
            currentStep = 0;

            if (!bruteForceMethod)
            {
                if (LoneWolf.IsArmyPlayer(owner))
                    zoneState.WaitingForSanctityCycle = cycle;

                int nextOwner = GetZoneOwner(cycle + 1);
                if (LoneWolf.IsArmyPlayer(nextOwner))
                    zoneState.WaitingForClearCycle = cycle;
            }

            Core.Logger(
                $"{LogPrefix} {playerAlias} entered Equalize cycle {cycle} section {currentSection}."
            );
        }

        return true;
    }

    private bool ProcessZoneState(ZoneState state, int fightAttempt)
    {
        if (!Bot.Player.Alive || !IsInBossRoom())
            return true;

        if (state.Moving)
        {
            if (DateTimeOffset.Now < state.ConfirmMovementAt)
                return true;

            if (!IsAtCoordinate(state.TargetX, state.TargetY))
            {
                IssueMovement(state);
                return true;
            }

            state.Moving = false;
            if (state.ArrivalSignal.Length == 0)
                return true;

            string signal = state.ArrivalSignal;
            state.ArrivalSignal = string.Empty;
            if (!LoneWolf.SendArmySignal(signal))
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} could not send {signal}.",
                    "ProcessZoneState",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

            Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
            return true;
        }

        if (bruteForceMethod)
            return true;

        if (state.WaitingForSanctityCycle > 0)
        {
            if (Bot.Self.GetAura(SanctityAura) == null)
                return true;

            int cycle = state.WaitingForSanctityCycle;
            state.WaitingForSanctityCycle = 0;
            StartMovement(
                state,
                SafeX,
                SafeY,
                GetZoneClearSignal(fightAttempt, cycle)
            );
            Core.Logger(
                $"{LogPrefix} {playerAlias} resolved Sanctity cycle {cycle} and moved safe."
            );
            return true;
        }

        if (state.WaitingForClearCycle > 0)
        {
            int cycle = state.WaitingForClearCycle;
            int owner = GetZoneOwner(cycle);
            string clearSignal = GetZoneClearSignal(fightAttempt, cycle);
            if (!LoneWolf.HasArmySignal(clearSignal, owner))
                return true;

            state.WaitingForClearCycle = 0;
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
            Core.Logger(
                $"{LogPrefix} {playerAlias} received {clearSignal} and moved into the red zone."
            );
        }

        return true;
    }

    private void ProcessTestHealClearSignals(
        int fightAttempt,
        ref int nextClearCycle
    )
    {
        if (
            armyComposition != ArmyComposition.Test
            || bruteForceMethod
            || !LoneWolf.IsArmyPlayer(GetArchPaladinPlayer())
        )
            return;

        while (true)
        {
            int owner = GetZoneOwner(nextClearCycle);
            string clearSignal = GetZoneClearSignal(
                fightAttempt,
                nextClearCycle
            );
            if (!LoneWolf.HasArmySignal(clearSignal, owner))
                return;

            LoneWolf.RequestLimitedPrioritySkill(2, 2);
            Core.Logger(
                $"{LogPrefix} {playerAlias} unlocked 2 heals from CLEAR cycle {nextClearCycle}."
            );
            nextClearCycle++;
        }
    }

    private void StartInitialMovement(ZoneState state)
    {
        if (bruteForceMethod)
            StartMovement(state, SafeX, SafeY, string.Empty);
        else if (LoneWolf.IsArmyPlayer(GetZoneOwner(1)))
            StartMovement(state, RedX, RedY, string.Empty);
        else
            StartMovement(state, SafeX, SafeY, string.Empty);
    }

    private void RestoreZonePositionAfterDeath(
        ZoneState state,
        int fightAttempt,
        int equalizeCycle
    )
    {
        state.Moving = false;
        state.ArrivalSignal = string.Empty;
        state.ConfirmMovementAt = DateTimeOffset.MinValue;
        state.WaitingForSanctityCycle = 0;
        state.WaitingForClearCycle = 0;

        if (bruteForceMethod)
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            return;
        }

        int nextCycle = equalizeCycle + 1;
        if (!LoneWolf.IsArmyPlayer(GetZoneOwner(nextCycle)))
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            return;
        }

        if (equalizeCycle == 0)
        {
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
            return;
        }

        string clearSignal = GetZoneClearSignal(fightAttempt, equalizeCycle);
        int clearOwner = GetZoneOwner(equalizeCycle);
        if (LoneWolf.HasArmySignal(clearSignal, clearOwner))
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
        else
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            state.WaitingForClearCycle = equalizeCycle;
        }
    }

    private void StartMovement(
        ZoneState state,
        int x,
        int y,
        string arrivalSignal
    )
    {
        state.Moving = true;
        state.TargetX = x;
        state.TargetY = y;
        state.ArrivalSignal = arrivalSignal;
        IssueMovement(state);
    }

    private void IssueMovement(ZoneState state)
    {
        Bot.Flash.Call(
            "walkTo",
            state.TargetX,
            state.TargetY,
            WalkSpeed
        );
        state.ConfirmMovementAt = DateTimeOffset.Now.AddMilliseconds(
            MovementRetryDelay
        );
    }

    private bool ReportChartFailure(
        int fightAttempt,
        string reason,
        ref bool localChartInvalid,
        ref bool chartFailureSent
    )
    {
        localChartInvalid = true;
        Core.Logger($"{LogPrefix} {playerAlias} chart mismatch: {reason}");

        if (chartFailureSent)
            return true;

        string signal = GetChartFailureSignal(fightAttempt);
        if (!LoneWolf.SendArmySignal(signal))
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not send {signal}.",
                "ReportChartFailure",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        chartFailureSent = true;
        Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
        return true;
    }

    private bool PublishChartResetIfRequested(
        int fightAttempt,
        ref bool chartResetSent,
        ref int nextFailureSender
    )
    {
        if (!LoneWolf.IsArmyPlayer(1) || chartResetSent)
            return true;

        string failureSignal = GetChartFailureSignal(fightAttempt);
        bool failureReported = LoneWolf.HasArmySignal(
            failureSignal,
            nextFailureSender
        );
        nextFailureSender = nextFailureSender % 4 + 1;
        if (!failureReported)
            return true;

        string signal = GetChartResetSignal(fightAttempt);
        if (!LoneWolf.SendArmySignal(signal))
        {
            Core.Logger(
                $"{LogPrefix} playerOne could not send {signal}.",
                "PublishChartResetIfRequested",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        chartResetSent = true;
        Core.Logger($"{LogPrefix} playerOne sent {signal}.");
        return true;
    }

    private bool HasChartResetSignal(int fightAttempt) =>
        LoneWolf.HasArmySignal(GetChartResetSignal(fightAttempt), 1);

    private void MaintainArchPaladinRighteousSeal(
        ref bool skillFourQueued
    )
    {
        if (!LoneWolf.IsArmyPlayer(GetArchPaladinPlayer()))
            return;

        var righteousSeal = Bot.Target.GetAura(RighteousSealAura);
        if (righteousSeal == null)
        {
            skillFourQueued = false;
            return;
        }

        if (skillFourQueued || LoneWolf.HasPendingPrioritySkill())
            return;

        TimeSpan remaining = righteousSeal.ExpiresAt - DateTimeOffset.Now;
        if (
            remaining <= TimeSpan.Zero
            || remaining
                > TimeSpan.FromMilliseconds(RighteousSealSkillFourWindow)
        )
            return;

        LoneWolf.RequestPrioritySkill(4);
        skillFourQueued = true;
        Core.Logger(
            $"{LogPrefix} {playerAlias} queued skill 4 for Righteous Seal."
        );
    }

    private bool HandleFightReset(int fightAttempt)
    {
        StopFightSystems();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightAttempt);
            Bot.Sleep(FightPollDelay);
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

    private SpeakerStep[] GetSectionSteps(int section)
    {
        if (armyComposition == ArmyComposition.Pay2Win)
        {
            return section switch
            {
                0 => Pay2WinOpeningSteps,
                1 => Pay2WinSectionOneSteps,
                2 => Pay2WinSectionTwoSteps,
                3 => Pay2WinSectionThreeSteps,
                4 => Pay2WinSectionFourSteps,
                _ => Array.Empty<SpeakerStep>(),
            };
        }

        if (armyComposition == ArmyComposition.Test2)
        {
            return section switch
            {
                0 => Test2OpeningSteps,
                1 => Test2SectionOneSteps,
                2 => Test2SectionTwoSteps,
                3 => Test2SectionThreeSteps,
                4 => Test2SectionFourSteps,
                _ => Array.Empty<SpeakerStep>(),
            };
        }

        return section switch
        {
            0 => OpeningSteps,
            1 or 3 => SectionOneSteps,
            2 or 4 => SectionTwoSteps,
            _ => Array.Empty<SpeakerStep>(),
        };
    }

    private int GetSectionStepCount(int section) =>
        GetSectionSteps(section).Length;

    private int GetZoneOwner(int cycle)
    {
        int[] zoneOwners = armyComposition switch
        {
            ArmyComposition.Pay2Win => Pay2WinZoneOwners,
            ArmyComposition.Test2 => Test2ZoneOwners,
            _ => ZoneOwners,
        };
        return zoneOwners[(cycle - 1) % zoneOwners.Length];
    }

    private int GetArchPaladinPlayer() => armyComposition switch
    {
        ArmyComposition.Pay2Win => 4,
        ArmyComposition.Test2 => 2,
        _ => 3,
    };

    private bool IsTaunter() =>
        (
            armyComposition != ArmyComposition.Pay2Win
            && armyComposition != ArmyComposition.Test2
        )
        || !LoneWolf.IsArmyPlayer(1);

    private bool IsAtCoordinate(int x, int y) =>
        Math.Abs(Bot.Player.X - x) <= CoordinateTolerance
        && Math.Abs(Bot.Player.Y - y) <= CoordinateTolerance;

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private string GetWarningName(string warning) =>
        string.Equals(warning, TruthMessage, StringComparison.Ordinal)
            ? "Truth"
            : string.Equals(warning, ListenMessage, StringComparison.Ordinal)
                ? "Listen"
                : "Unknown";

    private string GetPortionName(int section) =>
        section == 0 ? "Opening" : $"Section {section}";

    private string GetZoneClearSignal(int fightAttempt, int cycle) =>
        $"SPEAKER_ZONE_{fightAttempt}_{cycle}_CLEAR";

    private string GetChartFailureSignal(int fightAttempt) =>
        $"SPEAKER_CHART_FAILURE_{fightAttempt}";

    private string GetChartResetSignal(int fightAttempt) =>
        $"SPEAKER_CHART_RESET_{fightAttempt}";

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Pay2Win)
                return LoneWolf.Guardian();

            if (armyComposition == ArmyComposition.Test2)
            {
                ClassPreset chronoShadowHunter = LoneWolf.ChronoShadowHunter(
                    gunslingerMode: true
                );
                chronoShadowHunter.CapeEnhancement = CapeSpecial.Penitence;
                return chronoShadowHunter;
            }

            ClassPreset preset = armyComposition == ArmyComposition.Stable
                ? LoneWolf.VerusDoomKnight()
                : LoneWolf.LegionRevenant();
            preset.CapeEnhancement = CapeSpecial.Penitence;

            if (armyComposition != ArmyComposition.Stable)
                preset.HelmEnhancement = HelmSpecial.None;

            return preset;
        }

        if (LoneWolf.IsArmyPlayer(2))
        {
            if (armyComposition == ArmyComposition.Test2)
            {
                ClassPreset archPaladin = LoneWolf.ArchPaladin();
                archPaladin.CapeEnhancement = CapeSpecial.Penitence;

                if (!bruteForceMethod)
                    archPaladin.Skills = new[] { 2, 1 };

                return archPaladin;
            }

            ClassPreset preset = LoneWolf.StoneCrusher();
            preset.CapeEnhancement = CapeSpecial.Penitence;
            preset.Elixir = "Divine Elixir";
            return preset;
        }

        if (LoneWolf.IsArmyPlayer(3))
        {
            if (
                armyComposition == ArmyComposition.Pay2Win
                || armyComposition == ArmyComposition.Test2
            )
            {
                ClassPreset legionRevenant = LoneWolf.LegionRevenant();
                legionRevenant.CapeEnhancement = CapeSpecial.Penitence;
                legionRevenant.HelmEnhancement = HelmSpecial.None;
                return legionRevenant;
            }

            ClassPreset preset = LoneWolf.ArchPaladin();
            preset.CapeEnhancement = CapeSpecial.Penitence;

            if (!bruteForceMethod)
                preset.Skills = armyComposition == ArmyComposition.Test
                    ? new[] { 1 }
                    : new[] { 2, 1 };

            return preset;
        }

        if (armyComposition == ArmyComposition.Pay2Win)
        {
            ClassPreset archPaladin = LoneWolf.ArchPaladin();
            archPaladin.CapeEnhancement = CapeSpecial.Penitence;

            if (!bruteForceMethod)
                archPaladin.Skills = new[] { 2, 1 };

            return archPaladin;
        }

        ClassPreset lordOfOrder = LoneWolf.LordOfOrder();

        if (!bruteForceMethod)
            lordOfOrder.Skills = new[] { 2, 3, 1 };

        return lordOfOrder;
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
