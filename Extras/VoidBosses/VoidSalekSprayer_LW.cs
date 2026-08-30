/*
name: Void Salek Sprayer LW
description: Four-to-seven-player CoreLoneWolf Army script for Void Salek Sprayer.
tags: void, salek sprayer, challenge boss, seven-player, army, corelonewolf
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

public class VoidSalekSprayer_LW
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

    private const string LogPrefix = "Void Salek Sprayer LW";
    private const string SyncFileName = "VoidSalekSprayer_LW.sync";
    private const string MapName = "voidsalek";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string ExtinguishPacketText = "EXTINGUISH";
    private const string VoidEnergy = "Void Energy";
    private const string SalekDefeated = "Salek Sprayer Defeated";
    private const string SpoilOfSalek = "Spoil of Salek";
    private const int SurrenderQuestId = 9905;
    private const int SurrenderLegendQuestId = 9909;
    private const int FiendsPurgatoryQuestId = 10790;
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
    private const int TauntDelayMilliseconds = 5000;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool farmSalekSprayer;
    private bool isTaunter;

    public string OptionsStorage = "VoidSalekSprayer_LW";
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
            "FarmSalekSprayer",
            "Farm Salek Sprayer",
            "Farm Salek Sprayer repeatedly. Disable this to defeat it once.",
            false
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

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        ClassPreset preset = GetClassPreset();
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
        isTaunter = LoneWolf.IsArmyPlayer(1);
        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        UpdateDrops();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        Core.Join($"{MapName}-{privateRoomNumber}", FightCell, FightPad);
        if (!PrepareFightRoom(preset))
            return;

        if (
            isTaunter
            && !LoneWolf.StartPacketDetector(
                PacketCommand,
                ExtinguishPacketText
            )
        )
        {
            Fatal("The Salek Sprayer packet detector could not be started.", "Run");
            return;
        }

        try
        {
            if (!Sync("FIGHT_READY") || !RunFightLoop(preset))
                return;
        }
        finally
        {
            LoneWolf.StopPacketDetector();
        }

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");
        farmSalekSprayer = Bot.Config.Get<bool>("FarmSalekSprayer");

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
                isTaunter ? null : preset.CombatPotion
            );
        }

        if (isTaunter)
            LoneWolf.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareFightRoom(ClassPreset preset)
    {
        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                isTaunter ? null : preset.CombatPotion
            );
        }

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        return !Bot.ShouldExit;
    }

    private bool RunFightLoop(ClassPreset preset)
    {
        int fightCycle = 1;
        int killCount = 0;
        int nextExtinguishDetection = 1;

        while (!Bot.ShouldExit)
        {
            FightResult result = Fight(
                preset,
                fightCycle,
                ref nextExtinguishDetection
            );

            if (result == FightResult.Defeated)
            {
                killCount++;
                UpdateDrops();
                Core.Logger(
                    $"{LogPrefix} {playerAlias} completed kill {killCount}."
                );

                if (!farmSalekSprayer)
                    return true;

                if (!WaitForRespawn(ref nextExtinguishDetection))
                    return false;

                fightCycle++;
                continue;
            }

            if (
                result != FightResult.Reset
                || !HandleFightReset(
                    fightCycle,
                    ref nextExtinguishDetection
                )
            )
                return false;

            fightCycle++;
        }

        return false;
    }

    private FightResult Fight(
        ClassPreset preset,
        int fightCycle,
        ref int nextExtinguishDetection
    )
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fight cycle {fightCycle}.");

        bool bossObservedAlive = false;
        DateTimeOffset? tauntDue = null;
        int scheduledDetection = 0;

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
                    ProcessTauntCycle(
                        ref nextExtinguishDetection,
                        ref tauntDue,
                        ref scheduledDetection,
                        canRequestTaunt: false
                    );

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
            if (bossAlive && !bossObservedAlive)
            {
                bossObservedAlive = true;
                if (isTaunter)
                {
                    DrainExtinguishDetections(ref nextExtinguishDetection);
                    tauntDue = DateTimeOffset.UtcNow.AddMilliseconds(
                        TauntDelayMilliseconds
                    );
                    scheduledDetection = 0;
                    Core.Logger(
                        $"{LogPrefix} {playerAlias} scheduled the opening taunt in {TauntDelayMilliseconds}ms."
                    );
                }
            }
            else if (!bossAlive && bossObservedAlive)
                break;

            if (bossAlive)
                LoneWolf.MaintainTarget(BossMapId);

            ProcessTauntCycle(
                ref nextExtinguishDetection,
                ref tauntDue,
                ref scheduledDetection,
                canRequestTaunt: bossAlive
            );
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Salek Sprayer defeated.");
        return FightResult.Defeated;
    }

    private void ProcessTauntCycle(
        ref int nextExtinguishDetection,
        ref DateTimeOffset? tauntDue,
        ref int scheduledDetection,
        bool canRequestTaunt
    )
    {
        if (!isTaunter)
            return;

        if (!canRequestTaunt)
        {
            DrainExtinguishDetections(ref nextExtinguishDetection);
            return;
        }

        if (tauntDue.HasValue)
        {
            DrainExtinguishDetections(ref nextExtinguishDetection);
            if (DateTimeOffset.UtcNow < tauntDue.Value)
                return;

            LoneWolf.RequestAbsolutePriorityTaunt(BossMapId);
            string source = scheduledDetection == 0
                ? "opening timer"
                : $"EXTINGUISH detection {scheduledDetection}";
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested absolute priority taunt for {source}."
            );
            tauntDue = null;
            return;
        }

        if (!LoneWolf.HasPacketDetection(nextExtinguishDetection))
            return;

        scheduledDetection = nextExtinguishDetection;
        nextExtinguishDetection++;
        tauntDue = DateTimeOffset.UtcNow.AddMilliseconds(
            TauntDelayMilliseconds
        );
        DrainExtinguishDetections(ref nextExtinguishDetection);
        Core.Logger(
            $"{LogPrefix} {playerAlias} detected EXTINGUISH {scheduledDetection} and scheduled a taunt in {TauntDelayMilliseconds}ms."
        );
    }

    private void DrainExtinguishDetections(ref int nextExtinguishDetection)
    {
        if (!isTaunter)
            return;

        while (LoneWolf.HasPacketDetection(nextExtinguishDetection))
            nextExtinguishDetection++;
    }

    private bool WaitForRespawn(ref int nextExtinguishDetection)
    {
        Core.Logger(
            $"{LogPrefix} {playerAlias} waiting for Salek Sprayer to respawn."
        );

        while (!Bot.ShouldExit && !LoneWolf.IsMonsterAlive(BossMapId))
        {
            DrainExtinguishDetections(ref nextExtinguishDetection);
            Bot.Sleep(RespawnPollDelay);
        }

        return !Bot.ShouldExit;
    }

    private bool HandleFightReset(
        int fightCycle,
        ref int nextExtinguishDetection
    )
    {
        StopFightCombat();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            DrainExtinguishDetections(ref nextExtinguishDetection);
            LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        DrainExtinguishDetections(ref nextExtinguishDetection);
        LoneWolf.ShouldResetFight(fightCycle, armyPlayerCount);
        return Sync($"FIGHT_RESET_{fightCycle}_READY");
    }

    private void UpdateDrops()
    {
        UpdateDrop(VoidEnergy, eligible: true);
        UpdateDrop(
            SalekDefeated,
            Bot.Quests.IsInProgress(SurrenderQuestId)
                || Bot.Quests.IsInProgress(SurrenderLegendQuestId)
        );
        UpdateDrop(
            SpoilOfSalek,
            Bot.Quests.IsInProgress(FiendsPurgatoryQuestId)
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

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private bool Fatal(string message, string caller)
    {
        Core.Logger(message, caller, messageBox: true, stopBot: true);
        return false;
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
