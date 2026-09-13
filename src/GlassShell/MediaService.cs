using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace GlassShell;

internal sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool busy, disposed;
    private string artworkHash = "";
    private DateTimeOffset lastValidMedia = DateTimeOffset.MinValue;
    private TimeSpan observedPosition;
    private DateTimeOffset observedAt;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public bool Playing { get; private set; }
    public bool Paused { get; private set; }
    public bool Available { get; private set; }
    public bool Visible => Available && (Playing || Paused);
    public bool CanToggle { get; private set; }
    public bool CanNext { get; private set; }
    public bool CanPrevious { get; private set; }
    public bool CanSeek { get; private set; }
    public ImageSource? AlbumArt { get; private set; }
    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; }
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
    public TimeSpan Position => Duration > TimeSpan.Zero ? TimeSpan.FromTicks(Math.Clamp((observedPosition + (Playing ? DateTimeOffset.UtcNow - observedAt : TimeSpan.Zero)).Ticks, Start.Ticks, End.Ticks)) - Start : TimeSpan.Zero;
    public event Action? Changed;
    public async Task Initialize()
    {
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); await Refresh(); }
        catch (Exception ex) { Storage.Log("Media: " + ex.Message); }
    }
    public async Task Refresh()
    {
        if (busy || manager == null || disposed) return;
        busy = true;
        try
        {
            var selected = manager.GetCurrentSession();
            foreach (var item in manager.GetSessions())
                if (item.SourceAppUserModelId.Contains("chrome", StringComparison.OrdinalIgnoreCase)) { selected = item; break; }
            session = selected;
            if (session == null) { HoldOrClear(); return; }
            var properties = await session.TryGetMediaPropertiesAsync();
            var state = session.GetPlaybackInfo();
            bool playing = state.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool paused = state.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            // Chrome briefly reports stopped/empty metadata between tracks. Keep the
            // last complete presentation for a bounded grace period, including its art.
            if ((!playing && !paused) || string.IsNullOrWhiteSpace(properties.Title)) { HoldOrClear(); return; }
            var timeline = session.GetTimelineProperties();
            ImageSource? artwork = AlbumArt;
            string hash = artworkHash;
            // Chrome can publish a title before its thumbnail. Re-read the thumbnail
            // even when the title is unchanged; decode only when its bytes change.
            if (properties.Thumbnail != null)
            {
                try
                {
                    using var stream = await properties.Thumbnail.OpenReadAsync();
                    if (stream.Size > 0 && stream.Size <= 8 * 1024 * 1024)
                    {
                        using var reader = new DataReader(stream.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)stream.Size);
                        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                        hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                        if (hash != artworkHash)
                        {
                            using var memory = new MemoryStream(bytes);
                            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                            image.DecodePixelWidth = 180; image.StreamSource = memory; image.EndInit(); image.Freeze(); artwork = image;
                        }
                    }
                }
                catch (Exception ex) { Storage.Log("Album art: " + ex.Message); }
            }
            if (disposed) return;
            // Commit one coherent update after asynchronous thumbnail retrieval.
            Available = true; Playing = playing; Paused = paused;
            Title = properties.Title; Artist = properties.Artist; AlbumArt = artwork; artworkHash = hash;
            CanToggle = state.Controls.IsPlayPauseToggleEnabled; CanNext = state.Controls.IsNextEnabled;
            CanPrevious = state.Controls.IsPreviousEnabled; CanSeek = state.Controls.IsPlaybackPositionEnabled;
            Start = timeline.StartTime; End = timeline.EndTime; observedPosition = timeline.Position;
            observedAt = timeline.LastUpdatedTime > DateTimeOffset.UtcNow.AddHours(-12) && timeline.LastUpdatedTime <= DateTimeOffset.UtcNow ? timeline.LastUpdatedTime : DateTimeOffset.UtcNow;
            lastValidMedia = DateTimeOffset.UtcNow;
            if (!disposed) Changed?.Invoke();
        }
        catch (Exception ex) { Storage.Log("Media refresh: " + ex.Message); }
        finally { busy = false; }
    }
    private void HoldOrClear()
    {
        if (DateTimeOffset.UtcNow - lastValidMedia < TimeSpan.FromSeconds(3)) return;
        Available = Playing = Paused = CanToggle = CanNext = CanPrevious = CanSeek = false;
        Title = Artist = artworkHash = ""; AlbumArt = null; Start = End = observedPosition = TimeSpan.Zero;
        Changed?.Invoke();
    }
    internal void TestMediaGap() { if (Storage.OverrideRoot == null) throw new InvalidOperationException(); HoldOrClear(); }
    internal double? LastTestSeek { get; private set; }
    public async Task Control(string action)
    {
        if (session == null) return;
        try
        {
            bool success = action switch { "next" => await session.TrySkipNextAsync(), "previous" => await session.TrySkipPreviousAsync(), _ => await session.TryTogglePlayPauseAsync() };
            if (!success) Storage.Log("Player declined " + action); await Refresh();
        }
        catch (Exception ex) { Storage.Log("Media control: " + ex.Message); }
    }
    public async Task Seek(double fraction)
    {
        if (!CanSeek || Duration <= TimeSpan.Zero) return;
        if (Storage.OverrideRoot != null) { LastTestSeek = fraction; observedPosition = TimeSpan.FromTicks((long)(Duration.Ticks * fraction)); observedAt = DateTimeOffset.UtcNow; Changed?.Invoke(); return; }
        if (session == null) return;
        try
        {
            var target = Start + TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
            if (await session.TryChangePlaybackPositionAsync(target.Ticks)) { observedPosition = target; observedAt = DateTimeOffset.UtcNow; Changed?.Invoke(); }
            else Storage.Log("Player declined seek");
        }
        catch (Exception ex) { Storage.Log("Media seek: " + ex.Message); }
    }
    internal void SetTestState(bool playing, bool paused = false, ImageSource? artwork = null)
    {
        if (Storage.OverrideRoot == null) throw new InvalidOperationException("Test media requires isolated test mode");
        lastValidMedia = DateTimeOffset.UtcNow; Available = playing || paused; Playing = playing; Paused = paused;
        Title = Available ? "A test track" : ""; Artist = Available ? "Test artist" : ""; AlbumArt = artwork;
        Start = TimeSpan.Zero; End = TimeSpan.FromMinutes(4); observedPosition = TimeSpan.FromSeconds(50); observedAt = DateTimeOffset.UtcNow;
        CanToggle = CanNext = CanPrevious = CanSeek = Available; Changed?.Invoke();
    }
    public void Dispose() { disposed = true; session = null; manager = null; }
}
