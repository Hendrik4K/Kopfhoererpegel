using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Kopfhoererpegel
{
    // Minimal Windows Core Audio (MMDevice / WASAPI) interop: find the AirPods render endpoints, read their volume
    // and record what is sent to them (loopback). Only used from the meter's own (MTA) thread.
    static class CoreAudio
    {
        public const int DEVICE_STATE_ACTIVE = 1;
        public const int CLSCTX_ALL = 23;
        public const int AUDCLNT_SHAREMODE_SHARED = 0;
        public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const int AUDCLNT_BUFFERFLAGS_SILENT = 2;
        public const uint ENDPOINT_HARDWARE_SUPPORT_VOLUME = 1;

        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        public static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static readonly Guid SubTypeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        [StructLayout(LayoutKind.Sequential)]
        public struct PropertyKey { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Sequential)]
        public struct PropVariant { public ushort vt, r1, r2, r3; public IntPtr p1, p2; }

        // PKEY_Device_FriendlyName ("Kopfhörer (AirPods Pro 3 …)") and PKEY_DeviceInterface_FriendlyName ("AirPods Pro 3 …").
        public static readonly PropertyKey DeviceFriendlyName = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        public static readonly PropertyKey InterfaceFriendlyName = new PropertyKey { fmtid = new Guid("026e516e-b814-414b-83cd-856d6fef4822"), pid = 2 };

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceCollection
        {
            int GetCount(out int count);
            int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            int OpenPropertyStore(int access, out IPropertyStore store);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            int GetCount(out int count);
            int GetAt(int index, out PropertyKey key);
            int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            int GetBufferSize(out uint frames);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr handle);
            int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            int ReleaseBuffer(uint frames);
            int GetNextPacketSize(out uint frames);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float db, ref Guid context);
            int SetMasterVolumeLevelScalar(float level, ref Guid context);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            int GetChannelVolumeLevel(uint channel, out float db);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            int GetVolumeStepInfo(out uint step, out uint count);
            int VolumeStepUp(ref Guid context);
            int VolumeStepDown(ref Guid context);
            int QueryHardwareSupport(out uint mask);
            int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
        }

        public class Endpoint
        {
            public string Id, Name, InterfaceName;
            public IMMDevice Device;
        }

        public static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        }

        // All active playback endpoints (eRender = 0).
        public static List<Endpoint> ActiveRenderEndpoints(IMMDeviceEnumerator enumerator)
        {
            var list = new List<Endpoint>();
            IMMDeviceCollection devices;
            if (enumerator.EnumAudioEndpoints(0, DEVICE_STATE_ACTIVE, out devices) != 0) return list;
            int count;
            devices.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IMMDevice device;
                if (devices.Item(i, out device) != 0) continue;
                string id;
                device.GetId(out id);
                list.Add(new Endpoint
                {
                    Id = id, Device = device,
                    Name = ReadString(device, DeviceFriendlyName) ?? "",
                    InterfaceName = ReadString(device, InterfaceFriendlyName) ?? ""
                });
            }
            return list;
        }

        static string ReadString(IMMDevice device, PropertyKey key)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(0, out store) != 0) return null;
            try
            {
                PropVariant value;
                if (store.GetValue(ref key, out value) != 0) return null;
                var text = value.vt == 31 ? Marshal.PtrToStringUni(value.p1) : null;  // VT_LPWSTR
                PropVariantClear(ref value);
                return text;
            }
            finally { Marshal.ReleaseComObject(store); }
        }

        public static T Activate<T>(IMMDevice device, Guid iid) where T : class
        {
            object o;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o));
            return (T)o;
        }

        // Sample layout of the shared-mode mix format (almost always 32-bit float).
        public class Format
        {
            public int Channels, SampleRate, BitsPerSample, BlockAlign;
            public bool IsFloat;
        }

        public static Format ReadFormat(IntPtr p)
        {
            var f = new Format
            {
                Channels = Marshal.ReadInt16(p, 2),
                SampleRate = Marshal.ReadInt32(p, 4),
                BlockAlign = Marshal.ReadInt16(p, 12),
                BitsPerSample = Marshal.ReadInt16(p, 14)
            };
            var tag = (ushort)Marshal.ReadInt16(p, 0);
            if (tag == 3) f.IsFloat = true;  // WAVE_FORMAT_IEEE_FLOAT
            else if (tag == 0xFFFE)            // WAVE_FORMAT_EXTENSIBLE: sub format GUID at offset 24
            {
                var guid = new byte[16];
                Marshal.Copy(p + 24, guid, 0, 16);
                f.IsFloat = new Guid(guid) == SubTypeFloat;
            }
            return f;
        }
    }
}
