/*
name: Unbank 51 Percent Weapons
description: Moves every banked weapon with at least 51 percent generic damage boost into the inventory.
tags: utility, bank, weapons
*/

//cs_include Scripts/CoreBots.cs
using Skua.Core.Interfaces;
using System.Linq;

#nullable enable

public class Unbank51PercentWeapons
{
    private CoreBots Core => CoreBots.Instance;
    private IScriptInterface Bot => IScriptInterface.Instance;

    public void ScriptMain(IScriptInterface Bot)
    {
        UnbankWeapons();
    }

    private void UnbankWeapons()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForBankLoad(20);

        int[] weaponIDs = (Bot.Bank.Items ?? [])
            .Where(item =>
                item != null
                && !Core.NoneEnhancableFilter(item)
                && Core.GetBoostFloat(item, "dmgAll") >= 1.51f
            )
            .Select(item => item.ID)
            .ToArray();

        if (weaponIDs.Length == 0)
        {
            Core.Logger("No banked 51 percent damage boost weapons were found.");
            return;
        }

        Core.Unbank(weaponIDs);
    }
}
