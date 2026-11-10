using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace STS2Mobile;

public static partial class ModEntry
{
    [DllImport("__Internal", EntryPoint = "sts2_reinstall_crash_handlers")]
    private static extern void ReinstallNativeCrashHandlers();

    private static void InstallManagedExceptionHandlers()
    {
        if (System.Threading.Interlocked.Exchange(ref _exceptionHandlersInstalled, 1) == 1)
            return;

        if (OperatingSystem.IsIOS())
        {
            try
            {
                ReinstallNativeCrashHandlers();
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"Failed to reinstall native crash handlers: {ex.Message}");
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                BootstrapTrace.Log($"Unhandled managed exception: {args.ExceptionObject}");
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"Managed exception handler logging failed: {ex.Message}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                BootstrapTrace.Log($"Unobserved task exception: {args.Exception}");
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"Managed exception handler logging failed: {ex.Message}");
            }
        };

        BootstrapTrace.Log("Managed exception handlers installed");
    }
}
