using Hangfire;
using Hangfire.Server;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

using KamiYomu.Web.AppOptions;
using KamiYomu.Web.Entities;
using KamiYomu.Web.Entities.Definitions;
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

        IMonitoringApi monitoring = JobStorage.Current.GetMonitoringApi();
        using IStorageConnection connection = JobStorage.Current.GetConnection();
        HashSet<string> existingRecurringJobIds = [.. connection.GetRecurringJobs().Select(j => j.Id)];
        IEnumerable<Library> libraries = dbContext.Libraries.FindAll();
        string mangaDiscoveryQueue = workerOptions.Value.DiscoveryNewChapterQueues.First();
        int reconciled = 0;
        int mangaReset = 0;
        int chapterReset = 0;
        int triggered = 0;

        foreach (Library library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (library.CrawlerAgent is null || library.Manga is null)
            {
                logger.LogWarning("Skipping library {LibraryId} — missing CrawlerAgent or Manga.", library.Id);
                continue;
            }

            using LibraryDbContext libDbContext = library.GetReadWriteDbContext();
            MangaDownloadRecord? mangaDownloadRecord = libDbContext.MangaDownloadRecords.FindOne(p => true);

            if (mangaDownloadRecord is null)
            {
                logger.LogDebug("Skipping library {LibraryId} — no MangaDownloadRecord found.", library.Id);
                continue;
            }

            if (mangaDownloadRecord.DownloadStatus is DownloadStatus.Cancelled)
            {
                logger.LogDebug("Skipping library {LibraryId} — MangaDownloadRecord is cancelled.", library.Id);
                continue;
            }

            if (mangaDownloadRecord.DownloadStatus is DownloadStatus.InProgress or DownloadStatus.Scheduled
                && !string.IsNullOrWhiteSpace(mangaDownloadRecord.BackgroundJobId))
            {
                JobDetailsDto? jobDetails = monitoring.JobDetails(mangaDownloadRecord.BackgroundJobId);

                if (jobDetails is null)
                {
                    mangaDownloadRecord.Pending("Reset by reconciliation — background job no longer exists.");
                    _ = libDbContext.MangaDownloadRecords.Update(mangaDownloadRecord);
                    mangaReset++;
                    logger.LogInformation("Reset MangaDownloadRecord to ToBeRescheduled for \"{Title}\" (Library {LibraryId}) — job {JobId} no longer exists.",
                        library.Manga.Title, library.Id, mangaDownloadRecord.BackgroundJobId);
                }
            }

            IEnumerable<ChapterDownloadRecord> orphanedChapterRecords = libDbContext.ChapterDownloadRecords
                .Find(r => (r.DownloadStatus == DownloadStatus.InProgress || r.DownloadStatus == DownloadStatus.Scheduled)
                        && r.BackgroundJobId != null && r.BackgroundJobId != "");

            foreach (ChapterDownloadRecord chapterRecord in orphanedChapterRecords)
            {
                JobDetailsDto? jobDetails = monitoring.JobDetails(chapterRecord.BackgroundJobId);

                if (jobDetails is null)
                {
                    chapterRecord.ToBeRescheduled("Reset by reconciliation — background job no longer exists.");
                    _ = libDbContext.ChapterDownloadRecords.Update(chapterRecord);
                    chapterReset++;
                    logger.LogInformation("Reset ChapterDownloadRecord to ToBeRescheduled for chapter \"{ChapterId}\" (Library {LibraryId}) — job {JobId} no longer exists.",
                        chapterRecord.Chapter?.Id, library.Id, chapterRecord.BackgroundJobId);
                }
            }

            string discoveryJobId = library.GetDiscovertyJobId();
            bool isNewJob = !existingRecurringJobIds.Contains(discoveryJobId);

            RecurringJob.AddOrUpdate<IChapterDiscoveryJob>(
                discoveryJobId,
                (job) => job.DispatchAsync(mangaDiscoveryQueue, library.CrawlerAgent.Id, library.Id, null!, CancellationToken.None),
                Cron.Daily());

            if (isNewJob)
            {
                RecurringJob.TriggerJob(discoveryJobId);
                triggered++;
                logger.LogInformation("Triggered immediate execution of discovery job for \"{Title}\" (Library {LibraryId}).", library.Manga.Title, library.Id);
            }

            reconciled++;
            logger.LogInformation("Reconciled recurring discovery job for \"{Title}\" (Library {LibraryId}).", library.Manga.Title, library.Id);
        }

        context?.SetJobParameter("reconciledCount", reconciled);
        context?.SetJobParameter("mangaResetCount", mangaReset);
        context?.SetJobParameter("chapterResetCount", chapterReset);
        context?.SetJobParameter("triggeredCount", triggered);
        logger.LogInformation("Dispatch \"{title}\" completed. Reconciled {Reconciled} recurring jobs. Reset {MangaReset} manga records. Reset {ChapterReset} chapter records. Triggered {Triggered} immediate executions.",
            nameof(CollectionReconciliationJob), reconciled, mangaReset, chapterReset, triggered);
        return Task.CompletedTask;
    }
}
