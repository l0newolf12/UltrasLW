/*
name: Army Nightmare Carnax LW
description: Two-to-five-player CoreLoneWolf Army script for Nightmare Carnax.
tags: nightmare carnax, challenge boss, five-player, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class ArmyNightmareCarnax_LW
{
    public enum ArmyComposition
    {
        Default,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Army Nightmare Carnax LW";
    private const string SyncFileName = "ArmyNightmareCarnax_LW.sync";
    private const string MapName = "darkcarnax";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Right";
    private const string BossName = "Nightmare Carnax";
    private const string LastStandQuestName = "The Last Stand";
    private const string SyntheticViscera = "Synthetic Viscera";
    private const string CalamitousRuin = "Calamitous Ruin";
    private const int LastStandQuestId = 8872;
    private const int SyntheticVisceraMaxStack = 1000;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private readonly Queue<string> pendingZones = new();
    private readonly object zoneLock = new();

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool farmNightmareCarnax;
    private bool lastStandRegistered;

    public string OptionsStorage = "ArmyNightmareCarnax_LW";
    public bool DontPreconfigure = true;
    public static Option<string> player3 = new(
        "player3",
        "Player 3 (Optional)",
        "Player 3 (Optional) account name.",
        string.Empty
    );
    public static Option<string> player4 = new(
        "player4",
        "Player 4 (Optional)",
        "Player 4 (Optional) account name.",
        string.Empty
    );
    public static Option<string> player5 = new(
        "player5",
        "Player 5 (Optional)",
        "Player 5 (Optional) account name.",
        string.Empty
    );
    public List<IOption> Options = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        player3,
        player4,
        player5,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: DOT / DOT / SC / AP / LOO",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<bool>(
            "FarmNightmareCarnax",
            "Farm Nightmare Carnax",
            "Farm Nightmare Carnax indefinitely. Disable this to defeat it once.",
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
        Bot.Options.SkipCutscenes = true;
        Bot.Config?.Configure();

        try
        {
            Run();
        }
        finally
        {
            StopZoneListener();
            LoneWolf.StopSkillEngine();

            if (lastStandRegistered)
                Core.CancelRegisteredQuests();
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
        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        PrepareDropsAndQuest();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);

        if (!PrepareSafeRoom(preset))
            return;

        StartZoneListener();

        if (!Sync("FIGHT_READY"))
            return;

        Core.Jump(BossCell, BossPad);

        if (!RunFightLoop(preset))
            return;

        Core.Jump(SafeCell, SafePad);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");
        farmNightmareCarnax = Bot.Config.Get<bool>("FarmNightmareCarnax");

        string playerThree = Bot.Config.Get<string>("player3")?.Trim() ?? string.Empty;
        string playerFour = Bot.Config.Get<string>("player4")?.Trim() ?? string.Empty;
        string playerFive = Bot.Config.Get<string>("player5")?.Trim() ?? string.Empty;

        if (
            string.IsNullOrEmpty(playerThree)
            && (!string.IsNullOrEmpty(playerFour) || !string.IsNullOrEmpty(playerFive))
        )
        {
            Core.Logger(
                "Player 3 is required when Player 4 or Player 5 is configured.",
                "ValidateOptions",
                messageBox: true
            );
            return false;
        }

        if (string.IsNullOrEmpty(playerFour) && !string.IsNullOrEmpty(playerFive))
        {
            Core.Logger(
                "Player 4 is required when Player 5 is configured.",
                "ValidateOptions",
                messageBox: true
            );
            return false;
        }

        armyPlayerCount = !string.IsNullOrEmpty(playerFive)
            ? 5
            : !string.IsNullOrEmpty(playerFour)
                ? 4
                : !string.IsNullOrEmpty(playerThree)
                    ? 3
                    : 2;

        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private void PrepareDropsAndQuest()
    {
        Core.AddDrop(SyntheticViscera, CalamitousRuin);

        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();

        if (Core.CheckInventory(SyntheticViscera, SyntheticVisceraMaxStack))
        {
            Core.Logger(
                $"{SyntheticViscera} is already at maximum stacks. {LastStandQuestName} registration skipped.",
                "PrepareDropsAndQuest"
            );
            return;
        }

        if (!LoneWolf.AcceptUltraQuest(LastStandQuestId))
        {
            Core.Logger(
                $"{LastStandQuestName} could not be accepted. Complete The Beast Awakens and Nightmare Containment Field to receive Synthetic Viscera. Continuing without quest rewards.",
                "PrepareDropsAndQuest"
            );
            return;
        }

        Core.RegisterQuests(LastStandQuestId);
        lastStandRegistered = true;
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

        return !Bot.ShouldExit;
    }

    private bool RunFightLoop(ClassPreset preset)
    {
        int fightCycle = 1;

        while (!Bot.ShouldExit)
        {
            if (!Fight(preset, fightCycle))
                return false;

            Core.Logger(
                $"{LogPrefix} {playerAlias} completed kill {fightCycle}."
            );

            if (!Sync($"NIGHTMARE_CARNAX_{fightCycle}_DEFEATED"))
                return false;

            if (!farmNightmareCarnax)
                return true;

            fightCycle++;

            if (!WaitForBossRespawn())
                return false;
        }

        return false;
    }

    private bool Fight(ClassPreset preset, int fightCycle)
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
            DrainZoneEvents(move: Bot.Player.Alive);

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    DrainZoneEvents(move: false);
                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");
                Core.Jump(BossCell, BossPad);
                continue;
            }

            bool bossAlive = Core.IsMonsterAlive(BossName);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            if (bossAlive && !HasBossTarget())
                Bot.Combat.Attack(BossName);

            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Nightmare Carnax defeated.");
        return true;
    }

    private bool WaitForBossRespawn()
    {
        Core.Logger($"{LogPrefix} {playerAlias} waiting for Nightmare Carnax to respawn.");

        while (!Bot.ShouldExit && !Core.IsMonsterAlive(BossName))
        {
            DrainZoneEvents(move: Bot.Player.Alive);
            Bot.Sleep(RespawnPollDelay);
        }

        return !Bot.ShouldExit;
    }

    private bool HasBossTarget() =>
        Bot.Player.HasTarget
        && string.Equals(
            Bot.Player.Target?.Name,
            BossName,
            StringComparison.OrdinalIgnoreCase
        );

    private void StartZoneListener()
    {
        lock (zoneLock)
            pendingZones.Clear();

        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;
    }

    private void StopZoneListener()
    {
        Bot.Events.RunToArea -= OnRunToArea;

        lock (zoneLock)
            pendingZones.Clear();
    }

    private void OnRunToArea(string zone)
    {
        if (
            !string.Equals(zone, "A", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(zone, "B", StringComparison.OrdinalIgnoreCase)
        )
            return;

        lock (zoneLock)
            pendingZones.Enqueue(zone.ToUpperInvariant());
    }

    private void DrainZoneEvents(bool move)
    {
        while (true)
        {
            string zone;
            lock (zoneLock)
            {
                if (pendingZones.Count == 0)
                    return;

                zone = pendingZones.Dequeue();
            }

            if (!move)
                continue;

            int y = Bot.Random.Next(380, 475);
            int x = zone == "A"
                ? Bot.Random.Next(600, 931)
                : Bot.Random.Next(25, 326);

            Bot.Player.WalkTo(x, y);
        }
    }

    private void StopFightCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(2))
        {
            ClassPreset dragonOfTime = LoneWolf.DragonOfTime();
            dragonOfTime.HelmEnhancement = HelmSpecial.None;
            return dragonOfTime;
        }

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(4))
            return LoneWolf.ArchPaladin();

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

        if (LoneWolf.IsArmyPlayer(4))
            return "playerFour";

        return "playerFive";
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
