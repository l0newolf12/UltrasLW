/*
name: Void Nightbane LW
description: Four-to-seven-player CoreLoneWolf Army script for Void Nightbane
tags: void, nightbane, challenge boss, seven-player, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class VoidNightbane_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
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

    private const string LogPrefix = "Void Nightbane LW";
    private const string SyncFileName = "VoidNightbane_LW.sync";
    private const string MapName = "voidnightbane";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string VoidEnergy = "Void Energy";
    private const string InsatiableHunger = "Insatiable Hunger";
    private const string StarlitJournalPage3Scraps = "Starlit Journal Page 3 Scraps";
    private const string NightbaneEssence = "Nightbane's ??? Essence";
    private const int CelestialSkiesQuestId = 7713;
    private const int WrongTurnQuestId = 9091;
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool farmNightbane;

    public string OptionsStorage = "VoidNightbane_LW";
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
            "Default: LR / SC / AP / LOO / VDK / Bard / Shaman\n"
                + "Stable: KE / SC / AP / LOO / VDK / Bard / AF",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<bool>(
            "FarmNightbane",
            "Farm Nightbane",
            "Farm Nightbane repeatedly. Disable this to defeat it once.",
            true
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

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        ClassPreset preset = GetClassPreset();
        if (preset.CapeEnhancement == CapeSpecial.Vainglory)
            preset.CapeEnhancement = CapeSpecial.Lament;

        if (
            !LoneWolf.ValidateUltraAccess(
                0,
                0,
                string.Empty,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        playerAlias = GetPlayerAlias();
        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        UpdateDrops();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        Core.Join($"{MapName}-{privateRoomNumber}", FightCell, FightPad);
        if (!PrepareFightRoom(preset))
            return;

        if (!Sync("FIGHT_READY") || !RunFightLoop(preset))
            return;

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");
        farmNightbane = Bot.Config.Get<bool>("FarmNightbane");

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
                "Vainglory is enhanced on the cape. Void Nightbane will fail.",
                "Prepare",
                messageBox: true
            );
            return;
        }
    }

    private bool PrepareFightRoom(ClassPreset preset)
    {
        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        return !Bot.ShouldExit;
    }

    private bool RunFightLoop(ClassPreset preset)
    {
        int fightCycle = 1;
        int killCount = 0;

        while (!Bot.ShouldExit)
        {
            FightResult result = Fight(preset, fightCycle);

            if (result == FightResult.Defeated)
            {
                killCount++;
                UpdateDrops();
                Core.Logger(
                    $"{LogPrefix} {playerAlias} completed kill {killCount}."
                );

                if (!farmNightbane)
                    return true;

                if (!WaitForRespawn())
                    return false;

                fightCycle++;
                continue;
            }

            if (
                result != FightResult.Reset
                || !HandleFightReset(fightCycle)
            )
                return false;

            fightCycle++;
        }

        return false;
    }

    private FightResult Fight(ClassPreset preset, int fightCycle)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fight cycle {fightCycle}.");

        bool bossObservedAlive = false;

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount))
            {
                StopFightCombat();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount))
                    {
                        StopFightCombat();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount))
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");
                continue;
            }

            bool bossAlive = LoneWolf.IsMonsterAlive(BossMapId);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            if (bossAlive)
                LoneWolf.MaintainTarget(BossMapId);

            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return FightResult.Stopped;

        Core.Logger(
            $"{LogPrefix} {playerAlias} confirmed Nightbane defeated."
        );
        return FightResult.Defeated;
    }

    private bool WaitForRespawn()
    {
        Core.Logger(
            $"{LogPrefix} {playerAlias} waiting for Nightbane to respawn."
        );

        while (!Bot.ShouldExit && !LoneWolf.IsMonsterAlive(BossMapId))
            Bot.Sleep(RespawnPollDelay);

        return !Bot.ShouldExit;
    }

    private bool HandleFightReset(int fightCycle)
    {
        StopFightCombat();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount);
        return Sync($"FIGHT_RESET_{fightCycle}_READY");
    }

    private void UpdateDrops()
    {
        UpdateDrop(VoidEnergy, eligible: true);
        UpdateDrop(InsatiableHunger, eligible: true);
        UpdateDrop(
            StarlitJournalPage3Scraps,
            Bot.Quests.IsInProgress(CelestialSkiesQuestId)
        );
        UpdateDrop(
            NightbaneEssence,
            Bot.Quests.IsInProgress(WrongTurnQuestId)
        );
    }

    private void UpdateDrop(string itemName, bool eligible)
    {
        if (eligible && !Bot.Inventory.IsMaxStack(itemName))
        {
            if (!Bot.Drops.ToPickup.Contains(itemName))
                Core.AddDrop(itemName);

            return;
        }

        Core.RemoveDrop(itemName);
    }

    private void StopFightCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return armyComposition == ArmyComposition.Stable
                ? LoneWolf.KingsEcho()
                : LoneWolf.LegionRevenant();

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

        return armyComposition == ArmyComposition.Stable
            ? LoneWolf.ArchFiend()
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
