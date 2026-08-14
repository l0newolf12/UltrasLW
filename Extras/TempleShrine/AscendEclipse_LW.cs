/*
name: Ascend Eclipse LW
description: Four-player CoreLoneWolf Army script for the AscendEclipse dungeon.
tags: dungeon, ascend eclipse, temple shrine, army, corelonewolf
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

public class AscendEclipse_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();
    private static readonly CoreTempleShrine Temple = new(LoneWolf);

    private const string LogPrefix = "Ascend Eclipse LW";
    private const string SyncFileName = "AscendEclipse_LW.sync";
    private const string MapName = "ascendeclipse";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string RiteOfAscension = "Rite of Ascension";
    private const string SolarFlareAura = "Solar Flare";
    private const string DaybreakAura = "Daybreak";
    private const string GatheringLightAura = "Gathering Light";
    private const string RighteousSealAura = "Righteous Seal";
    private static readonly string[] RoomOneSecondaryAttackMarkers =
    {
        "\"nam\":\"Lunar Curse\"",
        "\"cInf\":\"m:4\"",
        "\"animStr\":\"Attack1\"",
    };
    private const string SunsWarmthAura = "Sun's Warmth";
    private const string MoonlightGazeAura = "Moonlight Gaze";
    private const string EnterCell = "Enter";
    private const string EnterPad = "Spawn";
    private const string RoomOneCell = "r1";
    private const string RoomOnePad = "Left";
    private const string RoomTwoCell = "r2";
    private const string RoomTwoPad = "Left";
    private const string FinalRoomCell = "r3";
    private const string FinalRoomPad = "Left";
    private const int EnterPrimaryMapId = 1;
    private const int EnterSecondaryMapId = 2;
    private const int RoomOnePrimaryMapId = 3;
    private const int RoomOneSecondaryMapId = 4;
    private const int RoomTwoSecondaryMapId = 5;
    private const int RoomTwoPrimaryMapId = 6;
    private const int AscendedSolsticeMapId = 7;
    private const int AscendedMidnightMapId = 8;
    private const int FightPollDelay = 100;
    private const int TimedAuraTauntSeconds = 8;
    private const int TauntTargetHold = 1_500;
    private const int RighteousSealSkillFourWindow = 1_000;
    private const float BalanceMinimumDifference = 4f;
    private const float BalanceMaximumDifference = 8f;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int privateRoomNumber;
    private bool masterMode;
    private bool masterStarted;
    private ClassPreset? masterPreset;
    private string[] masterPlayers = Array.Empty<string>();
    private int masterRunNumber = 1;

    public string OptionsStorage = "AscendEclipse_LW";
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
            "Default: LR / SC / AP / LOO\nStable: VDK / SC / AP / LOO",
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

        preset = GetClassPreset();
        preset.CapeEnhancement = LoneWolf.IsArmyPlayer(2)
            ? CapeSpecial.Absolution
            : CapeSpecial.Penitence;
        if (LoneWolf.IsArmyPlayer(4))
            preset.WeaponEnhancement = WeaponSpecial.Awe_Blast;
        preset.CombatPotion = null;

        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        if (
            !Temple.PrepareOracle()
            || !PrepareBaseSetup(preset)
            || !Sync("SETUP_DONE")
        )
            return false;

        if (masterMode && !EnsureRiteOfAscension())
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
            (!masterMode && !EnsureRiteOfAscension())
            || !PrepareRunConsumables(preset)
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
            "AscendEclipseComposition",
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

    private bool EnsureRiteOfAscension()
    {
        if (Bot.Inventory.Contains(RiteOfAscension))
        {
            Core.Logger($"{RiteOfAscension} already in inventory.", LogPrefix);
            return true;
        }

        if (!Bot.Bank.Loaded)
        {
            if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                Bot.Bank.Open();

            Bot.Bank.Load(waitForLoad: false);
            Bot.Wait.ForBankLoad(20);
        }

        if (!Bot.Bank.Contains(RiteOfAscension))
            return Failure($"{RiteOfAscension} is not in inventory or bank.");

        if (!Core.HasSpace)
            return Failure($"{RiteOfAscension} cannot be moved because the inventory is full.");

        Bot.Bank.EnsureToInventory(RiteOfAscension);
        Bot.Wait.ForTrue(() => Bot.Inventory.Contains(RiteOfAscension), 14);

        if (!Bot.Inventory.Contains(RiteOfAscension))
            return Failure($"{RiteOfAscension} could not be moved from bank.");

        Core.Logger($"{RiteOfAscension} moved from bank.", LogPrefix);
        return true;
    }

    private bool PrepareRunConsumables(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        LoneWolf.PrepareScrolls(EnrageScroll);
        return !Bot.ShouldExit;
    }

    private bool PrepareDungeonEntry(ClassPreset preset)
    {
        if (Bot.Player.Cell != EnterCell || Bot.Player.Pad != EnterPad)
            Core.Jump(EnterCell, EnterPad);

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

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

            StartSkillEngine(preset);
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
                    StartSkillEngine(preset);
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

                bool solarFlareActive = Bot.Self.HasActiveAura(SolarFlareAura);
                int targetMapId = solarFlareActive && secondaryAlive
                    ? EnterSecondaryMapId
                    : primaryAlive
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

            if (
                !LoneWolf.StartPacketDetector(
                    "ct",
                    RoomOneSecondaryAttackMarkers
                )
            )
                return Failure("The Room 1 Attack1 detector could not start.");

            StartRoomOneSkillEngine(preset);
            bool resetRoom = false;
            bool gatheringLightTauntHandled = false;
            bool righteousSealSkillFourQueued = false;
            int gatheringLightCycle = 0;
            int handledSecondaryAttacks = 0;
            DateTimeOffset secondaryTauntTargetUntil = DateTimeOffset.MinValue;

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
                    while (
                        LoneWolf.HasPacketDetection(handledSecondaryAttacks + 1)
                    )
                        handledSecondaryAttacks++;

                    secondaryTauntTargetUntil = DateTimeOffset.MinValue;
                    righteousSealSkillFourQueued = false;
                    StartRoomOneSkillEngine(preset);
                }

                bool primaryAlive = LoneWolf.IsMonsterAlive(RoomOnePrimaryMapId);
                bool secondaryAlive = LoneWolf.IsMonsterAlive(RoomOneSecondaryMapId);
                observedMonster |= primaryAlive || secondaryAlive;

                if (observedMonster && !primaryAlive && !secondaryAlive)
                {
                    LoneWolf.StopPacketDetector();
                    LoneWolf.StopSkillEngine();
                    Core.Logger($"{LogPrefix} {playerAlias} completed r1.");
                    return true;
                }

                HandleRoomOneSecondaryAttackDetections(
                    secondaryAlive,
                    ref handledSecondaryAttacks,
                    ref secondaryTauntTargetUntil
                );

                int targetMapId = secondaryAlive
                    && DateTimeOffset.Now < secondaryTauntTargetUntil
                        ? RoomOneSecondaryMapId
                        : primaryAlive
                            ? RoomOnePrimaryMapId
                            : secondaryAlive
                                ? RoomOneSecondaryMapId
                                : 0;

                if (targetMapId > 0)
                    LoneWolf.MaintainTarget(targetMapId);

                if (primaryAlive)
                    HandleAlternatingTargetAuraTaunt(
                        RoomOnePrimaryMapId,
                        GatheringLightAura,
                        ref gatheringLightTauntHandled,
                        ref gatheringLightCycle
                    );

                MaintainRoomOneArchPaladinRighteousSeal(
                    ref righteousSealSkillFourQueued
                );

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

    private bool RunRoomTwo(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedMonster = false;

        while (!Bot.ShouldExit)
        {
            if (!PrepareRoom("ROOM_3_READY", RoomTwoCell, RoomTwoPad))
                return false;

            StartSkillEngine(preset);
            bool resetRoom = false;
            bool sunsWarmthTauntHandled = false;
            bool moonlightGazeTauntHandled = false;
            DateTimeOffset lrTauntTargetUntil = DateTimeOffset.MinValue;

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
                    lrTauntTargetUntil = DateTimeOffset.MinValue;
                    StartSkillEngine(preset);
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

                if (secondaryAlive)
                {
                    bool tauntRequested = HandleSelfAuraTaunt(
                        1,
                        RoomTwoSecondaryMapId,
                        SunsWarmthAura,
                        ref sunsWarmthTauntHandled
                    );

                    if (tauntRequested)
                        lrTauntTargetUntil = DateTimeOffset.Now.AddMilliseconds(
                            TauntTargetHold
                        );
                }

                if (primaryAlive)
                    HandleSelfAuraTaunt(
                        4,
                        RoomTwoPrimaryMapId,
                        MoonlightGazeAura,
                        ref moonlightGazeTauntHandled
                    );

                int targetMapId = LoneWolf.IsArmyPlayer(1)
                    && secondaryAlive
                    && DateTimeOffset.Now < lrTauntTargetUntil
                        ? RoomTwoSecondaryMapId
                        : primaryAlive
                            ? RoomTwoPrimaryMapId
                            : secondaryAlive
                                ? RoomTwoSecondaryMapId
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

    private bool RunFinalRoom(ClassPreset preset, int runNumber)
    {
        int roomAttempt = 1;
        bool observedBoss = false;

        while (!Bot.ShouldExit)
        {
            LoneWolf.StopPacketDetector();

            if (!PrepareFinalRoomClassReset(preset))
                return false;

            bool solsticePair = LoneWolf.IsArmyPlayer(2)
                || LoneWolf.IsArmyPlayer(3);
            string convergesMarker = solsticePair
                ? "sun converges"
                : "moon converges";

            if (
                !LoneWolf.StartPacketChoiceDetector(
                    "ct",
                    new[] { convergesMarker },
                    pauseSkillChoice: convergesMarker
                )
            )
                return Failure("The Converges packet detector could not start.");

            if (!PrepareRoom("ROOM_4_READY", FinalRoomCell, FinalRoomPad))
            {
                LoneWolf.StopPacketDetector();
                return false;
            }

            StartSkillEngine(preset);
            bool resetRoom = false;
            int handledConverges = 0;
            DateTimeOffset tauntTargetUntil = DateTimeOffset.MinValue;

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
                    tauntTargetUntil = DateTimeOffset.MinValue;
                    StartSkillEngine(preset);
                }

                bool solsticeAlive = LoneWolf.IsMonsterAlive(AscendedSolsticeMapId);
                bool midnightAlive = LoneWolf.IsMonsterAlive(AscendedMidnightMapId);
                observedBoss |= solsticeAlive || midnightAlive;

                if (observedBoss && !solsticeAlive && !midnightAlive)
                {
                    LoneWolf.StopPacketDetector();
                    LoneWolf.StopSkillEngine();
                    Core.Logger(
                        $"{LogPrefix} {playerAlias} confirmed both Ascended bosses defeated."
                    );
                    return true;
                }

                HandleConvergesDetections(
                    solsticeAlive,
                    midnightAlive,
                    ref handledConverges,
                    ref tauntTargetUntil
                );

                MaintainFinalTarget(
                    solsticeAlive,
                    midnightAlive,
                    tauntTargetUntil
                );

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

    private bool PrepareFinalRoomClassReset(ClassPreset preset)
    {
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();

        LoneWolf.EquipClass(LoneWolf.Oracle());
        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1_000);
        LoneWolf.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1_000);
        Core.Logger(
            $"{LogPrefix} {playerAlias} completed the Oracle reset before the final room."
        );
        return !Bot.ShouldExit;
    }

    private void HandleConvergesDetections(
        bool solsticeAlive,
        bool midnightAlive,
        ref int handledConverges,
        ref DateTimeOffset tauntTargetUntil
    )
    {
        while (LoneWolf.HasPacketDetection(handledConverges + 1))
        {
            handledConverges++;

            int assignedMapId = GetFinalAssignedMapId();
            bool assignedBossAlive = assignedMapId == AscendedSolsticeMapId
                ? solsticeAlive
                : midnightAlive;

            if (!assignedBossAlive || !OwnsFinalTaunt(handledConverges))
            {
                LoneWolf.ResumeSkillEngine();
                continue;
            }

            LoneWolf.MaintainTarget(assignedMapId);
            LoneWolf.RequestAbsolutePriorityTaunt(assignedMapId);
            tauntTargetUntil = DateTimeOffset.Now.AddMilliseconds(TauntTargetHold);
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested absolute priority Converges taunt {handledConverges} on MapID {assignedMapId}."
            );
        }
    }

    private void MaintainFinalTarget(
        bool solsticeAlive,
        bool midnightAlive,
        DateTimeOffset tauntTargetUntil
    )
    {
        if (!solsticeAlive || !midnightAlive)
        {
            int survivingBoss = solsticeAlive
                ? AscendedSolsticeMapId
                : midnightAlive
                    ? AscendedMidnightMapId
                    : 0;

            if (survivingBoss > 0)
                LoneWolf.MaintainTarget(survivingBoss);

            return;
        }

        int assignedMapId = GetFinalAssignedMapId();
        if (LoneWolf.HasPendingAbsolutePriorityTaunt())
        {
            LoneWolf.MaintainTarget(assignedMapId);
            return;
        }

        if (DateTimeOffset.Now < tauntTargetUntil)
        {
            LoneWolf.MaintainTarget(assignedMapId);
            return;
        }

        float solsticeHealth = GetMonsterHealthPercent(AscendedSolsticeMapId);
        float midnightHealth = GetMonsterHealthPercent(AscendedMidnightMapId);

        if (Bot.Self.HasActiveAura(DaybreakAura))
        {
            LoneWolf.MaintainTarget(AscendedMidnightMapId);
            return;
        }

        float gap = midnightHealth - solsticeHealth;
        bool isBalancer = LoneWolf.IsArmyPlayer(2)
            || (armyComposition == ArmyComposition.Stable
                ? LoneWolf.IsArmyPlayer(1)
                : LoneWolf.IsArmyPlayer(4));
        if (isBalancer)
        {
            if (gap < BalanceMinimumDifference)
                LoneWolf.MaintainTarget(AscendedSolsticeMapId);
            else if (gap > BalanceMaximumDifference)
                LoneWolf.MaintainTarget(AscendedMidnightMapId);
            else
                LoneWolf.MaintainTarget(assignedMapId);
        }
        else
            LoneWolf.MaintainTarget(assignedMapId);
    }

    private float GetMonsterHealthPercent(int mapId)
    {
        var monsters = Bot.Monsters?.MapMonsters;
        if (monsters == null)
            return 0;

        foreach (var monster in monsters)
        {
            if (monster != null && monster.MapID == mapId && monster.MaxHP > 0)
                return monster.HP * 100f / monster.MaxHP;
        }

        return 0;
    }

    private bool OwnsFinalTaunt(int cycle)
    {
        bool openingOwner = LoneWolf.IsArmyPlayer(1)
            || LoneWolf.IsArmyPlayer(3);
        return openingOwner ? cycle % 2 == 1 : cycle % 2 == 0;
    }

    private int GetFinalAssignedMapId() =>
        LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(3)
            ? AscendedSolsticeMapId
            : AscendedMidnightMapId;

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

    private void HandleAlternatingTargetAuraTaunt(
        int targetMapId,
        string auraName,
        ref bool auraHandled,
        ref int cycle
    )
    {
        if (!LoneWolf.IsArmyPlayer(1) && !LoneWolf.IsArmyPlayer(3))
            return;

        bool hasAura = Bot.Target
            .GetMonsterAura(targetMapId)
            .Contains(auraName, StringComparison.Ordinal);

        if (!hasAura)
        {
            auraHandled = false;
            return;
        }

        if (auraHandled)
            return;

        auraHandled = true;
        cycle++;
        bool ownsTaunt = cycle % 2 == 1
            ? LoneWolf.IsArmyPlayer(3)
            : LoneWolf.IsArmyPlayer(1);

        if (!ownsTaunt)
            return;

        LoneWolf.RequestTaunt(targetMapId);
        if (LoneWolf.IsArmyPlayer(3))
        {
            LoneWolf.RequestPrioritySkill(3);
            Core.Logger(
                $"{LogPrefix} {playerAlias} queued skill 3 after its {auraName} taunt cycle {cycle}."
            );
        }

        Core.Logger(
            $"{LogPrefix} {playerAlias} requested {auraName} taunt cycle {cycle} on MapID {targetMapId}."
        );
    }

    private void MaintainRoomOneArchPaladinRighteousSeal(
        ref bool skillFourQueued
    )
    {
        if (!LoneWolf.IsArmyPlayer(3))
            return;

        var righteousSeal = Bot.Target.GetAura(RighteousSealAura);
        if (righteousSeal == null)
        {
            skillFourQueued = false;
            return;
        }

        if (skillFourQueued || LoneWolf.HasPendingPrioritySkill())
            return;

        TimeSpan remaining = righteousSeal.ExpiresAt - DateTimeOffset.Now;
        if (
            remaining <= TimeSpan.Zero
            || remaining
                > TimeSpan.FromMilliseconds(RighteousSealSkillFourWindow)
        )
            return;

        LoneWolf.RequestPrioritySkill(4);
        skillFourQueued = true;
        Core.Logger(
            $"{LogPrefix} {playerAlias} queued skill 4 for Righteous Seal."
        );
    }

    private void HandleRoomOneSecondaryAttackDetections(
        bool secondaryAlive,
        ref int handledAttacks,
        ref DateTimeOffset tauntTargetUntil
    )
    {
        while (LoneWolf.HasPacketDetection(handledAttacks + 1))
        {
            handledAttacks++;
            if (
                !secondaryAlive
                || handledAttacks < 3
                || (handledAttacks - 3) % 4 != 0
            )
                continue;

            int tauntCycle = (handledAttacks - 3) / 4 + 1;
            bool ownsTaunt = tauntCycle % 2 == 1
                ? LoneWolf.IsArmyPlayer(2)
                : LoneWolf.IsArmyPlayer(4);

            if (!ownsTaunt)
                continue;

            LoneWolf.MaintainTarget(RoomOneSecondaryMapId);
            LoneWolf.RequestTaunt(RoomOneSecondaryMapId);
            tauntTargetUntil = DateTimeOffset.Now.AddMilliseconds(
                TauntTargetHold
            );
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested MapID 4 taunt after Attack1 count {handledAttacks}."
            );
        }
    }

    private bool HandleSelfAuraTaunt(
        int armyPlayer,
        int targetMapId,
        string auraName,
        ref bool auraHandled
    )
    {
        if (!LoneWolf.IsArmyPlayer(armyPlayer))
            return false;

        var aura = Bot.Self.GetAura(auraName);
        if (aura == null)
        {
            auraHandled = false;
            return false;
        }

        if (
            !auraHandled
            && aura.RemainingTime > 0
            && aura.RemainingTime <= TimedAuraTauntSeconds
        )
        {
            LoneWolf.MaintainTarget(targetMapId);
            LoneWolf.RequestTaunt(targetMapId);
            auraHandled = true;
            Core.Logger(
                $"{LogPrefix} {playerAlias} requested the {auraName} taunt on MapID {targetMapId}."
            );
            return true;
        }

        return false;
    }

    private int DrainConverges(int handledConverges)
    {
        while (LoneWolf.HasPacketDetection(handledConverges + 1))
            handledConverges++;

        return handledConverges;
    }

    private void StartSkillEngine(ClassPreset preset)
    {
        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            true,
            LogPrefix,
            preset.SkillMode
        );
    }

    private void StartRoomOneSkillEngine(ClassPreset preset)
    {
        if (!LoneWolf.IsArmyPlayer(3))
        {
            StartSkillEngine(preset);
            return;
        }

        LoneWolf.StartSkillEngine(
            new[] { 2, 1 },
            playerAlias,
            true,
            LogPrefix,
            preset.SkillMode
        );
    }

    private ClassPreset GetClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
            return armyComposition == ArmyComposition.Stable
                ? LoneWolf.VerusDoomKnight()
                : LoneWolf.LegionRevenant();

        if (LoneWolf.IsArmyPlayer(2))
            return LoneWolf.StoneCrusher();

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        return LoneWolf.LordOfOrder();
    }

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
