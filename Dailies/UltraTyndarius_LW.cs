/*
name: Ultra Tyndarius LW
description: Four-player CoreLoneWolf Army script for Ultra Tyndarius.
tags: ultra, tyndarius, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraTyndarius_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Fast,
        Test,
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

    private const string LogPrefix = "Ultra Tyndarius LW";
    private const string SyncFileName = "UltraTyndarius_LW.sync";
    private const string MapName = "ultratyndarius";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string RighteousSealAura = "Righteous Seal";
    private const string FocusAura = "Focus";
    private const string RighteousSealSignalPrefix = "RIGHTEOUS_SEAL_READY_";
    private const string TauntSignalPrefix = "TYNDARIUS_TAUNT_";
    private const int UltraQuestId = 8245;
    private const int PrerequisiteQuestId = 8243;
    private const string PrerequisiteQuestName = "Avatar of Fire";
    private const int MinimumLevel = 61;
    private const int FirstAddMapId = 1;
    private const int MainBossMapId = 2;
    private const int SecondAddMapId = 3;
    private const int FocusRefreshMilliseconds = 1500;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraTyndarius_LW";
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
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nFast: AI / SC / AP / LOO\nTest: LR / SC / AP / LOO",
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
        isTaunter = IsTaunterRole();

        if (isTaunter)
            preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        LoneWolf.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

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

            if (LoneWolf.IsArmyPlayer(3))
            {
                if (
                    !PrepareRighteousSeal()
                    || !SendRighteousSealSignal(fightAttempt)
                )
                    return false;
            }
            else
            {
                if (
                    !WaitForRighteousSealSignal(fightAttempt)
                    || !MoveToBossRoom(useDirectFlash: true)
                )
                    return false;
            }

            FightResult result = Fight(preset, fightAttempt);
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
            "UltraTyndariusComposition",
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
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

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
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool PrepareRighteousSeal()
    {
        Core.Logger($"{LogPrefix} playerThree starting Righteous Seal preparation.");

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} playerThree died during Righteous Seal preparation.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Core.Logger($"{LogPrefix} playerThree respawned and is retrying preparation.");
            }

            if (!MoveToBossRoom(useDirectFlash: true))
                return false;

            if (!LoneWolf.IsMonsterAlive(MainBossMapId))
                return true;

            LoneWolf.MaintainTarget(MainBossMapId);

            if (Bot.Target.GetAura(RighteousSealAura) != null)
            {
                Core.Logger($"{LogPrefix} playerThree confirmed Righteous Seal.");
                return true;
            }

            if (Bot.Skills.CanUseSkill(3))
                Bot.Skills.UseSkill(3);

            Bot.Sleep(FightPollDelay);
        }

        return !Bot.ShouldExit;
    }

    private bool SendRighteousSealSignal(int fightAttempt)
    {
        string signal = GetRighteousSealSignal(fightAttempt);
        if (!LoneWolf.SendArmySignal(signal))
            return false;

        Core.Logger($"{LogPrefix} playerThree sent {signal}.");
        return true;
    }

    private bool WaitForRighteousSealSignal(int fightAttempt)
    {
        string signal = GetRighteousSealSignal(fightAttempt);
        Core.Logger($"{LogPrefix} {playerAlias} waiting for {signal}.");

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.HasArmySignal(signal, 3))
            {
                Core.Logger($"{LogPrefix} {playerAlias} received {signal}.");
                return true;
            }

            Bot.Sleep(FightPollDelay);
        }

        return false;
    }

    private static string GetRighteousSealSignal(int fightAttempt) =>
        $"{RighteousSealSignalPrefix}{fightAttempt}";

    private bool MoveToBossRoom(bool useDirectFlash = false)
    {
        if (Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad)
            return true;

        if (useDirectFlash)
        {
            Bot.Flash.Call("jumpCorrectRoom", BossCell, BossPad, false, false);

            while (
                !Bot.ShouldExit
                && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
            )
                Bot.Sleep(FightPollDelay);
        }
        else
        {
            Core.Jump(BossCell, BossPad);
        }

        return !Bot.ShouldExit
            && Bot.Player.Cell == BossCell
            && Bot.Player.Pad == BossPad;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        if (LoneWolf.IsArmyPlayer(3))
            return FightBossTaunter(
                preset,
                fightAttempt,
                isArchPaladin: true,
                partnerPlayerNumber: 4
            );

        if (LoneWolf.IsArmyPlayer(4))
            return FightBossTaunter(
                preset,
                fightAttempt,
                isArchPaladin: false,
                partnerPlayerNumber: 3
            );

        if (armyComposition == ArmyComposition.Test)
            return FightRightAddThenBoss(
                preset,
                fightAttempt,
                tauntLeftAdd: LoneWolf.IsArmyPlayer(2)
            );

        if (!UsesDefaultFightRoles())
            return FightDamageDealer(preset, fightAttempt);

        int tauntMapId = LoneWolf.IsArmyPlayer(1)
            ? FirstAddMapId
            : SecondAddMapId;
        return FightAddTaunter(preset, fightAttempt, tauntMapId);
    }

    private FightResult FightAddTaunter(
        ClassPreset preset,
        int fightAttempt,
        int tauntMapId
    )
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = LoneWolf.IsMonsterAlive(tauntMapId)
                && LoneWolf.RequestImmediateTaunt(tauntMapId);

            if (!immediateTauntAccepted)
                LoneWolf.MaintainTarget(GetPriorityTarget());

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightRightAddThenBoss(
        ClassPreset preset,
        int fightAttempt,
        bool tauntLeftAdd
    )
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");
        bool bossLocked = false;

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = tauntLeftAdd
                && LoneWolf.IsMonsterAlive(FirstAddMapId)
                && LoneWolf.RequestImmediateTaunt(FirstAddMapId);

            if (!immediateTauntAccepted)
            {
                int targetMapId = bossLocked
                    ? MainBossMapId
                    : LoneWolf.IsMonsterAlive(SecondAddMapId)
                        ? SecondAddMapId
                        : MainBossMapId;

                if (targetMapId == MainBossMapId)
                    bossLocked = true;

                LoneWolf.MaintainTarget(targetMapId);
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightDamageDealer(ClassPreset preset, int fightAttempt)
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");
        bool bossLocked = false;

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            int targetMapId = bossLocked
                ? MainBossMapId
                : GetPriorityTarget();

            if (targetMapId == MainBossMapId)
                bossLocked = true;

            LoneWolf.MaintainTarget(targetMapId);
            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightBossTaunter(
        ClassPreset preset,
        int fightAttempt,
        bool isArchPaladin,
        int partnerPlayerNumber
    )
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = isArchPaladin;
        bool partnerDeathObserved = false;
        int nextSignalNumber = 1;
        DateTimeOffset focusBaseline = GetFocusExpiry();
        string partnerName = (
            GetSetupOption<string>($"player{partnerPlayerNumber}")
        ).Trim();
        string partnerAlias = isArchPaladin ? "playerFour" : "playerThree";

        if (isArchPaladin)
        {
            LoneWolf.RequestTaunt(MainBossMapId);
            Core.Logger($"{LogPrefix} playerThree requested the first boss taunt.");
        }

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(
                fightAttempt,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return FinishFight(result);

            LoneWolf.MaintainTarget(MainBossMapId);

            if (recovered)
            {
                nextSignalNumber = AlignSignalNumber(nextSignalNumber, isArchPaladin);
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = GetFocusExpiry();
                LoneWolf.RequestTaunt(MainBossMapId);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next boss taunt after returning.");
            }

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignalName(
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
                        !isArchPaladin
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating boss taunts.");
                }
                else
                {
                    LoneWolf.RequestImmediateTaunt(MainBossMapId);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            var focus = Bot.Target.GetAura(FocusAura);
            if (
                waitingForOwnFocus
                && focus != null
                && focus.ExpiresAt > focusBaseline
            )
            {
                focusBaseline = focus.ExpiresAt;
                waitingForOwnFocus = false;
                ownsFocusCycle = true;
                Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus and owns the taunt cycle.");
            }

            if (waitingForOwnFocus)
            {
                if (focus == null)
                    LoneWolf.RequestImmediateTaunt(MainBossMapId);
            }
            else if (
                ownsFocusCycle
                && focus == null
            )
            {
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = DateTimeOffset.MinValue;
                LoneWolf.RequestImmediateTaunt(MainBossMapId);
            }
            else if (
                ownsFocusCycle
                && focus != null
                && focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
            )
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (LoneWolf.SendArmySignal(signal))
                {
                    Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                    nextSignalNumber++;
                    ownsFocusCycle = false;
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (LoneWolf.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = GetFocusExpiry();
                    waitingForOwnFocus = true;
                    LoneWolf.RequestTaunt(MainBossMapId);
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled boss taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private static string GetTauntSignalName(
        int fightAttempt,
        int signalNumber
    ) =>
        $"{TauntSignalPrefix}{fightAttempt}_{signalNumber}";

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsArchPaladin
    )
    {
        bool signalIsArchPaladin = signalNumber % 2 != 0;
        return signalIsArchPaladin == senderIsArchPaladin
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

    private int GetPriorityTarget()
    {
        if (LoneWolf.IsMonsterAlive(SecondAddMapId))
            return SecondAddMapId;

        if (LoneWolf.IsMonsterAlive(FirstAddMapId))
            return FirstAddMapId;

        return MainBossMapId;
    }

    private FightResult GetFightResult(int fightAttempt)
    {
        if (!LoneWolf.IsMonsterAlive(MainBossMapId))
            return FightResult.Defeated;

        return LoneWolf.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private FightResult RecoverFromDeath(
        int fightAttempt,
        out bool recovered
    )
    {
        recovered = false;

        if (Bot.Player.Alive)
            return GetFightResult(fightAttempt);

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

        if (!MoveToBossRoom())
            return FightResult.Stopped;

        recovered = true;
        return FightResult.Continue;
    }

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

    private FightResult FinishFight(FightResult result)
    {
        LoneWolf.StopSkillEngine();

        if (result != FightResult.Defeated)
            return result;

        Core.Jump(SafeCell, SafePad);
        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Tyndarius defeated.");
        return FightResult.Defeated;
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
            ? Bot.Config!.Get<T>("Daily_Ultras", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return armyComposition switch
            {
                ArmyComposition.Stable => LoneWolf.KingsEcho(),
                ArmyComposition.Reliable => LoneWolf.VerusDoomKnight(),
                ArmyComposition.Fast => LoneWolf.ArcanaInvoker(),
                _ => LoneWolf.LegionRevenant(),
            };

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        return LoneWolf.LordOfOrder();
    }

    private bool UsesDefaultFightRoles() =>
        armyComposition == ArmyComposition.Default
        || armyComposition == ArmyComposition.Reliable;

    private bool IsTaunterRole() =>
        UsesDefaultFightRoles()
        || LoneWolf.IsArmyPlayer(3)
        || LoneWolf.IsArmyPlayer(4)
        || (
            armyComposition == ArmyComposition.Test
            && LoneWolf.IsArmyPlayer(2)
        );

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
