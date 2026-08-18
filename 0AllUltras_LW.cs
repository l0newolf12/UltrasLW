/*
name: All Ultras LW
description: Runs selected LoneWolf four-player Ultra scripts in order.
tags: ultra, army, corelonewolf, master
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
//cs_include Scripts/UltrasLW/Dailies/UltraEzrajal_LW.cs
//cs_include Scripts/UltrasLW/Dailies/UltraWarden_LW.cs
//cs_include Scripts/UltrasLW/Dailies/UltraEngineer_LW.cs
//cs_include Scripts/UltrasLW/Dailies/UltraTyndarius_LW.cs
//cs_include Scripts/UltrasLW/UltraDrakath_LW.cs
//cs_include Scripts/UltrasLW/UltraDrago_LW.cs
//cs_include Scripts/UltrasLW/UltraNulgath_LW.cs
//cs_include Scripts/UltrasLW/UltraDage_LW.cs
//cs_include Scripts/UltrasLW/UltraDarkon_LW.cs
//cs_include Scripts/UltrasLW/UltraGramiel_LW.cs
//cs_include Scripts/UltrasLW/UltraSpeaker_LW.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class AllUltras_LW
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "All Ultras LW";
    private const string CompletionSyncFileName = "0AllUltras_Completion.sync";
    private const string CompletionSignalPrefix = "ULTRA_REQUIRED_";
    private const int EzrajalBit = 1 << 0;
    private const int WardenBit = 1 << 1;
    private const int EngineerBit = 1 << 2;
    private const int TyndariusBit = 1 << 3;
    private const int DrakathBit = 1 << 4;
    private const int DragoBit = 1 << 5;
    private const int NulgathBit = 1 << 6;
    private const int DageBit = 1 << 7;
    private const int DarkonBit = 1 << 8;
    private const int GramielBit = 1 << 9;
    private const int SpeakerBit = 1 << 10;

    private static readonly int[] UltraBits =
    {
        EzrajalBit,
        WardenBit,
        EngineerBit,
        TyndariusBit,
        DrakathBit,
        DragoBit,
        NulgathBit,
        DageBit,
        DarkonBit,
        GramielBit,
        SpeakerBit,
    };

    private bool skipCompletedUltras;
    private int partyRequiredMask;
    private int actualRunMask;
    private int remainingUltras;

    public string OptionsStorage = "0AllUltras_LW";
    public bool DontPreconfigure = true;
    public string[] MultiOptions = { "Setup", "Daily_Ultras", "Weekly_Ultras" };

    public List<IOption> Setup = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
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
        new Option<bool>(
            "SkipCompletedUltras",
            "Skip Completed Ultras?",
            "Skip an Ultra when all four accounts completed its quest.",
            true
        ),
    };

    public List<IOption> Daily_Ultras = new()
    {
        new Option<bool>(
            "RunUltraEzrajal",
            "Run Ultra Ezrajal",
            "Run Ultra Ezrajal.",
            true
        ),
        new Option<UltraEzrajal_LW.ArmyComposition>(
            "UltraEzrajalComposition",
            "→ Ultra Ezrajal Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
            UltraEzrajal_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "UltraEzrajalSkipManaLock",
            "→ Skip Mana Lock?",
            "Skip the Battle Oracle Battlestaff Mana Lock preparation.",
            false
        ),
        new Option<bool>(
            "RunUltraWarden",
            "Run Ultra Warden",
            "Run Ultra Warden.",
            true
        ),
        new Option<UltraWarden_LW.ArmyComposition>(
            "UltraWardenComposition",
            "→ Ultra Warden Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
            UltraWarden_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraEngineer",
            "Run Ultra Engineer",
            "Run Ultra Engineer.",
            true
        ),
        new Option<UltraEngineer_LW.ArmyComposition>(
            "UltraEngineerComposition",
            "→ Ultra Engineer Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
            UltraEngineer_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraTyndarius",
            "Run Ultra Tyndarius",
            "Run Ultra Tyndarius.",
            true
        ),
        new Option<UltraTyndarius_LW.ArmyComposition>(
            "UltraTyndariusComposition",
            "→ Ultra Tyndarius Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nFast: AI / SC / AP / LOO\nTest: LR / SC / AP / LOO",
            UltraTyndarius_LW.ArmyComposition.Default
        ),
    };

    public List<IOption> Weekly_Ultras = new()
    {
        new Option<bool>(
            "RunChampionDrakath",
            "Run Champion Drakath",
            "Run Champion Drakath.",
            true
        ),
        new Option<UltraDrakath_LW.ArmyComposition>(
            "ChampionDrakathComposition",
            "→ Champion Drakath Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nOptimized: Chaos Slayer / SC / AP / LOO",
            UltraDrakath_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraDrago",
            "Run Ultra Drago",
            "Run Ultra Drago.",
            true
        ),
        new Option<UltraDrago_LW.ArmyComposition>(
            "UltraDragoComposition",
            "→ Ultra Drago Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
            UltraDrago_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraNulgath",
            "Run Ultra Nulgath",
            "Run Ultra Nulgath.",
            true
        ),
        new Option<UltraNulgath_LW.ArmyComposition>(
            "UltraNulgathComposition",
            "→ Ultra Nulgath Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO\nOptimized: DOT / DOT / LR / LOO",
            UltraNulgath_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraDage",
            "Run Ultra Dage",
            "Run Ultra Dage.",
            true
        ),
        new Option<UltraDage_LW.ArmyComposition>(
            "UltraDageComposition",
            "→ Ultra Dage Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
            UltraDage_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraDarkon",
            "Run Ultra Darkon",
            "Run Ultra Darkon.",
            true
        ),
        new Option<UltraDarkon_LW.ArmyComposition>(
            "UltraDarkonComposition",
            "→ Ultra Darkon Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nOptimized: LC / SC / AP / LOO\nTest: LR / SC / AP / LOO\nTest2: VDK / SC / AP / LOO",
            UltraDarkon_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraGramiel",
            "Run Ultra Gramiel",
            "Run Ultra Gramiel.",
            true
        ),
        new Option<UltraGramiel_LW.ArmyComposition>(
            "UltraGramielComposition",
            "→ Ultra Gramiel Composition",
            "Default: LR / SC / AP / LOO\nOptimized: Shaman / SC / AP / LOO\nTest: LR / SC / AP / LOO\nTest2: Shaman / SC / AP / LOO\nTest3: VDK / SC / AP / LOO\nTest4: VDK / SC / AP / LOO",
            UltraGramiel_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "RunUltraSpeaker",
            "Run Ultra Speaker",
            "Run Ultra Speaker.",
            true
        ),
        new Option<UltraSpeaker_LW.ArmyComposition>(
            "UltraSpeakerComposition",
            "→ Ultra Speaker Composition",
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO\nPay2Win: Guardian / SC / LR / AP\nTest: LR / SC / AP / LOO\nTest3: LR / SC / AP / LOO",
            UltraSpeaker_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "UltraSpeakerBruteForceMethod",
            "→ Brute Force Method",
            "Keep everyone in the safe zone and skip Equalize movement.",
            false
        ),
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Config?.Configure();

        Run();
    }

    private void Run()
    {
        int privateRoomNumber = Bot.Config!.Get<int>("Setup", "PrivateRoomNumber");
        if (!LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        int selectedMask = BuildSelectedMask();
        if (selectedMask == 0)
        {
            Core.Logger("No Ultras were selected.", LogPrefix);
            return;
        }

        skipCompletedUltras = Bot.Config.Get<bool>(
            "Setup",
            "SkipCompletedUltras"
        );
        if (skipCompletedUltras && !BuildPartyRequiredMask())
            return;

        actualRunMask = skipCompletedUltras
            ? selectedMask & partyRequiredMask
            : selectedMask;
        remainingUltras = CountUltras(actualRunMask);

        if (remainingUltras > 1 && !PrepareOracle())
            return;

        if (
            !RunSelectedUltra(
                "Daily_Ultras",
                "RunUltraEzrajal",
                "Ultra Ezrajal",
                EzrajalBit,
                () => new UltraEzrajal_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Daily_Ultras",
                "RunUltraWarden",
                "Ultra Warden",
                WardenBit,
                () => new UltraWarden_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Daily_Ultras",
                "RunUltraEngineer",
                "Ultra Engineer",
                EngineerBit,
                () => new UltraEngineer_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Daily_Ultras",
                "RunUltraTyndarius",
                "Ultra Tyndarius",
                TyndariusBit,
                () => new UltraTyndarius_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunChampionDrakath",
                "Champion Drakath",
                DrakathBit,
                () => new UltraDrakath_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraDrago",
                "Ultra Drago",
                DragoBit,
                () => new UltraDrago_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraNulgath",
                "Ultra Nulgath",
                NulgathBit,
                () => new UltraNulgath_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraDage",
                "Ultra Dage",
                DageBit,
                () => new UltraDage_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraDarkon",
                "Ultra Darkon",
                DarkonBit,
                () => new UltraDarkon_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraGramiel",
                "Ultra Gramiel",
                GramielBit,
                () => new UltraGramiel_LW().RunFromMaster()
            )
            || !RunSelectedUltra(
                "Weekly_Ultras",
                "RunUltraSpeaker",
                "Ultra Speaker",
                SpeakerBit,
                () => new UltraSpeaker_LW().RunFromMaster()
            )
        )
            return;

        Core.Logger("All selected Ultras finished.", LogPrefix);
    }

    private int BuildSelectedMask()
    {
        int mask = 0;

        if (GetUltraOption("Daily_Ultras", "RunUltraEzrajal"))
            mask |= EzrajalBit;
        if (GetUltraOption("Daily_Ultras", "RunUltraWarden"))
            mask |= WardenBit;
        if (GetUltraOption("Daily_Ultras", "RunUltraEngineer"))
            mask |= EngineerBit;
        if (GetUltraOption("Daily_Ultras", "RunUltraTyndarius"))
            mask |= TyndariusBit;
        if (GetUltraOption("Weekly_Ultras", "RunChampionDrakath"))
            mask |= DrakathBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraDrago"))
            mask |= DragoBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraNulgath"))
            mask |= NulgathBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraDage"))
            mask |= DageBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraDarkon"))
            mask |= DarkonBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraGramiel"))
            mask |= GramielBit;
        if (GetUltraOption("Weekly_Ultras", "RunUltraSpeaker"))
            mask |= SpeakerBit;

        return mask;
    }

    private static int CountUltras(int mask)
    {
        int count = 0;

        foreach (int ultraBit in UltraBits)
        {
            if ((mask & ultraBit) != 0)
                count++;
        }

        return count;
    }

    private bool GetUltraOption(string category, string optionName) =>
        Bot.Config!.Get<bool>(category, optionName);

    private bool BuildPartyRequiredMask()
    {
        if (!LoneWolf.StartArmySync(CompletionSyncFileName, 4, "Setup"))
            return false;

        int localRequiredMask = BuildLocalRequiredMask();
        foreach (int ultraBit in UltraBits)
        {
            if (
                (localRequiredMask & ultraBit) != 0
                && !LoneWolf.SendArmySignal(GetCompletionSignal(ultraBit))
            )
                return false;
        }

        if (!LoneWolf.SyncArmy("COMPLETION_MASK_READY"))
            return false;

        partyRequiredMask = 0;
        foreach (int ultraBit in UltraBits)
        {
            string signal = GetCompletionSignal(ultraBit);
            for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
            {
                if (!LoneWolf.HasArmySignal(signal, playerNumber))
                    continue;

                partyRequiredMask |= ultraBit;
                break;
            }
        }

        Core.Logger("Ultra completion check finished.", LogPrefix);
        return true;
    }

    private int BuildLocalRequiredMask()
    {
        int mask = 0;

        if (NeedsUltra("Daily_Ultras", "RunUltraEzrajal", 8152))
            mask |= EzrajalBit;
        if (NeedsUltra("Daily_Ultras", "RunUltraWarden", 8153))
            mask |= WardenBit;
        if (NeedsUltra("Daily_Ultras", "RunUltraEngineer", 8154))
            mask |= EngineerBit;
        if (NeedsUltra("Daily_Ultras", "RunUltraTyndarius", 8245))
            mask |= TyndariusBit;
        if (NeedsUltra("Weekly_Ultras", "RunChampionDrakath", 8300))
            mask |= DrakathBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraDrago", 8397))
            mask |= DragoBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraNulgath", 8692))
            mask |= NulgathBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraDage", 8547))
            mask |= DageBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraDarkon", 8746))
            mask |= DarkonBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraGramiel", 10301))
            mask |= GramielBit;
        if (NeedsUltra("Weekly_Ultras", "RunUltraSpeaker", 9173))
            mask |= SpeakerBit;

        return mask;
    }

    private bool NeedsUltra(string category, string optionName, int questId) =>
        GetUltraOption(category, optionName)
        && !Bot.Quests.IsDailyComplete(questId);

    private static string GetCompletionSignal(int ultraBit) =>
        $"{CompletionSignalPrefix}{ultraBit}";

    private bool RunSelectedUltra(
        string category,
        string optionName,
        string ultraName,
        int ultraBit,
        Func<UltraRunResult> runUltra
    )
    {
        if (!GetUltraOption(category, optionName))
            return true;

        if (Bot.ShouldExit)
            return false;

        if ((actualRunMask & ultraBit) == 0)
        {
            Core.Logger(
                $"{ultraName} is completed on all four accounts. Skipping.",
                LogPrefix
            );
            return true;
        }

        Core.Logger($"Starting {ultraName}.", LogPrefix);
        UltraRunResult result = runUltra();

        if (result == UltraRunResult.Completed)
        {
            Core.Logger($"{ultraName} completed.", LogPrefix);
            return ContinueAfterUltra();
        }

        if (result == UltraRunResult.AttemptsExhausted)
        {
            Core.Logger(
                $"{ultraName} exhausted all fight attempts. Continuing.",
                LogPrefix
            );
            return ContinueAfterUltra();
        }

        if (Bot.ShouldExit)
            Core.Logger($"{ultraName} failed. Stopping the Ultra sequence.", LogPrefix);
        else
            Core.Logger(
                $"{ultraName} failed. Stopping the Ultra sequence.",
                LogPrefix,
                messageBox: true,
                stopBot: true
            );
        return false;
    }

    private bool PrepareOracle()
    {
        const string oracleName = "Oracle";

        if (Bot.Inventory.Contains(oracleName))
            return true;

        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForBankLoad(20);

        if (Bot.Bank.Contains(oracleName))
        {
            if (!Core.HasSpace)
                return OracleFailure(
                    "Oracle could not be moved from bank because no inventory slot is available."
                );

            Bot.Bank.EnsureToInventory(oracleName);
            Bot.Wait.ForTrue(() => Bot.Inventory.Contains(oracleName), 14);

            if (!Bot.Inventory.Contains(oracleName))
                return OracleFailure("Oracle could not be moved from bank.");

            Core.Logger("Oracle moved from bank.", LogPrefix);
            return true;
        }

        if (!Core.HasSpace)
            return OracleFailure(
                "Oracle could not be purchased because no inventory slot is available."
            );

        Core.BuyItem("classhalla", 759, oracleName);
        if (!Bot.Inventory.Contains(oracleName))
            return OracleFailure("Oracle could not be purchased.");

        Core.Logger("Oracle purchased.", LogPrefix);
        return true;
    }

    private bool ContinueAfterUltra()
    {
        remainingUltras--;
        if (remainingUltras <= 0)
            return true;

        return ResetBetweenUltras();
    }

    private bool ResetBetweenUltras()
    {
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1_000);
        LoneWolf.EquipClass(LoneWolf.Oracle());
        if (Bot.ShouldExit)
            return false;

        if (
            !string.Equals(
                Bot.Player.CurrentClass?.Name,
                "Oracle",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return OracleFailure("Oracle could not be equipped.");

        Bot.Sleep(1_000);
        return !Bot.ShouldExit;
    }

    private bool OracleFailure(string message)
    {
        Core.Logger(
            message + "\nThis class is required in order to reset the auras in between Ultra fights",
            LogPrefix,
            messageBox: true,
            stopBot: true
        );
        return false;
    }
}
