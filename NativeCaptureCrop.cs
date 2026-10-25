using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace OffWorkWidget.WinUI;

// Owned by one capture worker. The returned surface is borrowed until the next Copy or Dispose.
internal sealed unsafe class NativeCaptureCrop : IDisposable
{
    // ID3D11Device / ID3D11DeviceContext / ID3D11Texture2D slots from the Windows SDK d3d11.h.
    const int CreateTexture2DSlot = 5, ImmediateContextSlot = 40, CopyRegionSlot = 46, TextureDescriptionSlot = 10;
    const int MapSlot = 14, UnmapSlot = 15, CopyResourceSlot = 47;
    IntPtr device, context, texture, staging;
    IDirect3DSurface? surface;
    int width, height;
    uint format;
    bool disposed;

    internal NativeCaptureCrop(IDirect3DDevice captureDevice)
    {
        try
        {
            device = GetNative(captureDevice.As<IDirect3DDxgiInterfaceAccess>(),
                new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"));
            IntPtr immediate = IntPtr.Zero;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(device, ImmediateContextSlot))(device, &immediate);
            context = immediate;
            if (context == IntPtr.Zero) throw new InvalidOperationException("Capture device has no immediate context.");
            // WGC and asynchronous surface readback may also use this device's immediate context.
            // D3D11 defaults to an unprotected context; retain protection for the owned capture device.
            var multithreadIid = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(context, ref multithreadIid, out IntPtr multithread));
            try { ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(multithread, 5))(multithread, 1); }
            finally { Marshal.Release(multithread); }
        }
        catch { Dispose(); throw; }
    }

    internal IDirect3DSurface Copy(IDirect3DSurface source, int left, int top, int cropWidth, int cropHeight)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (left < 0 || top < 0 || cropWidth <= 0 || cropHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(cropWidth));
        IntPtr input = GetNative(source.As<IDirect3DDxgiInterfaceAccess>(),
            new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"));
        try
        {
            TextureDescription original;
            ((delegate* unmanaged[Stdcall]<IntPtr, TextureDescription*, void>)Slot(input, TextureDescriptionSlot))(input, &original);
            if ((long)left + cropWidth > original.Width || (long)top + cropHeight > original.Height || original.SampleCount != 1)
                throw new ArgumentOutOfRangeException(nameof(cropWidth), "Crop must fit the source texture.");
            EnsureTexture(cropWidth, cropHeight, original.Format);
            var box = new Box
            {
                Left = (uint)left, Top = (uint)top, Front = 0,
                Right = checked((uint)(left + cropWidth)), Bottom = checked((uint)(top + cropHeight)), Back = 1
            };
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(context, CopyRegionSlot))
                (context, texture, 0, 0, 0, 0, input, 0, &box);
            return surface!;
        }
        finally { Marshal.Release(input); }
    }

    // The capture worker may block on GPU completion here; the UI thread never maps this texture.
    internal void ReadPixels(byte[] destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        if (texture == IntPtr.Zero) throw new InvalidOperationException("Copy a capture region before reading pixels.");
        int rowBytes = checked(width * 4);
        if (destination.Length < checked(rowBytes * height))
            throw new ArgumentException("Destination does not fit the capture region.", nameof(destination));
        if (format != 87 && format != 91) // DXGI_FORMAT_B8G8R8A8_UNORM / UNORM_SRGB.
            throw new InvalidOperationException("Expected a BGRA8 capture texture.");
        if (staging == IntPtr.Zero)
        {
            var description = new TextureDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = format, SampleCount = 1, Usage = 3, CpuAccessFlags = 0x20000
            };
            IntPtr created = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, TextureDescription*, IntPtr, IntPtr*, int>)Slot(device, CreateTexture2DSlot))
                (device, &description, IntPtr.Zero, &created));
            staging = created;
        }
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(context, CopyResourceSlot))(context, staging, texture);
        MappedResource mapped;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, MappedResource*, int>)Slot(context, MapSlot))
            (context, staging, 0, 1, 0, &mapped));
        try
        {
            if (mapped.Data == IntPtr.Zero || mapped.RowPitch < rowBytes || (long)mapped.RowPitch * height > int.MaxValue)
                throw new InvalidOperationException("Capture staging texture has an invalid row layout.");
            if (mapped.RowPitch == rowBytes)
                Marshal.Copy(mapped.Data, destination, 0, checked(rowBytes * height));
            else
                for (int row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(mapped.Data, checked((int)((long)row * mapped.RowPitch))), destination, row * rowBytes, rowBytes);
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(context, UnmapSlot))(context, staging, 0);
        }
    }
    void EnsureTexture(int cropWidth, int cropHeight, uint pixelFormat)
    {
        if (surface != null && width == cropWidth && height == cropHeight && format == pixelFormat) return;
        ReleaseTexture();
        var description = new TextureDescription
        {
            Width = (uint)cropWidth, Height = (uint)cropHeight, MipLevels = 1, ArraySize = 1,
            Format = pixelFormat, SampleCount = 1, BindFlags = 0x28 // Shader resource and render target; GPU-only default usage.
        };
        IntPtr created = IntPtr.Zero, dxgi = IntPtr.Zero, wrapped = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, TextureDescription*, IntPtr, IntPtr*, int>)Slot(device, CreateTexture2DSlot))
                (device, &description, IntPtr.Zero, &created));
            var iid = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(created, ref iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi, out wrapped));
            surface = MarshalInterface<IDirect3DSurface>.FromAbi(wrapped);
            texture = created; created = IntPtr.Zero;
            width = cropWidth; height = cropHeight; format = pixelFormat;
        }
        finally
        {
            if (wrapped != IntPtr.Zero) Marshal.Release(wrapped);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (created != IntPtr.Zero) Marshal.Release(created);
        }
    }

    // Copies directly from the locked CPU bitmap; avoids a second WinRT buffer and array marshaling.
    internal static void CopyPixels(SoftwareBitmap bitmap, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(destination);
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            throw new ArgumentException("Expected a BGRA8 capture bitmap.", nameof(bitmap));
        int rows = bitmap.PixelHeight, rowBytes = checked(bitmap.PixelWidth * 4);
        if (destination.Length < checked(rows * rowBytes))
            throw new ArgumentException("Destination does not fit the capture bitmap.", nameof(destination));
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var plane = buffer.GetPlaneDescription(0);
        if (plane.Width < bitmap.PixelWidth || plane.Height < rows || Math.Abs((long)plane.Stride) < rowBytes)
            throw new InvalidOperationException("Capture bitmap has an invalid row layout.");
        using var reference = buffer.CreateReference();
        var access = reference.As<IMemoryBufferByteAccess>();
        try
        {
            Marshal.ThrowExceptionForHR(access.GetBuffer(out IntPtr memory, out uint capacity));
            long lastRow = (long)plane.StartIndex + (long)(rows - 1) * plane.Stride;
            long firstByte = Math.Min(plane.StartIndex, lastRow), lastByte = Math.Max(plane.StartIndex, lastRow) + rowBytes;
            if (memory == IntPtr.Zero || firstByte < 0 || lastByte > capacity || lastByte > int.MaxValue)
                throw new InvalidOperationException("Capture bitmap row data exceeds its buffer.");
            if (plane.Stride == rowBytes)
                Marshal.Copy(IntPtr.Add(memory, plane.StartIndex), destination, 0, checked(rows * rowBytes));
            else
                for (int row = 0; row < rows; row++)
                    Marshal.Copy(IntPtr.Add(memory, checked((int)((long)plane.StartIndex + (long)row * plane.Stride))),
                        destination, row * rowBytes, rowBytes);
        }
        finally { if (Marshal.IsComObject(access)) Marshal.ReleaseComObject(access); }
    }
    static IntPtr GetNative(IDirect3DDxgiInterfaceAccess access, Guid iid)
    {
        try { Marshal.ThrowExceptionForHR(access.GetInterface(ref iid, out IntPtr result)); return result; }
        finally { if (Marshal.IsComObject(access)) Marshal.ReleaseComObject(access); }
    }

    static IntPtr Slot(IntPtr instance, int slot) => ((IntPtr*)*(IntPtr*)instance)[slot];

    void ReleaseTexture()
    {
        surface?.Dispose(); surface = null;
        if (staging != IntPtr.Zero) { Marshal.Release(staging); staging = IntPtr.Zero; }
        if (texture != IntPtr.Zero) { Marshal.Release(texture); texture = IntPtr.Zero; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseTexture();
        if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
        if (device != IntPtr.Zero) { Marshal.Release(device); device = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TextureDescription
    {
        internal uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality;
        internal uint Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MappedResource { internal IntPtr Data; internal uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)]
    struct Box { internal uint Left, Top, Front, Right, Bottom, Back; }

    [ComImport, Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(ref Guid iid, out IntPtr result);
    }
    [ComImport, Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMemoryBufferByteAccess
    {
        [PreserveSig] int GetBuffer(out IntPtr memory, out uint capacity);
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);
}