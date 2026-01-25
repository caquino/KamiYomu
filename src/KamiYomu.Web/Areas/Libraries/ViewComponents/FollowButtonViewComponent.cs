using KamiYomu.Web.Areas.Libraries.Pages.Collection.Dialogs;

using Microsoft.AspNetCore.Mvc;

namespace KamiYomu.Web.Areas.Libraries.ViewComponents;

public class FollowButtonViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(FollowButtonViewModel viewModel)
    {
        return View(viewModel);
    }
}
