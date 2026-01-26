using KamiYomu.Web.Entities;

using Microsoft.AspNetCore.Mvc;

namespace KamiYomu.Web.Areas.Libraries.ViewComponents;

public class LibraryCardViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Library library, CancellationToken cancellationToken = default)
    {
        using ICrawlerAgent crawlerInstance = library.CrawlerAgent.GetCrawlerInstance();
        Uri faviconUrl = await crawlerInstance.GetFaviconAsync(cancellationToken);
        bool isNew = library.Id == Guid.Empty;
        string cardId = $"library-card-{library.Manga.Id}".Replace(".", "-");
        string addToCollectionUrl = $"/Libraries/Collection/Dialogs/AddToCollection?CrawlerAgentId={library.CrawlerAgent.Id}&MangaId={library.Manga.Id}&RefreshElementId={cardId}";
        string removeFromCollectionUrl = $"/Libraries/Collection/Dialogs/RemoveFromCollection?LibraryId={library.Id}&RefreshElementId={cardId}";
        string downloadStatusUrl = $"/Libraries/Collection/Dialogs/DownloadStatus?libraryId={library.Id}";
        string mangaDetailsUrl = $"/Libraries/Collection/Dialogs/MangaDetails?crawlerAgentId={library.CrawlerAgent.Id}&mangaId={library.Manga.Id}";
        return View(
            new LibraryCardViewComponentModel(
                library,
                faviconUrl,
                isNew,
                cardId,
                addToCollectionUrl,
                removeFromCollectionUrl,
                downloadStatusUrl,
                mangaDetailsUrl));
    }
}

public record LibraryCardViewComponentModel(
    Library Library,
    Uri FaviconUrl,
    bool IsNew,
    string CardId,
    string AddToCollectionUrl,
    string RemoveFromCollectionUrl,
    string DownloadStatusUrl,
    string MangaDetailsUrl);
