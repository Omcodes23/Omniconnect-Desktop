using System.Security.Cryptography;
using System.Text;

namespace OmniConnect.Backend.Services;

/// <summary>
/// E2E encryption service using AES-256-GCM for message content encryption.
/// Keys are derived using ECDH key exchange (X25519).
/// </summary>
public class EncryptionService
{
    private readonly ILogger<EncryptionService> _logger;

    public EncryptionService(ILogger<EncryptionService> logger)
    {
        _logger = logger;
    }

    /// <summary>Generate a new ECDH key pair (X25519).</summary>
    public (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (
            ecdh.PublicKey.ToByteArray(),
            ecdh.ExportECPrivateKey()
        );
    }

    /// <summary>Derive a shared secret from private key and peer's public key.</summary>
    public byte[] DeriveSharedSecret(byte[] privateKey, byte[] peerPublicKey)
    {
        using var ecdh = ECDiffieHellman.Create();
        ecdh.ImportECPrivateKey(privateKey, out _);

        var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

        return ecdh.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256);
    }

    /// <summary>Encrypt plaintext using AES-256-GCM with the derived key.</summary>
    public (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(byte[] key, string plaintext)
    {
        var nonce = new byte[12]; // 96-bit nonce for GCM
        RandomNumberGenerator.Fill(nonce);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[16]; // 128-bit authentication tag

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    /// <summary>Decrypt ciphertext using AES-256-GCM with the derived key.</summary>
    public string Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var plainBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>Hash a message for integrity verification.</summary>
    public string HashMessage(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Encrypt a message payload for storage with a device-specific key.</summary>
    public string EncryptForStorage(string plaintext, byte[] deviceKey)
    {
        var (ciphertext, nonce, tag) = Encrypt(deviceKey, plaintext);
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);
        return Convert.ToBase64String(combined);
    }

    /// <summary>Decrypt a message payload that was encrypted for storage.</summary>
    public string DecryptFromStorage(string encryptedBase64, byte[] deviceKey)
    {
        var combined = Convert.FromBase64String(encryptedBase64);
        var nonce = combined[..12];
        var tag = combined[12..28];
        var ciphertext = combined[28..];
        return Decrypt(deviceKey, nonce, ciphertext, tag);
    }
}