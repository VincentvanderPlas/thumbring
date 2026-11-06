namespace Loupedeck.ThumbringPlugin
{
    using System;

    // The service expects one ClientApplication in every plugin, also a universal one.
    public class ThumbringApplication : ClientApplication
    {
        public ThumbringApplication()
        {
        }

        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
