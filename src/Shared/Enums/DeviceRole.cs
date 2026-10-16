namespace OmniConnect.Shared.Enums;

[Flags]
public enum DeviceRole
{
    None = 0,
    Client = 1,
    ServiceHost = 2,
    Gateway = 4,
    AiHost = 8
}
