/*
name: Ultra Dage LW
description: Four-player CoreLoneWolf Army script for Ultra Dage.
tags: ultra, dage, army, corelonewolf
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDage_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
    }

    private enum FightResult
    {
        Continue,
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Ultra Dage LW";
    private const string SyncFileName = "UltraDage_LW.sync";
    private const string MapName = "ultradage";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Right";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string DecayScroll = "Scroll of Decay";
    private const string MystifyScroll = "Scroll of Mystify";
    private const string NoxiousDecayAura = "Noxious Decay";
    private const string UnleashedDoomAura = "Unleashed Doom";
    private const string FocusAura = "Focus";
    private const string DecayMessage =
        "I possess the full power of the Legion at my disposal.";
    private const int UltraQuestId = 8547;
    private const int PrerequisiteQuestId = 793;
    private const string PrerequisiteQuestName = "Fail to the King";
    private const int MinimumLevel = 80;
    private const int DageMapId = 1;
    private const int FocusRefreshMilliseconds = 1500;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;

    private readonly Queue<string> pendingZones = new();
    private readonly object zoneLock = new();
    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDage_LW";
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
            "Default: LR / SC / AP / LOO\nStable: KE / SC / AP / LOO\nReliable: VDK / SC / AP / LOO",
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
            StopAttemptSystems();
        }
    }

    public UltraRunResult RunFromMaster()
    {
        masterMode = true;
        runResult = UltraRunResult.Failed;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            StopAttemptSystems();
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
        isTaunter = armyComposition == ArmyComposition.Stable
            ? LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(4)
            : LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(3);
        ClassPreset preset = GetClassPreset();

        Core.Logger($"{LogPrefix} started as {playerAlias}.");
        LoneWolf.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

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

            if (!PrepareSafeRoom(preset, fightAttempt, out bool mystifyMode))
                return false;

            StartZoneListener();

            try
            {
                if (!StartDecayDetector(mystifyMode) || !Sync("FIGHT_READY"))
                    return false;

                StartSkillEngine(preset);
                Core.Jump(BossCell, BossPad);

                FightResult result = Fight(fightAttempt, mystifyMode);
                if (result == FightResult.Defeated)
                {
                    Core.Jump(SafeCell, SafePad);
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Dage defeated.");
                    return true;
                }

                if (result != FightResult.Reset)
                    return false;
            }
            finally
            {
                StopAttemptSystems();
            }

            if (!HandleFightReset(fightAttempt))
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
            "UltraDageComposition",
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
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );

        bool stableKingsEcho = armyComposition == ArmyComposition.Stable
            && LoneWolf.IsArmyPlayer(1);

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                stableKingsEcho ? preset.CombatPotion : null
            );

        if (armyComposition == ArmyComposition.Stable)
        {
            if (LoneWolf.IsArmyPlayer(2))
                LoneWolf.PrepareScrolls(DecayScroll);
            else if (LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(4))
                LoneWolf.PrepareScrolls(EnrageScroll);
        }
        else
        {
            if (LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(3))
                LoneWolf.PrepareScrolls(EnrageScroll);
            else if (LoneWolf.IsArmyPlayer(2))
                LoneWolf.PrepareScrolls(DecayScroll);
            else
            {
                LoneWolf.PrepareScrolls(MystifyScroll);
                LoneWolf.PrepareScrolls(DecayScroll);
            }
        }

        if (Bot.ShouldExit)
            return false;

        WarnIfHealthVampWeaponIsMissing();
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private void WarnIfHealthVampWeaponIsMissing()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                !item.Equipped
                || !string.Equals(
                    item.CategoryString,
                    "Weapon",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            if (
                item.EnhancementPatternID
                != (int)WeaponSpecial.Health_Vamp
            )
                Core.Logger(
                    "Health Vamp is not enhanced on the weapon. Ultra Dage will fail.",
                    "Prepare",
                    messageBox: true
                );

            return;
        }
    }

    private bool PrepareSafeRoom(
        ClassPreset preset,
        int fightAttempt,
        out bool mystifyMode
    )
    {
        mystifyMode = false;

        bool stableKingsEcho = armyComposition == ArmyComposition.Stable
            && LoneWolf.IsArmyPlayer(1);

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                stableKingsEcho ? preset.CombatPotion : null
            );

        if (armyComposition == ArmyComposition.Stable)
        {
            if (LoneWolf.IsArmyPlayer(2))
                LoneWolf.EquipScroll(DecayScroll);
            else if (LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(4))
                LoneWolf.EquipScroll(EnrageScroll);
        }
        else if (LoneWolf.IsArmyPlayer(1) || LoneWolf.IsArmyPlayer(3))
            LoneWolf.EquipScroll(EnrageScroll);
        else if (LoneWolf.IsArmyPlayer(2))
            LoneWolf.EquipScroll(DecayScroll);
        else
        {
            LoneWolf.EquipScroll(MystifyScroll);
            mystifyMode = Bot.Inventory.IsEquipped(MystifyScroll);

            if (!mystifyMode)
                LoneWolf.EquipScroll(DecayScroll);

            string modeSignal = mystifyMode
                ? GetMystifySignal(fightAttempt)
                : GetAlternatingDecaySignal(fightAttempt);

            if (!LoneWolf.SendArmySignal(modeSignal))
            {
                Core.Logger(
                    $"{LogPrefix} playerFour could not publish the scroll mode.",
                    "PrepareSafeRoom",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

            Core.Logger($"{LogPrefix} playerFour selected {(mystifyMode ? "Mystify" : "alternating Decay")} mode.");
        }

        if (Bot.ShouldExit || !Sync($"SCROLL_MODE_{fightAttempt}"))
            return false;

        if (
            armyComposition == ArmyComposition.Default
            && !LoneWolf.IsArmyPlayer(4)
        )
            mystifyMode = LoneWolf.HasArmySignal(
                GetMystifySignal(fightAttempt),
                4
            );

        return !Bot.ShouldExit;
    }

    private bool StartDecayDetector(bool mystifyMode)
    {
        bool detectsDecay = LoneWolf.IsArmyPlayer(2)
            || (
                armyComposition == ArmyComposition.Default
                && LoneWolf.IsArmyPlayer(4)
                && !mystifyMode
            );

        return !detectsDecay
            || LoneWolf.StartPacketDetector("ct", DecayMessage);
    }

    private FightResult Fight(int fightAttempt, bool mystifyMode)
    {
        if (isTaunter)
            return FightEnrageTaunter(fightAttempt);

        if (
            armyComposition == ArmyComposition.Stable
            && LoneWolf.IsArmyPlayer(1)
        )
            return FightDamageDealer(fightAttempt);

        return FightScrollHolder(fightAttempt, mystifyMode);
    }

    private FightResult FightDamageDealer(int fightAttempt)
    {
        int nextDecayDetection = 1;
        bool bossObserved = LoneWolf.IsMonsterAlive(DageMapId);

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out _
            );
            if (result != FightResult.Continue)
                return result;

            DrainZoneEvents(move: true);
            LoneWolf.MaintainTarget(DageMapId);
            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private FightResult FightEnrageTaunter(int fightAttempt)
    {
        bool openingOwner = armyComposition == ArmyComposition.Stable
            ? LoneWolf.IsArmyPlayer(3)
            : LoneWolf.IsArmyPlayer(1);
        int partnerPlayerNumber = armyComposition == ArmyComposition.Stable
            ? openingOwner ? 4 : 3
            : openingOwner ? 3 : 1;
        string partnerName = (
            GetSetupOption<string>($"player{partnerPlayerNumber}")
        ).Trim();
        string partnerAlias = partnerPlayerNumber == 1
            ? "playerOne"
            : partnerPlayerNumber == 3
                ? "playerThree"
                : "playerFour";
        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = openingOwner;
        bool partnerDeathObserved = false;
        bool safeHealUsedThisWindow = false;
        int nextSignalNumber = 1;
        int nextDecayDetection = 1;
        DateTimeOffset focusBaseline = GetFocusExpiry();
        bool bossObserved = LoneWolf.IsMonsterAlive(DageMapId);

        if (openingOwner)
        {
            LoneWolf.RequestTaunt(DageMapId);
            Core.Logger($"{LogPrefix} {playerAlias} requested the first Dage taunt.");
        }

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return result;

            DrainZoneEvents(move: true);
            LoneWolf.MaintainTarget(DageMapId);

            if (LoneWolf.IsArmyPlayer(3) || LoneWolf.IsArmyPlayer(4))
                HandleSafeHeal(ref safeHealUsedThisWindow);

            if (recovered)
            {
                safeHealUsedThisWindow = false;
                nextSignalNumber = AlignSignalNumber(
                    nextSignalNumber,
                    openingOwner
                );
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = GetFocusExpiry();
                LoneWolf.RequestTaunt(DageMapId);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next Dage taunt after returning.");
            }

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignal(
                    fightAttempt,
                    nextSignalNumber
                );
                if (
                    !ownsFocusCycle
                    && !waitingForOwnFocus
                    && LoneWolf.HasArmySignal(
                        expectedSignal,
                        partnerPlayerNumber
                    )
                )
                    nextSignalNumber++;

                partnerDeathObserved = true;
                ownsFocusCycle = false;
                waitingForOwnFocus = false;
                Core.Logger($"{LogPrefix} {playerAlias} detected its taunt partner died.");
            }

            if (partnerDeathObserved)
            {
                if (partnerFound && !partnerDead && partnerInBossRoom)
                {
                    partnerDeathObserved = false;
                    nextSignalNumber = AlignSignalNumber(
                        nextSignalNumber,
                        !openingOwner
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating Dage taunts.");
                }
                else
                {
                    LoneWolf.RequestImmediateTaunt(DageMapId);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            var focus = Bot.Target.GetAura(FocusAura);
            if (
                waitingForOwnFocus
                && focus != null
                && focus.ExpiresAt > focusBaseline
            )
            {
                focusBaseline = focus.ExpiresAt;
                waitingForOwnFocus = false;
                ownsFocusCycle = true;
                Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus and owns the taunt cycle.");
            }

            if (waitingForOwnFocus)
            {
                if (focus == null)
                    LoneWolf.RequestImmediateTaunt(DageMapId);
            }
            else if (ownsFocusCycle && focus == null)
            {
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = DateTimeOffset.MinValue;
                LoneWolf.RequestImmediateTaunt(DageMapId);
            }
            else if (
                ownsFocusCycle
                && focus != null
                && focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
            )
            {
                string signal = GetTauntSignal(fightAttempt, nextSignalNumber);
                if (LoneWolf.SendArmySignal(signal))
                {
                    Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                    nextSignalNumber++;
                    ownsFocusCycle = false;
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignal(fightAttempt, nextSignalNumber);
                if (LoneWolf.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = GetFocusExpiry();
                    waitingForOwnFocus = true;
                    LoneWolf.RequestTaunt(DageMapId);
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled Dage taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private FightResult FightScrollHolder(int fightAttempt, bool mystifyMode)
    {
        bool safeHealUsedThisWindow = false;
        bool packetCommandLogged = false;
        int nextDecayDetection = 1;
        bool bossObserved = LoneWolf.IsMonsterAlive(DageMapId);

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return result;

            if (recovered)
                safeHealUsedThisWindow = false;

            DrainZoneEvents(move: true);
            LoneWolf.MaintainTarget(DageMapId);

            if (LoneWolf.IsArmyPlayer(4))
            {
                HandleSafeHeal(ref safeHealUsedThisWindow);

                if (mystifyMode)
                    LoneWolf.RequestImmediateSkillFive(DageMapId);
            }

            HandleDecayDetections(
                mystifyMode,
                ref nextDecayDetection,
                ref packetCommandLogged
            );

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private void HandleDecayDetections(
        bool mystifyMode,
        ref int nextDetection,
        ref bool packetCommandLogged
    )
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
        {
            if (!packetCommandLogged)
            {
                string command = LoneWolf.GetPacketDetectorCommand();
                if (command.Length > 0)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} detected Decay in {command} packets.");
                    packetCommandLogged = true;
                }
            }

            int cycle = nextDetection++;
            bool ownsCycle = LoneWolf.IsArmyPlayer(2)
                && (
                    armyComposition == ArmyComposition.Stable
                    || mystifyMode
                    || cycle % 2 != 0
                )
                || LoneWolf.IsArmyPlayer(4)
                    && armyComposition == ArmyComposition.Default
                    && !mystifyMode
                    && cycle % 2 == 0;

            if (!ownsCycle)
                continue;

            if (!Bot.Inventory.IsEquipped(DecayScroll))
            {
                Core.Logger($"{LogPrefix} {playerAlias} consumed Decay cycle {cycle} without a cast because {DecayScroll} is not equipped.");
                continue;
            }

            LoneWolf.RequestSkillFive(DageMapId);
            Core.Logger($"{LogPrefix} {playerAlias} requested Decay for cycle {cycle}.");
        }
    }

    private void HandleSafeHeal(ref bool safeHealUsedThisWindow)
    {
        var selfAuras = Bot.Self.Auras;
        if (selfAuras == null || selfAuras.Count == 0)
            return;

        if (Bot.Self.GetAura(NoxiousDecayAura) != null)
        {
            safeHealUsedThisWindow = false;
            return;
        }

        if (
            safeHealUsedThisWindow
            || !Bot.Player.Alive
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != DageMapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(2)
        )
            return;

        Bot.Skills.UseSkill(2);
        safeHealUsedThisWindow = true;
        Core.Logger($"{LogPrefix} {playerAlias} used its safe-window heal.");
    }

    private FightResult RecoverFromDeath(
        int fightAttempt,
        ref bool bossObserved,
        ref int nextDecayDetection,
        out bool recovered
    )
    {
        recovered = false;

        if (Bot.Player.Alive)
            return FightResult.Continue;

        Core.Logger($"{LogPrefix} {playerAlias} died.");

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            DrainZoneEvents(move: false);
            AdvanceDecayDetections(ref nextDecayDetection);

            if (LoneWolf.ShouldResetFight(fightAttempt))
                return FightResult.Reset;

            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        FightResult result = GetFightResult(fightAttempt, ref bossObserved);
        if (result != FightResult.Continue)
            return result;

        Core.Logger($"{LogPrefix} {playerAlias} respawned.");

        if (!IsInBossRoom())
            Core.Jump(BossCell, BossPad);

        LoneWolf.MaintainTarget(DageMapId);
        recovered = true;
        return FightResult.Continue;
    }

    private void AdvanceDecayDetections(ref int nextDetection)
    {
        while (LoneWolf.HasPacketDetection(nextDetection))
            nextDetection++;
    }

    private FightResult GetFightResult(
        int fightAttempt,
        ref bool bossObserved
    )
    {
        if (LoneWolf.IsMonsterAlive(DageMapId))
            bossObserved = true;
        else if (bossObserved)
            return FightResult.Defeated;

        return LoneWolf.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private void StartSkillEngine(ClassPreset preset)
    {
        bool stableKingsEcho = armyComposition == ArmyComposition.Stable
            && LoneWolf.IsArmyPlayer(1);
        bool reliableVerusDoomKnight =
            armyComposition == ArmyComposition.Reliable
            && LoneWolf.IsArmyPlayer(1);

        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            useSurvivalSkill: !stableKingsEcho,
            maintainedPotion: stableKingsEcho
                && GetSetupOption<bool>("UsePotions")
                    ? preset.CombatPotion
                    : null,
            blockedStrictSkill: reliableVerusDoomKnight ? 2 : 0,
            blockedStrictSkillSelfAura: reliableVerusDoomKnight
                ? UnleashedDoomAura
                : string.Empty
        );
    }

    private void StartZoneListener()
    {
        lock (zoneLock)
            pendingZones.Clear();

        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;
    }

    private void StopZoneListener()
    {
        Bot.Events.RunToArea -= OnRunToArea;

        lock (zoneLock)
            pendingZones.Clear();
    }

    private void OnRunToArea(string zone)
    {
        if (zone != "A" && zone != "B" && zone.Length != 0)
            return;

        lock (zoneLock)
            pendingZones.Enqueue(zone);
    }

    private void DrainZoneEvents(bool move)
    {
        while (true)
        {
            string zone;
            lock (zoneLock)
            {
                if (pendingZones.Count == 0)
                    return;

                zone = pendingZones.Dequeue();
            }

            if (!move || !Bot.Player.Alive)
                continue;

            if (zone == "A")
                Bot.Player.WalkTo(122, 420);
            else if (zone == "B")
                Bot.Player.WalkTo(856, 420);
            else
                Bot.Player.WalkTo(500, 420);
        }
    }

    private void StopAttemptSystems()
    {
        LoneWolf.StopSkillEngine();
        LoneWolf.StopPacketDetector();
        StopZoneListener();
    }

    private bool HandleFightReset(int fightAttempt)
    {
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

    private T GetSetupOption<T>(string optionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Setup", optionName)
            : Bot.Config!.Get<T>(optionName))!;

    private T GetUltraOption<T>(string masterOptionName, string standaloneOptionName)
        where T : IConvertible =>
        (masterMode
            ? Bot.Config!.Get<T>("Weekly_Ultras", masterOptionName)
            : Bot.Config!.Get<T>(standaloneOptionName))!;

    private bool TryGetPartnerState(
        string partnerName,
        out bool dead,
        out bool inBossRoom
    )
    {
        dead = false;
        inBossRoom = false;

        var players = Bot.Map.Players;
        if (players == null)
            return false;

        foreach (var player in players)
        {
            if (!string.Equals(player.Name, partnerName, StringComparison.OrdinalIgnoreCase))
                continue;

            dead = player.State == 0;
            inBossRoom = !dead
                && string.Equals(
                    player.Cell,
                    BossCell,
                    StringComparison.OrdinalIgnoreCase
                );
            return true;
        }

        return false;
    }

    private DateTimeOffset GetFocusExpiry() =>
        Bot.Target.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsOpeningOwner
    )
    {
        bool signalIsOpeningOwner = signalNumber % 2 != 0;
        return signalIsOpeningOwner == senderIsOpeningOwner
            ? signalNumber
            : signalNumber + 1;
    }

    private static string GetTauntSignal(int attempt, int signalNumber) =>
        $"DAGE_TAUNT_{attempt}_{signalNumber}";

    private static string GetMystifySignal(int attempt) =>
        $"DAGE_MYSTIFY_{attempt}";

    private static string GetAlternatingDecaySignal(int attempt) =>
        $"DAGE_ALTERNATING_DECAY_{attempt}";

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private ClassPreset GetClassPreset()
    {
        ClassPreset preset;

        if (LoneWolf.IsArmyPlayer(1))
        {
            if (armyComposition == ArmyComposition.Stable)
                preset = LoneWolf.KingsEcho();
            else if (armyComposition == ArmyComposition.Reliable)
                preset = LoneWolf.VerusDoomKnight();
            else
            {
                preset = LoneWolf.LegionRevenant();
                preset.Skills = new[] { 3, 4, 2, 1 };
            }
        }
        else if (LoneWolf.IsArmyPlayer(2))
        {
            preset = LoneWolf.StoneCrusher();
            preset.Skills = new[] { 2, 4, 1 };
        }
        else if (LoneWolf.IsArmyPlayer(3))
        {
            preset = LoneWolf.ArchPaladin();
            preset.Skills = new[] { 3, 1, 4 };
        }
        else
        {
            preset = LoneWolf.LordOfOrder();
            preset.Skills = new[] { 3, 1, 4 };
        }

        preset.WeaponEnhancement = WeaponSpecial.Health_Vamp;
        preset.WeaponEnhancementFallbacks = Array.Empty<WeaponSpecial>();
        preset.CapeEnhancement = CapeSpecial.Vainglory;

        if (
            armyComposition != ArmyComposition.Stable
            || !LoneWolf.IsArmyPlayer(1)
        )
            preset.CombatPotion = null;

        return preset;
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
