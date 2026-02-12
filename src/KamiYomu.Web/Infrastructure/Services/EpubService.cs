using System.IO.Compression;

using KamiYomu.Web.Entities;
using KamiYomu.Web.Entities.Definitions;
using KamiYomu.Web.Infrastructure.Contexts;
using KamiYomu.Web.Infrastructure.Services.Interfaces;
using KamiYomu.Web.Models;

using static KamiYomu.Web.AppOptions.Defaults;

namespace KamiYomu.Web.Infrastructure.Services;

public class EpubService([FromKeyedServices(ServiceLocator.ReadOnlyDbContext)] DbContext dbContext) : IEpubService
{
    public DownloadResponse? GetDownloadResponse(Guid libraryId, Guid chapterDownloadId)
    {
        Library library = dbContext.Libraries
           .Query()
           .Where(l => l.Id == libraryId)
           .FirstOrDefault();

        if (library == null)
        {
            return null;
        }

        using LibraryDbContext libraryContext = library.GetReadOnlyDbContext();
        ChapterDownloadRecord chapterDownloadRecord = libraryContext.ChapterDownloadRecords
            .Query()
            .Where(r => r.Id == chapterDownloadId && (int)(object)r.DownloadStatus == (int)(object)DownloadStatus.Completed)
            .FirstOrDefault();

        if (chapterDownloadRecord == null)
        {
            return null;
        }

        string cbzFilePath = library.GetCbzFilePath(chapterDownloadRecord.Chapter);
        if (!File.Exists(cbzFilePath))
        {
            return null;
        }

        MemoryStream outputStream = new();
        List<string> pageNames = [];

        // Create EPUB archive (leaveOpen: false so the ZIP is finalized when disposed)
        using (ZipArchive epubArchive = new(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. Add mimetype file (must be first and uncompressed)
            ZipArchiveEntry mimeEntry = epubArchive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (StreamWriter writer = new(mimeEntry.Open()))
            {
                writer.Write("application/epub+zip");
            }

            // 2. Add META-INF/container.xml
            ZipArchiveEntry containerEntry = epubArchive.CreateEntry("META-INF/container.xml", CompressionLevel.Fastest);
            using (StreamWriter writer = new(containerEntry.Open()))
            {
                writer.Write(@"<?xml version=""1.0""?>
                                <container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
                                  <rootfiles>
                                    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
                                  </rootfiles>
                                </container>");
            }

            // 3. Add images from CBZ to OEBPS/images
            using ZipArchive cbzArchive = ZipFile.OpenRead(cbzFilePath);
            int pageNumber = 1;
            string coverFileName = null;

            foreach (ZipArchiveEntry? entry in cbzArchive.Entries.OrderBy(p => p.FullName))
            {
                if (!entry.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isCover = entry.FullName.Contains("cover", StringComparison.OrdinalIgnoreCase);
                string ext = Path.GetExtension(entry.FullName).ToLower();
                string pageFileName = isCover ? "images/cover" + ext : $"images/page{pageNumber:D3}{ext}";

                if (!isCover)
                {
                    pageNames.Add(pageFileName);
                    pageNumber++;
                }
                else
                {
                    coverFileName = pageFileName;
                }

                ZipArchiveEntry zipEntry = epubArchive.CreateEntry($"OEBPS/{pageFileName}", CompressionLevel.Fastest);
                using Stream entryStream = entry.Open();
                using Stream zipEntryStream = zipEntry.Open();
                entryStream.CopyTo(zipEntryStream);
            }

            // 4. Create content.opf
            ZipArchiveEntry contentOpfEntry = epubArchive.CreateEntry("OEBPS/content.opf", CompressionLevel.Fastest);
            using (StreamWriter writer = new(contentOpfEntry.Open()))
            {
                writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8""?>
                                <package xmlns=""http://www.idpf.org/2007/opf"" version=""2.0"" unique-identifier=""BookId"">
                                  <metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"">
                                    <dc:title>{library.GetComicInfoTitleTemplateResolved(chapterDownloadRecord.Chapter)}</dc:title>
                                    <dc:language>{library.Manga.OriginalLanguage}</dc:language>
                                    <dc:identifier id=""BookId"">{chapterDownloadRecord.Id}</dc:identifier>
                                    {(coverFileName != null ? $@"<meta name=""cover"" content=""cover-image""/>" : "")}
                                  </metadata>
                                  <manifest>
                                    {(coverFileName != null ? $@"<item id=""cover-image"" href=""{coverFileName}"" media-type=""image/{Path.GetExtension(coverFileName).TrimStart('.')}""></item>" : "")}
                                    {string.Join("\n", pageNames.Select(p => $@"<item id=""{Path.GetFileNameWithoutExtension(p)}"" href=""{p}"" media-type=""image/{Path.GetExtension(p).TrimStart('.')}""/>"))}
                                    <item id=""ncx"" href=""toc.ncx"" media-type=""application/x-dtbncx+xml""/>
                                  </manifest>
                                  <spine toc=""ncx"">
                                    {(coverFileName != null ? $@"<itemref idref=""cover-image""/>" : "")}
                                    {string.Join("\n", pageNames.Select(p => $@"<itemref idref=""{Path.GetFileNameWithoutExtension(p)}""/>"))}
                                  </spine>
                                </package>");
            }

            // 5. Create toc.ncx
            ZipArchiveEntry tocEntry = epubArchive.CreateEntry("OEBPS/toc.ncx", CompressionLevel.Fastest);
            using (StreamWriter writer = new(tocEntry.Open()))
            {
                writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8""?>
                                <ncx xmlns=""http://www.daisy.org/z3986/2005/ncx/"" version=""2005-1"">
                                  <head>
                                    <meta name=""dtb:uid"" content=""{chapterDownloadRecord.Id}""/>
                                  </head>
                                  <docTitle><text>{library.GetComicInfoTitleTemplateResolved(chapterDownloadRecord.Chapter)}</text></docTitle>
                                  <navMap>
                                    {string.Join("\n", pageNames.Select((p, i) => $@"<navPoint id=""navPoint-{i + 1}"" playOrder=""{i + 1}""><navLabel><text>Page {i + 1}</text></navLabel><content src=""{p}""/></navPoint>"))}
                                  </navMap>
                                </ncx>");
            }
        }

        string downloadFileName = $"{library.GetComicInfoTitleTemplateResolved(chapterDownloadRecord.Chapter)}.epub";
        return new DownloadResponse(outputStream, downloadFileName, "application/epub+zip");
    }
}
