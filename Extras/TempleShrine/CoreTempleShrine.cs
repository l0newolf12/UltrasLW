/*
name: Core Temple Shrine
description: Shared party, entry, recovery, and repeat-run helpers for Temple Shrine dungeon scripts.
tags: core, temple shrine, dungeon, army
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;

#nullable enable

public class CoreTempleShrine
{
    public enum DungeonRecoveryResult
    {
        Continue,
        RejoinRoom,
        ResetRoom,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;

    private const int PartyPollDelay = 250;
    private const int RecoveryPollDelay = 500;

    private readonly CoreLoneWolf? LoneWolf;
    private CoreLoneWolf ActiveLoneWolf => LoneWolf!;

    private string[] partyPlayers = Array.Empty<string>();
    private string expectedPartyLeader = string.Empty;
    private string recoveryScope = string.Empty;
    private bool? reportedAlive;

    public CoreTempleShrine()
    {
    }

    public CoreTempleShrine(CoreLoneWolf loneWolf)
    {
        LoneWolf = loneWolf;
    }

    public bool PrepareParty(string[] players)
    {
        if (LoneWolf == null)
            return Failure("CoreTempleShrine requires the active CoreLoneWolf instance.");

        if (!ValidatePartyPlayers(players))
            return Failure("Temple Shrine party configuration is invalid.");

        partyPlayers = players.Select(NormalizeUsername).ToArray();
        expectedPartyLeader = partyPlayers[0];

        if (!LoneWolf.SyncArmy("PARTY_SETUP"))
            return false;

        if (!IsExactParty() && IsInParty())
        {
            Bot.Send.Packet("%xt%zm%gp%1%pl%");
            Core.Logger("Left the current party.", "CoreTempleShrine");
        }

        Bot.Sleep(1_000);
        if (Bot.ShouldExit)
            return false;

        bool listensForInvites =
            !LoneWolf.IsArmyPlayer(1) && !IsExactParty();
        if (
            listensForInvites
            && !LoneWolf.StartPacketDetector("pi", "\"cmd\":\"pi\"")
        )
            return false;

        try
        {
            EnsurePartyInvitesEnabled();
            Bot.Sleep(500);
            if (Bot.ShouldExit)
                return false;

            if (LoneWolf.IsArmyPlayer(1) && !IsExactParty())
            {
                for (int index = 1; index < partyPlayers.Length; index++)
                {
                    Bot.Send.Packet(
                        $"%xt%zm%gp%1%pi%{partyPlayers[index]}%"
                    );

                    Bot.Sleep(750);
                    if (Bot.ShouldExit)
                        return false;
                }

                return WaitForExactParty();
            }

            return !listensForInvites || WaitForPartyInvitation();
        }
        finally
        {
            if (listensForInvites)
                LoneWolf.StopPacketDetector();
        }
    }

    public bool EnterDungeon(string map, int privateRoomNumber)
    {
        if (LoneWolf == null)
            return Failure("CoreTempleShrine requires the active CoreLoneWolf instance.");

        if (
            string.IsNullOrWhiteSpace(map)
            || privateRoomNumber < 1001
            || privateRoomNumber > 99999
        )
            return Failure("Temple Shrine dungeon entry configuration is invalid.");

        string mapName = map.Trim().ToLowerInvariant();

        if (LoneWolf.IsArmyPlayer(1))
        {
            Bot.Send.Packet(
                $"%xt%zm%dungeonQueue%{Bot.Map.RoomID}%{mapName}-{privateRoomNumber}%"
            );
            Core.Logger("Dungeon entry packet sent.", "CoreTempleShrine");
        }

        while (
            !Bot.ShouldExit
            && !string.Equals(Bot.Map.Name, mapName, StringComparison.OrdinalIgnoreCase)
        )
            Bot.Sleep(PartyPollDelay);

        if (Bot.ShouldExit)
            return false;

        string fullName = Bot.Map.FullName?.Trim() ?? string.Empty;
        string expectedFullName = $"{mapName}-{privateRoomNumber}";
        if (
            fullName.Contains('-')
            && !string.Equals(
                fullName,
                expectedFullName,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return Failure("The Army entered the wrong dungeon instance.");

        return LoneWolf.SyncArmy("DUNGEON_ARRIVED");
    }

    public DungeonRecoveryResult RecoverRoomDeath(
        int runNumber,
        int roomNumber,
        int roomAttempt
    )
    {
        if (LoneWolf == null)
        {
            Failure("CoreTempleShrine requires the active CoreLoneWolf instance.");
            return DungeonRecoveryResult.Stopped;
        }

        if (runNumber <= 0 || roomNumber <= 0 || roomAttempt <= 0)
            return DungeonRecoveryResult.Stopped;

        string scope = $"{runNumber}_{roomNumber}_{roomAttempt}";
        if (!string.Equals(recoveryScope, scope, StringComparison.Ordinal))
        {
            recoveryScope = scope;
            reportedAlive = null;
        }

        string deadSignal = $"ROOM_DEAD_{scope}";
        string aliveSignal = $"ROOM_ALIVE_{scope}";
        string resetSignal = $"ROOM_RESET_{scope}";

        if (LoneWolf.HasArmySignal(resetSignal, 1))
            return FinishRoomReset(
                aliveSignal,
                scope,
                runNumber,
                roomNumber,
                roomAttempt
            );

        if (Bot.Player.Alive)
        {
            ReportAliveIfNeeded(aliveSignal);
            return DungeonRecoveryResult.Continue;
        }

        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelTarget();
        ReportDeadIfNeeded(deadSignal, runNumber, roomNumber, roomAttempt);

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            if (
                LoneWolf.IsArmyPlayer(1)
                && !LoneWolf.HasArmySignal(resetSignal, 1)
                && AreAllPlayersDead(deadSignal, aliveSignal)
            )
            {
                if (!LoneWolf.SendArmySignal(resetSignal))
                    return DungeonRecoveryResult.Stopped;
            }

            Bot.Sleep(RecoveryPollDelay);
        }

        if (Bot.ShouldExit)
            return DungeonRecoveryResult.Stopped;

        ReportAliveIfNeeded(aliveSignal);

        return LoneWolf.HasArmySignal(resetSignal, 1)
            ? FinishRoomReset(
                aliveSignal,
                scope,
                runNumber,
                roomNumber,
                roomAttempt
            )
            : DungeonRecoveryResult.RejoinRoom;
    }

    public bool PrepareOracle()
    {
        if (LoneWolf == null)
            return Failure("CoreTempleShrine requires the active CoreLoneWolf instance.");

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
                return Failure(
                    "Oracle could not be moved from bank because no inventory slot is available."
                );

            Bot.Bank.EnsureToInventory(oracleName);
            Bot.Wait.ForTrue(() => Bot.Inventory.Contains(oracleName), 14);

            if (!Bot.Inventory.Contains(oracleName))
                return Failure("Oracle could not be moved from bank.");

            Core.Logger("Oracle moved from bank.", "CoreTempleShrine");
            return true;
        }

        if (!Core.HasSpace)
            return Failure(
                "Oracle could not be purchased because no inventory slot is available."
            );

        Core.BuyItem("classhalla", 759, oracleName);
        if (!Bot.Inventory.Contains(oracleName))
            return Failure("Oracle could not be purchased.");

        Core.Logger("Oracle purchased.", "CoreTempleShrine");
        return true;
    }

    public bool ReturnHome()
    {
        if (LoneWolf == null)
            return Failure("CoreTempleShrine requires the active CoreLoneWolf instance.");

        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");

        while (
            !Bot.ShouldExit
            && !string.Equals(Bot.Map.Name, "house", StringComparison.OrdinalIgnoreCase)
        )
            Bot.Sleep(PartyPollDelay);

        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1_000);
        ActiveLoneWolf.EquipClass(ActiveLoneWolf.Oracle());
        if (Bot.ShouldExit)
            return false;

        if (
            !string.Equals(
                Bot.Player.CurrentClass?.Name,
                "Oracle",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return Failure("Oracle could not be equipped.");

        Bot.Sleep(1_000);
        return !Bot.ShouldExit;
    }

    private bool WaitForExactParty()
    {
        long timeoutAt = Environment.TickCount64 + 30_000;

        while (
            !Bot.ShouldExit
            && !IsExactParty()
            && Environment.TickCount64 < timeoutAt
        )
            Bot.Sleep(PartyPollDelay);

        if (Bot.ShouldExit)
            return false;

        if (!IsExactParty())
            return Failure(
                "Exact four-player party was not formed within 30 seconds."
            );

        Core.Logger("Exact four-player party confirmed.", "CoreTempleShrine");
        return true;
    }

    private void EnsurePartyInvitesEnabled()
    {
        if (Bot.Flash.GetGameObject<bool>("uoPref.bParty"))
            return;

        Bot.Send.Packet("%xt%zm%cmd%1%uopref%bParty%true%");
        Core.Logger("Party invitations enabled.", "CoreTempleShrine");
    }

    private bool WaitForPartyInvitation()
    {
        int detectionNumber = 1;

        while (!Bot.ShouldExit)
        {
            if (!ActiveLoneWolf.HasPacketDetection(detectionNumber))
            {
                Bot.Sleep(PartyPollDelay);
                continue;
            }

            string packet = ActiveLoneWolf.GetPacketDetectorPacket();
            detectionNumber++;
            if (TryAcceptPartyInvitation(packet))
                return true;
        }

        return false;
    }

    private bool TryAcceptPartyInvitation(string packet)
    {
        if (string.IsNullOrWhiteSpace(packet))
            return false;

        JObject parsedPacket;
        try
        {
            parsedPacket = JObject.Parse(packet);
        }
        catch
        {
            return false;
        }

        JToken? data = parsedPacket["params"]?["dataObj"];
        if (!string.Equals(data?["cmd"]?.ToString(), "pi", StringComparison.Ordinal))
            return false;

        string owner = NormalizeUsername(data?["owner"]?.ToString());
        string partyIdText = data?["pid"]?.ToString() ?? string.Empty;
        if (!int.TryParse(partyIdText, out int partyId) || partyId <= 0)
            return false;

        if (
            !string.Equals(
                owner,
                expectedPartyLeader,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return false;

        Bot.Send.Packet($"%xt%zm%gp%1%pa%{partyId}%");
        Core.Logger("Accepted the playerOne party invitation.", "CoreTempleShrine");
        return true;
    }

    private DungeonRecoveryResult FinishRoomReset(
        string aliveSignal,
        string scope,
        int runNumber,
        int roomNumber,
        int roomAttempt
    )
    {
        while (!Bot.ShouldExit && !Bot.Player.Alive)
            Bot.Sleep(RecoveryPollDelay);

        if (Bot.ShouldExit)
            return DungeonRecoveryResult.Stopped;

        ReportAliveIfNeeded(aliveSignal);
        Core.Logger(
            $"{GetPlayerAlias()} received the coordinated room reset in run {runNumber}, room {roomNumber}, attempt {roomAttempt}.",
            "CoreTempleShrine"
        );

        return ActiveLoneWolf.SyncArmy($"ROOM_RESET_READY_{scope}")
            ? DungeonRecoveryResult.ResetRoom
            : DungeonRecoveryResult.Stopped;
    }

    private bool AreAllPlayersDead(string deadSignal, string aliveSignal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            long deadAt = ActiveLoneWolf.GetArmyTimestamp(deadSignal, playerNumber);
            long aliveAt = ActiveLoneWolf.GetArmyTimestamp(aliveSignal, playerNumber);
            if (deadAt <= 0 || deadAt <= aliveAt)
                return false;
        }

        return true;
    }

    private void ReportDeadIfNeeded(
        string deadSignal,
        int runNumber,
        int roomNumber,
        int roomAttempt
    )
    {
        if (reportedAlive == false)
            return;

        if (
            ActiveLoneWolf.SendArmyTimestamp(
                deadSignal,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            )
        )
        {
            reportedAlive = false;
            Core.Logger(
                $"{GetPlayerAlias()} died in run {runNumber}, room {roomNumber}, attempt {roomAttempt}.",
                "CoreTempleShrine"
            );
        }
    }

    private void ReportAliveIfNeeded(string aliveSignal)
    {
        if (reportedAlive != false)
            return;

        if (
            ActiveLoneWolf.SendArmyTimestamp(
                aliveSignal,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            )
        )
            reportedAlive = true;
    }

    private string GetPlayerAlias()
    {
        if (ActiveLoneWolf.IsArmyPlayer(1))
            return "playerOne";

        if (ActiveLoneWolf.IsArmyPlayer(2))
            return "playerTwo";

        if (ActiveLoneWolf.IsArmyPlayer(3))
            return "playerThree";

        return "playerFour";
    }

    private bool ValidatePartyPlayers(string[] players)
    {
        if (players == null || players.Length != 4)
            return false;

        string[] normalized = players.Select(NormalizeUsername).ToArray();
        return normalized.All(player => player.Length > 0)
            && normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4;
    }

    private bool IsExactParty()
    {
        string[] currentParty = GetCurrentParty();
        return currentParty.Length == partyPlayers.Length
            && partyPlayers.All(player =>
                currentParty.Contains(player, StringComparer.OrdinalIgnoreCase)
            )
            && string.Equals(
                GetPartyLeader(),
                expectedPartyLeader,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private bool IsInParty() =>
        GetOtherPartyMembers().Length > 0 || GetPartyLeader().Length > 0;

    private string[] GetCurrentParty() =>
        GetOtherPartyMembers()
            .Append(NormalizeUsername(Core.Username()))
            .Where(username => username.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] GetOtherPartyMembers() =>
        Bot.Flash.GetGameObject<string[]>("world.partyMembers")?
            .Select(NormalizeUsername)
            .Where(username => username.Length > 0)
            .ToArray() ?? Array.Empty<string>();

    private string GetPartyLeader() =>
        NormalizeUsername(
            Bot.Flash.GetGameObject<string>("world.partyOwner")
        );

    private bool Failure(string message)
    {
        Core.Logger(
            message,
            "CoreTempleShrine",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private static string NormalizeUsername(string? username) =>
        username?.Trim().ToLowerInvariant() ?? string.Empty;
}
