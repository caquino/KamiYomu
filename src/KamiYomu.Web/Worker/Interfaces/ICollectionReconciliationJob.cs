using Hangfire;
using Hangfire.Server;

using System.ComponentModel;

namespace KamiYomu.Web.Worker.Interfaces;

public interface ICollectionReconciliationJob
{
    [Queue("{0}")]
    [DisplayName("Collection Reconciliation")]
    Task DispatchAsync(string queue, PerformContext context, CancellationToken cancellationToken);
}
