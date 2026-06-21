using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace YtRec.Capture;

/// <summary>Shared GPU→CPU readback for WGC frames: an <c>IDirect3DSurface</c> → its <c>ID3D11Texture2D</c>
/// → a CPU-readable staging texture → tightly-packed BGRA bytes (respecting RowPitch padding). Used by both
/// the probe recorder and the live A/V session.</summary>
internal static class FrameReadback
{
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private static readonly Guid ID3D11Texture2D_IID = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static ID3D11Texture2D SurfaceToTexture(IDirect3DSurface surface)
    {
        var unknown = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(unknown);
            var iid = ID3D11Texture2D_IID;
            var texPtr = access.GetInterface(ref iid);
            return new ID3D11Texture2D(texPtr); // owns the AddRef'd pointer GetInterface returned
        }
        finally { Marshal.Release(unknown); }
    }

    public static Texture2DDescription StagingDesc(int w, int h, Format fmt) => new()
    {
        Width = (uint)w,
        Height = (uint)h,
        MipLevels = 1,
        ArraySize = 1,
        Format = fmt,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CPUAccessFlags = CpuAccessFlags.Read,
        MiscFlags = ResourceOptionFlags.None,
    };

    /// <summary>Copy <paramref name="src"/> into <paramref name="staging"/>, map it, and pack the BGRA rows
    /// tightly into <paramref name="dest"/> (width*4 per row, dropping RowPitch padding).</summary>
    public static void CopyToBuffer(ID3D11DeviceContext context, ID3D11Texture2D staging, ID3D11Texture2D src,
        byte[] dest, int width, int height)
        => CopyCropToBuffer(context, staging, src, dest, 0, 0, width, height);

    /// <summary>Copy <paramref name="src"/> into <paramref name="staging"/>, map it, and pack the BGRA of just
    /// the sub-rectangle (cropX, cropY, cropW, cropH) tightly into <paramref name="dest"/> — used to crop a
    /// full-monitor frame down to the player window's rectangle.</summary>
    public static void CopyCropToBuffer(ID3D11DeviceContext context, ID3D11Texture2D staging, ID3D11Texture2D src,
        byte[] dest, int cropX, int cropY, int cropW, int cropH)
    {
        context.CopyResource(staging, src);
        var map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = cropW * 4;
            int srcStart = cropX * 4;
            for (int y = 0; y < cropH; y++)
                Marshal.Copy(map.DataPointer + (cropY + y) * (int)map.RowPitch + srcStart, dest, y * rowBytes, rowBytes);
        }
        finally { context.Unmap(staging, 0); }
    }
}
