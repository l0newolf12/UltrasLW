/*
name: Army Legion Tokens LW
description: Four-to-seven-player Legion Token farm with optional helper accounts.
tags: legion token, legionarena, first class entertainment, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class ArmyLegionTokens_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Unselected,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Army Legion Tokens LW";
    private const string SyncFileName = "ArmyLegionTokens_LW.sync";
    private const string MapName = "legionarena";
    private const string FightCell = "Boss";
    private const string FightPad = "Left";
    private const string LegionToken = "Legion Token";
    private const string GoalSignal = "TOKEN_GOAL_RESOLVED";
    private const int QuestId = 6743;
    private const int PrerequisiteQuestId = 793;
    private const int LegionTokenId = 6521;
    private const int TokenCap = 50_000;
    private const int TargetMapId = 10;
    private const int FarmPollDelay = 100;
    private const int CompletionPollDelay = 500;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private bool eligible;
    private bool goalResolved;
    private bool questRegistered;
    private bool originalInfiniteRange;
    private bool originalAggroMonsters;
    private bool originalAggroAllMonsters;
    private bool antiLagApplied;
    private bool originalLagKiller;
    private bool originalHidePlayers;
    private bool originalDisableSelfAnimation;
    private bool originalDisableMonsterAnimation;
    private bool originalDisableSkillAnimation;
    private bool originalDisableDamageStrobe;
    private bool originalMonstersHidden;

    public string OptionsStorage = "ArmyLegionTokens_LW";
    public bool DontPreconfigure = true;
    public static Option<string> player5 = new(
        "player5", "Player 5 (Optional)", "Player 5 (Optional) account name.", string.Empty
    );
    public static Option<string> player6 = new(
        "player6", "Player 6 (Optional)", "Player 6 (Optional) account name.", string.Empty
    );
    public static Option<string> player7 = new(
        "player7", "Player 7 (Optional)", "Player 7 (Optional) account name.", string.Empty
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
            "Default: LR / SC / AP / LOO / Shaman / Bard / VDK\nStable: AF / SC / AP / LOO / Shaman / Bard / VDK\nUnselected: Current Class / Skua Advanced Skills / No Enhancements or Potions",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber", "Private Room Number",
            "Private room number from 1001 through 99999.", 0
        ),
        new Option<bool>(
            "UsePotions", "Use Potions",
            "Use owned inventory potions only. No acquisition or restocking.", false
        ),
        new Option<bool>(
            "UseEnhancements", "Use Enhancements",
            "Prepare the assigned enhancement loadout.", true
        ),
        new Option<bool>(
            "EnableAntiLag", "Enable AntiLag",
            "Temporarily enable AntiLag and disable visual effects while the script runs.", true
        ),
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Config?.Configure();
        originalInfiniteRange = Bot.Options.InfiniteRange;
        originalAggroMonsters = Bot.Options.AggroMonsters;
        originalAggroAllMonsters = Bot.Options.AggroAllMonsters;

        try
        {
            StopCombat();
            Bot.Options.InfiniteRange = true;
            ApplyAntiLag();
            Run();
        }
        finally
        {
            try
            {
                StopCombat();
                CancelQuest();
            }
            finally
            {
                RestoreAntiLag();
                Bot.Options.InfiniteRange = originalInfiniteRange;
                Bot.Options.AggroMonsters = originalAggroMonsters;
                Bot.Options.AggroAllMonsters = originalAggroAllMonsters;
            }
        }
    }

    private void Run()
    {
        goalResolved = false;
        questRegistered = false;
        if (!ValidateOptions() || !LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        playerAlias = GetPlayerAlias();
        eligible = CanEarnTokens();
        Core.BankingBlackList.Add(LegionToken);
        Core.AddDrop(LegionToken);
        Core.AddDrop(LegionTokenId);
        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();

        if (!PublishLocalCompletion())
            return;

        if (eligible && !PrepareTokens())
            return;

        ClassPreset preset = GetClassPreset();
        if (!Prepare(preset) || !PublishLocalCompletion() || !Sync("SETUP_DONE"))
            return;

        if (!AllPlayersResolved() && !Farm(preset))
            return;

        StopCombat();
        if (Bot.ShouldExit)
            return;

        if (string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase))
            Core.Jump("Enter", "Spawn");

        CancelQuest();
        if (!MoveToHouse() || !Sync("PARKED"))
            return;

        StopArmy();
    }

    private bool CanEarnTokens()
    {
        if (
            Bot.Quests.HasBeenCompleted(PrerequisiteQuestId)
            && Bot.Quests.IsUnlocked(QuestId)
        )
            return true;

        Core.Logger(
            $"{LogPrefix} {playerAlias} must complete Fail to the King and unlock First Class Entertainment to earn Legion Tokens. Continuing as a helper without quest registration or a token goal.",
            "CanEarnTokens",
            messageBox: true
        );
        return false;
    }

    private bool PrepareTokens()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();
        Bot.Bank.Load();
        Bot.Bank.Loaded = true;

        int bankQuantity = Bot.Bank.GetQuantity(LegionToken);
        if (bankQuantity <= 0)
            return !Bot.ShouldExit;

        if (!Core.HasSpaceFor(Bot.Bank.GetItem(LegionToken)))
            return Fatal("Legion Token cannot be moved from bank because the inventory is full.", "PrepareTokens");

        int expectedQuantity = Math.Min(TokenCap, Bot.Inventory.GetQuantity(LegionToken) + bankQuantity);
        Bot.Bank.EnsureToInventory(LegionToken, loadBank: false);
        Bot.Wait.ForTrue(() => Bot.Inventory.GetQuantity(LegionToken) >= expectedQuantity, 20);
        return !Bot.ShouldExit && (
            Bot.Inventory.GetQuantity(LegionToken) >= expectedQuantity
            || Fatal("The banked Legion Token stack could not be moved completely.", "PrepareTokens")
        );
    }

    private bool Prepare(ClassPreset preset)
    {
        if (armyComposition != ArmyComposition.Unselected)
        {
            LoneWolf.EquipClass(preset);
            if (Bot.ShouldExit)
                return false;
            if (Bot.Config!.Get<bool>("UseEnhancements"))
                LoneWolf.PrepareEnhancements(
                    preset.BaseEnhancement,
                    preset.CapeEnhancement,
                    preset.HelmEnhancement,
                    preset.WeaponEnhancement,
                    weaponFallbacks: preset.WeaponEnhancementFallbacks
                );
        }
        return !Bot.ShouldExit;
    }

    private bool Farm(ClassPreset preset)
    {
        if (armyComposition != ArmyComposition.Unselected && Bot.Config!.Get<bool>("UsePotions"))
        {
            Core.Join("battleon");
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        }
        if (Bot.ShouldExit || !JoinFarmRoom() || !PublishLocalCompletion())
            return false;

        if (eligible && !goalResolved)
        {
            questRegistered = true;
            Core.RegisterQuests(QuestId);
        }
        if (Bot.ShouldExit)
            return false;

        Bot.Options.AggroMonsters = true;
        if (armyComposition == ArmyComposition.Unselected)
            Bot.Skills.Start();
        else
            LoneWolf.StartSkillEngine(
                preset.Skills, playerAlias, false, LogPrefix, preset.SkillMode,
                maintainedPotion: Bot.Config!.Get<bool>("UsePotions") ? preset.CombatPotion : null
            );

        Core.Logger($"{LogPrefix} {playerAlias} farming Legion Fiend Rider.");
        long nextCompletionCheck = 0;
        bool deathLogged = false;
        while (!Bot.ShouldExit)
        {
            long now = Environment.TickCount64;
            if (now >= nextCompletionCheck)
            {
                if (!PublishLocalCompletion())
                    return false;
                if (AllPlayersResolved())
                    return !Bot.ShouldExit;
                nextCompletionCheck = now + CompletionPollDelay;
            }

            if (!Bot.Player.Alive)
            {
                if (!deathLogged)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died. Waiting to respawn.");
                    deathLogged = true;
                }
                Bot.Sleep(RespawnPollDelay);
                continue;
            }

            if (!JoinFarmRoom())
                return false;
            if (deathLogged)
            {
                Core.Logger($"{LogPrefix} {playerAlias} respawned and is continuing to help.");
                deathLogged = false;
            }
            Bot.Options.AggroMonsters = true;
            if (LoneWolf.IsMonsterAlive(TargetMapId))
                LoneWolf.MaintainTarget(TargetMapId);
            Bot.Sleep(FarmPollDelay);
        }
        return false;
    }

    private bool JoinFarmRoom()
    {
        string fullMapName = $"{MapName}-{privateRoomNumber}";
        string currentFullMapName = Bot.Map.FullName;
        if (!string.Equals(currentFullMapName, fullMapName, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase))
                Core.Join("battleon");
            Core.Join(fullMapName, FightCell, FightPad);
            currentFullMapName = Bot.Map.FullName;
        }
        if (Bot.ShouldExit)
            return false;
        if (!string.Equals(currentFullMapName, fullMapName, StringComparison.OrdinalIgnoreCase))
            return Fatal("The configured private legionarena room could not be reached.", "JoinFarmRoom");

        if (
            !string.Equals(Bot.Player.Cell, FightCell, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Bot.Player.Pad, FightPad, StringComparison.OrdinalIgnoreCase)
        )
            Core.Jump(FightCell, FightPad);

        return !Bot.ShouldExit && (
            (
                string.Equals(Bot.Player.Cell, FightCell, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Bot.Player.Pad, FightPad, StringComparison.OrdinalIgnoreCase)
            )
            || Fatal("The Legion Fiend Rider fight cell could not be reached.", "JoinFarmRoom")
        );
    }

    private bool PublishLocalCompletion()
    {
        if (goalResolved || Bot.ShouldExit)
            return !Bot.ShouldExit;
        if (eligible && Bot.Inventory.GetQuantity(LegionToken) < TokenCap)
            return true;

        CancelQuest();
        if (!LoneWolf.SendArmySignal(GoalSignal))
            return false;
        goalResolved = true;
        Core.Logger(eligible
            ? $"{LogPrefix} {playerAlias} reached 50000 Legion Tokens and is continuing to help."
            : $"{LogPrefix} {playerAlias} is helping without a token goal.");
        return true;
    }

    private bool AllPlayersResolved()
    {
        for (int playerNumber = 1; playerNumber <= armyPlayerCount; playerNumber++)
            if (!LoneWolf.HasArmySignal(GoalSignal, playerNumber))
                return false;
        return true;
    }

    private void CancelQuest()
    {
        if (!questRegistered)
            return;
        Core.CancelRegisteredQuests();
        questRegistered = false;
    }

    private void StopCombat()
    {
        Bot.Options.AggroMonsters = false;
        Bot.Options.AggroAllMonsters = false;
        LoneWolf.StopSkillEngine();
        Bot.Skills.Stop();
        Bot.Combat.CancelTarget();
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

    private void ApplyAntiLag()
    {
        if (!Bot.Config!.Get<bool>("EnableAntiLag"))
            return;

        originalLagKiller = Bot.Options.LagKiller;
        originalHidePlayers = Bot.Options.HidePlayers;
        originalDisableSelfAnimation = Bot.Lite.DisableSelfAnimation;
        originalDisableMonsterAnimation = Bot.Lite.DisableMonsterAnimation;
        originalDisableSkillAnimation = Bot.Lite.DisableSkillAnimation;
        originalDisableDamageStrobe = Bot.Lite.DisableDamageStrobe;
        originalMonstersHidden = Bot.Flash.GetGameObject<bool>(
            "ui.monsterIcon.redX.visible"
        );
        antiLagApplied = true;

        Bot.Options.LagKiller = true;
        Bot.Options.HidePlayers = true;
        Bot.Lite.DisableSelfAnimation = true;
        Bot.Lite.DisableMonsterAnimation = true;
        Bot.Lite.DisableSkillAnimation = true;
        Bot.Lite.DisableDamageStrobe = true;

        if (!originalMonstersHidden)
            Bot.Flash.CallGameFunction("world.toggleMonsters");
    }

    private void RestoreAntiLag()
    {
        if (!antiLagApplied)
            return;

        Bot.Options.LagKiller = originalLagKiller;
        Bot.Options.HidePlayers = originalHidePlayers;
        Bot.Lite.DisableSelfAnimation = originalDisableSelfAnimation;
        Bot.Lite.DisableMonsterAnimation = originalDisableMonsterAnimation;
        Bot.Lite.DisableSkillAnimation = originalDisableSkillAnimation;
        Bot.Lite.DisableDamageStrobe = originalDisableDamageStrobe;

        bool monstersHidden = Bot.Flash.GetGameObject<bool>(
            "ui.monsterIcon.redX.visible"
        );
        if (monstersHidden != originalMonstersHidden)
            Bot.Flash.CallGameFunction("world.toggleMonsters");

        antiLagApplied = false;
    }

    private ClassPreset GetClassPreset()
    {
        if (armyComposition == ArmyComposition.Unselected)
        {
            return new ClassPreset
            {
                ClassName = Bot.Player.CurrentClass?.Name ?? "Current Class",
            };
        }

        if (LoneWolf.IsArmyPlayer(1))
        {
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
        {
            ClassPreset shaman = LoneWolf.Shaman(farmMode: true);
            shaman.HelmEnhancement = HelmSpecial.Examen;
            return shaman;
        }

        if (LoneWolf.IsArmyPlayer(6))
            return LoneWolf.Bard();

        return LoneWolf.VerusDoomKnight();
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

    private bool MoveToHouse()
    {
        if (Bot.ShouldExit)
            return false;

        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        if (Bot.ShouldExit)
            return false;

        return string.Equals(Bot.Map.Name, "house", StringComparison.OrdinalIgnoreCase)
            || Fatal($"{LogPrefix} {playerAlias} could not reach their house.", "MoveToHouse");
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
