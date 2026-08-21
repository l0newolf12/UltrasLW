/*
name: Victor Matsuri Masakado LW
description: Four-player CoreLoneWolf prerequisite script for Victor of the Festival.
tags: victor, matsuri, masakado, army, corelonewolf
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

public class VictorMatsuriMaskado_LW
{
    public enum ArmyComposition
    {
        Default,
        Stable,
    }

    private enum FightResult
    {
        Continue,
        Defeated,
        Reset,
        Stopped,
    }

    private sealed class StoryStage
    {
        public StoryStage(
            int questId,
            int nextQuestId,
            string monsterName,
            string requirementName
        )
        {
            QuestId = questId;
            NextQuestId = nextQuestId;
            MonsterName = monsterName;
            RequirementName = requirementName;
        }

        public int QuestId { get; }
        public int NextQuestId { get; }
        public string MonsterName { get; }
        public string RequirementName { get; }
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreLoneWolf LoneWolf = new();

    private const string LogPrefix = "Victor Matsuri Masakado LW";
    private const string SyncFileName = "VictorMatsuriMaskado_LW.sync";
    private const string MapName = "victormatsuri";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r8";
    private const string BossPad = "Right";
    private const string BossName = "Masakado";
    private const string VictorItem = "Victor of the Festival";
    private const string RiteOfAscension = "Rite of Ascension";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string FocusAura = "Focus";
    private const string PraxisAura = "Praxis";
    private const string CounterAttackAura = "Counter Attack";
    private const int BossMapId = 6;
    private const int BossQuestId = 10295;
    private const int FocusRefreshMilliseconds = 1500;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;

    private static readonly StoryStage[] StoryStages =
    {
        new(10290, 10291, "Kitsune Himawari", "Sunny Fox Tail"),
        new(10291, 10292, "NeOni", "NeOni Prop Horn"),
        new(10292, 10293, "Narcis Arrhythmia", "Bloodsoaked Omamori"),
        new(10293, 10294, "Haruki Matsuoka", "Broken Oni Horn"),
        new(10294, 10295, "Lady Laidronette", "Sunrise Amulet"),
    };

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private int privateRoomNumber;
    private bool runMasakado;
    private bool isTaunter;
    private bool? reportedFightAlive;
    private bool masterMode;
    private bool runCompleted;
    private bool skipVictorWork;

    public string OptionsStorage = "VictorMatsuriMaskado_LW";
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
            "Prepare and use the assigned Masakado potion loadout.",
            true
        ),
        new Option<bool>(
            "UseEnhancements",
            "Use Enhancements",
            "Prepare the assigned enhancements. Player 1 Praxis is always required.",
            true
        ),
        new Option<bool>(
            "RunMasakado",
            "Run Masakado",
            "Fight Masakado after all four accounts finish the storyline.",
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
            LoneWolf.SetOrdinarySkillsSuppressed(false);
            LoneWolf.StopSkillEngine();
            Bot.Combat.CancelAutoAttack();
            Bot.Combat.CancelTarget();
        }
    }

    public bool RunFromMaster()
    {
        masterMode = true;
        runCompleted = false;
        skipVictorWork = false;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            LoneWolf.SetOrdinarySkillsSuppressed(false);
            LoneWolf.StopSkillEngine();
            Bot.Combat.CancelAutoAttack();
            Bot.Combat.CancelTarget();
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
        isTaunter = LoneWolf.IsArmyPlayer(2) || LoneWolf.IsArmyPlayer(4);
        ClassPreset preset = GetClassPreset();

        LoadBank();
        skipVictorWork = masterMode && OwnsRite();

        Core.Logger($"{LogPrefix} started as {playerAlias} using {armyComposition} composition.");

        LoneWolf.EquipClass(preset);
        if (
            Bot.ShouldExit
            || !ReportMissingStoryStages()
            || !Sync("STORY_STATUS_REPORTED")
        )
            return;

        if (!RunStory(preset))
            return;

        if (!runMasakado)
        {
            Core.Logger($"{LogPrefix} {playerAlias} stopped before Masakado.");
            StopArmy();
            return;
        }

        LoadBank();
        bool ownedVictorAtStart = skipVictorWork || OwnsVictor();
        if (ownedVictorAtStart)
            LoneWolf.SendArmySignal("VICTOR_ALREADY_OWNED");

        if (!Sync("VICTOR_STATUS_REPORTED"))
            return;

        if (AllPlayersSignaled("VICTOR_ALREADY_OWNED"))
        {
            Core.Logger($"{LogPrefix} every account already owns {VictorItem}.");
            runCompleted = true;
            StopArmy();
            return;
        }

        if (!PrepareBoss(preset, ownedVictorAtStart) || !Sync("SETUP_DONE"))
            return;

        if (!RunBossAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        if (!CompleteVictorQuest(ownedVictorAtStart))
            return;

        if (!Sync("VICTOR_OBTAINED"))
            return;

        runCompleted = true;
        StopArmy();
    }

    private bool ValidateOptions()
    {
        armyComposition = GetTempleOption<ArmyComposition>(
            "VictorMatsuriComposition",
            "ArmyComposition"
        );
        privateRoomNumber = GetSetupOption<int>("PrivateRoomNumber");
        runMasakado = masterMode || GetSetupOption<bool>("RunMasakado");
        return LoneWolf.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool ReportMissingStoryStages()
    {
        if (skipVictorWork)
            return true;

        foreach (StoryStage stage in StoryStages)
        {
            if (Bot.Quests.IsUnlocked(stage.NextQuestId))
                continue;

            if (!LoneWolf.SendArmySignal($"STORY_{stage.QuestId}_NEEDED"))
                return false;

            Core.Logger(
                $"{LogPrefix} {playerAlias} needs story quest {stage.QuestId}."
            );
        }

        return true;
    }

    private bool RunStory(ClassPreset preset)
    {
        Core.Join($"{MapName}-{privateRoomNumber}");
        if (Bot.ShouldExit || !IsInVictorMatsuri())
            return Failure("The Army could not enter the Victor Matsuri room.", "RunStory");

        StartStorySkillEngine(preset);

        try
        {
            foreach (StoryStage stage in StoryStages)
            {
                if (!RunStoryStage(stage))
                    return false;
            }
        }
        finally
        {
            LoneWolf.StopSkillEngine();
            Bot.Combat.CancelTarget();
        }

        return !Bot.ShouldExit;
    }

    private bool RunStoryStage(StoryStage stage)
    {
        string neededSignal = $"STORY_{stage.QuestId}_NEEDED";
        string doneSignal = $"STORY_{stage.QuestId}_COMPLETE";
        bool stageNeeded =
            !skipVictorWork && !Bot.Quests.IsUnlocked(stage.NextQuestId);
        bool stageComplete = !stageNeeded;
        bool stageReported = false;

        if (!AnyPlayerSignaled(neededSignal))
            return true;

        if (stageNeeded)
        {
            Core.EnsureAccept(stage.QuestId);
            if (
                !Bot.Wait.ForTrue(
                    () => Bot.Quests.IsInProgress(stage.QuestId),
                    20
                )
            )
                return Failure(
                    $"Quest {stage.QuestId} could not be accepted for {playerAlias}.",
                    "RunStoryStage"
                );
        }

        Core.Logger(
            $"{LogPrefix} {playerAlias} helping with {stage.MonsterName}."
        );

        while (
            !Bot.ShouldExit
            && !AllNeededPlayersCompleted(neededSignal, doneSignal)
        )
        {
            if (
                stageNeeded
                && !stageComplete
                && Bot.TempInv.Contains(stage.RequirementName, 1)
            )
            {
                if (!Core.EnsureComplete(stage.QuestId))
                    return Failure(
                        $"Quest {stage.QuestId} could not be completed for {playerAlias}.",
                        "RunStoryStage"
                    );

                if (
                    !Bot.Wait.ForTrue(
                        () => Bot.Quests.IsUnlocked(stage.NextQuestId),
                        20
                    )
                )
                    return Failure(
                        $"Quest {stage.NextQuestId} did not unlock for {playerAlias}.",
                        "RunStoryStage"
                    );

                stageComplete = true;
            }

            if (stageNeeded && stageComplete && !stageReported)
            {
                stageReported = LoneWolf.SendArmySignal(doneSignal);
                if (!stageReported)
                    return false;
            }

            if (AllNeededPlayersCompleted(neededSignal, doneSignal))
                break;

            if (!FightStoryMonsterOnce(stage.MonsterName))
                return false;
        }

        if (Bot.ShouldExit)
            return false;

        Core.Logger(
            $"{LogPrefix} {playerAlias} completed story stage {stage.QuestId}."
        );
        return true;
    }

    private bool FightStoryMonsterOnce(string monsterName)
    {
        bool seenAlive = false;

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died while helping with {monsterName}.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Core.Logger($"{LogPrefix} {playerAlias} respawned and resumed helping.");
            }

            var targetMonster = Bot.Monsters.MapMonsters?.FirstOrDefault(monster =>
                monster != null
                && string.Equals(
                    monster.Name,
                    monsterName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (targetMonster == null || string.IsNullOrWhiteSpace(targetMonster.Cell))
                return Failure(
                    $"{monsterName} could not be found in the Victor Matsuri room.",
                    "FightStoryMonsterOnce"
                );

            if (Bot.Player.Cell != targetMonster.Cell)
            {
                Core.Jump(targetMonster.Cell, "Left");

                if (Bot.ShouldExit || Bot.Player.Cell != targetMonster.Cell)
                    return Failure(
                        $"{playerAlias} could not reach {monsterName}.",
                        "FightStoryMonsterOnce"
                    );
            }

            bool monsterAlive = targetMonster.HP > 0;

            if (monsterAlive)
                seenAlive = true;
            else if (seenAlive)
            {
                Bot.Combat.CancelTarget();
                return true;
            }

            Bot.Combat.Attack(targetMonster.MapID);
            Bot.Sleep(FightPollDelay);
        }

        return false;
    }

    private void StartStorySkillEngine(ClassPreset preset)
    {
        int[] skills = LoneWolf.IsArmyPlayer(1)
            && armyComposition == ArmyComposition.Default
            ? LoneWolf.LegionRevenant().Skills
            : preset.Skills;

        LoneWolf.StartSkillEngine(
            skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );
    }

    private bool PrepareBoss(ClassPreset preset, bool ownedVictorAtStart)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting Masakado setup.");

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

        if (Bot.ShouldExit)
            return false;

        if (LoneWolf.IsArmyPlayer(1) && !IsPraxisEquipped())
            return Failure(
                "Praxis is not enhanced on the Player 1 weapon. Masakado cannot be started.",
                "PrepareBoss"
            );

        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (isTaunter)
            LoneWolf.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        if (!ownedVictorAtStart)
        {
            Core.AddDrop(VictorItem);
            Core.EnsureAccept(BossQuestId);
            if (
                !Bot.Wait.ForTrue(
                    () => Bot.Quests.IsInProgress(BossQuestId),
                    20
                )
            )
                return Failure(
                    $"Quest {BossQuestId} could not be accepted for {playerAlias}.",
                    "PrepareBoss"
                );
        }

        Core.Logger($"{LogPrefix} {playerAlias} finished Masakado setup.");
        return true;
    }

    private bool RunBossAttempts(ClassPreset preset)
    {
        int fightAttempt = 1;

        while (!Bot.ShouldExit)
        {
            Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);
            if (!PrepareBossSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            if (!Sync("START_FIGHT") || !MoveToBossRoom())
                return false;

            reportedFightAlive = null;
            FightResult result = Fight(preset, fightAttempt);
            if (result == FightResult.Defeated)
                return true;

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            fightAttempt++;
        }

        return false;
    }

    private bool PrepareBossSafeRoom(ClassPreset preset)
    {
        if (GetSetupOption<bool>("UsePotions"))
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (isTaunter)
            LoneWolf.EquipScroll(EnrageScroll);

        return !Bot.ShouldExit;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        if (isTaunter)
            return FightTaunter(preset, fightAttempt);

        return FightDamageDealer(preset, fightAttempt);
    }

    private FightResult FightDamageDealer(ClassPreset preset, int fightAttempt)
    {
        if (IsStablePlayerOne())
        {
            LoneWolf.MaintainTarget(BossMapId);
            Bot.Sleep(1000);

            if (Bot.ShouldExit)
                return FinishFight(FightResult.Stopped);
        }

        StartBossSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        bool bossSeenAlive = false;
        bool counterAttackActive = false;

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.IsMonsterAlive(BossMapId))
                bossSeenAlive = true;

            FightResult recovery = RecoverFromDeath(
                fightAttempt,
                bossSeenAlive,
                out _
            );
            if (recovery != FightResult.Continue)
                return FinishFight(recovery);

            LoneWolf.MaintainTarget(BossMapId);
            if (UpdateCounterAttack(ref counterAttackActive))
            {
                Bot.Sleep(FightPollDelay);
                continue;
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightTaunter(ClassPreset preset, int fightAttempt)
    {
        bool isFirstOwner = LoneWolf.IsArmyPlayer(2);
        int partnerPlayerNumber = isFirstOwner ? 4 : 2;
        string partnerName = (
            GetSetupOption<string>($"player{partnerPlayerNumber}")
            ?? string.Empty
        ).Trim();
        string partnerAlias = isFirstOwner ? "playerFour" : "playerTwo";

        StartBossSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        bool bossSeenAlive = false;
        bool counterAttackActive = false;
        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = isFirstOwner;
        bool partnerDeathObserved = false;
        int nextSignalNumber = 1;
        DateTimeOffset focusBaseline = GetFocusExpiry();

        if (isFirstOwner)
        {
            LoneWolf.RequestTaunt(BossMapId);
            Core.Logger($"{LogPrefix} playerTwo requested the first Masakado taunt.");
        }

        while (!Bot.ShouldExit)
        {
            if (LoneWolf.IsMonsterAlive(BossMapId))
                bossSeenAlive = true;

            FightResult recovery = RecoverFromDeath(
                fightAttempt,
                bossSeenAlive,
                out bool recovered
            );
            if (recovery != FightResult.Continue)
                return FinishFight(recovery);

            LoneWolf.MaintainTarget(BossMapId);
            UpdateCounterAttack(ref counterAttackActive);

            if (recovered)
            {
                nextSignalNumber = AlignSignalNumber(
                    nextSignalNumber,
                    isFirstOwner
                );
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = GetFocusExpiry();
                LoneWolf.RequestTaunt(BossMapId);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next taunt after returning.");
            }

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignalName(
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
                        !isFirstOwner
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating taunts.");
                }
                else
                {
                    LoneWolf.RequestImmediateTaunt(BossMapId);
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
                Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus.");
            }

            if (waitingForOwnFocus)
            {
                if (focus == null)
                    LoneWolf.RequestImmediateTaunt(BossMapId);
            }
            else if (ownsFocusCycle && focus == null)
            {
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = DateTimeOffset.MinValue;
                LoneWolf.RequestImmediateTaunt(BossMapId);
            }
            else if (
                ownsFocusCycle
                && focus != null
                && focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
            )
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (LoneWolf.SendArmySignal(signal))
                {
                    Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                    nextSignalNumber++;
                    ownsFocusCycle = false;
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (LoneWolf.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = GetFocusExpiry();
                    waitingForOwnFocus = true;
                    LoneWolf.RequestTaunt(BossMapId);
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its taunt.");
                }
            }

            if (counterAttackActive)
                Bot.Combat.CancelAutoAttack();

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private void StartBossSkillEngine(ClassPreset preset)
    {
        bool stablePlayerOne = IsStablePlayerOne();

        LoneWolf.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: !isTaunter
                && GetSetupOption<bool>("UsePotions")
                    ? preset.CombatPotion
                    : null,
            blockedStrictSkill: stablePlayerOne ? 1 : 0,
            blockedStrictSkillTargetAura: stablePlayerOne
                ? PraxisAura
                : string.Empty,
            blockedSimpleSkill: LoneWolf.IsArmyPlayer(1) && !stablePlayerOne
                ? 1
                : 0,
            blockedSimpleSkillTargetAura: LoneWolf.IsArmyPlayer(1)
                && !stablePlayerOne
                ? PraxisAura
                : string.Empty
        );
    }

    private bool UpdateCounterAttack(ref bool latched)
    {
        bool active = Bot.Player.Alive
            && Bot.Player.HasTarget
            && Bot.Player.Target?.MapID == BossMapId
            && Bot.Target.HasActiveAura(CounterAttackAura);

        LoneWolf.SetOrdinarySkillsSuppressed(active);

        if (active)
        {
            Bot.Combat.CancelAutoAttack();

            if (!latched)
                Core.Logger($"{LogPrefix} {playerAlias} paused for {CounterAttackAura}.");

            latched = true;
            return true;
        }

        if (latched)
        {
            Core.Logger($"{LogPrefix} {playerAlias} resumed after {CounterAttackAura}.");
            latched = false;
        }

        return false;
    }

    private FightResult RecoverFromDeath(
        int fightAttempt,
        bool bossSeenAlive,
        out bool recovered
    )
    {
        recovered = false;
        string deadSignal = $"MASAKADO_DEAD_{fightAttempt}";
        string aliveSignal = $"MASAKADO_ALIVE_{fightAttempt}";
        string resetSignal = $"MASAKADO_RESET_{fightAttempt}";

        if (LoneWolf.HasArmySignal(resetSignal, 1))
            return FightResult.Reset;

        if (Bot.Player.Alive)
        {
            ReportAliveIfNeeded(aliveSignal);
            return bossSeenAlive && !LoneWolf.IsMonsterAlive(BossMapId)
                ? FightResult.Defeated
                : FightResult.Continue;
        }

        LoneWolf.SetOrdinarySkillsSuppressed(false);
        Bot.Combat.CancelAutoAttack();
        Bot.Combat.CancelTarget();
        ReportDeadIfNeeded(deadSignal, fightAttempt);

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            if (
                LoneWolf.IsArmyPlayer(1)
                && !LoneWolf.HasArmySignal(resetSignal, 1)
                && AreAllPlayersDead(deadSignal, aliveSignal)
            )
            {
                if (!LoneWolf.SendArmySignal(resetSignal))
                    return FightResult.Stopped;
            }

            if (LoneWolf.HasArmySignal(resetSignal, 1))
                return FightResult.Reset;

            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        ReportAliveIfNeeded(aliveSignal);

        if (LoneWolf.HasArmySignal(resetSignal, 1))
            return FightResult.Reset;

        if (bossSeenAlive && !LoneWolf.IsMonsterAlive(BossMapId))
            return FightResult.Defeated;

        Core.Logger($"{LogPrefix} {playerAlias} respawned.");
        if (!MoveToBossRoom())
            return FightResult.Stopped;

        recovered = true;
        return FightResult.Continue;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        LoneWolf.SetOrdinarySkillsSuppressed(false);
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelAutoAttack();
        Bot.Combat.CancelTarget();

        string aliveSignal = $"MASAKADO_ALIVE_{fightAttempt}";
        while (!Bot.ShouldExit && !Bot.Player.Alive)
            Bot.Sleep(RespawnPollDelay);

        if (Bot.ShouldExit)
            return false;

        ReportAliveIfNeeded(aliveSignal);
        Core.Jump(SafeCell, SafePad);

        if (!IsInSafeRoom())
            return Failure(
                $"{playerAlias} could not reach the Masakado reset room.",
                "HandleFightReset"
            );

        Core.Logger($"{LogPrefix} {playerAlias} handling full wipe {fightAttempt}.");
        return Sync($"MASAKADO_RESET_READY_{fightAttempt}");
    }

    private FightResult FinishFight(FightResult result)
    {
        LoneWolf.SetOrdinarySkillsSuppressed(false);
        LoneWolf.StopSkillEngine();
        Bot.Combat.CancelAutoAttack();
        Bot.Combat.CancelTarget();

        if (result != FightResult.Defeated)
            return result;

        Core.Jump(SafeCell, SafePad);
        Core.Logger($"{LogPrefix} {playerAlias} confirmed {BossName} defeated.");
        return FightResult.Defeated;
    }

    private bool CompleteVictorQuest(bool ownedVictorAtStart)
    {
        if (!ownedVictorAtStart)
        {
            if (!Core.EnsureCompleteChoose(BossQuestId, new[] { VictorItem }))
                return Failure(
                    $"Quest {BossQuestId} could not be completed for {playerAlias}.",
                    "CompleteVictorQuest"
                );

            Bot.Wait.ForTrue(OwnsVictor, 20);
        }

        if (!OwnsVictor())
            return Failure(
                $"{VictorItem} was not obtained for {playerAlias}.",
                "CompleteVictorQuest"
            );

        Core.Logger($"{LogPrefix} {playerAlias} confirmed {VictorItem}.");
        return true;
    }

    private bool IsPraxisEquipped() =>
        Bot.Inventory.Items.Any(item =>
            item.Equipped
            && string.Equals(
                item.ItemGroup,
                "Weapon",
                StringComparison.OrdinalIgnoreCase
            )
            && item.ProcID == (int)WeaponSpecial.Praxis
        );

    private bool OwnsVictor() => Core.CheckInventory(VictorItem, 1, false);

    private bool OwnsRite() => Core.CheckInventory(RiteOfAscension, 1, false);

    private void LoadBank()
    {
        if (Bot.Bank.Loaded)
            return;

        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForBankLoad(20);
    }

    private bool AllPlayersSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (!LoneWolf.HasArmySignal(signal, playerNumber))
                return false;
        }

        return true;
    }

    private bool AnyPlayerSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (LoneWolf.HasArmySignal(signal, playerNumber))
                return true;
        }

        return false;
    }

    private bool AllNeededPlayersCompleted(
        string neededSignal,
        string completedSignal
    )
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            if (
                LoneWolf.HasArmySignal(neededSignal, playerNumber)
                && !LoneWolf.HasArmySignal(completedSignal, playerNumber)
            )
                return false;
        }

        return true;
    }

    private bool AreAllPlayersDead(string deadSignal, string aliveSignal)
    {
        for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
        {
            long deadAt = LoneWolf.GetArmyTimestamp(deadSignal, playerNumber);
            long aliveAt = LoneWolf.GetArmyTimestamp(aliveSignal, playerNumber);
            if (deadAt <= 0 || deadAt <= aliveAt)
                return false;
        }

        return true;
    }

    private void ReportDeadIfNeeded(string deadSignal, int fightAttempt)
    {
        if (reportedFightAlive == false)
            return;

        if (
            LoneWolf.SendArmyTimestamp(
                deadSignal,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            )
        )
        {
            reportedFightAlive = false;
            Core.Logger($"{LogPrefix} {playerAlias} died in attempt {fightAttempt}.");
        }
    }

    private void ReportAliveIfNeeded(string aliveSignal)
    {
        if (reportedFightAlive != false)
            return;

        if (
            LoneWolf.SendArmyTimestamp(
                aliveSignal,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            )
        )
            reportedFightAlive = true;
    }

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

    private bool MoveToBossRoom()
    {
        if (Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad)
            return true;

        Core.Jump(BossCell, BossPad);
        return !Bot.ShouldExit
            && Bot.Player.Cell == BossCell
            && Bot.Player.Pad == BossPad;
    }

    private DateTimeOffset GetFocusExpiry() =>
        Bot.Target.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

    private static string GetTauntSignalName(
        int fightAttempt,
        int signalNumber
    ) => $"MASAKADO_TAUNT_{fightAttempt}_{signalNumber}";

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsFirstOwner
    )
    {
        bool signalIsFirstOwner = signalNumber % 2 != 0;
        return signalIsFirstOwner == senderIsFirstOwner
            ? signalNumber
            : signalNumber + 1;
    }

    private ClassPreset GetClassPreset()
    {
        return armyComposition switch
        {
            ArmyComposition.Default => GetDefaultClassPreset(),
            ArmyComposition.Stable => GetStableClassPreset(),
            _ => GetDefaultClassPreset(),
        };
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

    private ClassPreset GetDefaultClassPreset()
    {
        if (LoneWolf.IsArmyPlayer(1))
        {
            ClassPreset preset = LoneWolf.LegionRevenant();
            preset.Skills = new[] { 3, 2, 1, 4 };
            preset.WeaponEnhancement = WeaponSpecial.Praxis;
            preset.WeaponEnhancementFallbacks = Array.Empty<WeaponSpecial>();
            return preset;
        }

        if (LoneWolf.IsArmyPlayer(2))
        {
            ClassPreset preset = LoneWolf.StoneCrusher();
            preset.CombatPotion = null;
            return preset;
        }

        if (LoneWolf.IsArmyPlayer(3))
            return LoneWolf.ArchPaladin();

        ClassPreset lordOfOrder = LoneWolf.LordOfOrder();
        lordOfOrder.CombatPotion = null;
        return lordOfOrder;
    }

    private ClassPreset GetStableClassPreset()
    {
        if (!LoneWolf.IsArmyPlayer(1))
            return GetDefaultClassPreset();

        ClassPreset preset = LoneWolf.VerusDoomKnight();
        preset.WeaponEnhancement = WeaponSpecial.Praxis;
        preset.WeaponEnhancementFallbacks = Array.Empty<WeaponSpecial>();
        return preset;
    }

    private bool IsStablePlayerOne() =>
        armyComposition == ArmyComposition.Stable
        && LoneWolf.IsArmyPlayer(1);

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

    private bool IsInVictorMatsuri() =>
        string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase);

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

    private void StopArmy()
    {
        LoneWolf.StopSkillEngine();

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

    private bool Failure(string message, string source)
    {
        Core.Logger(
            message,
            source,
            messageBox: true,
            stopBot: true
        );
        return false;
    }
}
