/*
name: Champion Drakath LW
description: Four-player CoreLoneWolf Army script for Champion Drakath.
tags: ultra, champion drakath, weekly, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDrakath_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Optimized,
        Pay2Win,
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

    private static readonly int[] StoneCrusherTauntThresholds =
    {
        16_500_000,
        12_500_000,
        6_200_000,
    };

    private static readonly int[] ArchPaladinTauntThresholds =
    {
        18_500_000,
        14_500_000,
        8_200_000,
        4_200_000,
    };

    private static readonly int[] AdjustedStoneCrusherTauntThresholds =
    {
        16_500_000,
        12_500_000,
        6_500_000,
    };

    private static readonly int[] AdjustedArchPaladinTauntThresholds =
    {
        18_500_000,
        14_500_000,
        8_500_000,
        4_500_000,
    };

    private const string LogPrefix = "Champion Drakath LW";
    private const string SyncFileName = "UltraDrakath_LW.sync";
    private const string MapName = "championdrakath";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const int UltraQuestId = 8300;
    private const int PrerequisiteQuestId = 3881;
    private const string PrerequisiteQuestName = "The Final Showdown!";
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDrakath_LW";
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
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nOptimized: Chaos Slayer / SC / AP / LOO\nPay2Win: Guardian / AP / LR / LOO",
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
        isTaunter = LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(3);

        ClassPreset preset = GetClassPreset();
        if (armyComposition == ArmyComposition.Pay2Win)
        {
            if (LoneWolf.IsArmyPlayer(2))
            {
                preset.WeaponEnhancement = WeaponSpecial.Praxis;
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
            else if (LoneWolf.IsArmyPlayer(3))
            {
                preset.HelmEnhancement = HelmSpecial.Hearty;
                preset.CapeEnhancement = CapeSpecial.Lament;
            }
            else if (LoneWolf.IsArmyPlayer(4))
                preset.CombatPotion = "Felicitous Philtre";
        }
        else if (
            LoneWolf.IsArmyPlayer(3)
            || (
                (
                    armyComposition == ArmyComposition.Default
                    || armyComposition == ArmyComposition.Reliable
                )
                && LoneWolf.IsArmyPlayer(1)
            )
        )
            preset.CapeEnhancement = CapeSpecial.Penitence;
        else if (preset.CapeEnhancement == CapeSpecial.Vainglory)
            preset.CapeEnhancement = CapeSpecial.Lament;

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
                runResult = UltraRunResult.AttemptsExhausted;
                return false;
            }
        }

        return false;
    }

    private bool ValidateOptions()
    {
        armyComposition = GetUltraOption<ArmyComposition>(
            "ChampionDrakathComposition",
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
        int[] tauntThresholds = GetTauntThresholds();
        int tauntIndex = 0;

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

        while (!Bot.ShouldExit)
        {
            if (!LoneWolf.IsMonsterAlive(BossMapId))
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

                if (!LoneWolf.IsMonsterAlive(BossMapId))
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt))
                {
                    LoneWolf.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (
                    LoneWolf.IsMonsterAlive(BossMapId)
                    && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                )
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            int bossHealth = LoneWolf.GetMonsterHP(BossMapId);
            LoneWolf.MaintainTarget(BossMapId);

            if (
                tauntIndex < tauntThresholds.Length
                && bossHealth > 0
                && bossHealth <= tauntThresholds[tauntIndex]
            )
            {
                int threshold = tauntThresholds[tauntIndex];
                LoneWolf.RequestTaunt(BossMapId);
                tauntIndex++;
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested taunt at {threshold} HP."
                );
            }

            Bot.Sleep(FightPollDelay);
        }

        LoneWolf.StopSkillEngine();

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Champion Drakath defeated.");
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
            ? Bot.Config!.Get<T>("Weekly_Ultras", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private int[] GetTauntThresholds()
    {
        if (LoneWolf.IsArmyPlayer(2))
            return armyComposition switch
            {
                ArmyComposition.Stable or ArmyComposition.Pay2Win =>
                    AdjustedStoneCrusherTauntThresholds,
                _ => StoneCrusherTauntThresholds,
            };

        if (LoneWolf.IsArmyPlayer(3))
            return armyComposition switch
            {
                ArmyComposition.Stable or ArmyComposition.Pay2Win =>
                    AdjustedArchPaladinTauntThresholds,
                _ => ArchPaladinTauntThresholds,
            };

        return Array.Empty<int>();
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return armyComposition switch
            {
                ArmyComposition.Stable => LoneWolf.KingsEcho(),
                ArmyComposition.Reliable => LoneWolf.VerusDoomKnight(),
                ArmyComposition.Optimized => LoneWolf.ChaosSlayer(),
                ArmyComposition.Pay2Win => LoneWolf.Guardian(),
                _ => LoneWolf.LegionRevenant(),
            };

        if (LoneWolf.IsArmyPlayer(2))
            return armyComposition == ArmyComposition.Pay2Win
                ? LoneWolf.ArchPaladin()
                : LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return armyComposition == ArmyComposition.Pay2Win
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
