using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GlassShell;

internal sealed record CalendarEvent(string Id, string Title, string Description, string Location, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Color, string JoinUrl, string WebUrl);

internal sealed class CalendarService
{
    readonly HttpClient http = new();
    List<CalendarEvent> events = new();
    OAuthConfig? config; TokenState? token;
    public event Action? Changed;
    public IReadOnlyList<CalendarEvent> Events => events;
    public bool Configured => config != null;
    public bool Connected => token?.RefreshToken?.Length > 0;
    public bool Busy { get; private set; }
    public string Message { get; private set; } = "";
    public int AttentionCount => events.Count(e => !e.AllDay && e.Start <= DateTimeOffset.Now.AddHours(1) && e.End > DateTimeOffset.Now);
    string ProjectConfigPath => Path.Combine(Environment.CurrentDirectory, "config", "google-calendar-oauth.json");
    string ConfigPath => File.Exists(ProjectConfigPath) ? ProjectConfigPath : Path.Combine(Storage.Root, "google-calendar-oauth.json");
    string TokenPath => Path.Combine(Storage.Root, "google-calendar-token.bin");

    public CalendarService()
    {
        Directory.CreateDirectory(Storage.Root);
        string template = Path.Combine(Storage.Root, "google-calendar-oauth.template.json");
        if (!File.Exists(template)) File.WriteAllText(template, "{\n  \"clientId\": \"YOUR_GOOGLE_DESKTOP_CLIENT_ID\",\n  \"clientSecret\": \"YOUR_GOOGLE_DESKTOP_CLIENT_SECRET\"\n}\n");
        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath)); var root = document.RootElement;
                if (root.TryGetProperty("installed", out var installed)) root = installed;
                string clientId = root.TryGetProperty("clientId", out var camelId) ? camelId.GetString() ?? "" : root.TryGetProperty("client_id", out var snakeId) ? snakeId.GetString() ?? "" : "";
                string clientSecret = root.TryGetProperty("clientSecret", out var camelSecret) ? camelSecret.GetString() ?? "" : root.TryGetProperty("client_secret", out var snakeSecret) ? snakeSecret.GetString() ?? "" : "";
                config = new(clientId, clientSecret);
            }
            if (File.Exists(TokenPath)) token = JsonSerializer.Deserialize<TokenState>(Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(File.ReadAllText(TokenPath)))), JsonOptions);
        }
        catch (Exception e) { Message = "Calendar setup could not be read."; Storage.Log("Calendar load: " + e); }
    }

    public async Task Connect()
    {
        Load();
        if (config == null || string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId.Contains("PASTE_", StringComparison.OrdinalIgnoreCase) || config.ClientId.Contains("YOUR_GOOGLE_", StringComparison.OrdinalIgnoreCase)) { Message = "Add Google OAuth credentials to config/google-calendar-oauth.json first."; Changed?.Invoke(); return; }
        Busy = true; Message = "Waiting for Google sign-in…"; Changed?.Invoke();
        try
        {
            int port; var socket = new TcpListener(IPAddress.Loopback, 0); try { socket.Start(); port = ((IPEndPoint)socket.LocalEndpoint).Port; } finally { socket.Stop(); }
            string redirect = $"http://127.0.0.1:{port}/";
            string state = Guid.NewGuid().ToString("N");
            string auth = "https://accounts.google.com/o/oauth2/v2/auth?" + Form(new Dictionary<string, string> { ["client_id"] = config.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "https://www.googleapis.com/auth/calendar.readonly", ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state });
            using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start(); Ui.Open(auth);
            var context = await listener.GetContextAsync(); string code = context.Request.QueryString["code"] ?? ""; string returnedState = context.Request.QueryString["state"] ?? "";
            byte[] page = Encoding.UTF8.GetBytes("<html><body style='font-family:system-ui;background:#111;color:#fff;padding:40px'>Google Calendar connected. You can close this tab.</body></html>"); context.Response.ContentType = "text/html"; context.Response.ContentLength64 = page.Length; await context.Response.OutputStream.WriteAsync(page); context.Response.Close();
            if (code.Length == 0 || returnedState != state) throw new InvalidOperationException("Google did not return a valid authorization code.");
            using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret ?? "", ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect }));
            response.EnsureSuccessStatusCode(); var result = JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync(), JsonOptions) ?? throw new InvalidOperationException("Empty Google token response.");
            token = new(result.AccessToken ?? "", result.RefreshToken ?? token?.RefreshToken ?? "", DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn - 60)); SaveToken(); Message = "";
            await Refresh(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(14));
        }
        catch (Exception e) { Message = "Google Calendar connection failed."; Storage.Log("Calendar connect: " + e); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task Refresh(DateTime from, DateTime to)
    {
        if (!Connected) return;
        Busy = true; Changed?.Invoke();
        try
        {
            await EnsureToken();
            using var calendarsJson = await Get("https://www.googleapis.com/calendar/v3/users/me/calendarList?minAccessRole=reader&showHidden=false&maxResults=250");
            var calendars = calendarsJson.RootElement.GetProperty("items").EnumerateArray().Where(x => !x.TryGetProperty("hidden", out var hidden) || !hidden.GetBoolean()).Where(x => !x.TryGetProperty("selected", out var selected) || selected.GetBoolean()).ToArray();
            var gathered = new List<CalendarEvent>();
            foreach (var calendar in calendars)
            {
                string id = calendar.GetProperty("id").GetString() ?? ""; string color = calendar.TryGetProperty("backgroundColor", out var c) ? c.GetString() ?? "#7c5cff" : "#7c5cff";
                string url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(id)}/events?singleEvents=true&orderBy=startTime&maxResults=2500&timeMin={Uri.EscapeDataString(new DateTimeOffset(from).ToUniversalTime().ToString("O"))}&timeMax={Uri.EscapeDataString(new DateTimeOffset(to).ToUniversalTime().ToString("O"))}";
                using var eventsJson = await Get(url);
                foreach (var item in eventsJson.RootElement.GetProperty("items").EnumerateArray())
                {
                    if (item.TryGetProperty("status", out var status) && status.GetString() == "cancelled") continue;
                    bool allDay = item.GetProperty("start").TryGetProperty("date", out var startDate); DateTimeOffset start, end;
                    if (allDay) { start = new DateTimeOffset(DateTime.Parse(startDate.GetString()!).Date); end = new DateTimeOffset(DateTime.Parse(item.GetProperty("end").GetProperty("date").GetString()!).Date); }
                    else { start = DateTimeOffset.Parse(item.GetProperty("start").GetProperty("dateTime").GetString()!).ToLocalTime(); end = DateTimeOffset.Parse(item.GetProperty("end").GetProperty("dateTime").GetString()!).ToLocalTime(); }
                    string join = item.TryGetProperty("hangoutLink", out var hangout) ? hangout.GetString() ?? "" : ConferenceLink(item);
                    gathered.Add(new(item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(), Text(item, "summary", "Untitled event"), Text(item, "description"), Text(item, "location"), start, end, allDay, color, join, Text(item, "htmlLink")));
                }
            }
            events = gathered.OrderBy(x => x.Start).ToList(); Message = "";
        }
        catch (Exception e) { Message = "Couldn’t refresh Google Calendar."; Storage.Log("Calendar refresh: " + e); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    async Task EnsureToken()
    {
        if (token == null || config == null) throw new InvalidOperationException("Calendar is not connected.");
        if (token.ExpiresAt > DateTimeOffset.UtcNow) return;
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret ?? "", ["refresh_token"] = token.RefreshToken, ["grant_type"] = "refresh_token" }));
        response.EnsureSuccessStatusCode(); var result = JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync(), JsonOptions) ?? throw new InvalidOperationException("Empty refresh response.");
        token = token with { AccessToken = result.AccessToken ?? "", ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn - 60) }; SaveToken();
    }
    async Task<JsonDocument> Get(string url) { using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Authorization = new("Bearer", token!.AccessToken); using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode(); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()); }
    void SaveToken() { byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token, JsonOptions)); File.WriteAllText(TokenPath, Convert.ToBase64String(Protect(json))); }
    static string ConferenceLink(JsonElement item) { if (!item.TryGetProperty("conferenceData", out var data) || !data.TryGetProperty("entryPoints", out var points)) return ""; foreach (var point in points.EnumerateArray()) if (point.TryGetProperty("entryPointType", out var type) && type.GetString() == "video") return Text(point, "uri"); return ""; }
    static string Text(JsonElement item, string name, string fallback = "") => item.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;
    static string Form(Dictionary<string, string> values) => string.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    sealed record OAuthConfig([property: JsonPropertyName("clientId")] string ClientId, [property: JsonPropertyName("clientSecret")] string? ClientSecret);
    sealed record TokenState(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
    sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken, [property: JsonPropertyName("refresh_token")] string? RefreshToken, [property: JsonPropertyName("expires_in")] int ExpiresIn);

    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static byte[] Protect(byte[] value) => Crypt(value, true);
    static byte[] Unprotect(byte[] value) => Crypt(value, false);
    static byte[] Crypt(byte[] value, bool protect)
    {
        IntPtr inputMemory = Marshal.AllocHGlobal(value.Length); Marshal.Copy(value, 0, inputMemory, value.Length); var input = new Blob { Size = value.Length, Data = inputMemory };
        try { Blob output; bool ok = protect ? CryptProtectData(ref input, "GlassShell Google Calendar", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output); if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result; } finally { LocalFree(output.Data); } }
        finally { Marshal.FreeHGlobal(inputMemory); }
    }
}
