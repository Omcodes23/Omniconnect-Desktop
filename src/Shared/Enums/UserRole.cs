using System.Text.Json.Serialization;
using OmniConnect.Shared.Converters;

namespace OmniConnect.Shared.Enums;

[JsonConverter(typeof(FlexibleEnumConverter<UserRole>))]
public enum UserRole
{
    User = 0,
    Moderator = 1,
    Admin = 2,
    System = 3
}
