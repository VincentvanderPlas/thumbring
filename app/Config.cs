using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Thumbring;

internal enum Kind { Smart, Page, App, Dictate, Folder }

/// <summary>One Chrome profile with a name, such as Work or Personal.</summary>
internal sealed class Space
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ChromeProfile { get; init; }
    public required string WindowId { get; init; }
    public string Letter { get; init; } = "?";
    public string? Glyph { get; init; }
    public Color Color { get; init; } = Config.Ink;

    /// <summary>Chrome stamps each window: "Default" is "Chrome", "Profile 7" is "Chrome.UserData.Profile7".</summary>
    public static string WindowIdFor(string profile) =>
        profile == "Default" ? "Chrome" : "Chrome.UserData." + profile.Replace(" ", "");
}

internal sealed class Target
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Kind Kind { get; init; }
    public string? Short { get; init; }          // Name in the label when it differs
    public string? Glyph { get; init; }
    public string? Letter { get; init; }
    public string? IconExe { get; init; }
    public Color Tint { get; init; } = Config.Ink;
    public string? Url { get; init; }            // Smart and Page
    public Space? Fixed { get; init; }           // Page: always this Chrome space
    public Space? Forced { get; init; }          // A choice under a smart button
    public Regex? OnPage { get; init; }          // Smart: does a window title mean we are on this page
    public Space? Prefer { get; init; }          // Smart: always start in this space
    public string? Process { get; init; }        // App
    public string? Launch { get; init; }         // App: a path, a URI such as slack: or shell:AppsFolder\id
    public int[]? Keys { get; init; }            // Dictate: fixed keys instead of Wispr Flow
    public List<Target>? Children { get; init; } // Folder
}

/// <summary>Everything personal lives in config.json. The code holds no accounts, URLs or logos of anyone.</summary>
internal sealed class Config
{
    public static readonly Color Ink = Color.FromRgb(0x1D, 0x1D, 0x1F);

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thumbring");
    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    public static Config Current { get; private set; } = new();

    public List<Space> Spaces { get; private set; } = new();
    public List<Target> Buttons { get; private set; } = new();
    public Space Start { get; private set; } = null!;

    public Space SpaceOf(Target t) => t.Forced ?? t.Fixed ?? Start;

    public Space? ByWindowId(string? id) => Spaces.FirstOrDefault(s => s.WindowId == id);

    public Space Next(Space s) => Spaces[(Spaces.IndexOf(s) + 1) % Spaces.Count];

    /// <summary>The choices that fan out of a smart button on hover, one per space.</summary>
    public List<Target> Choices(Target t) => Spaces.Select(s => new Target
    {
        Id = t.Id + ":" + s.Id, Name = t.Name + " " + s.Name, Short = t.Name, Kind = Kind.Page,
        Glyph = s.Glyph, Letter = s.Letter, Tint = s.Color, Url = t.Url, Forced = s
    }).ToList();

    public Target? Find(string id)
    {
        foreach (var t in Buttons)
        {
            if (t.Id == id) return t;
            if (t.Children != null)
                foreach (var c in t.Children)
                    if (c.Id == id) return c;
        }
        return null;
    }

    // ---- loading ----------------------------------------------------------------------------

    public static void Load()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Defaults.Json());
        Current = Parse(File.ReadAllText(FilePath));
    }

    private static Config Parse(string json)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var root = JsonNode.Parse(json, null, options)!.AsObject();
        var config = new Config();

        foreach (var node in root["spaces"]!.AsArray())
        {
            var o = node!.AsObject();
            string name = Str(o, "name") ?? "Space";
            string profile = Str(o, "chromeProfile") ?? "Default";
            config.Spaces.Add(new Space
            {
                Id = Str(o, "id") ?? name.ToLowerInvariant(),
                Name = name,
                ChromeProfile = profile,
                WindowId = Str(o, "chromeWindowId") ?? Space.WindowIdFor(profile),
                Letter = Str(o, "letter") ?? name[..1].ToUpperInvariant(),
                Glyph = Glyphs.Resolve(Str(o, "icon")),
                Color = Hex(Str(o, "color")) ?? Ink
            });
        }
        if (config.Spaces.Count == 0) throw new InvalidDataException("config.json needs at least one space");

        config.Start = config.SpaceById(Str(root, "startSpace")) ?? config.Spaces[0];
        foreach (var node in root["buttons"]!.AsArray())
            config.Buttons.Add(config.Button(node!.AsObject()));
        return config;
    }

    private Space? SpaceById(string? id) => id == null ? null : Spaces.FirstOrDefault(s => s.Id == id);

    private Target Button(JsonObject o)
    {
        string kind = Str(o, "kind") ?? "page";
        var children = o["children"]?.AsArray().Select(n => Button(n!.AsObject())).ToList();
        string? match = Str(o, "titleMatch");
        return new Target
        {
            Id = Str(o, "id") ?? Guid.NewGuid().ToString("n"),
            Name = Str(o, "name") ?? "Button",
            Kind = kind switch
            {
                "smart" => Kind.Smart, "app" => Kind.App, "dictate" => Kind.Dictate,
                "folder" => Kind.Folder, _ => Kind.Page
            },
            Glyph = Glyphs.Resolve(Str(o, "icon")),
            Letter = Str(o, "letter") ?? Str(o, "name")?[..1],
            IconExe = Expand(Str(o, "iconExe")),
            Tint = Hex(Str(o, "color")) ?? Ink,
            Url = Str(o, "url"),
            Fixed = SpaceById(Str(o, "space")),
            OnPage = match == null ? null : new Regex(match, RegexOptions.IgnoreCase),
            Prefer = SpaceById(Str(o, "preferSpace")),
            Process = Str(o, "process"),
            Launch = Expand(Str(o, "launch")),
            Keys = o["keys"]?.AsArray().Select(n => n!.GetValue<int>()).ToArray(),
            Children = children
        };
    }

    private static string? Str(JsonObject o, string key) => o[key]?.GetValue<string>();

    private static string? Expand(string? s) => s == null ? null : Environment.ExpandEnvironmentVariables(s);

    private static Color? Hex(string? s) =>
        s == null ? null : (Color)ColorConverter.ConvertFromString(s);
}

/// <summary>The personal settings the tray menu can change.</summary>
internal sealed class Prefs
{
    public string Finished { get; set; } = "completed";
    public string Permission { get; set; } = "knock";
    public string Hover { get; set; } = "subtle_collision";
    public string Click { get; set; } = "sharp_state_change";
    public bool BuzzWhileInVsCode { get; set; }

    private static readonly string PrefsPath = Path.Combine(Config.Folder, "prefs.json");

    public static Prefs Load()
    {
        try { return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(PrefsPath)) ?? new Prefs(); }
        catch { return new Prefs(); }
    }

    public void Save()
    {
        try { File.WriteAllText(PrefsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
