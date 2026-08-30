/*
name: Reset Army Compositions
description: Resets saved Army composition options to Default.
tags: utility, options, army, compositions
*/

//cs_include Scripts/CoreBots.cs
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.IO;

#nullable enable

public class ResetArmyCompositions
{
    private CoreBots Core => CoreBots.Instance;
    private IScriptInterface Bot => IScriptInterface.Instance;

    private const string LogPrefix = "Reset Army Compositions";

    public void ScriptMain(IScriptInterface Bot)
    {
        ResetCompositions();
    }

    private void ResetCompositions()
    {
        string optionsDirectory = ClientFileSources.SkuaOptionsDIR;
        if (
            string.IsNullOrWhiteSpace(optionsDirectory)
            || !Directory.Exists(optionsDirectory)
        )
        {
            Report("The Skua options directory was not found. No files were changed.");
            return;
        }

        string[] optionFiles;
        try
        {
            optionFiles = Directory.GetFiles(
                optionsDirectory,
                "*.cfg",
                SearchOption.TopDirectoryOnly
            );
        }
        catch
        {
            Report("The Skua options directory could not be read. No files were changed.");
            return;
        }

        if (optionFiles.Length == 0)
        {
            Report("No Skua option files were found. No files were changed.");
            return;
        }

        int selectionsChanged = 0;
        int filesChanged = 0;
        int filesFailed = 0;

        foreach (string optionFile in optionFiles)
        {
            try
            {
                string[] lines = File.ReadAllLines(optionFile);
                int fileSelectionsChanged = 0;

                for (int index = 0; index < lines.Length; index++)
                {
                    int equalsIndex = lines[index].IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string optionKey = lines[index].Substring(0, equalsIndex);
                    int categoryIndex = optionKey.LastIndexOf(':');
                    string optionName = categoryIndex >= 0
                        ? optionKey.Substring(categoryIndex + 1)
                        : optionKey;

                    if (
                        !optionName.Equals(
                            "ArmyComposition",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !optionName.EndsWith(
                            "Composition",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;

                    string currentValue = lines[index].Substring(equalsIndex + 1);
                    if (currentValue == "Default")
                        continue;

                    lines[index] = lines[index].Substring(0, equalsIndex + 1) + "Default";
                    fileSelectionsChanged++;
                }

                if (fileSelectionsChanged == 0)
                    continue;

                File.WriteAllLines(optionFile, lines);
                selectionsChanged += fileSelectionsChanged;
                filesChanged++;
            }
            catch
            {
                filesFailed++;
                Core.Logger(
                    $"Failed to reset compositions in {Path.GetFileName(optionFile)}.",
                    LogPrefix
                );
            }
        }

        Report(
            $"Reset {selectionsChanged} composition selections in {filesChanged} files. Failed files: {filesFailed}."
        );
    }

    private void Report(string message)
    {
        Core.Logger(message, LogPrefix);
        if (!Core.ForceOffMessageboxes)
            Bot.ShowMessageBox(message, LogPrefix);
    }
}
