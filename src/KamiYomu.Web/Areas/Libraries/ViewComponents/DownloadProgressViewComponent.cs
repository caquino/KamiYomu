using KamiYomu.Web.Entities;
using KamiYomu.Web.Entities.Definitions;
using KamiYomu.Web.Infrastructure.Contexts;

using Microsoft.AspNetCore.Mvc;

namespace KamiYomu.Web.Areas.Libraries.ViewComponents;

public class DownloadProgressViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(Library library)
    {
        using LibraryDbContext libDbContext = library.GetReadOnlyDbContext();
        MangaDownloadRecord downloadManga = libDbContext.MangaDownloadRecords.FindOne(p => p.Library.Id == library.Id);
        if (downloadManga == null)
        {
            return Content(string.Empty);
        }
        List<ChapterDownloadRecord> downloadChapters = [.. libDbContext.ChapterDownloadRecords.Find(p => p.MangaDownload.Id == downloadManga.Id)];
        decimal completed = downloadChapters.Count(p => p.DownloadStatus == DownloadStatus.Completed);
        decimal total = downloadChapters.Count;
        decimal progress = total > 0 ? completed / total * 100 : 0;

        return View(new DownloadProgressViewComponentModel(progress, total, completed));
    }
}

public record DownloadProgressViewComponentModel(
    decimal Progress,
    decimal Total,
    decimal Completed);
