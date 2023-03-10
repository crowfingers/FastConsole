using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace FastConsole;

public static class FConsole {
    // Interaction with Windows API to write to the console
    [StructLayout(LayoutKind.Sequential)]
    struct Coord {
        public short x, y;
        public Coord(short x, short y) {
            this.x = x; this.y = y;
        }
    };

    [StructLayout(LayoutKind.Explicit)]
    struct CharInfo {
        [FieldOffset(0)] public ushort Char;
        [FieldOffset(2)] public short Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rectangle {
        public short left, top, right, bottom;
        public Rectangle(short left, short top, short right, short bottom) {
            this.left = left; this.top = top; this.right = right; this.bottom = bottom;
        }
    }

    static SafeFileHandle outputHandle = new();

    // Output buffer
    static CharInfo[] buffer = Array.Empty<CharInfo>();
    static short width, height, left, top;
    static short right => (short)(left + width - 1);
    static short bottom => (short)(top + height - 1);
    public static int WindowWidth => width;
    public static int WindowHeight => height;

    // Initialization
    [STAThread]
    public static void Initialize(string title, ConsoleColor foreground = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black) {
        Console.OutputEncoding = System.Text.Encoding.Unicode;
        Console.Title = title;
        Maximize();
        Console.CursorVisible = false;

        GetOutputHandle();

        width = (short)(Console.WindowWidth);
        height = (short)(Console.WindowHeight);
        left = 0;
        top = 0;

        buffer = new CharInfo[width * height];

        ForegroundColor = foreground;
        BackgroundColor = background;
        Clear(ForegroundColor, BackgroundColor);
    }
    static void Maximize() {
        [DllImport("user32.dll")]
        static extern bool ShowWindow(System.IntPtr hWnd, int cmdShow);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        static extern IntPtr GetConsoleWindow();

        IntPtr window = GetConsoleWindow();
        ShowWindow(window, 3); // maximize

        if (OperatingSystem.IsWindows())
            Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
    }
    static void GetOutputHandle() {
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern SafeFileHandle CreateFile(
            string fileName,
            [MarshalAs(UnmanagedType.U4)] uint fileAccess,
            [MarshalAs(UnmanagedType.U4)] uint fileShare,
            IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] int flags,
            IntPtr template);

        outputHandle = CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (outputHandle.IsInvalid) throw new Exception("outputHandle is invalid!");
    }

    // Character attribute utilities
    static short Colorset(ConsoleColor foreground, ConsoleColor background)
        => (short)(foreground + ((short)background << 4));
    const short overlineBit = 0x0400;
    static short Gridset(bool overline, bool leftline, bool rightline) {
        const short leftlineBit = 0x0800;
        const short rightlineBit = 0x1000;

        return (short)(
            (overline ? overlineBit : 0) + (leftline ? leftlineBit : 0) + (rightline ? rightlineBit : 0)
        );
    }

    // Public output interface
    static ConsoleColor _ForegroundColor, _BackgroundColor;
    public static ConsoleColor ForegroundColor {
        get => _ForegroundColor;
        set {
            _ForegroundColor = value;
            Console.ForegroundColor = (System.ConsoleColor)value;
        }
    }
    public static ConsoleColor BackgroundColor {
        get => _BackgroundColor;
        set {
            _BackgroundColor = value;
            Console.BackgroundColor = (System.ConsoleColor)value;
        }
    }

    public static void SetChar(short x, short y, PixelValue value) {
        SetChar(x, y, value.character, value.foreground, value.background);
    }
    public static void SetChar((int x, int y) coords, char c, ConsoleColor foreground, ConsoleColor background, bool overline = false, bool leftline = false, bool rightline = false, bool underline = false) {
        SetChar((short)coords.x, (short)coords.y, c, foreground, background, overline, leftline, rightline, underline);
    }
    public static void SetChar(int x, int y, char c, ConsoleColor foreground, ConsoleColor background, bool overline = false, bool leftline = false, bool rightline = false, bool underline = false) {
        SetChar((short)x, (short)y, c, foreground, background, overline, leftline, rightline, underline);
    }
    public static void SetChar(short x, short y, char c, ConsoleColor foreground, ConsoleColor background, bool overline = false, bool leftline = false, bool rightline = false, bool underline = false) {
        short address = (short)(width * y + x);
        short colorset = Colorset(foreground, background);
        short gridset = Gridset(overline, leftline, rightline);

        if (address < 0 || address >= buffer.Length) return; //throw new Exception("Can't write to address (" + x + "," + y + ").");
        buffer[address].Char = c;
        buffer[address].Attributes = (short)(colorset + gridset);

        if (underline) {
            if (y == height - 1) return;
            address = (short)(width * (y + 1) + x);
            buffer[address].Attributes |= overlineBit;
        }
    }

    public static void FillBuffer(char c, ConsoleColor foreground, ConsoleColor background) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i].Attributes = Colorset(foreground, background);
            buffer[i].Char = c;
        }
    }
    public static void Clear(ConsoleColor foreground = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black) {
        FillBuffer(' ', foreground, background);
    }

    // Drawing the buffer to the screen
    public static void DrawBuffer() {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteConsoleOutputW(
          SafeFileHandle hConsoleOutput,
          CharInfo[] lpBuffer,
          Coord dwBufferSize,
          Coord dwBufferCoord,
          ref Rectangle lpWriteRegion);

        Rectangle rect = new(left, top, right, bottom);
        WriteConsoleOutputW(outputHandle, buffer, new Coord(width, height), new Coord(0, 0), ref rect);
    }

    // Reading from the buffer
    public static (char character, ConsoleColor foreground, ConsoleColor background) ReadChar((int x, int y) coords) {
        short address = (short)(width * coords.y + coords.x);
        char character = (char)buffer[address].Char;
        short attributes = buffer[address].Attributes;
        attributes &= 0x0FF;
        ConsoleColor foreground = (ConsoleColor)(attributes & 0x0F);
        ConsoleColor background = (ConsoleColor)(attributes >> 4);
        return (character, foreground, background);
    }
}

public readonly record struct PixelValue {
    public readonly ConsoleColor foreground, background;
    public readonly char character;

    public override string ToString() => foreground.ToString() + character + background.ToString();

    public enum Density { background, sparse, medium, dense }
    static char DensityChar(Density d) =>
        d == Density.background ? ' ' :
        d == Density.sparse ? '░' :
        d == Density.medium ? '▒' :
        d == Density.dense ? '▓' :
        '?';
    public PixelValue(ConsoleColor foreground, ConsoleColor background, char character) {
        this.foreground = foreground;
        this.background = background;
        this.character = character;
    }
    public PixelValue(ConsoleColor foreground, ConsoleColor background, Density density)
        : this(foreground, background, DensityChar(density)) { }
    public PixelValue(ConsoleColor color) : this(color, color, DensityChar(Density.background)) { }
}