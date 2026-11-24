using Microsoft.UI.Xaml;

namespace LogOrganizer.WinUI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Runs before WinUI loads its native dependencies, so a missing runtime gets a readable message.
        if (!PrerequisiteCheck.TryVerify(out var message))
        {
            NativeMethods.ShowMessageBox(IntPtr.Zero, message!, "日志归档助手", 0x10);
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}