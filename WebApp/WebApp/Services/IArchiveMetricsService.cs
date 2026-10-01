using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IArchiveMetricsService
{
    /// <param name="canEnterFolder">When set, only files in directories it accepts are counted (a member's readable content).</param>
    DashboardArchiveDto GetArchiveMetrics(Func<string, bool>? canEnterFolder = null);
}
