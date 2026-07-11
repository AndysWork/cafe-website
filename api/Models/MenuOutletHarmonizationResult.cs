namespace Cafe.Api.Models;

public class MenuOutletHarmonizationResult
{
    public string SourceOutletId { get; set; } = string.Empty;
    public string SourceOutletName { get; set; } = string.Empty;
    public int OutletsProcessed { get; set; }
    public int TotalCategoriesCreated { get; set; }
    public int TotalSubCategoriesCreated { get; set; }
    public int TotalMenuItemsCreated { get; set; }
    public int TotalMenuItemsUpdated { get; set; }
    public int TotalExtraMenuItemsDisabled { get; set; }
    public List<MenuOutletHarmonizationOutletSummary> Summaries { get; set; } = new();
}

public class MenuOutletHarmonizationOutletSummary
{
    public string OutletId { get; set; } = string.Empty;
    public string OutletName { get; set; } = string.Empty;
    public int CategoriesCreated { get; set; }
    public int SubCategoriesCreated { get; set; }
    public int MenuItemsCreated { get; set; }
    public int MenuItemsUpdated { get; set; }
    public int ExtraMenuItemsDisabled { get; set; }
}
