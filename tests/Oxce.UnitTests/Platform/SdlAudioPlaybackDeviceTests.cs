using System.Runtime.InteropServices;
using Oxce.Engine.Audio;
using Oxce.Platform.Sdl;
using Xunit;

namespace Oxce.UnitTests.Platform;

public sealed class SdlAudioPlaybackDeviceTests
{
    [Fact]
    public void OpenConfiguresResumesAndDisposesDefaultPlaybackStream()
    {
        var api = new RecordingAudioApi();
        var source = new SequenceSampleSource(22_050);

        using (var device = SdlAudioPlaybackDevice.Open(source, 256, api))
        {
            Assert.Equal(22_050, device.SampleRate);
            Assert.Equal(2, device.Channels);
            Assert.Equal(256, device.BufferFrames);
            Assert.False(device.HasCallbackFailure);
            Assert.Equal(1, api.InitializeCount);
            Assert.Equal(1, api.OpenCount);
            Assert.Equal(1, api.ResumeCount);
            Assert.NotEqual(IntPtr.Zero, api.Callback);
            Assert.NotEqual(IntPtr.Zero, api.UserData);
            Assert.Equal(2, api.Specification.Channels);
            Assert.Equal(22_050, api.Specification.Frequency);
            Assert.Equal(
                BitConverter.IsLittleEndian ? 0x8010u : 0x9010u,
                api.Specification.Format);
        }

        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.QuitCount);
    }

    [Fact]
    public void CallbackStateFeedsAlignedChunksWithoutLosingRequestedBytes()
    {
        var api = new RecordingAudioApi();
        var source = new SequenceSampleSource(8_000);
        var state = new SdlAudioCallbackState(source, api, bufferFrames: 2);

        state.Provide(api.Stream, additionalBytes: 11);

        Assert.False(state.HasFailed);
        Assert.Equal([8, 4], api.PutLengths);
        Assert.Equal(new short[] { 1, 2, 3, 4, 5, 6 }, api.Samples);
        Assert.Equal(3, source.MixedFrames);
    }

    [Fact]
    public void CallbackStateSubstitutesSilenceWhenTheSourceFails()
    {
        var api = new RecordingAudioApi();
        var state = new SdlAudioCallbackState(new ThrowingSampleSource(), api, bufferFrames: 2);

        state.Provide(api.Stream, additionalBytes: 8);

        Assert.True(state.HasFailed);
        Assert.Equal(new short[4], api.Samples);
    }

    [Fact]
    public void OpenFailureReleasesInitializedAudioSubsystem()
    {
        var api = new RecordingAudioApi { OpenSucceeds = false };
        var source = new SequenceSampleSource(8_000);

        var exception = Assert.Throws<SdlException>(() => SdlAudioPlaybackDevice.Open(source, 2, api));

        Assert.Contains("SDL_OpenAudioDeviceStream", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, api.DestroyCount);
        Assert.Equal(1, api.QuitCount);
    }

    [Fact]
    public void TryOpenReportsUnavailableAudioWithoutThrowing()
    {
        var api = new RecordingAudioApi { InitializeSucceeds = false };

        var opened = SdlAudioPlaybackDevice.TryOpen(
            new SequenceSampleSource(8_000),
            out var device,
            out var error,
            bufferFrames: 2,
            api: api);

        Assert.False(opened);
        Assert.Null(device);
        Assert.Contains("SDL_InitSubSystem", error, StringComparison.Ordinal);
        Assert.Equal(0, api.QuitCount);
    }

    [Fact]
    public void ResumeFailureDestroysStreamBeforeQuittingAudioSubsystem()
    {
        var api = new RecordingAudioApi { ResumeSucceeds = false };
        var source = new SequenceSampleSource(8_000);

        Assert.Throws<SdlException>(() => SdlAudioPlaybackDevice.Open(source, 2, api));

        Assert.Equal(["destroy", "quit"], api.CleanupOperations);
    }

    [Fact]
    public void OpenRejectsNonStereoSourcesAndInvalidBufferSizesBeforeNativeCalls()
    {
        var api = new RecordingAudioApi();

        Assert.Throws<ArgumentException>(
            () => SdlAudioPlaybackDevice.Open(new MonoSampleSource(), 2, api));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SdlAudioPlaybackDevice.Open(new SequenceSampleSource(8_000), 0, api));
        Assert.Equal(0, api.InitializeCount);
    }

    private sealed class RecordingAudioApi : ISdlAudioApi
    {
        private readonly List<short> _samples = [];

        internal IntPtr Stream { get; } = new(123);

        internal bool OpenSucceeds { get; init; } = true;

        internal bool ResumeSucceeds { get; init; } = true;

        internal bool InitializeSucceeds { get; init; } = true;

        internal int InitializeCount { get; private set; }

        internal int OpenCount { get; private set; }

        internal int ResumeCount { get; private set; }

        internal int DestroyCount { get; private set; }

        internal int QuitCount { get; private set; }

        internal SdlAudioSpec Specification { get; private set; }

        internal IntPtr Callback { get; private set; }

        internal IntPtr UserData { get; private set; }

        internal List<int> PutLengths { get; } = [];

        internal IReadOnlyList<short> Samples => _samples;

        internal List<string> CleanupOperations { get; } = [];

        public bool InitializeAudio()
        {
            InitializeCount++;
            return InitializeSucceeds;
        }

        public IntPtr OpenDefaultPlaybackStream(SdlAudioSpec specification, IntPtr callback, IntPtr userData)
        {
            OpenCount++;
            Specification = specification;
            Callback = callback;
            UserData = userData;
            return OpenSucceeds ? Stream : IntPtr.Zero;
        }

        public bool PutStreamData(IntPtr stream, IntPtr buffer, int length)
        {
            Assert.Equal(Stream, stream);
            PutLengths.Add(length);
            var copied = new short[length / sizeof(short)];
            Marshal.Copy(buffer, copied, 0, copied.Length);
            _samples.AddRange(copied);
            return true;
        }

        public bool ResumeStreamDevice(IntPtr stream)
        {
            Assert.Equal(Stream, stream);
            ResumeCount++;
            return ResumeSucceeds;
        }

        public void DestroyStream(IntPtr stream)
        {
            Assert.Equal(Stream, stream);
            DestroyCount++;
            CleanupOperations.Add("destroy");
        }

        public void QuitAudio()
        {
            QuitCount++;
            CleanupOperations.Add("quit");
        }

        public string GetError() => "test failure";
    }

    private sealed class SequenceSampleSource(int sampleRate) : IAudioSampleSource
    {
        private short _nextSample = 1;

        public int SampleRate { get; } = sampleRate;

        public int Channels => 2;

        internal int MixedFrames { get; private set; }

        public void Mix(Span<short> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = _nextSample++;
            }

            MixedFrames += destination.Length / Channels;
        }
    }

    private sealed class ThrowingSampleSource : IAudioSampleSource
    {
        public int SampleRate => 8_000;

        public int Channels => 2;

        public void Mix(Span<short> destination) => throw new InvalidOperationException("test failure");
    }

    private sealed class MonoSampleSource : IAudioSampleSource
    {
        public int SampleRate => 8_000;

        public int Channels => 1;

        public void Mix(Span<short> destination) => destination.Clear();
    }
}
