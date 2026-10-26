using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IgnitionSwitch.App.Audio;

internal sealed partial class AudioThreadRegistration
{
    private nint handle;
    private int threadId;
    private bool attempted;

    public void Enter()
    {
        if (attempted)
        {
            return;
        }

        attempted = true;
        threadId = Environment.CurrentManagedThreadId;
        uint taskIndex = 0;
        handle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);

        if (handle == 0)
        {
            Trace.TraceWarning(
                "Audio MMCSS registration failed: {0}",
                Marshal.GetLastPInvokeError()
            );
        }
    }

    public void Leave()
    {
        if (handle == 0)
        {
            return;
        }

        if (threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "Audio scheduling must be released on its registered render thread."
            );
        }

        if (!AvRevertMmThreadCharacteristics(handle))
        {
            Trace.TraceWarning("Audio MMCSS release failed: {0}", Marshal.GetLastPInvokeError());
        }

        handle = 0;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "avrt.dll",
        EntryPoint = "AvSetMmThreadCharacteristicsW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true
    )]
    private static partial nint AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AvRevertMmThreadCharacteristics(nint handle);
}
