using Oxce.Engine.Audio;
using Oxce.Platform.Sdl;
using Oxce.Rendering;

if (args.Length == 1 && string.Equals(args[0], "--sdl-audio-smoke", StringComparison.Ordinal))
{
    const int sampleRate = 22_050;
    const int toneFrames = sampleRate / 4;
    var samples = new short[toneFrames];
    for (var frame = 0; frame < samples.Length; frame++)
    {
        samples[frame] = (short)Math.Round(
            Math.Sin(2 * Math.PI * 440 * frame / sampleRate) * short.MaxValue / 8,
            MidpointRounding.AwayFromZero);
    }

    using var mixer = new ManagedAudioMixer(sampleRate);
    using var playback = mixer.Play(
        new PcmAudioClip(samples, sampleRate, channels: 1),
        new AudioPlaybackOptions(AudioBus.UserInterface));
    if (!SdlAudioPlaybackDevice.TryOpen(mixer, out var audioDevice, out var error))
    {
        Console.Error.WriteLine($"SDL3 audio smoke could not open a playback device: {error}");
        Environment.ExitCode = 1;
        return;
    }

    using (audioDevice)
    {
        Thread.Sleep(TimeSpan.FromMilliseconds(500));
        if (audioDevice.HasCallbackFailure)
        {
            Console.Error.WriteLine("SDL3 audio callback reported a mixing or stream failure.");
            Environment.ExitCode = 1;
            return;
        }
    }

    Console.WriteLine("SDL3 managed-mixer audio stream completed.");
    return;
}

if (args.Length == 1 && string.Equals(args[0], "--sdl-smoke", StringComparison.Ordinal))
{
    const int width = 160;
    const int height = 100;
    var surface = new IndexedSurface(width, height);
    for (var y = 0; y < height; ++y)
    {
        var row = surface.GetRow(y);
        for (var x = 0; x < width; ++x)
        {
            row[x] = (byte)((x + y) & 0xff);
        }
    }

    SdlIndexedFramePresenter.ShowFrame(
        surface,
        IndexedPalette.CreateGrayscale(),
        "OXCE .NET SDL3 event-loop smoke",
        scale: 4,
        duration: TimeSpan.FromSeconds(2));
    Console.WriteLine("SDL3 indexed-window event loop completed.");
    return;
}

Console.WriteLine("OXCE .NET compatibility port");
Console.WriteLine("Use --sdl-smoke to run the indexed-window SDL3 event-loop smoke test.");
Console.WriteLine("Use --sdl-audio-smoke to run the SDL3 managed-mixer playback smoke test.");
