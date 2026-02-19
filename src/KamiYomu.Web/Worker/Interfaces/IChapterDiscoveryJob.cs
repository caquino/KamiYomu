using Hangfire;
using Hangfire.Server;

using System.ComponentModel;

namespace KamiYomu.Web.Worker.Interfaces;

public interface IChapterDiscoveryJob
{
    [Queue("{0}")]
    [DisplayName("Discovery New Chapter")]
    Task DispatchAsync(string queue, Guid crawlerId, Guid libraryId, PerformContext context, CancellationToken cancellationToken);
}
