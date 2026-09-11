using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// AVAudioEngine input tap converted to 44100 Hz mono float. The
/// hardware runs at its native format (typically 48 kHz); an
/// AVAudioConverter brings each tap buffer to the pipeline format.
/// </summary>
public sealed class IosMicrophoneSource : IMicrophoneSource
{
    private AVAudioEngine? _engine;
    private AVAudioConverter? _converter;
    private AVAudioFormat? _target;

    public event Action<float[]>? ChunkAvailable;

    public bool IsRunning => _engine?.Running ?? false;

    public async Task<bool> StartAsync()
    {
        if (IsRunning)
            return true;
        // An engine an interruption (a call, Siri) stopped under us: start clean
        // rather than leave its tap and its converter behind.
        if (_engine != null)
            Stop();

        // Asked before the session is touched: a refused microphone must leave
        // the audio session as it was, or the next song would play into a
        // recording session — and the tuner now asks by itself on every launch.
        // Through MAUI's own permissions: AVAudioSession.RequestRecordPermission
        // is obsolete from iOS 17 (AVAudioApplication took it over).
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            return false;

        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Record);
        session.SetActive(true);
        AudioSessions.Changed();

        _engine = new AVAudioEngine();
        var input = _engine.InputNode;
        var native = input.GetBusOutputFormat(0);
        _target = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32,
                                    IMicrophoneSource.SampleRate, 1, false);
        _converter = new AVAudioConverter(native, _target);

        input.InstallTapOnBus(0, 4096, native, (buffer, _) =>
        {
            var converter = _converter;
            var target = _target;
            if (converter is null || target is null)
                return;

            uint capacity = (uint)(buffer.FrameLength *
                IMicrophoneSource.SampleRate / native.SampleRate) + 64;
            using var output = new AVAudioPcmBuffer(target, capacity);
            bool consumed = false;
            converter.ConvertToBuffer(output, out NSError? error,
                (uint _, out AVAudioConverterInputStatus status) =>
                {
                    if (consumed)
                    {
                        status = AVAudioConverterInputStatus.NoDataNow;
                        return null!;
                    }
                    consumed = true;
                    status = AVAudioConverterInputStatus.HaveData;
                    return buffer;
                });
            if (error != null || output.FrameLength == 0)
                return;

            var chunk = new float[output.FrameLength];
            // FloatChannelData points to an array of per-channel pointers.
            IntPtr channel = Marshal.ReadIntPtr(output.FloatChannelData);
            Marshal.Copy(channel, chunk, 0, chunk.Length);
            ChunkAvailable?.Invoke(chunk);
        });

        _engine.Prepare();
        if (_engine.StartAndReturnError(out _))
            return true;
        Stop();                                                  // no input to be had: back to the playback session
        return false;
    }

    public void Stop()
    {
        if (_engine == null)
            return;
        _engine.InputNode.RemoveTapOnBus(0);
        _engine.Stop();
        _engine.Dispose();
        _engine = null;
        _converter?.Dispose();
        _converter = null;
        // Back to the playback state at once, not when the next sound needs it:
        // that later change is what pauses a video already playing.
        AudioSessions.ForPlayback();
    }

    public void Dispose() => Stop();
}
