using Microsoft.AspNetCore.Mvc;

namespace KamiYomu.Web.Areas.Reader.ViewComponents;

public class ReaderHeaderViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string returnUrl)
    {
        returnUrl = string.IsNullOrWhiteSpace(returnUrl) ? HttpContext.Request.Query["returnUrl"].ToString() : returnUrl;

        return View(new ReaderHeaderComponentModel(returnUrl));
    }
}

public record ReaderHeaderComponentModel(string ReturnUrl);
