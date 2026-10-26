using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession
{
    public event EventHandler<CaptureResult>? CaptureCompleted;

    public bool CanCaptureRecentPerformance
    {
        get
        {
            lock (inputLock)
            {
                return !disposed && !interactionBlocked && Engine.HasRecentPerformance;
            }
        }
    }

    public CaptureResult? CaptureRecentPerformance()
    {
        CaptureResult result;

        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return null;
            }

            result = CaptureRecentPerformanceCore();
        }

        RefreshFeedback();
        return result;
    }

    private CaptureResult CaptureRecentPerformanceCore()
    {
        CaptureResult result = Engine.CaptureRecentPerformance();

        if (result.Status == CaptureStatus.Captured)
        {
            revision++;
        }

        CaptureCompleted?.Invoke(this, result);
        return result;
    }
}
