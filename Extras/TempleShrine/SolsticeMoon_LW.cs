/*
name: Solstice Moon LW
description: Four-player CoreLoneWolf Army script for the SolsticeMoon dungeon.
tags: dungeon, solstice moon, temple shrine, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
//cs_include Scripts/UltrasLW/Extras/TempleShrine/CoreTempleShrine.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class SolsticeMoon_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();
    private static readonly CoreTempleShrine Temple = new(LoneWolf);

    private const string LogPrefix = "Solstice Moon LW";
    private const string SyncFileName = "SolsticeMoon_LW.sync";
    private const string MapName = "solsticemoon";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string MoonlightGazeAura = "Moonlight Gaze";
    private const string EnterCell = "Enter";
    private const string EnterPad = "Spawn";
    private const string RoomOneCell = "r1";
    private const string RoomOnePad = "Left";
    private const string RoomTwoCell = "r2";
    private const string RoomTwoPad = "Right";
    private const string FinalRoomCell = "r3";
    private const string FinalRoomPad = "Right";
    private const int EnterPrimaryMapId = 1;
    private const int EnterSecondaryMapId = 2;
    private const int RoomOneSecondaryMapId = 3;
    private const int RoomOnePrimaryMapId = 4;
    private const int RoomTwoSecondaryMapId = 5;
    private const int RoomTwoPrimaryMapId = 6;
    private const int FinalBossMapId = 7;
    private const int FightPollDelay = 150;
    private const int MoonlightGazeTauntSeconds = 8;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private bool isTaunter;
    private int privateRoomNumber;
    private bool masterMode;
    private bool runCompleted;

    public string OptionsStorage = "SolsticeMoon_LW";
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
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO\nReliable: Shaman / SC / AP / LOO",
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
        masterMode = true;
        runCompleted = false;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();
            masterMode = false;
        }

        return runCompleted;
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, 4, masterMode ? "Setup" : null))
            return;

        playerAlias = GetPlayerAlias();
        isTaunter = LoneWolf.IsArmyPlayer(PrimaryTaunterArmyPlayer)
            || LoneWolf.IsArmyPlayer(4);

        ClassPreset preset = GetClassPreset();
        if (isTaunter)
            preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        if (
            !Temple.PrepareOracle()
            || !PrepareBaseSetup(preset)
            || !Sync("SETUP_DONE")
        )
            return;

        string[] players = GetConfiguredPlayers();
        int runNumber = 1;

        while (!Bot.ShouldExit)
        {
            Core.Logger($"{LogPrefix} {playerAlias} starting run {runNumber}.");

            LoneWolf.EquipClass(preset);
            if (Bot.ShouldExit)
                return;

            if (
                !PrepareRunConsumables(preset)
                || !Temple.PrepareParty(players)
                || !Temple.EnterDungeon(MapName, privateRoomNumber)
                || !PrepareDungeonEntry(preset)
                || !RunEnterRoom(preset, runNumber)
                || !RunRoomOne(preset, runNumber)
                || !RunRoomTwo(preset, runNumber)
                || !RunFinalRoom(preset, runNumber)
            )
                return;

            if (!Sync($"RUN_{runNumber}_COMPLETE"))
                return;

            Core.Logger($"{LogPrefix} {playerAlias} completed run {runNumber}.");

            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();
            Bot.Combat.CancelTarget();

            Bot.Sleep(2_000);
            if (Bot.ShouldExit)
                return;

            if (!Temple.ReturnHome())
                return;

            if (masterMode)
            {
                runCompleted = true;
                return;
            }

            runNumber++;
        }
    }

    private bool ValidateOptions()
    {
        armyComposition = GetTempleOption<ArmyComposition>(
            "SolsticeMoonComposition",
            "ArmyComposition"
        );
        privateRoomNumber = GetSetupOption<int>("PrivateRoomNumber");
        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool PrepareBaseSetup(ClassPreset preset)
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

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareRunConsumables(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            LoneWolf.PrepareScrolls(EnrageScroll);

        return !Bot.ShouldExit;
    }

    private bool PrepareDungeonEntry(ClassPreset preset)
    {
        if (Bot.Player.Cell != EnterCell || Bot.Player.Pad != EnterPad)
            Core.Jump(EnterCell, EnterPad);

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        return !Bot.ShouldExit;
    }

    private bool RunEnterRoom(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedMonster = false;

        while (!Bot.ShouldExit)
        {
            if (!PrepareRoom("ROOM_1_READY", EnterCell, EnterPad))
                return false;

            StartSkillEngine(preset, useShamanFarmMode: true);
            bool resetRoom = false;

            while (!Bot.ShouldExit)
            {
                CoreTempleShrine.DungeonRecoveryResult recovery =
                    Temple.RecoverRoomDeath(runNumber, 1, roomAttempt);

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.Stopped)
                    return false;

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.ResetRoom)
                {
                    resetRoom = true;
                    break;
                }

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.RejoinRoom)
                {
                    Core.Jump(EnterCell, EnterPad);
                    StartSkillEngine(preset, useShamanFarmMode: true);
                }

                bool primaryAlive = LoneWolf.IsMonsterAlive(EnterPrimaryMapId);
                bool secondaryAlive = LoneWolf.IsMonsterAlive(EnterSecondaryMapId);
                observedMonster |= primaryAlive || secondaryAlive;

                if (observedMonster && !primaryAlive && !secondaryAlive)
                {
                    LoneWolf.StopSkillEngine();
                    Core.Logger($"{LogPrefix} {playerAlias} completed Enter.");
                    return true;
                }

                int targetMapId = primaryAlive
                    ? EnterPrimaryMapId
                    : secondaryAlive
                        ? EnterSecondaryMapId
                        : 0;

                if (targetMapId > 0)
                    LoneWolf.MaintainTarget(targetMapId);

                Bot.Sleep(FightPollDelay);
            }

            LoneWolf.StopSkillEngine();
            if (!resetRoom)
                return false;

            roomAttempt++;
        }

        return false;
    }

    private bool RunRoomOne(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedMonster = false;

        while (!Bot.ShouldExit)
        {
            if (!PrepareRoom("ROOM_2_READY", RoomOneCell, RoomOnePad))
                return false;

            StartSkillEngine(preset, useShamanFarmMode: true);
            bool resetRoom = false;
            bool gazeTauntHandled = false;

            while (!Bot.ShouldExit)
            {
                CoreTempleShrine.DungeonRecoveryResult recovery =
                    Temple.RecoverRoomDeath(runNumber, 2, roomAttempt);

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.Stopped)
                    return false;

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.ResetRoom)
                {
                    resetRoom = true;
                    break;
                }

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.RejoinRoom)
                {
                    Core.Jump(RoomOneCell, RoomOnePad);
                    StartSkillEngine(preset, useShamanFarmMode: true);
                }

                bool primaryAlive = LoneWolf.IsMonsterAlive(RoomOnePrimaryMapId);
                bool secondaryAlive = LoneWolf.IsMonsterAlive(RoomOneSecondaryMapId);
                observedMonster |= primaryAlive || secondaryAlive;

                if (observedMonster && !primaryAlive && !secondaryAlive)
                {
                    LoneWolf.StopSkillEngine();
                    Core.Logger($"{LogPrefix} {playerAlias} completed r1.");
                    return true;
                }

                int targetMapId = primaryAlive
                    ? RoomOnePrimaryMapId
                    : secondaryAlive
                        ? RoomOneSecondaryMapId
                        : 0;

                if (targetMapId > 0)
                    LoneWolf.MaintainTarget(targetMapId);

                if (primaryAlive)
                    HandleMoonlightGazeTaunt(
                        RoomOnePrimaryMapId,
                        ref gazeTauntHandled
                    );

                Bot.Sleep(FightPollDelay);
            }

            LoneWolf.StopSkillEngine();
            if (!resetRoom)
                return false;

            roomAttempt++;
        }

        return false;
    }

    private bool RunRoomTwo(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedMonster = false;

        while (!Bot.ShouldExit)
        {
            if (!PrepareRoom("ROOM_3_READY", RoomTwoCell, RoomTwoPad))
                return false;

            StartSkillEngine(preset, useShamanFarmMode: true);
            bool resetRoom = false;
            bool gazeTauntHandled = false;

            while (!Bot.ShouldExit)
            {
                CoreTempleShrine.DungeonRecoveryResult recovery =
                    Temple.RecoverRoomDeath(runNumber, 3, roomAttempt);

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.Stopped)
                    return false;

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.ResetRoom)
                {
                    resetRoom = true;
                    break;
                }

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.RejoinRoom)
                {
                    Core.Jump(RoomTwoCell, RoomTwoPad);
                    StartSkillEngine(preset, useShamanFarmMode: true);
                }

                bool primaryAlive = LoneWolf.IsMonsterAlive(RoomTwoPrimaryMapId);
                bool secondaryAlive = LoneWolf.IsMonsterAlive(RoomTwoSecondaryMapId);
                observedMonster |= primaryAlive || secondaryAlive;

                if (observedMonster && !primaryAlive && !secondaryAlive)
                {
                    LoneWolf.StopSkillEngine();
                    Core.Logger($"{LogPrefix} {playerAlias} completed r2.");
                    return true;
                }

                int targetMapId = primaryAlive
                    ? RoomTwoPrimaryMapId
                    : secondaryAlive
                        ? RoomTwoSecondaryMapId
                        : 0;

                if (targetMapId > 0)
                    LoneWolf.MaintainTarget(targetMapId);

                if (primaryAlive)
                    HandleMoonlightGazeTaunt(
                        RoomTwoPrimaryMapId,
                        ref gazeTauntHandled
                    );

                Bot.Sleep(FightPollDelay);
            }

            LoneWolf.StopSkillEngine();
            if (!resetRoom)
                return false;

            roomAttempt++;
        }

        return false;
    }

    private bool RunFinalRoom(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedBoss = false;

        while (!Bot.ShouldExit)
        {
            LoneWolf.StopPacketDetector();

            if (!LoneWolf.StartPacketDetector("ct", "converges"))
                return Failure("The Converges packet detector could not start.");

            if (!PrepareRoom("ROOM_4_READY", FinalRoomCell, FinalRoomPad))
            {
                LoneWolf.StopPacketDetector();
                return false;
            }

            StartSkillEngine(preset);
            bool resetRoom = false;
            int handledConverges = 0;

            while (!Bot.ShouldExit)
            {
                CoreTempleShrine.DungeonRecoveryResult recovery =
                    Temple.RecoverRoomDeath(runNumber, 4, roomAttempt);

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.Stopped)
                    return false;

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.ResetRoom)
                {
                    resetRoom = true;
                    break;
                }

                if (recovery == CoreTempleShrine.DungeonRecoveryResult.RejoinRoom)
                {
                    Core.Jump(FinalRoomCell, FinalRoomPad);
                    handledConverges = DrainConverges(handledConverges);
                    StartSkillEngine(preset);
                }

                bool bossAlive = LoneWolf.IsMonsterAlive(FinalBossMapId);
                observedBoss |= bossAlive;

                if (observedBoss && !bossAlive)
                {
                    LoneWolf.StopPacketDetector();
                    LoneWolf.StopSkillEngine();
                    Core.Logger(
                        $"{LogPrefix} {playerAlias} confirmed Hollow Midnight defeated."
                    );
                    return true;
                }

                if (bossAlive)
                {
                    LoneWolf.MaintainTarget(FinalBossMapId);

                    if (LoneWolf.HasPacketDetection(handledConverges + 1))
                    {
                        handledConverges++;
                        bool ownsTaunt = handledConverges % 2 == 1
                            ? LoneWolf.IsArmyPlayer(4)
                            : LoneWolf.IsArmyPlayer(PrimaryTaunterArmyPlayer);

                        if (ownsTaunt)
                        {
                            LoneWolf.RequestTaunt(FinalBossMapId);
                            Core.Logger(
                                $"{LogPrefix} {playerAlias} requested Converges taunt {handledConverges}."
                            );
                        }
                    }
                }

                Bot.Sleep(FightPollDelay);
            }

            LoneWolf.StopPacketDetector();
            LoneWolf.StopSkillEngine();

            if (!resetRoom)
                return false;

            roomAttempt++;
        }

        return false;
    }

    private bool PrepareRoom(string step, string cell, string pad)
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        if (Bot.ShouldExit || !Sync(step))
            return false;

        if (Bot.Player.Cell != cell || Bot.Player.Pad != pad)
            Core.Jump(cell, pad);

        if (Bot.ShouldExit)
            return false;

        if (Bot.Player.Cell == cell && Bot.Player.Pad == pad)
            return true;

        return Failure($"Could not reach dungeon cell {cell}.");
    }

    private void HandleMoonlightGazeTaunt(
        int targetMapId,
        ref bool auraHandled
    )
    {
        if (!LoneWolf.IsArmyPlayer(4))
            return;

        var aura = Bot.Self.GetAura(MoonlightGazeAura);
        if (aura == null)
        {
            auraHandled = false;
            return;
        }

        if (
            !auraHandled
            && aura.RemainingTime > 0
            && aura.RemainingTime <= MoonlightGazeTauntSeconds
        )
        {
            LoneWolf.RequestTaunt(targetMapId);
            auraHandled = true;
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested the Moonlight Gaze taunt."
            );
        }
    }

    private int DrainConverges(int handledConverges)
    {
        while (LoneWolf.HasPacketDetection(handledConverges + 1))
            handledConverges++;

        return handledConverges;
    }

    private void StartSkillEngine(
        ClassPreset preset,
        bool useShamanFarmMode = false
    )
    {
        SkillEngineMode skillMode =
            useShamanFarmMode
            && armyComposition == ArmyComposition.Reliable
            && LoneWolf.IsArmyPlayer(1)
                ? SkillEngineMode.Simple
                : preset.SkillMode;

        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            skillMode
        );
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Reliable)
            {
                ClassPreset shaman = LoneWolf.Shaman();
                shaman.HelmEnhancement = HelmSpecial.Examen;
                return shaman;
            }

            if (armyComposition == ArmyComposition.Stable)
                return LoneWolf.VerusDoomKnight();

            return LoneWolf.LegionRevenant();
        }

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        return LoneWolf.LordOfOrder();
    }

    private int PrimaryTaunterArmyPlayer =>
        armyComposition == ArmyComposition.Reliable ? 2 : 1;

    private T GetSetupOption<T>(string optionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Setup", optionName)
            : Bot.Config!.Get<T>(optionName))!;

    private T GetTempleOption<T>(string masterOptionName, string standaloneOptionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Temple_Shrine", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private string[] GetConfiguredPlayers() =>
        new[]
        {
            GetSetupOption<string>("player1") ?? string.Empty,
            GetSetupOption<string>("player2") ?? string.Empty,
            GetSetupOption<string>("player3") ?? string.Empty,
            GetSetupOption<string>("player4") ?? string.Empty,
        };

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

    private bool Failure(string message)
    {
        Core.Logger(
            message,
            LogPrefix,
            messageBox: true,
            stopBot: true
        );
        return false;
    }
}
