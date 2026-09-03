using Oxce.Core.Graphics;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Rendering;
using System.Globalization;

namespace Oxce.Engine;

/// <summary>
/// Minimal indexed campaign view used to operate the headless foundation without
/// making SDL or persistence part of gameplay.
/// </summary>
public sealed class CampaignOverviewClient : IIndexedLoopClient
{
    public const int Width = 320;
    public const int Height = 200;
    public const uint AdvanceMinuteKey = 0x20;
    private const int GlobeLeft = 16;
    private const int GlobeTop = 32;
    private const int GlobeWidth = 288;
    private const int GlobeHeight = 120;
    private readonly ICampaignQuery _queries;
    private readonly ICampaignCommandTarget _commands;

    public CampaignOverviewClient(ICampaignQuery queries, ICampaignCommandTarget commands)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(commands);
        _queries = queries;
        _commands = commands;
        Frame = new IndexedSurface(Width, Height);
        Palette = CreatePalette();
        Redraw();
    }

    public IndexedSurface Frame { get; }

    public IndexedPalette Palette { get; }

    public long PresentationRevision { get; private set; }

    public bool ExitRequested => false;

    public CampaignOverview Overview => _queries.QueryOverview();

    public void HandleInput(in GameInputEvent input)
    {
        if (input.Kind == GameInputEventKind.KeyPressed && !input.IsRepeat && input.KeyCode == AdvanceMinuteKey)
        {
            _commands.Execute(new AdvanceCampaignTime(12));
            Redraw();
            return;
        }

        if (input.Kind != GameInputEventKind.PointerPressed || input.PointerButton != 1 ||
            input.X < GlobeLeft || input.X >= GlobeLeft + GlobeWidth ||
            input.Y < GlobeTop || input.Y >= GlobeTop + GlobeHeight)
        {
            return;
        }

        var overview = _queries.QueryOverview();
        if (overview.Bases.Count == 0 || overview.Bases[0].IsPlaced) return;
        var longitude = (input.X - GlobeLeft) / GlobeWidth * (2 * Math.PI);
        var latitude = (0.5 - ((input.Y - GlobeTop) / GlobeHeight)) * Math.PI;
        _commands.Execute(new PlaceStartingBase(0, "First Base", longitude, latitude));
        Redraw();
    }

    public void Tick(TimeSpan fixedInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fixedInterval, TimeSpan.Zero);
    }

    private void Redraw()
    {
        var overview = _queries.QueryOverview();
        Frame.Clear(1);
        DrawDate(overview.Time);
        var baseState = overview.Bases.Count == 0 ? null : overview.Bases[0];
        if (baseState is null || !baseState.IsPlaced) DrawGlobePlacement();
        else DrawBase(baseState);
        DrawMetrics(overview);
        PresentationRevision = checked(PresentationRevision + 1);
    }

    private void DrawDate(CampaignTime time)
    {
        DrawNumber(time.Year, 16, 8, 11);
        DrawNumber(time.Month, 55, 8, 11, minimumDigits: 2);
        DrawNumber(time.Day, 75, 8, 11, minimumDigits: 2);
        DrawNumber(time.Hour, 112, 8, 14, minimumDigits: 2);
        DrawNumber(time.Minute, 132, 8, 14, minimumDigits: 2);
    }

    private void DrawGlobePlacement()
    {
        Frame.FillRectangle(GlobeLeft, GlobeTop, GlobeWidth, GlobeHeight, 3);
        Frame.FillCircle(160, 92, 58, 5);
        Frame.DrawLine(102, 92, 218, 92, 8);
        Frame.DrawLine(160, 34, 160, 150, 8);
        Frame.DrawLine(110, 63, 210, 63, 7);
        Frame.DrawLine(110, 121, 210, 121, 7);
    }

    private void DrawBase(CampaignBaseOverview baseState)
    {
        const int cell = 18;
        const int left = 24;
        const int top = 36;
        for (var coordinate = 0; coordinate <= CampaignState.BaseGridSize; coordinate++)
        {
            Frame.DrawLine(left, top + coordinate * cell, left + CampaignState.BaseGridSize * cell,
                top + coordinate * cell, 7);
            Frame.DrawLine(left + coordinate * cell, top, left + coordinate * cell,
                top + CampaignState.BaseGridSize * cell, 7);
        }
        foreach (var facility in baseState.Facilities)
        {
            Frame.FillRectangle(left + facility.X * cell + 2, top + facility.Y * cell + 2,
                facility.SizeX * cell - 3, facility.SizeY * cell - 3,
                facility.BuildTime == 0 ? (byte)12 : (byte)9);
        }
        DrawNumber(baseState.CraftCount, 160, 48, 13);
        DrawNumber(baseState.SoldierCount, 160, 72, 14);
        DrawNumber(baseState.ItemTypeCount, 160, 96, 10);
        DrawNumber(baseState.Scientists, 220, 48, 15);
        DrawNumber(baseState.Engineers, 220, 72, 15);
    }

    private void DrawMetrics(CampaignOverview overview)
    {
        DrawNumber(overview.CountryCount, 16, 170, 10);
        DrawNumber(overview.RegionCount, 64, 170, 12);
        DrawNumber(overview.DaysPassed, 112, 170, 14);
        DrawNumber(overview.Funds, 176, 170, overview.Funds < 0 ? (byte)9 : (byte)11);
    }

    private void DrawNumber(long value, int x, int y, byte color, int minimumDigits = 1)
    {
        Span<char> text = stackalloc char[20];
        if (!value.TryFormat(text, out var written, provider: CultureInfo.InvariantCulture)) return;
        var padding = Math.Max(0, minimumDigits - written);
        for (var index = 0; index < padding; index++) DrawDigit(0, x + index * 6, y, color);
        for (var index = 0; index < written; index++)
        {
            if (text[index] is >= '0' and <= '9')
                DrawDigit(text[index] - '0', x + (padding + index) * 6, y, color);
        }
    }

    private void DrawDigit(int digit, int x, int y, byte color)
    {
        ReadOnlySpan<byte> masks = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
        var mask = masks[digit];
        if ((mask & 0x01) != 0) Frame.FillRectangle(x + 1, y, 3, 1, color);
        if ((mask & 0x02) != 0) Frame.FillRectangle(x + 4, y + 1, 1, 3, color);
        if ((mask & 0x04) != 0) Frame.FillRectangle(x + 4, y + 5, 1, 3, color);
        if ((mask & 0x08) != 0) Frame.FillRectangle(x + 1, y + 8, 3, 1, color);
        if ((mask & 0x10) != 0) Frame.FillRectangle(x, y + 5, 1, 3, color);
        if ((mask & 0x20) != 0) Frame.FillRectangle(x, y + 1, 1, 3, color);
        if ((mask & 0x40) != 0) Frame.FillRectangle(x + 1, y + 4, 3, 1, color);
    }

    private static IndexedPalette CreatePalette()
    {
        var colors = new Rgba32[IndexedPalette.ColorCount];
        colors[1] = new Rgba32(8, 14, 28);
        colors[3] = new Rgba32(16, 38, 64);
        colors[5] = new Rgba32(20, 74, 112);
        colors[7] = new Rgba32(50, 110, 132);
        colors[8] = new Rgba32(90, 170, 188);
        colors[9] = new Rgba32(216, 80, 64);
        colors[10] = new Rgba32(208, 172, 60);
        colors[11] = new Rgba32(104, 212, 140);
        colors[12] = new Rgba32(88, 156, 228);
        colors[13] = new Rgba32(192, 128, 232);
        colors[14] = new Rgba32(236, 236, 220);
        colors[15] = new Rgba32(96, 208, 216);
        return new IndexedPalette(colors);
    }
}
