namespace Loupedeck.ThumbringPlugin
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Threading;
    using System.Threading.Tasks;

    // Thin on purpose. The Thumbring overlay owns the interface and the logic. This plugin only
    // exists because the Logi Plugin Service is the one route to the mouse's vibration motor.
    // It listens on a named pipe for waveform names and raises the matching haptic event.
    public class ThumbringPlugin : Plugin
    {
        public const String PipeName = "thumbring-haptics";

        // Every waveform in Logitech's list. Each one maps to itself in eventMapping.yaml.
        public static readonly String[] Waveforms =
        {
            "sharp_state_change", "damp_state_change",
            "sharp_collision", "damp_collision", "subtle_collision",
            "happy_alert", "angry_alert", "completed",
            "square", "wave", "firework", "mad", "knock", "jingle", "ringing"
        };

        private CancellationTokenSource _stop;

        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public ThumbringPlugin()
        {
        }

        public override void Load()
        {
            foreach (var waveform in Waveforms)
            {
                this.PluginEvents.AddEvent(EventName(waveform), waveform, $"Plays the {waveform} waveform");
            }

            this._stop = new CancellationTokenSource();
            _ = Task.Run(() => this.ListenAsync(this._stop.Token));
            _ = Task.Run(() => this.ListenAsync(this._stop.Token));
            this.Log.Info("Thumbring haptics pipe is up");
        }

        public override void Unload()
        {
            this._stop?.Cancel();
        }

        public void Play(String waveform)
        {
            if (Array.IndexOf(Waveforms, waveform) >= 0)
            {
                this.PluginEvents.RaiseEvent(EventName(waveform));
            }
        }

        private static String EventName(String waveform) => "wf_" + waveform;

        private async Task ListenAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(stop);

                    using var reader = new StreamReader(pipe);
                    String line;
                    while ((line = await reader.ReadLineAsync(stop)) != null)
                    {
                        this.Play(line.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    this.Log.Warning(ex, "Haptics pipe dropped, listening again");
                    await Task.Delay(250, CancellationToken.None);
                }
            }
        }
    }
}
