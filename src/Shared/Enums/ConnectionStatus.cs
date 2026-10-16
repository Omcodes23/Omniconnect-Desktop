using System.Text.Json.Serialization;
using OmniConnect.Shared.Converters;

namespace OmniConnect.Shared.Enums;

[JsonConverter(typeof(FlexibleEnumConverter<ConnectionStatus>))]
public enum ConnectionStatus
{
    Offline = 0,
    Online = 1,
    Away = 2,
    Busy = 3,
    DoNotDisturb = 4,
    Invisible = 5
}
