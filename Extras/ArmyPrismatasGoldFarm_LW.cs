/*
name: Army Prismatas Gold Farm LW
description: Four-to-seven-player CoreLoneWolf Army gold farm for Elemental Binding.
tags: gold, prismatas, elemental binding, seven-player, army, corelonewolf
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

public class ArmyPrismatasGoldFarm_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Army Prismatas Gold Farm LW";
    private const string SyncFileName = "ArmyPrismatasGoldFarm_LW.sync";
    private const string MapName = "archmage";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string FightCell = "r2";
    private const string FightPad = "Left";
    private const string ElementalBinding = "Elemental Binding";
    private const string GoldVoucher100k = "Gold Voucher 100k";
    private const string GoldVoucher500k = "Gold Voucher 500k";
    private const string VoucherMap = "alchemyacademy";
    private const int VoucherShopId = 2036;
    private const int VoucherMaxStack = 300;
    private const int BindingMaxStack = 2500;
    private const int GoldVoucher100kPrice = 100_000;
    private const int GoldVoucher500kPrice = 500_000;
    private const int GoldCap = 100_000_000;
    private const int MinimumLevel = 80;
    private const int FirstTargetMapId = 1;
    private const int SecondTargetMapId = 2;
    private const int FarmPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;
    private int bindingSellQuantity;
    private bool farmGoldVouchers;
    private bool antiLagApplied;
    private bool originalLagKiller;
    private bool originalHidePlayers;
    private bool originalDisableSelfAnimation;
    private bool originalDisableMonsterAnimation;
    private bool originalDisableSkillAnimation;
    private bool originalDisableDamageStrobe;
    private bool originalMonstersHidden;

    public string OptionsStorage = "GoldPrismatas_LW";
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
            "Default: LR / SC / AP / LOO / Shaman / Bard / VDK\nStable: AF / SC / AP / LOO / Shaman / Bard / VDK",
            ArmyComposition.Default
        ),
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
        new Option<int>(
            "BindingSellQuantity",
            "Binding Sell Quantity",
            "Sell after every player reaches this many Elemental Bindings. Range: 1 through 2500.",
            100
        ),
        new Option<bool>(
            "FarmGoldVouchers",
            "Farm Gold Vouchers?",
            "Max Gold Voucher 100k first, then Gold Voucher 500k, before finishing at 100 million gold.",
            false
        ),
        new Option<bool>(
            "EnableAntiLag",
            "Enable AntiLag",
            "Temporarily enable AntiLag and disable visual effects while the script runs.",
            true
        ),
        new Option<bool>(
            "UsePotions",
            "Use Potions",
            "Prepare and use the assigned potion loadout after each sale.",
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
        Bot.Options.AggroMonsters = true;
        Bot.Config?.Configure();

        try
        {
            ApplyAntiLag();
            Run();
        }
        finally
        {
            LoneWolf.StopSkillEngine();
            RestoreAntiLag();
        }
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

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, armyPlayerCount))
            return;

        playerAlias = GetPlayerAlias();
        ClassPreset preset = GetClassPreset();

        if (!ValidateAccess(preset))
            return;

        Core.Logger(
            $"{LogPrefix} started as {playerAlias} using {armyComposition} composition."
        );

        RegisterBindingDrop();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        bool armyComplete;
        if (!RunSafeRoomStage(preset, 0, out armyComplete))
            return;

        int farmCycle = 1;
        while (!Bot.ShouldExit && !armyComplete)
        {
            if (!FarmBindings(preset, farmCycle))
                return;

            if (!Sync($"CYCLE_{farmCycle}_SELL_READY"))
                return;

            if (!RunSafeRoomStage(preset, farmCycle, out armyComplete))
                return;

            farmCycle++;
        }

        if (Bot.ShouldExit || !armyComplete)
            return;

        MoveToHouse();
        if (Bot.ShouldExit || !Sync("PARKED"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");
        bindingSellQuantity = Bot.Config.Get<int>("BindingSellQuantity");
        farmGoldVouchers = Bot.Config.Get<bool>("FarmGoldVouchers");

        if (bindingSellQuantity < 1 || bindingSellQuantity > BindingMaxStack)
        {
            Core.Logger(
                $"Binding Sell Quantity must be from 1 through {BindingMaxStack}.",
                "ValidateOptions",
                messageBox: true
            );
            return false;
        }

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

    private bool ValidateAccess(ClassPreset preset)
    {
        if (Bot.Player.Level >= MinimumLevel)
            return true;

        return Fatal(
            $"{LogPrefix} requires level {MinimumLevel} or higher for {playerAlias} ({preset.ClassName}).",
            "ValidateAccess"
        );
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

        if (!PrepareBankItems())
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareBankItems()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load();
        Bot.Bank.Loaded = true;

        if (!MoveBankedItemToInventory(ElementalBinding))
            return false;

        return !farmGoldVouchers
            || (
                MoveBankedItemToInventory(GoldVoucher100k)
                && MoveBankedItemToInventory(GoldVoucher500k)
            );
    }

    private bool MoveBankedItemToInventory(string itemName)
    {
        int bankQuantity = Bot.Bank.GetQuantity(itemName);
        if (bankQuantity <= 0)
            return true;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
        {
            return Fatal(
                $"{itemName} cannot be moved from bank because the inventory is full.",
                "PrepareBankItems"
            );
        }

        int inventoryQuantity = Bot.Inventory.GetQuantity(itemName);
        int expectedQuantity = inventoryQuantity + bankQuantity;

        Bot.Bank.EnsureToInventory(itemName, loadBank: false);
        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(itemName) >= expectedQuantity,
            20
        );

        int finalQuantity = Bot.Inventory.GetQuantity(itemName);
        if (finalQuantity < expectedQuantity)
        {
            return Fatal(
                $"{itemName} could not be moved completely from bank. Inventory quantity: {finalQuantity}/{expectedQuantity}.",
                "PrepareBankItems"
            );
        }

        Core.Logger(
            $"{itemName} moved from bank. Inventory quantity: {finalQuantity}.",
            "PrepareBankItems"
        );
        return true;
    }

    private void RegisterBindingDrop()
    {
        if (!Bot.Drops.ToPickup.Contains(ElementalBinding))
            Core.AddDrop(ElementalBinding);

        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();
    }

    private bool FarmBindings(ClassPreset preset, int farmCycle)
    {
        Core.Jump(FightCell, FightPad);
        Bot.Options.AggroMonsters = true;

        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );
        Core.Logger(
            $"{LogPrefix} {playerAlias} started Elemental Binding cycle {farmCycle}."
        );

        bool deathLogged = false;
        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                if (!deathLogged)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died during cycle {farmCycle}.");
                    deathLogged = true;
                }

                Bot.Sleep(RespawnPollDelay);
                continue;
            }

            if (deathLogged)
            {
                Core.Logger($"{LogPrefix} {playerAlias} respawned during cycle {farmCycle}.");
                deathLogged = false;

                if (!ReachedBindingSellQuantity())
                    Core.Jump(FightCell, FightPad);
            }

            if (ReachedBindingSellQuantity())
                break;

            int targetMapId = GetCurrentTargetMapId();
            if (targetMapId > 0)
                LoneWolf.MaintainTarget(targetMapId);

            Bot.Sleep(FarmPollDelay);
        }

        StopCombat();

        if (Bot.ShouldExit)
            return false;

        Core.Logger(
            $"{LogPrefix} {playerAlias} reached {bindingSellQuantity} Elemental Binding for cycle {farmCycle}."
        );
        return true;
    }

    private bool ReachedBindingSellQuantity() =>
        Bot.Inventory.GetQuantity(ElementalBinding) >= bindingSellQuantity;

    private int GetCurrentTargetMapId()
    {
        if (LoneWolf.IsMonsterAlive(FirstTargetMapId))
            return FirstTargetMapId;

        return LoneWolf.IsMonsterAlive(SecondTargetMapId)
            ? SecondTargetMapId
            : 0;
    }

    private bool RunSafeRoomStage(
        ClassPreset preset,
        int farmCycle,
        out bool armyComplete
    )
    {
        armyComplete = false;
        StopCombat();
        MoveToSafeRoom();

        if (!IsLocalGoalComplete())
        {
            if (farmGoldVouchers && Bot.Player.Gold >= GoldCap)
            {
                PurchaseGoldVouchers();

                if (Bot.ShouldExit)
                    return false;

                MoveToSafeRoom();
            }

            if (Bot.Player.Gold < GoldCap)
            {
                if (!SellBindings(farmCycle))
                    return false;

                if (farmGoldVouchers)
                    PurchaseGoldVouchers();

                if (Bot.ShouldExit)
                    return false;
            }
        }

        MoveToSafeRoom();

        bool localComplete = IsLocalGoalComplete();
        string completionSignal = $"CYCLE_{farmCycle}_COMPLETE";
        if (localComplete && !LoneWolf.SendArmySignal(completionSignal))
            return false;

        if (!Sync($"CYCLE_{farmCycle}_SAFE_DONE"))
            return false;

        armyComplete = AllPlayersSignaled(completionSignal);
        if (armyComplete)
        {
            Core.Logger(
                $"{LogPrefix} confirmed every active account completed the gold objective."
            );
            return true;
        }

        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        Bot.Options.AggroMonsters = true;
        return !Bot.ShouldExit;
    }

    private void MoveToSafeRoom()
    {
        if (string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase))
        {
            Core.Jump(SafeCell, SafePad);
            return;
        }

        Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);
    }

    private bool SellBindings(int farmCycle)
    {
        int quantityBefore = Bot.Inventory.GetQuantity(ElementalBinding);
        if (quantityBefore <= 0)
        {
            Core.Logger(
                $"{ElementalBinding} inventory is already clear for cycle {farmCycle}.",
                "SellBindings"
            );
            return true;
        }

        Core.SellItem(ElementalBinding, all: true);
        int quantityAfter = Bot.Inventory.GetQuantity(ElementalBinding);

        if (quantityAfter <= 0)
        {
            Core.Logger(
                $"Sold {quantityBefore} {ElementalBinding} for cycle {farmCycle}.",
                "SellBindings"
            );
            return true;
        }

        return Fatal(
            $"{ElementalBinding} failed to sell completely. Remaining quantity: {quantityAfter}.",
            "SellBindings"
        );
    }

    private void PurchaseGoldVouchers()
    {
        BuyAffordableVoucher(
            GoldVoucher100k,
            GoldVoucher100kPrice
        );

        if (GetVoucherQuantity(GoldVoucher100k) < VoucherMaxStack)
            return;

        BuyAffordableVoucher(
            GoldVoucher500k,
            GoldVoucher500kPrice
        );
    }

    private void BuyAffordableVoucher(string voucherName, int price)
    {
        int quantityBefore = GetVoucherQuantity(voucherName);
        int missing = VoucherMaxStack - quantityBefore;
        int affordable = Bot.Player.Gold / price;
        int buyQuantity = Math.Min(missing, affordable);

        if (buyQuantity <= 0)
            return;

        Core.BuyItem(VoucherMap, VoucherShopId, voucherName, buyQuantity);

        int quantityAfter = GetVoucherQuantity(voucherName);
        Core.Logger(
            $"{voucherName}: {quantityAfter}/{VoucherMaxStack} after buying {Math.Max(0, quantityAfter - quantityBefore)}.",
            "PurchaseGoldVouchers"
        );
    }

    private int GetVoucherQuantity(string voucherName) =>
        Bot.Inventory.GetQuantity(voucherName) + Bot.Bank.GetQuantity(voucherName);

    private bool IsLocalGoalComplete()
    {
        if (Bot.Player.Gold < GoldCap)
            return false;

        return !farmGoldVouchers
            || (
                GetVoucherQuantity(GoldVoucher100k) >= VoucherMaxStack
                && GetVoucherQuantity(GoldVoucher500k) >= VoucherMaxStack
            );
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

    private void StopCombat()
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private ClassPreset GetClassPreset()
    {
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

    private void MoveToHouse()
    {
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
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
