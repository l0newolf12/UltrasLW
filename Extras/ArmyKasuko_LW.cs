/*
name: Army Kasuko LW
description: Four-to-seven-player CoreLoneWolf Army script for Kasuko.
tags: kasuko, seven-player, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class ArmyKasuko_LW
{
    public enum ArmyComposition
    {
        Default,
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

    private const string LogPrefix = "Army Kasuko LW";
    private const string SyncFileName = "ArmyKasuko_LW.sync";
    private const string MapName = "lavarockshore";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const int DailyQuestId = 9254;
    private const int MinimumLevel = 75;
    private const int FirstTargetMapId = 1;
    private const int SecondTargetMapId = 2;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DeathResetThreshold = 3;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;

    public string OptionsStorage = "ArmyKasuko_LW";
    public bool DontPreconfigure = true;
    public static Option<string> player5 = new(
        "player5",
        "Player 5 (Optional)",
        "Player 5 (Optional) account name.",
        string.Empty
    );
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
        player5,
        player6,
        player7,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: LR / SC / AP / LOO / VDK / Bard / Shaman",
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
        LoneWolf.SetAntiLag();
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

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        playerAlias = GetPlayerAlias();
        ClassPreset preset = GetClassPreset();

        if (
            !LoneWolf.ValidateUltraAccess(
                DailyQuestId,
                0,
                string.Empty,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        LoneWolf.AcceptUltraQuest(DailyQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Core.Jump(SafeCell, SafePad);
        LoneWolf.CompleteUltraQuest(DailyQuestId);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");

        string playerFive = Bot.Config.Get<string>("player5")?.Trim() ?? string.Empty;
        string playerSix = Bot.Config.Get<string>("player6")?.Trim() ?? string.Empty;
        string playerSeven = Bot.Config.Get<string>("player7")?.Trim() ?? string.Empty;

        if (
            string.IsNullOrEmpty(playerFive)
            && (!string.IsNullOrEmpty(playerSix) || !string.IsNullOrEmpty(playerSeven))
        )
        {
            Core.Logger(
                "Player 5 is required when Player 6 or Player 7 is configured.",
                "ValidateOptions",
                messageBox: true
            );
            return false;
        }

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
                : !string.IsNullOrEmpty(playerFive)
                    ? 5
                    : 4;

        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (Bot.Config!.Get<bool>("UseEnhancements"))
        {
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );
        }

        if (Bot.Config.Get<bool>("UsePotions"))
        {
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

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

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            FightResult result = Fight(preset, fightAttempt);
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
            false,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: Bot.Config!.Get<bool>("UsePotions")
                ? preset.CombatPotion
                : null
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        bool kasukoObservedAlive = false;

        while (!Bot.ShouldExit)
        {
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
                    if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        StopFightCombat();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            bool firstTargetAlive = LoneWolf.IsMonsterAlive(FirstTargetMapId);
            bool secondTargetAlive = LoneWolf.IsMonsterAlive(SecondTargetMapId);

            if (secondTargetAlive)
                kasukoObservedAlive = true;
            else if (kasukoObservedAlive)
                break;

            if (firstTargetAlive)
                LoneWolf.MaintainTarget(FirstTargetMapId);
            else if (secondTargetAlive)
                LoneWolf.MaintainTarget(SecondTargetMapId);

            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !kasukoObservedAlive)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Kasuko encounter defeated.");
        return FightResult.Defeated;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        StopFightCombat();

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

    private ClassPreset GetClassPreset()
    {
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

        return LoneWolf.Shaman();
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
        string.Equals(Bot.Player.Cell, SafeCell, StringComparison.OrdinalIgnoreCase);

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
