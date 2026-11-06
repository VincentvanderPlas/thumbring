using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Thumbring;

/// <summary>The line to the mouse: a held-open pipe to the Logi plugin, so a hover pulse has no connect cost.</summary>
internal sealed class Haptics
{
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly object _gate = new();
    private DateTime _nextTry = DateTime.MinValue;

    public void Play(string waveform)
    {
        Task.Run(() =>
        {
            lock (_gate)
            {
                try
                {
                    if (_pipe is not { IsConnected: true })
                    {
                        if (DateTime.UtcNow < _nextTry) return;
                        _nextTry = DateTime.UtcNow.AddSeconds(3);
                        _pipe?.Dispose();
                        _pipe = new NamedPipeClientStream(".", "thumbring-haptics", PipeDirection.Out);
                        _pipe.Connect(120);
                        _writer = new StreamWriter(_pipe) { AutoFlush = true };
                    }
                    _writer!.WriteLine(waveform);
                }
                catch
                {
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                }
            }
        });
    }
}

/// <summary>Claude Code chats that finished or need you while you were elsewhere.</summary>
internal sealed class Chats
{
    private readonly Dictionary<string, (bool needsYou, DateTime at)> _open = new();
    private DateTime _lastBuzz = DateTime.MinValue;
    private readonly Haptics _haptics;
    private readonly Prefs _prefs;
    private readonly Func<bool> _vsCodeInFront;

    public event Action? Changed;

    public Chats(Haptics haptics, Prefs prefs, Func<bool> vsCodeInFront)
    {
        _haptics = haptics;
        _prefs = prefs;
        _vsCodeInFront = vsCodeInFront;
    }

    public int Count { get { Expire(); return _open.Count; } }
    public int NeedYou { get { Expire(); return _open.Values.Count(v => v.needsYou); } }

    public void On(string ev, string session)
    {
        bool front = _vsCodeInFront();
        Log.Line($"chat {ev}, VS Code in front: {front}");
        switch (ev)
        {
            case "stop":
                if (!front) _open[session] = (false, DateTime.UtcNow);
                else _open.Remove(session);
                if (!front || _prefs.BuzzWhileInVsCode) Buzz(_prefs.Finished);
                break;
            case "permission":
                _open[session] = (true, DateTime.UtcNow);
                if (!front || _prefs.BuzzWhileInVsCode) Buzz(_prefs.Permission);
                break;
            case "prompt":
            case "end":
                _open.Remove(session);
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Back in VS Code you can see what finished. Permission requests stay until answered.</summary>
    public void SeenInVsCode()
    {
        var done = _open.Where(p => !p.Value.needsYou).Select(p => p.Key).ToList();
        if (done.Count == 0) return;
        foreach (var k in done) _open.Remove(k);
        Changed?.Invoke();
    }

    private void Buzz(string waveform)
    {
        // Three chats finishing in one second should feel like one event.
        if (DateTime.UtcNow - _lastBuzz < TimeSpan.FromSeconds(1.5)) return;
        _lastBuzz = DateTime.UtcNow;
        _haptics.Play(waveform);
    }

    private void Expire()
    {
        foreach (var k in _open.Where(p => DateTime.UtcNow - p.Value.at > TimeSpan.FromHours(8)).Select(p => p.Key).ToList())
            _open.Remove(k);
    }
}

/// <summary>Inbound pipe. Hooks and second launches of Thumbring.exe write one JSON line each.</summary>
internal static class Bus
{
    public const string PipeName = "thumbring";

    public static void Listen(Dispatcher ui, Action<JsonElement> handle)
    {
        for (int i = 0; i < 2; i++)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await pipe.WaitForConnectionAsync();
                        using var reader = new StreamReader(pipe);
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var doc = JsonDocument.Parse(line);
                        _ = ui.BeginInvoke(() => handle(doc.RootElement));
                    }
                    catch
                    {
                        await Task.Delay(200);
                    }
                }
            });
        }
    }

    public static bool Send(string json)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(400);
            using var w = new StreamWriter(pipe) { AutoFlush = true };
            w.WriteLine(json);
            return true;
        }
        catch { return false; }
    }
}
