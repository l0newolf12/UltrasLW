/*
name: Midnight Sun LW
description: Four-player CoreLoneWolf Army script for the MidnightSun dungeon.
tags: dungeon, midnight sun, temple shrine, army, corelonewolf
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

public class MidnightSun_LW
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

    private const string LogPrefix = "Midnight Sun LW";
    private const string SyncFileName = "MidnightSun_LW.sync";
    private const string MapName = "midnightsun";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string GatheringLightAura = "Gathering Light";
    private const string SunsWarmthAura = "Sun's Warmth";
    private const string EnterCell = "Enter";
    private const string EnterPad = "Spawn";
    private const string RoomOneCell = "r1";
    private const string RoomOnePad = "Left";
    private const string RoomTwoCell = "r2";
    private const string RoomTwoPad = "Left";
    private const string FinalRoomCell = "r3";
    private const string FinalRoomPad = "Left";
    private const int EnterSecondaryMapId = 1;
    private const int EnterPrimaryMapId = 2;
    private const int RoomOneSecondaryMapId = 3;
    private const int RoomOnePrimaryMapId = 4;
    private const int RoomTwoSecondaryMapId = 5;
    private const int RoomTwoPrimaryMapId = 6;
    private const int FinalBossMapId = 7;
    private const int FightPollDelay = 150;
    private const int SunsWarmthTauntSeconds = 8;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private bool isTaunter;
    private int privateRoomNumber;
    private bool masterMode;
    private bool masterStarted;
    private ClassPreset? masterPreset;
    private string[] masterPlayers = Array.Empty<string>();
    private int masterRunNumber = 1;

    public string OptionsStorage = "MidnightSun_LW";
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
        try
        {
            return StartFromMaster() && RunOnceFromMaster();
        }
        finally
        {
            StopFromMaster();
        }
    }

    public bool StartFromMaster()
    {
        masterMode = true;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        if (!PrepareLifecycle(out ClassPreset preset, out string[] players))
            return false;

        masterPreset = preset;
        masterPlayers = players;
        masterRunNumber = 1;
        masterStarted = true;
        return true;
    }

    public bool RunOnceFromMaster()
    {
        if (!masterStarted || masterPreset == null)
            return false;

        if (!RunOnce(masterPreset, masterPlayers, masterRunNumber))
            return false;

        masterRunNumber++;
        return true;
    }

    public void StopFromMaster()
    {
        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        masterPreset = null;
        masterPlayers = Array.Empty<string>();
        masterRunNumber = 1;
        masterStarted = false;
        masterMode = false;
    }

    private void Run()
    {
        if (!PrepareLifecycle(out ClassPreset preset, out string[] players))
            return;

        int runNumber = 1;

        while (!Bot.ShouldExit)
        {
            if (!RunOnce(preset, players, runNumber))
                return;

            runNumber++;
        }
    }

    private bool PrepareLifecycle(out ClassPreset preset, out string[] players)
    {
        preset = new ClassPreset();
        players = Array.Empty<string>();

        if (!ValidateOptions())
            return false;

        if (!LoneWolf.StartArmySync(SyncFileName, 4, masterMode ? "Setup" : null))
            return false;

        playerAlias = GetPlayerAlias();
        isTaunter = LoneWolf.IsArmyPlayer(PrimaryTaunterArmyPlayer)
            || LoneWolf.IsArmyPlayer(4);

        preset = GetClassPreset();
        if (isTaunter)
            preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        if (
            !Temple.PrepareOracle()
            || !PrepareBaseSetup(preset)
            || !Sync("SETUP_DONE")
        )
            return false;

        players = GetConfiguredPlayers();
        return true;
    }

    private bool RunOnce(ClassPreset preset, string[] players, int runNumber)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting run {runNumber}.");

        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

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
            return false;

        if (!Sync($"RUN_{runNumber}_COMPLETE"))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} completed run {runNumber}.");

        LoneWolf.StopPacketDetector();
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        Bot.Sleep(2_000);
        if (Bot.ShouldExit)
            return false;

        if (!Temple.ReturnHome())
            return false;

        return true;
    }

    private bool ValidateOptions()
    {
        armyComposition = GetTempleOption<ArmyComposition>(
            "MidnightSunComposition",
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
            bool gatheringLightTauntHandled = false;

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

                if (primaryAlive)
                    HandleGatheringLightTaunt(
                        PrimaryTaunterArmyPlayer,
                        EnterPrimaryMapId,
                        ref gatheringLightTauntHandled
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
            bool sunsWarmthTauntHandled = false;

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
                    HandleSunsWarmthTaunt(
                        RoomOnePrimaryMapId,
                        ref sunsWarmthTauntHandled
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
            bool sunsWarmthTauntHandled = false;
            bool gatheringLightTauntHandled = false;

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
                    HandleSunsWarmthTaunt(
                        RoomTwoPrimaryMapId,
                        ref sunsWarmthTauntHandled
                    );

                if (secondaryAlive)
                    HandleGatheringLightTaunt(
                        4,
                        RoomTwoSecondaryMapId,
                        ref gatheringLightTauntHandled
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
                        $"{LogPrefix} {playerAlias} confirmed Hollow Solstice defeated."
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

    private void HandleGatheringLightTaunt(
        int armyPlayer,
        int targetMapId,
        ref bool auraHandled
    )
    {
        if (!LoneWolf.IsArmyPlayer(armyPlayer))
            return;

        bool hasGatheringLight = Bot.Target
            .GetMonsterAura(targetMapId)
            .Contains(GatheringLightAura, StringComparison.Ordinal);

        if (!hasGatheringLight)
        {
            auraHandled = false;
            return;
        }

        if (auraHandled)
            return;

        LoneWolf.RequestTaunt(targetMapId);
        auraHandled = true;
        Core.Logger(
            $"{LogPrefix} {playerAlias} requested the Gathering Light taunt on MapID {targetMapId}."
        );
    }

    private void HandleSunsWarmthTaunt(
        int targetMapId,
        ref bool auraHandled
    )
    {
        if (!LoneWolf.IsArmyPlayer(PrimaryTaunterArmyPlayer))
            return;

        var aura = Bot.Self.GetAura(SunsWarmthAura);
        if (aura == null)
        {
            auraHandled = false;
            return;
        }

        if (
            !auraHandled
            && aura.RemainingTime > 0
            && aura.RemainingTime <= SunsWarmthTauntSeconds
        )
        {
            LoneWolf.RequestTaunt(targetMapId);
            auraHandled = true;
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested the Sun's Warmth taunt on MapID {targetMapId}."
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
