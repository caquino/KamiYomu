using Hangfire;
using Hangfire.InMemory;

using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.Web.AppOptions;
using KamiYomu.Web.Entities;
using KamiYomu.Web.Infrastructure.Contexts;
using KamiYomu.Web.Worker;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KamiYomu.Web.Tests.Worker;

public class CollectionReconciliationJobTests : IDisposable
{
    private readonly Mock<ILogger<CollectionReconciliationJob>> _mockLogger;
    private readonly Mock<IOptions<WorkerOptions>> _mockWorkerOptions;
    private readonly DbContext _dbContext;
    private readonly CollectionReconciliationJob _job;
    private readonly InMemoryStorage _storage;

    public CollectionReconciliationJobTests()
    {
        _mockLogger = new Mock<ILogger<CollectionReconciliationJob>>();

        WorkerOptions workerOptions = new()
        {
            ServerAvailableNames = ["server-1"],
            DownloadChapterQueues = ["download-queue"],
            MangaDownloadSchedulerQueues = ["manga-scheduler-queue"],
            DiscoveryNewChapterQueues = ["discovery-queue"]
        };

        _mockWorkerOptions = new Mock<IOptions<WorkerOptions>>();
        _ = _mockWorkerOptions.Setup(o => o.Value).Returns(workerOptions);

        _dbContext = new DbContext(":memory:", false);

        _storage = new InMemoryStorage();
        JobStorage.Current = _storage;

        _job = new CollectionReconciliationJob(
            _mockLogger.Object,
            _mockWorkerOptions.Object,
            _dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _storage.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Library CreateLibrary()
    {
        CrawlerAgent crawlerAgent = new();
        Manga manga = MangaBuilder.Create().WithTitle("Test Manga").Build();
        return new Library(crawlerAgent, manga, "test", "test", "test");
    }

    [Fact]
    public async Task DispatchAsync_WithNoLibraries_CompletesWithZeroReconciled()
    {
        // Arrange — empty Libraries collection (no inserts)

        // Act
        await _job.DispatchAsync(Defaults.Worker.CollectionReconciliationQueue, null!, CancellationToken.None);

        // Assert — completed log message with 0 reconciled
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Reconciled 0 recurring jobs")
                                           && v.ToString()!.Contains("Reset 0 manga records")
                                           && v.ToString()!.Contains("Reset 0 chapter records")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WithCancellationRequested_ThrowsOperationCancelledException()
    {
        // Arrange
        Library library = CreateLibrary();
        _ = _dbContext.Libraries.Insert(library);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act & Assert
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => _job.DispatchAsync(Defaults.Worker.CollectionReconciliationQueue, null!, cts.Token));
    }

    [Fact]
    public async Task DispatchAsync_SkipsLibrary_WhenMangaIsNull()
    {
        // Arrange — Library with empty Manga results in library.Manga == null
        Library library = new(new CrawlerAgent(), new Manga(), "test", "test", "test");
        _ = _dbContext.Libraries.Insert(library);

        // Act
        await _job.DispatchAsync(Defaults.Worker.CollectionReconciliationQueue, null!, CancellationToken.None);

        // Assert — skipped because Manga is null
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("missing CrawlerAgent or Manga")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
