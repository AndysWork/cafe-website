using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Cafe.Api.Helpers;

/// <summary>
/// A BSON serializer that can deserialize either a BsonType.ObjectId (converting to 24-char hex string)
/// or a BsonType.String directly into a C# string property.
/// Serializes always as BsonType.ObjectId if it is a valid 24-digit hex, otherwise as BsonType.String or Null.
/// </summary>
public class StringOrObjectIdSerializer : SerializerBase<string?>
{
    public override string? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.CurrentBsonType;

        switch (bsonType)
        {
            case BsonType.Null:
                context.Reader.ReadNull();
                return null;

            case BsonType.ObjectId:
                var objectId = context.Reader.ReadObjectId();
                return objectId.ToString();

            case BsonType.String:
                return context.Reader.ReadString();

            default:
                throw new BsonSerializationException($"Cannot deserialize a string from BsonType {bsonType}");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string? value)
    {
        if (value == null)
        {
            context.Writer.WriteNull();
            return;
        }

        var trimmed = value.Trim();
        if (ObjectId.TryParse(trimmed, out var objectId))
        {
            context.Writer.WriteObjectId(objectId);
        }
        else
        {
            context.Writer.WriteString(value);
        }
    }
}
