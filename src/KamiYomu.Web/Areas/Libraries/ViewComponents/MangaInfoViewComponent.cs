using KamiYomu.CrawlerAgents.Core.Catalog;

using Microsoft.AspNetCore.Mvc;

namespace KamiYomu.Web.Areas.Libraries.ViewComponents;

public class MangaInfoViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(Manga manga)
    {
        string? preview = manga?.Description?.Length > 50 ? manga.Description[..50] + "..." : manga.Description;
        bool needsExpand = manga?.Description?.Length > 50;

        return View(new MangaInfoViewComponentModel(manga, preview, needsExpand));
    }
}

public record MangaInfoViewComponentModel(Manga? Manga, string Preview, bool NeedsExpand);
