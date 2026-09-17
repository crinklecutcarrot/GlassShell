using System;
using System.Runtime.InteropServices;

namespace GlassShell;

internal sealed class AudioService
{
    public bool Muted { get; private set; }
    public float Volume { get; private set; } = 1;
    DateTime nextRead;

    public bool RefreshIfDue()
    {
        if (DateTime.UtcNow < nextRead) return false;
        nextRead = DateTime.UtcNow.AddMilliseconds(500);
        bool oldMuted = Muted; float oldVolume = Volume;
        object? enumeratorObject = null, deviceObject = null, endpointObject = null;
        try
        {
            enumeratorObject = new MMDeviceEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
            deviceObject = device;
            Guid endpointId = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref endpointId, 23, IntPtr.Zero, out endpointObject));
            var endpoint = (IAudioEndpointVolume)endpointObject;
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out bool muted));
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out float volume));
            Muted = muted; Volume = volume;
        }
        catch (Exception e) { Storage.Log("Audio status: " + e.Message); }
        finally
        {
            foreach (object? item in new[] { endpointObject, deviceObject, enumeratorObject })
                if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
        return oldMuted != Muted || Math.Abs(oldVolume - Volume) > .001f;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    sealed class MMDeviceEnumerator { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
