using System.Drawing;

namespace HLView.Net;

internal class LightmapBuilder
{
    private const int BytesPerPixel = 3;

    public int Width { get; }
    public int Height { get; private set; }
    public Rectangle FullbrightRectangle { get; }

    private byte[] _data;
    public byte[] Data => _data;

    private int _currentX;
    private int _currentY;
    private int _currentRowHeight;

    public LightmapBuilder(int initialWidth = 256, int initialHeight = 32)
    {
        _data = new byte[initialWidth * initialHeight * BytesPerPixel];
        Width = initialWidth;
        Height = initialHeight;
        _currentX = 0;
        _currentY = 0;
        _currentRowHeight = 2;

        // (0, 0) is fullbright
        FullbrightRectangle = Allocate(1, 1, new[] { byte.MaxValue, byte.MaxValue, byte.MaxValue }, 0);
    }

    public Vector2 GetLightmapUv(Rectangle rect, Vector2 originalUvs)
    {
        var x = (rect.X + 0.5f) / Width + (originalUvs.X * (rect.Width - 1)) / Width;
        var y = (rect.Y + 0.5f) / Height + (originalUvs.Y * (rect.Height - 1)) / Height;
        return new Vector2(x, y);
    }

    public Rectangle Allocate(int width, int height, byte[] data, int index)
    {
        if (_currentX + width > Width) NewRow();
        if (_currentY + height > Height) Expand();

        for (var i = 0; i < height; i++)
        {
            // data is in RGB format, but the bitmap wants BGR, so we need to reverse the order
            /*
            var bytes = new byte[width * 3];
            var st = width * i * 3 + index;
            for (var j = 0; j < width * 3; j += 3)
            {
                bytes[j + 0] = data[st + j + 2];
                bytes[j + 1] = data[st + j + 1];
                bytes[j + 2] = data[st + j + 0];
            }*/
            var start = (_currentY + i) * (Width * BytesPerPixel) + _currentX * BytesPerPixel;
            System.Array.Copy(data, width * i * BytesPerPixel + index, _data, start, width * BytesPerPixel);
        }

        var x = _currentX;
        var y = _currentY;

        _currentX += width + 2;
        _currentRowHeight = Math.Max(_currentRowHeight, height + 2);

        return new Rectangle(x, y, width, height);
    }

    private void NewRow()
    {
        _currentX = 0;
        _currentY += _currentRowHeight;
        _currentRowHeight = 2;
    }

    private void Expand()
    {
        Height *= 2;
        System.Array.Resize(ref _data, Width * Height * BytesPerPixel);
    }
}