/*
name: Ultra Ezrajal LW
description: Four-player CoreLoneWolf Army script for Ultra Ezrajal.
tags: ultra, ezrajal, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Options;

#nullable enable

public class UltraEzrajal_LW
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
    private CoreAdvanced Advanced => CoreAdvanced.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Ultra Ezrajal LW";
    private const string SyncFileName = "UltraEzrajal_LW.sync";
    private const string MapName = "ultraezrajal";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string BattlestaffName = "Battle Oracle Battlestaff";
    private const string SkillLockedAura = "Skill Locked";
    private const string CounterAttackAura = "Counter Attack";
    private const int BattlestaffShopId = 759;
    private const int UltraQuestId = 8152;
    private const int PrerequisiteQuestId = 8151;
    private const string PrerequisiteQuestName = "The Engineer";
    private const int MinimumLevel = 61;
    private const int BossMapId = 1;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int ManaLockSafeDelay = 2000;
    private const int MaxFightAttempts = 3;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int privateRoomNumber;
    private int normalWeaponId;
    private string normalWeaponName = string.Empty;
    private bool runManaLock;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraEzrajal_LW";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        new Option<ArmyComposition>(
            "ArmyComposition",
            "Army Composition",
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO",
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
            "Prepare and use the assigned potion loadout.",
            true
        ),
        new Option<bool>(
            "UseEnhancements",
            "Use Enhancements",
            "Prepare the assigned enhancement loadout.",
            true
        ),
        new Option<bool>(
            "SkipManaLock",
            "Skip Mana Lock?",
            "Skip the Battle Oracle Battlestaff Mana Lock preparation.",
            false
        ),
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.UltraBossHelper.DisableCounterAttack();
        Bot.Options.InfiniteRange = true;
        Bot.Config?.Configure();

        try
        {
            Run();
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
            LoneWolf.StopSkillEngine();
        }
    }

    public UltraRunResult RunFromMaster()
    {
        masterMode = true;
        runResult = UltraRunResult.Failed;
        Bot.Skills.Stop();
        Bot.UltraBossHelper.DisableCounterAttack();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
            LoneWolf.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, 4, masterMode ? "Setup" : null))
            return;

        if (
            !LoneWolf.ValidateUltraAccess(
                UltraQuestId,
                PrerequisiteQuestId,
                PrerequisiteQuestName,
                MinimumLevel,
                LogPrefix,
                GetClassPreset().ClassName
            )
        )
            return;

        playerAlias = GetPlayerAlias();
        ClassPreset preset = GetClassPreset();

        Core.Logger($"{LogPrefix} started as {playerAlias}.");

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
        runResult = UltraRunResult.Completed;
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

            if (GetSetupOption<bool>("UsePotions"))
                LoneWolf.UsePotions(preset.Tonic, preset.Elixir);

            if (!EquipManaLockWeapon() || !RunManaLock())
                return false;

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            FightResult result = Fight(preset, fightAttempt);
            if (result == FightResult.Defeated)
                return true;

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                StopArmyAfterFailedAttempts();
                runResult = UltraRunResult.AttemptsExhausted;
                return false;
            }
        }

        return false;
    }

    private bool ValidateOptions()
    {
        armyComposition = GetUltraOption<ArmyComposition>(
            "UltraEzrajalComposition",
            "ArmyComposition"
        );
        privateRoomNumber = GetSetupOption<int>("PrivateRoomNumber");
        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (GetSetupOption<bool>("UseEnhancements"))
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement
            );

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (!PrepareManaLockWeapon(preset) || Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareManaLockWeapon(ClassPreset preset)
    {
        runManaLock = false;

        if (
            GetUltraOption<bool>(
                "UltraEzrajalSkipManaLock",
                "SkipManaLock"
            )
        )
        {
            Core.Logger($"{LogPrefix} {playerAlias} skipped Mana Lock preparation.");
            return true;
        }

        InventoryItem? normalWeapon = Bot.Inventory.Items.FirstOrDefault(item =>
            item.Equipped
            && string.Equals(item.ItemGroup, "Weapon", StringComparison.OrdinalIgnoreCase)
        );

        if (normalWeapon == null)
        {
            Core.Logger(
                "The currently equipped weapon could not be recorded.",
                "PrepareManaLock",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        normalWeaponId = normalWeapon.ID;
        normalWeaponName = normalWeapon.Name;

        if (string.Equals(normalWeaponName, BattlestaffName, StringComparison.OrdinalIgnoreCase))
        {
            Core.Logger(
                $"{BattlestaffName} is the normal combat weapon. Mana Vamp would overwrite its enhancement.",
                "PrepareManaLock",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (!EnsureBattlestaffInInventory())
            return !Bot.ShouldExit;

        Advanced.EnhanceItem(
            BattlestaffName,
            preset.BaseEnhancement,
            wSpecial: WeaponSpecial.Mana_Vamp
        );

        InventoryItem? battlestaff = Bot.Inventory.Items.FirstOrDefault(item =>
            string.Equals(item.Name, BattlestaffName, StringComparison.OrdinalIgnoreCase)
        );

        if (battlestaff == null || battlestaff.ProcID != (int)WeaponSpecial.Mana_Vamp)
        {
            Core.Logger(
                $"{BattlestaffName} skipped because Mana Vamp was not verified.",
                "PrepareManaLock"
            );
            return true;
        }

        Core.Equip(battlestaff.ID);

        if (!Bot.Inventory.IsEquipped(battlestaff.ID))
        {
            Core.Logger(
                $"{BattlestaffName} could not be equipped. Mana Lock was skipped.",
                "PrepareManaLock"
            );
            return true;
        }

        runManaLock = true;
        Core.Logger($"{BattlestaffName} prepared with Mana Vamp.", "PrepareManaLock");
        return true;
    }

    private bool EnsureBattlestaffInInventory()
    {
        if (Bot.Inventory.Contains(BattlestaffName))
            return true;

        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForTrue(() => Bot.Bank.Contains(BattlestaffName), 20);

        if (Bot.Bank.Contains(BattlestaffName))
        {
            if (Bot.Inventory.FreeSlots <= 0)
            {
                Core.Logger(
                    $"{BattlestaffName} is banked but no inventory slot is available. Mana Lock was skipped.",
                    "PrepareManaLock"
                );
                return false;
            }

            Bot.Bank.EnsureToInventory(BattlestaffName);
            Bot.Wait.ForTrue(() => Bot.Inventory.Contains(BattlestaffName), 14);

            if (!Bot.Inventory.Contains(BattlestaffName))
            {
                Core.Logger(
                    $"{BattlestaffName} could not be moved from bank. Mana Lock was skipped.",
                    "PrepareManaLock"
                );
                return false;
            }

            Core.Logger($"{BattlestaffName} moved from bank.", "PrepareManaLock");
            return true;
        }

        if (Bot.Inventory.FreeSlots <= 0)
        {
            Core.Logger(
                $"{BattlestaffName} could not be purchased because no inventory slot is available. Mana Lock was skipped.",
                "PrepareManaLock"
            );
            return false;
        }

        Core.BuyItem(MapName, BattlestaffShopId, BattlestaffName);

        if (Bot.Inventory.Contains(BattlestaffName))
        {
            Core.Logger($"{BattlestaffName} purchased.", "PrepareManaLock");
            return true;
        }

        Core.Logger(
            $"{BattlestaffName} could not be purchased from shop {BattlestaffShopId}.",
            "PrepareManaLock",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool RunManaLock()
    {
        if (!runManaLock)
            return !Bot.ShouldExit;

        Core.Jump(BossCell, BossPad);
        Core.Logger($"{LogPrefix} {playerAlias} started Mana Lock preparation.");

        while (!Bot.ShouldExit && !Bot.Self.HasActiveAura(SkillLockedAura))
        {
            if (!LoneWolf.IsMonsterAlive(BossMapId))
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} did not receive {SkillLockedAura} before Ultra Ezrajal died."
                );
                Bot.Combat.CancelAutoAttack();
                Bot.Combat.CancelTarget();
                Core.Jump(SafeCell, SafePad);
                return RestoreNormalWeapon();
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died during Mana Lock preparation.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Core.Jump(BossCell, BossPad);
                continue;
            }

            Bot.Combat.Attack(BossMapId);
            Bot.Sleep(FightPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        Bot.Combat.CancelAutoAttack();
        Bot.Combat.CancelTarget();
        Core.Jump(SafeCell, SafePad);
        Bot.Sleep(ManaLockSafeDelay);

        if (!RestoreNormalWeapon())
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed {SkillLockedAura}.");
        return true;
    }

    private bool EquipManaLockWeapon()
    {
        if (!runManaLock)
            return !Bot.ShouldExit;

        InventoryItem? battlestaff = Bot.Inventory.Items.FirstOrDefault(item =>
            string.Equals(item.Name, BattlestaffName, StringComparison.OrdinalIgnoreCase)
            && item.ProcID == (int)WeaponSpecial.Mana_Vamp
        );

        if (battlestaff == null)
        {
            Core.Logger(
                $"{BattlestaffName} with Mana Vamp is missing before Mana Lock.",
                "RunManaLock",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (!battlestaff.Equipped)
            Core.Equip(battlestaff.ID);

        if (Bot.Inventory.IsEquipped(battlestaff.ID))
            return true;

        Core.Logger(
            $"{BattlestaffName} could not be equipped before Mana Lock.",
            "RunManaLock",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool RestoreNormalWeapon()
    {
        Core.Equip(normalWeaponId);

        if (Bot.Inventory.IsEquipped(normalWeaponId))
        {
            Core.Logger($"{normalWeaponName} restored.", "RunManaLock");
            return true;
        }

        Core.Logger(
            $"{normalWeaponName} could not be restored after Mana Lock.",
            "RunManaLock",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        LoneWolf.GenericPrebuff();
        return !Bot.ShouldExit;
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
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        try
        {
            while (!Bot.ShouldExit)
            {
                if (!LoneWolf.IsMonsterAlive(BossMapId))
                    break;

                if (LoneWolf.ShouldResetFight(fightAttempt))
                    return FightResult.Reset;

                if (!Bot.Player.Alive)
                {
                    Bot.Combat.StopAttacking = false;
                    Core.Logger($"{LogPrefix} {playerAlias} died.");

                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                    {
                        if (LoneWolf.ShouldResetFight(fightAttempt))
                            return FightResult.Reset;

                        Bot.Sleep(RespawnPollDelay);
                    }

                    if (Bot.ShouldExit)
                        break;

                    if (!LoneWolf.IsMonsterAlive(BossMapId))
                        break;

                    if (LoneWolf.ShouldResetFight(fightAttempt))
                        return FightResult.Reset;

                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                    if (
                        LoneWolf.IsMonsterAlive(BossMapId)
                        && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    )
                        Core.Jump(BossCell, BossPad);

                    continue;
                }

                LoneWolf.MaintainTarget(BossMapId);

                if (HandleCounterAttack(fightAttempt))
                    continue;

                Bot.Sleep(FightPollDelay);
            }
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
            LoneWolf.StopSkillEngine();
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Ezrajal defeated.");
        return FightResult.Defeated;
    }

    private bool HandleCounterAttack(int fightAttempt)
    {
        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != BossMapId
            || !Bot.Target.HasActiveAura(CounterAttackAura)
        )
            return false;

        Bot.Combat.StopAttacking = true;
        Bot.Combat.CancelAutoAttack();
        Core.Logger($"{LogPrefix} {playerAlias} paused for {CounterAttackAura}.");
        bool resetRequested = false;

        while (
            !Bot.ShouldExit
            && Bot.Player.Alive
            && LoneWolf.IsMonsterAlive(BossMapId)
            && Bot.Player.HasTarget
            && Bot.Player.Target?.MapID == BossMapId
            && Bot.Target.HasActiveAura(CounterAttackAura)
        )
        {
            if (LoneWolf.ShouldResetFight(fightAttempt))
            {
                resetRequested = true;
                LoneWolf.StopSkillEngine();
                break;
            }

            Bot.Sleep(FightPollDelay);
        }

        Bot.Combat.StopAttacking = false;

        if (
            !resetRequested
            && !Bot.ShouldExit
            && Bot.Player.Alive
            && LoneWolf.IsMonsterAlive(BossMapId)
        )
        {
            Bot.Combat.Attack(BossMapId);
            Core.Logger($"{LogPrefix} {playerAlias} resumed after {CounterAttackAura}.");
        }

        return true;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Bot.Combat.StopAttacking = false;
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            LoneWolf.ShouldResetFight(fightAttempt);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        LoneWolf.ShouldResetFight(fightAttempt);
        Core.Jump(SafeCell, SafePad);

        if (!IsInSafeRoom())
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not reach the safe room after reset.",
                "HandleFightReset",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopArmyAfterFailedAttempts()
    {
        if (masterMode)
        {
            if (LoneWolf.IsArmyPlayer(1))
                LoneWolf.StopArmySync("ATTEMPTS_EXHAUSTED");
            else
                LoneWolf.SyncArmy("STOP_CHECK");

            Core.Logger(
                $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
                "RunFightAttempts"
            );
            return;
        }

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

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private T GetSetupOption<T>(string optionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Setup", optionName)
            : Bot.Config!.Get<T>(optionName))!;

    private T GetUltraOption<T>(string masterOptionName, string standaloneOptionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Daily_Ultras", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Stable)
            {
                ClassPreset preset = LoneWolf.KingsEcho();
                preset.CapeEnhancement = CapeSpecial.None;
                return preset;
            }

            return LoneWolf.LegionRevenant();
        }

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
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

        return "playerFour";
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
