using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.ViewModels;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentManagement.Display.Models;
using OrchardCore.DisplayManagement.Views;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Drivers;

public sealed class AutoBlogPartDisplayDriver : ContentPartDisplayDriver<AutoBlogPart>
{
    public override IDisplayResult Edit(AutoBlogPart part, BuildPartEditorContext context) =>
        Initialize<AutoBlogPartViewModel>("AutoBlogPart_Edit", model =>
            model.PreserveLocalEdits = part.PreserveLocalEdits);

    public override async Task<IDisplayResult> UpdateAsync(AutoBlogPart part, UpdatePartEditorContext context)
    {
        var model = new AutoBlogPartViewModel();
        if (await context.Updater.TryUpdateModelAsync(model, Prefix))
        {
            part.PreserveLocalEdits = model.PreserveLocalEdits;
        }
        return Edit(part, context);
    }
}
