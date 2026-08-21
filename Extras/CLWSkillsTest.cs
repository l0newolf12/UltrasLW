/*
name: CLW Skills Test
description: Tests CoreLoneWolf class presets and skill-engine modes in isolation.
tags: prototype, corelonewolf, skills, test
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/UltrasLW/CoreLoneWolf.cs
using System.Collections.Generic;
using System.Diagnostics;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class CLWSkillsTest
{
    private enum SkillPresetChoice
    {
        Legion_Revenant,
        LightCaster,
        ArchFiend,
        Void_Highlord,
        Hollowborn_Vindicator,
        Verus_DoomKnight,
        Dragon_of_Time,
        Bard,
        Kings_Echo,
        Arcana_Invoker,
        Chrono_ShadowHunter,
        Chaos_Slayer,
        Shaman,
        StoneCrusher,
        ArchPaladin,
        Lord_Of_Order,
        Oracle,
        Guardian,
        Chaos_Avenger,
        Scion_of_Flames,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private CoreLoneWolf LoneWolf { get; } = new();

    private const string LogPrefix = "CLW Skills Test";
    private const int MonsterMapId = 1;
    private const int TestPollDelay = 150;

    public string OptionsStorage = "CLWSkillsTest";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<SkillPresetChoice>(
            "SelectedPreset",
            "Class Preset",
            "Select the CoreLoneWolf class preset to test.",
            SkillPresetChoice.Legion_Revenant
        ),
        new Option<bool>(
            "UseEnhancements",
            "Use Enhancements",
            "Prepare the selected class preset enhancement loadout.",
            true
        ),
        new Option<bool>(
            "UsePotions",
            "Use Potions",
            "Prepare and use the selected class preset potion loadout.",
            true
        ),
        new Option<bool>(
            "UseLightCasterHealingMode",
            "Use LightCaster Healing Mode",
            "Prioritize LightCaster skill 3 and rotate skills 2, 1, and 4 otherwise.",
            false
        ),
        new Option<bool>(
            "UseKingsEchoSkill3",
            "Use Kings Echo Skill 3",
            "Enable the automatic low-HP survival and taunt skill for King's Echo.",
            true
        ),
        new Option<bool>(
            "UseShamanFarmMode",
            "Use Shaman Farm Mode",
            "Use only Shaman skills 1 and 2.",
            false
        ),
        new Option<bool>(
            "UseChronoShadowHunterGunslingerMode",
            "Use Chrono ShadowHunter Gunslinger Mode",
            "Use the Gunslinger Stance and skill 0 cycle.",
            false
        ),
        new Option<bool>(
            "UseChaosAvengerOptimizedMode",
            "Use Chaos Avenger Optimized Mode",
            "Wait for Branded to be consumed before using the next array skill.",
            false
        ),
        new Option<int>(
            "TestDurationSeconds",
            "Test Duration Seconds",
            "Number of seconds to run the selected skill engine.",
            60
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
            LoneWolf.StopSkillEngine();
        }
    }

    private void Run()
    {
        int durationSeconds = Bot.Config!.Get<int>("TestDurationSeconds");
        if (durationSeconds <= 0)
        {
            Core.Logger(
                "Test duration must be greater than 0 seconds.",
                LogPrefix,
                stopBot: true
            );
            return;
        }

        SkillPresetChoice selectedPreset = Bot.Config.Get<SkillPresetChoice>("SelectedPreset");
        bool useEnhancements = Bot.Config.Get<bool>("UseEnhancements");
        bool usePotions = Bot.Config.Get<bool>("UsePotions");
        bool useLightCasterHealingMode = Bot.Config.Get<bool>(
            "UseLightCasterHealingMode"
        );
        bool useKingsEchoSkill3 = Bot.Config.Get<bool>("UseKingsEchoSkill3");
        bool useShamanFarmMode = Bot.Config.Get<bool>("UseShamanFarmMode");
        bool useChronoShadowHunterGunslingerMode = Bot.Config.Get<bool>(
            "UseChronoShadowHunterGunslingerMode"
        );
        bool useChaosAvengerOptimizedMode = Bot.Config.Get<bool>(
            "UseChaosAvengerOptimizedMode"
        );
        ClassPreset preset = GetClassPreset(
            selectedPreset,
            useLightCasterHealingMode,
            useShamanFarmMode,
            useChronoShadowHunterGunslingerMode,
            useChaosAvengerOptimizedMode
        );

        Core.Join("classhall");
        LoneWolf.EquipClass(preset);

        if (Bot.ShouldExit)
            return;

        if (useEnhancements)
            LoneWolf.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement
            );

        if (usePotions)
            LoneWolf.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (Bot.ShouldExit)
            return;

        Core.Join("classhall");

        if (usePotions)
            LoneWolf.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (Bot.ShouldExit)
            return;

        Core.Jump("r4", "Left");
        LoneWolf.MaintainTarget(MonsterMapId);
        LoneWolf.StartSkillEngine(
            preset.Skills,
            "tester",
            false,
            LogPrefix,
            preset.SkillMode,
            useKingsEchoSkill3
        );

        Core.Logger(
            $"{LogPrefix} started {preset.ClassName} with {preset.SkillMode} mode for {durationSeconds} seconds."
        );

        Stopwatch timer = Stopwatch.StartNew();

        while (!Bot.ShouldExit && timer.Elapsed.TotalSeconds < durationSeconds)
        {
            LoneWolf.MaintainTarget(MonsterMapId);
            Bot.Sleep(TestPollDelay);
        }

        LoneWolf.StopSkillEngine();

        if (!Bot.ShouldExit)
            Core.Logger($"{LogPrefix} completed {preset.ClassName} skill test.");
    }

    private ClassPreset GetClassPreset(
        SkillPresetChoice selectedPreset,
        bool useLightCasterHealingMode,
        bool useShamanFarmMode,
        bool useChronoShadowHunterGunslingerMode,
        bool useChaosAvengerOptimizedMode
    ) =>
        selectedPreset switch
        {
            SkillPresetChoice.LightCaster => LoneWolf.LightCaster(
                useLightCasterHealingMode
            ),
            SkillPresetChoice.ArchFiend => LoneWolf.ArchFiend(),
            SkillPresetChoice.Void_Highlord => LoneWolf.VoidHighlord(),
            SkillPresetChoice.Hollowborn_Vindicator =>
                LoneWolf.HollowbornVindicator(),
            SkillPresetChoice.Verus_DoomKnight => LoneWolf.VerusDoomKnight(),
            SkillPresetChoice.Dragon_of_Time => LoneWolf.DragonOfTime(),
            SkillPresetChoice.Bard => LoneWolf.Bard(),
            SkillPresetChoice.Kings_Echo => LoneWolf.KingsEcho(),
            SkillPresetChoice.Arcana_Invoker => LoneWolf.ArcanaInvoker(),
            SkillPresetChoice.Chrono_ShadowHunter => LoneWolf.ChronoShadowHunter(
                useChronoShadowHunterGunslingerMode
            ),
            SkillPresetChoice.Chaos_Slayer => LoneWolf.ChaosSlayer(),
            SkillPresetChoice.Shaman => LoneWolf.Shaman(useShamanFarmMode),
            SkillPresetChoice.StoneCrusher => LoneWolf.StoneCrusher(),
            SkillPresetChoice.ArchPaladin => LoneWolf.ArchPaladin(),
            SkillPresetChoice.Lord_Of_Order => LoneWolf.LordOfOrder(),
            SkillPresetChoice.Oracle => LoneWolf.Oracle(),
            SkillPresetChoice.Guardian => LoneWolf.Guardian(),
            SkillPresetChoice.Chaos_Avenger => LoneWolf.ChaosAvenger(
                useChaosAvengerOptimizedMode
            ),
            SkillPresetChoice.Scion_of_Flames => LoneWolf.ScionOfFlames(),
            _ => LoneWolf.LegionRevenant(),
        };
}
