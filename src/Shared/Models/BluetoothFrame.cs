using System.Text;
using System.Text.Json;

namespace OmniConnect.Shared.Models;

/// <summary>
/// Framed Bluetooth envelope used for standalone peer-to-peer messaging.
///
/// Frame layout (all big-endian unless noted):
///   [0..1]   Magic        0x4F 0x4D ("OM")
///   [2]      Version      1
///   [3]      FrameType    0x01=ChatMessage, 0x02=Discovery, 0x03=Ack
///   [4..5]   PayloadLen   uint16
///   [6..6+n] Payload      JSON UTF-8 (ChatMessageFramePayload)
///   [6+n..]  Checksum     uint16 (sum of bytes [0..6+n))
///
/// Messages are ephemeral: kept in memory only, never written to the DB,
/// and cleared when the transport stops or a session ends.
/// </summary>
public sealed class BluetoothFrame
{
    public const byte Magic1 = 0x4F; // 'O'
    public const byte Magic2 = 0x4D; // 'M'
    public const byte Version = 1;

    public const byte FrameTypeChatMessage = 0x01;
    public const byte FrameTypeDiscovery = 0x02;
    public const byte FrameTypeAck = 0x03;

    public byte FrameType { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public BluetoothFrame() { }

    public BluetoothFrame(byte frameType, byte[] payload)
    {
        FrameType = frameType;
        Payload = payload;
    }

    /// <summary>Serialize this frame to wire bytes.</summary>
    public byte[] ToBytes()
    {
        var payload = Payload;
        var length = 6 + payload.Length + 2; // header + payload + checksum
        var bytes = new byte[length];

        bytes[0] = Magic1;
        bytes[1] = Magic2;
        bytes[2] = Version;
        bytes[3] = FrameType;
        bytes[4] = (byte)(payload.Length >> 8);
        bytes[5] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, bytes, 6, payload.Length);

        ushort checksum = 0;
        for (var i = 0; i < 6 + payload.Length; i++)
            checksum += bytes[i];
        bytes[6 + payload.Length] = (byte)(checksum >> 8);
        bytes[7 + payload.Length] = (byte)(checksum & 0xFF);

        return bytes;
    }

    /// <summary>Parse wire bytes into a frame. Returns null on malformed input.</summary>
    public static BluetoothFrame? FromBytes(byte[] data)
    {
        if (data == null || data.Length < 8) return null;
        if (data[0] != Magic1 || data[1] != Magic2 || data[2] != Version) return null;

        var payloadLength = (ushort)((data[4] << 8) | data[5]);
        if (data.Length < 6 + payloadLength + 2) return null;

        ushort expectedChecksum = 0;
        for (var i = 0; i < 6 + payloadLength; i++)
            expectedChecksum += data[i];
        ushort actualChecksum = (ushort)((data[6 + payloadLength] << 8) | data[7 + payloadLength]);
        if (expectedChecksum != actualChecksum) return null;

        var payload = new byte[payloadLength];
        Buffer.BlockCopy(data, 6, payload, 0, payloadLength);

        return new BluetoothFrame(data[3], payload);
    }

    /// <summary>Build a chat-message frame payload as JSON.</summary>
    public static byte[] ChatPayload(string fromPeerId, string fromPeerName, string toPeerId,
        string content, string clientMessageId, DateTime sentAt)
    {
        var obj = new
        {
            type = "chat",
            fromPeerId,
            fromPeerName,
            toPeerId,
            content,
            clientMessageId,
            sentAt = sentAt.ToUniversalTime().ToString("o")
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
    }

    /// <summary>Parse a chat-message frame payload.</summary>
    public static ChatMessagePayload? ParseChatPayload(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatMessagePayload>(
                Encoding.UTF8.GetString(payload),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Deserialized chat payload carried inside a BluetoothFrame.</summary>
public sealed class ChatMessagePayload
{
    public string Type { get; set; } = "chat";
    public string FromPeerId { get; set; } = string.Empty;
    public string FromPeerName { get; set; } = string.Empty;
    public string ToPeerId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ClientMessageId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}
