/*
name: Greatblade of the Entwined Eclipse LW
description: Coordinates four accounts through the Temple Shrine dungeons and merges the Greatblade.
tags: temple shrine, dungeon, army, corelonewolf, master
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/CoreTempleShrine.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/VictorMatsuriMaskado_LW.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/MidnightSun_LW.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/SolsticeMoon_LW.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/AscendEclipse_LW.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class GreatbladeOfTheEntwinedEclipse_LW
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Greatblade Entwined Eclipse LW";
    private const string SyncFileName = "0GreatbladeOfTheEntwinedEclipse_LW.sync";
    private const string MergeMap = "templeshrine";
    private const int MergeShopId = 2303;

    private const string Victor = "Victor of the Festival";
    private const string Rite = "Rite of Ascension";
    private const string Sunlight = "Sliver of Sunlight";
    private const string Moonlight = "Sliver of Moonlight";
    private const string EclipticOffering = "Ecliptic Offering";

    private const string Solarbrand = "Solarbrand";
    private const string Lunarbrand = "Lunarbrand";
    private const string Umbrabrand = "Umbrabrand";
    private const string BurningSun = "Blade of the Burning Sun";
    private const string GlowingMoon = "Blade of the Glowing Moon";
    private const string BoundEclipse = "Blade of the Bound Eclipse";
    private const string MidnightGreatblade = "Greatblade of the Midnight Sun";
    private const string SolsticeGreatblade = "Greatblade of the Solstice Moon";
    private const string EntwinedGreatblade = "Greatblade of the Entwined Eclipse";

    private const int RiteShopItemId = 10741;
    private const int SolarbrandShopItemId = 10744;
    private const int LunarbrandShopItemId = 10745;
    private const int UmbrabrandShopItemId = 10746;
    private const int BurningSunShopItemId = 10747;
    private const int GlowingMoonShopItemId = 10748;
    private const int BoundEclipseShopItemId = 10749;
    private const int MidnightGreatbladeShopItemId = 10750;
    private const int SolsticeGreatbladeShopItemId = 10751;
    private const int EntwinedGreatbladeShopItemId = 10752;

    private const int NightFallsQuestId = 9303;
    private const int DawnBreaksQuestId = 9304;
    private const int FrozenCycleQuestId = 9305;

    private readonly HashSet<int> disabledDailyQuests = new();
    private string playerAlias = string.Empty;
    private bool buybackMethod;

    public string OptionsStorage = "0GreatbladeOfTheEntwinedEclipse_LW";
    public bool DontPreconfigure = true;
    public string[] MultiOptions = { "Setup", "Temple_Shrine" };

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
    };

    public List<IOption> Temple_Shrine = new()
    {
        new Option<VictorMatsuriMaskado_LW.ArmyComposition>(
            "VictorMatsuriComposition",
            "Victor Matsuri Composition",
            "Default: LR / SC / AP / LOO",
            VictorMatsuriMaskado_LW.ArmyComposition.Default
        ),
        new Option<MidnightSun_LW.ArmyComposition>(
            "MidnightSunComposition",
            "Midnight Sun Composition",
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO\nReliable: Shaman / SC / AP / LOO",
            MidnightSun_LW.ArmyComposition.Default
        ),
        new Option<SolsticeMoon_LW.ArmyComposition>(
            "SolsticeMoonComposition",
            "Solstice Moon Composition",
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO\nReliable: Shaman / SC / AP / LOO",
            SolsticeMoon_LW.ArmyComposition.Default
        ),
        new Option<AscendEclipse_LW.ArmyComposition>(
            "AscendEclipseComposition",
            "Ascend Eclipse Composition",
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO",
            AscendEclipse_LW.ArmyComposition.Default
        ),
        new Option<bool>(
            "BuybackMethod",
            "Buyback Method?",
            "Farm 155 of every raw material on all four accounts without merging the Greatblade.",
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

        if (!LoneWolf.StartArmySync(SyncFileName, 4, "Setup"))
            return;

        playerAlias = GetPlayerAlias();
        buybackMethod = Bot.Config.Get<bool>("Temple_Shrine", "BuybackMethod");
        RegisterDrops();

        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using the {(buybackMethod ? "Buyback" : "automatic Greatblade")} method."
        );

        if (!PrepareRiteOfAscension())
            return;

        int sunlightTarget = buybackMethod ? 155 : 215;
        int moonlightTarget = buybackMethod ? 155 : 215;
        MidnightSun_LW midnightSun = new();
        SolsticeMoon_LW solsticeMoon = new();
        AscendEclipse_LW ascendEclipse = new();

        if (
            !FarmMaterial(
                Sunlight,
                sunlightTarget,
                "SUNLIGHT_READY",
                "SUNLIGHT",
                DawnBreaksQuestId,
                midnightSun.RunOnceFromMaster,
                startDungeon: midnightSun.StartFromMaster,
                stopDungeon: midnightSun.StopFromMaster
            )
            || !FarmMaterial(
                Moonlight,
                moonlightTarget,
                "MOONLIGHT_READY",
                "MOONLIGHT",
                NightFallsQuestId,
                solsticeMoon.RunOnceFromMaster,
                startDungeon: solsticeMoon.StartFromMaster,
                stopDungeon: solsticeMoon.StopFromMaster
            )
            || !FarmMaterial(
                EclipticOffering,
                155,
                "ECLIPTIC_READY",
                "ECLIPTIC",
                FrozenCycleQuestId,
                ascendEclipse.RunOnceFromMaster,
                startDungeon: ascendEclipse.StartFromMaster,
                stopDungeon: ascendEclipse.StopFromMaster
            )
        )
            return;

        if (buybackMethod)
        {
            if (!Sync("BUYBACK_MATERIALS_READY"))
                return;

            Core.Logger(
                $"{LogPrefix} {playerAlias} reached 155 {Sunlight}, {Moonlight}, and {EclipticOffering}."
            );
            StopArmy("BUYBACK_COMPLETE");
            return;
        }

        if (!Sync("MATERIALS_READY"))
            return;

        bool mergeSucceeded = Owns(EntwinedGreatblade) || MergeGreatblade();
        if (!mergeSucceeded)
            LoneWolf.SendArmySignal("GREATBLADE_FAILURE");
        else
            LoneWolf.SendArmySignal("GREATBLADE_READY");

        if (!Sync("GREATBLADE_RESULT"))
            return;

        if (AnyPlayerSignaled("GREATBLADE_FAILURE"))
        {
            ArmyFailure("At least one player could not merge the Greatblade of the Entwined Eclipse.");
            return;
        }

        if (!AllPlayersSignaled("GREATBLADE_READY"))
        {
            ArmyFailure("Greatblade completion could not be confirmed for all four players.");
            return;
        }

        Core.Logger($"{LogPrefix} every player completed the Greatblade workflow.");
        StopArmy("GREATBLADE_COMPLETE");
    }

    private bool PrepareRiteOfAscension()
    {
        RefreshBank();

        bool needsRite = !Owns(Rite);
        if (!needsRite && !LoneWolf.SendArmySignal("RITE_ALREADY_OWNED"))
            return false;

        if (!Sync("RITE_STATUS_REPORTED"))
            return false;

        if (!AllPlayersSignaled("RITE_ALREADY_OWNED"))
        {
            if (!new VictorMatsuriMaskado_LW().RunFromMaster())
                LoneWolf.SendArmySignal("VICTOR_FAILURE");

            RefreshBank();

            if (needsRite && !Owns(Victor))
                LoneWolf.SendArmySignal("VICTOR_FAILURE");

            if (!Sync("VICTOR_RESULT"))
                return false;

            if (AnyPlayerSignaled("VICTOR_FAILURE"))
                return ArmyFailure(
                    "Victor of the Festival could not be obtained for every player that needs a Rite of Ascension."
                );

            if (
                !FarmMaterial(
                    Sunlight,
                    1,
                    "RITE_SUNLIGHT_READY",
                    "RITE_SUNLIGHT",
                    DawnBreaksQuestId,
                    () => new MidnightSun_LW().RunFromMaster(),
                    needsRite
                )
                || !FarmMaterial(
                    Moonlight,
                    1,
                    "RITE_MOONLIGHT_READY",
                    "RITE_MOONLIGHT",
                    NightFallsQuestId,
                    () => new SolsticeMoon_LW().RunFromMaster(),
                    needsRite
                )
            )
                return false;

            bool riteMerged = !needsRite || MergeRite();
            if (!riteMerged)
                LoneWolf.SendArmySignal("RITE_FAILURE");
            else
                LoneWolf.SendArmySignal("RITE_READY");

            if (!Sync("RITE_MERGE_RESULT"))
                return false;

            if (AnyPlayerSignaled("RITE_FAILURE"))
                return ArmyFailure(
                    "Rite of Ascension could not be merged for every required player."
                );
        }
        else if (!LoneWolf.SendArmySignal("RITE_READY"))
            return false;

        if (!MoveToInventory(Rite, 1))
            LoneWolf.SendArmySignal("RITE_INVENTORY_FAILURE");
        else
            LoneWolf.SendArmySignal("RITE_INVENTORY_READY");

        if (!Sync("RITE_INVENTORY_RESULT"))
            return false;

        if (AnyPlayerSignaled("RITE_INVENTORY_FAILURE"))
            return ArmyFailure(
                "Rite of Ascension could not be moved into every player's inventory."
            );

        if (!AllPlayersSignaled("RITE_INVENTORY_READY"))
            return ArmyFailure(
                "Rite of Ascension inventory readiness could not be confirmed for all four players."
            );

        return true;
    }

    private bool FarmMaterial(
        string itemName,
        int targetQuantity,
        string readySignal,
        string phaseName,
        int dailyQuestId,
        Func<bool> runDungeon,
        bool localRequired = true,
        Func<bool>? startDungeon = null,
        Action? stopDungeon = null
    )
    {
        bool readyReported = false;
        bool startAttempted = false;
        int cycle = 0;

        try
        {
            while (!Bot.ShouldExit)
            {
                RefreshBank();

                int quantity = TotalQuantity(itemName);
                if ((!localRequired || quantity >= targetQuantity) && !readyReported)
                {
                    if (!LoneWolf.SendArmySignal(readySignal))
                        return false;

                    readyReported = true;
                    Core.Logger(
                        $"{LogPrefix} {playerAlias} is ready with {quantity}/{targetQuantity} {itemName}."
                    );
                }

                if (!Sync($"{phaseName}_{cycle}_STATUS"))
                    return false;

                if (AllPlayersSignaled(readySignal))
                    return true;

                if (!startAttempted && startDungeon != null)
                {
                    startAttempted = true;
                    if (!startDungeon())
                        return false;
                }

                TryAcceptDaily(dailyQuestId);
                bool runSucceeded = runDungeon();
                if (!runSucceeded)
                    LoneWolf.SendArmySignal($"{phaseName}_FAILURE");

                if (!Sync($"{phaseName}_{cycle}_RUN"))
                    return false;

                if (AnyPlayerSignaled($"{phaseName}_FAILURE"))
                    return ArmyFailure(
                        $"The {phaseName} dungeon run failed on at least one player."
                    );

                TryCompleteDaily(dailyQuestId);
                cycle++;
            }

            return false;
        }
        finally
        {
            if (startAttempted)
                stopDungeon?.Invoke();
        }
    }

    private bool MergeRite()
    {
        if (
            !MoveToInventory(Victor, 1)
            || !MoveToInventory(Sunlight, 1)
            || !MoveToInventory(Moonlight, 1)
        )
            return LocalFailure(
                "The Rite of Ascension ingredients could not be moved into inventory."
            );

        Core.BuyItem(MergeMap, MergeShopId, Rite, 1, shopItemID: RiteShopItemId);
        if (!Owns(Rite))
            return LocalFailure("Rite of Ascension could not be merged.");

        Core.Logger($"{LogPrefix} {playerAlias} merged {Rite}.");
        return true;
    }

    private bool MergeGreatblade()
    {
        if (
            !EnsureSolarbrand()
            || !MergeItem(
                BurningSun,
                BurningSunShopItemId,
                Requirement(Solarbrand, 1),
                Requirement(Sunlight, 50)
            )
            || !MergeItem(
                MidnightGreatblade,
                MidnightGreatbladeShopItemId,
                Requirement(BurningSun, 1),
                Requirement(Sunlight, 100)
            )
            || !EnsureLunarbrand()
            || !MergeItem(
                GlowingMoon,
                GlowingMoonShopItemId,
                Requirement(Lunarbrand, 1),
                Requirement(Moonlight, 50)
            )
            || !MergeItem(
                SolsticeGreatblade,
                SolsticeGreatbladeShopItemId,
                Requirement(GlowingMoon, 1),
                Requirement(Moonlight, 100)
            )
            || !EnsureSolarbrand()
            || !MergeItem(
                BurningSun,
                BurningSunShopItemId,
                Requirement(Solarbrand, 1),
                Requirement(Sunlight, 50)
            )
            || !EnsureLunarbrand()
            || !MergeItem(
                GlowingMoon,
                GlowingMoonShopItemId,
                Requirement(Lunarbrand, 1),
                Requirement(Moonlight, 50)
            )
            || !EnsureUmbrabrand()
            || !MergeItem(
                BoundEclipse,
                BoundEclipseShopItemId,
                Requirement(BurningSun, 1),
                Requirement(GlowingMoon, 1),
                Requirement(Umbrabrand, 1),
                Requirement(EclipticOffering, 50)
            )
            || !MergeItem(
                EntwinedGreatblade,
                EntwinedGreatbladeShopItemId,
                Requirement(BoundEclipse, 1),
                Requirement(SolsticeGreatblade, 1),
                Requirement(MidnightGreatblade, 1),
                Requirement(EclipticOffering, 100)
            )
        )
            return false;

        return Owns(EntwinedGreatblade);
    }

    private bool EnsureSolarbrand() =>
        MergeItem(
            Solarbrand,
            SolarbrandShopItemId,
            Requirement(Sunlight, 5)
        );

    private bool EnsureLunarbrand() =>
        MergeItem(
            Lunarbrand,
            LunarbrandShopItemId,
            Requirement(Moonlight, 5)
        );

    private bool EnsureUmbrabrand()
    {
        if (Owns(Umbrabrand))
            return true;

        return EnsureSolarbrand()
            && EnsureLunarbrand()
            && MergeItem(
                Umbrabrand,
                UmbrabrandShopItemId,
                Requirement(Solarbrand, 1),
                Requirement(Lunarbrand, 1),
                Requirement(EclipticOffering, 5)
            );
    }

    private bool MergeItem(
        string outputName,
        int shopItemId,
        params MergeRequirement[] requirements
    )
    {
        if (Owns(outputName))
        {
            Core.Logger($"{LogPrefix} {playerAlias} already owns {outputName}.");
            return true;
        }

        foreach (MergeRequirement requirement in requirements)
        {
            if (!MoveToInventory(requirement.Name, requirement.Quantity))
                return LocalFailure(
                    $"{outputName} could not be merged because {requirement.Name} x{requirement.Quantity} is unavailable in inventory."
                );
        }

        Core.BuyItem(MergeMap, MergeShopId, outputName, 1, shopItemID: shopItemId);
        if (!Owns(outputName))
            return LocalFailure($"{outputName} could not be merged.");

        Core.Logger($"{LogPrefix} {playerAlias} merged {outputName}.");
        return true;
    }

    private bool MoveToInventory(string itemName, int quantity)
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        RefreshBank();
        if (TotalQuantity(itemName) < quantity)
            return false;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
            return false;

        Bot.Bank.EnsureToInventory(itemName, loadBank: false);
        return Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(itemName) >= quantity,
            20
        );
    }

    private void RefreshBank()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForBankLoad(20);
    }

    private int TotalQuantity(string itemName) =>
        Bot.Inventory.GetQuantity(itemName) + Bot.Bank.GetQuantity(itemName);

    private bool Owns(string itemName) =>
        Bot.Inventory.Contains(itemName) || Bot.Bank.Contains(itemName);

    private void TryAcceptDaily(int questId)
    {
        if (
            disabledDailyQuests.Contains(questId)
            || Bot.Quests.IsDailyComplete(questId)
            || Bot.Quests.IsInProgress(questId)
        )
            return;

        if (Core.EnsureAccept(questId))
            return;

        disabledDailyQuests.Add(questId);
        Core.Logger(
            $"Optional daily quest {questId} could not be accepted. Direct dungeon farming will continue.",
            LogPrefix
        );
    }

    private void TryCompleteDaily(int questId)
    {
        if (
            disabledDailyQuests.Contains(questId)
            || Bot.Quests.IsDailyComplete(questId)
            || !Bot.Quests.IsInProgress(questId)
            || !Bot.Quests.CanComplete(questId)
        )
            return;

        if (Core.EnsureComplete(questId))
        {
            Core.Logger($"Optional daily quest {questId} completed.", LogPrefix);
            return;
        }

        disabledDailyQuests.Add(questId);
        Core.Logger(
            $"Optional daily quest {questId} could not be completed. Direct dungeon farming will continue.",
            LogPrefix
        );
    }

    private void RegisterDrops()
    {
        Bot.Drops.Add(
            Sunlight,
            Moonlight,
            EclipticOffering,
            "Midnight Moondrop",
            "Solstice Sundew",
            "Midnight's Shadow",
            "Solstice's Shadow"
        );
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");
        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private bool AllPlayersSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (!LoneWolf.HasArmySignal(signal, playerNumber))
                return false;
        }

        return true;
    }

    private bool AnyPlayerSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (LoneWolf.HasArmySignal(signal, playerNumber))
                return true;
        }

        return false;
    }

    private string GetPlayerAlias()
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (LoneWolf.IsArmyPlayer(playerNumber))
                return $"player{NumberWord(playerNumber)}";
        }

        return "playerUnknown";
    }

    private static string NumberWord(int playerNumber) =>
        playerNumber switch
        {
            1 => "One",
            2 => "Two",
            3 => "Three",
            4 => "Four",
            _ => "Unknown",
        };

    private void StopArmy(string reason)
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            Bot.Sleep(2_000);
            LoneWolf.StopArmySync(reason);
            return;
        }

        LoneWolf.SyncArmy("STOP_CHECK");
    }

    private bool ArmyFailure(string message)
    {
        Core.Logger(message, LogPrefix, messageBox: true, stopBot: true);
        return false;
    }

    private bool LocalFailure(string message)
    {
        Core.Logger(message, LogPrefix);
        return false;
    }

    private static MergeRequirement Requirement(string name, int quantity) =>
        new(name, quantity);

    private sealed class MergeRequirement
    {
        public MergeRequirement(string name, int quantity)
        {
            Name = name;
            Quantity = quantity;
        }

        public string Name { get; }
        public int Quantity { get; }
    }
}
