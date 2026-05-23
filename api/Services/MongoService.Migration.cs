using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    /// <summary>
    /// Migrates FrozenItems without OutletId to specified outlet
    /// </summary>
    public async Task<int> MigrateFrozenItemsOutletIdAsync(string outletId)
    {
        var filter = Builders<FrozenItem>.Filter.Or(
            Builders<FrozenItem>.Filter.Eq(item => item.OutletId, null),
            Builders<FrozenItem>.Filter.Exists(item => item.OutletId, false)
        );

        var update = Builders<FrozenItem>.Update
            .Set(item => item.OutletId, outletId)
            .Set(item => item.UpdatedAt, DateTime.UtcNow);

        var result = await _frozenItems.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }

    /// <summary>
    /// Migrates Ingredients without OutletId to specified outlet
    /// </summary>
    public async Task<int> MigrateIngredientsOutletIdAsync(string outletId)
    {
        var filter = Builders<Ingredient>.Filter.Or(
            Builders<Ingredient>.Filter.Eq(item => item.OutletId, null),
            Builders<Ingredient>.Filter.Exists(item => item.OutletId, false)
        );

        var update = Builders<Ingredient>.Update
            .Set(item => item.OutletId, outletId)
            .Set(item => item.UpdatedAt, DateTime.UtcNow);

        var result = await _ingredients.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }

    /// <summary>
    /// Migrates MenuCategories without OutletId to specified outlet
    /// </summary>
    public async Task<int> MigrateCategoriesOutletIdAsync(string outletId)
    {
        var filter = Builders<MenuCategory>.Filter.Or(
            Builders<MenuCategory>.Filter.Eq(item => item.OutletId, null),
            Builders<MenuCategory>.Filter.Exists(item => item.OutletId, false)
        );

        var update = Builders<MenuCategory>.Update
            .Set(item => item.OutletId, outletId);

        var result = await _categories.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }

    /// <summary>
    /// Migrates MenuSubCategories without OutletId to specified outlet
    /// </summary>
    public async Task<int> MigrateSubCategoriesOutletIdAsync(string outletId)
    {
        var filter = Builders<MenuSubCategory>.Filter.Or(
            Builders<MenuSubCategory>.Filter.Eq(item => item.OutletId, null),
            Builders<MenuSubCategory>.Filter.Exists(item => item.OutletId, false)
        );

        var update = Builders<MenuSubCategory>.Update
            .Set(item => item.OutletId, outletId);

        var result = await _subCategories.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }

    /// <summary>
    /// Migrates PlatformCharges without OutletId to specified outlet
    /// </summary>
    public async Task<int> MigratePlatformChargesOutletIdAsync(string outletId)
    {
        var filter = Builders<PlatformCharge>.Filter.Or(
            Builders<PlatformCharge>.Filter.Eq(c => c.OutletId, null),
            Builders<PlatformCharge>.Filter.Exists(c => c.OutletId, false)
        );

        var update = Builders<PlatformCharge>.Update
            .Set(c => c.OutletId, outletId)
            .Set(c => c.UpdatedAt, GetIstNow());

        var result = await _platformCharges.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }

    /// <summary>
    /// Gets the count of records with and without outlet IDs
    /// </summary>
    public async Task<MigrationStatus> GetMigrationStatusAsync()
    {
        var status = new MigrationStatus();

        // Count FrozenItems
        var frozenItemsNoOutlet = Builders<FrozenItem>.Filter.Or(
            Builders<FrozenItem>.Filter.Eq(item => item.OutletId, null),
            Builders<FrozenItem>.Filter.Exists(item => item.OutletId, false)
        );
        status.FrozenItemsWithoutOutlet = (int)await _frozenItems.CountDocumentsAsync(frozenItemsNoOutlet);
        status.FrozenItemsWithOutlet = (int)await _frozenItems.CountDocumentsAsync(
            Builders<FrozenItem>.Filter.And(
                Builders<FrozenItem>.Filter.Exists(item => item.OutletId, true),
                Builders<FrozenItem>.Filter.Ne(item => item.OutletId, null)
            )
        );

        // Count Ingredients
        var ingredientsNoOutlet = Builders<Ingredient>.Filter.Or(
            Builders<Ingredient>.Filter.Eq(item => item.OutletId, null),
            Builders<Ingredient>.Filter.Exists(item => item.OutletId, false)
        );
        status.IngredientsWithoutOutlet = (int)await _ingredients.CountDocumentsAsync(ingredientsNoOutlet);
        status.IngredientsWithOutlet = (int)await _ingredients.CountDocumentsAsync(
            Builders<Ingredient>.Filter.And(
                Builders<Ingredient>.Filter.Exists(item => item.OutletId, true),
                Builders<Ingredient>.Filter.Ne(item => item.OutletId, null)
            )
        );

        // Count Categories
        var categoriesNoOutlet = Builders<MenuCategory>.Filter.Or(
            Builders<MenuCategory>.Filter.Eq(item => item.OutletId, null),
            Builders<MenuCategory>.Filter.Exists(item => item.OutletId, false)
        );
        status.CategoriesWithoutOutlet = (int)await _categories.CountDocumentsAsync(categoriesNoOutlet);
        status.CategoriesWithOutlet = (int)await _categories.CountDocumentsAsync(
            Builders<MenuCategory>.Filter.And(
                Builders<MenuCategory>.Filter.Exists(item => item.OutletId, true),
                Builders<MenuCategory>.Filter.Ne(item => item.OutletId, null)
            )
        );

        // Count SubCategories
        var subCategoriesNoOutlet = Builders<MenuSubCategory>.Filter.Or(
            Builders<MenuSubCategory>.Filter.Eq(item => item.OutletId, null),
            Builders<MenuSubCategory>.Filter.Exists(item => item.OutletId, false)
        );
        status.SubCategoriesWithoutOutlet = (int)await _subCategories.CountDocumentsAsync(subCategoriesNoOutlet);
        status.SubCategoriesWithOutlet = (int)await _subCategories.CountDocumentsAsync(
            Builders<MenuSubCategory>.Filter.And(
                Builders<MenuSubCategory>.Filter.Exists(item => item.OutletId, true),
                Builders<MenuSubCategory>.Filter.Ne(item => item.OutletId, null)
            )
        );

        // Count PlatformCharges
        var platformChargesNoOutlet = Builders<PlatformCharge>.Filter.Or(
            Builders<PlatformCharge>.Filter.Eq(c => c.OutletId, null),
            Builders<PlatformCharge>.Filter.Exists(c => c.OutletId, false)
        );
        status.PlatformChargesWithoutOutlet = (int)await _platformCharges.CountDocumentsAsync(platformChargesNoOutlet);
        status.PlatformChargesWithOutlet = (int)await _platformCharges.CountDocumentsAsync(
            Builders<PlatformCharge>.Filter.And(
                Builders<PlatformCharge>.Filter.Exists(c => c.OutletId, true),
                Builders<PlatformCharge>.Filter.Ne(c => c.OutletId, null)
            )
        );

        status.TotalWithoutOutlet = status.FrozenItemsWithoutOutlet + status.IngredientsWithoutOutlet + status.CategoriesWithoutOutlet + status.SubCategoriesWithoutOutlet + status.PlatformChargesWithoutOutlet;
        status.TotalWithOutlet = status.FrozenItemsWithOutlet + status.IngredientsWithOutlet + status.CategoriesWithOutlet + status.SubCategoriesWithOutlet + status.PlatformChargesWithOutlet;
        status.NeedsMigration = status.TotalWithoutOutlet > 0;

        return status;
    }

    public class MigrationStatus
    {
        public int FrozenItemsWithoutOutlet { get; set; }
        public int FrozenItemsWithOutlet { get; set; }
        public int IngredientsWithoutOutlet { get; set; }
        public int IngredientsWithOutlet { get; set; }
        public int CategoriesWithoutOutlet { get; set; }
        public int CategoriesWithOutlet { get; set; }
        public int SubCategoriesWithoutOutlet { get; set; }
        public int SubCategoriesWithOutlet { get; set; }
        public int PlatformChargesWithoutOutlet { get; set; }
        public int PlatformChargesWithOutlet { get; set; }
        public int TotalWithoutOutlet { get; set; }
        public int TotalWithOutlet { get; set; }
        public bool NeedsMigration { get; set; }
    }
}
