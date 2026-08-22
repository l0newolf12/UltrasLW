/*
name: Kathool Depths LW
description: Five-to-seven-player CoreLoneWolf Army script for God of the Depths.
tags: ultra, kathool depths, god of the depths, seven-player, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class KathoolDepths_LW
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

    private const string LogPrefix = "Kathool Depths LW";
    private const string SyncFileName = "KathoolDepths_LW.sync";
    private const string MapName = "kathooldepths";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string Vigil = "Vigil";
    private const string PacketCommand = "ct";
    private const string ResistPacketText = "cannot resist";
    private const int UltraQuestId = 9350;
    private const int MinimumLevel = 80;
    private const int VigilShopId = 2322;
    private const int VigilRestockThreshold = 100;
    private const int VigilMaxStack = 1000;
    private const int FirstTargetMapId = 3;
    private const int SecondTargetMapId = 1;
    private const int BossMapId = 2;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DeathResetThreshold = 3;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int armyPlayerCount;
    private int privateRoomNumber;

    public string OptionsStorage = "KathoolDepths_LW";
    public bool DontPreconfigure = true;
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
        LoneWolf.player5,
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
            "UsePotions",
            "Use Potions",
            "Prepare and use the assigned tonic and elixir. Vigil is always required.",
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
                UltraQuestId,
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

        LoneWolf.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Core.Jump(SafeCell, SafePad);
        LoneWolf.CompleteUltraQuest(UltraQuestId);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = Bot.Config!.Get<ArmyComposition>("ArmyComposition");
        privateRoomNumber = Bot.Config.Get<int>("PrivateRoomNumber");

        string playerSix = Bot.Config.Get<string>("player6")?.Trim() ?? string.Empty;
        string playerSeven = Bot.Config.Get<string>("player7")?.Trim() ?? string.Empty;
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
                : 5;

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

        if (Bot.ShouldExit || !PrepareVigil())
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareVigil()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForTrue(() => Bot.Bank.Contains(Vigil), 20);

        if (Bot.Bank.Contains(Vigil))
        {
            if (!Bot.Inventory.Contains(Vigil) && !Core.HasSpace)
            {
                return Fatal(
                    "Vigil is banked but there is no inventory space to move it.",
                    "PrepareVigil"
                );
            }

            int quantityBefore = Bot.Inventory.GetQuantity(Vigil);
            Bot.Bank.EnsureToInventory(Vigil);
            Bot.Wait.ForTrue(
                () => Bot.Inventory.GetQuantity(Vigil) > quantityBefore,
                14
            );

            if (!Bot.Inventory.Contains(Vigil))
                return Fatal("Vigil could not be moved from bank.", "PrepareVigil");

            Core.Logger("Vigil moved from bank.", "PrepareVigil");
        }

        Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);

        if (Bot.Inventory.GetQuantity(Vigil) < VigilRestockThreshold)
            Core.BuyItem(MapName, VigilShopId, Vigil, VigilMaxStack);

        int quantity = Bot.Inventory.GetQuantity(Vigil);
        if (quantity <= 0)
            return Fatal("Vigil could not be obtained. The fight cannot start.", "PrepareVigil");

        if (quantity < VigilRestockThreshold)
        {
            Core.Logger(
                $"Vigil preparation ended at {quantity}. Continuing.",
                "PrepareVigil"
            );
        }
        else
            Core.Logger($"Vigil available at {quantity}.", "PrepareVigil");

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

        if (!EquipVigil())
            return false;

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool EquipVigil()
    {
        if (!Bot.Inventory.Contains(Vigil))
            return Fatal("Vigil is not in inventory. The fight cannot start.", "EquipVigil");

        if (!Bot.Inventory.IsEquipped(Vigil))
        {
            Bot.Inventory.EquipUsableItem(Vigil);
            Bot.Wait.ForItemEquip(Vigil);
        }

        if (!Bot.Inventory.IsEquipped(Vigil))
            return Fatal("Vigil could not be equipped. The fight cannot start.", "EquipVigil");

        Core.Logger("Vigil equipped.", "EquipVigil");
        return true;
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (
            int fightAttempt = 1;
            fightAttempt <= MaxFightAttempts && !Bot.ShouldExit;
            fightAttempt++
        )
        {
            Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            if (!LoneWolf.StartPacketDetector(PacketCommand, ResistPacketText))
            {
                return Fatal(
                    "The Vigil packet detector could not be started.",
                    "RunFightAttempts"
                );
            }

            FightResult result;
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                result = Fight(preset, fightAttempt);
            }
            finally
            {
                LoneWolf.StopPacketDetector();
            }

            if (result == FightResult.Defeated)
                return true;

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                StopArmyAfterFailedAttempts();
                return false;
            }
        }

        return false;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        bool bossObservedAlive = false;
        int nextResistDetection = 1;

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
            {
                StopFightCombat();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    ProcessResistDetections(ref nextResistDetection, requestVigil: false);

                    if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        StopFightCombat();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold))
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            bool bossAlive = LoneWolf.IsMonsterAlive(BossMapId);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            int targetMapId = GetCurrentTargetMapId();
            if (targetMapId > 0)
                LoneWolf.MaintainTarget(targetMapId);

            ProcessResistDetections(ref nextResistDetection, requestVigil: true);
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();

        if (Bot.ShouldExit || !bossObservedAlive)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed God of the Depths defeated.");
        return FightResult.Defeated;
    }

    private int GetCurrentTargetMapId()
    {
        bool focusBoss =
            armyComposition == ArmyComposition.Stable
                ? LoneWolf.IsArmyPlayer(1)
                    || LoneWolf.IsArmyPlayer(2)
                    || LoneWolf.IsArmyPlayer(5)
                    || LoneWolf.IsArmyPlayer(6)
                : LoneWolf.IsArmyPlayer(2)
                    || LoneWolf.IsArmyPlayer(5)
                    || LoneWolf.IsArmyPlayer(6)
                    || LoneWolf.IsArmyPlayer(7);

        if (focusBoss)
            return LoneWolf.IsMonsterAlive(BossMapId) ? BossMapId : 0;

        if (LoneWolf.IsMonsterAlive(FirstTargetMapId))
            return FirstTargetMapId;

        if (LoneWolf.IsMonsterAlive(SecondTargetMapId))
            return SecondTargetMapId;

        return LoneWolf.IsMonsterAlive(BossMapId) ? BossMapId : 0;
    }

    private void ProcessResistDetections(
        ref int nextResistDetection,
        bool requestVigil
    )
    {
        while (LoneWolf.HasPacketDetection(nextResistDetection))
        {
            if (requestVigil)
            {
                LoneWolf.RequestAbsolutePrioritySkill(5);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested Vigil for resist detection {nextResistDetection}."
                );
            }

            nextResistDetection++;
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        LoneWolf.ShouldResetFight(fightAttempt, DeathResetThreshold);
        Core.Jump(SafeCell, SafePad);

        if (!IsInSafeRoom())
        {
            return Fatal(
                $"{LogPrefix} {playerAlias} could not reach the safe room after reset.",
                "HandleFightReset"
            );
        }

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
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

        if (armyComposition == ArmyComposition.Stable)
            return LoneWolf.ArchFiend();

        ClassPreset shaman = LoneWolf.Shaman();
        shaman.HelmEnhancement = HelmSpecial.Examen;
        return shaman;
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

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

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

    private void StopArmyAfterFailedAttempts()
    {
        if (LoneWolf.IsArmyPlayer(1))
            LoneWolf.StopArmySync("ATTEMPTS_EXHAUSTED");
        else
            LoneWolf.SyncArmy("STOP_CHECK");

        Core.Logger(
            $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
            "RunFightAttempts",
            messageBox: true,
            stopBot: true
        );
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
