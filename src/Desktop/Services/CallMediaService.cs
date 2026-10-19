using System.Diagnostics;
using NAudio.Wave;
using OpenCvSharp;

namespace OmniConnect.Desktop.Services;

/// <summary>
/// Captures and plays 16 kHz mono PCM16 call audio and streams JPEG webcam frames.
/// Transport is the authenticated Internet SignalR hub only; there is no LAN or
/// Bluetooth call path. Hardware that is missing or busy degrades to a silent,
/// video-less call instead of throwing.
/// </summary>
public sealed class CallMediaService : IDisposable
{
    private const int SampleRate = 16_000;
    private const int MicGateRms = 380;       // below this, treat as silence
    private const int EchoRms = 700;          // above this, duck remote playback
    private const int VideoWidth = 480;
    private const int VideoFps = 8;
    private const int JpegQuality = 55;

    private readonly Guid _targetUserId;
    private readonly Func<Guid, string, string, Task> _send;

    private WaveInEvent? _mic;
    private WaveOutEvent? _speaker;
    private BufferedWaveProvider? _remoteAudio;
    private readonly object _audioLock = new();

    private VideoCapture? _camera;
    private CancellationTokenSource? _videoCts;
    private Task? _videoTask;

    private volatile bool _muted;
    private volatile bool _speakerOn = true;
    private volatile bool _videoEnabled;
    private volatile bool _disposed;

    public event Action<byte[]>? RemoteVideoFrame;
    public event Action<byte[]>? LocalVideoFrame;

    public string AudioStatus { get; private set; } = "Audio ready";
    public string VideoStatus { get; private set; } = "Video off";
    public bool HasCamera { get; private set; }

    public CallMediaService(Guid targetUserId, Func<Guid, string, string, Task> send)
    {
        _targetUserId = targetUserId;
        _send = send;
    }

    public void Start(bool video)
    {
        StartAudio();
        if (video)
        {
            _videoEnabled = true;
            StartVideo();
        }
    }

    private void StartAudio()
    {
        try
        {
            var format = new WaveFormat(SampleRate, 16, 1);
            _remoteAudio = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true
            };

            _speaker = new WaveOutEvent { DesiredLatency = 120 };
            _speaker.Init(_remoteAudio);
            _speaker.Play();
        }
        catch (Exception ex)
        {
            AudioStatus = $"Speaker unavailable: {ex.Message}";
            _speaker = null;
        }

        try
        {
            _mic = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 40,
                NumberOfBuffers = 4
            };
            _mic.DataAvailable += OnMicData;
            _mic.StartRecording();
        }
        catch (Exception ex)
        {
            AudioStatus = $"Microphone unavailable: {ex.Message}";
            _mic = null;
        }
    }

    private void OnMicData(object? sender, WaveInEventArgs e)
    {
        if (_disposed || _muted || e.BytesRecorded == 0) return;

        var rms = ComputeRms(e.Buffer, e.BytesRecorded);
        DuckRemotePlayback(rms);

        // Simple noise gate: do not spend bandwidth on silence.
        if (rms < MicGateRms) return;

        var payload = Convert.ToBase64String(e.Buffer, 0, e.BytesRecorded);
        _ = SendSafeAsync("audio", payload);
    }

    private static int ComputeRms(byte[] buffer, int bytes)
    {
        var samples = bytes / 2;
        if (samples <= 0) return 0;

        long sum = 0;
        for (var i = 0; i + 1 < bytes; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sum += sample * sample;
        }

        return (int)Math.Sqrt(sum / (double)samples);
    }

    private void DuckRemotePlayback(int micRms)
    {
        if (_speaker == null) return;

        var target = !_speakerOn ? 0f : (micRms > EchoRms ? 0.08f : 0.9f);
        try { _speaker.Volume = target; } catch { /* device does not support volume */ }
    }

    public void AddRemoteAudio(byte[] pcm)
    {
        if (_disposed || pcm.Length == 0) return;
        lock (_audioLock)
        {
            if (_remoteAudio == null) return;
            try { _remoteAudio.AddSamples(pcm, 0, pcm.Length); } catch { }
        }
    }

    public void SetMuted(bool muted) => _muted = muted;

    public void SetSpeakerOn(bool on)
    {
        _speakerOn = on;
        if (_speaker == null) return;
        try { _speaker.Volume = on ? 0.9f : 0f; } catch { }
    }

    public void SetVideoEnabled(bool enabled)
    {
        if (_disposed || enabled == _videoEnabled) return;

        _videoEnabled = enabled;
        if (enabled)
        {
            if (_videoTask == null || _videoTask.IsCompleted)
            {
                StartVideo();
            }
        }
        else
        {
            StopVideo();
        }
    }

    private void StopVideo()
    {
        try { _videoCts?.Cancel(); } catch { }
        try { _videoTask?.Wait(600); } catch { }

        _videoTask = null;

        try { _videoCts?.Dispose(); } catch { }
        _videoCts = null;

        try
        {
            _camera?.Release();
            _camera?.Dispose();
        }
        catch { }
        _camera = null;

        VideoStatus = "Video off";
    }

    private void StartVideo()
    {
        if (_disposed || _videoTask is { IsCompleted: false }) return;

        try
        {
            _camera = OpenCamera();
            if (_camera == null)
            {
                HasCamera = false;
                VideoStatus = "No camera found";
                return;
            }

            HasCamera = true;
            VideoStatus = "Camera on";
        }
        catch (Exception ex)
        {
            HasCamera = false;
            VideoStatus = $"Camera unavailable: {ex.Message}";
            return;
        }

        _videoCts = new CancellationTokenSource();
        var token = _videoCts.Token;
        _videoTask = Task.Run(() => VideoLoop(token));
    }

    private static VideoCapture? OpenCamera()
    {
        VideoCapture? capture = new VideoCapture(0);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        }

        if (!capture.IsOpened())
        {
            capture.Dispose();
            return null;
        }

        capture.Set(VideoCaptureProperties.FrameWidth, 640);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        capture.Set(VideoCaptureProperties.Fps, 15);
        return capture;
    }

    private void VideoLoop(CancellationToken token)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / VideoFps);
        using var frame = new Mat();

        while (!token.IsCancellationRequested && !_disposed)
        {
            var started = Stopwatch.GetTimestamp();

            if (!_videoEnabled || _camera == null)
            {
                break;
            }

            try
            {
                if (!_camera.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(150);
                    continue;
                }

                using var resized = new Mat();
                var height = (int)Math.Round(frame.Height * (VideoWidth / (double)frame.Width));
                Cv2.Resize(frame, resized, new Size(VideoWidth, Math.Max(2, height)));

                Cv2.ImEncode(".jpg", resized, out var jpeg,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));

                LocalVideoFrame?.Invoke(jpeg);
                _ = SendSafeAsync("video", Convert.ToBase64String(jpeg));
            }
            catch
            {
                // Drop this frame; a transient camera error must not kill the call.
                Thread.Sleep(150);
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var remaining = frameInterval - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                Thread.Sleep(remaining);
            }
        }
    }

    private async Task SendSafeAsync(string kind, string payload)
    {
        try
        {
            await _send(_targetUserId, kind, payload);
        }
        catch
        {
            // A dropped media frame is not fatal.
        }
    }

    public void FeedRemoteVideo(byte[] jpeg)
    {
        if (_disposed) return;
        RemoteVideoFrame?.Invoke(jpeg);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _videoEnabled = false;
        StopVideo();
        _disposed = true;

        try
        {
            if (_mic != null)
            {
                _mic.DataAvailable -= OnMicData;
                _mic.StopRecording();
                _mic.Dispose();
            }
        }
        catch { }
        _mic = null;

        try
        {
            _speaker?.Stop();
            _speaker?.Dispose();
        }
        catch { }
        _speaker = null;

        lock (_audioLock)
        {
            _remoteAudio = null;
        }
    }
}
