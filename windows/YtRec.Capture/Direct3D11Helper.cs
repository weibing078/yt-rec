using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using WinDirect3D = Windows.Graphics.DirectX.Direct3D11;

namespace YtRec.Capture;

/// <summary>Creates the IDirect3DDevice that WGC's frame pool needs, from a Vortice D3D11 device.
/// Mirrors the canonical robmikh Direct3D11Helper pattern.</summary>
internal static class Direct3D11Helper
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static (ID3D11Device d3d, WinDirect3D.IDirect3DDevice winrt) CreateDevice()
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out var device).CheckError();

        using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
        CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr graphicsDevicePtr);
        try
        {
            var winrtDevice = MarshalInterface<WinDirect3D.IDirect3DDevice>.FromAbi(graphicsDevicePtr);
            return (device!, winrtDevice);
        }
        finally
        {
            Marshal.Release(graphicsDevicePtr);
        }
    }
}
