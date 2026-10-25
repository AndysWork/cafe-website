using Cafe.Api.Models;
using Cafe.Api.Repositories;

namespace Cafe.Api.Services;

public static class ShopOrderItemBuilder
{
    public static decimal UnitPrice(CafeMenuItem item) =>
        item.ShopSellingPrice > 0 ? item.ShopSellingPrice : item.WebPrice > 0 ? item.WebPrice : item.OnlinePrice;

    public static async Task<(List<OrderItem> Items, decimal Subtotal, string? Error)> BuildAsync(
        IMenuRepository menu, List<OrderItemRequest> requestedItems, string outletId)
    {
        var items = new List<OrderItem>();
        if (requestedItems == null || requestedItems.Count is < 1 or > 100
            || requestedItems.Any(i => i == null || string.IsNullOrWhiteSpace(i.MenuItemId)
                || i.Quantity is < 1 or > 1000 || i.SelectedAddOnNames?.Count > 50))
            return (items, 0, "Provide 1-100 valid items, quantities 1-1000, and at most 50 add-ons per item");

        var menuItems = await menu.GetMenuItemsByIdsAsync(requestedItems.Select(i => i.MenuItemId).Distinct().ToList(), outletId);
        var menuMap = menuItems.Where(m => m.Id != null).ToDictionary(m => m.Id!);
        var categoryIds = menuItems.Where(m => !string.IsNullOrEmpty(m.CategoryId)).Select(m => m.CategoryId!).Distinct().ToList();
        var categories = categoryIds.Count > 0
            ? await menu.GetCategoriesByIdsAsync(categoryIds)
            : new Dictionary<string, string>();
        decimal subtotal = 0;
        foreach (var item in requestedItems)
        {
            if (!menuMap.TryGetValue(item.MenuItemId, out var menuItem))
                return (items, 0, $"Menu item {item.MenuItemId} not found");
            if (!menuItem.IsAvailable)
                return (items, 0, $"{menuItem.Name} is currently unavailable");
            var basePrice = UnitPrice(menuItem);
            string? variantName = null;
            decimal? variantPrice = null;
            if (!string.IsNullOrWhiteSpace(item.SelectedVariantName))
            {
                var variant = menuItem.Variants?.FirstOrDefault(v =>
                    v.VariantName.Equals(item.SelectedVariantName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (variant == null)
                    return (items, 0, $"Selected variant '{item.SelectedVariantName}' is not available for {menuItem.Name}");
                variantName = variant.VariantName;
                variantPrice = basePrice = variant.Price;
            }
            var addOns = new List<OrderItemAddOn>();
            foreach (var name in item.SelectedAddOnNames ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var addOn = menuItem.AddOns?.FirstOrDefault(a => a.IsActive &&
                    a.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (addOn == null)
                    return (items, 0, $"Selected add-on '{name}' is not available for {menuItem.Name}");
                addOns.Add(new OrderItemAddOn { Name = addOn.Name, Price = addOn.Price });
            }
            var addOnTotal = addOns.Sum(a => a.Price);
            var price = basePrice + addOnTotal;
            var total = price * item.Quantity;
            subtotal += total;
            categories.TryGetValue(menuItem.CategoryId ?? string.Empty, out var categoryName);
            items.Add(new OrderItem
            {
                MenuItemId = menuItem.Id!, Name = menuItem.Name, Description = menuItem.Description,
                CategoryId = menuItem.CategoryId, CategoryName = categoryName,
                Quantity = item.Quantity, Price = price, BaseUnitPrice = basePrice,
                SelectedVariantName = variantName, SelectedVariantPrice = variantPrice,
                SelectedAddOns = addOns, AddOnTotal = addOnTotal, Total = total
            });
        }
        return (items, subtotal, null);
    }
}
