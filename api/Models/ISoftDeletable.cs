using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

/// <summary>
/// Interface for entities that support soft-delete instead of hard-delete.
/// When an entity is "deleted", IsDeleted is set to true and DeletedAt/DeletedBy are populated.
/// All queries for active data must filter where IsDeleted != true.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
