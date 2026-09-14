using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlassShell;

internal sealed class YouTubeMusicBridge : IDisposable
{
    const string Secret = "glass-shell-ytm-v1";
    readonly HttpListener listener = new();
    CancellationTokenSource? cancellation;
    int togglePending;
    DateTimeOffset lastSeen;
    public bool Connected => DateTimeOffset.UtcNow - lastSeen < TimeSpan.FromSeconds(4);
    public event Action<bool>? LikeStateChanged;

    public void Start()
    {
        try
        {
            listener.Prefixes.Add("http://127.0.0.1:45971/"); listener.Start();
            cancellation = new CancellationTokenSource(); _ = Task.Run(() => Listen(cancellation.Token));
        }
        catch (Exception ex) { Storage.Log("YouTube Music bridge: " + ex.Message); }
    }

    public void ToggleLike() => Interlocked.Exchange(ref togglePending, 1);

    async Task Listen(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(token); }
            catch when (token.IsCancellationRequested || !listener.IsListening) { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    async Task Handle(HttpListenerContext context)
    {
        try
        {
            if (context.Request.Headers["X-GlassShell-Key"] != Secret) { context.Response.StatusCode = 403; return; }
            context.Response.ContentType = "application/json";
            if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/state")
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var state = JsonSerializer.Deserialize<State>(await reader.ReadToEndAsync());
                if (state != null) { lastSeen = DateTimeOffset.UtcNow; LikeStateChanged?.Invoke(state.liked); }
                await Write(context, "{\"ok\":true}");
            }
            else if (context.Request.Url?.AbsolutePath == "/command")
                await Write(context, Interlocked.Exchange(ref togglePending, 0) == 1 ? "{\"toggleLike\":true}" : "{\"toggleLike\":false}");
            else context.Response.StatusCode = 404;
        }
        catch (Exception ex) { Storage.Log("YouTube Music bridge request: " + ex.Message); }
        finally { try { context.Response.Close(); } catch { } }
    }

    static async Task Write(HttpListenerContext context, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json); context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    sealed class State { public bool liked { get; set; } }
    public void Dispose() { cancellation?.Cancel(); listener.Close(); cancellation?.Dispose(); }
}
