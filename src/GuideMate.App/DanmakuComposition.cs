using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;

namespace GuideMate.App;

// Only glyph-sized surfaces are uploaded. DWM animates retained visuals without a WPF frame callback.
internal sealed class DanmakuComposition : IDisposable
{
    private readonly ID3D11Device _graphics;
    private readonly ID2D1Device _drawing;
    private readonly ID2D1DeviceContext _context;
    private readonly IDCompositionDevice _device;
    private readonly IDCompositionVisual _root;
    private readonly IDCompositionEffectGroup _opacity;
    private IDCompositionTarget? _target;

    public DanmakuComposition()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
            out var graphics, out _, out var immediate).CheckError();
        _graphics = graphics!;
        immediate!.Dispose();
        using var dxgi = _graphics.QueryInterface<IDXGIDevice>();
        _drawing = D2D1.D2D1CreateDevice(dxgi);
        _context = _drawing.CreateDeviceContext(DeviceContextOptions.None);
        _device = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        _root = _device.CreateVisual();
        _opacity = _device.CreateEffectGroup();
        _root.SetEffect(_opacity).CheckError();
    }

    public void Attach(nint hwnd)
    {
        _device.CreateTargetForHwnd(hwnd, true, out _target).CheckError();
        _target.SetRoot(_root).CheckError();
    }

    public void Configure(double width, double height, double dpi, double opacity)
    {
        _root.SetTransform(Matrix3x2.CreateScale((float)dpi)).CheckError();
        _root.SetClip(new RawRectF(0, 0, (float)width, (float)height)).CheckError();
        _opacity.SetOpacity((float)opacity).CheckError();
    }

    public Sprite Add(BitmapSource image, double dpi)
    {
        // Long comments at high DPI exceed a single GPU texture. Tile without truncating the text.
        const int tileWidth = 2048;
        if (image.PixelWidth <= tileWidth) return AddTile(image, dpi);
        var visual = _device.CreateVisual();
        var opacity = _device.CreateEffectGroup();
        var tiles = new List<Sprite>();
        var sprite = new Sprite(visual, null, opacity, tiles);
        try
        {
            visual.SetTransform(Matrix3x2.CreateScale((float)(1 / dpi))).CheckError();
            visual.SetEffect(opacity).CheckError();
            for (var x = 0; x < image.PixelWidth; x += tileWidth)
            {
                var tile = AddTile(new CroppedBitmap(image,
                    new Int32Rect(x, 0, Math.Min(tileWidth, image.PixelWidth - x), image.PixelHeight)), 1);
                tiles.Add(tile);
                _root.RemoveVisual(tile.Visual).CheckError();
                tile.Visual.SetOffsetX(x).CheckError();
                visual.AddVisual(tile.Visual, true, null).CheckError();
            }
            _root.AddVisual(visual, true, null).CheckError();
            return sprite;
        }
        catch { sprite.Dispose(); throw; }
    }

    private Sprite AddTile(BitmapSource image, double dpi)
    {
        _device.CreateSurface((uint)image.PixelWidth, (uint)image.PixelHeight,
            Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out var surface).CheckError();
        var visual = _device.CreateVisual();
        var opacity = _device.CreateEffectGroup();
        var sprite = new Sprite(visual, surface, opacity);
        try
        {
            var stride = image.PixelWidth * 4;
            var pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels(pixels, stride, 0);
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var bitmap = _context.CreateBitmap(new SizeI(image.PixelWidth, image.PixelHeight),
                    pin.AddrOfPinnedObject(), (uint)stride,
                    new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied)));
                using var update = surface.BeginDraw<IDXGISurface>(null, out var offset);
                try
                {
                    using var target = _context.CreateBitmapFromDxgiSurface(update,
                        new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
                            96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
                    _context.Target = target;
                    _context.Transform = Matrix3x2.CreateTranslation(offset.X, offset.Y);
                    _context.BeginDraw();
                    // BeginDraw may return a shared atlas: clear only this sprite's update rectangle.
                    _context.PushAxisAlignedClip(new RawRectF(0, 0, image.PixelWidth, image.PixelHeight), AntialiasMode.Aliased);
                    _context.Clear(new Color4(0, 0, 0, 0));
                    _context.DrawBitmap(bitmap, 1, BitmapInterpolationMode.NearestNeighbor);
                    _context.PopAxisAlignedClip();
                    _context.EndDraw().CheckError();
                }
                finally
                {
                    _context.Target = null;
                    surface.EndDraw().CheckError();
                }
            }
            finally { pin.Free(); }
            visual.SetContent(surface).CheckError();
            visual.SetTransform(Matrix3x2.CreateScale((float)(1 / dpi))).CheckError();
            visual.SetEffect(opacity).CheckError();
            _root.AddVisual(visual, true, null).CheckError();
            return sprite;
        }
        catch { sprite.Dispose(); throw; } // Release a partially created sprite; do not substitute another renderer.
    }

    public void Position(Sprite sprite, double x, double y, double velocity, double remaining, bool paused)
    {
        sprite.Visual.SetOffsetY((float)y).CheckError();
        if (paused)
        {
            sprite.Visual.SetOffsetX((float)x).CheckError();
            sprite.Opacity.SetOpacity(1).CheckError();
            return;
        }
        using var movement = _device.CreateAnimation();
        movement.AddCubic(0, (float)x, (float)velocity, 0, 0).CheckError();
        movement.End(remaining, (float)(x + velocity * remaining)).CheckError();
        sprite.Visual.SetOffsetX(movement).CheckError();
        using var expiry = _device.CreateAnimation();
        expiry.AddCubic(0, 1, 0, 0, 0).CheckError();
        expiry.End(remaining, 0).CheckError();
        sprite.Opacity.SetOpacity(expiry).CheckError();
    }

    public void Remove(Sprite sprite)
    {
        _root.RemoveVisual(sprite.Visual).CheckError();
        sprite.Dispose();
    }

    public void Commit() => _device.Commit().CheckError();

    public void Dispose()
    {
        _target?.Dispose();
        _root.Dispose(); _opacity.Dispose(); _device.Dispose();
        _context.Dispose(); _drawing.Dispose(); _graphics.Dispose();
    }

    internal sealed record Sprite(IDCompositionVisual Visual, IDCompositionSurface? Surface,
        IDCompositionEffectGroup Opacity, List<Sprite>? Tiles = null) : IDisposable
    {
        public void Dispose()
        {
            Visual.Dispose(); Surface?.Dispose(); Opacity.Dispose();
            if (Tiles != null) foreach (var tile in Tiles) tile.Dispose();
        }
    }
}
