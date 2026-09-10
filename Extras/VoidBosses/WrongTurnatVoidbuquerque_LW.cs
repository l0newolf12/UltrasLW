/*
name: Wrong Turn at Voidbuquerque LW
description: Runs the required Void bosses for the Wrong Turn at Voidbuquerque daily quest.
tags: void, daily, army, corelonewolf, master
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
//cs_include Scripts/UltrasLW/Extras/VoidBosses/VoidXyfrag_LW.cs
//cs_include Scripts/UltrasLW/Extras/VoidBosses/VoidFlibbitiestgibbet_LW.cs
//cs_include Scripts/UltrasLW/Extras/VoidBosses/VoidNightbane_LW.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class WrongTurnatVoidbuquerque_LW
{
    public enum DailyReward
    {
        TaintedGem,
        DarkCrystalShard,
        DiamondOfNulgath,
        TotemOfNulgath,
        GemOfNulgath,
        BloodGemOfTheArchfiend,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Wrong Turn at Voidbuquerque LW";
    private const string SyncFileName = "WrongTurnatVoidbuquerque_LW.sync";
    private const string QuestName = "Wrong Turn at Voidbuquerque";
    private const string PrerequisiteQuestName = "Break their Muti-kneecaps";
    private const string XyfragEssence = "Xyfrag's ??? Essence";
    private const string FlibbitiestgibbetEssence = "Flibbitiestgibbet's ??? Essence";
    private const string NightbaneEssence = "Nightbane's ??? Essence";
    private const int QuestId = 9091;

    private string playerAlias = string.Empty;
    private int armyPlayerCount;
    private bool questEligible;

    public string OptionsStorage = "WrongTurnatVoidbuquerque_LW";
    public bool DontPreconfigure = true;
    public string[] MultiOptions = { "Setup", "Void_Bosses" };

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

    public List<IOption> Setup = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        LoneWolf.player5,
        player6,
        player7,
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
        new Option<DailyReward>(
            "DailyReward",
            "Daily Reward",
            "Tainted Gem: 60\n"
                + "Dark Crystal Shard: 35\n"
                + "Diamond of Nulgath: 100\n"
                + "Totem of Nulgath: 2\n"
                + "Gem of Nulgath: 35\n"
                + "Blood Gem of the Archfiend: 7",
            DailyReward.TaintedGem
        ),
    };

    public List<IOption> Void_Bosses = new()
    {
        new Option<VoidXyfrag_LW.ArmyComposition>(
            "XyfragComposition",
            "Xyfrag Composition",
            "Default: KE / SC / AP / LOO / VDK / Bard / AF\n"
                + "Reliable: LR / SC / AP / LOO / VDK / Bard / Shaman\n"
                + "Stable: AF / SC / AP / LOO / VDK / Bard / Shaman",
            VoidXyfrag_LW.ArmyComposition.Default
        ),
        new Option<VoidFlibbitiestgibbet_LW.ArmyComposition>(
            "FlibbitiestgibbetComposition",
            "Flibbitiestgibbet Composition",
            "Default: KE / SC / AP / LOO / VDK / Bard / AF\n"
                + "Reliable: LR / SC / AP / LOO / VDK / Bard / AF\n"
                + "Stable: KE / SC / AP / LOO / VDK / Bard / AF",
            VoidFlibbitiestgibbet_LW.ArmyComposition.Default
        ),
        new Option<VoidNightbane_LW.ArmyComposition>(
            "NightbaneComposition",
            "Nightbane Composition",
            "Default: KE / SC / AP / LOO / VDK / Bard / AF\n"
                + "Reliable: LR / SC / AP / LOO / VDK / Bard / Shaman\n"
                + "Stable: KE / SC / AP / LOO / VDK / Bard / AF",
            VoidNightbane_LW.ArmyComposition.Default
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
            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();
        }
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!StartArmy())
            return;

        playerAlias = GetPlayerAlias();
        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        if (!PrepareQuest() || !Sync("QUEST_READY"))
            return;

        VoidXyfrag_LW xyfrag = new();
        if (
            !RunBossUntilReady(
                "XYFRAG",
                "Xyfrag",
                XyfragEssence,
                xyfrag.StartFromMaster,
                xyfrag.RunOnceFromMaster,
                xyfrag.StopFromMaster
            )
        )
            return;

        if (!MoveToHouse())
            return;

        VoidFlibbitiestgibbet_LW flibbitiestgibbet = new();
        if (
            !RunBossUntilReady(
                "FLIBBITIESTGIBBET",
                "Void Flibbitiestgibbet",
                FlibbitiestgibbetEssence,
                flibbitiestgibbet.StartFromMaster,
                flibbitiestgibbet.RunOnceFromMaster,
                flibbitiestgibbet.StopFromMaster
            )
        )
            return;

        if (!MoveToHouse())
            return;

        VoidNightbane_LW nightbane = new();
        if (
            !RunBossUntilReady(
                "NIGHTBANE",
                "Nightbane",
                NightbaneEssence,
                nightbane.StartFromMaster,
                nightbane.RunOnceFromMaster,
                nightbane.StopFromMaster
            )
        )
            return;

        if (!MoveToHouse())
            return;

        CompleteQuest();
        ShowFinalPrerequisiteWarning();

        if (Bot.ShouldExit || !Sync("QUEST_RESULT"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        int privateRoomNumber = Bot.Config!.Get<int>("Setup", "PrivateRoomNumber");
        if (!LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber))
            return false;

        string playerSeven = Bot.Config.Get<string>("Setup", "player7")?.Trim()
            ?? string.Empty;
        armyPlayerCount = string.IsNullOrEmpty(playerSeven) ? 6 : 7;
        return true;
    }

    private bool StartArmy() =>
        LoneWolf.StartArmySync(SyncFileName, armyPlayerCount, "Setup");

    private bool PrepareQuest()
    {
        if (Bot.Quests.IsDailyComplete(QuestId))
        {
            questEligible = false;
            Core.Logger(
                $"{QuestName} is already completed for {playerAlias}. This account will help the Army.",
                LogPrefix
            );
            return true;
        }

        bool unlocked = Bot.Quests.IsInProgress(QuestId)
            || Bot.Quests.IsUnlocked(QuestId);
        if (!unlocked)
        {
            questEligible = false;
            Core.Logger(
                $"{QuestName} is locked for {playerAlias}. Complete {PrerequisiteQuestName} before running this daily. This account will continue as a helper.",
                LogPrefix,
                messageBox: true
            );
            return true;
        }

        if (!Bot.Quests.IsInProgress(QuestId))
            Core.EnsureAccept(QuestId);

        questEligible = Bot.Quests.IsInProgress(QuestId);
        if (!questEligible)
        {
            Core.Logger(
                $"{QuestName} could not be accepted for {playerAlias}. This account will continue as a helper.",
                LogPrefix,
                messageBox: true
            );
            return true;
        }

        Core.Logger($"{QuestName} is active for {playerAlias}.", LogPrefix);
        return true;
    }

    private bool RunBossUntilReady(
        string phaseName,
        string bossName,
        string requiredEssence,
        Func<bool> startBoss,
        Func<bool> runBoss,
        Action stopBoss
    )
    {
        string readySignal = $"{phaseName}_ESSENCE_READY";
        int cycle = 0;
        int statusPass = 0;
        bool startAttempted = false;

        try
        {
            while (!Bot.ShouldExit)
            {
                bool localReady = !questEligible
                    || Bot.Inventory.Contains(requiredEssence);
                if (localReady)
                {
                    if (!LoneWolf.SendArmySignal(readySignal))
                        return false;

                    Core.Logger(
                        questEligible
                            ? $"{LogPrefix} {playerAlias} confirmed {requiredEssence}."
                            : $"{LogPrefix} {playerAlias} is exempt from the {requiredEssence} gate."
                    );
                }

                if (!Sync($"{phaseName}_{statusPass}_STATUS"))
                    return false;

                statusPass++;

                if (AllPlayersSignaled(readySignal))
                {
                    Core.Logger(
                        $"Every eligible account confirmed {requiredEssence}.",
                        LogPrefix
                    );
                    return true;
                }

                if (!startAttempted)
                {
                    startAttempted = true;
                    if (!startBoss())
                    {
                        return BossFailure(
                            $"{bossName} setup failed before the essence gate was ready."
                        );
                    }

                    continue;
                }

                Core.Logger($"Starting {bossName} kill {cycle + 1}.", LogPrefix);
                if (!runBoss())
                {
                    return BossFailure(
                        $"{bossName} failed before the essence gate was ready."
                    );
                }

                Bot.Sleep(1500);
                cycle++;
            }
        }
        finally
        {
            if (startAttempted)
                stopBoss();
        }

        return false;
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

    private bool MoveToHouse()
    {
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        return !Bot.ShouldExit;
    }

    private void CompleteQuest()
    {
        if (!questEligible || Bot.ShouldExit)
            return;

        int rewardItemId = GetRewardItemId(
            Bot.Config!.Get<DailyReward>("Setup", "DailyReward")
        );
        Core.EnsureComplete(QuestId, rewardItemId);

        if (Bot.Quests.IsDailyComplete(QuestId))
        {
            Core.Logger(
                $"{QuestName} completed for {playerAlias}.",
                LogPrefix
            );
            return;
        }

        Core.Logger(
            $"{QuestName} could not be completed for {playerAlias}. Other accounts will continue.",
            LogPrefix,
            messageBox: true
        );
    }

    private void ShowFinalPrerequisiteWarning()
    {
        if (
            Bot.Quests.IsDailyComplete(QuestId)
            || Bot.Quests.IsInProgress(QuestId)
            || Bot.Quests.IsUnlocked(QuestId)
        )
            return;

        Core.Logger(
            $"{QuestName} is still locked for {playerAlias}. Complete {PrerequisiteQuestName} before this account can turn in the daily.",
            LogPrefix,
            messageBox: true
        );
    }

    private static int GetRewardItemId(DailyReward reward) =>
        reward switch
        {
            DailyReward.TaintedGem => 4769,
            DailyReward.DarkCrystalShard => 4770,
            DailyReward.DiamondOfNulgath => 4771,
            DailyReward.TotemOfNulgath => 5357,
            DailyReward.GemOfNulgath => 6136,
            DailyReward.BloodGemOfTheArchfiend => 22332,
            _ => 4769,
        };

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

        if (LoneWolf.IsArmyPlayer(7))
            return "playerSeven";

        return "player";
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private bool BossFailure(string message)
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
