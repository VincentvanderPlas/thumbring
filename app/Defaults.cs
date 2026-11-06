using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thumbring;

/// <summary>Writes the first config.json from what is on this computer, so the ring works before any editing.</summary>
internal static class Defaults
{
    public static string Json()
    {
        var spaces = new JsonArray();
        foreach (var (profile, name) in ChromeProfiles().Take(2))
        {
            spaces.Add(new JsonObject
            {
                ["id"] = name.ToLowerInvariant().Replace(" ", "-"),
                ["name"] = name,
                ["chromeProfile"] = profile
            });
        }
        if (spaces.Count == 0)
            spaces.Add(new JsonObject { ["id"] = "chrome", ["name"] = "Chrome", ["chromeProfile"] = "Default" });

        var root = new JsonObject
        {
            ["spaces"] = spaces,
            ["buttons"] = new JsonArray(
                App("vscode", "VS Code", "vscode", "#0A7ACC", "Code", "vscode:"),
                new JsonObject { ["id"] = "dictate", ["name"] = "Dictate", ["kind"] = "dictate", ["icon"] = "mic", ["color"] = "#6E56CF" },
                Smart("gmail", "Gmail", "gmail", "#EA4335", "https://mail.google.com/", @"\S+@\S+\.\S+ - "),
                Smart("calendar", "Calendar", "calendar", "#1A73E8", "https://calendar.google.com/", @"(^|- )(Google )?Calendar( -|$)"),
                Smart("linkedin", "LinkedIn", "linkedin", "#0A66C2", "https://www.linkedin.com/", "LinkedIn"),
                App("slack", "Slack", "slack", "#E01E5A", "slack", "slack:"),
                App("whatsapp", "WhatsApp", "whatsapp", "#128C4B", "WhatsApp.Root", "whatsapp:"),
                new JsonObject
                {
                    ["id"] = "more", ["name"] = "More", ["kind"] = "folder", ["icon"] = "dots",
                    ["children"] = new JsonArray(
                        App("notion", "Notion", "notion", "#1D1D1F", "Notion", "notion:"),
                        new JsonObject { ["id"] = "hubspot", ["name"] = "HubSpot", ["icon"] = "hubspot", ["color"] = "#FF7A59", ["url"] = "https://app.hubspot.com/" })
                })
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static JsonObject App(string id, string name, string icon, string color, string process, string launch) => new()
    {
        ["id"] = id, ["name"] = name, ["kind"] = "app", ["icon"] = icon, ["color"] = color,
        ["process"] = process, ["launch"] = launch
    };

    private static JsonObject Smart(string id, string name, string icon, string color, string url, string match) => new()
    {
        ["id"] = id, ["name"] = name, ["kind"] = "smart", ["icon"] = icon, ["color"] = color,
        ["url"] = url, ["titleMatch"] = match
    };

    /// <summary>Signed-in profiles first, most recently used first.</summary>
    private static IEnumerable<(string profile, string name)> ChromeProfiles()
    {
        var found = new List<(string profile, string name, bool signedIn, double active)>();
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Local State");
            var cache = JsonNode.Parse(File.ReadAllText(path))!["profile"]!["info_cache"]!.AsObject();
            foreach (var (dir, node) in cache)
            {
                var info = node!.AsObject();
                found.Add((dir,
                    info["name"]?.GetValue<string>() ?? dir,
                    !string.IsNullOrEmpty(info["user_name"]?.GetValue<string>()),
                    info["active_time"]?.GetValue<double>() ?? 0));
            }
        }
        catch { }
        return found.OrderByDescending(f => f.signedIn).ThenByDescending(f => f.active).Select(f => (f.profile, f.name));
    }
}
