namespace Clock;

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal struct IconDir {
    public IconDir () { }
    // must be zero.
#pragma warning disable IDE0051
#pragma warning disable CS0414
    private readonly ushort zero = 0;
#pragma warning restore CS0414
#pragma warning restore IDE0051
    public ushort type;
    public ushort count;
}

internal struct IconDirEntry {
    public IconDirEntry () { }
    public byte width, height, colorCount;
    // must be zero.
#pragma warning disable IDE0051
#pragma warning disable CS0414
    private readonly byte zero = 0;
#pragma warning restore CS0414
#pragma warning restore IDE0051
    public ushort colorPlanes, bitsPerPixel;
    public uint byteCount, dataOffset;
}

internal class Clock {

#if DEBUG
    static Clock () {
        Debug.Assert(Marshal.SizeOf<IconDir>() == 3 * sizeof(ushort));
        Debug.Assert(Marshal.SizeOf<IconDirEntry>() == 4 * sizeof(byte) + 2 * sizeof(ushort) + 2 * sizeof(uint));
    }
#endif

    public static T StructFromStream<T> (Stream data) where T : struct {
        var bytes = new byte[Marshal.SizeOf<T>()];
        var count = data.Read(bytes, 0, bytes.Length);
        Debug.Assert(count == bytes.Length);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var t = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        handle.Free();
        return t;
    }

    private static void StructToStream<T> (T item, Stream target) where T : struct {
        var bytes = new byte[Marshal.SizeOf<T>()];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        Marshal.StructureToPtr(item, handle.AddrOfPinnedObject(), false);
        handle.Free();
        target.Write(bytes, 0, bytes.Length);
    }

    private static Graphics NearestNeighborGraphicsFromImage (Bitmap b) {
        var graphics = Graphics.FromImage(b);
        graphics.Clear(Color.Transparent);
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.SmoothingMode = SmoothingMode.None;
        return graphics;
    }

    private static byte[] BitmapToBytes (Bitmap source) {
        using MemoryStream data = new();
        source.Save(data, ImageFormat.Png);
        return data.ToArray();
    }

    private static Icon FromBitmap (Bitmap source) {
        Debug.Assert(source.Width <= byte.MaxValue && source.Height <= byte.MaxValue);
        var bytes = BitmapToBytes(source);
        using MemoryStream data = new();
        IconDir iconDirectory = new() { count = 1, type = 1 };
        StructToStream(iconDirectory, data);
        IconDirEntry directoryEntry = new() { bitsPerPixel = 32, colorCount = 0, width = (byte)source.Width, height = (byte)source.Height, dataOffset = 22, colorPlanes = 1, byteCount = (uint)bytes.Length };
        StructToStream(directoryEntry, data);
        data.Write(bytes, 0, bytes.Length);
        data.Position = 0;
        return new Icon(data);
    }

    private const string fontData = "iVBORw0KGgoAAAANSUhEUgAAAEYAAAAHCAYAAAC1KjtNAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAB4SURBVEhL7dXBDoAgDANQ8f//Gdellxk6CajhwDsxmi07SCzVHEIxPAZZT4/W3NmZr1MLZYvOZMAyYDQ0V/GBhmXASGYnz9uNPyX1abfuARmPS/hkT9WcDUUGLLtlPU8ZsAzUPXiTYRmoe0C2n5Lw+19ppZm6r9YL2IibkIeRAU8AAAAASUVORK5CYII=";
    private readonly Timer timer;
    private readonly Bitmap temporaryBitmap;
    private readonly Bitmap[] digits = new Bitmap[10];
    private readonly Rectangle firstDigit = new(0, 1, 7, 15);
    private readonly Rectangle secondDigit = new(9, 1, 7, 15);
    private readonly NotifyIcon hourMinuteIcon, secondIcon;
    private int currentSeconds = -1;
    private DateTime lastDate;

    [STAThread]
    private static void Main () {
        _ = new Clock();
        Application.Run();
    }

    private Clock () {
        hourMinuteIcon = CreatePlaceholder();
        hourMinuteIcon.ContextMenu = new(new MenuItem[] { new("a"), new("&Close", Stop) });
        secondIcon = CreatePlaceholder();

        temporaryBitmap = new(16, 16, PixelFormat.Format32bppArgb);

        var fontLocation = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "font.png");

        if (File.Exists(fontLocation)) {
            using (var font = new Bitmap(fontLocation))
                SeparateDigits(font);
        } else {
            using (var data = new MemoryStream(Convert.FromBase64String(fontData)))
            using (var font = new Bitmap(data))
                SeparateDigits(font);
        }

        timer = new() { Interval = 250, Enabled = true };
        timer.Tick += Tick_timer;
    }

    private void SeparateDigits (Bitmap font) {
        Rectangle target = new(0, 0, 7, 7);
        for (var i = 0; i < 10; i++) {
            digits[i] = new(7, 7, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(digits[i]);
            graphics.DrawImage(font, target, new(7 * i, 0, 7, 7), GraphicsUnit.Pixel);
        }
    }

    private void Stop (object sender, EventArgs args) {
        hourMinuteIcon.Visible = false;
        secondIcon.Visible = false;
        Application.Exit();
    }

    private static NotifyIcon CreatePlaceholder () => new() { Visible = true, Icon = new(SystemIcons.Asterisk, 16, 16) };

    private void Tick_timer (object sender, EventArgs e) {
        var now = DateTime.Now;
        if (now.Date > lastDate) {
            lastDate = now.Date;
            hourMinuteIcon.ContextMenu.MenuItems[0].Text = lastDate.ToLongDateString();
        }
        if (now.Second == currentSeconds)
            return;
        SwapIcon(secondIcon, ForSeconds(now.Second));
        if (now.Second == 0 || currentSeconds < 0) {
            using (var graphics = NearestNeighborGraphicsFromImage(temporaryBitmap)) {
                graphics.DrawImageUnscaled(digits[now.Hour / 10], 0, 0);
                graphics.DrawImageUnscaled(digits[now.Hour % 10], 8, 0);
                graphics.DrawImageUnscaled(digits[now.Minute / 10], 0, 9);
                graphics.DrawImageUnscaled(digits[now.Minute % 10], 8, 9);
            }
            SwapIcon(hourMinuteIcon, FromBitmap(temporaryBitmap));
        }
        currentSeconds = now.Second;
    }

    private Icon ForSeconds (int seconds) {
        using var graphics = NearestNeighborGraphicsFromImage(temporaryBitmap);
        graphics.DrawImage(digits[seconds / 10], firstDigit);
        graphics.DrawImage(digits[seconds % 10], secondDigit);
        return FromBitmap(temporaryBitmap);
    }

    private static void SwapIcon (NotifyIcon target, Icon newIcon) {
        target.Icon.Dispose();
        target.Icon = newIcon;
    }
}