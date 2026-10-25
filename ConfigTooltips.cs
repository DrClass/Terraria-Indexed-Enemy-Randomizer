using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Word-wraps this mod's configuration tooltips to fit the available UI width.
/// </summary>
public sealed class ConfigTooltips : ModSystem
{
    /// <summary>
    /// Locates tModLoader's internal config draw method, which is not exposed through the public config API.
    /// </summary>
    internal static MethodInfo DrawMethod() => typeof(Mod).Assembly
        .GetType("Terraria.ModLoader.Config.UI.UIModConfig", true)!
        .GetMethod("Draw", BindingFlags.Public | BindingFlags.Instance)!;

    /// <summary>
    /// Installs the tooltip patch only when a graphical UI is available.
    /// </summary>
    public override void Load()
    {
        if (!Main.dedServ)
            MonoModHooks.Modify(DrawMethod(), Patch);
    }

    /// <summary>
    /// Transforms the tooltip string on the IL stack immediately before the shared tooltip renderer consumes it.
    /// </summary>
    internal static void Patch(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Call
            && i.Operand is MethodReference m && m.Name == "TooltipMouseText"))
            throw new InvalidOperationException("Cannot locate config tooltip renderer.");
        cursor.EmitDelegate<Func<string, string>>(WrapOwnTooltip);
    }

    /// <summary>
    /// Matches this config's localized boolean tooltips, then measures and wraps them for the current UI scale.
    /// </summary>
    private static string WrapOwnTooltip(string text)
    {
        // The renderer is shared by every mod, so match our own localized tooltip text before changing it.
        bool ours = typeof(RandomizerConfig).GetProperties().Where(p => p.PropertyType == typeof(bool))
            .Any(p => text == Language.GetTextValue("Mods.IndexedEnemyRandomizer.Configs.RandomizerConfig." + p.Name + ".Tooltip"));
        if (!ours)
            return text;
        // Measure in UI-scaled coordinates, leaving a screen margin and capping overly wide tooltips.
        float width = Math.Max(100, Math.Min(480, Main.screenWidth / Main.UIScale - 40));
        return Wrap(text, width, value => FontAssets.MouseText.Value.MeasureString(value).X);
    }

    /// <summary>
    /// Wraps at word boundaries using a supplied text-width measurement, preserving explicit paragraph breaks.
    /// An individual overlong word is retained intact rather than split mid-word.
    /// </summary>
    /// <param name="text">Tooltip text, possibly containing explicit line breaks.</param>
    /// <param name="width">Maximum line width in the same units returned by measure.</param>
    /// <param name="measure">Width measurement callback; the live UI uses its font, while tests can use character counts.</param>
    /// <returns>Text with added newline breaks between words.</returns>
    internal static string Wrap(string text, float width, Func<string, float> measure)
    {
        var lines = new List<string>();
        // Wrap each explicit line independently; an empty paragraph still contributes an empty output line.
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // Measure the proposed line with its next word before committing it.
                string candidate = line.Length == 0 ? word : line + " " + word;
                // Start a fresh line at a word boundary; keep a single long word intact rather than creating an empty line.
                if (line.Length > 0 && measure(candidate) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                    line = candidate;
            }
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}
