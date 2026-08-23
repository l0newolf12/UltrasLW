/*
name: Void Xyfrag LW
description: Four-to-seven-player CoreLoneWolf Army script for Xyfrag.
tags: void, xyfrag, challenge boss, seven-player, army, corelonewolf
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

public class VoidXyfrag_LW
{
    public enum ArmyComposition
    {
        Default,
        Reliable,
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

    private const string LogPrefix = "Void Xyfrag LW";
    private const string SyncFileName = "VoidXyfrag_LW.sync";
    private const string MapName = "voidxyfrag";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string BleechPacketText = "BLEEEEEEEEEEEECCH";
    private const string VoidEnergy = "Void Energy";
    private const string SlimyTooth = "Xyfrag's Slimy Tooth";
    private const string XyfragEssence = "Xyfrag's ??? Essence";
    private const int XyfragEssenceId = 73863;
    private const int DoomSpikesQuestId = 9418;
    private const int WrongTurnQuestId = 9091;
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool farmXyfrag;
    private bool isTaunter;
    private bool masterMode;
    private bool masterStarted;
    private ClassPreset? masterPreset;
    private int masterFightCycle = 1;
    private int masterNextBleechDetection = 1;

    public string OptionsStorage = "VoidXyfrag_LW";
    public bool DontPreconfigure = true;
    public static Option<string> player6 = new(
        "player6",
        "Player 6",
        "Player 6 account name.",
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
        LoneWolf.player5,
        player6,
        player7,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: KE / SC / AP / LOO / VDK / Bard / AF\n"
                + "Reliable: LR / SC / AP / LOO / VDK / Bard / Shaman\n"
                + "Stable: AF / SC / AP / LOO / VDK / Bard / Shaman",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<bool>(
            "FarmXyfrag",
            "Farm Xyfrag",
            "Farm Xyfrag repeatedly. Disable this to defeat Xyfrag once.",
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
            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();
        }
    }

    public bool RunFromMaster()
    {
        try
        {
            return StartFromMaster() && RunOnceFromMaster();
        }
        finally
        {
            StopFromMaster();
        }
    }

    public bool StartFromMaster()
    {
        masterMode = true;
        masterStarted = false;
        masterPreset = null;
        masterFightCycle = 1;
        masterNextBleechDetection = 1;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        if (!ValidateOptions())
            return false;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount, "Setup"))
            return false;

        ClassPreset preset = GetClassPreset();
        if (
            armyComposition == ArmyComposition.Default
            && preset.CapeEnhancement == CapeSpecial.Vainglory
        )
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
            return false;

        playerAlias = GetPlayerAlias();
        isTaunter = LoneWolf.IsArmyPlayer(5);
        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        UpdateDrops();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return false;

        Core.Join($"{MapName}-{privateRoomNumber}", FightCell, FightPad);
        if (!PrepareFightRoom(preset))
            return false;

        if (
            isTaunter
            && !LoneWolf.StartPacketDetector(PacketCommand, BleechPacketText)
        )
            return Fatal("The Xyfrag packet detector could not be started.", "StartFromMaster");

        if (!Sync("FIGHT_READY"))
            return false;

        masterPreset = preset;
        masterStarted = true;
        return true;
    }

    public bool RunOnceFromMaster()
    {
        if (!masterStarted || masterPreset == null)
            return false;

        return RunFightLoop(
            masterPreset,
            ref masterFightCycle,
            ref masterNextBleechDetection
        );
    }

    public void StopFromMaster()
    {
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        masterPreset = null;
        masterStarted = false;
        masterFightCycle = 1;
        masterNextBleechDetection = 1;
        masterMode = false;
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (
            !LoneWolf.StartArmySync(
                SyncFileName,
                armyPlayerCount,
                masterMode ? "Setup" : null
            )
        )
            return;

        ClassPreset preset = GetClassPreset();
        if (
            armyComposition == ArmyComposition.Default
            && preset.CapeEnhancement == CapeSpecial.Vainglory
        )
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
        isTaunter = LoneWolf.IsArmyPlayer(5);
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
            && !LoneWolf.StartPacketDetector(PacketCommand, BleechPacketText)
        )
        {
            Fatal("The Xyfrag packet detector could not be started.", "Run");
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
        armyComposition = GetBossOption<ArmyComposition>(
            "XyfragComposition",
            "ArmyComposition"
        );
        privateRoomNumber = GetSetupOption<int>("PrivateRoomNumber");
        farmXyfrag = masterMode
            ? false
            : Bot.Config!.Get<bool>("FarmXyfrag");

        string playerSeven = GetSetupOption<string>("player7")?.Trim() ?? string.Empty;
        armyPlayerCount = string.IsNullOrEmpty(playerSeven) ? 6 : 7;

        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (GetSetupOption<bool>("UseEnhancements"))
        {
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );
        }

        if (GetSetupOption<bool>("UsePotions"))
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
        if (GetSetupOption<bool>("UsePotions"))
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
        int nextBleechDetection = 1;

        return RunFightLoop(
            preset,
            ref fightCycle,
            ref nextBleechDetection
        );
    }

    private bool RunFightLoop(
        ClassPreset preset,
        ref int fightCycle,
        ref int nextBleechDetection
    )
    {
        int killCount = 0;

        while (!Bot.ShouldExit)
        {
            FightResult result = Fight(
                preset,
                fightCycle,
                ref nextBleechDetection
            );

            if (result == FightResult.Defeated)
            {
                killCount++;
                UpdateDrops();
                Core.Logger(
                    $"{LogPrefix} {playerAlias} completed kill {killCount}."
                );

                fightCycle++;

                if (!farmXyfrag)
                    return true;

                if (!WaitForRespawn(ref nextBleechDetection))
                    return false;

                continue;
            }

            if (
                result != FightResult.Reset
                || !HandleFightReset(fightCycle, ref nextBleechDetection)
            )
                return false;

            fightCycle++;
        }

        return false;
    }

    private FightResult Fight(
        ClassPreset preset,
        int fightCycle,
        ref int nextBleechDetection
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
                    ProcessBleechDetections(
                        ref nextBleechDetection,
                        requestTaunt: false
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
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            if (bossAlive)
                LoneWolf.MaintainTarget(BossMapId);

            ProcessBleechDetections(
                ref nextBleechDetection,
                requestTaunt: bossAlive
            );
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Xyfrag defeated.");
        return FightResult.Defeated;
    }

    private void ProcessBleechDetections(
        ref int nextBleechDetection,
        bool requestTaunt
    )
    {
        if (!isTaunter)
            return;

        while (LoneWolf.HasPacketDetection(nextBleechDetection))
        {
            if (requestTaunt)
            {
                LoneWolf.RequestAbsolutePriorityTaunt(BossMapId);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested absolute priority taunt for BLEECH detection {nextBleechDetection}."
                );
            }

            nextBleechDetection++;
        }
    }

    private bool WaitForRespawn(ref int nextBleechDetection)
    {
        Core.Logger($"{LogPrefix} {playerAlias} waiting for Xyfrag to respawn.");

        while (!Bot.ShouldExit && !LoneWolf.IsMonsterAlive(BossMapId))
        {
            ProcessBleechDetections(
                ref nextBleechDetection,
                requestTaunt: false
            );
            Bot.Sleep(RespawnPollDelay);
        }

        return !Bot.ShouldExit;
    }

    private bool HandleFightReset(
        int fightCycle,
        ref int nextBleechDetection
    )
    {
        StopFightCombat();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            ProcessBleechDetections(
                ref nextBleechDetection,
                requestTaunt: false
            );
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
        UpdateDrop(SlimyTooth, Bot.Quests.IsInProgress(DoomSpikesQuestId));
        UpdateDrop(
            XyfragEssence,
            Bot.Quests.IsInProgress(WrongTurnQuestId),
            XyfragEssenceId
        );

        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();
    }

    private void UpdateDrop(string itemName, bool eligible, int itemId = 0)
    {
        if (eligible && !Bot.Inventory.IsMaxStack(itemName))
        {
            if (!Bot.Drops.ToPickup.Contains(itemName))
                Core.AddDrop(itemName);

            if (itemId > 0 && !Bot.Drops.ToPickupIDs.Contains(itemId))
                Core.AddDrop(itemId);

            return;
        }

        Core.RemoveDrop(itemName);
        if (itemId > 0)
            Core.RemoveDrop(itemId);
    }

    private void StopFightCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Default)
                return LoneWolf.KingsEcho();

            return armyComposition == ArmyComposition.Stable
                ? LoneWolf.ArchFiend()
                : LoneWolf.LegionRevenant();
        }

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

        return armyComposition == ArmyComposition.Default
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

    private T GetSetupOption<T>(string optionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Setup", optionName)
            : Bot.Config!.Get<T>(optionName))!;

    private T GetBossOption<T>(string masterOptionName, string standaloneOptionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Void_Bosses", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

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
