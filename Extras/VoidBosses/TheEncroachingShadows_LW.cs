/*
name: The Encroaching Shadows LW
description: Farms Flibbitiestgibbet, Warlord Icewing and Hydra Head 90 for the daily quest with four to six players.
tags: void aura, daily, army, corelonewolf, master
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
//cs_include Scripts/UltrasLW/Extras/VoidBosses/VoidFlibbitiestgibbet_LW.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Monsters;
using Skua.Core.Options;

#nullable enable

public class TheEncroachingShadows_LW
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "The Encroaching Shadows LW";
    private const string SyncFileName = "TheEncroachingShadows_LW.sync";
    private const string QuestName = "The Encroaching Shadows";
    private const int QuestId = 8653;
    private const int FlibbitigibletsId = 70054;
    private const int GlacialPinionId = 70052;
    private const int HydraEyeballId = 70053;
    private const int PollDelay = 500;

    private string playerAlias = string.Empty;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool questEligible;
    private bool routeSetupComplete;
    private ClassPreset? routePreset;
    private VoidFlibbitiestgibbet_LW.ArmyComposition armyComposition;

    public string OptionsStorage = "TheEncroachingShadows_LW";
    public bool DontPreconfigure = true;
    public string[] MultiOptions = { "Setup", "Void_Bosses" };

    public List<IOption> Setup = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        VoidFlibbitiestgibbet_LW.player5,
        VoidFlibbitiestgibbet_LW.player6,
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<bool>(
            "UseEnhancements",
            "Use Enhancements",
            "Prepare the assigned enhancement loadouts.",
            true
        ),
        new Option<bool>(
            "UsePotions",
            "Use Potions",
            "Prepare and use the assigned potion loadouts.",
            true
        ),
    };

    public List<IOption> Void_Bosses = new()
    {
        new Option<VoidFlibbitiestgibbet_LW.ArmyComposition>(
            "FlibbitiestgibbetComposition",
            "Army Composition",
            "Default: KE / SC / AP / LOO / VDK / Bard\n"
                + "Reliable: LR / SC / AP / LOO / VDK / Bard\n"
                + "Stable: KE / SC / AP / LOO / VDK / Bard",
            VoidFlibbitiestgibbet_LW.ArmyComposition.Default
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
            StopCombat();
        }
    }

    private void Run()
    {
        routeSetupComplete = false;
        routePreset = null;
        if (!ValidateOptions()
            || !LoneWolf.StartArmySync(SyncFileName, armyPlayerCount, "Setup"))
            return;

        playerAlias = GetPlayerAlias();
        Core.Logger($"{LogPrefix} started as {playerAlias} using {armyComposition} composition.");
        Core.BankingBlackList.AddRange(new[]
        {
            "Flibbitigiblets", "Glacial Pinion", "Hydra Eyeball",
        });

        if (!PrepareQuest() || !Sync("QUEST_READY"))
            return;

        if (!RunFlibbitiestgibbet() || !MoveToHouse())
            return;

        if (!RunFarmStage("ICEWING", "icewing", "Enter", "Spawn", new[] { 1 },
                GlacialPinionId, "Glacial Pinion", 1)
            || !MoveToHouse())
            return;

        if (!RunFarmStage("HYDRA", "hydrachallenge", "h90", "Left", new[] { 32, 33, 34 },
                HydraEyeballId, "Hydra Eyeball", 3)
            || !MoveToHouse())
            return;

        CompleteQuest();
        if (!Bot.ShouldExit && Sync("QUEST_RESULT"))
            StopArmy();
    }

    private bool ValidateOptions()
    {
        privateRoomNumber = Bot.Config!.Get<int>("Setup", "PrivateRoomNumber");
        if (!LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber))
            return false;

        armyComposition = Bot.Config.Get<VoidFlibbitiestgibbet_LW.ArmyComposition>(
            "Void_Bosses", "FlibbitiestgibbetComposition");
        string playerFive = Bot.Config.Get<string>("Setup", "player5")?.Trim() ?? string.Empty;
        string playerSix = Bot.Config.Get<string>("Setup", "player6")?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(playerFive) && !string.IsNullOrEmpty(playerSix))
        {
            Core.Logger("Player 5 is required when Player 6 is configured.",
                LogPrefix, messageBox: true);
            return false;
        }

        armyPlayerCount = !string.IsNullOrEmpty(playerSix) ? 6
            : !string.IsNullOrEmpty(playerFive) ? 5 : 4;
        return true;
    }

    private bool PrepareQuest()
    {
        if (Bot.Quests.IsDailyComplete(QuestId))
        {
            Core.Logger($"{QuestName} is already completed for {playerAlias}. This account will help the Army.",
                LogPrefix);
            return true;
        }

        if (!Bot.Quests.IsInProgress(QuestId) && !Bot.Quests.IsUnlocked(QuestId))
        {
            Core.Logger($"{QuestName} is locked for {playerAlias}. Unlock it before running this daily. This account will continue as a helper.",
                LogPrefix, messageBox: true);
            return true;
        }

        Bot.Bank.Load();
        if (Bot.ShouldExit)
            return false;
        if (!Bot.Quests.IsInProgress(QuestId))
            Core.EnsureAccept(QuestId);

        questEligible = Bot.Quests.IsInProgress(QuestId);
        if (!questEligible)
        {
            Core.Logger($"{QuestName} could not be accepted for {playerAlias}. This account will continue as a helper.",
                LogPrefix, messageBox: true);
            return !Bot.ShouldExit;
        }

        Core.AddDrop("Flibbitigiblets", "Glacial Pinion", "Hydra Eyeball");
        Core.AddDrop(FlibbitigibletsId, GlacialPinionId, HydraEyeballId);
        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();

        Core.Logger($"{QuestName} is active for {playerAlias}.", LogPrefix);
        return !Bot.ShouldExit;
    }

    private bool RunFlibbitiestgibbet()
    {
        const string signal = "FLIBBITIESTGIBBET_ITEM_READY";
        VoidFlibbitiestgibbet_LW child = new();
        bool readySent = false;
        bool startAttempted = false;
        int statusPass = 0;
        int cycle = 0;

        try
        {
            while (!Bot.ShouldExit)
            {
                if (!PublishReady(signal, FlibbitigibletsId, "Flibbitigiblets", 1, ref readySent)
                    || !Sync($"FLIBBITIESTGIBBET_{statusPass}_STATUS"))
                    return false;

                statusPass++;
                if (AllPlayersSignaled(signal))
                {
                    Core.Logger("Every eligible account confirmed Flibbitigiblets.", LogPrefix);
                    return true;
                }

                if (!startAttempted)
                {
                    if (string.Equals(Bot.Map.Name, "voidflibbi", StringComparison.OrdinalIgnoreCase)
                        && !MoveToHouse())
                        return false;

                    startAttempted = true;
                    if (!child.StartFromMaster())
                        return Failure("Flibbitiestgibbet setup failed before the item gate was ready.");
                    if (!VerifyRoom("voidflibbi"))
                        return false;
                    routePreset ??= GetClassPreset();
                    routeSetupComplete = true;
                    continue;
                }

                Core.Logger($"Starting Flibbitiestgibbet kill {cycle + 1}.", LogPrefix);
                if (!child.RunOnceFromMaster())
                    return Failure("Flibbitiestgibbet failed before the item gate was ready.");

                Bot.Sleep(1500);
                cycle++;
            }
        }
        finally
        {
            if (startAttempted)
                child.StopFromMaster();
        }

        return false;
    }

    private bool RunFarmStage(string phase, string map, string fightCell, string fightPad, int[] targetMapIds,
        int itemId, string itemName, int quantity)
    {
        string signal = $"{phase}_ITEM_READY";
        bool readySent = false;
        if (!PublishReady(signal, itemId, itemName, quantity, ref readySent)
            || !Sync($"{phase}_INITIAL_STATUS"))
            return false;
        if (AllPlayersSignaled(signal))
            return true;

        ClassPreset preset = routePreset ??= GetClassPreset();
        if (!EnsureRouteSetup(preset))
            return false;

        if (string.Equals(Bot.Map.Name, map, StringComparison.OrdinalIgnoreCase)
            && !MoveToHouse())
            return false;
        Core.Join($"{map}-{privateRoomNumber}");
        if (!VerifyRoom(map))
            return false;

        if (!JumpTo(fightCell, fightPad))
            return false;
        if (Bot.ShouldExit || !Sync($"{phase}_FIGHT_READY"))
            return false;

        try
        {
            LoneWolf.StartSkillEngine(preset.Skills, playerAlias, false, LogPrefix, preset.SkillMode);
            int targetMapId = 0;
            bool deathLogged = false;
            while (!Bot.ShouldExit)
            {
                if (!PublishReady(signal, itemId, itemName, quantity, ref readySent))
                    return false;
                if (!Bot.Player.Alive)
                {
                    if (!deathLogged)
                        Core.Logger($"{LogPrefix} {playerAlias} died during {phase}.");
                    deathLogged = true;
                    targetMapId = 0;
                    Bot.Sleep(PollDelay);
                    continue;
                }

                bool needsReentry = deathLogged
                    || !string.Equals(Bot.Player.Cell, fightCell, StringComparison.OrdinalIgnoreCase);
                if (deathLogged)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} respawned during {phase}.");
                    deathLogged = false;
                }
                if (AllPlayersSignaled(signal))
                {
                    StopCombat();
                    if (phase == "HYDRA" && !JumpTo("Enter", "Spawn"))
                        return false;
                    Core.Logger($"Every eligible account confirmed {itemName} x{quantity}.", LogPrefix);
                    return Sync($"{phase}_COMPLETE");
                }

                if (!VerifyRoom(map))
                    return false;
                if (needsReentry)
                {
                    if (!JumpTo(fightCell, fightPad))
                        return false;
                    targetMapId = 0;
                    Bot.Sleep(PollDelay);
                    continue;
                }

                // Re-read after arriving. Retain a living selected instance across polls.
                var monsters = Bot.Monsters.MapMonsters;
                targetMapId = SelectTarget(monsters, targetMapId, targetMapIds, fightCell);
                if (targetMapId > 0)
                    LoneWolf.MaintainTarget(targetMapId);
                Bot.Sleep(PollDelay);
            }
        }
        finally
        {
            StopCombat();
        }

        return false;
    }

    private static int SelectTarget(IEnumerable<Monster> monsters, int selectedMapId,
        int[] targetMapIds, string fightCell)
    {
        var matching = monsters.Where(monster => monster != null
            && targetMapIds.Contains(monster.MapID) && monster.HP > 0
            && string.Equals(monster.Cell, fightCell, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matching.Any(monster => monster.MapID == selectedMapId)
            ? selectedMapId
            : matching.OrderBy(monster => monster.MapID).FirstOrDefault()?.MapID ?? 0;
    }

    private bool EnsureRouteSetup(ClassPreset preset)
    {
        if (routeSetupComplete)
            return true;

        // The Flibbi child normally owns setup. This path is only for a skipped Flibbi stage.
        if (!LoneWolf.ValidateUltraAccess(0, 0, string.Empty, 80, LogPrefix, preset.ClassName)
            || !Prepare(preset))
            return false;
        if (Bot.Config!.Get<bool>("Setup", "UsePotions"))
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        if (Bot.ShouldExit || !Sync("ROUTE_SETUP_DONE"))
            return false;

        routeSetupComplete = true;
        return true;
    }

    private bool JumpTo(string cell, string pad)
    {
        if (Bot.ShouldExit)
            return false;
        Core.Jump(cell, pad);
        if (Bot.ShouldExit)
            return false;

        // Core.Jump can return early in the correct cell even when its pad differs.
        if (!string.Equals(Bot.Player.Cell, cell, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Bot.Player.Pad, pad, StringComparison.OrdinalIgnoreCase))
        {
            Bot.Map.Jump(cell, pad, autoCorrect: false, clientOnly: false);
            Bot.Wait.ForTrue(() => Bot.ShouldExit
                || (string.Equals(Bot.Player.Cell, cell, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Bot.Player.Pad, pad, StringComparison.OrdinalIgnoreCase)), 40);
            if (Bot.ShouldExit)
                return false;
            Bot.Player.SetSpawnPoint();
        }

        return (string.Equals(Bot.Player.Cell, cell, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Bot.Player.Pad, pad, StringComparison.OrdinalIgnoreCase))
            || Failure($"{playerAlias} could not reach {cell} at {pad}.");
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");
        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;
        if (Bot.Config!.Get<bool>("Setup", "UseEnhancements"))
        {
            LoneWolf.PrepareEnhancements(preset.BaseEnhancement, preset.CapeEnhancement,
                preset.HelmEnhancement, preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks);
        }
        if (Bot.Config.Get<bool>("Setup", "UsePotions"))
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        return !Bot.ShouldExit;
    }

    private ClassPreset GetClassPreset()
    {
        ClassPreset preset;
        if (LoneWolf.IsArmyPlayer(1))
            preset = armyComposition == VoidFlibbitiestgibbet_LW.ArmyComposition.Reliable
                ? LoneWolf.LegionRevenant() : LoneWolf.KingsEcho();
        else if (LoneWolf.IsArmyPlayer(2))
            preset = LoneWolf.StoneCrusher();
        else if (LoneWolf.IsArmyPlayer(3))
            preset = LoneWolf.ArchPaladin();
        else if (LoneWolf.IsArmyPlayer(4))
            preset = LoneWolf.LordOfOrder();
        else if (LoneWolf.IsArmyPlayer(5))
            preset = LoneWolf.VerusDoomKnight();
        else
            preset = LoneWolf.Bard();

        if (armyComposition == VoidFlibbitiestgibbet_LW.ArmyComposition.Default
            && preset.CapeEnhancement == CapeSpecial.Vainglory)
            preset.CapeEnhancement = CapeSpecial.Lament;
        return preset;
    }

    private bool PublishReady(string signal, int itemId, string itemName,
        int quantity, ref bool readySent)
    {
        if (readySent || (questEligible && Bot.Inventory.GetQuantity(itemId) < quantity))
            return true;
        if (!LoneWolf.SendArmySignal(signal))
            return false;

        readySent = true;
        Core.Logger(questEligible
            ? $"{LogPrefix} {playerAlias} confirmed {itemName} x{quantity}."
            : $"{LogPrefix} {playerAlias} is exempt from the {itemName} gate.");
        return true;
    }

    private bool AllPlayersSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= armyPlayerCount; playerNumber++)
        {
            if (!LoneWolf.HasArmySignal(signal, playerNumber))
                return false;
        }
        return true;
    }

    private bool VerifyRoom(string map) => !Bot.ShouldExit
        && (string.Equals(Bot.Map.FullName, $"{map}-{privateRoomNumber}",
                StringComparison.OrdinalIgnoreCase)
            || Failure($"The configured private {map} room could not be reached."));

    private bool MoveToHouse()
    {
        StopCombat();
        if (Bot.ShouldExit)
            return false;
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        return !Bot.ShouldExit
            && (string.Equals(Bot.Map.Name, "house", StringComparison.OrdinalIgnoreCase)
                || Failure($"{playerAlias} could not reach their house."));
    }

    private void CompleteQuest()
    {
        if (!questEligible || Bot.ShouldExit)
            return;
        Core.EnsureComplete(QuestId);
        Bot.Wait.ForTrue(() => Bot.ShouldExit || Bot.Quests.IsDailyComplete(QuestId), 40);
        if (Bot.ShouldExit)
            return;
        if (Bot.Quests.IsDailyComplete(QuestId))
        {
            Core.Logger($"{QuestName} completed for {playerAlias}.", LogPrefix);
            return;
        }
        Core.Logger($"{QuestName} could not be completed for {playerAlias}. Other accounts will continue.",
            LogPrefix, messageBox: true);
    }

    private void StopCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private string GetPlayerAlias()
    {
        if (LoneWolf.IsArmyPlayer(1)) return "playerOne";
        if (LoneWolf.IsArmyPlayer(2)) return "playerTwo";
        if (LoneWolf.IsArmyPlayer(3)) return "playerThree";
        if (LoneWolf.IsArmyPlayer(4)) return "playerFour";
        if (LoneWolf.IsArmyPlayer(5)) return "playerFive";
        return "playerSix";
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");
        if (!LoneWolf.SyncArmy(step))
            return false;
        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private bool Failure(string message)
    {
        if (Bot.ShouldExit)
            Core.Logger(message, LogPrefix);
        else
            Core.Logger(message, LogPrefix, messageBox: true, stopBot: true);
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
