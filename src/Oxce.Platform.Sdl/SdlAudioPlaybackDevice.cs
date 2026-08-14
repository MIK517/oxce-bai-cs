using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Oxce.Engine.Audio;

namespace Oxce.Platform.Sdl;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SdlAudioSpec
{
    internal SdlAudioSpec(uint format, int channels, int frequency)
    {
        Format = format;
        Channels = channels;
        Frequency = frequency;
    }

    internal readonly uint Format;

    internal readonly int Channels;

    internal readonly int Frequency;
}

internal interface ISdlAudioApi
{
    bool InitializeAudio();

    IntPtr OpenDefaultPlaybackStream(SdlAudioSpec specification, IntPtr callback, IntPtr userData);

    bool PutStreamData(IntPtr stream, IntPtr buffer, int length);

    bool ResumeStreamDevice(IntPtr stream);

    void DestroyStream(IntPtr stream);

    void QuitAudio();

    string GetError();
}

internal sealed class SdlNativeAudioApi : ISdlAudioApi
{
    internal static SdlNativeAudioApi Instance { get; } = new();

    private const uint DefaultPlaybackDevice = uint.MaxValue;
    private const uint AudioSigned16LittleEndian = 0x8010;
    private const uint AudioSigned16BigEndian = 0x9010;

    private SdlNativeAudioApi()
    {
    }

    internal static uint NativeSigned16Format =>
        BitConverter.IsLittleEndian ? AudioSigned16LittleEndian : AudioSigned16BigEndian;

    public bool InitializeAudio() => SdlNative.SDL_InitSubSystem(SdlNative.InitAudio);

    public unsafe IntPtr OpenDefaultPlaybackStream(
        SdlAudioSpec specification,
        IntPtr callback,
        IntPtr userData) =>
        SdlNative.SDL_OpenAudioDeviceStream(
            DefaultPlaybackDevice,
            &specification,
            callback,
            userData);

    public bool PutStreamData(IntPtr stream, IntPtr buffer, int length) =>
        SdlNative.SDL_PutAudioStreamData(stream, buffer, length);

    public bool ResumeStreamDevice(IntPtr stream) => SdlNative.SDL_ResumeAudioStreamDevice(stream);

    public void DestroyStream(IntPtr stream) => SdlNative.SDL_DestroyAudioStream(stream);

    public void QuitAudio() => SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);

    public string GetError() => SdlNative.GetError();
}

internal sealed class SdlAudioCallbackState
{
    private const int BytesPerSample = sizeof(short);

    private readonly IAudioSampleSource _source;
    private readonly ISdlAudioApi _api;
    private readonly short[] _buffer;
    private int _failed;

    internal SdlAudioCallbackState(IAudioSampleSource source, ISdlAudioApi api, int bufferFrames)
    {
        _source = source;
        _api = api;
        _buffer = new short[checked(bufferFrames * source.Channels)];
    }

    internal bool HasFailed => Volatile.Read(ref _failed) != 0;

    internal int Channels => _source.Channels;

    internal void MarkFailed() => Interlocked.Exchange(ref _failed, 1);

    internal unsafe void Provide(IntPtr stream, int additionalBytes)
    {
        if (additionalBytes <= 0)
        {
            return;
        }

        var frameBytes = checked(_source.Channels * BytesPerSample);
        var remainingBytes = additionalBytes;
        while (remainingBytes > 0)
        {
            var requestedBytes = Math.Min(remainingBytes, checked(_buffer.Length * BytesPerSample));
            var frameCount = checked((requestedBytes + frameBytes - 1) / frameBytes);
            var sampleCount = checked(frameCount * _source.Channels);
            var samples = _buffer.AsSpan(0, sampleCount);
            try
            {
                _source.Mix(samples);
            }
            catch (Exception)
            {
                samples.Clear();
                MarkFailed();
            }

            fixed (short* samplesPointer = samples)
            {
                if (!_api.PutStreamData(
                        stream,
                        (IntPtr)samplesPointer,
                        checked(sampleCount * BytesPerSample)))
                {
                    MarkFailed();
                    return;
                }
            }

            remainingBytes -= requestedBytes;
        }
    }
}

internal static class SdlAudioCallback
{
    internal static unsafe IntPtr Pointer =>
        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, void>)&Invoke;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void Invoke(IntPtr userData, IntPtr stream, int additionalAmount, int totalAmount)
    {
        _ = totalAmount;
        if (userData == IntPtr.Zero)
        {
            return;
        }

        SdlAudioCallbackState? state = null;
        try
        {
            var callbackHandle = GCHandle.FromIntPtr(userData);
            state = callbackHandle.Target as SdlAudioCallbackState;
            state?.Provide(stream, additionalAmount);
        }
        catch (Exception)
        {
            // Exceptions cannot cross an unmanaged callback boundary. The owner can
            // observe this flag on its main thread and disable audio deliberately.
            state?.MarkFailed();
        }
    }
}

public sealed class SdlAudioPlaybackDevice : IDisposable
{
    public const int DefaultBufferFrames = 1_024;

    private readonly object _sync = new();
    private readonly ISdlAudioApi _api;
    private readonly SdlAudioCallbackState _callbackState;
    private GCHandle _callbackHandle;
    private IntPtr _stream;
    private bool _audioInitialized;

    private SdlAudioPlaybackDevice(
        ISdlAudioApi api,
        SdlAudioCallbackState callbackState,
        GCHandle callbackHandle,
        IntPtr stream,
        int sampleRate,
        int bufferFrames)
    {
        _api = api;
        _callbackState = callbackState;
        _callbackHandle = callbackHandle;
        _stream = stream;
        _audioInitialized = true;
        SampleRate = sampleRate;
        BufferFrames = bufferFrames;
    }

    public int SampleRate { get; }

    public int Channels => _callbackState.Channels;

    public int BufferFrames { get; }

    public bool HasCallbackFailure => _callbackState.HasFailed;

    public static SdlAudioPlaybackDevice Open(
        IAudioSampleSource source,
        int bufferFrames = DefaultBufferFrames) =>
        Open(source, bufferFrames, SdlNativeAudioApi.Instance);

    public static bool TryOpen(
        IAudioSampleSource source,
        [NotNullWhen(true)] out SdlAudioPlaybackDevice? device,
        [NotNullWhen(false)] out string? error,
        int bufferFrames = DefaultBufferFrames) =>
        TryOpen(source, out device, out error, bufferFrames, SdlNativeAudioApi.Instance);

    internal static bool TryOpen(
        IAudioSampleSource source,
        [NotNullWhen(true)] out SdlAudioPlaybackDevice? device,
        [NotNullWhen(false)] out string? error,
        int bufferFrames,
        ISdlAudioApi api)
    {
        try
        {
            device = Open(source, bufferFrames, api);
            error = null;
            return true;
        }
        catch (Exception exception) when (IsAvailabilityFailure(exception))
        {
            device = null;
            error = exception.Message;
            return false;
        }
    }

    internal static SdlAudioPlaybackDevice Open(
        IAudioSampleSource source,
        int bufferFrames,
        ISdlAudioApi api)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(api);
        if (source.SampleRate is < 1 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (source.Channels != 2)
        {
            throw new ArgumentException("SDL playback requires an interleaved stereo sample source.", nameof(source));
        }

        if (bufferFrames is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferFrames));
        }

        if (!api.InitializeAudio())
        {
            throw new SdlException("SDL_InitSubSystem(SDL_INIT_AUDIO)", api.GetError());
        }

        var callbackState = new SdlAudioCallbackState(source, api, bufferFrames);
        var callbackHandle = GCHandle.Alloc(callbackState);
        IntPtr stream = IntPtr.Zero;
        try
        {
            var specification = new SdlAudioSpec(
                SdlNativeAudioApi.NativeSigned16Format,
                source.Channels,
                source.SampleRate);
            stream = api.OpenDefaultPlaybackStream(
                specification,
                SdlAudioCallback.Pointer,
                GCHandle.ToIntPtr(callbackHandle));
            if (stream == IntPtr.Zero)
            {
                throw new SdlException("SDL_OpenAudioDeviceStream", api.GetError());
            }

            if (!api.ResumeStreamDevice(stream))
            {
                throw new SdlException("SDL_ResumeAudioStreamDevice", api.GetError());
            }

            return new SdlAudioPlaybackDevice(
                api,
                callbackState,
                callbackHandle,
                stream,
                source.SampleRate,
                bufferFrames);
        }
        catch
        {
            if (stream != IntPtr.Zero)
            {
                api.DestroyStream(stream);
            }

            callbackHandle.Free();
            api.QuitAudio();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_stream != IntPtr.Zero)
            {
                _api.DestroyStream(_stream);
                _stream = IntPtr.Zero;
            }

            if (_callbackHandle.IsAllocated)
            {
                _callbackHandle.Free();
            }

            if (_audioInitialized)
            {
                _api.QuitAudio();
                _audioInitialized = false;
            }
        }
    }

    private static bool IsAvailabilityFailure(Exception exception) =>
        exception is SdlException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}
