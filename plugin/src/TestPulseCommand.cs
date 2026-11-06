namespace Loupedeck.ThumbringPlugin
{
    using System;

    // One visible action so the plugin shows up under Installed Plugins in Options+
    // and the vibration path can be checked by hand from there.
    public class TestPulseCommand : PluginDynamicCommand
    {
        public TestPulseCommand()
            : base(displayName: "Thumbring test pulse", description: "Plays the finished pulse", groupName: "Thumbring")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            ((ThumbringPlugin)this.Plugin).Play("completed");
        }
    }
}
