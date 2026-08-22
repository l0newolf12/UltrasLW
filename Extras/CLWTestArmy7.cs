/*
name: CLW Test Army 7
description: Tests CoreLoneWolf Army synchronization with seven players.
tags: prototype, corelonewolf, army, sync, seven-player, test
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class CLWTestArmy7
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private static readonly int[] Skills = { 1, 2, 3, 4 };

    private const string LogPrefix = "CLW Test Army 7";
    private const string SyncFileName = "CLWTestArmy7.sync";
    private const string MapName = "classhall";
    private const string FightCell = "r4";
    private const string FightPad = "Left";
    private const int MonsterMapId = 1;
    private const int CombatDuration = 10_000;

    private string playerAlias = string.Empty;
    private int privateRoomNumber;

    public string OptionsStorage = "CLWTestArmy7";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        LoneWolf.player1,
        LoneWolf.player2,
        LoneWolf.player3,
        LoneWolf.player4,
        LoneWolf.player5,
        LoneWolf.player6,
        LoneWolf.player7,
        new Option<int>(
            "PrivateRoomNumber",
            "Private Room Number",
            "Private room number from 1001 through 99999.",
            0
        ),
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Config?.Configure();

        try
        {
            RunPrototype();
        }
        finally
        {
            LoneWolf.StopSkillEngine();
        }
    }

    private void RunPrototype()
    {
        privateRoomNumber = Bot.Config!.Get<int>("PrivateRoomNumber");
        if (!LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        if (!LoneWolf.StartArmySync(SyncFileName, 7))
            return;

        playerAlias = GetPlayerAlias();
        Core.Logger($"{LogPrefix} started as {playerAlias}.");

        if (!JoinClassHall() || !RunCombat() || !ReturnHome())
            return;

        RunStopTest();
    }

    private bool JoinClassHall()
    {
        Core.Join($"{MapName}-{privateRoomNumber}");
        Core.Jump(FightCell, FightPad);

        if (Bot.ShouldExit)
            return false;

        return Sync("CLASSHALL_READY");
    }

    private bool RunCombat()
    {
        LoneWolf.StartSkillEngine(Skills, playerAlias, false, LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started the custom skill engine.");

        try
        {
            Bot.Combat.Attack(MonsterMapId);
            Core.Logger($"{LogPrefix} {playerAlias} started the 10 second combat test.");
            Bot.Sleep(CombatDuration);
        }
        finally
        {
            LoneWolf.StopSkillEngine();
            Bot.Combat.CancelTarget();
        }

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} completed the combat test.");
        return Sync("COMBAT_COMPLETE");
    }

    private bool ReturnHome()
    {
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} reached its own house.");
        return Sync("HOME_READY");
    }

    private void RunStopTest()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            Bot.Sleep(2000);

            if (Bot.ShouldExit)
                return;

            if (LoneWolf.StopArmySync("TEST_COMPLETE"))
                Core.Logger($"{LogPrefix} playerOne published TEST_COMPLETE.");
            else
                Core.Logger($"{LogPrefix} playerOne could not publish TEST_COMPLETE.");

            return;
        }

        if (LoneWolf.SyncArmy("STOP_CHECK"))
            Core.Logger($"{LogPrefix} {playerAlias} unexpectedly passed STOP_CHECK.");
        else
            Core.Logger($"{LogPrefix} {playerAlias} detected TEST_COMPLETE.");
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!LoneWolf.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
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
}
