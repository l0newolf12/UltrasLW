/*
name: Ultra Drago LW
description: Four-player CoreLoneWolf Army script for Ultra Drago.
tags: ultra, drago, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDrago_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
    }

    private enum FightResult
    {
        Continue,
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Ultra Drago LW";
    private const string SyncFileName = "UltraDrago_LW.sync";
    private const string MapName = "ultradrago";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string FocusAura = "Focus";
    private const string EntryTimestampName = "DRAGO_ENTRY_TIME";
    private const string AlgieTauntSignalPrefix = "ALGIE_TAUNT_";
    private const string DeneTauntSignalPrefix = "DENIE_TAUNT_";
    private const int UltraQuestId = 8397;
    private const int PrerequisiteQuestId = 8395;
    private const string PrerequisiteQuestName = "Mahapadma";
    private const int MinimumLevel = 80;
    private const int DeneMapId = 1;
    private const int DragoMapId = 2;
    private const int AlgieMapId = 3;
    private const int EntryLeadMilliseconds = 2000;
    private const int FocusRefreshMilliseconds = 1500;
    private const int TargetSettleDelay = 50;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int EntryTimeoutMilliseconds = 5000;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDrago_LW";
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
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
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
        isTaunter = armyComposition != ArmyComposition.Stable
            || !LoneWolf.IsArmyPlayer(1);

        if (isTaunter)
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

            LoneWolf.GenericPrebuff();
            if (Bot.ShouldExit)
                return false;

            long entryTimestamp = GetEntryTimestamp(fightAttempt);
            if (entryTimestamp <= 0)
                return false;

            int initialTargetMapId = GetInitialTarget();
            LoneWolf.MaintainTarget(initialTargetMapId);
            StartSkillEngine(preset);

            if (
                !WaitForEntryTimestamp(entryTimestamp)
                || !AggressiveMoveToBossRoom(initialTargetMapId)
            )
                return false;

            FightResult result = Fight(fightAttempt);
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
            "UltraDragoComposition",
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

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);
        return !Bot.ShouldExit;
    }

    private long GetEntryTimestamp(int fightAttempt)
    {
        string timestampName = $"{EntryTimestampName}_{fightAttempt}";
        if (LoneWolf.IsArmyPlayer(1))
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + EntryLeadMilliseconds;

            if (!LoneWolf.SendArmyTimestamp(timestampName, timestamp))
                return 0;

            Core.Logger($"{LogPrefix} playerOne published the entry timestamp.");
            return timestamp;
        }

        long entryTimestamp = 0;
        while (!Bot.ShouldExit && entryTimestamp <= 0)
        {
            entryTimestamp = LoneWolf.GetArmyTimestamp(timestampName, 1);
            if (entryTimestamp <= 0)
                Bot.Sleep(FightPollDelay);
        }

        if (entryTimestamp > 0)
            Core.Logger($"{LogPrefix} {playerAlias} received the entry timestamp.");

        return entryTimestamp;
    }

    private bool WaitForEntryTimestamp(long entryTimestamp)
    {
        while (!Bot.ShouldExit)
        {
            long remaining = entryTimestamp
                - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remaining <= 0)
                return true;

            Bot.Sleep((int)Math.Min(remaining, TargetSettleDelay));
        }

        return false;
    }

    private bool AggressiveMoveToBossRoom(int targetMapId)
    {
        if (IsInBossRoom())
            return true;

        Bot.Combat.Attack(targetMapId);
        Bot.Flash.Call("jumpCorrectRoom", BossCell, BossPad, false, false);

        if (WaitForBossRoom(targetMapId))
            return true;

        Bot.Map.Jump(BossCell, BossPad, autoCorrect: false);
        if (WaitForBossRoom(targetMapId))
            return true;

        Core.Logger(
            $"{LogPrefix} {playerAlias} could not enter the boss room.",
            "AggressiveMoveToBossRoom",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool WaitForBossRoom(int targetMapId)
    {
        DateTimeOffset timeout = DateTimeOffset.Now.AddMilliseconds(
            EntryTimeoutMilliseconds
        );

        while (!Bot.ShouldExit && DateTimeOffset.Now < timeout)
        {
            if (IsInBossRoom())
                return true;

            Bot.Combat.Attack(targetMapId);
            Bot.Sleep(FightPollDelay);
        }

        return IsInBossRoom();
    }

    private FightResult Fight(int fightAttempt)
    {
        if (armyComposition != ArmyComposition.Stable)
            return FightGuardPair(fightAttempt);

        if (LoneWolf.IsArmyPlayer(1))
            return FightDamageDealer(fightAttempt);

        if (LoneWolf.IsArmyPlayer(4))
            return FightImmediateAlgieTaunter(fightAttempt);

        return FightGuardPair(fightAttempt);
    }

    private FightResult FightDamageDealer(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            LoneWolf.MaintainTarget(GetNormalTarget());
            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightImmediateAlgieTaunter(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = LoneWolf.IsMonsterAlive(AlgieMapId)
                && LoneWolf.RequestImmediateTaunt(AlgieMapId);

            if (!immediateTauntAccepted)
                LoneWolf.MaintainTarget(GetNormalTarget());

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightGuardPair(int fightAttempt)
    {
        int assignedGuardMapId = GetAssignedGuardMapId();
        int partnerPlayerNumber = GetPartnerPlayerNumber();
        bool openingOwner = IsOpeningOwner();
        string signalPrefix = GetTauntSignalPrefix();
        string partnerName = (
            GetSetupOption<string>($"player{partnerPlayerNumber}")
        ).Trim();
        string partnerAlias = GetPlayerAlias(partnerPlayerNumber);

        bool pairClosed = false;
        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = openingOwner;
        bool partnerDeathObserved = false;
        int nextSignalNumber = 1;
        DateTimeOffset focusBaseline = DateTimeOffset.MinValue;
        DateTimeOffset nextFocusInspection = DateTimeOffset.MinValue;

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        if (openingOwner && LoneWolf.IsMonsterAlive(assignedGuardMapId))
        {
            focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
            Core.Logger($"{LogPrefix} {playerAlias} requested the first guard taunt.");
        }

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out bool recovered);
            if (result != FightResult.Continue)
                return FinishFight(result);

            if (!pairClosed && !LoneWolf.IsMonsterAlive(assignedGuardMapId))
            {
                pairClosed = true;
                ownsFocusCycle = false;
                waitingForOwnFocus = false;
                Core.Logger($"{LogPrefix} {playerAlias} closed its guard taunt loop.");
            }

            if (pairClosed)
            {
                LoneWolf.MaintainTarget(GetNormalTarget());
                Bot.Sleep(FightPollDelay);
                continue;
            }

            if (recovered)
            {
                nextSignalNumber = AlignSignalNumber(
                    nextSignalNumber,
                    openingOwner
                );
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next guard taunt after returning.");
            }

            if (!waitingForOwnFocus)
                LoneWolf.MaintainTarget(GetNormalTarget());

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignalName(
                    signalPrefix,
                    fightAttempt,
                    nextSignalNumber
                );
                if (
                    !ownsFocusCycle
                    && !waitingForOwnFocus
                    && LoneWolf.HasArmySignal(expectedSignal, partnerPlayerNumber)
                )
                    nextSignalNumber++;

                partnerDeathObserved = true;
                ownsFocusCycle = false;
                waitingForOwnFocus = false;
                Core.Logger($"{LogPrefix} {playerAlias} detected its taunt partner died.");
            }

            if (partnerDeathObserved)
            {
                if (partnerFound && !partnerDead && partnerInBossRoom)
                {
                    partnerDeathObserved = false;
                    nextSignalNumber = AlignSignalNumber(
                        nextSignalNumber,
                        !openingOwner
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating guard taunts.");
                }
                else
                {
                    LoneWolf.RequestImmediateTaunt(assignedGuardMapId);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            if (waitingForOwnFocus)
            {
                SelectGuard(assignedGuardMapId);
                var focus = Bot.Target.GetAura(FocusAura);

                if (focus != null && focus.ExpiresAt > focusBaseline)
                {
                    focusBaseline = focus.ExpiresAt;
                    nextFocusInspection = focus.ExpiresAt
                        - TimeSpan.FromMilliseconds(FocusRefreshMilliseconds);
                    waitingForOwnFocus = false;
                    ownsFocusCycle = true;
                    LoneWolf.MaintainTarget(GetNormalTarget());
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus and owns the taunt cycle.");
                }
                else if (focus == null)
                {
                    LoneWolf.RequestImmediateTaunt(assignedGuardMapId);
                }
            }
            else if (
                ownsFocusCycle
                && DateTimeOffset.Now >= nextFocusInspection
            )
            {
                SelectGuard(assignedGuardMapId);
                var focus = Bot.Target.GetAura(FocusAura);

                if (focus == null)
                {
                    ownsFocusCycle = false;
                    waitingForOwnFocus = true;
                    focusBaseline = DateTimeOffset.MinValue;
                    LoneWolf.RequestImmediateTaunt(assignedGuardMapId);
                }
                else if (
                    focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
                )
                {
                    string signal = GetTauntSignalName(
                        signalPrefix,
                        fightAttempt,
                        nextSignalNumber
                    );
                    if (LoneWolf.SendArmySignal(signal))
                    {
                        Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                        nextSignalNumber++;
                        ownsFocusCycle = false;
                        LoneWolf.MaintainTarget(GetNormalTarget());
                    }
                }
                else
                {
                    nextFocusInspection = focus.ExpiresAt
                        - TimeSpan.FromMilliseconds(FocusRefreshMilliseconds);
                    LoneWolf.MaintainTarget(GetNormalTarget());
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignalName(
                    signalPrefix,
                    fightAttempt,
                    nextSignalNumber
                );
                if (LoneWolf.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
                    waitingForOwnFocus = true;
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled guard taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FinishFight(FightResult result)
    {
        LoneWolf.StopSkillEngine();

        if (Bot.ShouldExit || result == FightResult.Stopped)
            return FightResult.Stopped;

        if (result == FightResult.Defeated)
            Core.Logger($"{LogPrefix} {playerAlias} confirmed King Drago defeated.");
        else if (result == FightResult.Reset)
            Core.Logger($"{LogPrefix} {playerAlias} received the coordinated fight reset.");

        return result;
    }

    private DateTimeOffset BeginTauntAcquisition(int guardMapId)
    {
        SelectGuard(guardMapId);
        DateTimeOffset baseline = GetFocusExpiry();
        LoneWolf.RequestTaunt(guardMapId);
        return baseline;
    }

    private void SelectGuard(int guardMapId)
    {
        var target = Bot.Player.Target;
        bool changed = !Bot.Player.HasTarget
            || target?.MapID != guardMapId
            || target?.HP <= 0;

        LoneWolf.MaintainTarget(guardMapId);
        if (changed)
            Bot.Sleep(TargetSettleDelay);
    }

    private int GetInitialTarget()
    {
        if (
            LoneWolf.IsArmyPlayer(3)
            || (
                armyComposition == ArmyComposition.Stable
                && LoneWolf.IsArmyPlayer(2)
            )
        )
            return DeneMapId;

        return AlgieMapId;
    }

    private int GetNormalTarget()
    {
        bool algieAlive = LoneWolf.IsMonsterAlive(AlgieMapId);
        bool deneAlive = LoneWolf.IsMonsterAlive(DeneMapId);

        if (algieAlive && deneAlive)
        {
            if (armyComposition == ArmyComposition.Stable)
                return LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(4)
                    ? AlgieMapId
                    : DeneMapId;

            return LoneWolf.IsArmyPlayer(3) ? DeneMapId : AlgieMapId;
        }

        if (algieAlive)
            return AlgieMapId;

        if (deneAlive)
            return DeneMapId;

        return DragoMapId;
    }

    private int GetAssignedGuardMapId() =>
        LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(4)
            ? AlgieMapId
            : DeneMapId;

    private int GetPartnerPlayerNumber()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return 4;

        if (LoneWolf.IsArmyPlayer(4))
            return 1;

        if (LoneWolf.IsArmyPlayer(3))
            return 2;

        return 3;
    }

    private bool IsOpeningOwner() =>
        LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(3);

    private string GetTauntSignalPrefix() =>
        LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(4)
            ? AlgieTauntSignalPrefix
            : DeneTauntSignalPrefix;

    private static string GetTauntSignalName(
        string signalPrefix,
        int fightAttempt,
        int signalNumber
    ) => $"{signalPrefix}{fightAttempt}_{signalNumber}";

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsOpeningOwner
    )
    {
        bool signalIsOpeningOwner = signalNumber % 2 != 0;
        return signalIsOpeningOwner == senderIsOpeningOwner
            ? signalNumber
            : signalNumber + 1;
    }

    private void StartSkillEngine(ClassPreset preset)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: !isTaunter
                && GetSetupOption<bool>("UsePotions")
                    ? preset.CombatPotion
                    : null
        );
    }

    private FightResult GetFightResult(int fightAttempt)
    {
        if (!LoneWolf.IsMonsterAlive(DragoMapId))
            return FightResult.Defeated;

        return LoneWolf.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private FightResult RecoverFromDeath(int fightAttempt, out bool recovered)
    {
        recovered = false;

        if (Bot.Player.Alive)
            return FightResult.Continue;

        Core.Logger($"{LogPrefix} {playerAlias} died.");

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            if (LoneWolf.ShouldResetFight(fightAttempt))
                return FightResult.Reset;

            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        FightResult result = GetFightResult(fightAttempt);
        if (result != FightResult.Continue)
            return result;

        Core.Logger($"{LogPrefix} {playerAlias} respawned.");

        if (!AggressiveMoveToBossRoom(GetNormalTarget()))
            return FightResult.Stopped;

        recovered = true;
        return FightResult.Continue;
    }

    private bool HandleFightReset(int fightAttempt)
    {
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

    private bool TryGetPartnerState(
        string partnerName,
        out bool dead,
        out bool inBossRoom
    )
    {
        dead = false;
        inBossRoom = false;

        var players = Bot.Map.Players;
        if (players == null)
            return false;

        foreach (var player in players)
        {
            if (!string.Equals(player.Name, partnerName, StringComparison.OrdinalIgnoreCase))
                continue;

            dead = player.State == 0;
            inBossRoom = !dead
                && string.Equals(player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        return false;
    }

    private DateTimeOffset GetFocusExpiry() =>
        Bot.Target.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Stable)
                return LoneWolf.KingsEcho();

            return armyComposition == ArmyComposition.Reliable
                ? LoneWolf.VerusDoomKnight()
                : LoneWolf.LegionRevenant();
        }

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        return LoneWolf.LordOfOrder();
    }

    private string GetPlayerAlias() => GetPlayerAlias(
        LoneWolf.IsArmyPlayer(1)
            ? 1
            : LoneWolf.IsArmyPlayer(2)
                ? 2
                : LoneWolf.IsArmyPlayer(3)
                    ? 3
                    : 4
    );

    private static string GetPlayerAlias(int playerNumber) =>
        playerNumber switch
        {
            1 => "playerOne",
            2 => "playerTwo",
            3 => "playerThree",
            _ => "playerFour",
        };

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
