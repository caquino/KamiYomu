using Hangfire;
using Hangfire.Server;

using KamiYomu.Web.AppOptions;
using KamiYomu.Web.Entities;
using KamiYomu.Web.Infrastructure.Contexts;
using KamiYomu.Web.Worker.Interfaces;

using Microsoft.Extensions.Options;

namespace KamiYomu.Web.Worker;

public class CollectionReconciliationJob(
    ILogger<CollectionReconciliationJob> logger,
    IOptions<WorkerOptions> workerOptions,
    DbContext dbContext) : ICollectionReconciliationJob
{
    public Task DispatchAsync(string queue, PerformContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Dispatch \"{title}\".", nameof(CollectionReconciliationJob));

        IEnumerable<Library> libraries = dbContext.Libraries.FindAll();
        string mangaDiscoveryQueue = workerOptions.Value.DiscoveryNewChapterQueues.First();
        int reconciled = 0;

        foreach (Library library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (library.CrawlerAgent is null || library.Manga is null)
            {
                logger.LogWarning("Skipping library {LibraryId} — missing CrawlerAgent or Manga.", library.Id);
                continue;
            }

            using LibraryDbContext libDbContext = library.GetReadOnlyDbContext();
            MangaDownloadRecord? mangaDownloadRecord = libDbContext.MangaDownloadRecords.FindOne(p => true);

            if (mangaDownloadRecord is null)
            {
                logger.LogDebug("Skipping library {LibraryId} — no MangaDownloadRecord found.", library.Id);
                continue;
            }

            if (mangaDownloadRecord.DownloadStatus is Entities.Definitions.DownloadStatus.Cancelled)
            {
                logger.LogDebug("Skipping library {LibraryId} — MangaDownloadRecord is cancelled.", library.Id);
                continue;
            }

            RecurringJob.AddOrUpdate<IChapterDiscoveryJob>(
                library.GetDiscovertyJobId(),
                (job) => job.DispatchAsync(mangaDiscoveryQueue, library.CrawlerAgent.Id, library.Id, null!, CancellationToken.None),
                Cron.Daily());

            reconciled++;
            logger.LogInformation("Reconciled recurring discovery job for \"{Title}\" (Library {LibraryId}).", library.Manga.Title, library.Id);
        }

        context?.SetJobParameter("reconciledCount", reconciled);
        logger.LogInformation("Dispatch \"{title}\" completed. Reconciled {Count} recurring jobs.", nameof(CollectionReconciliationJob), reconciled);
        return Task.CompletedTask;
    }
}
