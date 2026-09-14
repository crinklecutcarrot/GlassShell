using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace GlassShell;

internal sealed record WifiNetwork(string Ssid, int Signal, bool Secure, bool Connected, bool Saved);
internal sealed record BluetoothItem(string Name, bool Connected, bool Paired);

internal sealed class ConnectivityService : IDisposable
{
    public event Action? Changed;
    public IReadOnlyList<WifiNetwork> Networks { get; private set; } = Array.Empty<WifiNetwork>();
    public IReadOnlyList<BluetoothItem> BluetoothDevices { get; private set; } = Array.Empty<BluetoothItem>();
    public bool WifiOn { get; private set; } = true;
    public bool BluetoothOn { get; private set; } = true;
    public bool WifiBusy { get; private set; }
    public bool BluetoothBusy { get; private set; }
    public string WifiMessage { get; private set; } = "";
    public string BluetoothMessage { get; private set; } = "";
    int wifiRefresh, bluetoothRefresh;

    public async Task RefreshAll() { await Task.WhenAll(RefreshWifi(), RefreshBluetooth()); }

    public async Task RefreshWifi()
    {
        int request = Interlocked.Increment(ref wifiRefresh); WifiBusy = true; WifiMessage = "Scanning…"; Notify();
        if (Storage.OverrideRoot != null) { Networks = new[] { new WifiNetwork("Test Network", 92, true, true, true), new WifiNetwork("Studio Guest", 68, true, false, false) }; WifiOn = true; WifiBusy = false; WifiMessage = ""; Notify(); return; }
        try
        {
            string interfaces = await Netsh("wlan", "show", "interfaces");
            string profiles = await Netsh("wlan", "show", "profiles");
            string visible = await Netsh("wlan", "show", "networks", "mode=bssid");
            if (request != wifiRefresh) return;
            bool connected = Regex.IsMatch(interfaces, @"(?im)^\s*State\s*:\s*connected\s*$");
            string connectedSsid = connected ? Match(interfaces, @"(?im)^\s*SSID\s*:\s*(.+)$") : "";
            var saved = Regex.Matches(profiles, @"(?im)^\s*All User Profile\s*:\s*(.+)$").Select(x => x.Groups[1].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var found = new List<WifiNetwork>();
            foreach (Match block in Regex.Matches(visible + "\nSSID 999 :", @"(?ims)^SSID\s+\d+\s*:\s*(.*?)\r?\n(.*?)(?=^SSID\s+\d+\s*:)"))
            {
                string ssid = block.Groups[1].Value.Trim(); if (ssid.Length == 0) continue;
                string details = block.Groups[2].Value;
                int signal = Regex.Matches(details, @"(?im)^\s*Signal\s*:\s*(\d+)%").Select(x => int.Parse(x.Groups[1].Value)).DefaultIfEmpty(0).Max();
                string auth = Match(details, @"(?im)^\s*Authentication\s*:\s*(.+)$");
                found.Add(new(ssid, signal, !auth.Contains("Open", StringComparison.OrdinalIgnoreCase), ssid.Equals(connectedSsid, StringComparison.OrdinalIgnoreCase), saved.Contains(ssid)));
            }
            Networks = found.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Signal).ToArray();
            WifiOn = !interfaces.Contains("wireless interface on the system", StringComparison.OrdinalIgnoreCase) && !interfaces.Contains("radio is off", StringComparison.OrdinalIgnoreCase);
            WifiMessage = Networks.Count == 0 && WifiOn ? "No networks found" : "";
        }
        catch (Exception e) { WifiMessage = "Couldn’t scan Wi‑Fi. Open Windows settings to manage it."; Storage.Log("Wi-Fi refresh: " + e); }
        finally { if (request == wifiRefresh) { WifiBusy = false; Notify(); } }
    }

    public async Task RefreshBluetooth()
    {
        int request = Interlocked.Increment(ref bluetoothRefresh); BluetoothBusy = true; BluetoothMessage = "Looking for devices…"; Notify();
        if (Storage.OverrideRoot != null) { BluetoothDevices = new[] { new BluetoothItem("Test Headphones", true, true), new BluetoothItem("Test Controller", false, true) }; BluetoothOn = true; BluetoothBusy = false; BluetoothMessage = ""; Notify(); return; }
        try
        {
            var radios = await Radio.GetRadiosAsync(); var radio = radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth); BluetoothOn = radio?.State != RadioState.Off;
            if (!BluetoothOn) BluetoothDevices = Array.Empty<BluetoothItem>();
            else
            {
                var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()); var devices = new List<BluetoothItem>();
                foreach (var info in infos.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                {
                    using var device = await BluetoothDevice.FromIdAsync(info.Id);
                    devices.Add(new(info.Name, device?.ConnectionStatus == BluetoothConnectionStatus.Connected, info.Pairing.IsPaired));
                }
                BluetoothDevices = devices.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Connected).First()).OrderByDescending(x => x.Connected).ThenByDescending(x => x.Paired).ThenBy(x => x.Name).ToArray();
            }
            BluetoothMessage = BluetoothDevices.Count == 0 && BluetoothOn ? "No paired devices found" : "";
        }
        catch (Exception e) { BluetoothMessage = "Couldn’t read Bluetooth devices. Open Windows settings to manage them."; Storage.Log("Bluetooth refresh: " + e); }
        finally { if (request == bluetoothRefresh) { BluetoothBusy = false; Notify(); } }
    }

    public async Task ToggleRadio(RadioKind kind)
    {
        try
        {
            var access = await Radio.RequestAccessAsync(); if (access != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows denied radio access");
            var radios = await Radio.GetRadiosAsync(); var radio = radios.FirstOrDefault(x => x.Kind == kind); if (radio == null) throw new InvalidOperationException("Radio not found");
            var result = await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On); if (result != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows denied the change");
            if (kind == RadioKind.WiFi) await RefreshWifi(); else await RefreshBluetooth();
        }
        catch (Exception e) { Storage.Log("Radio toggle: " + e); Ui.Open(kind == RadioKind.WiFi ? "ms-settings:network-wifi" : "ms-settings:bluetooth"); }
    }

    public async Task ConnectWifi(string ssid)
    {
        var network = Networks.FirstOrDefault(x => x.Ssid == ssid);
        if (network?.Saved != true) { WifiMessage = "Open Windows settings once to enter this network’s password."; Notify(); Ui.Open("ms-settings:network-wifi"); return; }
        try { WifiMessage = "Connecting…"; Notify(); await Netsh("wlan", "connect", "name=" + ssid); await Task.Delay(900); await RefreshWifi(); }
        catch (Exception e) { WifiMessage = "Windows couldn’t connect to that network."; Storage.Log("Wi-Fi connect: " + e); Notify(); }
    }
    public async Task DisconnectWifi() { try { WifiMessage = "Disconnecting…"; Notify(); await Netsh("wlan", "disconnect"); await Task.Delay(500); await RefreshWifi(); } catch (Exception e) { WifiMessage = "Windows couldn’t disconnect from that network."; Storage.Log("Wi-Fi disconnect: " + e); Notify(); } }

    static string Match(string text, string pattern) { var match = Regex.Match(text, pattern); return match.Success ? match.Groups[1].Value.Trim() : ""; }
    static async Task<string> Netsh(params string[] arguments)
    {
        var start = new ProcessStartInfo("netsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start netsh");
        string output = await process.StandardOutput.ReadToEndAsync(); string error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Length > 0 ? error : output); return output;
    }
    void Notify() => Changed?.Invoke();
    public void Dispose() { Interlocked.Increment(ref wifiRefresh); Interlocked.Increment(ref bluetoothRefresh); }
}
