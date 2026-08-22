/*
name: Ultra Nulgath LW
description: Four-player CoreLoneWolf Army script for Ultra Nulgath.
tags: ultra, nulgath, weekly, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraNulgath_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Optimized,
        Pay2Win,
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

    private const string LogPrefix = "Ultra Nulgath LW";
    private const string SyncFileName = "UltraNulgath_LW.sync";
    private const string MapName = "ultranulgath";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Right";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string AbyssPacketText = "Abyss!";
    private const int UltraQuestId = 8692;
    private const int PrerequisiteQuestId = 0;
    private const string PrerequisiteQuestName = "";
    private const int MinimumLevel = 80;
    private const int BladeMapId = 1;
    private const int NulgathMapId = 2;
    private const int BladeHealthThreshold = 750_000;
    private const int TaunterOneTauntDelay = 5000;
    private const int PlayerFourTauntDelay = 2000;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private bool isTaunter;
    private bool isTaunterOne;
    private int privateRoomNumber;

    public string OptionsStorage = "UltraNulgath_LW";
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
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nOptimized: DOT / DOT / LR / LOO\nPay2Win: Guardian / SC / LR / LOO\nFast: AI / VDK / LR / LOO",
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
        isTaunterOne = armyComposition == ArmyComposition.Default
            ? LoneWolf.IsArmyPlayer(1)
            : LoneWolf.IsArmyPlayer(3);
        isTaunter = isTaunterOne || LoneWolf.IsArmyPlayer(4);

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

            if (!PrepareSafeRoom(preset))
                return false;

            if (
                LoneWolf.IsArmyPlayer(4)
                && !LoneWolf.StartPacketDetector(PacketCommand, AbyssPacketText)
            )
            {
                Core.Logger(
                    "The Abyss packet detector could not be started.",
                    "RunFightAttempts",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

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
            "UltraNulgathComposition",
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

    private FightResult Fight(ClassPreset preset, int fightAttempt)
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
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool playerFour = LoneWolf.IsArmyPlayer(4);
        bool tauntScheduled = isTaunterOne;
        bool openingTaunt = isTaunterOne;
        int abyssCycle = 1;
        DateTimeOffset tauntAt = isTaunterOne
            ? DateTimeOffset.Now.AddMilliseconds(TaunterOneTauntDelay)
            : DateTimeOffset.MinValue;

        while (!Bot.ShouldExit)
        {
            if (!LoneWolf.IsMonsterAlive(NulgathMapId))
                break;

            if (LoneWolf.ShouldResetFight(fightAttempt))
            {
                LoneWolf.StopSkillEngine();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

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

                if (!LoneWolf.IsMonsterAlive(NulgathMapId))
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt))
                {
                    LoneWolf.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            LoneWolf.MaintainTarget(GetTargetMapId());
            DateTimeOffset now = DateTimeOffset.Now;

            if (isTaunterOne)
                RunTaunterOneTaunts(fightAttempt, ref abyssCycle, ref tauntScheduled, ref openingTaunt, ref tauntAt, now);
            else if (playerFour && !RunPlayerFourTaunts(fightAttempt, ref abyssCycle, ref tauntScheduled, ref tauntAt, now))
            {
                LoneWolf.StopSkillEngine();
                return FightResult.Stopped;
            }

            Bot.Sleep(FightPollDelay);
        }

        LoneWolf.StopSkillEngine();

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Nulgath defeated.");
        return FightResult.Defeated;
    }

    private void RunTaunterOneTaunts(
        int fightAttempt,
        ref int abyssCycle,
        ref bool tauntScheduled,
        ref bool openingTaunt,
        ref DateTimeOffset tauntAt,
        DateTimeOffset now
    )
    {
        if (tauntScheduled)
        {
            if (now < tauntAt)
                return;

            LoneWolf.RequestTaunt(NulgathMapId);

            if (openingTaunt)
            {
                openingTaunt = false;
                Core.Logger($"{LogPrefix} {playerAlias} requested the opening Nulgath taunt.");
            }
            else
            {
                Core.Logger($"{LogPrefix} {playerAlias} requested Abyss response taunt {abyssCycle}.");
                abyssCycle++;
            }

            tauntScheduled = false;
            return;
        }

        string signal = GetAbyssTauntSignalName(fightAttempt, abyssCycle);
        if (!LoneWolf.HasArmySignal(signal, 4))
            return;

        tauntAt = now.AddMilliseconds(TaunterOneTauntDelay);
        tauntScheduled = true;
        Core.Logger($"{LogPrefix} {playerAlias} received {signal}.");
    }

    private bool RunPlayerFourTaunts(
        int fightAttempt,
        ref int abyssCycle,
        ref bool tauntScheduled,
        ref DateTimeOffset tauntAt,
        DateTimeOffset now
    )
    {
        if (!tauntScheduled)
        {
            if (!LoneWolf.HasPacketDetection(abyssCycle))
                return true;

            tauntAt = now.AddMilliseconds(PlayerFourTauntDelay);
            tauntScheduled = true;
            Core.Logger($"{LogPrefix} playerFour detected Abyss cycle {abyssCycle}.");
            return true;
        }

        if (now < tauntAt)
            return true;

        LoneWolf.RequestTaunt(NulgathMapId);
        string signal = GetAbyssTauntSignalName(fightAttempt, abyssCycle);

        if (!LoneWolf.SendArmySignal(signal))
            return false;

        Core.Logger($"{LogPrefix} playerFour requested taunt and sent {signal}.");
        abyssCycle++;
        tauntScheduled = false;
        return true;
    }

    private int GetTargetMapId()
    {
        if (
            armyComposition == ArmyComposition.Optimized
            || armyComposition == ArmyComposition.Pay2Win
            || armyComposition == ArmyComposition.Fast
        )
            return NulgathMapId;

        if (
            armyComposition == ArmyComposition.Stable
            || armyComposition == ArmyComposition.Reliable
        )
        {
            if (LoneWolf.IsArmyPlayer(1))
            {
                if (armyComposition == ArmyComposition.Reliable)
                    return LoneWolf.IsMonsterAlive(BladeMapId)
                        ? BladeMapId
                        : NulgathMapId;

                return LoneWolf.GetMonsterHP(BladeMapId) > 500_000
                    ? BladeMapId
                    : NulgathMapId;
            }

            if (LoneWolf.IsArmyPlayer(2))
                return LoneWolf.IsMonsterAlive(BladeMapId)
                    ? BladeMapId
                    : NulgathMapId;

            return NulgathMapId;
        }

        if (!LoneWolf.IsArmyPlayer(2))
            return NulgathMapId;

        int bladeHealth = LoneWolf.GetMonsterHP(BladeMapId);
        return LoneWolf.IsMonsterAlive(BladeMapId)
            && bladeHealth > BladeHealthThreshold
                ? BladeMapId
                : NulgathMapId;
    }

    private static string GetAbyssTauntSignalName(
        int fightAttempt,
        int abyssCycle
    ) =>
        $"ABYSS_TAUNT_DONE_{fightAttempt}_{abyssCycle}";

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

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Fast)
                return LoneWolf.ArcanaInvoker();

            if (armyComposition == ArmyComposition.Pay2Win)
                return LoneWolf.Guardian();

            if (armyComposition == ArmyComposition.Optimized)
                return LoneWolf.DragonOfTime();

            if (armyComposition == ArmyComposition.Reliable)
                return LoneWolf.VerusDoomKnight();

            return armyComposition == ArmyComposition.Stable
                ? LoneWolf.KingsEcho()
                : LoneWolf.LegionRevenant();
        }

        if (LoneWolf.IsArmyPlayer(2))
        {
            if (armyComposition == ArmyComposition.Fast)
                return LoneWolf.VerusDoomKnight();

            return armyComposition == ArmyComposition.Optimized
                ? LoneWolf.DragonOfTime()
                : LoneWolf.StoneCrusher();
        }

        if (LoneWolf.IsArmyPlayer(3))
            return (
                armyComposition == ArmyComposition.Optimized
                    || armyComposition == ArmyComposition.Pay2Win
                    || armyComposition == ArmyComposition.Fast
            )
                ? LoneWolf.LegionRevenant()
                : LoneWolf.ArchPaladin();

        return LoneWolf.LordOfOrder();
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
