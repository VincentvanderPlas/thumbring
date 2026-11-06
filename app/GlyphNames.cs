using System;
using System.Collections.Generic;

namespace Thumbring;

internal static partial class Glyphs
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail"] = Gmail, ["calendar"] = Calendar, ["linkedin"] = LinkedIn, ["hubspot"] = HubSpot,
        ["slack"] = Slack, ["whatsapp"] = WhatsApp, ["notion"] = Notion, ["vscode"] = VsCode,
        ["mic"] = Mic, ["dots"] = Dots, ["bars"] = Bars,
    };

    /// <summary>"icon" in config.json: a built-in name, or raw SVG path data for your own logo.</summary>
    public static string? Resolve(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        if (Names.TryGetValue(icon, out var known)) return known;
        return icon.StartsWith("F1 ") ? icon : "F1 " + icon;
    }
}
